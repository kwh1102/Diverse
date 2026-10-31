using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum StructKind { Town, Camp, Ruins, Shrine, Grove, Graveyard, Lair, Chest, Pond, Wanderer, Ferry, Landmark, Border }

    /// <summary>
    /// A structure spec produced by the generator. Being pure data (no Unity objects),
    /// the same seed always yields the same structure. ChunkView turns it into actual objects.
    /// </summary>
    public class StructureSpec
    {
        public string id;              // unique id (for save records)
        public StructKind kind;
        public Vector2 center;
        public float radius;
        public Biome biome;
        public int tier;               // difficulty
        public string name;
        public string boss;
        public bool landmark;          // whether it shows on the map
        public ThemeDef theme;         // look of the place (zone landmarks: their zone's theme)
        public int zone = -1, zone2 = -1;   // Landmark: its zone; Border: the two zones it joins
    }

    /// <summary>
    /// Seed-based infinite world generator, in the order the world is composed:
    /// 1. Skeleton: the spawn and, in normal mode, the five zones (ZoneMap: build type + theme per zone, blended borders)
    /// 2. Landmarks: each zone's landmark and the border sites (ZoneMap.sites), placed before any terrain
    /// 3. Large terrain (Terrain): mountain ranges with natural saddles, mesas, rivers and lakes, roads to the landmarks
    /// 4. Theme painting: ground, liquids and cliff look from the theme at each tile (organic patches near borders)
    /// 5. Small scenery: ChunkView scatters each theme's decor on open ground
    /// Every step reads world coordinates only, so any chunk order gives the same world. Structures: one or two
    /// candidates per region (3x3 chunks), kept off mountains, water and the landmarks.
    /// </summary>
    public class WorldGen
    {
        public const int Chunk = 16;            // tiles per chunk
        public const int RegionChunks = 3;      // one structure candidate per region
        public const float TownRadius = 15f;
        const int TrailSpacing = 96;            // bridges sit where a (hidden) trail line crosses a river

        // ───────────── Frontier rings ─────────────
        // The world is infinite, but the Mist of Oblivion keeps the unproven close to home.
        // Each ring opens with the hero's level AND the bosses the world has seen fall (across lives).
        public struct Ring { public float radius; public int level, bosses; public string name; }
        public static readonly Ring[] Rings =
        {
            new Ring { radius = 170, level = 0, bosses = 0, name = "고향의 들판" },
            new Ring { radius = 320, level = 8, bosses = 1, name = "바깥 땅" },
            new Ring { radius = 500, level = 14, bosses = 3, name = "잊힌 변경" },
            new Ring { radius = 720, level = 20, bosses = 6, name = "안개 너머" },
            new Ring { radius = 0, level = 27, bosses = 10, name = "세계의 끝" },     // 0 = no limit
        };

        /// <summary>Index of the outermost ring that's open.</summary>
        public static int OpenRing(int level, int bosses)
        {
            int r = 0;
            for (int i = 1; i < Rings.Length; i++) if (level >= Rings[i].level && bosses >= Rings[i].bosses) r = i; else break;
            return r;
        }

        /// <summary>Max distance from the origin the hero may travel (0 = unlimited).</summary>
        public static float Bound(int level, int bosses) => Rings[OpenRing(level, bosses)].radius;

        /// <summary>Danger grows without limit with distance; each ring is a clear step up.</summary>
        public static float TierAt(float dist) => Mathf.Max(0, (dist - 30) / 55f);

        public static int RingAt(float dist)
        {
            for (int i = 0; i < Rings.Length - 1; i++) if (dist < Rings[i].radius) return i;
            return Rings.Length - 1;
        }

        public readonly int seed;
        /// <summary>The five zones (normal mode); null in a custom-mode world, where biomes come from noise.</summary>
        public readonly ZoneMap Zones;
        public readonly Terrain Terrain;
        readonly Dictionary<Vector2Int, List<StructureSpec>> regionCache = new Dictionary<Vector2Int, List<StructureSpec>>();

        public WorldGen(int seed, ZoneMap zones = null)
        {
            this.seed = seed;
            Zones = zones;
            Terrain = new Terrain(seed, zones);
        }

        // ───────────── Themes ─────────────

        /// <summary>The theme at a point: the zones' (normal mode) or the noise biome's (custom mode).</summary>
        public ThemeDef ThemeAt(float x, float y) => Zones != null ? Zones.ThemeAt(x, y) : DB.ThemeForBiome(NoiseBiome(x, y));

        public Biome BiomeAt(float x, float y) => ThemeAt(x, y).biome;

        /// <summary>Custom-mode biomes: temperature / moisture noise (harsh biomes farther out).</summary>
        Biome NoiseBiome(float x, float y)
        {
            float d = new Vector2(x, y).magnitude;
            if (d < 40) return Biome.Meadow;     // starting area is always meadow
            float t = Noise.Fractal(x, y, seed + 11, 0.006f, 2);   // temperature
            float m = Noise.Fractal(x, y, seed + 23, 0.007f, 2);   // moisture
            float far = Mathf.Clamp01((d - 40) / 300f);
            if (t < 0.36f) return Biome.Tundra;
            if (t > 0.66f) return far > 0.35f && m < 0.45f ? Biome.Ashland : Biome.Dunes;
            if (m > 0.6f) return Biome.Marsh;
            if (m > 0.47f) return Biome.Forest;
            return Biome.Meadow;
        }

        // ───────────── Ground ─────────────

        public Ground GroundAt(int tx, int ty)
        {
            float x = tx + 0.5f, y = ty + 0.5f;
            var pos = new Vector2(x, y);
            float dTown = pos.magnitude;

            // Town plaza
            if (dTown < TownRadius)
            {
                if (Mathf.Abs(x) < 2.2f || Mathf.Abs(y) < 2.2f) return Ground.Plaza;
                if (dTown < 5.5f) return Ground.Plaza;
                return Ground.Grass;
            }

            // Ground around structures (landmarks and border sites first: they were placed before the terrain)
            foreach (var s in StructuresNear(pos, 24))
            {
                float d = Vector2.Distance(pos, s.center);
                if (d > s.radius) continue;
                switch (s.kind)
                {
                    case StructKind.Landmark:
                    case StructKind.Border:
                    {
                        // An arena paved in patches with the theme's ground (both themes at a border site)
                        var t = s.kind == StructKind.Border && Hash.F(tx / 3, ty / 3, seed + 5) > 0.5f ? Zones.themes[s.zone2] : s.theme;
                        if (d < s.radius * 0.8f && Noise.Perlin(x, y, seed + 6, 0.18f) > 0.42f) return t.arenaGround;
                        return Noise.Perlin(x, y, seed + 7, 0.12f) > 1 - t.altShare ? t.groundAlt : t.ground;
                    }
                    case StructKind.Camp: if (d < s.radius * 0.8f) return Ground.Path; break;
                    case StructKind.Ruins: if (Hash.F(tx, ty, seed + 3) > 0.35f && d < s.radius * 0.85f) return Ground.Plaza; break;
                    case StructKind.Shrine: if (d < 3.2f) return Ground.Plaza; break;
                    case StructKind.Graveyard: if (d < s.radius * 0.9f) return Ground.DarkGrass; break;
                    case StructKind.Pond: if (d < s.radius * 0.7f) return d < s.radius * 0.4f ? Ground.DeepWater : Ground.Water; break;
                    case StructKind.Lair: if (d < s.radius * 0.9f) return Ground.Ash; break;
                    case StructKind.Ferry: if (d < 2.2f) return Ground.Path; break;
                }
            }

            var theme = ThemeAt(x, y);
            bool nearTown = dTown < TownRadius + 6;

            // Mountain ranges and mesas
            int h = nearTown ? 0 : Terrain.Height(tx, ty);
            if (h > 0) return h == 2 ? Ground.Peak : Ground.Cliff;

            // Rivers and lakes, filled with the theme's liquid; roads and the hidden trails bridge them
            int water = nearTown ? 0 : Terrain.Water(pos);
            bool road = Terrain.OnRoad(pos);
            if (water > 0)
            {
                if (!IsLiquid(theme.liquid)) return theme.liquid;      // frozen: walk across, no bridge
                if (road || OnTrail(tx, ty)) return Ground.Wood;
                if (theme.liquid == Ground.Water) return water == 2 ? Ground.DeepWater : Ground.Water;
                return theme.liquid;
            }
            if (road) return theme.road;

            // A designed world can lay its own roads / lanes / paving over open land (never right next to the town)
            var open = OpenGround(theme, x, y, pos);
            return nearTown ? open : DesignLayout.Ground(tx, ty, open, seed);
        }

        Ground OpenGround(ThemeDef t, float x, float y, Vector2 pos)
        {
            if (t.liquid == Ground.Water && t.ground == Ground.Grass && Terrain.Shore(pos)) return Ground.Sand;   // sandy lake shores
            float detail = Noise.Perlin(x, y, seed + 91, 0.12f);
            return detail > 1 - t.altShare ? t.groundAlt : t.ground;
        }

        public static bool IsWater(Ground g) => g == Ground.Water || g == Ground.DeepWater;
        /// <summary>Rivers, lakes, lava, swamp water: impassable (ice is walkable).</summary>
        public static bool IsLiquid(Ground g) => g is Ground.Water or Ground.DeepWater or Ground.Lava or Ground.Toxic;
        /// <summary>Cliffs: the ground stands 1 (Cliff) or 2 (Peak) levels high and blocks movement and shots.</summary>
        public static int Level(Ground g) => g == Ground.Peak ? 2 : g == Ground.Cliff ? 1 : 0;
        public static bool Impassable(Ground g) => IsLiquid(g) || Level(g) > 0;
        /// <summary>Height of one cliff level on screen, in tiles (the 3/4 view).</summary>
        public const float FaceHeight = 1f;          // one level = one tile (16 dots, TileBaker)

        /// <summary>
        /// Invisible north-south / east-west trails, one every TrailSpacing tiles (offset per line).
        /// They only show up where they cross a river — as a 3-wide bridge — so crossings are sparse but reliable.
        /// </summary>
        bool OnTrail(int tx, int ty)
        {
            int cx = MathX.FloorDiv(tx, TrailSpacing), cy = MathX.FloorDiv(ty, TrailSpacing);
            int lineX = cx * TrailSpacing + 20 + (int)(Hash.U(cx, 0, seed + 601) % (TrailSpacing - 40));
            int lineY = cy * TrailSpacing + 20 + (int)(Hash.U(0, cy, seed + 607) % (TrailSpacing - 40));
            return Mathf.Abs(tx - lineX) <= 1 || Mathf.Abs(ty - lineY) <= 1;
        }

        /// <summary>Any water within r tiles (used to place ferrymen by rivers).</summary>
        bool WaterNear(Vector2 c, int r)
        {
            for (int y = -r; y <= r; y += 2)
                for (int x = -r; x <= r; x += 2)
                    if (Terrain.Water(c + new Vector2(x, y)) > 0) return true;
            return false;
        }

        /// <summary>Open, dry, flat land within r of c (structures need it).</summary>
        bool OpenLand(Vector2 c, float r)
        {
            for (int i = 0; i < 9; i++)
            {
                var p = i == 0 ? c : c + MathX.Dir(i * 45) * r;
                if (Terrain.Height(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)) > 0 || Terrain.Water(p) > 0) return false;
            }
            return true;
        }

        // ───────────── Structures ─────────────

        public List<StructureSpec> StructuresInRegion(int rx, int ry)
        {
            var key = new Vector2Int(rx, ry);
            if (regionCache.TryGetValue(key, out var list)) return list;
            list = new List<StructureSpec>();
            regionCache[key] = list;

            float regionSize = Chunk * RegionChunks;
            Vector2 regionMin = new Vector2(rx * regionSize, ry * regionSize);

            // Fixed structures: the town, then the zones' landmarks and border sites (placed before everything else)
            if (InRegion(Vector2.zero, regionMin, regionSize))
                list.Add(new StructureSpec { id = "town", kind = StructKind.Town, center = Vector2.zero, radius = TownRadius, name = "기억의 광장", landmark = true, theme = DB.SpawnTheme });
            if (Zones != null)
                foreach (var s in Zones.sites)
                    if (InRegion(s.center, regionMin, regionSize))
                    {
                        s.biome = s.theme.biome;
                        s.tier = Mathf.RoundToInt(TierAt(s.center.magnitude));
                        list.Add(s);
                    }

            var rng = new Rng(Hash.U(rx, ry, seed + 1009));
            // 1–2 candidates per region, on open dry land away from the landmarks
            int count = rng.Chance(0.55f) ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                var c = regionMin + new Vector2(rng.Range(8f, regionSize - 8f), rng.Range(8f, regionSize - 8f));
                if (c.magnitude < TownRadius + 14) continue;
                bool overlaps = false;
                foreach (var o in list) if (Vector2.Distance(o.center, c) < o.radius + 12) overlaps = true;
                if (Zones != null) foreach (var o in Zones.sites) if (Vector2.Distance(o.center, c) < o.radius + 16) overlaps = true;
                if (overlaps || !OpenLand(c, 6)) continue;

                var theme = ThemeAt(c.x, c.y);
                float dist = c.magnitude;
                var spec = new StructureSpec
                {
                    id = $"s{rx}_{ry}_{i}", center = c, theme = theme, biome = theme.biome, tier = Mathf.RoundToInt(TierAt(dist)),
                };
                spec.kind = PickKind(rng, theme.biome, dist);
                if (spec.kind == StructKind.Wanderer && WaterNear(c, 9)) spec.kind = StructKind.Ferry;
                FillSpec(spec, rng);
                list.Add(spec);
            }
            return list;
        }

        static bool InRegion(Vector2 p, Vector2 min, float size) =>
            p.x >= min.x && p.y >= min.y && p.x < min.x + size && p.y < min.y + size;

        StructKind PickKind(Rng rng, Biome b, float dist)
        {
            var options = new List<(StructKind k, float w)>
            {
                (StructKind.Camp, 2.0f), (StructKind.Ruins, 1.6f), (StructKind.Shrine, 1.0f), (StructKind.Chest, 1.4f),
                (StructKind.Grove, 1.2f), (StructKind.Pond, 0.8f), (StructKind.Wanderer, 0.8f),
                (StructKind.Graveyard, b == Biome.Marsh || b == Biome.Ashland ? 1.4f : 0.5f),
                (StructKind.Lair, dist > 90 ? 0.9f : 0.15f),
            };
            return rng.Weighted(options, o => o.w).k;
        }

        void FillSpec(StructureSpec s, Rng rng)
        {
            switch (s.kind)
            {
                case StructKind.Camp: s.radius = 7; s.name = "여행자 야영지"; s.landmark = true; break;
                case StructKind.Ruins: s.radius = 9; s.name = rng.Pick(new[] { "잊힌 신전 터", "무너진 성채", "이름 없는 유적" }); s.landmark = true; break;
                case StructKind.Shrine: s.radius = 4; s.name = "기억의 제단"; s.landmark = true; break;
                case StructKind.Chest: s.radius = 3; s.name = "숨겨진 상자"; break;
                case StructKind.Grove: s.radius = 7; s.name = "고요한 숲"; break;
                case StructKind.Pond: s.radius = 6; s.name = "작은 호수"; break;
                case StructKind.Wanderer: s.radius = 3; s.name = "방랑자"; s.landmark = true; break;
                case StructKind.Ferry: s.radius = 3; s.name = "뱃사공의 오두막"; s.landmark = true; break;
                case StructKind.Graveyard: s.radius = 7; s.name = "옛 묘지"; s.landmark = true; break;
                case StructKind.Lair:
                    // A lone boss in its den (the zone theme's boss)
                    s.radius = 10; s.landmark = true;
                    s.boss = s.theme.boss;
                    s.name = s.boss == "boss_fox" ? "붉은 꼬리의 소굴" : s.boss == "boss_knight" ? "망각의 보루" : "버섯왕의 고목";
                    break;
            }
        }

        public IEnumerable<StructureSpec> StructuresNear(Vector2 p, float range)
        {
            float regionSize = Chunk * RegionChunks;
            int rx0 = Mathf.FloorToInt((p.x - range) / regionSize), rx1 = Mathf.FloorToInt((p.x + range) / regionSize);
            int ry0 = Mathf.FloorToInt((p.y - range) / regionSize), ry1 = Mathf.FloorToInt((p.y + range) / regionSize);
            for (int ry = ry0; ry <= ry1; ry++)
                for (int rx = rx0; rx <= rx1; rx++)
                    foreach (var s in StructuresInRegion(rx, ry))
                        if (Vector2.Distance(s.center, p) <= range + s.radius) yield return s;
        }

        public IEnumerable<StructureSpec> StructuresInChunk(int cx, int cy)
        {
            float regionSize = Chunk * RegionChunks;
            var min = new Vector2(cx * Chunk, cy * Chunk);
            int rx = Mathf.FloorToInt(min.x / regionSize), ry = Mathf.FloorToInt(min.y / regionSize);
            foreach (var s in StructuresInRegion(rx, ry))
                if (s.center.x >= min.x && s.center.y >= min.y && s.center.x < min.x + Chunk && s.center.y < min.y + Chunk)
                    yield return s;
        }

        /// <summary>Find the nearest structure of a given kind (used for quest target selection).</summary>
        public StructureSpec FindNearest(Vector2 from, StructKind kind, float minDist, float maxDist, System.Func<StructureSpec, bool> filter = null)
        {
            StructureSpec best = null; float bd = float.MaxValue;
            float regionSize = Chunk * RegionChunks;
            int r = Mathf.CeilToInt(maxDist / regionSize);
            int crx = Mathf.FloorToInt(from.x / regionSize), cry = Mathf.FloorToInt(from.y / regionSize);
            for (int ry = cry - r; ry <= cry + r; ry++)
                for (int rx = crx - r; rx <= crx + r; rx++)
                    foreach (var s in StructuresInRegion(rx, ry))
                    {
                        if (s.kind != kind) continue;
                        float d = Vector2.Distance(from, s.center);
                        if (d < minDist || d > maxDist || d >= bd) continue;
                        if (filter != null && !filter(s)) continue;
                        best = s; bd = d;
                    }
            return best;
        }

        public string RegionName(Vector2 p)
        {
            if (p.magnitude < TownRadius + 4) return "기억의 광장";
            return Zones != null ? Zones.RegionName(p) : ThemeAt(p.x, p.y).displayName;
        }
    }
}
