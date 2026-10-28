using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Live design-studio test against the real API (needs openai_key.txt in the scratch save folder set by DIVERSE_SAVE_DIR).
    /// Batch, play mode: Unity -batchmode -executeMethod Diverse.EditorTools.DesignLiveTest.RunBatch
    /// → Logs/DesignLive/result.txt (pack JSON, tokens, cost) + upscaled sprite PNGs. Exit 0 = the AI answered and the hero was drawn.
    /// </summary>
    public static class DesignLiveTest
    {
        const string RunningKey = "Diverse.DesignLiveTest.Running";
        const string OutDir = "Logs/DesignLive";
        static readonly string[] Prompts =
        {
            "마법사 모자를 쓴 보라색 문어",
            "은색 권총을 들고 총알을 빠르게 발사하는 공격. 데미지는 100배로!",
            "사탕으로 만든 숲과 우유가 흐르는 강",
            "모든 적이 태엽 장난감 병정과 로봇",
        };

        [InitializeOnLoadMethod]
        static void Reattach()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (EditorApplication.isPlaying && Object.FindFirstObjectByType<Runner>() == null) new GameObject("DesignLiveTestRunner").AddComponent<Runner>();
        }

        public static void RunBatch()
        {
            SessionState.SetBool(RunningKey, true);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(UIBuilder.MenuScenePath);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Object.FindFirstObjectByType<Runner>() == null) new GameObject("DesignLiveTestRunner").AddComponent<Runner>();
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayMode;
                SessionState.SetBool(RunningKey, false);
                var path = Path.Combine(OutDir, "result.txt");
                EditorApplication.Exit(File.Exists(path) && File.ReadAllText(path).StartsWith("PASS") ? 0 : 1);
            }
        }

        class Runner : MonoBehaviour
        {
            void Awake() => DontDestroyOnLoad(gameObject);

            IEnumerator Start()
            {
                Directory.CreateDirectory(OutDir);
                var log = new StringBuilder();
                for (int i = 0; i < 5; i++) yield return null;
                var menu = Object.FindFirstObjectByType<MenuUI>(FindObjectsInactive.Include);
                menu.Show(MenuUI.Screen.Design);
                yield return null;
                var studio = Object.FindFirstObjectByType<DesignStudioView>(FindObjectsInactive.Include);
                Button B(string n) => studio.GetComponentsInChildren<Button>(true).First(b => b.name == n);
                for (int i = 0; i < 4; i++) { B("Tab " + i).onClick.Invoke(); studio.GetComponentInChildren<InputField>().text = Prompts[i]; }
                var gen = B("Generate");
                float t0 = Time.realtimeSinceStartup;
                gen.onClick.Invoke();
                yield return null;
                while (!gen.interactable && Time.realtimeSinceStartup - t0 < AiClient.TimeoutSeconds + ImageClient.TimeoutSeconds + 10) yield return null;
                float secs = Time.realtimeSinceStartup - t0;
                yield return null;

                var p = DesignRuntime.Active;
                bool ai = p.source == "ai" && p.hero.image != null;
                int pin = AiClient.LastPromptTokens, pout = AiClient.LastCompletionTokens;
                var cost = (float?)DesignAi.LastCostUsd;
                log.AppendLine(ai ? "PASS" : "FAIL (fell back to local: " + AiClient.LastStatus + ")");
                log.AppendLine($"model={Game.Settings.aiModel} time={secs:0.0}s prompt_tokens={pin} completion_tokens={pout} cost_usd={(cost ?? -1):0.000000}");
                log.AppendLine("status=" + studio.GetComponentsInChildren<Text>(true).First(x => x.name == "Status").text.Replace("\n", " | "));
                log.AppendLine("info=" + studio.GetComponentsInChildren<Text>(true).First(x => x.name == "Info").text.Replace("\n", " | "));
                log.AppendLine(JsonUtility.ToJson(p, true));

                var kind = DesignRuntime.Chassis(p);
                Save("weapon", Art.Weapon(kind));
                Save("projectile", Art.Bolt());
                Save("hero", Art.Rabbit(DesignStore.PreviewCostume(p), Pose.Idle0));
                Save("tree", Art.Tree(Biome.Meadow, 1));
                Save("rock", Art.Rock(Biome.Meadow, 0));
                foreach (var e in DB.Enemies.Values) Save("enemy_" + e.id, Art.Enemy(e.sprite, 0));
                log.AppendLine($"power={DesignCombat.Power(DesignCombat.For(p) ?? DB.W(kind)):0.0} budget={DesignCombat.Budget:0.0}");
                File.WriteAllText(Path.Combine(OutDir, "result.txt"), log.ToString());
                EditorApplication.ExitPlaymode();
            }

            /// <summary>Sprite → PNG at 8x (nearest neighbour) on a dark backdrop.</summary>
            static void Save(string name, Sprite s)
            {
                if (s == null || !s.texture.isReadable) return;
                var r = s.textureRect;
                int w = (int)r.width, h = (int)r.height, k = 8;
                var src = s.texture.GetPixels32();
                var tex = new Texture2D(w * k + 16, h * k + 16, TextureFormat.RGBA32, false);
                var bg = new Color32(0x1b, 0x15, 0x24, 255);
                var px = Enumerable.Repeat(bg, tex.width * tex.height).ToArray();
                for (int y = 0; y < h * k; y++)
                    for (int x = 0; x < w * k; x++)
                    {
                        var c = src[((int)r.y + y / k) * s.texture.width + (int)r.x + x / k];
                        if (c.a > 0) px[(y + 8) * tex.width + x + 8] = c;
                    }
                tex.SetPixels32(px);
                File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            }
        }
    }
}
