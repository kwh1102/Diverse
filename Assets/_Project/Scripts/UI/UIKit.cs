using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Shared helpers for the uGUI views. Views own their hierarchy in prefabs (Prefabs/UI);
    /// these only fill values, toggle visibility and clone list templates.
    /// </summary>
    public static class UIKit
    {
        public static readonly Color Ink = Pal.Hex("f2e8d8");
        public static readonly Color Muted = Pal.Hex("8c7aa8");
        public static readonly Color Highlight = Pal.Hex("fff2b3");

        public static void Show(Component c, bool on) { if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }
        public static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }

        public static void Set(Text t, string s) { if (t != null && t.text != s) t.text = s; }

        public static void Fill(Image bar, float k) { if (bar != null) bar.fillAmount = Mathf.Clamp01(k); }

        /// <summary>Clear a button's listeners and bind one action (+ the UI click sound).</summary>
        public static void Bind(Button b, System.Action act)
        {
            if (b == null) return;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => { Sfx.Play("ui_select", 0.5f); act(); });
        }

        /// <summary>Set a button's caption: its child named "Text" (falls back to the first Text below it).</summary>
        public static void Label(Button b, string s)
        {
            if (b == null) return;
            var t = b.transform.Find("Text")?.GetComponent<Text>() ?? b.GetComponentInChildren<Text>(true);
            Set(t, s);
        }

        /// <summary>
        /// Keep exactly <paramref name="count"/> clones of a (disabled) template child alive under its parent and return them.
        /// The template stays in the prefab so the layout can be edited in the editor.
        /// </summary>
        public static List<T> Pool<T>(T template, List<T> pool, int count) where T : Component
        {
            if (template.gameObject.activeSelf) template.gameObject.SetActive(false);
            while (pool.Count < count)
            {
                var c = Object.Instantiate(template, template.transform.parent, false);
                c.name = template.name + " " + pool.Count;
                pool.Add(c);
            }
            for (int i = 0; i < pool.Count; i++) Show(pool[i], i < count);
            return pool;
        }

        /// <summary>Pixel sprite in an Image, scaled by whole multiples (like the old IMGUI drawing).</summary>
        public static void Sprite(Image img, Sprite s, bool flip = false)
        {
            if (img == null) return;
            img.enabled = s != null;
            if (s == null) return;
            if (img.sprite != s) img.sprite = s;
            img.preserveAspect = true;
            var sc = img.rectTransform.localScale;
            sc.x = Mathf.Abs(sc.x) * (flip ? -1 : 1);
            img.rectTransform.localScale = sc;
        }

        public static bool Hovered(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            var canvas = rt.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, Controls.MouseScreen, cam);
        }
    }
}
