using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Short notices stacked under the top of the screen; each fades out after a few seconds.</summary>
    public class ToastView : MonoBehaviour
    {
        [SerializeField] RectTransform template;     // framed box with a CanvasGroup and a child "Text"
        [SerializeField] int maxToasts = 4;
        [SerializeField] float life = 4.5f;
        [SerializeField] float spacing = 21f;        // box top to next box top
        [SerializeField] float padding = 16f;        // left + right space around the text

        readonly List<(string text, float t)> toasts = new List<(string, float)>();
        readonly List<RectTransform> pool = new List<RectTransform>();

        public void Add(string text)
        {
            toasts.Add((text, Time.unscaledTime));
            if (toasts.Count > maxToasts) toasts.RemoveAt(0);
        }

        void Update()
        {
            toasts.RemoveAll(x => Time.unscaledTime - x.t > life);
            UIKit.Pool(template, pool, toasts.Count);
            float maxW = ((RectTransform)transform).rect.width;
            for (int i = 0; i < toasts.Count; i++)
            {
                float age = Time.unscaledTime - toasts[i].t;
                float a = age < 0.2f ? age / 0.2f : age > life - 0.7f ? 1 - (age - (life - 0.7f)) / 0.7f : 1;
                var box = pool[i];
                var text = box.Find("Text")?.GetComponent<Text>();
                UIKit.Set(text, toasts[i].text);
                if (text != null) box.sizeDelta = new Vector2(Mathf.Min(text.preferredWidth + padding, maxW), box.sizeDelta.y);
                // Newest at the bottom; it slides up into place as it fades in
                box.anchoredPosition = new Vector2(0, -i * spacing + (1 - Mathf.Clamp01(age / 0.2f)) * 8f);
                var cg = box.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = a;
            }
        }
    }
}
