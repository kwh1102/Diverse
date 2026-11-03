using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Play-mode frame probe: the built-in world, 20 s walking east (chunks stream in) then 20 s of non-stop fighting.
    /// Every frame records its time, GC allocation / collections and the Diverse.* profiler markers, then writes the slowest
    /// frames with what ran in them to Logs/PerfProbe.txt. Batch (with graphics): -executeMethod Diverse.EditorTools.PerfProbe.RunBatch
    /// Uses a scratch save folder (DIVERSE_SAVE_DIR), AI off.
    /// </summary>
    public static class PerfProbe
    {
        const string RunningKey = "Diverse.PerfProbe.Running", DirKey = "Diverse.PerfProbe.Dir";
        // Diverse.* markers plus Unity's own player-loop stages, so a slow frame shows where outside our code the time went
        static readonly string[] Markers = { "Diverse.Game", "Diverse.Chunks", "Diverse.ChunkBuild", "Diverse.SpriteMake", "Diverse.Enemies", "Diverse.Player", "Diverse.Projectiles", "Diverse.Fx", "Diverse.Hud", "Diverse.Minimap", "Diverse.Path", "Diverse.Environment",
            "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate", "Update.ScriptRunDelayedDynamicFrameRate",
            "FixedUpdate.Physics2DFixedUpdate", "PostLateUpdate.PlayerUpdateCanvases", "PostLateUpdate.FinishFrameRendering", "Gfx.WaitForPresentOnGfxThread", "GC.Collect", "EditorLoop", "Instantiate", "Loading.ReadObject" };

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(DirKey, null));
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Runner>() == null) new GameObject("PerfProbe").AddComponent<Runner>();
        }

        public static void RunBatch() => StartProbe(false);
        public static void RunBatchEnvironmentOff() => StartProbe(true);
        static void StartProbe(bool environmentOff)
        {
            SessionState.SetBool("Diverse.PerfProbe.EnvironmentOff", environmentOff);
            var dir = Path.Combine(Path.GetTempPath(), "DiversePerfProbe");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, damageNumbers = true }));
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene(UIBuilder.GameScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Object.FindFirstObjectByType<Runner>() == null) new GameObject("PerfProbe").AddComponent<Runner>();
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                EditorApplication.Exit(0);
            }
        }

        class Frame { public string phase; public float ms; public long gcBytes; public bool gcRan, fontRebuilt, paused; public float[] markers; public int enemies; }

        class Runner : MonoBehaviour
        {
            readonly List<Frame> frames = new List<Frame>();
            ProfilerRecorder[] recorders;
            ProfilerRecorder gcAlloc;
            int lastGc;
            bool fontRebuilt;   // a dynamic font atlas was rebuilt this frame (every uGUI text re-generates)
            float walked;
            int streamed;
            void OnFont(Font f) => fontRebuilt = true;

            void Awake() => DontDestroyOnLoad(gameObject);

            IEnumerator Start()
            {
                if (SessionState.GetBool("Diverse.PerfProbe.EnvironmentOff", false)) DB.WorldRules.environment.enabled = false;
                for (int i = 0; i < 90; i++) yield return null;
                var g = Game.I;
                if (g == null || g.Player == null) { Write("FAIL: no game"); yield break; }
                // Benchmark the built-in world the player actually uses; the old custom city layout is retired.
                g.Player.invulnUntil = float.MaxValue;
                // Keep the benchmark in live play: an automatic level-up offer otherwise pauses the combat phase.
                g.Player.Xp = 0; g.Player.XpToNext = float.MaxValue; g.Player.PendingEvolutions = 0;
                g.State = GameState.Playing; GameTime.ClearPauses();
                for (int i = 0; i < 60; i++) yield return null;

                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var categories = new Dictionary<string, ProfilerCategory>();
                foreach (var h in handles) { var d = ProfilerRecorderHandle.GetDescription(h); categories[d.Name] = d.Category; }
                recorders = Markers.Select(m => ProfilerRecorder.StartNew(categories.TryGetValue(m, out var c) ? c : ProfilerCategory.Scripts, m)).ToArray();
                gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                lastGc = System.GC.CollectionCount(0);
                Font.textureRebuilt += OnFont;

                // Walking: keep a destination far to the east so new chunks stream in the whole time
                Vector2 start = g.Player.Pos; int chunksBefore = HitchLog.ChunksBuilt;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 20)
                {
                    if (Time.frameCount % 30 == 0) g.Player.SetDestination(new Vector2(g.Player.Pos.x + 12, start.y), false);
                    Record("walk");
                    yield return null;
                }
                walked = Vector2.Distance(start, g.Player.Pos); streamed = HitchLog.ChunksBuilt - chunksBefore;
                // Fighting: a pack of enemies around the hero, the hero swinging at the nearest one
                var attack = typeof(Player).GetMethod("BasicAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                g.Player.StopMoving();
                t0 = Time.realtimeSinceStartup;
                float next = 0;
                string[] kinds = { "fox", "shroom", "bat", "raccoon", "boar" };
                var benchmarkRandom = new System.Random(12345);
                while (Time.realtimeSinceStartup - t0 < 20)
                {
                    int alive = Actor.All.Count(a => a != null && a.Alive && a.team == Team.Enemy);
                    if (alive < 8)
                        for (int k = 0; k < 3; k++)
                            Enemy.Spawn(DB.ScaledEnemy(kinds[benchmarkRandom.Next(kinds.Length)], 0), g.Player.Pos +
                                MathX.Dir((float)benchmarkRandom.NextDouble() * 360) * (3 + (float)benchmarkRandom.NextDouble() * 2), null);
                    if (Time.time > next)
                    {
                        next = Time.time + 0.25f;
                        var target = Actor.Nearest(g.Player.Pos, 6, Team.Enemy);
                        if (target != null) attack.Invoke(g.Player, new object[] { (target.Pos - g.Player.Pos).SafeNormal(Vector2.right) });
                    }
                    Record("fight");
                    yield return null;
                }
                Report();
                EditorApplication.ExitPlaymode();
            }

            void Record(string phase)
            {
                int gc = System.GC.CollectionCount(0);
                frames.Add(new Frame
                {
                    phase = phase, ms = Time.unscaledDeltaTime * 1000f, gcBytes = gcAlloc.Valid ? gcAlloc.LastValue : 0, gcRan = gc != lastGc, fontRebuilt = fontRebuilt, paused = GameTime.Paused,
                    markers = recorders.Select(r => r.Valid ? r.LastValue / 1e6f : 0).ToArray(),
                    enemies = Actor.All.Count(a => a != null && a.Alive && a.team == Team.Enemy),
                });
                lastGc = gc;
                fontRebuilt = false;
            }

            void Report()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Built-in world: walked {walked:0.0} tiles, streamed {streamed} new chunks during the walk phase.");
                foreach (var phase in new[] { "walk", "fight" })
                {
                    var f = frames.Where(x => x.phase == phase).Skip(2).ToList();
                    if (f.Count == 0) continue;
                    var sorted = f.Select(x => x.ms).OrderBy(x => x).ToList();
                    sb.AppendLine($"== {phase}: {f.Count} frames · avg {f.Average(x => x.ms):0.0} ms · p95 {sorted[(int)(sorted.Count * 0.95f)]:0.0} ms · max {sorted.Last():0.0} ms"
                                  + $" · frames > 33 ms: {f.Count(x => x.ms > 33)} · paused frames: {f.Count(x => x.paused)} · GC runs: {f.Count(x => x.gcRan)} · font atlas rebuilds: {f.Count(x => x.fontRebuilt)} · avg GC alloc {f.Average(x => x.gcBytes) / 1024f:0.0} KB/frame");
                    sb.AppendLine("   avg ms per section: " + string.Join(", ", Markers.Select((m, i) => $"{Short(m)} {f.Average(x => x.markers[i]):0.00}")));
                    sb.AppendLine("   slowest frames:");
                    foreach (var x in f.OrderByDescending(x => x.ms).Take(12))
                        sb.AppendLine($"     {x.ms,6:0.0} ms  gc {(x.gcRan ? "RAN" : "-  ")} font {(x.fontRebuilt ? "REBUILT" : "-      ")} alloc {x.gcBytes / 1024f,6:0.0} KB  enemies {x.enemies,2}  | "
                                      + string.Join(" ", Markers.Select((m, i) => x.markers[i] >= 0.5f ? $"{Short(m)}={x.markers[i]:0.0}" : null).Where(s => s != null)));
                }
                Write(sb.ToString());
            }

            static string Short(string m) => m.Substring(m.LastIndexOf('.') + 1);

            void Write(string text)
            {
                File.WriteAllText("Logs/PerfProbe.txt", text);
                Debug.Log("[PerfProbe]\n" + text);
            }

            void OnDestroy()
            {
                if (recorders != null) foreach (var r in recorders) r.Dispose();
                gcAlloc.Dispose();
                Font.textureRebuilt -= OnFont;
            }
        }
    }
}
