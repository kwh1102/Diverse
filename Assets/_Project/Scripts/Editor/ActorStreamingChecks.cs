using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Diverse.EditorTools
{
    public static class ActorStreamingChecks
    {
        public const string Out = "Logs/ActorStreaming";

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void RunBatchValidated()
        {
            try { Run(); WorldVisualChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void RunBatchPlayReview()
        {
            try { Run(); FrontierReview.RunBatch(); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        public static void RunBatchFullReview()
        {
            try { Run(); WorldVisualChecks.Run(); FrontierReview.RunBatch(); }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        [MenuItem("Diverse/Checks/Actor Eyes and Streaming Bake")]
        public static void Run()
        {
            Directory.CreateDirectory(Out);
            Preview();
            foreach (var rule in WorldArtOverhaul.NpcEyes)
                for (int frame = 0; frame < 2; frame++)
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/Sprites/npc_"+rule.Key+"_"+frame+".png");
                    var px = t.GetPixels32(); int y = rule.Value.z + frame;
                    var ink = DB.ArtStyle.palette[DB.ArtStyle.outline];
                    foreach (int x in new[] { rule.Value.x, rule.Value.y })
                        if (!px[y*t.width+x].Equals(ink) || !px[(y+1)*t.width+x].Equals(ink))
                            throw new Exception("Inconsistent native eyes: " + rule.Key);
                }
            var go = new GameObject("Streaming bake inspection");
            var report = new StringBuilder();
            try
            {
                var world = go.AddComponent<WorldStreamer>();
                world.EditorInit(new WorldGen(12345, ZoneMap.ForWorld(null, 12345)));
                Art.Preload(); // Game preloads its sprites before streaming begins; do the same here.
                var warmup = TileBaker.Bake(world,Vector2Int.zero); Object.DestroyImmediate(warmup);
                // Same cold region keys for before/after comparison. Report preparation separately from raster work.
                for (int i = 0; i < 8; i++)
                {
                    var key = new Vector2Int(5 + i % 4, -3 + i / 4);
                    var watch = Stopwatch.StartNew();
                    var operation = new TileBaker.Operation(world, key);
                    double creation = watch.Elapsed.TotalMilliseconds;
                    int steps = 1;
                    while (!operation.Step(2))
                    {
                        if (++steps > 2000) throw new Exception("Bake did not finish");
                        if (steps == 2)
                        {
                            watch.Stop();
                            var inspection = TileBaker.Bake(world,key+Vector2Int.right);
                            Object.DestroyImmediate(inspection);
                            watch.Start();
                        }
                    }
                    double total = watch.Elapsed.TotalMilliseconds;
                    report.AppendLine($"{key}: constructor {creation:F2}ms, sliced preparation {operation.PreparationMs:F2}ms, total CPU {total:F2}ms, {steps} frames at 2ms, max slice {operation.MaxSliceMs:F2}ms");
                    // Interleave a synchronous inspection while an operation is only partly prepared.
                    var expected = TileBaker.Bake(world,key);
                    if (!expected.GetPixels32().SequenceEqual(operation.Texture.GetPixels32())) throw new Exception("Sliced terrain differs: " + key);
                    Object.DestroyImmediate(expected);
                    Object.DestroyImmediate(operation.Texture);
                    operation.Cancel();
                }
                File.WriteAllText(Out + "/bake.txt", report.ToString());
                UnityEngine.Debug.Log(report.ToString());
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void Preview()
        {
            var keys = new List<string> { "npc_owl_0", "npc_bear_0", "npc_cat_0", "npc_otter_0", "rabbit_white_Idle0" };
            keys.AddRange(Enumerable.Range(0,10).Select(i => "npc_villager" + i + "_0"));
            keys.AddRange(Enumerable.Range(0,6).Select(i => "npc_wanderer" + i + "_0"));
            const int cell = 180, columns = 5, side = columns * cell;
            var pixels = Enumerable.Repeat(new Color32(123,145,118,255), side * side).ToArray();
            for (int i = 0; i < keys.Count; i++)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/Sprites/" + keys[i] + ".png");
                var data = t.GetPixels32(); int scale = 4;
                int left = i % columns * cell + (cell-t.width*scale)/2;
                int bottom = (4-i/columns)*cell + (cell-t.height*scale)/2;
                for (int y = 0; y < t.height * scale; y++) for (int x = 0; x < t.width * scale; x++)
                {
                    var c = data[y/scale*t.width+x/scale];
                    if (c.a != 0) pixels[(bottom+y)*side+left+x] = c;
                }
            }
            var result = new Texture2D(side,side,TextureFormat.RGBA32,false); result.SetPixels32(pixels); result.Apply();
            File.WriteAllBytes(Out + "/eyes.png",result.EncodeToPNG()); Object.DestroyImmediate(result);
            File.WriteAllLines(Out + "/eyes_order.txt",keys);
        }
    }
}
