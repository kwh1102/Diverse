using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// MainMenu scene: loads the saves, then switches between the title, character select and settings screens.
    /// All three are children of this canvas in MainMenu.unity, so they can be laid out in the editor.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        public enum Screen { Title, CharacterSelect, Settings }

        [SerializeField] TitleView title;
        [SerializeField] CharacterSelectView select;
        [SerializeField] SettingsView settings;

        void Awake()
        {
            Application.targetFrameRate = 120;
            GameTime.Reset();
            Time.timeScale = 1;
            Session.EnsureLoaded();
            title.Init(this);
            select.Init(this);
            settings.OnBack = () => Show(Screen.Title);
        }

        void Start()
        {
            Sfx.PlayMusic(true);
            bool toSelect = Session.OpenCharacterSelect && !Session.HasRunToContinue;
            Session.OpenCharacterSelect = false;
            Show(toSelect ? Screen.CharacterSelect : Screen.Title);
        }

        public void Show(Screen s)
        {
            UIKit.Show(title, s == Screen.Title);
            UIKit.Show(select, s == Screen.CharacterSelect);
            UIKit.Show(settings, s == Screen.Settings);
            if (s == Screen.Settings) settings.Page(false);
        }
    }
}
