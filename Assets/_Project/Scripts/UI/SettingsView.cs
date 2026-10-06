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
        [Header("Page tabs (title screen only)")]
        [SerializeField] Button settingsTab, keysTab, backButton;
        [SerializeField] Color selectedTint = new Color(1f, 0.95f, 0.7f);

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
            UIKit.Bind(settingsTab, () => Page(false));
            UIKit.Bind(keysTab, () => Page(true));
            UIKit.Bind(backButton, () => { Controls.Save(Session.Settings); OnBack?.Invoke(); });
        }

        static void Save() => SaveSystem.SaveSettings(Session.Settings);

        /// <summary>Show the settings page or the key bindings page.</summary>
        public void Page(bool keysOn)
        {
            Build();
            UIKit.Show(settingsPage, !keysOn);
            UIKit.Show(keysPage, keysOn);
            if (settingsTab != null) settingsTab.image.color = keysOn ? Color.white : selectedTint;
            if (keysTab != null) keysTab.image.color = keysOn ? selectedTint : Color.white;
        }

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
