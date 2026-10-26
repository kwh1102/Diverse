using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Diverse
{
    /// <summary>Opt-in test tools. F8 opens the panel; F9 teleports to the cursor.</summary>
    public sealed class DeveloperMode : MonoBehaviour
    {
        static DeveloperMode instance;
        bool enabledTools, infiniteGold;
        Rect panel = new Rect(20, 80, 600, 720);
        bool resizing;
        Vector2 resizeStart, sizeStart;
        Vector2 scroll;
        string level = "1", x = "0", y = "0";
        Font font;
        GUISkin panelSkin;
        int fontSize = 16;
        public static bool CapturingInput => instance != null && instance.enabledTools &&
            Game.I != null && Game.I.State != GameState.Evolving &&
            (instance.resizing || GUIUtility.keyboardControl != 0 || (Mouse.current != null && instance.panel.Contains(
                new Vector2(Mouse.current.position.ReadValue().x, Screen.height - Mouse.current.position.ReadValue().y))));

        void Awake() { instance = this; }
        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (panelSkin != null) Destroy(panelSkin);
        }

        void Update()
        {
            var p = Player.I;
            if (Keyboard.current?.f8Key.wasPressedThisFrame == true)
            {
                enabledTools = !enabledTools;
                resizing = false;
                GUIUtility.hotControl = 0;
                if (p != null) level = p.Level.ToString();
                GUIUtility.keyboardControl = 0;
            }
            if (!enabledTools || p == null || !p.Alive) return;
            if (infiniteGold) p.Gold = 100000000;
            if (Game.I.State == GameState.Playing && Keyboard.current?.f9Key.wasPressedThisFrame == true)
                p.DebugTeleport(CameraRig.I.MouseWorld());
        }

        void OnGUI()
        {
            if (!enabledTools || Player.I == null || Game.I.State == GameState.Evolving) return;
            if (font == null) font = Resources.Load<Font>("Fonts/Galmuri11");
            var oldSkin = GUI.skin;
            if (panelSkin == null) panelSkin = Instantiate(oldSkin);
            panelSkin.font = font;
            foreach (var style in new[] { panelSkin.label, panelSkin.button, panelSkin.textField,
                panelSkin.toggle, panelSkin.window, panelSkin.box })
                style.fontSize = fontSize;
            panelSkin.window.padding.top = fontSize + 12;
            panelSkin.toggle.fixedHeight = Mathf.Max(20, fontSize + 8);
            GUI.skin = panelSkin;
            panel.width = Mathf.Clamp(panel.width, Mathf.Min(360, Screen.width), Screen.width);
            panel.height = Mathf.Clamp(panel.height, Mathf.Min(300, Screen.height), Screen.height);
            panel.x = Mathf.Clamp(panel.x, 0, Mathf.Max(0, Screen.width - panel.width));
            panel.y = Mathf.Clamp(panel.y, 0, Mathf.Max(0, Screen.height - panel.height));
            var drawn = GUI.Window(GetInstanceID(), panel, DrawPanel, "Developer mode (F8)");
            if (resizing) drawn.size = panel.size;
            panel = drawn;
            GUI.skin = oldSkin;
        }

        void DrawPanel(int id)
        {
            var p = Player.I;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"글자 크기: {fontSize}", GUILayout.Width(fontSize * 8));
            fontSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(fontSize, 12, 32, GUILayout.MinWidth(80)));
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Level: " + p.Level);
            GUILayout.Label($"Pending evolutions: {p.PendingEvolutions}");
            if (p.Alive && p.PendingEvolutions > 0 && Game.I.State == GameState.Playing && !Game.I.Generating &&
                GUILayout.Button("Choose evolution"))
            {
                enabledTools = false;
                GUIUtility.keyboardControl = 0;
                Game.I.BeginEvolution();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-1")) { p.DebugSetLevel(p.Level - 1); level = p.Level.ToString(); }
            level = GUILayout.TextField(level, 3);
            if (GUILayout.Button("Apply") && int.TryParse(level, out int lv)) p.DebugSetLevel(lv);
            if (GUILayout.Button("+1")) { p.DebugSetLevel(p.Level + 1); level = p.Level.ToString(); }
            GUILayout.EndHorizontal();
            infiniteGold = GUILayout.Toggle(infiniteGold, "Unlimited gold");
            GUILayout.Label($"Position: {p.Pos.x:0.0}, {p.Pos.y:0.0} | F9: teleport to cursor");
            GUILayout.BeginHorizontal();
            x = GUILayout.TextField(x); y = GUILayout.TextField(y);
            if (GUILayout.Button("Teleport") && float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float py)
                && !float.IsNaN(px) && !float.IsInfinity(px) && !float.IsNaN(py) && !float.IsInfinity(py))
                p.DebugTeleport(new Vector2(px, py));
            GUILayout.EndHorizontal();
            if (p.Alive && GUILayout.Button("Die now")) p.DebugDie();
            GUILayout.Space(12);
            GUILayout.Label("Weapon skills (remove)");
            foreach (var skill in p.Skills)
                if (skill.def != null && GUILayout.Button("Remove: " + skill.def.name)) { skill.def = null; skill.cd = 0; }
            GUILayout.Space(12);
            GUILayout.Label("Abilities / live states");
            foreach (var a in p.Abilities.Owned.ToArray())
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(a.name);
                foreach (var state in a.states.Where(s => s.type is "Counter" or "StoredValue" or "Charge" or "Timer"))
                    GUILayout.Label($"{state.label ?? state.name}: {p.Abilities.GetState(a, state.name):0.##} / {state.max:0.##}" +
                        (state.decay > 0 ? $" (-{state.decay:0.##}/s)" : ""));
                if (GUILayout.Button("Remove ability")) p.Abilities.Remove(a);
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.Space(18);
            ResizeHandle();
            GUI.DragWindow(new Rect(0, 0, panel.width, fontSize + 10));
        }

        void ResizeHandle()
        {
            var grip = new Rect(panel.width - 26, panel.height - 26, 24, 24);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            GUI.Box(grip, "+");
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                resizing = true;
                GUIUtility.hotControl = control;
                resizeStart = GUIUtility.GUIToScreenPoint(e.mousePosition);
                sizeStart = panel.size;
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
            if (resizing && e.type == EventType.MouseDrag)
            {
                var size = sizeStart + GUIUtility.GUIToScreenPoint(e.mousePosition) - resizeStart;
                panel.width = Mathf.Clamp(size.x, Mathf.Min(360, Screen.width), Screen.width - panel.x);
                panel.height = Mathf.Clamp(size.y, Mathf.Min(300, Screen.height), Screen.height - panel.y);
                e.Use();
            }
            if (resizing && (e.rawType == EventType.MouseUp || e.type == EventType.Ignore))
            {
                resizing = false;
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }
    }
}
