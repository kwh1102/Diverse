using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>Compact movement demonstration using the same afterimage timing as gameplay.</summary>
    public sealed class TravelAbilityPreview : MonoBehaviour
    {
        Image hero, ground;
        Text spaceKey;
        readonly Image[] ghosts = new Image[12];
        readonly float[] born = new float[12];
        static readonly Sprite[] Grounds = new Sprite[6];
        int cursor;
        float previousTime = -1, lastGhost;
        Vector2 previousPosition;
        TravelType previousType;

        public static float Duration(TravelType type) => type switch
        {
            TravelType.Dash => 0.16f, TravelType.Flight => TravelTypes.FlightDuration * 2, TravelType.Blink => 0.20f,
            TravelType.Bounce => 0.32f, TravelType.Burrow => 0.9f, _ => 1.1f
        };

        public void Init(Image character, Text keyLabel)
        {
            hero = character; spaceKey = keyLabel;
            ground = Make("Continuous ground and crossing");
            ground.rectTransform.pivot = new Vector2(0.5f, 0);
            ground.rectTransform.anchoredPosition = new Vector2(0, -40);
            ground.rectTransform.sizeDelta = new Vector2(228, 32);
            for (int i = 0; i < ghosts.Length; i++) { ghosts[i] = Make("Movement afterimage"); born[i] = -10; }
            hero.transform.SetAsLastSibling();
        }

        Image Make(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false; image.preserveAspect = true;
            return image;
        }

        public void Draw(TravelType type, Sprite sprite, float elapsed)
        {
            float phase = Mathf.Repeat(elapsed, 2.8f);
            bool reset = elapsed < previousTime || type != previousType || Mathf.Repeat(previousTime, 2.8f) > phase;
            if (reset) { for (int i = 0; i < born.Length; i++) born[i] = -10; lastGhost = elapsed - TravelTypes.AfterimageInterval; }
            float duration = Duration(type);
            float t = Mathf.Clamp01((phase - 0.45f) / duration);
            float progress = type == TravelType.Blink ? (t < 0.35f ? 0 : 1) : t;
            float lift = type == TravelType.Flight ? Mathf.Sin(t * Mathf.PI) * 15
                : type == TravelType.Bounce ? Mathf.Sin(t * Mathf.PI) * 24
                : type == TravelType.Burrow ? -Mathf.Sin(t * Mathf.PI) * 5 : 0;
            var position = new Vector2(Mathf.Lerp(-74, 74, progress), -28 + lift);
            UIKit.Sprite(hero, sprite);
            if (sprite != null)
            {
                hero.rectTransform.sizeDelta = sprite.rect.size;
                hero.rectTransform.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            }
            hero.rectTransform.anchoredPosition = position;
            bool moving = phase >= 0.45f && phase < 0.45f + duration;
            if (spaceKey != null)
            {
                spaceKey.text = !moving ? "SPACE · 시연" : type != TravelType.Flight ? "SPACE · 이동"
                    : phase < 0.45f + TravelTypes.FlightDuration ? "SPACE · 비행 1/2" : "SPACE · 비행 2/2";
                spaceKey.color = moving ? UIKit.Highlight : UIKit.Muted;
            }
            float distance = Vector2.Distance(previousPosition, position);
            if (!reset && moving && sprite != null && distance > 0.1f && elapsed - lastGhost >= TravelTypes.AfterimageInterval)
            {
                // Same cadence, lifetime, and opacity as the old dash. Never multiply snapshots by distance.
                int count = type == TravelType.Blink && distance > 30 ? 5 : 1;
                for (int i = 0; i < count; i++)
                {
                    int slot = cursor++ % ghosts.Length;
                    float fraction = count > 1 ? i / (float)(count - 1) : 1;
                    UIKit.Sprite(ghosts[slot], Art.Silhouette(sprite));
                    ghosts[slot].rectTransform.sizeDelta = hero.rectTransform.sizeDelta;
                    ghosts[slot].rectTransform.pivot = hero.rectTransform.pivot;
                    ghosts[slot].rectTransform.anchoredPosition = Vector2.Lerp(previousPosition, position, fraction);
                    born[slot] = elapsed - (count > 1 ? (1 - fraction) * TravelTypes.AfterimageLifetime * 0.6f : 0);
                }
                lastGhost = elapsed;
            }
            for (int i = 0; i < ghosts.Length; i++)
            {
                Color color = TravelTypes.AfterimageColor(type);
                color.a *= Mathf.Clamp01(1 - (elapsed - born[i]) / TravelTypes.AfterimageLifetime);
                ghosts[i].color = color;
            }
            UIKit.Sprite(ground, Grounds[(int)type] ??= DrawGround(type));
            previousTime = elapsed; previousPosition = position; previousType = type;
        }

        static Sprite DrawGround(TravelType type)
        {
            var cv = new PixelCanvas(228, 32);
            var soil = Pal.Hex("51464a");
            var stone = Pal.Hex("73646a");
            var grass = Pal.Hex("92917a");
            cv.Rect(0, 0, 228, 10, soil);
            cv.Rect(0, 9, 228, 3, stone);
            cv.Rect(0, 12, 228, 2, grass);
            // Long, connected layers make a ground cross-section rather than a dotted guide line.
            for (int x = 8; x < 228; x += 21)
            {
                cv.Rect(x, 5, 8, 1, Pal.Hex("645357"));
                cv.Rect(x + 4, 11, 5, 1, Pal.Hex("aaa18a"));
            }
            if (type is TravelType.Flight or TravelType.Swim)
            {
                cv.Rect(82, 0, 64, 16, Color.clear);
                cv.Rect(82, 0, 64, 9, Pal.Hex("284958"));
                cv.Rect(83, 7, 62, 2, Pal.Hex("487f94"));
                cv.Rect(90, 5, 17, 1, Pal.Hex("75a9b7"));
                cv.Rect(118, 6, 16, 1, Pal.Hex("75a9b7"));
                cv.Rect(80, 0, 3, 12, stone); cv.Rect(145, 0, 3, 12, stone);
                cv.Rect(80, 11, 3, 2, grass); cv.Rect(145, 11, 3, 2, grass);
            }
            else if (type == TravelType.Burrow)
            {
                cv.Rect(83, 12, 62, 11, Pal.Hex("6f615a"));
                cv.Rect(87, 23, 54, 3, Pal.Hex("928274"));
                cv.Rect(83, 12, 2, 11, Pal.Hex("ad9984"));
                cv.Line(108, 23, 103, 18, soil); cv.Line(103, 18, 109, 13, soil);
                cv.Line(129, 23, 125, 17, soil);
            }
            return cv.ToSprite(new Vector2(114, 0));
        }
    }
}
