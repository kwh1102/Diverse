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
    /// - Roads run from the spawn to every landmark and border site, wandering a little. The land is kept open around the
    ///   spawn, the roads and the sites by lowering the crest there (ranges taper into a pass), not by cutting a corridor.
    /// Zone borders don't cut any of this: ranges cross them and only their look changes (theme blending).
    /// </summary>
    public class Terrain
    {
        readonly int seed;
        readonly ZoneMap zones;
        readonly List<Road> roads = new List<Road>();
        static WorldRulesDef R => DB.WorldRules;

        class Road
        {
            public Vector2[] pts;
            public Rect box;      // grown by the road's reach (cheap reject)
        }

        public Terrain(int seed, ZoneMap zones)
        {
            this.seed = seed;
            this.zones = zones;
            if (zones != null) foreach (var s in zones.sites) roads.Add(MakeRoad(s.center, Hash.Str(s.id) + (uint)seed));
        }

        // ───────────── Roads ─────────────

        Road MakeRoad(Vector2 to, uint h)
        {
            float len = to.magnitude;
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 14f));
            var dir = to / len;
            var side = new Vector2(-dir.y, dir.x);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                // wanders sideways, pinned at both ends; starts at the town's edge
                float wob = (Noise.Fractal(t * len, h % 1000, (int)(h % 100000), 0.02f, 2) - 0.5f) * 2f * R.roadWander * Mathf.Sin(t * Mathf.PI);
                pts[i] = dir * Mathf.Lerp(WorldGen.TownRadius - 2, len, t) + side * wob;
            }
            float pad = 40;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            return new Road { pts = pts, box = Rect.MinMaxRect(minX - pad, minY - pad, maxX + pad, maxY + pad) };
        }

        /// <summary>Distance to the nearest road (a large number far from all of them).</summary>
        public float RoadDistance(Vector2 p)
        {
            float best = 999;
            foreach (var r in roads)
            {
                if (!r.box.Contains(p)) continue;
                for (int i = 0; i < r.pts.Length - 1; i++)
                {
                    var a = r.pts[i]; var ab = r.pts[i + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    float d = Vector2.Distance(p, a + ab * t);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        public bool OnRoad(Vector2 p) => RoadDistance(p) <= R.roadWidth + (Noise.Perlin(p.x, p.y, seed + 1301, 0.3f) - 0.5f) * 0.8f;

        // ───────────── Openness ─────────────

        /// <summary>0 where the land must stay open (spawn, roads, sites), rising to 1 over WorldRules.clearance.</summary>
        public float Openness(Vector2 p)
        {
            float d = p.magnitude - (R.centerRadius - 10);
            d = Mathf.Min(d, RoadDistance(p) - R.roadWidth - 1);
            if (zones != null)
                foreach (var s in zones.sites)
                {
                    float ds = Vector2.Distance(p, s.center) - s.radius;
                    if (ds < d) d = ds;
                }
            return Mathf.Clamp01(d / Mathf.Max(1, R.clearance));
        }

        // ───────────── Mountains ─────────────

        /// <summary>Crest strength (0~1+) of the ranges at p: rock above WorldRules.cliffLevel, the upper tier above peakLevel.</summary>
        public float Crest(Vector2 p, float bias)
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
            float open = Openness(p);
            e -= (1 - open) * 0.35f;
            return e;
        }

        /// <summary>Terrain level at a tile: 0 open ground, 1 rock (cliffs), 2 the upper tier.</summary>
        public int Height(int tx, int ty)
        {
            var p = new Vector2(tx + 0.5f, ty + 0.5f);
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
