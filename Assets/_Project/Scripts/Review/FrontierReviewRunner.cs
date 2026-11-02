#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Diverse.EditorTools
{
    public class FrontierReviewRunner : MonoBehaviour
    {
        const string Output = "Logs/FrontierReview";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<double> timings = new List<double>();
        readonly List<long> allocations = new List<long>();
        bool failed;
        static void Finish(bool ok)
        {
            UnityEditor.SessionState.SetBool("Diverse.FrontierReview.OK", ok);
            UnityEditor.EditorApplication.ExitPlaymode();
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;
        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) failed = true;
        }
        static int Count(object target, string field) => ((IDictionary)target.GetType().GetField(field, Fields).GetValue(target)).Count;
        void Check(bool ok, string message)
        {
            File.AppendAllText(Output + "/checks.txt", (ok ? "PASS " : "FAIL ") + message + "\n");
            if (!ok) { failed = true; Debug.LogError(message); }
        }

        IEnumerator Start()
        {
            File.WriteAllText(Output + "/checks.txt", "Residents, frontier and zoomed-out map regression\n");
            for (int i = 0; i < 90; i++) yield return null;
            var game = Game.I; var streamer = WorldStreamer.I;
            if (game == null || game.Player == null) { Finish(false); yield break; }
            game.Player.invulnUntil = float.MaxValue;
            game.Player.PendingEvolutions = 0; game.Player.XpToNext = float.MaxValue;
            game.Player.StopMoving();
            Check(Enumerable.Range(0, 10).Select(i => Convert.ToBase64String(Art.Npc("villager" + i, 0).texture.EncodeToPNG())).Distinct().Count() == 10,
                "10 distinct resident sprites");
            Check(Enumerable.Range(0, 6).Select(i => Convert.ToBase64String(Art.Npc("wanderer" + i, 0).texture.EncodeToPNG())).Distinct().Count() == 6,
                "6 distinct wanderer sprites");
            ReferenceReviewRunner.Shot("residents", false, Output, 225);
            var travelers = new List<Interactable>();
            for (int i = 0; i < 6; i++)
                travelers.Add(Interactable.Create(transform, "wanderer", "review" + i, "방랑자", new Vector2(-5 + i * 2, -2), Art.Npc("wanderer" + i, 0), null));
            ReferenceReviewRunner.Shot("wanderers", false, Output, 225);
            foreach (var traveler in travelers) Destroy(traveler.gameObject);

            // Sweep a fresh corridor at the normal movement speed and widest camera setting.
            // Scripted positions isolate streaming from terrain/pathfinding and never touch the user's save.
            game.State = GameState.Playing; GameTime.ClearPauses();
            game.enabled = false; // Only the measured Tick may advance streaming during the sweep.
            int originalTargetRate = Application.targetFrameRate; Application.targetFrameRate = 60;
            int missingFrames = 0, movingFrames = 0, beforeBuilds = HitchLog.ChunksBuilt;
            double maxTick = 0;
            double bakeAtMax = 0, buildAtMax = 0;
            string phaseAtMax = "";
            bool partialSafe = true;
            var streamWatch = new System.Diagnostics.Stopwatch();
            float speed = game.Player.MoveSpeed;
            for (float traveled = 0; traveled < 80; traveled += speed * Time.unscaledDeltaTime)
            {
                game.Player.StopMoving(); game.Player.Pos = new Vector2(traveled,0);
                CameraRig.I.Snap(game.Player.Pos);
                streamWatch.Restart(); streamer.Tick(game.Player.Pos); streamWatch.Stop();
                var pendingView = (ChunkView)typeof(WorldStreamer).GetField("pendingBuild",Fields).GetValue(streamer);
                if (pendingView != null)
                    partialSafe &= !pendingView.Ready && !pendingView.gameObject.activeSelf &&
                        !streamer.IsLoaded((Vector2)(pendingView.key*WorldGen.Chunk)+Vector2.one);
                if (streamWatch.Elapsed.TotalMilliseconds > maxTick)
                {
                    maxTick = streamWatch.Elapsed.TotalMilliseconds;
                    bakeAtMax = streamer.LastBakeSliceMs; buildAtMax = streamer.LastBuildMs;
                    phaseAtMax = streamer.LastBuildPhase;
                }
                var rect = CameraRig.I.ViewRect(); bool missing = false;
                for (float y = rect.yMin+.5f; y < rect.yMax; y += 2)
                    for (float x = rect.xMin+.5f; x < rect.xMax; x += 2)
                        if (!streamer.IsLoaded(new Vector2(x,y))) missing = true;
                if (missing) missingFrames++;
                movingFrames++;
                yield return null;
            }
            game.enabled = true; Application.targetFrameRate = originalTargetRate;
            File.AppendAllText(Output+"/checks.txt",$"Streaming 80m corridor at {speed:F2}m/s: {HitchLog.ChunksBuilt-beforeBuilds} new chunks, visible gaps {missingFrames}/{movingFrames} frames, max manual Tick {maxTick:F2}ms (bake {bakeAtMax:F2}ms, objects {buildAtMax:F2}ms, phase {phaseAtMax}; editor CPU)\n");
            Check(missingFrames == 0,"No visible unloaded terrain during normal-speed streaming sweep");
            Check(partialSafe,"Partially built chunks stay inactive and cannot receive new ambient spawns");

            float bound = game.Player.Bound;
            // A dry walkable point near the first collision radius.
            Vector2 edge = Vector2.up * (bound - 1);
            for (int angle = 0; angle < 360; angle += 3)
            {
                var candidate = MathX.Dir(angle) * (bound - 1);
                if (WorldGen.Impassable(streamer.Gen.GroundAt(Mathf.FloorToInt(candidate.x), Mathf.FloorToInt(candidate.y)))) continue;
                edge = candidate; break;
            }
            game.Player.Pos = edge; CameraRig.I.Snap(edge); streamer.Warm(edge);
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            ReferenceReviewRunner.Shot("field_mist", false, Output, 315);
            ReferenceReviewRunner.Shot("minimap_mist", true, Output, 315);
            var minimap = FindFirstObjectByType<MinimapView>();
            var miniCanvas = (MapCanvas)typeof(MinimapView).GetField("map",Fields).GetValue(minimap);
            var miniImage = (UnityEngine.UI.RawImage)typeof(MapCanvas).GetField("image",Fields).GetValue(miniCanvas);
            Check(miniImage.material != null && miniImage.material.shader.name == "Diverse/MapMist" &&
                Mathf.Approximately(miniImage.material.GetFloat("_Bound"),bound),"Minimap shares the current frontier mist radius");
            var mist = streamer.GetComponent<FrontierMist>().GetComponentInChildren<MeshRenderer>();
            Check(mist != null && mist.enabled, "Continuous field mist visible beside collision boundary");
            Check(streamer.Move(edge, edge.normalized * 3, game.Player.radius, true, bound).magnitude <= bound + .001f, "Movement blocked beyond mist radius");

            // A broad explored save without instantiating or baking the distant chunks.
            game.World.exploredChunks.Clear();
            for (int y = -24; y <= 24; y++) for (int x = -24; x <= 24; x++) game.World.exploredChunks.Add(x + "," + y);
            game.OpenOverlay(GameState.Map);
            for (int i = 0; i < 10; i++) yield return null;
            var view = FindFirstObjectByType<MapView>();
            var map = (MapCanvas)typeof(MapView).GetField("map", Fields).GetValue(view);
            view.Zoom = .5f; map.PixelsPerUnit = .5f; map.Center = Vector2.zero;
            view.enabled = false; // Paint timing must not be hidden by Update consuming its throttle earlier in the frame.
            MapCanvas.ClearCache();
            int chunks = Count(streamer, "chunks"), ground = Count(streamer, "groundCache");
            // Isolate Paint cost; normal map Update still exercises marker generation each frame.
            var watch = new System.Diagnostics.Stopwatch();
            Func<int, int, bool> include = (x, y) => x >= -24 && x <= 24 && y >= -24 && y <= 24;
            for (int frame = 0; frame < 120; frame++)
            {
                yield return new WaitForSecondsRealtime(.09f);
                map.Center = new Vector2(Mathf.Sin(frame * .04f) * 110, Mathf.Cos(frame * .04f) * 80);
                long before = GC.GetAllocatedBytesForCurrentThread();
                watch.Restart();
                map.Paint(include, 2401, bound);
                watch.Stop(); timings.Add(watch.Elapsed.TotalMilliseconds); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                yield return null;
            }
            Check(Count(streamer, "chunks") == chunks, "Map pan creates no live chunks");
            Check(Count(streamer, "groundCache") == ground, "Map pan does not populate live ground cache");
            var image = (UnityEngine.UI.RawImage)typeof(MapCanvas).GetField("image", Fields).GetValue(map);
            Check(image.texture.width <= 512 && image.texture.height <= 512, "Map texture capped at 512 per axis");
            // Keep a stable view for the visual review while progressive terrain fills.
            map.Center = edge;
            view.Zoom = 3; view.enabled = true;
            yield return new WaitForSecondsRealtime(5);
            ReferenceReviewRunner.Shot("map_mist", true, Output, 315);
            game.CloseOverlay();
            game.Player.Level = 8; game.World.bossKills = 1;
            for (int i = 0; i < 3; i++) yield return null;
            Check(Mathf.Approximately(game.Player.Bound, 320), "Frontier moves to next ring on unlock");
            Check(!mist.enabled, "Old field boundary disappears after unlocking");
            game.Player.Level = 27; game.World.bossKills = 10;
            for (int i = 0; i < 3; i++) yield return null;
            Check(game.Player.Bound == 0 && !mist.enabled, "Fully unlocked world hides mist");
            yield return new WaitForSecondsRealtime(.2f);
            Check(miniImage.material.shader.name != "Diverse/MapMist","Minimap clears mist when the world is fully unlocked");
            timings.Sort();
            File.AppendAllText(Output + "/checks.txt", $"Map Paint 120 actual repaints, cold cache, 2401 explored chunks: avg {timings.Average():F3}ms, p95 {timings[(int)(timings.Count*.95)]:F3}ms, max {timings.Last():F3}ms; avg allocations {allocations.Average():F0} B/call\n");
            File.AppendAllText(Output + "/checks.txt", failed ? "FAILED\n" : "PASS\n");
            Finish(!failed);
        }
    }
}
#endif
