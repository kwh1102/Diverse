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
        public static void RunBatch() { UILayoutRepair.Run(); Begin(true); }

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
            static void Shot(string name, int width = W, int height = H)
            {
                var cam = Camera.main;
                var rt = new RenderTexture(width, height, 24);
                var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.enabled).ToList();
                var modes = canvases.Select(c => c.renderMode).ToList();
                // Sorting layer order keeps the canvas above world sprites, like Overlay does in the real game
                var orders = canvases.Select(c => c.sortingOrder).ToList();
                foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1; c.sortingOrder = 32767; }
                var oldTarget = cam.targetTexture;
                cam.targetTexture = rt;
                // CanvasScaler reads Screen.* — force the reference size for this render
                foreach (var s in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None)) s.enabled = false;
                foreach (var c in canvases) { c.scaleFactor = Mathf.Min(width / 640f, height / 360f); }
                Canvas.ForceUpdateCanvases();
                cam.Render();
                cam.targetTexture = oldTarget;
                for (int i = 0; i < canvases.Count; i++) { canvases[i].renderMode = modes[i]; canvases[i].sortingOrder = orders[i]; }
                foreach (var s in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None)) s.enabled = true;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
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
                Click(Find<SettingsView>(), "GraphicsTab"); yield return F(3); Shot("03b_graphics");
                Click(Find<SettingsView>(), "Back"); yield return F(2);
                Click(Find<TitleView>(), "ChangeSlot"); yield return F(3); Shot("00_slots");
                Click(Find<SaveSelectView>(), "CustomTab"); yield return F(1);
                Click(Find<SaveSelectView>().transform.Find("Panel/Slots/Slot 0"), "Play"); yield return F(3); Shot("04_design");
                var studio = Find<DesignStudioView>();
                var promptField = studio.GetComponentInChildren<InputField>();
                string[] prompts = { "민트색 털에 남색 망토, 마법사 모자를 쓴 토끼", "이쑤시개를 입에 물고 있다가 퉤 하고 발사, 공격력 10배", "가을 단풍 숲", "모든 적이 과자로 만들어진 세계" };
                for (int i = 0; i < 4; i++) { Click(studio, "Tab " + i); promptField.text = prompts[i]; }
                Click(studio, "Tab 1");
                Click(studio, "Generate"); yield return F(3); Shot("04b_design_generated");
                Click(studio, "StartLife"); yield return F(60);
                var g = Game.I;
                if (g != null)
                {
                    g.Ui.Toast("우클릭 이동/공격 · 좌클릭 공격 · QWER 스킬 · Space 대시");
                    g.Player.UnspentAttr = 2;
                    yield return F(3); Shot("05_hud");
                    // Damage numbers: a plain hit, a crit, an elemental hit and a hit taken, mid-flight
                    var at = g.Player.Pos + new Vector2(2.5f, 0.6f);
                    Fx.I.Number(at, "24", Color.white, 1f, 0.8f, 1);
                    Fx.I.Number(at + new Vector2(1.6f, 0), "87!", new Color(1f, 0.82f, 0.25f), 1.45f, 0.8f, 1, true);
                    Fx.I.Number(at + new Vector2(-4.5f, 0), "31", Color.Lerp(Color.white, (Color)Combat.ElementColor(Element.Fire), 0.7f), 1.1f, 0.8f, -1);
                    Fx.I.Number(g.Player.Pos + new Vector2(0, 0.6f), "12", new Color(1f, 0.4f, 0.45f), 1f, 0.8f, -1);
                    yield return F(9); Shot("05d_damage_numbers");
                    // A basic attack mid-swing: the swing picture should sit in front of the hero, not on it
                    var weaponBefore = g.Player.Weapon;
                    g.Player.Weapon = DB.W(WeaponKind.Katana);
                    typeof(Player).GetMethod("BasicAttack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?.Invoke(g.Player, new object[] { Vector2.right });
                    for (int i = 0; i < 4; i++) { yield return F(2); Shot("05e_attack_swing_" + i); }
                    g.Player.Weapon = weaponBefore;
                    var minimapEnemy = Enemy.Spawn(DB.ScaledEnemy("fox", 0), g.Player.Pos + Vector2.right * 8, null);
                    yield return F(12); Shot("05b_minimap_enemy");
                    Object.Destroy(minimapEnemy.gameObject);
                    g.Player.PendingEvolutions = 0;
                    g.OpenOverlay(GameState.Paused); yield return F(3); Shot("06_pause_abilities");
                    g.Ui.OpenTab("attrs"); yield return F(3); Shot("07_pause_attrs");
                    g.Ui.OpenTab("chronicle"); yield return F(3); Shot("08_pause_chronicle");
                    g.Ui.OpenTab("settings"); yield return F(3); Shot("09_pause_settings");
                    g.Ui.OpenTab("graphics"); yield return F(3); Shot("09b_pause_graphics");
                    Click(Find<PauseView>(), "Design"); yield return F(3); Shot("09b_pause_design");
                    g.CloseOverlay(); yield return F(30); Shot("09c_world_designed");
                    // A pictured city (local stand-in pictures): street grid, buildings along the streets, two-variant ground
                    var home = g.Player.Pos;
                    g.ApplyDesign(DesignStore.Commit(DesignTestData.Pictured(g.Player)));
                    var spot = WorldStreamer.I.NearestWalkable(new Vector2(42, -42), g.Player.radius, 8, false);
                    g.Player.Pos = spot; WorldStreamer.I.Warm(spot); CameraRig.I.Snap(spot);
                    yield return F(60); Shot("09d_city_layout");
                    g.Player.Pos = home; WorldStreamer.I.Warm(home); CameraRig.I.Snap(home);
                    yield return F(10);
                    var npc = Interactable.All.Where(i => i != null && i.kind == "npc_storyteller").FirstOrDefault()
                              ?? Interactable.All.FirstOrDefault(i => i != null && i.kind.StartsWith("npc_"));
                    if (npc != null) { Dialogue.Open(g, npc); yield return F(3); Shot("10_dialogue"); g.CloseOverlay(); yield return F(2); }
                    var merchant = Interactable.All.FirstOrDefault(i => i != null && i.kind == "npc_merchant");
                    if (merchant != null)
                    {
                        g.Player.Gold = 1000; g.Player.DashChargesMax = 4; g.Player.Potions = 5;
                        Dialogue.Open(g, merchant); yield return F(5); Shot("10b_merchant_disabled");
                        Shot("10d_merchant_narrow", 640, 720);
                        var optionsScroll = Find<DialogueView>().GetComponentsInChildren<ScrollRect>().FirstOrDefault(s => s.name == "OptionsScroll");
                        if (optionsScroll != null) optionsScroll.verticalNormalizedPosition = 0;
                        yield return F(3); Shot("10c_merchant_bottom");
                        g.CloseOverlay(); yield return F(2);
                    }
                    g.Player.PendingEvolutions = 1; g.BeginEvolution();
                    for (int i = 0; i < 300 && g.Offer == null; i++) yield return null;
                    yield return F(60); Shot("11_evolve");
                    if (g.Offer != null && g.Offer.Count > 0)
                    {
                        foreach (var ability in g.Offer)
                        {
                            ability.source = "ai";
                            ability.desc = string.Concat(Enumerable.Repeat("분신이 남긴 흔적이 이어져 새로운 힘을 깨웁니다. ", 12));
                            ability.semanticReason = string.Concat(Enumerable.Repeat("플레이의 움직임과 공격 방식이 능력에 반영됩니다. ", 5));
                        }
                        yield return F(5); Shot("11b_evolve_long_text");
                        Shot("11d_evolve_narrow", 640, 720);
                        foreach (var scroll in Find<EvolveView>().GetComponentsInChildren<ScrollRect>())
                            if (scroll.name == "DetailsScroll") scroll.verticalNormalizedPosition = 0;
                        yield return F(3); Shot("11c_evolve_long_text_bottom");
                        // Render the chat warning without enabling AI or making any network requests.
                        var evolve = Find<EvolveView>();
                        var serialized = new SerializedObject(evolve);
                        var chatPanel = (GameObject)serialized.FindProperty("chatPanel").objectReferenceValue;
                        var warning = (Text)serialized.FindProperty("chatWarning").objectReferenceValue;
                        var panel = (RectTransform)serialized.FindProperty("panel").objectReferenceValue;
                        chatPanel.SetActive(true); warning.gameObject.SetActive(true);
                        warning.text = "대화에는 40 골드가 필요하다";
                        panel.sizeDelta = new Vector2(panel.sizeDelta.x, 330);
                        Shot("11e_evolve_chat_warning");
                    }
                    var legacy = g.Offer?.FirstOrDefault()?.Clone();
                    g.SkipEvolution(); yield return F(3);
                    if (legacy != null)
                    {
                        var grave = new GraveRecord { heroName = "지난 영웅", abilities = { AbilityRecord.From(legacy) } };
                        var it = Object.Instantiate(DB.Asset.interactablePrefab);
                        it.kind = "grave"; it.data = grave;
                        Dialogue.Open(g, it); yield return F(3);
                        Dialogue.Current.options[0].act(); yield return F(3); Shot("10e_legacy_description");
                        g.CloseOverlay(); Object.Destroy(it.gameObject); yield return F(2);
                    }
                    Dialogue.Current = new DialoguePage
                    {
                        speaker = "긴 대화 배치 검증",
                        text = string.Concat(Enumerable.Repeat("아주 긴 설명도 다른 버튼이나 대사를 덮지 않고 끝까지 읽을 수 있어야 합니다.\n", 20)),
                    };
                    for (int i = 0; i < 9; i++) Dialogue.Current.options.Add(new DialogueOption(
                        $"{i + 1}번째 긴 선택지: 줄바꿈이 필요한 설명을 포함해도 다음 버튼과 겹치지 않아야 합니다. 길이가 늘어난 선택지도 읽을 수 있습니다.", () => { }));
                    g.OpenOverlay(GameState.Dialogue); yield return F(5); Shot("10f_dialogue_long_text");
                    foreach (var scroll in Find<DialogueView>().GetComponentsInChildren<ScrollRect>()) scroll.verticalNormalizedPosition = 0;
                    yield return F(3); Shot("10g_dialogue_long_text_bottom");
                    g.CloseOverlay(); yield return F(2);
                    g.OpenOverlay(GameState.Map); yield return F(5); Shot("12_map");
                    // Widest zoom with the whole explored area marked: icons must sit on the terrain they belong to
                    foreach (var s in World.I.Gen.StructuresNear(Vector2.zero, 200)) if (s.landmark) World.I.Discover(s.id);
                    for (int cy = -14; cy <= 14; cy++) for (int cx = -14; cx <= 14; cx++) { var key = cx + "," + cy; if (!g.World.exploredChunks.Contains(key)) g.World.exploredChunks.Add(key); }
                    var mv = Find<MapView>(); mv.Zoom = 0; yield return F(5); Shot("12b_map_widest");
                    mv.Zoom = 3;
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
