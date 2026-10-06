using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Live AI test against the real API (needs openai_key.txt in the scratch save folder). Batch, play mode:
    ///   Unity -batchmode -executeMethod Diverse.EditorTools.AiLiveTest.RunBatch  → Logs/AiLiveTest.txt (exit 0 = pass)
    /// Steps: start a life → grant 3 pending evolutions at once → open evolution #1, pick → #2, pick → #3,
    /// and on one of them send a chat message, recording timings, raw responses and every outcome.
    /// Uses DIVERSE_SAVE_DIR (set by the caller) so the real saves are never touched.
    /// </summary>
    public static class AiLiveTest
    {
        const string RunningKey = "Diverse.AiLiveTest.Running";

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Runner>() == null) new GameObject("AiLiveTestRunner").AddComponent<Runner>();
        }

        public static void RunBatch()
        {
            SessionState.SetBool(RunningKey, true);
            EditorSceneManagerOpen();
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        static void EditorSceneManagerOpen() => UnityEditor.SceneManagement.EditorSceneManager.OpenScene(UIBuilder.MenuScenePath);

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Object.FindFirstObjectByType<Runner>() == null) new GameObject("AiLiveTestRunner").AddComponent<Runner>();
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                bool ok = File.Exists("Logs/AiLiveTest.txt") && File.ReadAllText("Logs/AiLiveTest.txt").StartsWith("PASS");
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        class Runner : MonoBehaviour
        {
            readonly StringBuilder log = new StringBuilder();
            bool fail;
            void Awake() => DontDestroyOnLoad(gameObject);
            void OnEnable() => Application.logMessageReceived += OnLog;
            void OnDisable() => Application.logMessageReceived -= OnLog;
            void OnLog(string msg, string stack, LogType t)
            {
                if (t is LogType.Exception or LogType.Error) { fail = true; log.AppendLine($"!! {t}: {msg}\n{stack}"); }
                else if (msg.StartsWith("[AI")) log.AppendLine("   log: " + msg.Replace("\n", " | "));
            }
            void L(string s) { log.AppendLine($"[{Time.realtimeSinceStartup:0.0}s] {s}"); Debug.Log("[AiLiveTest] " + s); }

            IEnumerator Start()
            {
                yield return null;
                IEnumerator flow = Flow();
                while (true)
                {
                    bool more;
                    try { more = flow.MoveNext(); }
                    catch (System.Exception e) { fail = true; L("THREW " + e); break; }
                    if (!more) break;
                    yield return flow.Current;
                }
                File.WriteAllText("Logs/AiLiveTest.txt", (fail ? "FAIL\n" : "PASS\n") + log);
                EditorApplication.ExitPlaymode();
            }

            IEnumerator Flow()
            {
                L($"AI enabled={AiClient.Enabled} key={AiClient.HasKey} model={Game.Settings.aiModel}");
                if (!AiClient.Enabled) { fail = true; L("AI not enabled — put openai_key.txt in DIVERSE_SAVE_DIR"); yield break; }
                // Menu → start a life
                yield return null;
                Session.NewLife(DB.Costumes[0], WeaponKind.SwordShield);
                for (int i = 0; i < 120 && (Game.I == null || Game.I.Player == null); i++) yield return null;
                for (int i = 0; i < 20; i++) yield return null;
                var g = Game.I;
                if (g == null || g.Player == null) { fail = true; L("no game/player"); yield break; }
                var p = g.Player;
                for (int i = 0; i < 30; i++) { p.Telemetry.Record("Attack"); p.Telemetry.Record("Hit"); p.Telemetry.Record("Dash"); }
                for (int i = 0; i < 6; i++) p.Telemetry.Record("Kill");

                // Three levels at once (one big XP burst), exactly like the reported case
                L("gain 3 levels at once");
                p.GainXp(p.XpToNext + 40 * 1.22f * 1.22f * 3);
                L($"level {p.Level}, pending {p.PendingEvolutions}");

                for (int n = 1; n <= 3 && p.PendingEvolutions > 0; n++)
                {
                    float t0 = Time.realtimeSinceStartup;
                    g.BeginEvolution();
                    while (g.Offer == null && Time.realtimeSinceStartup - t0 < 200) yield return null;
                    float dt = Time.realtimeSinceStartup - t0;
                    if (g.Offer == null) { fail = true; L($"evolution #{n}: no offer after {dt:0}s"); yield break; }
                    int ai = g.Offer.Count(x => x.source == "ai");
                    L($"evolution #{n}: offer after {dt:0.0}s — {g.Offer.Count} cards, AI {ai} — {g.OfferSummary}");
                    foreach (var line in g.PipelineLog) log.AppendLine("     " + line);

                    if (n == 2)
                    {
                        // Chat: ask for a change, watch every frame until the storyteller answers or gives up
                        while (g.OfferLocked) yield return null;
                        p.Gold = 9999;
                        var before = g.Offer.Select(x => x.Signature()).ToList();
                        int chatBefore = g.Chat.Count;
                        float c0 = Time.realtimeSinceStartup;
                        L($"chat: send (CanChat={g.CanChat})");
                        g.SendChat("두 번째 카드를 화염 속성으로 바꿔 줘");
                        bool sawBusy = g.ChatBusy;
                        while (g.ChatBusy && Time.realtimeSinceStartup - c0 < 200) yield return null;
                        float cdt = Time.realtimeSinceStartup - c0;
                        var added = g.Chat.Skip(chatBefore).Select(c => (c.player ? "나: " : "이야기꾼: ") + c.text).ToList();
                        int changed = g.Offer == null ? -1 : g.Offer.Select(x => x.Signature()).Where((s, i) => i >= before.Count || s != before[i]).Count();
                        L($"chat: done after {cdt:0.0}s busy={sawBusy} offerNull={g.Offer == null} changedCards={changed} status={AiClient.LastStatus}");
                        foreach (var a in added) log.AppendLine("     " + a);
                        log.AppendLine("     raw: " + (AiClient.LastResponse ?? "(null)").Replace("\n", " "));
                        if (added.Count < 2) { fail = true; L("chat: the storyteller never answered (no reply line added)"); }
                        if (changed <= 0) { fail = true; L("chat: the requested change did not reach the cards"); }
                    }
                    while (g.OfferLocked) yield return null;
                    g.ChooseEvolution(0);
                    yield return null;
                    L($"picked card 1 → pending {p.PendingEvolutions}, state {g.State}");
                }
            }
        }
    }
}
