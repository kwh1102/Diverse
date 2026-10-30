using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>What a large landform puts on a tile. WorldGen turns it into a Ground; ChunkView into scenery.</summary>
    public enum Feature : byte
    {
        None,
        Scree,      // foothill gravel around a range (walkable)
        Mountain,   // the range itself (impassable)
        Peak,       // snow-capped crest (impassable)
        Canyon,     // a gap through the range: a hidden trail cut or a natural notch (walkable floor)
        Wetland,    // marshy bank along a river
        River,
        DeepRiver,
        Ford,       // shallow crossing (walkable)
        Bridge,     // where a trail crosses a river
        Lake,
        Causeway,   // a trail across a lake
        Road,       // a trail near a site, shown as a path
        Wall,       // keep / house wall (impassable)
        Rubble,     // collapsed wall (walkable)
        Court,      // paved yard inside a keep or village square
    }

    public enum SiteKind { Keep, Village }

    /// <summary>A built site on the trail network (a ruined keep, an abandoned village). Pure data from the seed.</summary>
    public class SiteSpec
    {
        public SiteKind kind;
        public Vector2Int center;      // tile
        public int hw, hh;             // half extents (tiles); the walls sit on the rim
        public bool onCross;           // sits on a crossroad (the E-W trail passes through too)
        public uint seed;
        public string id, name;
        public Vector2 Center => new Vector2(center.x + 0.5f, center.y + 0.5f);
        public float Radius => Mathf.Max(hw, hh) + 2;
        public bool Covers(int tx, int ty) => Mathf.Abs(tx - center.x) <= hw + 2 && Mathf.Abs(ty - center.y) <= hh + 2;
    }

    public struct LandSample
    {
        public Feature feature;
        public float lake;     // lake noise (sandy shores form where it is high but below the water line)
        public bool trail;     // on a hidden trail (its ground shows only where it matters)
        public bool forest;    // inside a dense forest
    }

    /// <summary>
    /// Large landforms of the infinite world, decided per tile from world coordinates alone — no chunk ever needs its
    /// neighbours, so the same seed and tile always give the same result in any order, and nothing is kept for far places.
    ///
    /// One relief field drives the natural shapes: mountain ranges run along its crest lines (n ≈ 0.5), rivers along its
    /// valley contours (n ≈ 0.25 / 0.75). Contours of one field never cross, so a river follows a range and never cuts it,
    /// and where the two masks overlap a river runs along the foot of the mountains. Separate masks decide where ranges and
    /// rivers actually appear, so some places have only mountains, some only rivers, some both, and much is open plain.
    ///
    /// A hidden trail grid (one wavy N-S and E-W line per TrailSpacing band) keeps everything connected: a trail cuts a
    /// canyon through a range, a bridge over a river and a causeway across a lake. Ruined keeps and abandoned villages
    /// stand on the trails (keeps on a crossroad or a road, villages on crossroads); the trail shows as a road near them.
    /// </summary>
    public class Landforms
    {
        public const int TrailSpacing = 96;
        const float TrailWave = 10f;             // how far a trail wanders from its straight line
        const int RoadReach = 40;                // trails show as roads this far from a site's walls
        public const int SiteMinDistance = 110;  // no site closer to the spawn

        readonly int seed;
        readonly System.Func<Vector2, bool> forbidden;   // places that must stay open (tower, zone boss arenas…)
        readonly Dictionary<Vector2Int, SiteSpec> sites = new Dictionary<Vector2Int, SiteSpec>();

        public Landforms(int seed, System.Func<Vector2, bool> forbidden = null)
        {
            this.seed = seed;
            this.forbidden = forbidden;
        }

        // ───────────── Trails ─────────────

        int TrailBaseX(int cx) => cx * TrailSpacing + 20 + (int)(Hash.U(cx, 0, seed + 601) % (TrailSpacing - 40));
        int TrailBaseY(int cy) => cy * TrailSpacing + 20 + (int)(Hash.U(0, cy, seed + 607) % (TrailSpacing - 40));

        /// <summary>
        /// The x of the N-S trail of column band cx at height y: a straight line inside a site and where it crosses a river
        /// (so bridges are straight planks, not staircases), wavy elsewhere.
        /// </summary>
        float TrailX(int cx, float y)
        {
            int baseX = TrailBaseX(cx);
            float wave = TrailWave * Mathf.Min(SiteTaper(SiteAt(cx, MathX.FloorDiv(Mathf.FloorToInt(y), TrailSpacing)), y, true), RiverTaper(baseX + 0.5f, y));
            return baseX + (Noise.Perlin(y, cx * 37.1f, seed + 611, 0.02f) - 0.5f) * 2f * wave;
        }

        float TrailY(int cy, float x)
        {
            int baseY = TrailBaseY(cy);
            var s = SiteAt(MathX.FloorDiv(Mathf.FloorToInt(x), TrailSpacing), cy);
            float wave = TrailWave * Mathf.Min(s != null && s.onCross ? SiteTaper(s, x, false) : 1, RiverTaper(x, baseY + 0.5f));
            return baseY + (Noise.Perlin(x, cy * 53.7f, seed + 613, 0.02f) - 0.5f) * 2f * wave;
        }

        static float SiteTaper(SiteSpec s, float along, bool ns) =>
            s == null ? 1 : Mathf.Clamp01((Mathf.Abs(along - (ns ? s.center.y : s.center.x)) - (ns ? s.hh : s.hw) - 3) / 14f);

        /// <summary>0 on a river's valley contour (where the trail will bridge it), 1 away from it.</summary>
        float RiverTaper(float x, float y)
        {
            float n = Relief(x, y);
            float level = RiverLevel(x, y);
            float dv = Mathf.Min(Mathf.Abs(n - level), Mathf.Abs(n - (1 - level)));
            return Mathf.Clamp01((dv - 0.03f) / 0.04f);
        }

        /// <summary>Is the tile on a trail (3 tiles wide)?</summary>
        public bool OnTrail(int tx, int ty)
        {
            int cx = MathX.FloorDiv(tx, TrailSpacing), cy = MathX.FloorDiv(ty, TrailSpacing);
            // Cheap reject first: a trail never strays further than its wave from its straight line
            bool nearX = Mathf.Abs(tx + 0.5f - TrailBaseX(cx)) <= TrailWave + 2, nearY = Mathf.Abs(ty + 0.5f - TrailBaseY(cy)) <= TrailWave + 2;
            if (!nearX && !nearY) return false;
            return (nearX && Mathf.Abs(tx + 0.5f - TrailX(cx, ty + 0.5f)) <= 1.5f) || (nearY && Mathf.Abs(ty + 0.5f - TrailY(cy, tx + 0.5f)) <= 1.5f);
        }

        // ───────────── Sites ─────────────

        /// <summary>The site of trail band (cx, cy), or null. Decided once per band from the seed.</summary>
        public SiteSpec SiteAt(int cx, int cy)
        {
            var key = new Vector2Int(cx, cy);
            if (sites.TryGetValue(key, out var s)) return s;
            sites[key] = null;    // guards re-entry (TrailX asks for the site while the site asks for nothing, but be safe)
            s = MakeSite(cx, cy);
            sites[key] = s;
            return s;
        }

        SiteSpec MakeSite(int cx, int cy)
        {
            uint h = Hash.U(cx, cy, seed + 701);
            var basePt = new Vector2Int(TrailBaseX(cx), TrailBaseY(cy));
            float r = (h & 0xFFFF) / 65536f;
            if (r < 0.55f) return null;
            bool keep = r < 0.8f;
            int offset = keep ? ((int)((h >> 16) % 3) - 1) * 30 : 0;     // a keep may sit on the road between crossroads
            var center = new Vector2Int(basePt.x, basePt.y + offset);
            var c = new Vector2(center.x + 0.5f, center.y + 0.5f);
            if (c.magnitude < SiteMinDistance) return null;
            if (forbidden != null && forbidden(c)) return null;
            var rng = new Rng(h);
            var s = new SiteSpec
            {
                kind = keep ? SiteKind.Keep : SiteKind.Village,
                center = center, onCross = offset == 0, seed = h,
                hw = keep ? rng.Range(16, 22) : 22,
                hh = keep ? rng.Range(14, 20) : 22,
                id = $"site{cx}_{cy}",
            };
            s.name = keep
                ? rng.Pick(new[] { "무너진 성채", "잊힌 왕의 성벽", "잿빛 요새 폐허", "바람 깎인 성채" })
                : rng.Pick(new[] { "버려진 마을", "말 없는 마을", "잡초 덮인 마을터", "텅 빈 마을" });
            return s;
        }

        /// <summary>The site whose footprint covers the tile (sites spill up to ~50 tiles out of their band).</summary>
        public SiteSpec SiteCovering(int tx, int ty)
        {
            int cx = MathX.FloorDiv(tx, TrailSpacing), cy = MathX.FloorDiv(ty, TrailSpacing);
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    var s = SiteAt(x, y);
                    if (s != null && s.Covers(tx, ty)) return s;
                }
            return null;
        }

        public IEnumerable<SiteSpec> SitesNear(Vector2 p, float range)
        {
            int cx0 = MathX.FloorDiv(Mathf.FloorToInt(p.x - range - 60), TrailSpacing), cx1 = MathX.FloorDiv(Mathf.FloorToInt(p.x + range + 60), TrailSpacing);
            int cy0 = MathX.FloorDiv(Mathf.FloorToInt(p.y - range - 60), TrailSpacing), cy1 = MathX.FloorDiv(Mathf.FloorToInt(p.y + range + 60), TrailSpacing);
            for (int y = cy0; y <= cy1; y++)
                for (int x = cx0; x <= cx1; x++)
                {
                    var s = SiteAt(x, y);
                    if (s != null && Vector2.Distance(s.Center, p) <= range + s.Radius) yield return s;
                }
        }

        /// <summary>Distance from the tile to the nearest site centre (for roads), or a large number.</summary>
        float SiteDistance(int tx, int ty)
        {
            int cx = MathX.FloorDiv(tx, TrailSpacing), cy = MathX.FloorDiv(ty, TrailSpacing);
            float best = 9999;
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    var s = SiteAt(x, y);
                    if (s == null) continue;
                    // Roads reach out along the trails: measure along the axis the trail runs on
                    float d = Mathf.Max(Mathf.Abs(tx - s.center.x) - s.hw, Mathf.Abs(ty - s.center.y) - s.hh);
                    if (d < best) best = d;
                }
            return best;
        }

        /// <summary>The site's own tile: walls, rubble, paved yard, or None where the land shows through (overgrown yard).</summary>
        public Feature SiteFeature(SiteSpec s, int tx, int ty)
        {
            int lx = tx - s.center.x, ly = ty - s.center.y;
            return s.kind == SiteKind.Keep ? KeepFeature(s, tx, ty, lx, ly) : VillageFeature(s, tx, ty, lx, ly);
        }

        Feature KeepFeature(SiteSpec s, int tx, int ty, int lx, int ly)
        {
            int ax = Mathf.Abs(lx), ay = Mathf.Abs(ly);
            if (ax > s.hw || ay > s.hh)
            {
                // Just outside the walls: rubble spilled from collapsed stretches, else the land
                if (ax <= s.hw + 2 && ay <= s.hh + 2 && Collapsed(tx, ty, 0.6f)) return Feature.Rubble;
                return Feature.None;
            }
            bool gateNS = ax <= 2, gateEW = s.onCross && ay <= 2;
            bool tower = ax >= s.hw - 3 && ay >= s.hh - 3;
            bool rim = ax >= s.hw - 1 || ay >= s.hh - 1;
            if (tower) return Feature.Wall;                       // corner towers never fall
            if (rim)
            {
                if ((ay >= s.hh - 1 && gateNS) || (ax >= s.hw - 1 && gateEW)) return Feature.Court;   // gate
                bool nearGate = (ay >= s.hh - 1 && ax <= 5) || (ax >= s.hw - 1 && s.onCross && ay <= 5);
                return !nearGate && Collapsed(tx, ty, 0.66f) ? Feature.Rubble : Feature.Wall;
            }
            if (gateNS || gateEW) return Feature.Court;           // the road through the yard
            // Inner keep (north-west), two house ruins (east), one (south-west)
            if (Building(tx, ty, lx, ly, -(s.hw * 45 / 100), s.hh * 40 / 100, Mathf.Min(6, s.hw / 2 - 3), Mathf.Min(4, s.hh / 2 - 3), 0, out var f)) return f;
            if (Building(tx, ty, lx, ly, s.hw * 50 / 100, s.hh * 35 / 100, 3, 2, 0, out f)) return f;
            if (Building(tx, ty, lx, ly, s.hw * 50 / 100, -(s.hh * 40 / 100), 3, 2, 0, out f)) return f;
            if (Building(tx, ty, lx, ly, -(s.hw * 50 / 100), -(s.hh * 40 / 100), 3, 2, 0, out f)) return f;
            // Yard: paving with grass breaking through, rubble heaps
            if (Collapsed(tx, ty, 0.72f)) return Feature.Rubble;
            return Noise.Perlin(tx + 0.5f, ty + 0.5f, seed + 731, 0.11f) > 0.56f ? Feature.None : Feature.Court;
        }

        Feature VillageFeature(SiteSpec s, int tx, int ty, int lx, int ly)
        {
            int ax = Mathf.Abs(lx), ay = Mathf.Abs(ly);
            if (ax > s.hw || ay > s.hh) return Feature.None;
            if (ax <= 4 && ay <= 4) return Feature.Court;         // the square
            var rng = new Rng(s.seed + 17);
            // Houses lining both roads, doors toward the road
            for (int i = 0; i < 10; i++)
            {
                int side = i % 2 == 0 ? 1 : -1;
                int along = -19 + (i / 2) * 9 + rng.Range(-1, 2);
                int off = side * (6 + rng.Range(0, 3));
                int hw = 2 + rng.Range(0, 2), hh = 2;
                bool ns = i < 6 || !s.onCross;      // the first houses on the N-S road, the rest on the E-W road
                if (!ns && !s.onCross) continue;
                int cx = ns ? off : along, cy = ns ? along : off;
                if (Mathf.Abs(cx) <= 5 && Mathf.Abs(cy) <= 5) continue;   // keep the square clear
                if (Building(tx, ty, lx, ly, cx, cy, ns ? hw : hh, ns ? hh : hw, ns ? (side > 0 ? 3 : 1) : (side > 0 ? 2 : 0), out var f)) return f;
            }
            return Feature.None;
        }

        /// <summary>
        /// A ruined building: a wall ring of half size (hw, hh) around local (cx, cy), with a doorway on one side
        /// (door: 0 = +x, 1 = -x, 2 = +y, 3 = -y) and stretches fallen to rubble. Returns false outside it.
        /// </summary>
        bool Building(int tx, int ty, int lx, int ly, int cx, int cy, int hw, int hh, int door, out Feature f)
        {
            f = Feature.None;
            int dx = lx - cx, dy = ly - cy;
            int ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
            if (ax > hw || ay > hh) return false;
            bool rim = ax == hw || ay == hh;
            if (!rim) { f = Collapsed(tx, ty, 0.7f) ? Feature.Rubble : Feature.Court; return true; }
            bool atDoor = door switch { 0 => dx == hw && ay <= 1, 1 => dx == -hw && ay <= 1, 2 => dy == hh && ax <= 1, _ => dy == -hh && ax <= 1 };
            if (atDoor) { f = Feature.Court; return true; }
            f = Collapsed(tx, ty, 0.58f) ? Feature.Rubble : Feature.Wall;
            return true;
        }

        bool Collapsed(int tx, int ty, float threshold) => Noise.Perlin(tx + 0.5f, ty + 0.5f, seed + 721, 0.09f) > threshold;

        float Relief(float x, float y) => Noise.Fractal(x, y, seed + 801, 0.0045f, 2);
        /// <summary>The relief value rivers run along (and 1 - it): wanders a little so valleys are not all alike.</summary>
        float RiverLevel(float x, float y) => 0.36f + (Noise.Perlin(x, y, seed + 841, 0.004f) - 0.5f) * 0.08f;
        float RidgeAt(float x, float y) => 1 - Mathf.Abs(2 * Relief(x, y) - 1);

        // ───────────── Relief: mountains, rivers, lakes ─────────────

        /// <summary>
        /// The landform on a tile. relief = false keeps the ground free of mountains and sites (spawn, boss arenas…);
        /// water = false keeps it dry (the spawn, structure yards).
        /// </summary>
        public LandSample At(int tx, int ty, Biome b, bool relief = true, bool water = true)
        {
            float x = tx + 0.5f, y = ty + 0.5f;
            var r = new LandSample();
            if (relief)
            {
                var site = SiteCovering(tx, ty);
                if (site != null)
                {
                    var f = SiteFeature(site, tx, ty);
                    if (f != Feature.None) { r.feature = f; return r; }
                    if (Mathf.Abs(tx - site.center.x) <= site.hw && Mathf.Abs(ty - site.center.y) <= site.hh) return r;   // overgrown yard: plain land
                }
            }
            bool trail = OnTrail(tx, ty);
            r.trail = trail;

            float n = Relief(x, y);                                        // the relief field
            float ridge = 1 - Mathf.Abs(2 * n - 1);                        // 1 on a crest line

            // Mountains: where the range mask allows, the crest band becomes rock; wider where the mask is strong
            if (relief)
            {
                float mask = Noise.Fractal(x, y, seed + 811, 0.0025f, 2) + BiomeRangeBias(b);
                if (mask > 0.45f)
                {
                    float thresh = 0.93f - (mask - 0.55f) * 0.3f;
                    if (ridge > thresh)
                    {
                        // A tile is rock only with three rocky neighbours: this rounds the corners and drops the specks
                        // that a bare threshold leaves along the edge (a lone rock tile is a pebble, not a range)
                        int near = 0;
                        if (RidgeAt(x + 1, y) > thresh) near++;
                        if (RidgeAt(x - 1, y) > thresh) near++;
                        if (RidgeAt(x, y + 1) > thresh) near++;
                        if (RidgeAt(x, y - 1) > thresh) near++;
                        if (near >= 3)
                        {
                            if (trail) { r.feature = Feature.Canyon; return r; }
                            // Natural notches: the range breaks here and there (never into a wide plain)
                            if (Noise.Perlin(x, y, seed + 821, 0.035f) > 0.76f) { r.feature = Feature.Canyon; return r; }
                            r.feature = ridge > thresh + (1 - thresh) * 0.7f ? Feature.Peak : Feature.Mountain;
                            return r;
                        }
                        r.feature = trail ? Feature.Canyon : Feature.Scree;
                    }
                    else if (ridge > thresh - 0.05f) r.feature = trail ? Feature.Canyon : Feature.Scree;
                }
            }

            if (water)
            {
                // Rivers along the valley contours of the same field: they run beside the ranges, never through them
                float wMask = Noise.Fractal(x, y, seed + 831, 0.003f, 2) + BiomeWaterBias(b);
                float lake = Noise.Fractal(x, y, seed + 51, 0.012f, 2);
                r.lake = lake;
                if (wMask > 0.38f)
                {
                    float level = RiverLevel(x, y);
                    float dv = Mathf.Min(Mathf.Abs(n - level), Mathf.Abs(n - (1 - level)));
                    float width = 0.012f + (wMask - 0.45f) * 0.06f;
                    if (dv < width)
                    {
                        if (trail) { r.feature = Feature.Bridge; return r; }
                        if (width < 0.022f && Noise.Perlin(x, y, seed + 851, 0.06f) > 0.76f) { r.feature = Feature.Ford; return r; }
                        r.feature = width > 0.018f && dv < width * 0.45f ? Feature.DeepRiver : Feature.River;
                        return r;
                    }
                    if (dv < width + 0.025f && wMask > 0.5f && r.feature == Feature.None && (b == Biome.Meadow || b == Biome.Forest || b == Biome.Marsh))
                        r.feature = Feature.Wetland;
                    // Lakes gather in the wet lowlands by the rivers
                    lake += (wMask - 0.5f) * 0.12f + (dv < 0.06f ? 0.05f : 0);
                }
                if (lake > 0.74f && r.feature != Feature.Scree)
                {
                    r.feature = trail ? Feature.Causeway : Feature.Lake;
                    return r;
                }
            }

            if (trail && r.feature == Feature.None && SiteDistance(tx, ty) < RoadReach) r.feature = Feature.Road;
            if (r.feature == Feature.None && (b == Biome.Meadow || b == Biome.Forest || b == Biome.Marsh || b == Biome.Starlit))
                r.forest = Noise.Fractal(x, y, seed + 861, 0.01f, 2) > 0.6f && Noise.Perlin(x, y, seed + 871, 0.06f) < 0.68f;   // dense forest with clearings
            return r;
        }

        static float BiomeRangeBias(Biome b) => b switch
        {
            Biome.Tundra => 0.08f, Biome.Volcano => 0.08f, Biome.Ashland => 0.04f, Biome.Dunes => -0.06f, Biome.Marsh => -0.08f, _ => 0,
        };

        static float BiomeWaterBias(Biome b) => b switch
        {
            Biome.Marsh => 0.1f, Biome.Forest => 0.03f, Biome.Dunes => -0.12f, Biome.Ashland => -0.1f, Biome.Volcano => -0.15f, Biome.Hive => -0.08f, _ => 0,
        };

        /// <summary>Does water (river or lake) lie within r tiles of c? (ferrymen by the water)</summary>
        public bool WaterNear(Vector2 c, int r, Biome b)
        {
            for (int y = -r; y <= r; y += 3)
                for (int x = -r; x <= r; x += 3)
                {
                    var f = At(Mathf.FloorToInt(c.x) + x, Mathf.FloorToInt(c.y) + y, b).feature;
                    if (f is Feature.River or Feature.DeepRiver or Feature.Lake or Feature.Ford) return true;
                }
            return false;
        }

        /// <summary>Share of tiles around c (ring of radius r) that are open ground (not rock, wall or water).</summary>
        public float OpenAround(Vector2 c, float r, Biome b)
        {
            int open = 0, n = 16;
            for (int i = 0; i < n; i++)
            {
                var p = c + MathX.Dir(i * 360f / n) * r;
                var f = At(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), b).feature;
                if (!IsBlocking(f) && f != Feature.Lake && f != Feature.River && f != Feature.DeepRiver) open++;
            }
            return open / (float)n;
        }

        public static bool IsBlocking(Feature f) => f is Feature.Mountain or Feature.Peak or Feature.Wall;

        static bool Passable(Feature f) => !IsBlocking(f) && f != Feature.Lake && f != Feature.River && f != Feature.DeepRiver;

        /// <summary>
        /// Can the place be walked out of? Rays in 16 directions must run reach tiles without meeting rock or water in at
        /// least minFree of them. A yard inside a river loop or a ring of mountains fails (nothing is put there).
        /// </summary>
        public bool CanLeave(Vector2 c, Biome b, float reach = 40, int minFree = 3)
        {
            int free = 0;
            for (int i = 0; i < 16; i++)
            {
                var dir = MathX.Dir(i * 22.5f);
                bool clear = true;
                for (float d = 3; d <= reach && clear; d += 2)
                {
                    var p = c + dir * d;
                    if (!Passable(At(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), b).feature)) clear = false;
                }
                if (clear && ++free >= minFree) return true;
            }
            return false;
        }
    }
}
