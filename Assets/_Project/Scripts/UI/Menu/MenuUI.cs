using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// MainMenu scene: the title, the mode + save slot screen (where worlds are continued, started, ended, deleted),
    /// the design studio, the old character select and settings.
    /// All of them are children of this canvas in MainMenu.unity, so they can be laid out in the editor.
    /// A new life starts in the design studio; the old character select is still reachable from it ("기본 캐릭터").
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public enum Screen { Title, CharacterSelect, Settings, Design, Slots }

        [SerializeField] TitleView title;
        [SerializeField] CharacterSelectView select;
        [SerializeField] SettingsView settings;
        [SerializeField] DesignStudioView design;
        [SerializeField] SaveSelectView slots;

        void Awake()
        {
            GameTime.Reset();
            Time.timeScale = 1;
            Session.EnsureLoaded();
            title.Init(this);
            select.Init(this);
            if (design != null) design.Init(this);
            if (slots != null) slots.Init(this);
            settings.OnBack = () => Show(Screen.Title);
        }

        void Start()
        {
            Sfx.PlayMusic(true);
            bool toSelect = Session.OpenCharacterSelect && !Session.HasRunToContinue;
            Session.OpenCharacterSelect = false;
            // The title first (모드·세계 선택 / 설정 / 종료); after a death, straight to designing the next life of that world
            Show(toSelect ? NewLifeScreen : Screen.Title);
        }

        /// <summary>Where "start a new life" leads: the design studio (or the character select if the canvas has no studio yet).</summary>
        public Screen NewLifeScreen => design != null ? Screen.Design : Screen.CharacterSelect;

        public void Show(Screen s)
        {
            UIKit.Show(title, s == Screen.Title);
            UIKit.Show(select, s == Screen.CharacterSelect);
            UIKit.Show(settings, s == Screen.Settings);
            UIKit.Show(design, s == Screen.Design);
            UIKit.Show(slots, s == Screen.Slots);
            if (s == Screen.Settings) settings.Page(SettingsView.SettingsPage);
        }
    }
}
