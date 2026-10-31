using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Debug pictures and checks of a normal-mode world, without play mode (Logs/World/):
    /// - terrain_&lt;seed&gt;.png: 1 px per tile, ground colours, cliffs shaded by height, roads, landmarks (white) and border sites (yellow)
    /// - zones_&lt;seed&gt;.png: each tile's theme colour, the zone borders' blend band darker, the spawn's land
    /// - report.txt: share of mountains / liquids, open ground reachable on foot from the spawn, every site reachable,
    ///   and the same tiles again from a fresh generator in a different order (determinism)
    /// Batch: Unity -batchmode -nographics -executeMethod Diverse.EditorTools.WorldOverview.RunBatch [-seeds 1,2,3]
    /// </summary>
    public static class WorldOverview
    {
        const int Half = 420;          // window: ±Half tiles around the spawn
        const string OutDir = "Logs/World";

        [MenuItem("Diverse/World/Overview Maps and Checks")]
        public static void RunMenu() { Run(new[] { 12345, 777, 4242 }); EditorUtility.RevealInFinder(OutDir + "/report.txt"); }

        public static void RunBatch()
        {
            var seeds = new[] { 12345, 777, 4242 };
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-seeds");
            if (i >= 0 && i + 1 < args.Length) seeds = args[i + 1].Split(',').Select(int.Parse).ToArray();
            bool ok = Run(seeds);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Run(int[] seeds)
        {
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();
            bool allOk = true;
            foreach (int seed in seeds) allOk &= Check(seed, report);
            File.WriteAllText(OutDir + "/report.txt", report.ToString());
            Debug.Log("[WorldOverview]\n" + report);
            return allOk;
        }

        static bool Check(int seed, StringBuilder report)
        {
            var zones = ZoneMap.ForWorld(null, seed);
            var gen = new WorldGen(seed, zones);
            int n = Half * 2;
            var tiles = new Ground[n, n];
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    tiles[x, y] = gen.GroundAt(x - Half, y - Half);
            double msPerChunk = t0.Elapsed.TotalMilliseconds / (n * n / 256.0);

            // Walkable like the game: no liquid, no cliff, no ground hidden behind a cliff to its south
            int hide = Mathf.CeilToInt(2 * WorldGen.FaceHeight);
            bool Walk(int x, int y)
            {
                if (WorldGen.Impassable(tiles[x, y])) return false;
                for (int m = 1; m <= hide && y - m >= 0; m++) if (Mathf.CeilToInt(WorldGen.Level(tiles[x, y - m]) * WorldGen.FaceHeight) >= m) return false;
                return true;
            }
            var reach = new bool[n, n];
            var q = new Queue<Vector2Int>();
            q.Enqueue(new Vector2Int(Half, Half)); reach[Half, Half] = true;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                {
                    var nb = c + d;
                    if (nb.x < 0 || nb.y < 0 || nb.x >= n || nb.y >= n || reach[nb.x, nb.y] || !Walk(nb.x, nb.y)) continue;
                    reach[nb.x, nb.y] = true; q.Enqueue(nb);
                }
            }
            int walk = 0, reached = 0, rock = 0, liquid = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var g = tiles[x, y];
                    if (WorldGen.Level(g) > 0) rock++;
                    if (WorldGen.IsLiquid(g)) liquid++;
                    if (Walk(x, y)) { walk++; if (reach[x, y]) reached++; }
                }

            // Every landmark / border site reachable (any walkable tile within its arena)
            var lost = new List<string>();
            foreach (var s in zones.sites)
            {
                int cx = Mathf.FloorToInt(s.center.x) + Half, cy = Mathf.FloorToInt(s.center.y) + Half;
                bool any = false;
                for (int y = cy - 6; y <= cy + 6 && !any; y++)
                    for (int x = cx - 6; x <= cx + 6 && !any; x++)
                        if (x >= 0 && y >= 0 && x < n && y < n && reach[x, y]) any = true;
                if (!any) lost.Add($"{s.name} ({s.center.x:0},{s.center.y:0})");
            }

            // Determinism: a fresh generator, tiles visited in another order
            var gen2 = new WorldGen(seed, ZoneMap.ForWorld(null, seed));
            var rnd = new System.Random(seed);
            int mismatch = 0;
            for (int i = 0; i < 4000; i++)
            {
                int x = rnd.Next(n), y = rnd.Next(n);
                if (gen2.GroundAt(x - Half, y - Half) != tiles[x, y]) mismatch++;
            }

            bool ok = lost.Count == 0 && mismatch == 0 && reached > walk * 0.9f;
            report.AppendLine($"seed {seed}  zones: {string.Join(", ", Enumerable.Range(0, ZoneMap.Count).Select(z => $"{zones.CombatName(z)}={zones.themes[z].displayName}"))}");
            report.AppendLine($"  mountains {100f * rock / (n * n):0.0}%  liquids {100f * liquid / (n * n):0.0}%  open ground reachable from spawn {100f * reached / Mathf.Max(1, walk):0.0}%");
            report.AppendLine($"  sites unreachable: {(lost.Count == 0 ? "none" : string.Join(", ", lost))}");
            report.AppendLine($"  determinism: {mismatch} / 4000 tiles differ   ground {msPerChunk:0.00} ms per chunk   {(ok ? "OK" : "FAIL")}");

            Pictures(seed, gen, zones, tiles, reach);
            return ok;
        }

        static void Pictures(int seed, WorldGen gen, ZoneMap zones, Ground[,] tiles, bool[,] reach)
        {
            int n = tiles.GetLength(0);
            var terrain = new Color32[n * n];
            var zoneImg = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var g = tiles[x, y];
                    Art.GroundColors(g, out var a, out _, out _);
                    Color c = a;
                    int lvl = WorldGen.Level(g);
                    if (lvl > 0)
                    {
                        var th = gen.ThemeAt(x - Half + 0.5f, y - Half + 0.5f);
                        c = Color.Lerp(th.mapColor, Color.gray, 0.55f) * (lvl == 2 ? 1.2f : 0.85f);
                        if (y > 0 && WorldGen.Level(tiles[x, y - 1]) < lvl) c *= 0.55f;            // the south face
                    }
                    else if (!WorldGen.IsLiquid(g) && !reach[x, y]) c = Color.Lerp(c, Color.magenta, 0.35f);   // walkable but cut off
                    terrain[y * n + x] = c;

                    var p = new Vector2(x - Half + 0.5f, y - Half + 0.5f);
                    var s = zones.At(p);
                    Color z = gen.ThemeAt(p.x, p.y).mapColor;
                    if (s.t > 0.45f) z *= 0.6f;                                                 // the border line itself
                    zoneImg[y * n + x] = z;
                }
            void Dot(Color32[] img, Vector2 c, int r, Color32 col)
            {
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        int px = Mathf.FloorToInt(c.x) + Half + x, py = Mathf.FloorToInt(c.y) + Half + y;
                        if (px >= 0 && py >= 0 && px < n && py < n && x * x + y * y <= r * r) img[py * n + px] = col;
                    }
            }
            foreach (var s in zones.sites)
            {
                var col = s.kind == StructKind.Landmark ? new Color32(255, 255, 255, 255) : new Color32(255, 220, 60, 255);
                Dot(terrain, s.center, s.kind == StructKind.Landmark ? 6 : 4, col);
                Dot(zoneImg, s.center, s.kind == StructKind.Landmark ? 6 : 4, col);
            }
            Dot(terrain, Vector2.zero, 5, new Color32(255, 80, 80, 255));
            Save($"{OutDir}/terrain_{seed}.png", terrain, n);
            Save($"{OutDir}/zones_{seed}.png", zoneImg, n);
        }

        static void Save(string path, Color32[] px, int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
    }
}
