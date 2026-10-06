using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Dialogue box (Game scene): portrait, speaker, text, numbered options (click or 1~9).</summary>
    public class DialogueView : MonoBehaviour
    {
        [SerializeField] Image portrait;
        [SerializeField] Text speaker, body;
        [SerializeField] Button optionTemplate;
        readonly List<Button> options = new List<Button>();
        DialoguePage shown;
        Game g;

        public void Init(Game game) => g = game;

        void Update()
        {
            var d = Dialogue.Current;
            if (d == null) { g.CloseOverlay(); return; }
            if (d != shown) Build(d);
            for (int i = 0; i < d.options.Count && i < 9; i++)
                if (d.options[i].enabled && Controls.KeyDown(Key.Digit1 + i)) { d.options[i].act?.Invoke(); return; }
        }

        void Build(DialoguePage d)
        {
            shown = d;
            UIKit.Sprite(portrait, d.portrait);
            UIKit.Set(speaker, $"<b><color=#fff2b3>{d.speaker}</color></b>");
            UIKit.Set(body, d.text);
            UIKit.Pool(optionTemplate, options, d.options.Count);
            for (int i = 0; i < d.options.Count; i++)
            {
                var o = d.options[i];
                UIKit.Label(options[i], $"{i + 1}. {o.text}");
                options[i].interactable = o.enabled;
                UIKit.Bind(options[i], () => o.act?.Invoke());
            }
        }

        void OnDisable() => shown = null;
    }
}
