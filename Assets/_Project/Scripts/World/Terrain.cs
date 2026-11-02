using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Large landforms of the infinite world, decided per tile from world coordinates alone (any chunk order gives the same
    /// tiles, nothing is kept for far places):
    /// - Mountain ranges follow the crest lines of one warped noise field, so they run long and bend. Where the crest dips
    ///   (a second, slower noise) the range sinks below the rock level by itself and leaves a natural saddle to walk
    ///   through — passes are part of the range's shape, never holes cut into it. Ranges fade where a large-scale mask
    ///   says so, so the land also has wide plains. A crest gets an upper tier (Peak) along its middle.
    /// - Mesas: flat rock islands where a theme asks for them (the canyon).
    /// - Roads share a few town exits and outer branches, with local loops and terrain-aware bends. The land stays open around the
    ///   spawn, the roads and the sites by lowering the crest there (ranges taper into a pass), not by cutting a corridor.
    /// Zone borders don't cut any of this: ranges cross them and only their look changes (theme blending).
    /// </summary>
    public class Terrain
    {
        readonly int seed;
        readonly ZoneMap zones;
        readonly List<Road> roads = new List<Road>();
        public ExplorationLayout Exploration;
        static WorldRulesDef R => DB.WorldRules;

        class Road
        {
            public Vector2[] pts;
            public Rect box;      // grown by the road's reach (cheap reject)
            public float width;
        }

        public readonly List<Vector2> Junctions = new List<Vector2>();
        public readonly List<Vector2Int> Connections = new List<Vector2Int>();
        public int ExitCount { get; private set; }

        public Terrain(int seed, ZoneMap zones)
        {
            this.seed = seed;
            this.zones = zones;
            if (zones != null) BuildRoadNetwork();
        }

        // ───────────── Roads ─────────────

        void BuildRoadNetwork()
        {
            var rng = new Rng(Hash.U(seed, 91, 6221));
            int exits = Mathf.Clamp(R.roadExits, 2, 4);
            ExitCount = exits;
            // Match the existing plaza's cardinal approaches without changing its authored houses or square.
            float rotation = rng.Range(0, 4) * 90f;
            // Only these gates reach the town. Destinations attach to shared outer branches, never the origin.
            for (int i = 0; i < exits; i++)
            {
                float angle = rotation + i * 90f;
                var gate = MathX.Dir(angle) * (WorldGen.TownRadius - 2);
                var hub = MathX.Dir(angle + rng.Range(-25f, 25f)) * rng.Range(R.roadBranchDistance.x, R.roadBranchDistance.y);
                int a = Junctions.Count; Junctions.Add(gate); Junctions.Add(hub);
                Connect(a, a + 1, R.roadWidth);
            }
            var pending = new List<StructureSpec>(zones.sites);
            while (pending.Count > 0)
            {
                float best = float.MaxValue; int parent = -1, selected = -1;
                for (int i = 0; i < pending.Count; i++) for (int j = 0; j < Junctions.Count; j++)
                {
                    if (j < exits * 2 && j % 2 == 0) continue;
                    float distance = Vector2.Distance(pending[i].center, Junctions[j]);
                    // Avoid sending later links back through the town or the opposite side of the world.
                    if (SegmentDistance(Vector2.zero, Junctions[j], pending[i].center) < WorldGen.TownRadius + 8) continue;
                    if (distance < best) { best = distance; parent = j; selected = i; }
                }
                if (selected < 0) throw new System.InvalidOperationException("Road destination has no outer connection");
                var site = pending[selected]; pending.RemoveAt(selected);
                int child = Junctions.Count; Junctions.Add(site.center);
                Connect(parent, child, site.kind == StructKind.Landmark ? R.roadWidth : R.roadBranchWidth);
            }
            // Close a few local loops; no regular ring road and no cross-map diameters.
            for (int loop = 0; loop < R.roadLoops + exits - 1; loop++)
            {
                float best = float.MaxValue; int a = -1, b = -1;
                for (int i = 1; i < Junctions.Count; i++) for (int j = i + 1; j < Junctions.Count; j++)
                {
                    if (i < exits * 2 && i % 2 == 0 || j < exits * 2 && j % 2 == 0 || Linked(i, j)) continue;
                    float distance = Vector2.Distance(Junctions[i], Junctions[j]);
                    if (SegmentDistance(Vector2.zero, Junctions[i], Junctions[j]) < WorldGen.TownRadius + 14) continue;
                    if (distance < best) { best = distance; a = i; b = j; }
                }
                if (a < 0) break;
                Connect(a, b, R.roadLoopWidth);
            }
        }

        bool Linked(int a, int b) => Connections.Exists(e => e.x == a && e.y == b || e.x == b && e.y == a);

        void Connect(int a, int b, float width)
        {
            Connections.Add(new Vector2Int(a, b));
            roads.Add(MakeRoad(Junctions[a], Junctions[b], Hash.U(a, b, seed + 6231), width));
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            return Vector2.Distance(p, a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(.001f, ab.sqrMagnitude)));
        }

        Road MakeRoad(Vector2 from, Vector2 to, uint h, float width)
        {
            float len = Vector2.Distance(from, to);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / Mathf.Max(6, R.roadSegmentLength)));
            var dir = (to - from) / Mathf.Max(.001f, len);
            var side = new Vector2(-dir.y, dir.x);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                // wanders sideways, pinned at both ends; starts at the town's edge
                float wob = (Noise.Fractal(t * len, h % 1000, (int)(h % 100000), 0.02f, 2) - 0.5f) * 2f * R.roadWander * Mathf.Sin(t * Mathf.PI);
                var desired = Vector2.Lerp(from, to, t) + side * wob;
                pts[i] = desired;
                if (i > 0 && i < n)
                {
                    float best = float.MaxValue;
                    for (int option = -2; option <= 2; option++)
                    {
                        var candidate = desired + side * option * R.roadTerrainAvoidance * Mathf.Sin(t * Mathf.PI) * .5f;
                        float ridge = Ridge(candidate, Bias(candidate, theme => theme.mountainBias), false);
                        float cost = Mathf.Max(0, ridge - R.cliffLevel + .03f) * 80 + Water(candidate) * 2 + Mathf.Abs(option) * .25f;
                        if (i > 1) cost += Mathf.Abs(Vector2.Dot(candidate - pts[i - 1], side)) * .06f;
                        if (candidate.magnitude < WorldGen.TownRadius + width && from.magnitude > WorldGen.TownRadius) cost += 100;
                        if (cost < best) { best = cost; pts[i] = candidate; }
                    }
                }
            }
            float pad = 40;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            return new Road { pts = pts, width = width, box = Rect.MinMaxRect(minX - pad, minY - pad, maxX + pad, maxY + pad) };
        }

        /// <summary>Distance to the nearest road (a large number far from all of them).</summary>
        public float RoadDistance(Vector2 p) => RoadFrame(p, out _);

        /// <summary>Closest road segment's tangent, for consistently orienting the deck across a river.</summary>
        public float RoadFrame(Vector2 p, out Vector2 direction)
            => RoadFrame(p, out direction, out _, true);

        public Vector2 NearestRoadPoint(Vector2 p) { RoadFrame(p, out _, out var nearest, false); return nearest; }

        float RoadFrame(Vector2 p, out Vector2 direction, out Vector2 nearest, bool local)
        {
            float best = 999;
            direction = Vector2.up;
            nearest = p;
            foreach (var r in roads)
            {
                // NearestRoadPoint also serves places farther from a road than its rendering query box.
                if (local && !r.box.Contains(p)) continue;
                for (int i = 0; i < r.pts.Length - 1; i++)
                {
                    var a = r.pts[i]; var ab = r.pts[i + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    var point = a + ab * t;
                    float d = Vector2.Distance(p, point);
                    if (d < best) { best = d; direction = ab; nearest = point; }
                }
            }
            return best;
        }

        public float RoadClearance(Vector2 p)
        {
            float best = 999;
            foreach (var road in roads)
            {
                if (!road.box.Contains(p)) continue;
                for (int i = 1; i < road.pts.Length; i++)
                    best = Mathf.Min(best, SegmentDistance(p, road.pts[i - 1], road.pts[i]) - road.width);
            }
            return best;
        }

        public bool OnRoad(Vector2 p) => RoadClearance(p) <= (Noise.Perlin(p.x, p.y, seed + 1301, 0.3f) - .5f) * .65f;

        // ───────────── Openness ─────────────

        /// <summary>0 where the land must stay open (spawn, roads, sites), rising to 1 over WorldRules.clearance.</summary>
        public float Openness(Vector2 p)
        {
            float d = p.magnitude - (R.centerRadius - 10);
            d = Mathf.Min(d, RoadClearance(p) - 1);
            if (zones != null)
                foreach (var s in zones.sites)
                {
                    float ds = Vector2.Distance(p, s.center) - s.radius;
                    if (ds < d) d = ds;
                }
            float openness = Mathf.Clamp01(d / Mathf.Max(1, R.clearance));
            return Exploration != null ? Mathf.Min(openness, Exploration.TerrainOpenness(p)) : openness;
        }

        // ───────────── Mountains ─────────────

        /// <summary>Crest strength (0~1+) of the ranges at p: rock above WorldRules.cliffLevel, the upper tier above peakLevel.</summary>
        public float Crest(Vector2 p, float bias)
            => Ridge(p, bias, true);

        float Ridge(Vector2 p, float bias, bool clearRoads)
        {
            var r = R;
            // Warped crest lines: long, bending ranges
            var q = p + new Vector2(Noise.Fractal(p.x, p.y, seed + 1203, 0.009f, 2) - 0.5f, Noise.Fractal(p.x, p.y, seed + 1207, 0.009f, 2) - 0.5f) * (2 * r.rangeWarp);
            // One smooth octave keeps the crest band about equally wide along its whole length (a long range, not blobs);
            // a little detail noise roughens its edges
            float n = Noise.Perlin(q.x, q.y, seed + 1201, r.rangeFrequency);
            float ridge = 1 - Mathf.Abs(2 * n - 1) + (Noise.Perlin(p.x, p.y, seed + 1205, 0.09f) - 0.5f) * 0.05f;
            // Where ranges exist at all (wide plains elsewhere): a range thins out and ends where the mask falls
            float mask = Noise.Fractal(p.x, p.y, seed + 1211, r.rangeMaskFrequency, 2) + bias;
            float m = Mathf.SmoothStep(0, 1, Mathf.Clamp01((mask - r.rangeMaskThreshold + 0.06f) / 0.12f));
            float e = ridge - (1 - m) * 0.45f;
            // The crest rises and sinks along its length; deep dips are the natural saddles
            e *= Mathf.Lerp(0.93f, 1.03f, Noise.Perlin(q.x, q.y, seed + 1221, 0.025f));
            float dip = Mathf.Clamp01((0.3f - Noise.Perlin(q.x, q.y, seed + 1231, 1.5f / Mathf.Max(8, r.passSpacing))) / 0.12f);
            e -= dip * dip * (3 - 2 * dip) * (r.passRadius / 7f) * 0.3f;
            // Around the spawn, the roads and the sites the ranges taper down into open ground
            float open = clearRoads ? Openness(p) : 1;
            e -= (1 - open) * 0.35f;
            return e;
        }

        /// <summary>Terrain level at a tile: 0 open ground, 1 rock (cliffs), 2 the upper tier.</summary>
        public int Height(int tx, int ty)
        {
            int step = Mathf.Max(1, R.terrainStep);
            var p = new Vector2(MathX.FloorDiv(tx, step) * step + step * .5f,
                MathX.FloorDiv(ty, step) * step + step * .5f);
            float bias = Bias(p, t => t.mountainBias);
            float e = Crest(p, bias);
            var r = R;
            if (e > r.peakLevel) return 2;
            if (e > r.cliffLevel) return 1;
            // Mesas (flat rock islands) where the theme wants them
            float mesas = Bias(p, t => t.mesas);
            if (mesas > 0.01f)
            {
                float f = Noise.Fractal(p.x, p.y, seed + 1241, 0.035f, 2);
                if (f > 1 - mesas * 0.32f && Openness(p) > 0.9f) return 1;
            }
            return 0;
        }

        float Bias(Vector2 p, System.Func<ThemeDef, float> f) =>
            zones != null ? zones.Blend(p, f) : f(DB.ThemeForBiome(Biome.Meadow));

        // ───────────── Water ─────────────

        /// <summary>0 dry, 1 river or lake, 2 deep. Rivers run along one noise contour; lakes gather in another field's highs.</summary>
        public int Water(Vector2 p)
        {
            var r = R;
            float bias = Bias(p, t => t.waterBias);
            float lake = Noise.Fractal(p.x, p.y, seed + 51, r.lakeFrequency, 3);
            float river = Mathf.Abs(Noise.Fractal(p.x, p.y, seed + 77, r.riverFrequency, 2) - 0.5f);
            float width = r.riverWidth * Mathf.Max(0, 1 + bias * 2.5f);
            float lakeLevel = r.lakeLevel - bias * 0.12f;
            if (lake > lakeLevel) return lake > lakeLevel + 0.06f ? 2 : 1;
            if (river < width) return river < width * 0.35f && width > r.riverWidth ? 2 : 1;
            return 0;
        }

        /// <summary>The shore where lakes meet the land (sandy edges).</summary>
        public bool Shore(Vector2 p) => Noise.Fractal(p.x, p.y, seed + 51, R.lakeFrequency, 3) > R.lakeLevel - 0.06f;
    }
}
