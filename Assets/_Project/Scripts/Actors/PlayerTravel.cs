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
        bool completingTravel;
        Vector2 travelExitTarget;
        readonly System.Collections.Generic.List<Vector2> travelExitPath = new System.Collections.Generic.List<Vector2>();
        int travelExitWaypoint;
        float travelVisualLift;

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
        static float SecondsPerCharge(TravelType type) => TravelTypes.GaugeSecondsPerUnit(type);

        bool SpendTravelCharge()
        {
            if (DashCharges <= 0) return false;
            // A full pool has no pending recovery. Otherwise spending one completed charge
            // must preserve the partial charge already being recovered.
            if (DashCharges >= DashChargesMax) dashRecharge = 0;
            DashCharges--;
            return true;
        }
        public void RecoverTravel(float seconds)
        {
            if (seconds <= 0 || DashCharges >= DashChargesMax) return;
            dashRecharge += seconds;
            while (DashCharges < DashChargesMax && dashRecharge >= DashRechargeTime)
            { dashRecharge -= DashRechargeTime; DashCharges++; }
            if (DashCharges >= DashChargesMax) dashRecharge = 0;
        }

        void ExtendFlight(Vector2 direction)
        {
            if (Abilities != null && Abilities.Forbids("Dash")) return;
            if (!SpendTravelCharge()) return;
            float elapsed = TravelElapsed;
            // The automatic exit route is not purchased flight time. Replace it with a fresh
            // paid segment; during normal flight append the segment to the remaining time.
            dashT = (completingTravel ? 0 : Mathf.Max(0, dashT)) + TravelTypes.FlightDuration;
            travelDuration = elapsed + dashT;
            completingTravel = false;
            travelExitPath.Clear(); travelExitWaypoint = 0;
            direction = direction.SafeNormal(Facing);
            dashVel = direction * 15f;
            Facing = direction;
            showTravelPose = true; travelExitUntil = 0;
            // Keep the original landing position and animation phase. No landing/takeoff
            // reset, and extending flight does not grant another invulnerability window.
            PlayTravelFx(TravelType.Flight);
            Sfx.Play("dash", 0.45f);
            Telemetry.Record("FlightExtend");
        }

        float TravelStep(float dt)
        {
            if (completingTravel) return Mathf.Min(dt, dashT);
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
                frame = Costume.frames == null
                    ? entering ? Mathf.Min(1, Mathf.FloorToInt(elapsed / 0.05f)) : 2 + Mathf.FloorToInt(elapsed * 14f) % 4
                    : entering ? 0 : 1 + Mathf.FloorToInt(elapsed * 10f) % 2;
            }
            else if (Time.time < travelExitUntil) frame = Costume.frames == null ? 6 + Mathf.Min(1, Mathf.FloorToInt(ExitProgress * 2)) : 3;
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
            completingTravel = false;
            travelExitPath.Clear();
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
                        if (!TravelPoint(world, target) || !TravelRules.CanCrossLiquid(type, TravelGround(world, target)) ||
                            !TravelPathClear(world, Pos, target)) continue;
                        direction = candidate;
                        enteredWater = TravelRules.CanCrossLiquid(type, TravelGround(world, Pos));
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
            EmitTravelAfterimages(Pos, targetPoint, 0.04f, true);
            Pos = targetPoint;
            return true;
        }

        void MoveTravel(float dt)
        {
            var world = WorldStreamer.I;
            if (completingTravel) { MoveToTravelExit(dt); return; }
            if (activeTravel == TravelType.Dash)
            {
                Pos = world.Move(Pos, dashVel * dt, radius, false, Bound);
                return;
            }
            if (activeTravel == TravelType.Blink) return;

            var direction = dashVel.normalized;
            if (completingTravel) direction = (travelExitTarget - Pos).SafeNormal(direction);
            else if (activeTravel == TravelType.Burrow || (activeTravel == TravelType.Swim && enteredWater))
                direction = (AimPoint - Pos).SafeNormal(direction);
            float distance = dashVel.magnitude * dt;
            if (completingTravel) distance = Mathf.Min(distance, Vector2.Distance(Pos, travelExitTarget));
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.16f));
            for (int i = 0; i < steps; i++)
            {
                var next = Pos + direction * (distance / steps);
                if (!TravelPoint(world, next)) break;
                var ground = TravelGround(world, next);
                bool water = WorldGen.IsLiquid(ground);
                bool dry = world.Free(next, radius);
                bool allowed = TravelFree(world, next);
                if (!allowed) break;
                Pos = next;
                Facing = direction;
                if (dry) travelLanding = Pos;
                if (completingTravel && dry) { dashT = 0; break; }
                if (activeTravel == TravelType.Swim)
                {
                    if (enteredWater && dry && !water) { dashT = 0; break; }
                    if (water && !enteredWater) waterEnteredAt = Time.time;
                    enteredWater |= water;
                }
            }
        }

        bool TravelFree(WorldStreamer world, Vector2 point)
        {
            bool LiquidAllowed(Vector2 p) => TravelPoint(world, p) &&
                (!WorldGen.IsLiquid(TravelGround(world, p)) || TravelRules.CanCrossLiquid(activeTravel, TravelGround(world, p)));
            // Check the whole collision footprint, including lava touching the bank rather than just the centre tile.
            float r = radius * 0.7f;
            if (!LiquidAllowed(point) || !LiquidAllowed(point + new Vector2(-r, -r * 0.5f)) ||
                !LiquidAllowed(point + new Vector2(r, -r * 0.5f)) || !LiquidAllowed(point + new Vector2(-r, r * 0.5f)) ||
                !LiquidAllowed(point + new Vector2(r, r * 0.5f))) return false;
            return activeTravel switch
            {
                TravelType.Flight or TravelType.Swim => world.Free(point, radius, true),
                TravelType.Burrow => true,
                TravelType.Bounce => WorldGen.Level(TravelGround(world, point)) == 0,
                _ => world.Free(point, radius)
            };
        }

        bool TravelPathClear(WorldStreamer world, Vector2 from, Vector2 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / TravelRules.ProbeStep));
            for (int i = 1; i <= steps; i++)
                if (!TravelFree(world, Vector2.Lerp(from, to, i / (float)steps))) return false;
            return true;
        }

        void CompleteNearTravelExit()
        {
            if (completingTravel || activeTravel is not (TravelType.Flight or TravelType.Burrow or TravelType.Swim)) return;
            var world = WorldStreamer.I;
            if (!TravelRules.TryFindNearestExit(Pos, p => TravelFree(world, p), p => world.Free(p, radius), travelExitPath)) return;
            travelExitWaypoint = 0;
            travelExitTarget = travelExitPath[travelExitPath.Count - 1];
            completingTravel = true;
            float distance = 0;
            var previous = Pos;
            foreach (var point in travelExitPath) { distance += Vector2.Distance(previous, point); previous = point; }
            // Follow the nearest reachable exit without spending more gauge or allowing steering.
            dashT = distance / Mathf.Max(1, dashVel.magnitude) + 0.1f;
            travelDuration += dashT;
        }

        void MoveToTravelExit(float dt)
        {
            float remaining = Mathf.Max(1, dashVel.magnitude) * dt;
            var world = WorldStreamer.I;
            while (remaining > 0 && travelExitWaypoint < travelExitPath.Count)
            {
                var target = travelExitPath[travelExitWaypoint];
                float distance = Vector2.Distance(Pos, target);
                if (distance < 0.005f) { travelExitWaypoint++; continue; }
                var direction = (target - Pos) / distance;
                float step = Mathf.Min(remaining, distance, 0.1f);
                var next = Pos + direction * step;
                if (!TravelFree(world, next)) { completingTravel = false; dashT = 0; break; }
                Pos = next; Facing = direction; remaining -= step;
                if (world.Free(Pos, radius)) { travelLanding = Pos; dashT = 0; return; }
            }
            if (travelExitWaypoint >= travelExitPath.Count) dashT = 0;
        }

        void EmitTravelAfterimages(Vector2 from, Vector2 to, float dt, bool force = false)
        {
            float distance = Vector2.Distance(from, to);
            ghostT -= dt;
            if (!force && (ghostT > 0 || distance < 0.01f)) return;
            ghostT = TravelTypes.AfterimageInterval;
            // One snapshot per interval, with no distance-based multiplication each frame.
            // A blink gets a small bridge at once because its movement itself is instantaneous.
            int count = force ? 5 : 1;
            Color color = TravelTypes.AfterimageColor(activeTravel);
            Sprite sprite = TravelSprite() ?? body.sprite;
            var offset = body.transform.position - (Vector3)Pos;
            for (int i = 0; i < count; i++)
            {
                float fraction = force ? i / (float)(count - 1) : 1;
                var tint = color;
                if (force) tint.a *= 0.4f + fraction * 0.6f;
                Fx.I?.Afterimage(body, tint, TravelTypes.AfterimageLifetime, true,
                    (Vector3)Vector2.Lerp(from, to, fraction) + offset, sprite, true);
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
            completingTravel = false;
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
            TravelType.Flight => Mathf.SmoothStep(0, 0.75f,
                Mathf.Min(Mathf.Clamp01(TravelElapsed / 0.1f), Mathf.Clamp01(dashT / 0.12f))),
            TravelType.Bounce => Mathf.Sin(Mathf.Clamp01(dashT / travelDuration) * Mathf.PI) * 1.05f,
            TravelType.Burrow => -0.32f * TravelDepth,
            TravelType.Swim when enteredWater => -0.24f * TravelDepth,
            _ => -0.24f * TravelDepth
        };
    }
}
