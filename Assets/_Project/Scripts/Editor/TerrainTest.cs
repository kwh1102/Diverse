using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Edit-mode checks of the large landforms (Landforms / WorldGen), no play mode needed:
    /// - determinism: two generators of the same seed lay the same tiles; chunk order never matters
    /// - continuity: rivers and ranges carry on across chunk borders (no lone water / rock tiles)
    /// - connectivity: from the spawn, how much of the open ground within ±W tiles can be walked to, and whether every
    ///   structure's yard can be reached (no camp walled in by mountains or water)
    /// - variety and sanity: shares of rock / water / wall per seed, at least one keep within reach
    /// - cost: ms per chunk of ground
    /// Also paints an overview PNG per seed (1 px = 1 tile) into Logs/Terrain for a look at the macro layout.
    /// Batch: Unity -batchmode -nographics -executeMethod Diverse.EditorTools.TerrainTest.RunBatch (exit code 0 = pass)
    /// </summary>
    public static class TerrainTest
    {
        static string OutDir => Path.GetFullPath("Logs/Terrain");
        const int Window = 384;          // tiles each way from the spawn that the checks cover

        [MenuItem("Diverse/지형 생성 검증 (Logs/Terrain)", priority = 27)]
        public static void RunMenu() { bool ok = Run(); EditorUtility.DisplayDialog("지형 생성 검증", (ok ? "통과" : "실패") + "\n" + OutDir, "확인"); }

        public static void RunBatch()
        {
            bool ok = Run();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Run()
        {
            Directory.CreateDirectory(OutDir);
            var log = new StringBuilder();
            bool ok = true;
            void Check(bool cond, string msg) { log.AppendLine((cond ? "  ok   " : "  FAIL ") + msg); if (!cond) ok = false; }

            int[] seeds = { 12345, 7, 2026, 99991, 31337, 4242 };
            var zoneShares = new List<float>();
            foreach (int seed in seeds)
                foreach (bool zoneWorld in new[] { true, false })
                {
                    if (zoneWorld && DB.Zones.Count == 0) continue;
                    var gen = Make(seed, zoneWorld);
                    string tag = $"seed {seed} {(zoneWorld ? "zones" : "classic")}";
                    log.AppendLine($"[{tag}]");

                    // Determinism and chunk-order independence: 40 chunks, scattered order vs sorted, two generators
                    var keys = new List<Vector2Int>();
                    var rng = new Rng((uint)seed * 3 + 1);
                    for (int i = 0; i < 40; i++) keys.Add(new Vector2Int(rng.Range(-20, 21), rng.Range(-20, 21)));
                    var a = new Dictionary<Vector2Int, Ground[]>();
                    foreach (var k in keys) a[k] = Chunk(gen, k);
                    var gen2 = Make(seed, zoneWorld);
                    var sorted = keys.OrderBy(k => k.y).ThenBy(k => k.x).ToList();
                    bool same = true;
                    foreach (var k in sorted) if (!Chunk(gen2, k).SequenceEqual(a[k])) same = false;
                    Check(same, "same seed, other chunk order → same tiles");
                    var specsA = gen.StructuresNear(Vector2.zero, 300).Select(s => s.id + "@" + s.center + s.kind).OrderBy(x => x).ToList();
                    var specsB = gen2.StructuresNear(Vector2.zero, 300).Select(s => s.id + "@" + s.center + s.kind).OrderBy(x => x).ToList();
                    Check(specsA.SequenceEqual(specsB), $"same structures ({specsA.Count})");

                    // The window around the spawn: tiles, stats, continuity, connectivity
                    int W = Window, S = W * 2;
                    var tiles = new Ground[S * S];
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int N = WorldGen.Chunk;
                    for (int cy = -W / N; cy < W / N; cy++)
                        for (int cx = -W / N; cx < W / N; cx++)
                        {
                            var ch = gen.GroundOf(new Vector2Int(cx, cy));
                            for (int y = 0; y < N; y++)
                                for (int x = 0; x < N; x++)
                                    tiles[(cy * N + y + W) * S + cx * N + x + W] = ch[x + 1, y + 1];
                        }
                    sw.Stop();
                    float msPerChunk = (float)sw.Elapsed.TotalMilliseconds / (S * S / 256f);
                    log.AppendLine($"  ground: {msPerChunk:0.00} ms per chunk (16x16)");
                    Check(msPerChunk < 6f, "ground cost under 6 ms per chunk");

                    int rock = 0, water = 0, wall = 0, bridge = 0, ford = 0, canyon = 0, lone = 0, waterLone = 0;
                    for (int y = 1; y < S - 1; y++)
                        for (int x = 1; x < S - 1; x++)
                        {
                            var g = tiles[y * S + x];
                            if (g == Ground.Rock || g == Ground.Peak) rock++;
                            else if (WorldGen.IsWater(g)) water++;
                            else if (g == Ground.Wall) wall++;
                            else if (g == Ground.Wood) bridge++;
                            else if (g == Ground.Ford) ford++;
                            if (g == Ground.Scree) canyon++;
                            // A rock / water tile with no like neighbour is a speck, never part of a range or river
                            if (g == Ground.Rock || g == Ground.Peak)
                            {
                                bool any = false;
                                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { var n = tiles[(y + d.Item2) * S + x + d.Item1]; if (n == Ground.Rock || n == Ground.Peak) any = true; }
                                if (!any) lone++;
                            }
                            if (WorldGen.IsWater(g))
                            {
                                bool any = false;
                                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { var n = tiles[(y + d.Item2) * S + x + d.Item1]; if (WorldGen.IsWater(n) || n == Ground.Wood || n == Ground.Ford) any = true; }
                                if (!any) waterLone++;
                            }
                        }
                    float total = S * S;
                    log.AppendLine($"  rock {rock / total:P1}  water {water / total:P1}  wall {wall / total:P2}  scree/canyon {canyon / total:P1}  bridges {bridge} tiles  fords {ford} tiles");
                    Check(rock / total > 0.015f && rock / total < 0.2f, "mountains cover 1.5–20% of the land");
                    Check(water / total > 0.01f && water / total < 0.15f, "water covers 1–15% of the land");
                    Check(lone <= 2, $"at most 2 lone rock tiles ({lone})");
                    Check(waterLone < 40, $"few lone water tiles ({waterLone})");
                    Check(bridge > 0, "rivers get bridges");

                    // Flood fill on the ground alone (scenery ignored) from the spawn
                    var seen = new bool[S * S];
                    var queue = new Queue<int>();
                    int start = W * S + W;
                    seen[start] = true; queue.Enqueue(start);
                    int reached = 0, open = 0;
                    for (int i = 0; i < S * S; i++) if (Open(tiles[i])) open++;
                    while (queue.Count > 0)
                    {
                        int i = queue.Dequeue(); reached++;
                        int x = i % S, y = i / S;
                        foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = x + d.Item1, ny = y + d.Item2;
                            if (nx < 0 || ny < 0 || nx >= S || ny >= S) continue;
                            int j = ny * S + nx;
                            if (seen[j] || !Open(tiles[j])) continue;
                            seen[j] = true; queue.Enqueue(j);
                        }
                    }
                    float share = reached / (float)open;
                    // The pockets left over: how many, and the biggest (islands inside river loops, hollows between ranges)
                    int pockets = 0, biggest = 0;
                    var seen2 = (bool[])seen.Clone();
                    for (int i0 = 0; i0 < S * S; i0++)
                    {
                        if (seen2[i0] || !Open(tiles[i0])) continue;
                        int size = 0;
                        seen2[i0] = true; queue.Enqueue(i0);
                        while (queue.Count > 0)
                        {
                            int i = queue.Dequeue(); size++;
                            int x = i % S, y = i / S;
                            foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                int nx = x + d.Item1, ny = y + d.Item2;
                                if (nx < 0 || ny < 0 || nx >= S || ny >= S) continue;
                                int j = ny * S + nx;
                                if (seen2[j] || !Open(tiles[j])) continue;
                                seen2[j] = true; queue.Enqueue(j);
                            }
                        }
                        // Pockets touching the window edge may just continue outside it
                        pockets++; if (size > biggest) biggest = size;
                    }
                    log.AppendLine($"  reachable open ground: {share:P2} ({reached}/{open}); {pockets} cut-off pockets, biggest {biggest} tiles");
                    Check(share > 0.97f, "at least 97% of the open ground connects to the spawn");

                    // Every structure (camps, keeps, bosses…) inside the window, except water-only ponds: reachable yard
                    int sites = 0, keeps = 0, villages = 0, unreachable = 0;
                    var names = new List<string>();
                    foreach (var s in gen.StructuresNear(Vector2.zero, W - 40))
                    {
                        if (s.kind == StructKind.Pond || s.kind == StructKind.Town) continue;
                        if (Mathf.Abs(s.center.x) > W - 30 || Mathf.Abs(s.center.y) > W - 30) continue;
                        sites++;
                        if (s.kind == StructKind.Keep) keeps++;
                        if (s.kind == StructKind.Village) villages++;
                        bool ok1 = false;
                        for (int dy = -3; dy <= 3 && !ok1; dy++)
                            for (int dx = -3; dx <= 3 && !ok1; dx++)
                            {
                                int x = Mathf.FloorToInt(s.center.x) + dx + W, y = Mathf.FloorToInt(s.center.y) + dy + W;
                                if (x >= 0 && y >= 0 && x < S && y < S && seen[y * S + x]) ok1 = true;
                            }
                        if (!ok1) { unreachable++; names.Add($"{s.kind} {s.id} at {s.center}"); }
                    }
                    log.AppendLine($"  structures {sites} (keeps {keeps}, villages {villages}), unreachable {unreachable}" + (names.Count > 0 ? ": " + string.Join(", ", names) : ""));
                    Check(unreachable == 0, "every structure can be reached from the spawn");
                    Check(keeps + villages >= 1, "a keep or village lies within the window");
                    zoneShares.Add(rock / total);

                    Overview(tiles, S, gen, Path.Combine(OutDir, $"map_{seed}_{(zoneWorld ? "zones" : "classic")}.png"));
                }
            // Seeds differ from each other (the share of rock alone should not be a constant)
            Check(zoneShares.Max() - zoneShares.Min() > 0.01f, "seeds give different amounts of mountain");

            string report = (ok ? "PASS" : "FAIL") + "\n" + log;
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), report);
            Debug.Log("[TerrainTest] " + report);
            return ok;
        }

        static bool Open(Ground g) => !WorldGen.IsSolid(g) && !WorldGen.IsWater(g);

        static WorldGen Make(int seed, bool zones)
        {
            if (!zones) return new WorldGen(seed);
            var save = new WorldSave { seed = seed };
            return new WorldGen(seed, ZoneMap.ForWorld(save));
        }

        static Ground[] Chunk(WorldGen gen, Vector2Int k)
        {
            int N = WorldGen.Chunk;
            var ch = gen.GroundOf(k);
            var g = new Ground[(N + 2) * (N + 2)];
            for (int y = 0; y < N + 2; y++)
                for (int x = 0; x < N + 2; x++)
                    g[y * (N + 2) + x] = ch[x, y];
            return g;
        }

        /// <summary>1 px per tile, own colours (no runtime look needed): rock grey, crest white, water blue, walls purple, roads tan.</summary>
        static void Overview(Ground[] tiles, int S, WorldGen gen, string path)
        {
            var px = new Color32[S * S];
            for (int i = 0; i < S * S; i++) px[i] = Colour(tiles[i]);
            int W = S / 2;
            foreach (var s in gen.StructuresNear(Vector2.zero, W))
            {
                int cx = Mathf.FloorToInt(s.center.x) + W, cy = Mathf.FloorToInt(s.center.y) + W;
                Color32 c = s.kind switch
                {
                    StructKind.Keep => new Color32(255, 60, 60, 255), StructKind.Village => new Color32(255, 160, 40, 255),
                    StructKind.ZoneBoss => new Color32(255, 255, 0, 255), StructKind.Camp => new Color32(255, 90, 160, 255), _ => new Color32(255, 255, 255, 255),
                };
                int r = s.kind is StructKind.Keep or StructKind.Village or StructKind.ZoneBoss ? 3 : 1;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < S && y < S) px[y * S + x] = c;
                    }
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static Color32 Colour(Ground g) => g switch
        {
            Ground.Rock => new Color32(90, 84, 100, 255),
            Ground.Peak => new Color32(235, 232, 245, 255),
            Ground.Scree => new Color32(140, 130, 118, 255),
            Ground.Water => new Color32(70, 140, 220, 255),
            Ground.DeepWater => new Color32(45, 95, 180, 255),
            Ground.Ford => new Color32(150, 200, 240, 255),
            Ground.Wood => new Color32(190, 130, 70, 255),
            Ground.Wall => new Color32(150, 110, 200, 255),
            Ground.Rubble => new Color32(170, 160, 150, 255),
            Ground.Plaza => new Color32(205, 192, 176, 255),
            Ground.Path => new Color32(216, 180, 126, 255),
            Ground.Swamp => new Color32(100, 120, 80, 255),
            Ground.DarkGrass => new Color32(70, 140, 80, 255),
            Ground.Sand or Ground.Dune or Ground.DuneRidge => new Color32(240, 215, 154, 255),
            Ground.Snow or Ground.Glacier or Ground.Frost => new Color32(225, 235, 245, 255),
            Ground.Ash or Ground.Scorch or Ground.Magma => new Color32(110, 95, 110, 255),
            _ => new Color32(124, 197, 107, 255),
        };
    }
}
