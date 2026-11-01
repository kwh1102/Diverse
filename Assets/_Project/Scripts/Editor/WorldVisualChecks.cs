using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Diverse.EditorTools
{
    /// <summary>Regression checks for the real renderer, deterministic placement and native asset contracts.</summary>
    public static class WorldVisualChecks
    {
        const string Out = "Logs/Redesign";

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Diverse/Art/Check World Overhaul")]
        public static void Run()
        {
            Directory.CreateDirectory(Out); var report = new StringBuilder();
            var go = new GameObject("World visual checks (temporary)");
            try
            {
                var world = go.AddComponent<WorldStreamer>(); world.EditorRegister();
                foreach (var theme in DB.Themes)
                {
                    var save = new WorldSave { seed = 12345 }; save.zoneThemes.AddRange(Enumerable.Repeat(theme.id, ZoneMap.Count));
                    world.EditorInit(new WorldGen(save.seed, ZoneMap.ForWorld(save, save.seed)));
                    CheckSeams(world, new Vector2Int(-8, 5), theme.id, report);
                    CheckPlacement(world, theme.id, report);
                }
                CheckSprites(report);
                CheckSlicedBake(world, report);
                SpritePreview();
                report.AppendLine(Performance(world));
                File.WriteAllText(Out + "/checks.txt", report.ToString());
                Debug.Log("[WorldVisualChecks] PASS\n" + report);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        static void CheckSeams(WorldStreamer world, Vector2Int key, string theme, StringBuilder report)
        {
            int side = WorldGen.Chunk * Art.PPU;
            var combined = TileBaker.BakeRegion(world, key * WorldGen.Chunk, WorldGen.Chunk * 2);
            var expected = combined.GetPixels32(); int differences = 0;
            for (int quadrant = 3; quadrant >= 0; quadrant--)
            {
                var offset = new Vector2Int(quadrant % 2, quadrant / 2);
                var tile = TileBaker.Bake(world, key + offset); var pixels = tile.GetPixels32();
                for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
                    if (!pixels[y * side + x].Equals(expected[(y + offset.y * side) * side * 2 + x + offset.x * side])) differences++;
                Object.DestroyImmediate(tile);
            }
            Object.DestroyImmediate(combined);
            Require(differences == 0, theme + " chunk seams differ: " + differences);
            report.AppendLine(theme + ": 0 differing pixels (combined region vs reverse-order separate chunks, negative coordinates)");
        }

        static void CheckPlacement(WorldStreamer world, string theme, StringBuilder report)
        {
            var layout = new SceneryLayout(world); var accepted = new List<SceneryLayout.Placement>();
            var cells = new List<Vector2Int>();
            for (int y = 12; y < 36; y++) for (int x = -36; x < -12; x++) cells.Add(new Vector2Int(x, y));
            foreach (var c in cells)
            {
                var p = layout.At(c.x, c.y);
                if (p != null && layout.Accepted(c.x, c.y, p)) accepted.Add(p);
            }
            for (int i = 0; i < accepted.Count; i++) for (int j = i + 1; j < accepted.Count; j++)
                Require(accepted[i].level != accepted[j].level || !accepted[i].visual.Overlaps(accepted[j].visual), theme + " overlapping accepted scenery");
            var reversed = new SceneryLayout(world); int count = 0;
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                var c = cells[i]; var p = reversed.At(c.x, c.y);
                if (p == null || !reversed.Accepted(c.x, c.y, p)) continue;
                count++;
                Require(accepted.Any(a => a.position == p.position && a.sprite.name == p.sprite.name), theme + " load-order changed scenery");
            }
            Require(count == accepted.Count, theme + " load-order changed placement count");
            report.AppendLine("  " + accepted.Count + " accepted scenery: 0 silhouette overlaps, identical reverse proposal order");
        }

        static void CheckSprites(StringBuilder report)
        {
            int count = 0;
            var palette = new HashSet<uint>(DB.ArtStyle.palette.Select(Pack));
            foreach (string key in File.ReadAllLines(WorldArtOverhaul.Source + "managed.txt"))
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/Sprites/" + key + ".png");
                Require(sprite != null, "Missing managed sprite: " + key);
                Require(sprite.pixelsPerUnit == Art.PPU && sprite.texture.filterMode == FilterMode.Point, "Wrong native scale/filter: " + key);
                int visible = 0;
                foreach (var c in sprite.texture.GetPixels32())
                {
                    Require(c.a is 0 or 255, "Nonbinary alpha: " + key);
                    if (c.a == 0) continue;
                    visible++; Require(palette.Contains(Pack(c)), "Off-palette pixel: " + key);
                }
                Require(visible > 8, "Empty managed sprite: " + key); count++;
            }
            report.AppendLine(count + " production sprites: binary alpha, shared palette, 16 PPU, point filtering");
        }

        static void CheckSlicedBake(WorldStreamer world, StringBuilder report)
        {
            var key = new Vector2Int(-7, 6);
            var prepare = Stopwatch.StartNew();
            var operation = new TileBaker.Operation(world, key); int steps = 0;
            double prepareMs = prepare.Elapsed.TotalMilliseconds;
            try
            {
                while (!operation.Step(2))
                {
                    Require(++steps < 500, "Sliced bake did not finish");
                    // A screenshot/inspection bake between streaming frames must not corrupt the pending tile.
                    var interleaved = TileBaker.Bake(world, key + Vector2Int.right);
                    Object.DestroyImmediate(interleaved);
                }
                var direct = TileBaker.Bake(world, key);
                Require(direct.GetPixels32().SequenceEqual(operation.Texture.GetPixels32()), "Sliced and synchronous renderer differ");
                Object.DestroyImmediate(direct); Object.DestroyImmediate(operation.Texture);
                report.AppendLine($"Time slicing: exact pixels despite {steps} interleaved review renders; preparation {prepareMs:0.00} ms, largest raster slice {operation.MaxSliceMs:0.00} ms (2 ms budget plus one four-row block; texture upload excluded).");
            }
            finally { operation.Cancel(); }
        }

        static uint Pack(Color32 c) => (uint)(c.r << 16 | c.g << 8 | c.b);

        static void SpritePreview()
        {
            string[] keys = { "tree_Meadow_0", "tree_Forest_0", "tree_Tundra_0", "tree_Ashland_0", "bush_Fortress_0", "rock_Meadow_0", "prop_pillar", "prop_chest",
                "enemy_jelly_0", "enemy_fox_0", "enemy_raccoon_0", "enemy_boss_knight_0", "rabbit_white_Idle0", "npc_owl_0", "house_0_c2603e", "landmark_fortress" };
            const int cell = 224, size = cell * 4; var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(205, 215, 204, 255);
            for (int i = 0; i < keys.Length; i++)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/Sprites/" + keys[i] + ".png");
                int w = (int)sprite.rect.width, h = (int)sprite.rect.height;
                int scale = Mathf.Max(1, Mathf.Min(4, (cell - 16) / Mathf.Max(w, h)));
                var src = sprite.texture.GetPixels32(); int x0 = i % 4 * cell + (cell - w * scale) / 2;
                int y0 = (3 - i / 4) * cell + (cell - h * scale) / 2;
                for (int y = 0; y < h * scale; y++) for (int x = 0; x < w * scale; x++)
                {
                    var c = src[(y / scale) * w + x / scale];
                    if (c.a != 0) pixels[(y0 + y) * size + x0 + x] = c;
                }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false); texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(Out + "/native_sprite_preview.png", texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        }

        public static string RunPerformance()
        {
            Directory.CreateDirectory(Out);
            var go = new GameObject("World bake benchmark (temporary)");
            try
            {
                var world = go.AddComponent<WorldStreamer>();
                world.EditorInit(new WorldGen(12345, ZoneMap.ForWorld(null, 12345)));
                string result = Performance(world); File.WriteAllText(Out + "/bake_performance.txt", result); return result;
            }
            finally { Object.DestroyImmediate(go); }
        }

        static string Performance(WorldStreamer world)
        {
            var timings = new List<double>(); long allocations = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew(); var texture = TileBaker.Bake(world, new Vector2Int(-8 + i % 5, 5 + i / 5));
                timings.Add(watch.Elapsed.TotalMilliseconds); Object.DestroyImmediate(texture);
            }
            allocations = GC.GetAllocatedBytesForCurrentThread() - allocations; timings.Sort();
            string gc = allocations > 0 ? $"managed allocations {allocations / 20 / 1024} KB/chunk" : "allocation counter unavailable in this editor runtime";
            return $"Renderer: 20 actual 16x16-tile chunks; mean {timings.Average():0.00} ms, median {timings[10]:0.00} ms, p95 {timings[18]:0.00} ms; {gc}. Editor CPU measurement, excludes GPU frame time.";
        }
    }
}
