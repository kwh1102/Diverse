using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum StructKind { Town, Tower, Camp, Ruins, Shrine, Grove, Graveyard, Lair, Chest, Pond, Wanderer, Ferry, Obelisk, Fortress }

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
        public string enemy;           // main monster species
        public int enemyCount;
        public string boss;
        public bool landmark;          // whether it shows on the map
    }

    /// <summary>
    /// Seed-based infinite world generator.
    /// - Terrain: tile type decided per tile from noise (biome, water, paths)
    /// - Structures: one candidate per region (Region = 3x3 chunks) → deterministic, never overlapping
    /// - The town (origin) and the Tower of Memory (fixed position) are always fixed structures
    /// </summary>
    public class WorldGen
    {
        public const int Chunk = 16;            // tiles per chunk
        public const int RegionChunks = 3;      // one structure candidate per region
        public const float TownRadius = 15f;
        public static readonly Vector2 TowerPos = new Vector2(0, 92);
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
        readonly Dictionary<Vector2Int, List<StructureSpec>> regionCache = new Dictionary<Vector2Int, List<StructureSpec>>();

        public WorldGen(int seed) { this.seed = seed; }

        // ───────────── Terrain ─────────────

        public Biome BiomeAt(float x, float y)
        {
            float d = new Vector2(x, y).magnitude;
            if (d < 40) return Biome.Meadow;     // starting area is always meadow
            float t = Noise.Fractal(x, y, seed + 11, 0.006f, 2);   // temperature
            float m = Noise.Fractal(x, y, seed + 23, 0.007f, 2);   // moisture
            // gradual difficulty with distance: harsh biomes appear farther out
            float far = Mathf.Clamp01((d - 40) / 300f);
            if (t < 0.36f) return Biome.Tundra;
            if (t > 0.66f) return far > 0.35f && m < 0.45f ? Biome.Ashland : Biome.Dunes;
            if (m > 0.6f) return Biome.Marsh;
            if (m > 0.47f) return Biome.Forest;
            return Biome.Meadow;
        }

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
            // Road from the town to the tower
            if (y > 0 && y < TowerPos.y && Mathf.Abs(x - Mathf.Sin(y * 0.08f) * 3f) < 1.6f) return Ground.Path;
            if (Vector2.Distance(pos, TowerPos) < 8) return Ground.Plaza;

            // Ground around structures
            foreach (var s in StructuresNear(pos, 20))
            {
                float d = Vector2.Distance(pos, s.center);
                if (d > s.radius) continue;
                switch (s.kind)
                {
                    case StructKind.Camp: if (d < s.radius * 0.8f) return Ground.Path; break;
                    case StructKind.Ruins: if (Hash.F(tx, ty, seed + 3) > 0.35f && d < s.radius * 0.85f) return Ground.Plaza; break;
                    case StructKind.Shrine: if (d < 3.2f) return Ground.Plaza; break;
                    case StructKind.Graveyard: if (d < s.radius * 0.9f) return Ground.DarkGrass; break;
                    case StructKind.Pond: if (d < s.radius * 0.7f) return d < s.radius * 0.4f ? Ground.DeepWater : Ground.Water; break;
                    case StructKind.Lair: if (d < s.radius * 0.9f) return Ground.Ash; break;
                    case StructKind.Obelisk: if (d < s.radius * 0.75f) return Ground.Plaza; break;
                    case StructKind.Fortress: if (d < s.radius * 0.9f) return Hash.F(tx, ty, seed + 4) > 0.2f ? Ground.Plaza : Ground.Ash; break;
                    case StructKind.Ferry: if (d < 2.2f) return Ground.Path; break;
                }
            }

            // Rivers / lakes (avoid near the town and on roads)
            float water = Noise.Fractal(x, y, seed + 51, 0.018f, 3);
            float river = Mathf.Abs(Noise.Fractal(x, y, seed + 77, 0.006f, 2) - 0.5f);
            bool nearTown = dTown < TownRadius + 6;
            if (!nearTown && (water > 0.72f || river < 0.012f))
            {
                // A plank bridge where a trail crosses a river (not over lakes)
                if (water <= 0.72f && OnTrail(tx, ty)) return Ground.Wood;
                return water > 0.78f ? Ground.DeepWater : Ground.Water;
            }

            var b = BiomeAt(x, y);
            float detail = Noise.Perlin(x, y, seed + 91, 0.12f);
            switch (b)
            {
                case Biome.Forest: return detail > 0.55f ? Ground.DarkGrass : Ground.Grass;
                case Biome.Dunes: return Ground.Sand;
                case Biome.Tundra: return detail > 0.7f ? Ground.Grass : Ground.Snow;
                case Biome.Marsh: return detail > 0.6f ? Ground.DarkGrass : Ground.Swamp;
                case Biome.Ashland: return Ground.Ash;
                default:
                    if (water > 0.66f) return Ground.Sand;   // sandy shore
                    return detail > 0.62f ? Ground.DarkGrass : Ground.Grass;
            }
        }

        public static bool IsWater(Ground g) => g == Ground.Water || g == Ground.DeepWater;

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
                {
                    float px = c.x + x, py = c.y + y;
                    float water = Noise.Fractal(px, py, seed + 51, 0.018f, 3);
                    float river = Mathf.Abs(Noise.Fractal(px, py, seed + 77, 0.006f, 2) - 0.5f);
                    if (water > 0.72f || river < 0.012f) return true;
                }
            return false;
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

            // Fixed structures
            if (InRegion(Vector2.zero, regionMin, regionSize))
                list.Add(new StructureSpec { id = "town", kind = StructKind.Town, center = Vector2.zero, radius = TownRadius, name = "기억의 광장", landmark = true });
            if (InRegion(TowerPos, regionMin, regionSize))
                list.Add(new StructureSpec { id = "tower", kind = StructKind.Tower, center = TowerPos, radius = 8, name = "기억의 탑", landmark = true, boss = "boss_knight", tier = 3 });

            var rng = new Rng(Hash.U(rx, ry, seed + 1009));
            // 1–2 candidates per region
            int count = rng.Chance(0.55f) ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                var c = regionMin + new Vector2(rng.Range(8f, regionSize - 8f), rng.Range(8f, regionSize - 8f));
                if (c.magnitude < TownRadius + 14) continue;
                if (Vector2.Distance(c, TowerPos) < 18) continue;
                if (c.y > 0 && c.y < TowerPos.y && Mathf.Abs(c.x) < 8) continue;   // keep the road clear
                bool overlaps = false;
                foreach (var o in list) if (Vector2.Distance(o.center, c) < o.radius + 12) overlaps = true;
                if (overlaps) continue;

                var biome = BiomeAt(c.x, c.y);
                float dist = c.magnitude;
                int tier = Mathf.RoundToInt(TierAt(dist));
                var spec = new StructureSpec
                {
                    id = $"s{rx}_{ry}_{i}", center = c, biome = biome, tier = tier,
                };
                spec.kind = PickKind(rng, biome, dist);
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
                (StructKind.Camp, 3.2f), (StructKind.Ruins, 1.6f), (StructKind.Shrine, 1.0f), (StructKind.Chest, 1.4f),
                (StructKind.Grove, 1.2f), (StructKind.Pond, 0.8f), (StructKind.Wanderer, 0.8f),
                (StructKind.Graveyard, b == Biome.Marsh || b == Biome.Ashland ? 1.4f : 0.5f),
                (StructKind.Lair, dist > 90 ? 0.9f : 0.15f),
                // Farther rings bring bigger landmarks
                (StructKind.Obelisk, dist > 140 ? 0.7f : 0f),
                (StructKind.Fortress, dist > 230 ? 0.6f + Mathf.Min(0.6f, (dist - 230) / 600f) : 0f),
            };
            return rng.Weighted(options, o => o.w).k;
        }

        void FillSpec(StructureSpec s, Rng rng)
        {
            string[] pool = s.biome switch
            {
                Biome.Forest => new[] { "fox", "shroom", "raccoon", "bat" },
                Biome.Dunes => new[] { "raccoon", "jelly", "boar" },
                Biome.Tundra => new[] { "frostling", "boar", "bat" },
                Biome.Marsh => new[] { "jelly", "wisp", "shroom" },
                Biome.Ashland => new[] { "wisp", "boar", "bat" },
                _ => new[] { "shroom", "jelly", "fox" },
            };
            s.enemy = rng.Pick(pool);
            switch (s.kind)
            {
                case StructKind.Camp:
                    s.radius = 7; s.enemyCount = 4 + s.tier + rng.Range(0, 3);
                    s.name = EnemyName(s.enemy) + " 야영지"; s.landmark = true;
                    break;
                case StructKind.Ruins:
                    s.radius = 9; s.enemyCount = 3 + s.tier; s.name = rng.Pick(new[] { "잊힌 신전 터", "무너진 성채", "이름 없는 유적" }); s.landmark = true;
                    break;
                case StructKind.Shrine: s.radius = 4; s.name = "기억의 제단"; s.landmark = true; break;
                case StructKind.Chest: s.radius = 3; s.name = "숨겨진 상자"; s.enemyCount = rng.Chance(0.5f) ? 2 : 0; break;
                case StructKind.Grove: s.radius = 7; s.name = "고요한 숲"; s.enemyCount = 0; break;
                case StructKind.Pond: s.radius = 6; s.name = "작은 호수"; break;
                case StructKind.Wanderer: s.radius = 3; s.name = "방랑자"; s.landmark = true; break;
                case StructKind.Ferry: s.radius = 3; s.name = "뱃사공의 오두막"; s.landmark = true; break;
                case StructKind.Obelisk:
                    // A guardian stands watch over the frontier: one strong boss, few minions
                    s.radius = 8; s.enemyCount = 2 + s.tier / 2; s.landmark = true;
                    s.boss = rng.Pick(new[] { "boss_fox", "boss_shroom" });
                    s.name = "경계의 오벨리스크";
                    break;
                case StructKind.Fortress:
                    s.radius = 12; s.enemyCount = 7 + s.tier; s.landmark = true;
                    s.boss = "boss_knight";
                    s.name = rng.Pick(new[] { "망각의 요새", "검은 성채", "무너진 왕의 보루" });
                    break;
                case StructKind.Graveyard: s.radius = 7; s.enemy = "wisp"; s.enemyCount = 3 + s.tier; s.name = "옛 묘지"; s.landmark = true; break;
                case StructKind.Lair:
                    s.radius = 10; s.enemyCount = 3 + s.tier; s.landmark = true;
                    s.boss = s.biome == Biome.Forest || s.biome == Biome.Meadow ? "boss_fox" : "boss_shroom";
                    s.name = s.boss == "boss_fox" ? "붉은 꼬리의 소굴" : "버섯왕의 고목";
                    break;
            }
        }

        static string EnemyName(string id) => DB.Enemy(id).name;

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
            if (Vector2.Distance(p, TowerPos) < 14) return "기억의 탑";
            var b = BiomeAt(p.x, p.y);
            return b switch
            {
                Biome.Forest => "속삭이는 숲",
                Biome.Dunes => "황금 모래 언덕",
                Biome.Tundra => "서리 고원",
                Biome.Marsh => "안개 늪",
                Biome.Ashland => "잿빛 황야",
                _ => "바람결 초원",
            };
        }
    }
}
