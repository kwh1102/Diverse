using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Title screen (MainMenu scene): continue / new life / settings / new world / quit.</summary>
    public class TitleView : MonoBehaviour
    {
        [SerializeField] RectTransform logo;
        [SerializeField] Image[] rabbits = new Image[6];
        [SerializeField] Button continueButton, abandonButton, startButton, settingsButton, resetButton, quitButton;
        [SerializeField] Text footer;

        MenuUI menu;
        bool confirmReset, confirmAbandon;
        Vector2 logoBase;

        public void Init(MenuUI m)
        {
            menu = m;
            if (logo != null) logoBase = logo.anchoredPosition;
            UIKit.Bind(continueButton, () => { confirmAbandon = false; Session.ContinueLife(); });
            UIKit.Bind(abandonButton, () =>
            {
                // Abandoning a life counts as a death: the hero gets a grave where they stood
                if (confirmAbandon) { Session.AbandonRun(); confirmAbandon = false; menu.Show(MenuUI.Screen.CharacterSelect); }
                else confirmAbandon = true;
            });
            UIKit.Bind(startButton, () => menu.Show(MenuUI.Screen.CharacterSelect));
            UIKit.Bind(settingsButton, () => menu.Show(MenuUI.Screen.Settings));
            UIKit.Bind(resetButton, () =>
            {
                if (confirmReset) { Session.ResetWorld(); confirmReset = false; }
                else confirmReset = true;
            });
            UIKit.Bind(quitButton, () =>
            {
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
        }

        void OnEnable() { confirmReset = confirmAbandon = false; }

        void Update()
        {
            var w = Session.World;
            if (w == null) return;
            float t = Time.unscaledTime;
            if (logo != null) logo.anchoredPosition = logoBase + Vector2.up * Mathf.Sin(t * 1.5f) * 4f;
            for (int i = 0; i < rabbits.Length && i < DB.Costumes.Count; i++)
                UIKit.Sprite(rabbits[i], Art.Rabbit(DB.Costumes[i], (int)(t * 3 + i) % 2 == 0 ? Pose.Idle0 : Pose.Idle1), i >= 3);

            bool cont = Session.HasRunToContinue;
            UIKit.Show(continueButton, cont);
            UIKit.Show(abandonButton, cont);
            UIKit.Show(startButton, !cont);
            UIKit.Show(resetButton, w.lifeCount > 0);
            if (cont)
            {
                var run = Session.PendingRun;
                UIKit.Label(continueButton, $"이어하기 — {run.heroName} Lv.{run.level}");
                UIKit.Label(abandonButton, confirmAbandon ? "정말 이 삶을 끝낼까요? (무덤이 남음)" : "이 삶을 끝내고 새로 시작");
            }
            else UIKit.Label(startButton, w.lifeCount == 0 ? "새로운 삶 시작" : $"다음 삶 시작 ({w.lifeCount + 1}번째 삶)");
            UIKit.Label(resetButton, confirmReset ? "정말 세계를 초기화할까요?" : "새로운 세계 시작");

            var s = Session.Settings;
            string ai = AiClient.HasKey ? (s.aiEnabled ? $"AI 진화: 켜짐 ({AiClient.ModelLabel(s.aiModel)})" : "AI 진화: 꺼짐 (키 있음)") : "AI 진화: 로컬 모드 (API 키 없음)";
            UIKit.Set(footer, $"{ai}   ·   세계 시드 {w.seed}   ·   기억의 조각 {w.memoryShards}   ·   탑 {w.towerFloor}층");
        }
    }
}
