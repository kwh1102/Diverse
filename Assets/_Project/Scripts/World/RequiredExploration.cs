using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public sealed partial class ExplorationLayout
    {
        readonly List<StructureSpec> requiredSites = new List<StructureSpec>();
        readonly Dictionary<string, float> requiredBounds = new Dictionary<string, float>();
        public IReadOnlyList<StructureSpec> RequiredSites => requiredSites;

        /// <summary>Reserve every selected theme's two distinct places before chunk generation, independent of load order.</summary>
        public void ReserveRequiredSites()
        {
            if (world.Zones == null || requiredSites.Count != 0) return;
            var placedThemes = new HashSet<string>();
            ReserveTheme(DB.SpawnTheme, true);
            foreach (var theme in world.Zones.themes) ReserveTheme(theme, false);

            void ReserveTheme(ThemeDef theme, bool central)
            {
                if (!placedThemes.Add(theme.id) || !pools.TryGetValue(theme.id, out var pool) || pool.Count == 0) return;
                var ordered = new List<ExplorationSiteDef>(pool);
                ordered.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
                int offset = (int)(Hash.U(0, 0, world.seed ^ (int)Hash.Str(theme.id)) % (uint)ordered.Count);
                for (int i = 0; i < ordered.Count; i++)
                {
                    var def = ordered[(i + offset) % ordered.Count];
                    int ring = central ? 0 : Mathf.Min(i, 1);
                    var center = FindRequiredPosition(def, theme, ring);
                    var site = new StructureSpec
                    {
                        id = "required:" + def.id, kind = StructKind.Exploration, exploration = def.id,
                        center = center, radius = def.halfSize.magnitude + 1, theme = theme, biome = theme.biome,
                        tier = Mathf.RoundToInt(WorldGen.TierAt(center.magnitude)), name = def.displayName, landmark = true,
                    };
                    requiredSites.Add(site);
                    requiredBounds[site.id] = WorldGen.Rings[ring].radius;
                }
            }
        }

        Vector2 FindRequiredPosition(ExplorationSiteDef def, ThemeDef theme, int ring)
        {
            float radius = def.halfSize.magnitude + 1;
            float margin = Mathf.Max(10, Rules.explorationTrailBend + 4);
            float min = ring == 0 ? WorldGen.TownRadius + radius + Rules.explorationClearance
                : WorldGen.Rings[ring - 1].radius + radius + margin;
            float max = WorldGen.Rings[ring].radius - radius - margin;
            var rng = new Rng(Hash.U(ring, 0, world.seed ^ (int)Hash.Str(def.id)));
            Vector2 best = default;
            float bestScore = float.MaxValue;
            bool found = false;
            // Prefer natural dry sites away from roads. Water/road conflicts may never silently erase a required place.
            for (int i = 0; i < 2048; i++)
            {
                float distance = Mathf.Sqrt(rng.Range(min * min, max * max));
                var point = MathX.Dir(rng.Range(0f, 360f)) * distance;
                point = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
                Consider(point, Rules.explorationClearance);
                if (found && bestScore < 0.001f) return best;
            }
            if (found) return best;
            // Deterministic exhaustive fallback: ignore decorative clearance, but retain physical non-overlap,
            // the correct theme, the town, and the full footprint inside the intended mist ring.
            int reach = Mathf.CeilToInt(max);
            for (int y = -reach; y <= reach; y++) for (int x = -reach; x <= reach; x++)
            {
                Consider(new Vector2(x, y), 1);
                if (found && bestScore < 0.001f) return best;
            }
            if (!found) throw new InvalidOperationException("No reserved exploration space for " + def.id + " in ring " + ring);
            return best;

            void Consider(Vector2 point, float gap)
            {
                float distance = point.magnitude;
                if (distance < min || distance > max || world.ThemeAt(point.x, point.y).id != theme.id) return;
                foreach (var other in requiredSites)
                    if (Vector2.Distance(point, other.center) < radius + other.radius + gap) return;
                foreach (var other in world.Zones.sites)
                    if (Vector2.Distance(point, other.center) < radius + other.radius + gap) return;
                float score = Mathf.Max(0, radius + Rules.roadWidth + 2 - world.Terrain.RoadDistance(point)) * 10;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                    if (world.Terrain.Water(point + new Vector2(x * def.halfSize.x, y * def.halfSize.y)) != 0) score += 100;
                if (score >= bestScore) return;
                found = true; bestScore = score; best = point;
            }
        }

        public bool ReservedGround(Vector2 point)
        {
            foreach (var site in requiredSites)
            {
                var half = Definition(site).halfSize;
                var delta = point - site.center;
                if (Mathf.Abs(delta.x) <= half.x + 1 && Mathf.Abs(delta.y) <= half.y + 1) return true;
            }
            return false;
        }
    }
}
