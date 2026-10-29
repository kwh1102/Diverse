using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Diverse.EditorTools.ProjectAssetBuilder;

namespace Diverse.EditorTools
{
    /// <summary>
    /// 설정 → 그래픽 page (SettingsView), the pause menu's 그래픽 tab and the HUD FPS label: built into fresh canvases by
    /// UIBuilder, and added to the EXISTING MenuCanvas/GameCanvas prefabs in place by InstallGraphicsSettings
    /// (never rebuilds them, so editor tweaks survive).
    /// </summary>
    public static partial class UIBuilder
    {
        [MenuItem("Diverse/그래픽 설정 UI 추가 (기존 프리팹에)")]
        public static void InstallGraphicsSettings()
        {
            font = Resources.Load<Font>("Fonts/Galmuri11");
            fontBold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
            BuildFrames();
            Patch(UIPrefabDir + "/MenuCanvas.prefab", root =>
            {
                bool changed = false;
                foreach (var v in root.GetComponentsInChildren<SettingsView>(true)) changed |= AddGraphicsPage(v);
                return changed;
            });
            Patch(UIPrefabDir + "/GameCanvas.prefab", root =>
            {
                bool changed = false;
                foreach (var v in root.GetComponentsInChildren<SettingsView>(true)) changed |= AddGraphicsPage(v);
                var pause = root.GetComponentInChildren<PauseView>(true);
                if (pause != null) changed |= AddGraphicsTab(pause);
                var hud = root.GetComponentInChildren<HudView>(true);
                if (hud != null) changed |= AddFpsLabel(hud);
                return changed;
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] graphics settings installed");
        }

        /// <summary>The graphics page next to SettingsPage/KeysPage (+ a 그래픽 tab on the title-screen version).</summary>
        static bool AddGraphicsPage(SettingsView v)
        {
            var sp = v.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SettingsPage");
            if (sp == null) return false;
            var body = sp.parent;
            if (body.Find("GraphicsPage") != null) return false;

            var gp = Stretch(body, "GraphicsPage");
            Box(gp, slotSpr);
            const float x = 8, rowX = 104, rowW = 330;
            float y = 8;
            Button[] Row(string name, string label, string[] captions)
            {
                Label(gp, name + "Label", label, TL, new Vector2(x, y + 3), new Vector2(rowX - x, 14), 9);
                var row = Node(gp, name, TL, new Vector2(rowX, y), new Vector2(rowW, 18));
                var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.spacing = 4; hl.childControlWidth = true; hl.childForceExpandWidth = true; hl.childControlHeight = true;
                var buttons = captions.Select(c => Btn(row, c, c, TL, Vector2.zero, new Vector2(70, 18), 9)).ToArray();
                return buttons;
            }
            var presets = Row("Presets", "<b>품질 프리셋</b>", new[] { "낮음", "보통", "높음" }); y += 28;
            var fps = Row("FpsCap", "프레임 제한", new[] { "30", "60", "120", "모니터에 맞춤" }); y += 22;
            var effects = Row("Effects", "효과 양", new[] { "적게", "보통", "많이" }); y += 22;
            var post = Row("PostFx", "후처리", new[] { "끔", "가볍게", "전체" }); y += 28;
            var showFps = Btn(gp, "ShowFps", "FPS 표시: 꺼짐", TL, new Vector2(x, y), new Vector2(150, 18), 9); y += 28;
            var hint = Label(gp, "Hint", "", TL, new Vector2(x, y), new Vector2(430, 52), 9, color: UIKit.Muted);
            UIKit.Show(gp, false);

            Wire(v, "graphicsPage", gp.gameObject);
            WireArray(v, "presetButtons", presets);
            WireArray(v, "fpsButtons", fps);
            WireArray(v, "effectButtons", effects);
            WireArray(v, "postButtons", post);
            Wire(v, "showFps", showFps);
            Wire(v, "graphicsHint", hint);

            // Title-screen version (it has its own 설정/키 설정 tabs): a 그래픽 tab in that row. The pause menu's copy has
            // none; the pause menu's own tab row (also "Tabs", next to it) gets its tab in AddGraphicsTab.
            var tabs = body.parent != null ? body.parent.Find("Tabs") : null;
            bool ownTabs = new SerializedObject(v).FindProperty("settingsTab").objectReferenceValue != null;
            if (ownTabs && tabs != null && tabs.Find("GraphicsTab") == null)
            {
                var t = Btn(tabs, "GraphicsTab", "그래픽", TL, Vector2.zero, new Vector2(100, 20));
                Wire(v, "graphicsTab", t);
            }
            return true;
        }

        /// <summary>Sixth pause tab, after 키 설정 (PauseView.TabIds order).</summary>
        static bool AddGraphicsTab(PauseView pause)
        {
            var tabs = pause.transform.Find("Panel/Tabs");
            if (tabs == null) return false;
            bool changed = false;
            // A first install put a stray settings-page tab here (it opened the page without the pause tab tint)
            var stray = tabs.Find("GraphicsTab");
            if (stray != null)
            {
                Object.DestroyImmediate(stray.gameObject);
                foreach (var v in pause.GetComponentsInChildren<SettingsView>(true)) Wire(v, "graphicsTab", null);
                changed = true;
            }
            if (tabs.Find("Tab 그래픽") == null) { Btn(tabs, "Tab 그래픽", "그래픽", TL, Vector2.zero, new Vector2(80, 20)); changed = true; }
            if (!changed) return false;
            WireArray(pause, "tabs", tabs.Cast<Transform>().Select(t => t.GetComponent<Button>()).Where(b => b != null).Cast<Object>().ToArray());
            return true;
        }

        /// <summary>Small FPS readout at the top centre of the HUD (hidden unless 설정 → 그래픽 → FPS 표시).</summary>
        static bool AddFpsLabel(HudView hud)
        {
            if (hud.transform.Find("Fps") != null) return false;
            var t = Label(hud.transform, "Fps", "60 FPS", TC, new Vector2(0, 4), new Vector2(200, 12), 8, TextAnchor.MiddleCenter);
            t.rectTransform.pivot = TC;
            UIKit.Show(t, false);
            Wire(hud, "fpsText", t);
            return true;
        }
    }
}
