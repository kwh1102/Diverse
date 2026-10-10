using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Diverse
{
    public enum Act { Move, Attack, Stop, Dash, SkillQ, SkillW, SkillE, SkillR, Interact, Map, Potion, Pause, Abilities, Chronicle }

    /// <summary>
    /// Input. Builds InputActions in code, and saves user changes as override JSON.
    /// Default layout: right-click move/attack (LoL style), QWER skills, Space dash, F interact, Tab map.
    /// </summary>
    public static class Controls
    {
        static InputActionMap map;
        static readonly Dictionary<Act, InputAction> acts = new Dictionary<Act, InputAction>();
        public static bool Initialized => map != null;

        public static readonly (Act act, string label, string path)[] Defaults =
        {
            (Act.Move, "이동 / 공격 대상 지정", "<Mouse>/rightButton"),
            (Act.Attack, "커서 방향 기본 공격", "<Mouse>/leftButton"),
            (Act.Stop, "정지", "<Keyboard>/s"),
            (Act.Dash, "대시", "<Keyboard>/space"),
            (Act.SkillQ, "스킬 1", "<Keyboard>/q"),
            (Act.SkillW, "스킬 2", "<Keyboard>/w"),
            (Act.SkillE, "스킬 3", "<Keyboard>/e"),
            (Act.SkillR, "스킬 4 (궁극기)", "<Keyboard>/r"),
            (Act.Interact, "상호작용", "<Keyboard>/f"),
            (Act.Map, "지도", "<Keyboard>/tab"),
            (Act.Potion, "회복약", "<Keyboard>/1"),
            (Act.Abilities, "능력 목록", "<Keyboard>/c"),
            (Act.Chronicle, "연대기", "<Keyboard>/j"),
            (Act.Pause, "일시정지 / 메뉴", "<Keyboard>/escape"),
        };

        public static void Init(Settings settings)
        {
            if (map != null) return;
            map = new InputActionMap("Gameplay");
            foreach (var d in Defaults)
            {
                var a = map.AddAction(d.act.ToString(), InputActionType.Button, d.path);
                acts[d.act] = a;
            }
            Load(settings);
            map.Enable();
        }

        public static InputAction Get(Act a) => acts[a];
        public static bool Down(Act a) => map != null && !Blocked(a) && acts[a].WasPressedThisFrame();
        public static bool Held(Act a) => map != null && !Blocked(a) && acts[a].IsPressed();
        public static bool Up(Act a) => map != null && acts[a].WasReleasedThisFrame();

        /// <summary>
        /// A text field has keyboard focus: keyboard-bound actions must not fire (typing "w" or Space in the chat
        /// would otherwise cast a skill or dash). Mouse-bound actions still work.
        /// </summary>
        public static bool Typing => TypingField != null && TypingField.isFocused;
        public static UnityEngine.UI.InputField TypingField;

        static bool Blocked(Act a) => Typing && acts[a].bindings[0].effectivePath.StartsWith("<Keyboard>");

        public static string KeyName(Act a)
        {
            if (map == null) return "?";
            // Letter keys: name them by the physical key. The display string follows the OS keyboard layout, so with the
            // Korean input source on it reads "ㅂ" for Q (the bindings themselves are physical and still work).
            string path = acts[a].bindings[0].effectivePath;
            const string kb = "<Keyboard>/";
            if (path != null && path.StartsWith(kb) && path.Length == kb.Length + 1 && char.IsLetterOrDigit(path[kb.Length]))
                return path.Substring(kb.Length).ToUpperInvariant();
            var s = acts[a].GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            return Pretty(s);
        }

        static string Pretty(string s) => s switch
        {
            "RMB" or "Right Button" or "Right Click" => "우클릭",
            "LMB" or "Left Button" or "Left Click" => "좌클릭",
            "MMB" or "Middle Button" => "휠클릭",
            "Space" => "Space",
            "Escape" => "Esc",
            _ => s.ToUpperInvariant(),
        };

        public static InputActionRebindingExtensions.RebindingOperation Rebind(Act a, System.Action onDone)
        {
            var action = acts[a];
            action.Disable();
            var op = action.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.08f)
                .OnComplete(o => { o.Dispose(); action.Enable(); onDone?.Invoke(); })
                .OnCancel(o => { o.Dispose(); action.Enable(); onDone?.Invoke(); });
            op.Start();
            return op;
        }

        /// <summary>Returns the action that already uses the same key (for conflict warnings).</summary>
        public static Act? Conflict(Act a)
        {
            string p = acts[a].bindings[0].effectivePath;
            foreach (var kv in acts)
                if (kv.Key != a && kv.Value.bindings[0].effectivePath == p) return kv.Key;
            return null;
        }

        public static void ResetAll()
        {
            map.RemoveAllBindingOverrides();
        }

        public static void Save(Settings s)
        {
            s.bindings.Clear();
            foreach (var kv in acts)
            {
                var b = kv.Value.bindings[0];
                if (!string.IsNullOrEmpty(b.overridePath)) s.bindings.Add(kv.Key + "=" + b.overridePath);
            }
            SaveSystem.SaveSettings(s);
        }

        static void Load(Settings s)
        {
            if (s?.bindings == null) return;
            foreach (var line in s.bindings)
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                if (System.Enum.TryParse<Act>(line.Substring(0, i), out var a) && acts.TryGetValue(a, out var action))
                    action.ApplyBindingOverride(0, line.Substring(i + 1));
            }
        }

        public static Vector2 MouseScreen => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0;
        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool KeyDown(Key k) => Keyboard.current != null && !Typing && Keyboard.current[k].wasPressedThisFrame;
    }
}
