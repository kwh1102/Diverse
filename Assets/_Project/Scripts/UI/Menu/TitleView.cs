using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Title screen (MainMenu scene): only 모드·세계 선택 / 설정 / 종료. Continuing, ending a life, starting one and
    /// creating or deleting worlds all happen per world on the slot screen (SaveSelectView).
    /// </summary>
    public class TitleView : MonoBehaviour
    {
        [SerializeField] RectTransform logo;
        [SerializeField] Image[] rabbits = new Image[6];
        [SerializeField] Button slotButton, settingsButton, quitButton;
        [SerializeField] Text footer;

        MenuUI menu;
        Vector2 logoBase;

        public void Init(MenuUI m)
        {
            menu = m;
            if (logo != null) logoBase = logo.anchoredPosition;
            UIKit.Bind(slotButton, () => menu.Show(MenuUI.Screen.Slots));
            UIKit.Bind(settingsButton, () => menu.Show(MenuUI.Screen.Settings));
            UIKit.Bind(quitButton, () =>
            {
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
        }

        void Update()
        {
            float t = Time.unscaledTime;
            if (logo != null) logo.anchoredPosition = logoBase + Vector2.up * Mathf.Sin(t * 1.5f) * 4f;
            for (int i = 0; i < rabbits.Length && i < DB.Costumes.Count; i++)
                UIKit.Sprite(rabbits[i], Art.Rabbit(DB.Costumes[i], (int)(t * 3 + i) % 2 == 0 ? Pose.Idle0 : Pose.Idle1), i >= 3);

            var s = Session.Settings;
            string ai = AiClient.HasKey ? (s.aiEnabled ? $"AI: 켜짐 ({AiClient.ModelLabel(s.aiModel)})" : "AI: 꺼짐 (키 있음)") : "AI: 로컬 모드 (API 키 없음)";
            UIKit.Set(footer, ai);
        }
    }
}
