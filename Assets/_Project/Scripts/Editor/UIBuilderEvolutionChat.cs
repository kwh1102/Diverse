using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    public static partial class UIBuilder
    {
        internal static void RepairEvolutionChat(EvolveView view)
        {
            var data = new SerializedObject(view);
            var chat = (data.FindProperty("chatPanel").objectReferenceValue as GameObject)?.transform as RectTransform;
            var scroll = data.FindProperty("chatScroll").objectReferenceValue as ScrollRect;
            var body = data.FindProperty("chatLog").objectReferenceValue as Text;
            var input = data.FindProperty("chatInput").objectReferenceValue as InputField;
            var send = data.FindProperty("send").objectReferenceValue as Button;
            if (chat == null || scroll == null || body == null || input == null || send == null) return;
            // Preserve authored input/button positions. Stretch history to the actual top of that footer.
            float footerTop = chat.rect.yMin;
            var corners = new Vector3[4];
            foreach (var footer in new[] { (RectTransform)input.transform, (RectTransform)send.transform })
            {
                footer.GetWorldCorners(corners);
                foreach (var corner in corners) footerTop = Mathf.Max(footerTop, chat.InverseTransformPoint(corner).y);
            }
            var area = (RectTransform)scroll.transform;
            area.SetParent(chat, false); area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(4, footerTop - chat.rect.yMin + 3); area.offsetMax = new Vector2(-4, -3);
            var viewport = scroll.viewport;
            if (viewport == null || viewport == area) viewport = area.Find("Viewport") as RectTransform;
            if (viewport == null) viewport = Stretch(area, "Viewport");
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(2, 0); viewport.offsetMax = new Vector2(-2, 0);
            InventoryComponent<RectMask2D>(viewport.gameObject).enabled = true;
            var hit = InventoryComponent<Image>(area.gameObject); hit.color = Color.clear; hit.raycastTarget = true;
            var content = body.rectTransform; content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1);
            content.anchoredPosition = content.sizeDelta = Vector2.zero;
            var fitter = InventoryComponent<ContentSizeFitter>(body.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            body.alignment = TextAnchor.UpperLeft; body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow; body.resizeTextForBestFit = false;
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }
    }
}
