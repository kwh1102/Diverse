using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum StructKind { Town, Tower, Camp, Ruins, Shrine, Grove, Graveyard, Lair, Chest, Pond, Wanderer, Ferry, Obelisk, Fortress, ZoneBoss, BorderBoss, Keep, Village }

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
        public ZoneDef zone, zone2;    // zone bosses: their zone (border mid-bosses: the two zones they stand between)
        public SiteSpec site;          // keeps / villages: the tile layout (Landforms)
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
        const float OpenHome = 62f;             // no mountains or walls this close to the spawn (the home fields stay open)

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
        /// <summary>Normal mode: the five zones (biomes, monsters, bosses). Null = the classic noise-biome world (custom mode).</summary>
        public readonly ZoneMap Zones;
        /// <summary>A zone's deep layer: no tower, the whole world is that zone.</summary>
        public bool Deep => Zones != null && Zones.deep;
        readonly Dictionary<Vector2Int, List<StructureSpec>> regionCache = new Dictionary<Vector2Int, List<StructureSpec>>();
        /// <summary>Mountains, rivers, lakes, keeps: the large landforms (pure functions of the seed and world coordinates).</summary>
        public readonly Landforms Land;

        public WorldGen(int seed, ZoneMap zones = null)
        {
            Zones = zones;
            this.seed = zones != null ? zones.seed : seed;
            Land = new Landforms(this.seed, p => (!Deep && Vector2.Distance(p, TowerPos) < 50) || NearZoneBoss(p, 44));
        }

        // ───────────── Terrain ─────────────

        public Biome BiomeAt(float x, float y)
        {
            if (Zones != null) return Zones.BiomeAt(x, y);
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

        /// <summary>
        /// The ground of a chunk with a 1-tile border, as the streamer shows it. The raw per-tile ground is computed with a
        /// 3-tile border, then rock is eroded twice (a rock tile with fewer than two rocky neighbours becomes gravel), which
        /// takes the pebbles and hairline spurs off the ranges. Each pass only reads the pass before it, so a tile's result
        /// depends on raw values within 2 tiles of it: the same in every chunk that contains it, whatever was built first.
        /// </summary>
        public Ground[,] GroundOf(Vector2Int k)
        {
            const int B = 3, Passes = 2;
            int N = Chunk, M = N + 2 * B;
            var raw = new Ground[M, M];
            for (int y = 0; y < M; y++)
                for (int x = 0; x < M; x++)
                    raw[x, y] = GroundAt(k.x * N + x - B, k.y * N + y - B);
            var cur = raw;
            for (int pass = 1; pass <= Passes; pass++)
            {
                var next = new Ground[M, M];
                for (int y = pass; y < M - pass; y++)
                    for (int x = pass; x < M - pass; x++)
                    {
                        var g = cur[x, y];
                        if (g == Ground.Rock || g == Ground.Peak)
                        {
                            int n = 0;
                            if (IsRock(cur[x - 1, y])) n++;
                            if (IsRock(cur[x + 1, y])) n++;
                            if (IsRock(cur[x, y - 1])) n++;
                            if (IsRock(cur[x, y + 1])) n++;
                            if (n < 2) g = Ground.Scree;
                        }
                        next[x, y] = g;
                    }
                cur = next;
            }
            var outTiles = new Ground[N + 2, N + 2];
            for (int y = 0; y < N + 2; y++)
                for (int x = 0; x < N + 2; x++)
                    outTiles[x, y] = cur[x + B - 1, y + B - 1];
            return outTiles;
        }

        static bool IsRock(Ground g) => g == Ground.Rock || g == Ground.Peak;

        /// <summary>The raw ground of one tile (before the rock erosion of GroundOf). Pure: the same for the same seed and tile.</summary>
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
            if (!Deep && y > 0 && y < TowerPos.y && Mathf.Abs(x - Mathf.Sin(y * 0.08f) * 3f) < 1.6f) return Ground.Path;
            if (!Deep && Vector2.Distance(pos, TowerPos) < 8) return Ground.Plaza;

            // Ground around structures (their yards stay free of mountains and water)
            bool inYard = false;
            foreach (var s in StructuresNear(pos, 20))
            {
                float d = Vector2.Distance(pos, s.center);
                if (s.kind == StructKind.Keep || s.kind == StructKind.Village) continue;   // laid out tile by tile by Landforms
                // Boss arenas get a wide open approach; other yards just stay clear of rock and water
                if (d < s.radius + (s.kind is StructKind.ZoneBoss or StructKind.BorderBoss ? 16 : 3)) inYard = true;
                if (d > s.radius) continue;
                switch (s.kind)
                {
                    case StructKind.Camp: if (d < s.radius * 0.8f) return Ground.Path; break;
                    case StructKind.Ruins: if (Hash.F(tx, ty, seed + 3) > 0.35f && d < s.radius * 0.85f) return Ground.Plaza; break;
                    case StructKind.Shrine: if (d < 3.2f) return Ground.Plaza; break;
                    case StructKind.Graveyard: if (d < s.radius * 0.9f) return ZoneGround(s, Ground.DarkGrass); break;
                    case StructKind.Pond: if (d < s.radius * 0.7f) return d < s.radius * 0.4f ? Ground.DeepWater : Ground.Water; break;
                    case StructKind.Lair: if (d < s.radius * 0.9f) return ZoneGround(s, Ground.Ash); break;
                    case StructKind.Obelisk: if (d < s.radius * 0.75f) return Ground.Plaza; break;
                    case StructKind.Fortress: if (d < s.radius * 0.9f) return Hash.F(tx, ty, seed + 4) > 0.2f ? Ground.Plaza : ZoneGround(s, Ground.Ash); break;
                    case StructKind.Ferry: if (d < 2.2f) return Ground.Path; break;
                    case StructKind.ZoneBoss: if (d < s.radius * 0.85f) return Hash.F(tx, ty, seed + 6) > 0.25f ? Ground.Plaza : BossGround(s.zone); break;
                    case StructKind.BorderBoss: if (d < s.radius * 0.8f) return Hash.F(tx, ty, seed + 7) > 0.5f ? BossGround(s.zone) : BossGround(s.zone2); break;
                }
            }

            // Large landforms: mountains, rivers, lakes, keeps. The home fields, the tower and every yard stay open and dry.
            bool nearTown = dTown < TownRadius + 6;
            bool nearTower = !Deep && Vector2.Distance(pos, TowerPos) < 24;
            var b = BiomeAt(x, y);
            var land = Land.At(tx, ty, b, relief: dTown >= OpenHome && !nearTower && !inYard, water: !nearTown && !nearTower && !inYard);
            switch (land.feature)
            {
                case Feature.Mountain: return Ground.Rock;
                case Feature.Peak: return Ground.Peak;
                case Feature.Wall: return Ground.Wall;
                case Feature.Scree: case Feature.Canyon: return Ground.Scree;
                case Feature.River: case Feature.Lake: return Ground.Water;
                case Feature.DeepRiver: return Ground.DeepWater;
                case Feature.Ford: return Ground.Ford;
                case Feature.Bridge: return Ground.Wood;
                case Feature.Causeway: case Feature.Road: return Ground.Path;
                case Feature.Rubble: return Ground.Rubble;
                case Feature.Court: return Ground.Plaza;
                case Feature.Wetland: return Noise.Perlin(x, y, seed + 93, 0.1f) > 0.55f ? Ground.DarkGrass : Ground.Swamp;
            }

            // A designed world can lay its own roads / lanes / paving over open land (never right next to the town)
            var open = OpenGround(x, y, b, land.lake, land.forest);
            return nearTown ? open : DesignLayout.Ground(tx, ty, open, seed);
        }

        Ground OpenGround(float x, float y, Biome b, float water, bool forest)
        {
            float detail = Noise.Perlin(x, y, seed + 91, 0.12f);
            if (forest && (b == Biome.Meadow || b == Biome.Forest)) return detail > 0.8f ? Ground.Grass : Ground.DarkGrass;   // the floor of a deep wood
            // Zone worlds: every zone lays its own pair of grounds (main, with accent patches)
            if (Zones != null && b != Biome.Meadow && Zones.ByBiome(b) is { } z)
                return detail > 0.6f ? z.art.groundAccent : z.art.ground;
            switch (b)
            {
                case Biome.Forest: return detail > 0.55f ? Ground.DarkGrass : Ground.Grass;
                case Biome.Dunes: return Ground.Sand;
                case Biome.Tundra: return detail > 0.7f ? Ground.Grass : Ground.Snow;
                case Biome.Marsh: return detail > 0.6f ? Ground.DarkGrass : Ground.Swamp;
                case Biome.Ashland: return Ground.Ash;
                case Biome.Volcano: return detail > 0.68f ? Ground.Ash : Ground.Scorch;
                case Biome.Starlit: return detail > 0.64f ? Ground.DarkGrass : Ground.Starstone;
                case Biome.Hive: return detail > 0.66f ? Ground.Sand : Ground.Hive;
                default:
                    if (water > 0.66f) return Ground.Sand;   // sandy shore
                    return detail > 0.62f ? Ground.DarkGrass : Ground.Grass;
            }
        }

        /// <summary>The ground a boss arena is paved with: its zone's plain ground.</summary>
        /// <summary>Ground of a camp-like site: in a zone world its zone's accent ground, else the classic one.</summary>
        Ground ZoneGround(StructureSpec s, Ground classic) => Zones?.ByBiome(s.biome) is { } z ? z.art.groundAccent : classic;

        static Ground BossGround(ZoneDef z) => z == null ? Ground.Ash : z.art.groundAccent;

        /// <summary>Water that needs a raft (a ford is wadable).</summary>
        public static bool IsWater(Ground g) => g == Ground.Water || g == Ground.DeepWater;

        /// <summary>Ground nothing walks through or shoots across: mountain rock, crests, walls.</summary>
        public static bool IsSolid(Ground g) => Art.IsTall(g);

        /// <summary>What large landform the tile lies on (scenery rules, region names).</summary>
        public Feature FeatureAt(int tx, int ty)
        {
            var p = new Vector2(tx + 0.5f, ty + 0.5f);
            return Land.At(tx, ty, BiomeAt(p.x, p.y), relief: p.magnitude >= OpenHome).feature;
        }

        /// <summary>Inside a dense forest (ChunkView plants it thick, with clearings)?</summary>
        public bool ForestAt(int tx, int ty, Biome b) => Land.At(tx, ty, b).forest;

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
            if (!Deep && InRegion(TowerPos, regionMin, regionSize))
                list.Add(new StructureSpec { id = "tower", kind = StructKind.Tower, center = TowerPos, radius = 8, name = "기억의 탑", landmark = true, boss = "boss_knight", tier = 3 });
            if (Zones != null)
                foreach (var b in Zones.bosses)
                    if (InRegion(b.center, regionMin, regionSize))
                    {
                        b.biome = BiomeAt(b.center.x, b.center.y);
                        b.tier = Mathf.RoundToInt(TierAt(b.center.magnitude) + (Deep ? DB.ZoneRules.deepTierBonus : 0));
                        b.enemy = Zones.PickEnemy(b.center, new Rng(Hash.Str(b.id)));
                        b.enemyCount = b.kind == StructKind.ZoneBoss ? 4 + b.tier / 2 : 2 + b.tier / 3;
                        list.Add(b);
                    }
            string idPre = Deep ? $"L{Zones.layerKey}:" : "";
            // Keeps and villages of the trail network whose centre lies in this region
            foreach (var site in Land.SitesNear(regionMin + Vector2.one * regionSize / 2, regionSize))
                if (InRegion(site.Center, regionMin, regionSize))
                {
                    var biome = BiomeAt(site.Center.x, site.Center.y);
                    int tier = Mathf.RoundToInt(TierAt(site.Center.magnitude) + (Deep ? DB.ZoneRules.deepTierBonus : 0));
                    var spec = new StructureSpec
                    {
                        id = idPre + site.id, kind = site.kind == SiteKind.Keep ? StructKind.Keep : StructKind.Village,
                        center = site.Center, radius = site.Radius, biome = biome, tier = tier, site = site, name = site.name, landmark = true,
                    };
                    FillSpec(spec, new Rng(site.seed));
                    list.Add(spec);
                }

            var rng = new Rng(Hash.U(rx, ry, seed + 1009));
            // 1–2 candidates per region
            int count = rng.Chance(0.55f) ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                var c = regionMin + new Vector2(rng.Range(8f, regionSize - 8f), rng.Range(8f, regionSize - 8f));
                if (c.magnitude < TownRadius + 14) continue;
                if (!Deep && Vector2.Distance(c, TowerPos) < 18) continue;
                if (!Deep && c.y > 0 && c.y < TowerPos.y && Mathf.Abs(c.x) < 8) continue;   // keep the road clear
                if (NearZoneBoss(c, 26)) continue;
                bool overlaps = false;
                foreach (var o in list) if (Vector2.Distance(o.center, c) < o.radius + 12) overlaps = true;
                foreach (var o in Land.SitesNear(c, 14)) if (Vector2.Distance(o.Center, c) < o.Radius + 14) overlaps = true;
                if (overlaps) continue;

                var biome = BiomeAt(c.x, c.y);
                // Not inside a range or a lake: the yard is kept open, but a camp walled in by mountains could not be reached
                if (Land.OpenAround(c, 13, biome) < 0.6f || Land.OpenAround(c, 20, biome) < 0.5f || !Land.CanLeave(c, biome)) continue;
                float dist = c.magnitude;
                int tier = Mathf.RoundToInt(TierAt(dist) + (Deep ? DB.ZoneRules.deepTierBonus : 0));
                var spec = new StructureSpec
                {
                    id = $"{idPre}s{rx}_{ry}_{i}", center = c, biome = biome, tier = tier,
                };
                spec.kind = PickKind(rng, biome, dist);
                if (spec.kind == StructKind.Wanderer && Land.WaterNear(c, 9, biome)) spec.kind = StructKind.Ferry;
                FillSpec(spec, rng);
                list.Add(spec);
            }
            return list;
        }

        bool NearZoneBoss(Vector2 c, float pad)
        {
            if (Zones == null) return false;
            foreach (var b in Zones.bosses) if (Vector2.Distance(b.center, c) < b.radius + pad) return true;
            return false;
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
                Biome.Marsh => new[] { "frog", "jelly", "wisp", "shroom" },
                Biome.Ashland => new[] { "wisp", "boar", "bat" },
                _ => new[] { "shroom", "jelly", "fox", "frog" },
            };
            s.enemy = Zones != null ? Zones.PickEnemy(s.center, rng) : rng.Pick(pool);
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
                case StructKind.Graveyard: s.radius = 7; if (Zones == null) s.enemy = "wisp"; s.enemyCount = 3 + s.tier; s.name = "옛 묘지"; s.landmark = true; break;
                case StructKind.Lair:
                    s.radius = 10; s.enemyCount = 3 + s.tier; s.landmark = true;
                    s.boss = s.biome == Biome.Forest || s.biome == Biome.Meadow ? "boss_fox" : "boss_shroom";
                    s.name = s.boss == "boss_fox" ? "붉은 꼬리의 소굴" : "버섯왕의 고목";
                    break;
                case StructKind.Keep:
                    // A garrison holds the yard; its captain waits in the inner keep
                    s.enemyCount = 7 + s.tier + rng.Range(0, 3);
                    s.boss = "boss_knight";
                    break;
                case StructKind.Village:
                    s.enemyCount = 5 + s.tier + rng.Range(0, 3);
                    break;
            }
            // Zone worlds: a site's boss is a captain of its zone's master (same body; World.SpawnBoss makes it smaller in name and HP)
            if (Zones != null && s.boss != null && (Zones.ByBiome(s.biome) ?? Zones.MainZone(s.center)) is { } home)
            {
                s.boss = home.boss;
                if (s.kind == StructKind.Lair) s.name = $"{home.prefix} 우두머리의 소굴";
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
            if (!Deep && Vector2.Distance(p, TowerPos) < 14) return "기억의 탑";
            var site = Land.SiteCovering(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
            if (site != null) return site.name;
            if (Zones != null) return p.magnitude < DB.ZoneRules.centerRadius ? (Deep ? Zones.zones[0].deepName + " 입구" : "바람결 초원") : Zones.RegionName(p);
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
