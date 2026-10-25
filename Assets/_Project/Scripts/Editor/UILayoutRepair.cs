using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>Patch existing canvases in place; never rebuild user-authored prefabs.</summary>
    public static class UILayoutRepair
    {
        public static void RunSmokeBatch() { Run(); SmokeTest.RunBatch(); }
        [MenuItem("Diverse/UI 겹침 및 버튼 상태 수정")]
        public static void Run()
        {
            foreach (var name in new[] { "GameCanvas", "MenuCanvas" })
            {
                string path = $"Assets/_Project/Prefabs/UI/{name}.prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var scaler in root.GetComponentsInChildren<CanvasScaler>(true))
                        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                    if (name == "GameCanvas") { Dialogue(root.transform.Find("Dialogue")); Cards(root); }
                    foreach (var text in root.GetComponentsInChildren<Text>(true))
                    {
                        // Map glyphs are positioned explicitly; scroll content needs its full preferred height.
                        if (text.GetComponentInParent<MapCanvas>() != null) continue;
                        text.horizontalOverflow = HorizontalWrapMode.Wrap;
                        bool content = text.GetComponent<ContentSizeFitter>() != null;
                        text.verticalOverflow = content ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
                        if (!content && text.GetComponentInParent<InputField>() == null && text.transform.parent.GetComponent<VerticalLayoutGroup>() == null)
                        {
                            text.resizeTextForBestFit = true;
                            text.resizeTextMaxSize = text.fontSize;
                            text.resizeTextMinSize = Mathf.Min(7, text.fontSize);
                        }
                    }
                    foreach (var button in root.GetComponentsInChildren<Button>(true))
                    {
                        button.transition = Selectable.Transition.ColorTint;
                        var colors = button.colors;
                        colors.normalColor = Color.white;
                        colors.highlightedColor = new Color(1f, 0.94f, 0.76f);
                        colors.pressedColor = new Color(0.75f, 0.65f, 0.9f);
                        colors.selectedColor = Color.white;
                        colors.disabledColor = new Color(0.38f, 0.35f, 0.42f, 0.65f);
                        button.colors = colors;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Diverse] UI layout repair complete");
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null) return existing;
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Area(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0, 1);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static ScrollRect Scroll(RectTransform area, RectTransform content)
        {
            var viewport = Rect(area, "Viewport"); Stretch(viewport);
            viewport.offsetMax = new Vector2(-6, 0);
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear; hit.raycastTarget = true;
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1); content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var fit = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = area.GetComponent<ScrollRect>() ?? area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18;
            var rail = Rect(area, "Scrollbar");
            rail.anchorMin = new Vector2(1, 0); rail.anchorMax = Vector2.one;
            rail.pivot = Vector2.one; rail.offsetMin = new Vector2(-4, 0); rail.offsetMax = Vector2.zero;
            var track = rail.GetComponent<Image>() ?? rail.gameObject.AddComponent<Image>();
            track.color = new Color(0.12f, 0.08f, 0.17f, 0.9f);
            var thumb = Rect(rail, "Handle"); Stretch(thumb);
            var thumbImage = thumb.GetComponent<Image>() ?? thumb.gameObject.AddComponent<Image>();
            thumbImage.color = new Color(0.65f, 0.55f, 0.8f, 0.9f);
            var bar = rail.GetComponent<Scrollbar>() ?? rail.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = thumb; bar.targetGraphic = thumbImage;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        static void Dialogue(Transform host)
        {
            if (host == null) return;
            var frame = (RectTransform)host;
            frame.anchorMin = new Vector2(0.05f, 0); frame.anchorMax = new Vector2(0.95f, 0);
            frame.sizeDelta = new Vector2(0, 230); frame.anchoredPosition = new Vector2(0, 10);
            Area((RectTransform)host.Find("Speaker"), 76, 8, 12, 18);
            var body = host.GetComponentInChildren<DialogueView>(true);
            var so = new SerializedObject(body);
            var text = (Text)so.FindProperty("body").objectReferenceValue;
            var bodyArea = Rect(host, "BodyScroll"); Area(bodyArea, 76, 30, 12, 68);
            Scroll(bodyArea, text.rectTransform);
            var opts = host.Find("Options") as RectTransform;
            if (opts == null) opts = host.Find("OptionsScroll/Viewport/Options") as RectTransform;
            var grid = opts.GetComponent<GridLayoutGroup>();
            if (grid != null) Object.DestroyImmediate(grid);
            var layout = opts.GetComponent<VerticalLayoutGroup>() ?? opts.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var opt = (Button)so.FindProperty("optionTemplate").objectReferenceValue;
            var row = opt.GetComponent<VerticalLayoutGroup>() ?? opt.gameObject.AddComponent<VerticalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 6, 6);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true; row.childForceExpandHeight = false;
            var caption = opt.GetComponentInChildren<Text>(true);
            caption.resizeTextForBestFit = false;
            caption.alignment = TextAnchor.MiddleLeft;
            var min = opt.GetComponent<LayoutElement>() ?? opt.gameObject.AddComponent<LayoutElement>();
            min.minHeight = 30;
            var area = Rect(host, "OptionsScroll"); Area(area, 12, 108, 12, 110);
            Scroll(area, opts);
        }

        static void Cards(GameObject root)
        {
            var evolve = root.GetComponentInChildren<EvolveView>(true);
            if (evolve != null)
            {
                var view = new SerializedObject(evolve);
                var warning = (Text)view.FindProperty("chatWarning").objectReferenceValue;
                Area(warning.rectTransform, 4, 44, 4, 10);
                var log = (ScrollRect)view.FindProperty("chatScroll").objectReferenceValue;
                Area((RectTransform)log.transform, 4, 3, 4, 39);
            }
            foreach (var card in root.GetComponentsInChildren<EvolveCard>(true))
            {
                var so = new SerializedObject(card);
                var desc = (Text)so.FindProperty("desc").objectReferenceValue;
                var area = Rect(card.transform, "DetailsScroll"); Area(area, 6, 48, 6, 84);
                Scroll(area, desc.rectTransform);
                var reason = (Text)so.FindProperty("reason").objectReferenceValue;
                reason.gameObject.SetActive(false);
            }
        }
    }
}
