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
            if (activeTravel == TravelType.Swim && enteredWater)
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
                    enteredWater |= water;
                }
            }
        }

        void FinishTravel()
        {
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

        float TravelLift => activeTravel switch
        {
            TravelType.Flight => Mathf.Sin(Mathf.Clamp01(dashT / travelDuration) * Mathf.PI) * 0.75f,
            TravelType.Bounce => Mathf.Sin(Mathf.Clamp01(dashT / travelDuration) * Mathf.PI) * 1.05f,
            TravelType.Burrow => -0.24f,
            TravelType.Swim when enteredWater => -0.2f,
            _ => 0f
        };
    }
}
