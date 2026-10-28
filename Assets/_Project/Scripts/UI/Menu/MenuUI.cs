using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// MainMenu scene: loads the saves, then switches between the title, design studio, character select and settings screens.
    /// All of them are children of this canvas in MainMenu.unity, so they can be laid out in the editor.
    /// A new life starts in the design studio; the old character select is still reachable from it ("기본 캐릭터").
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public enum Screen { Title, CharacterSelect, Settings, Design }

        [SerializeField] TitleView title;
        [SerializeField] CharacterSelectView select;
        [SerializeField] SettingsView settings;
        [SerializeField] DesignStudioView design;

        void Awake()
        {
            Application.targetFrameRate = 120;
            GameTime.Reset();
            Time.timeScale = 1;
            Session.EnsureLoaded();
            title.Init(this);
            select.Init(this);
            if (design != null) design.Init(this);
            settings.OnBack = () => Show(Screen.Title);
        }

        void Start()
        {
            Sfx.PlayMusic(true);
            bool toSelect = Session.OpenCharacterSelect && !Session.HasRunToContinue;
            Session.OpenCharacterSelect = false;
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
            if (s == Screen.Settings) settings.Page(false);
        }
    }
}
