using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>A small panel next to the cursor that stays on screen. Call Show every frame while hovering.</summary>
    public class TooltipView : MonoBehaviour
    {
        [SerializeField] Text head, body;
        [SerializeField] Vector2 offset = new Vector2(-12, -16);
        int shownFrame = -1;

        public void Show(string h, string b)
        {
            shownFrame = Time.frameCount;
            UIKit.Show(this, true);
            UIKit.Set(head, h);
            UIKit.Set(body, b);
            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Controls.MouseScreen, null, out var local)) return;
            // Anchor the panel's top-right at the cursor; flip right if it would leave the screen
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            var size = rt.rect.size;
            var r = parent.rect;
            Vector2 pos = local + offset;
            float x = pos.x - size.x;
            if (x < r.xMin + 4) x = local.x + 16;
            float y = Mathf.Max(pos.y, r.yMin + size.y + 4);
            rt.pivot = new Vector2(0, 1);
            rt.localPosition = new Vector2(x, y);
        }

        void LateUpdate()
        {
            // Hidden unless something called Show this frame
            if (shownFrame < Time.frameCount - 1) UIKit.Show(this, false);
        }
    }
}
