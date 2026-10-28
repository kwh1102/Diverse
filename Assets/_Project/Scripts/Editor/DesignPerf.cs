using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Timing of the work that runs while walking and fighting, default look vs a fully pictured design (city layout,
    /// ground textures, generated effects). Batch: -executeMethod Diverse.EditorTools.DesignPerf.RunBatch → Logs/DesignPerf.txt
    /// Uses a scratch save folder (DIVERSE_SAVE_DIR) for the stand-in pictures.
    /// </summary>
    public static class DesignPerf
    {
        public static void RunBatch() { Run(); EditorApplication.Exit(0); }

        /// <summary>Measure and write Logs/DesignPerf.txt (safe in an open editor: nothing is saved outside the scratch folder).</summary>
        public static string Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiversePerf");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            var log = new System.Text.StringBuilder();
            var gen = new WorldGen(12345);
            int N = WorldGen.Chunk;

            Ground[,] Tiles(int cx, int cy)
            {
                var g = new Ground[N + 2, N + 2];
                for (int y = -1; y <= N; y++)
                    for (int x = -1; x <= N; x++) g[x + 1, y + 1] = gen.GroundAt(cx * N + x, cy * N + y);
                return g;
            }

            void Measure(string label)
            {
                var sw = Stopwatch.StartNew();
                Ground[,] t = null;
                for (int i = 0; i < 20; i++) t = Tiles(3 + i, -4);
                double tiles = sw.Elapsed.TotalMilliseconds / 20;
                sw.Restart();
                for (int i = 0; i < 20; i++) Object.DestroyImmediate(Art.BakeGround(Tiles(3 + i, -4), N, (3 + i) * N, -4 * N, 12345));
                double bake = sw.Elapsed.TotalMilliseconds / 20 - tiles;
                var w = DB.W(WeaponKind.Katana);
                sw.Restart();
                for (int i = 0; i < 1000; i++) { Art.Slash(w.slashColor, w.slashCore, 20, 140, 1); Art.HitSpark(w.slashColor, 9); }
                double hit = sw.Elapsed.TotalMilliseconds / 1000;
                log.AppendLine($"{label}: ground tiles {tiles:0.00} ms/chunk · ground bake {bake:0.00} ms/chunk · slash+spark lookup {hit * 1000:0.0} µs/hit");
            }

            Measure("default look ");
            DesignRuntime.Activate(DesignTestData.Pictured((DesignPack)null));
            Measure("pictured city");
            File.WriteAllText("Logs/DesignPerf.txt", log.ToString());
            Debug.Log("[DesignPerf]\n" + log);
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
            DesignRuntime.Activate(null);
            return log.ToString();
        }
    }
}
