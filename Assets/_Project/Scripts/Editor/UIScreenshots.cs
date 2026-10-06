using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Review aid: plays through the menu and game and saves 1280x720 screenshots of every screen to Logs/UIShots
    /// (title, settings, character select, HUD, pause tabs, dialogue, evolution, map, death). Scratch save, AI off.
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.UIScreenshots.RunBatch
    /// </summary>
    public static class UIScreenshots
    {
        const string RunningKey = "Diverse.Shots.Running";
        const string BatchKey = "Diverse.Shots.Batch";
        const string DirKey = "Diverse.Shots.Dir";
        const int W = 1280, H = 720;
        static string OutDir => Path.GetFullPath("Logs/UIShots");

        [MenuItem("Diverse/UI 스크린샷 찍기 (Logs/UIShots)", priority = 26)]
        public static void RunMenu() => Begin(false);
        public static void RunBatch() => Begin(true);

        static void Begin(bool batch)
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiverseShots");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonUtility.ToJson(new Settings { aiEnabled = false, sfx = 0, music = 0 }));
            File.WriteAllText(Path.Combine(dir, "world.json"), JsonUtility.ToJson(new WorldSave { seed = 12345 }));
            if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
            Directory.CreateDirectory(OutDir);
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", dir);
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene(UIBuilder.MenuScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", SessionState.GetString(DirKey, null));
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Shooter>() == null) new GameObject("Shots").AddComponent<Shooter>();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                if (Object.FindFirstObjectByType<Shooter>() == null) new GameObject("Shots").AddComponent<Shooter>();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                System.Environment.SetEnvironmentVariable("DIVERSE_SAVE_DIR", null);
                Debug.Log("[Diverse] UI 스크린샷: " + OutDir);
                if (SessionState.GetBool(BatchKey, false)) EditorApplication.Exit(0);
            }
        }

        class Shooter : MonoBehaviour
        {
            void Awake() => DontDestroyOnLoad(gameObject);

            /// <summary>
            /// Render the scene camera plus every overlay canvas into one image. Overlay canvases are switched to
            /// Screen Space - Camera for the shot (batch mode has no screen to read back), then restored.
            /// </summary>
            static void Shot(string name)
            {
                var cam = Camera.main;
                var rt = new RenderTexture(W, H, 24);
                var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.enabled).ToList();
                var modes = canvases.Select(c => c.renderMode).ToList();
                // Sorting layer order keeps the canvas above world sprites, like Overlay does in the real game
                var orders = canvases.Select(c => c.sortingOrder).ToList();
                foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1; c.sortingOrder = 32767; }
                var oldTarget = cam.targetTexture;
                cam.targetTexture = rt;
                // CanvasScaler reads Screen.* — force the reference size for this render
                foreach (var s in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None)) s.enabled = false;
                foreach (var c in canvases) { c.scaleFactor = H / 360f; }
                Canvas.ForceUpdateCanvases();
                cam.Render();
                cam.targetTexture = oldTarget;
                for (int i = 0; i < canvases.Count; i++) { canvases[i].renderMode = modes[i]; canvases[i].sortingOrder = orders[i]; }
                foreach (var s in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None)) s.enabled = true;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                rt.Release();
            }

            static T Find<T>() where T : Object => FindFirstObjectByType<T>(FindObjectsInactive.Include);
            static void Click(Component root, string name) =>
                root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy)?.onClick.Invoke();
            static IEnumerator F(int n) { for (int i = 0; i < n; i++) yield return null; }

            IEnumerator Start()
            {
                yield return F(5);
                Shot("01_title");
                Click(Find<TitleView>(), "Settings"); yield return F(3); Shot("02_settings");
                Click(Find<SettingsView>(), "KeysTab"); yield return F(3); Shot("03_keys");
                Click(Find<SettingsView>(), "Back"); yield return F(2);
                Click(Find<TitleView>(), "Start"); yield return F(3); Shot("04_select");
                Click(Find<CharacterSelectView>(), "StartLife"); yield return F(60);
                var g = Game.I;
                if (g != null)
                {
                    g.Ui.Toast("우클릭 이동/공격 · 좌클릭 공격 · QWER 스킬 · Space 대시");
                    g.Player.UnspentAttr = 2;
                    yield return F(3); Shot("05_hud");
                    g.Player.PendingEvolutions = 0;
                    g.OpenOverlay(GameState.Paused); yield return F(3); Shot("06_pause_abilities");
                    g.Ui.OpenTab("attrs"); yield return F(3); Shot("07_pause_attrs");
                    g.Ui.OpenTab("chronicle"); yield return F(3); Shot("08_pause_chronicle");
                    g.Ui.OpenTab("settings"); yield return F(3); Shot("09_pause_settings");
                    g.CloseOverlay(); yield return F(2);
                    var npc = Interactable.All.Where(i => i != null && i.kind == "npc_storyteller").FirstOrDefault()
                              ?? Interactable.All.FirstOrDefault(i => i != null && i.kind.StartsWith("npc_"));
                    if (npc != null) { Dialogue.Open(g, npc); yield return F(3); Shot("10_dialogue"); g.CloseOverlay(); yield return F(2); }
                    g.Player.PendingEvolutions = 1; g.BeginEvolution();
                    for (int i = 0; i < 300 && g.Offer == null; i++) yield return null;
                    yield return F(60); Shot("11_evolve");
                    g.SkipEvolution(); yield return F(3);
                    g.OpenOverlay(GameState.Map); yield return F(5); Shot("12_map");
                    g.CloseOverlay(); yield return F(2);
                    g.Player.invulnUntil = 0;
                    g.Player.TakeDamage(new DamageInfo { amount = 999999, direction = Vector2.right, noEvents = true });
                    for (int i = 0; i < 400 && !(Find<DeathView>()?.gameObject.activeInHierarchy ?? false); i++) yield return null;
                    yield return F(3); Shot("13_death");
                }
                EditorApplication.ExitPlaymode();
            }
        }
    }
}
