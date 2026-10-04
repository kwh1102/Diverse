using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum StructKind { Town, Tower, Camp, Ruins, Shrine, Grove, Graveyard, Lair, Chest, Pond, Wanderer }

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
                }
            }

            // Rivers / lakes (avoid near the town and on roads)
            float water = Noise.Fractal(x, y, seed + 51, 0.018f, 3);
            float river = Mathf.Abs(Noise.Fractal(x, y, seed + 77, 0.006f, 2) - 0.5f);
            bool nearTown = dTown < TownRadius + 6;
            if (!nearTown && (water > 0.72f || river < 0.012f)) return water > 0.78f ? Ground.DeepWater : Ground.Water;

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
                int tier = Mathf.Clamp((int)(dist / 70f), 0, 6);
                var spec = new StructureSpec
                {
                    id = $"s{rx}_{ry}_{i}", center = c, biome = biome, tier = tier,
                };
                spec.kind = PickKind(rng, biome, dist);
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
