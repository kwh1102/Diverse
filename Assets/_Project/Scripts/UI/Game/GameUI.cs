using UnityEngine;
using UnityEngine.EventSystems;

namespace Diverse
{
    /// <summary>
    /// Game scene canvas root: shows the view for the current Game.State. Every view is a child of this canvas
    /// in Game.unity (built from Prefabs/UI), so all of them can be laid out in the editor.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [SerializeField] HudView hud;
        [SerializeField] PauseView pause;
        [SerializeField] DialogueView dialogue;
        [SerializeField] DeathView death;
        [SerializeField] EvolveView evolve;
        [SerializeField] MapView map;
        [SerializeField] ToastView toasts;
        [SerializeField] GameObject dim;            // full-screen shade behind overlays

        Game g;

        /// <summary>Esc is being used by a text field or key rebinding, so it must not close the overlay.</summary>
        public bool ConsumesEscape => pause.Rebinding || evolve.Typing;

        public void Init(Game game)
        {
            g = game;
            hud.Init(game);
            pause.Init(game);
            dialogue.Init(game);
            death.Init(game);
            evolve.Init(game);
            map.Init(game);
            Refresh();
        }

        public void Toast(string text) => toasts.Add(text);

        public void OnOverlayOpened(GameState s, string tab)
        {
            Refresh();
            if (s == GameState.Map) map.Opened();
            if (s == GameState.Paused) pause.Open(tab ?? "abilities");
        }

        public void OpenTab(string t) => pause.Open(t);
        public void CloseDialogue() { Dialogue.Current = null; }
        public void ShowDeath(GraveRecord gr) { death.Show(gr); Refresh(); }

        void Refresh()
        {
            if (g == null) return;
            var s = g.State;
            UIKit.Show(hud, s != GameState.Map);
            UIKit.Show(pause, s == GameState.Paused);
            UIKit.Show(dialogue, s == GameState.Dialogue);
            UIKit.Show(death, s == GameState.Dead && death.HasRecord);
            UIKit.Show(evolve, s == GameState.Evolving);
            UIKit.Show(map, s == GameState.Map);
            UIKit.Show(dim, s == GameState.Paused || s == GameState.Evolving || s == GameState.Map || (s == GameState.Dead && death.HasRecord));
        }

        void Update()
        {
            if (g == null) return;
            Refresh();
            if (hud.isActiveAndEnabled) hud.Tick();
            // Clicks on UI must not also move/attack in the world
            g.UiCapturingMouse = g.State != GameState.Playing || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        }
    }
}
