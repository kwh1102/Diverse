using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        internal static void RepairInventoryDetails(InventoryView view)
        {
            var data = new SerializedObject(view);
            var body = data.FindProperty("details").objectReferenceValue as Text;
            var preview = data.FindProperty("preview").objectReferenceValue as Image;
            var glyph = data.FindProperty("previewGlyph").objectReferenceValue as Text;
            if (body == null || preview == null) return;
            var box = view.transform.Find("Panel/ItemBox") as RectTransform;
            var scroll = body.GetComponentInParent<ScrollRect>();
            var actions = box?.Find("Actions") as RectTransform;
            if (box == null || scroll == null || actions == null) return;
            var layout = InventoryComponent<VerticalLayoutGroup>(box.gameObject);
            layout.padding = new RectOffset(8,8,8,8); layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var header = box.Find("ItemHeader") as RectTransform ?? Node(box,"ItemHeader",TL,Vector2.zero,new Vector2(160,48));
            header.SetAsFirstSibling();
            var headerSize = InventoryComponent<LayoutElement>(header.gameObject); headerSize.preferredHeight = 48; headerSize.flexibleHeight = 0;
            InventoryRect(preview.rectTransform,header,0,4,36,36);
            if (glyph != null) InventoryRect(glyph.rectTransform,header,0,4,36,36);
            Text Title(string id, float y, float height, int size)
            {
                var text = header.Find(id)?.GetComponent<Text>();
                if (text == null) { text = Object.Instantiate(body,header); text.name = id; }
                var fitter = text.GetComponent<ContentSizeFitter>(); if (fitter != null) Object.DestroyImmediate(fitter);
                InventoryRect(text.rectTransform,header,42,y,118,height);
                text.fontSize = size; text.resizeTextForBestFit = false; text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
                return text;
            }
            Wire(view,"itemHeading",Title("Name",0,30,10));
            Wire(view,"itemMeta",Title("TypeTier",32,14,8));
            ((RectTransform)scroll.transform).SetParent(box,false); scroll.transform.SetSiblingIndex(1);
            var bodySize = InventoryComponent<LayoutElement>(scroll.gameObject); bodySize.minHeight = 60; bodySize.flexibleHeight = 1;
            if (scroll.verticalScrollbar == null)
            {
                var rail = Node(scroll.transform,"Scrollbar",TL,Vector2.zero,new Vector2(5,100));
                var bg = rail.gameObject.AddComponent<Image>(); bg.color = new Color(.18f,.15f,.23f,1);
                var bar = rail.gameObject.AddComponent<Scrollbar>();
                var handle = Node(rail,"Handle",TL,Vector2.zero,new Vector2(5,20));
                var image = handle.gameObject.AddComponent<Image>(); image.color = UIKit.Highlight;
                bar.handleRect = handle; bar.targetGraphic = image; scroll.verticalScrollbar = bar;
            }
            BoundPartScroll(scroll,body);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            Wire(view,"detailsScroll",scroll);
            actions.SetAsLastSibling();
            var footer = InventoryComponent<VerticalLayoutGroup>(actions.gameObject);
            footer.spacing = 3; footer.padding = new RectOffset();
            footer.childControlWidth = footer.childControlHeight = true; footer.childForceExpandWidth = true; footer.childForceExpandHeight = false;
            foreach (var button in actions.GetComponentsInChildren<Button>(true))
            { var size = InventoryComponent<LayoutElement>(button.gameObject); size.preferredHeight = 20; size.flexibleHeight = 0; }
            var oldFitter = actions.GetComponent<ContentSizeFitter>(); if (oldFitter != null) oldFitter.enabled = false;
            body.fontSize = 9;
        }
    }
}
