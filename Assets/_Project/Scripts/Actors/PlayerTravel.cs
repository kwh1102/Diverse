using UnityEngine;

namespace Diverse
{
    public partial class Player
    {
        TravelType activeTravel;
        Vector2 travelLanding;
        float travelDuration;
        bool enteredWater, bounceFollowup;
        float bounceAgainUntil;
        bool showTravelPose;
        const float TravelEntryTime = 0.16f;
        const float TravelExitTime = 0.24f;
        float travelExitUntil, waterEnteredAt;
        TravelType exitingTravel;

        float TravelElapsed => travelDuration - dashT;
        float ExitProgress => 1f - Mathf.Clamp01((travelExitUntil - Time.time) / TravelExitTime);
        bool SubmergedTravel => showTravelPose && (activeTravel == TravelType.Burrow || activeTravel == TravelType.Swim && enteredWater);
        bool BuriedInTerrain => activeTravel == TravelType.Burrow && WorldStreamer.I.IsSolidObstacle(Pos);
        float TravelDepth => SubmergedTravel
            ? Mathf.Clamp01((activeTravel == TravelType.Swim ? Time.time - waterEnteredAt : TravelElapsed) / TravelEntryTime)
            : exitingTravel == TravelType.Burrow || exitingTravel == TravelType.Swim && enteredWater ? 1f - ExitProgress : 0f;

        public bool UsesTravelGauge => TravelTypes.UsesGauge(MovementType);
        float GaugeUnits => Mathf.Clamp(DashCharges + (DashCharges >= DashChargesMax ? 0 : DashRechargeProgress), 0, DashChargesMax);
        public float TravelGaugeFill => GaugeUnits / Mathf.Max(1, DashChargesMax);
        static float SecondsPerCharge(TravelType type) => type == TravelType.Swim ? 0.88f : 0.4f;

        float TravelStep(float dt)
        {
            if (!TravelTypes.UsesGauge(activeTravel)) return Mathf.Min(dt, dashT);
            float elapsed = travelDuration - dashT;
            if (elapsed >= (activeTravel == TravelType.Swim ? 0.20f : TravelEntryTime) && !Controls.Held(Act.Dash)) return 0;
            float seconds = SecondsPerCharge(activeTravel);
            float units = GaugeUnits;
            float step = Mathf.Min(dt, dashT, units * seconds);
            float remaining = Mathf.Max(0, units - step / seconds);
            DashCharges = Mathf.FloorToInt(remaining);
            dashRecharge = (remaining - DashCharges) * DashRechargeTime;
            return step;
        }

        Sprite TravelSprite()
        {

            int frame;
            if (dashT > 0 && showTravelPose)
            {
                float elapsed = travelDuration - dashT;
                bool entering = activeTravel == TravelType.Swim
                    ? !enteredWater || Time.time - waterEnteredAt < TravelEntryTime
                    : elapsed < (activeTravel == TravelType.Burrow ? TravelEntryTime : 0.08f);
                frame = entering ? 0 : 1 + Mathf.FloorToInt(elapsed * 10f) % 2;
            }
            else if (Time.time < travelExitUntil) frame = 3;
            else return null;
            if (Costume.frames == null) return RabbitTravelArt.Frame(Costume, showTravelPose ? activeTravel : exitingTravel, frame);
            if (Costume.frames.Length != 12) return null;
            return Art.Get($"travel_{Costume.id}_{frame}", () => DesignArt.HeroPose(Costume.frames, Pose.Idle0, false, frame));
        }

        DesignPack TravelDesign => DesignStore.PackOfCostume(Costume?.id);
        public TravelType MovementType => TravelDesign?.travelType ?? TravelType.Dash;
        public Vector2 SavePosition => activeTravel is TravelType.Dash or TravelType.Blink ? Pos : travelLanding;

        static Ground TravelGround(WorldStreamer world, Vector2 point) =>
            world.GroundAtTile(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));

        bool TravelPoint(WorldStreamer world, Vector2 point) =>
            world.IsLoaded(point) && (Bound <= 0 || point.sqrMagnitude <= Mathf.Max(Bound * Bound, Pos.sqrMagnitude));

        bool PrepareTravel(TravelType type, ref Vector2 direction)
        {
            activeTravel = type;
            travelLanding = Pos;
            enteredWater = false;
            var world = WorldStreamer.I;
            if (type == TravelType.Swim)
            {
                var aim = direction;
                for (float distance = 0.5f; distance <= 3f; distance += 0.5f)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        var candidate = MathX.Dir(aim.Angle() + (i % 2 == 0 ? -1 : 1) * ((i + 1) / 2) * 20);
                        var target = Pos + candidate * distance;
                        if (!TravelPoint(world, target) || !WorldGen.IsWater(TravelGround(world, target)) ||
                            !world.PathClear(Pos, target, true, radius)) continue;
                        direction = candidate;
                        enteredWater = WorldGen.IsWater(TravelGround(world, Pos));
                        waterEnteredAt = Time.time;
                        return true;
                    }
                }
            }
            if (type != TravelType.Blink) return false;

            // Blink through combatants while respecting the world's walls and landing on dry ground.
            var targetPoint = world.Raycast(Pos, direction, 4.2f, radius);
            if (!TravelPoint(world, targetPoint)) return false;
            PlayTravelFx(type);
            Pos = targetPoint;
            return true;
        }

        void MoveTravel(float dt)
        {
            var world = WorldStreamer.I;
            if (activeTravel == TravelType.Dash)
            {
                Pos = world.Move(Pos, dashVel * dt, radius, false, Bound);
                return;
            }
            if (activeTravel == TravelType.Blink) return;

            var direction = dashVel.normalized;
            if (activeTravel == TravelType.Burrow || (activeTravel == TravelType.Swim && enteredWater))
                direction = (AimPoint - Pos).SafeNormal(direction);
            float distance = dashVel.magnitude * dt;
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.16f));
            for (int i = 0; i < steps; i++)
            {
                var next = Pos + direction * (distance / steps);
                if (!TravelPoint(world, next)) break;
                var ground = TravelGround(world, next);
                bool water = WorldGen.IsWater(ground);
                if (WorldGen.IsLiquid(ground) && !water) break;
                bool dry = world.Free(next, radius);
                bool allowed = activeTravel switch
                {
                    TravelType.Flight or TravelType.Swim => world.Free(next, radius, true),
                    TravelType.Burrow => !water,
                    TravelType.Bounce => !water && WorldGen.Level(ground) == 0,
                    _ => dry
                };
                if (!allowed) break;
                Pos = next;
                Facing = direction;
                if (dry) travelLanding = Pos;
                if (activeTravel == TravelType.Swim)
                {
                    if (enteredWater && dry && !water) { dashT = 0; break; }
                    if (water && !enteredWater) waterEnteredAt = Time.time;
                    enteredWater |= water;
                }
            }
        }

        void FinishTravel()
        {
            if (showTravelPose)
            {
                exitingTravel = activeTravel;
                bool emerge = activeTravel == TravelType.Burrow || activeTravel == TravelType.Swim && enteredWater;
                travelExitUntil = Time.time + (emerge ? TravelExitTime : 0.12f);

            }
            showTravelPose = false;
            if (activeTravel == TravelType.Dash) return;
            if (activeTravel != TravelType.Blink && !WorldStreamer.I.Free(Pos, radius))
                Pos = travelLanding;
            PlayTravelFx(activeTravel);
            if (activeTravel == TravelType.Bounce && !bounceFollowup) bounceAgainUntil = Time.time + 0.32f;
            activeTravel = TravelType.Dash;
        }

        void PlayTravelFx(TravelType type)
        {
            var color = TravelTypes.Color(type);
            var frames = DesignRuntime.TravelFx(TravelDesign) ?? (type switch
            {
                TravelType.Burrow or TravelType.Bounce => Art.Dust(),
                TravelType.Swim => Art.ImpactShards(color, 14),
                TravelType.Blink => Art.ImpactRing(color, 14),
                _ => Art.Shockwave(color, 18)
            });
            Fx.I?.Play(frames, Pos, 0, 22, 0.85f, null, type is TravelType.Flight or TravelType.Blink);
            Fx.I?.Burst(Pos, color, 7, 3f, 0.3f, 0, 2, 100, (-Facing).Angle(), true);
        }

        void AnimateTravelFallback()
        {
            if (Costume.frames == null || Costume.frames.Length == 12 || (!showTravelPose && TravelDepth <= 0)) return;
            float elapsed = travelDuration - dashT;
            float stride = Mathf.Sin(elapsed * 25f);
            var scale = visual.localScale;
            float facing = Facing.x < 0 ? -1 : 1;
            switch (showTravelPose ? activeTravel : exitingTravel)
            {
                case TravelType.Burrow:
                    scale.y *= Mathf.Lerp(1f, 0.28f, TravelDepth);
                    scale.x *= 1f + TravelDepth * 0.15f;
                    break;
                case TravelType.Swim:
                    scale.y *= Mathf.Lerp(1f, 0.63f + stride * 0.10f, TravelDepth);
                    visual.localRotation = Quaternion.Euler(0, 0, facing * (-34f + stride * 8f) * TravelDepth);
                    break;
                case TravelType.Flight:
                    scale.y *= 1.05f + stride * 0.15f;
                    visual.localRotation = Quaternion.Euler(0, 0, facing * (-9f + stride * 9f));
                    break;
                case TravelType.Bounce:
                    scale.y *= 0.85f + Mathf.Sin(Mathf.Clamp01(dashT / travelDuration) * Mathf.PI) * 0.35f;
                    break;
            }
            visual.localScale = scale;
        }

        float TravelLift => activeTravel switch
        {
            TravelType.Flight => Mathf.Sin(Mathf.Clamp01(dashT / travelDuration) * Mathf.PI) * 0.75f,
            TravelType.Bounce => Mathf.Sin(Mathf.Clamp01(dashT / travelDuration) * Mathf.PI) * 1.05f,
            TravelType.Burrow => -0.32f * TravelDepth,
            TravelType.Swim when enteredWater => -0.24f * TravelDepth,
            _ => -0.24f * TravelDepth
        };
    }
}
