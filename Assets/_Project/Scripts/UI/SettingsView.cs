using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Settings + key bindings. Used by both scenes (title "설정" and the pause menu's 설정/키 설정 tabs).
    /// </summary>
    public class SettingsView : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] GameObject settingsPage;
        [SerializeField] Slider sfx, music, shake;
        [SerializeField] Button damageNumbers, aiToggle, openFolder;
        [SerializeField] Text keyState, keyHint, modelHint, lastCall;
        [SerializeField] Button modelTemplate;
        [Header("Key bindings")]
        [SerializeField] GameObject keysPage;
        [SerializeField] Button keyTemplate;          // label child "Label", key text on the button
        [SerializeField] Button resetKeys;
        [Header("Graphics")]
        [SerializeField] GameObject graphicsPage;
        [SerializeField] Button[] presetButtons = new Button[3];     // 낮음 / 보통 / 높음 (GraphicsQuality.Presets)
        [SerializeField] Button[] fpsButtons = new Button[4];        // GraphicsQuality.FpsCaps: 30 / 60 / 120 / 모니터
        [SerializeField] Button[] effectButtons = new Button[3];     // 효과 낮음 / 보통 / 높음
        [SerializeField] Button[] postButtons = new Button[3];       // 후처리 끔 / 가볍게 / 전체
        [SerializeField] Button showFps;
        [SerializeField] Text graphicsHint;
        [Header("Page tabs (title screen only)")]
        [SerializeField] Button settingsTab, keysTab, graphicsTab, backButton;
        [SerializeField] Color selectedTint = new Color(1f, 0.95f, 0.7f);

        public const int SettingsPage = 0, KeysPage = 1, GraphicsPage = 2;

        readonly List<Button> models = new List<Button>();
        readonly List<Button> keys = new List<Button>();
        Act? rebinding;
        bool built;

        public System.Action OnBack;
        public bool Rebinding => rebinding != null;

        void Build()
        {
            if (built) return;
            built = true;
            var s = Session.Settings;
            sfx.onValueChanged.AddListener(v => { s.sfx = v; Sfx.Volume = v; Save(); });
            music.onValueChanged.AddListener(v => { s.music = v; Sfx.SetMusicVolume(v); Save(); });
            shake.onValueChanged.AddListener(v => { s.shakeScale = v; s.screenShake = v > 0.01f; Save(); });
            UIKit.Bind(damageNumbers, () => { s.damageNumbers = !s.damageNumbers; Save(); });
            UIKit.Bind(aiToggle, () => { s.aiEnabled = !s.aiEnabled; Save(); });
            UIKit.Bind(openFolder, () => Application.OpenURL("file:///" + Application.persistentDataPath));

            UIKit.Pool(modelTemplate, models, AiClient.Models.Length);
            for (int i = 0; i < models.Count; i++)
            {
                var m = AiClient.Models[i];
                UIKit.Label(models[i], m.label);
                models[i].onClick.RemoveAllListeners();
                models[i].onClick.AddListener(() => { s.aiModel = m.id; Sfx.Play("ui"); Save(); });
            }

            UIKit.Pool(keyTemplate, keys, Controls.Defaults.Length);
            for (int i = 0; i < keys.Count; i++)
            {
                var d = Controls.Defaults[i];
                UIKit.Set(keys[i].transform.Find("Label")?.GetComponent<Text>(), d.label);
                keys[i].onClick.RemoveAllListeners();
                keys[i].onClick.AddListener(() =>
                {
                    if (rebinding != null) return;
                    rebinding = d.act;
                    Controls.Rebind(d.act, () => { rebinding = null; Controls.Save(Session.Settings); });
                });
            }
            UIKit.Bind(resetKeys, () => { Controls.ResetAll(); Controls.Save(Session.Settings); });

            for (int i = 0; i < presetButtons.Length; i++) { int k = i; UIKit.Bind(presetButtons[i], () => { GraphicsQuality.ApplyPreset(s, k); Save(); }); }
            for (int i = 0; i < fpsButtons.Length && i < GraphicsQuality.FpsCaps.Length; i++) { int k = i; UIKit.Bind(fpsButtons[i], () => { s.fpsCap = GraphicsQuality.FpsCaps[k]; ApplyGraphics(); }); }
            for (int i = 0; i < effectButtons.Length; i++) { int k = i; UIKit.Bind(effectButtons[i], () => { s.effects = k; ApplyGraphics(); }); }
            for (int i = 0; i < postButtons.Length; i++) { int k = i; UIKit.Bind(postButtons[i], () => { s.postFx = k; ApplyGraphics(); }); }
            UIKit.Bind(showFps, () => { s.showFps = !s.showFps; ApplyGraphics(); });

            UIKit.Bind(settingsTab, () => Page(SettingsPage));
            UIKit.Bind(keysTab, () => Page(KeysPage));
            UIKit.Bind(graphicsTab, () => Page(GraphicsPage));
            UIKit.Bind(backButton, () => { Controls.Save(Session.Settings); OnBack?.Invoke(); });
        }

        static void Save() => SaveSystem.SaveSettings(Session.Settings);

        static void ApplyGraphics() { GraphicsQuality.Apply(Session.Settings); Save(); }

        /// <summary>Show the settings, key bindings or graphics page.</summary>
        public void Page(int page)
        {
            Build();
            if (graphicsPage == null && page == GraphicsPage) page = SettingsPage;   // prefab without the graphics page yet
            UIKit.Show(settingsPage, page == SettingsPage);
            UIKit.Show(keysPage, page == KeysPage);
            UIKit.Show(graphicsPage, page == GraphicsPage);
            Tint(settingsTab, page == SettingsPage);
            Tint(keysTab, page == KeysPage);
            Tint(graphicsTab, page == GraphicsPage);
        }

        void Tint(Button b, bool on) { if (b != null) b.image.color = on ? selectedTint : Color.white; }

        static readonly string[] FpsHints =
        {
            "초당 30프레임으로 고정한다. 노트북 배터리·발열을 줄인다.",
            "초당 60프레임으로 고정한다 (수직 동기화 끔). 끊김이 느껴질 때 먼저 시도해 볼 것.",
            "초당 120프레임까지 그린다 (수직 동기화 끔). 고주사율 모니터용.",
            "모니터 주사율에 맞춘다 (수직 동기화). 화면 찢어짐이 없다.",
        };

        void OnEnable()
        {
            if (Session.World == null) return;
            Build();
            var s = Session.Settings;
            sfx.SetValueWithoutNotify(s.sfx);
            music.SetValueWithoutNotify(s.music);
            shake.SetValueWithoutNotify(s.shakeScale);
            rebinding = null;
        }

        static RectTransform Rt(Button[] list, int i) => list != null && i < list.Length && list[i] != null ? (RectTransform)list[i].transform : null;

        void Update()
        {
            if (!built) return;
            var s = Session.Settings;
            if (settingsPage == null || settingsPage.activeSelf)
            {
                UIKit.Label(damageNumbers, "피해 숫자: " + (s.damageNumbers ? "켜짐" : "꺼짐"));
                UIKit.Label(aiToggle, "AI: " + (s.aiEnabled ? "켜짐" : "꺼짐"));
                UIKit.Set(keyState, AiClient.HasKey ? "<color=#8bff9a>API 키 확인됨</color>" : "<color=#ff8a8a>API 키 없음</color> — 로컬 모드로 실행 중");
                UIKit.Set(keyHint, $"키 위치: 환경 변수 OPENAI_API_KEY 또는 키만 적힌 파일:\n{SaveSystem.KeyFileHint}");
                string hint = null;
                for (int i = 0; i < models.Count; i++)
                {
                    var m = AiClient.Models[i];
                    bool sel = s.aiModel == m.id;
                    models[i].image.color = sel ? selectedTint : Color.white;
                    if (UIKit.Hovered((RectTransform)models[i].transform) || (sel && hint == null)) hint = $"{m.label}: {m.desc}";
                }
                UIKit.Set(modelHint, hint ?? "");
                UIKit.Set(lastCall, string.IsNullOrEmpty(AiClient.LastStatus) ? "" : $"마지막 호출: {AiClient.LastStatus}");
            }
            if (graphicsPage != null && graphicsPage.activeSelf)
            {
                int preset = GraphicsQuality.PresetOf(s);
                for (int i = 0; i < presetButtons.Length; i++) Tint(presetButtons[i], i == preset);
                int fps = System.Array.IndexOf(GraphicsQuality.FpsCaps, s.fpsCap);
                for (int i = 0; i < fpsButtons.Length; i++) Tint(fpsButtons[i], i == fps);
                for (int i = 0; i < effectButtons.Length; i++) Tint(effectButtons[i], i == s.effects);
                for (int i = 0; i < postButtons.Length; i++) Tint(postButtons[i], i == s.postFx);
                UIKit.Label(showFps, "FPS 표시: " + (s.showFps ? "켜짐" : "꺼짐"));
                // Hovered button explains itself; otherwise the general advice
                string hint = "끊김이 느껴지면 프리셋을 '낮음'으로 바꾸거나 프레임 제한을 60으로 두세요. 게임 중 마우스 휠로 화면을 확대·축소할 수 있습니다.";
                for (int i = 0; i < fpsButtons.Length && i < FpsHints.Length; i++) if (UIKit.Hovered((RectTransform)fpsButtons[i]?.transform)) hint = FpsHints[i];
                if (UIKit.Hovered(Rt(effectButtons, 0)) || UIKit.Hovered(Rt(effectButtons, 1)) || UIKit.Hovered(Rt(effectButtons, 2)))
                    hint = "파티클·잔상·날씨 입자의 양. 전투가 몰릴 때 끊기면 낮춘다.";
                if (UIKit.Hovered(Rt(postButtons, 0)) || UIKit.Hovered(Rt(postButtons, 1)) || UIKit.Hovered(Rt(postButtons, 2)))
                    hint = "빛 번짐(블룸)·색 보정. '가볍게'는 가장 무거운 빛 번짐만 끈다. 내장 그래픽이면 '끔'이 가장 가볍다.";
                UIKit.Set(graphicsHint, hint);
            }
            if (keysPage != null && keysPage.activeSelf)
                for (int i = 0; i < keys.Count; i++)
                {
                    var a = Controls.Defaults[i].act;
                    string k = rebinding == a ? "<color=#fff2b3>키를 누르세요…</color>" : Controls.KeyName(a);
                    if (rebinding != a && Controls.Conflict(a).HasValue) k = $"<color=#ff8a8a>{k}</color>";
                    UIKit.Label(keys[i], k);
                    keys[i].interactable = rebinding == null;
                }
            if (backButton != null && backButton.gameObject.activeInHierarchy && rebinding == null && Controls.KeyDown(Key.Escape)) { Controls.Save(s); OnBack?.Invoke(); }
        }
    }
}
