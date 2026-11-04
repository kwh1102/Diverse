using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    public enum ButtonRole { Secondary, Primary, Category, Craft, Choice, Danger }

    /// <summary>Role-specific pixel frames shared by existing prefabs and runtime-created buttons.</summary>
    [DisallowMultipleComponent]
    public sealed class GameButtonStyle : MonoBehaviour
    {
        static readonly Dictionary<string, Sprite[]> Frames = new Dictionary<string, Sprite[]>();
        Button button;
        Image glow;
        ButtonRole role;
        bool chosen;
        Color accent;
        ButtonBorderLight borderLight;

        public static void UseOriginal(Button target, Button source)
        {
            var style = target.GetComponent<GameButtonStyle>();
            if (style != null) style.enabled = false;
            var glow = target.transform.Find("Category Glow");
            if (glow != null) glow.gameObject.SetActive(false);
            target.image.sprite = source.image.sprite;
            target.image.overrideSprite = null;
            target.image.type = source.image.type;
            target.image.color = source.image.color;
            target.image.pixelsPerUnitMultiplier = source.image.pixelsPerUnitMultiplier;
            target.transition = source.transition;
            target.spriteState = source.spriteState;
            target.colors = source.colors;
        }

        public static void Suggested(Button button)
        {
            if (button == null) return;
            var existing = button.GetComponent<GameButtonStyle>();
            if (existing != null && existing.button == button) return;
            string id = button.name.ToLowerInvariant();
            var role = id.Contains("delete") || id.Contains("forget") || id.Contains("abandon") ? ButtonRole.Danger
                : id.Contains("start") || id.Contains("continue") || id.Contains("resume") || id.Contains("apply") || id.Contains("rebirth") ? ButtonRole.Primary
                : id.Contains("generate") || id.Contains("upgrade") || id.Contains("buy") || id.Contains("unlock") || id.Contains("inherit") ? ButtonRole.Craft
                : id.Contains("tab") || id.Contains("settings") || id.Contains("graphics") || id.Contains("design") ? ButtonRole.Category
                : ButtonRole.Secondary;
            Apply(button, role);
        }

        public static void Apply(Button button, ButtonRole role, bool chosen = false, Color? accent = null)
        {
            if (button == null || button.image == null) return;
            var style = button.GetComponent<GameButtonStyle>() ?? button.gameObject.AddComponent<GameButtonStyle>();
            style.Configure(button, role, chosen, accent ?? DefaultAccent(role));
        }

        static Color DefaultAccent(ButtonRole role) => Pal.Hex(role switch
        {
            ButtonRole.Primary => "8ad8ba", ButtonRole.Category => "b6a1ed", ButtonRole.Craft => "e8bc73",
            ButtonRole.Danger => "db8795", ButtonRole.Choice => "c0b2df", _ => "8c96aa"
        });

        void Configure(Button target, ButtonRole nextRole, bool selected, Color nextAccent)
        {
            button = target;
            role = nextRole;
            chosen = selected;
            accent = nextAccent;
            enabled = true;
            var key = $"{role}_{ColorUtility.ToHtmlStringRGB(accent)}_{chosen}";
            if (!Frames.TryGetValue(key, out var frames))
            {
                frames = new Sprite[5];
                for (int i = 0; i < frames.Length; i++) frames[i] = Draw(role, accent, chosen, i);
                Frames[key] = frames;
            }
            var image = button.image;
            image.sprite = frames[0];
            image.overrideSprite = null;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1;
            image.color = Color.white;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = frames[1], pressedSprite = frames[2], selectedSprite = frames[1], disabledSprite = frames[4]
            };
            // The glow sits behind the frame, does not affect layout, and never intercepts input.
            var existing = transform.Find("Category Glow");
            glow = existing != null ? existing.GetComponent<Image>() : null;
            if (role == ButtonRole.Category && glow == null)
            {
                var go = new GameObject("Category Glow", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.transform.SetAsFirstSibling();
                glow = go.GetComponent<Image>();
                var rect = glow.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(-2, -2); rect.offsetMax = new Vector2(2, 2);
                glow.raycastTarget = false;
                var outline = go.AddComponent<Outline>();
                outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.25f);
                outline.effectDistance = Vector2.one;
            }
            if (glow != null)
            {
                glow.gameObject.SetActive(role == ButtonRole.Category);
                glow.sprite = frames[3]; glow.type = Image.Type.Sliced;
                glow.pixelsPerUnitMultiplier = 1;
                if (role == ButtonRole.Category)
                {
                    // Replace the old separate moving squares with a light gradient drawn into the complete rim.
                    for (int i = glow.transform.childCount - 1; i >= 0; i--)
                        if (glow.transform.GetChild(i).name == "Orbiting border light") Destroy(glow.transform.GetChild(i).gameObject);
                    borderLight = glow.GetComponentInChildren<ButtonBorderLight>(true);
                    if (borderLight == null)
                    {
                        var rim = new GameObject("Flowing frame light", typeof(RectTransform), typeof(ButtonBorderLight));
                        rim.transform.SetParent(glow.transform, false);
                        var rect = (RectTransform)rim.transform;
                        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                        rect.offsetMin = rect.offsetMax = Vector2.zero;
                        borderLight = rim.GetComponent<ButtonBorderLight>();
                    }
                    borderLight.raycastTarget = false;
                    borderLight.color = accent;
                }
            }
        }

        void Update()
        {
            if (glow == null || !glow.gameObject.activeSelf) return;
            float alpha = button.interactable ? 0.27f + Mathf.Sin(Time.unscaledTime * 1.7f) * 0.05f : 0.1f;
            glow.color = new Color(accent.r, accent.g, accent.b, alpha);
            if (borderLight != null) borderLight.Strength = button.interactable ? 1 : 0.15f;
        }

        static Sprite Draw(ButtonRole role, Color accent, bool chosen, int state)
        {
            const int width = 24, height = 16;
            var cv = new PixelCanvas(width, height);
            bool disabled = state == 4, hover = state == 1, pressed = state == 2;
            Color edge = disabled ? Pal.Hex("555462") : accent;
            Color fill = Pal.Hex(role switch
            {
                ButtonRole.Primary => "244c48", ButtonRole.Category => "40315b", ButtonRole.Craft => "62432d",
                ButtonRole.Danger => "532c3e", ButtonRole.Choice => chosen ? "514467" : "302e42", _ => "292e3b"
            });
            if (disabled) fill = Pal.Hex("292a33");
            else if (hover || chosen) fill = Color.Lerp(fill, accent, 0.16f);
            else if (pressed) fill = Color.Lerp(fill, Color.black, 0.23f);
            var shadow = Pal.Hex("141722");
            int cut = role == ButtonRole.Category ? 3 : role == ButtonRole.Primary ? 2 : 1;
            cv.Rect(cut, 0, width - cut * 2, height, shadow);
            cv.Rect(0, cut, width, height - cut * 2, shadow);
            cv.Rect(1, cut, width - 2, height - cut * 2, edge);
            cv.Rect(cut, 1, width - cut * 2, height - 2, edge);
            cv.Rect(2, cut + 1, width - 4, height - cut * 2 - 2, fill);
            cv.Rect(cut + 1, 2, width - cut * 2 - 2, height - 4, fill);
            if (role == ButtonRole.Secondary)
            {
                cv.Rect(1, 1, width - 2, 2, shadow);
                cv.Rect(3, height - 3, width - 6, 1, Color.Lerp(fill, edge, 0.45f));
            }
            else if (role == ButtonRole.Primary)
            {
                cv.Rect(4, 3, width - 8, 1, Color.Lerp(fill, edge, 0.5f));
                cv.Rect(4, height - 4, width - 8, 1, Color.Lerp(edge, Color.white, 0.2f));
                cv.Set(2, height / 2, edge); cv.Set(width - 3, height / 2, edge);
            }
            else if (role == ButtonRole.Category)
            {
                cv.Rect(5, height - 4, width - 10, 1, Color.Lerp(fill, edge, 0.4f));
                cv.Rect(2, height / 2 - 1, 2, 2, edge); cv.Rect(width - 4, height / 2 - 1, 2, 2, edge);
                cv.Set(4, height - 3, Color.white); cv.Set(width - 5, 2, edge);
            }
            else if (role == ButtonRole.Craft)
            {
                cv.Rect(2, 1, width - 4, 2, Color.Lerp(edge, shadow, 0.55f));
                cv.Set(3, height - 4, edge); cv.Set(width - 4, height - 4, edge);
                cv.Rect(5, height - 3, width - 10, 1, Color.Lerp(edge, Color.white, 0.15f));
            }
            else if (role == ButtonRole.Choice && chosen)
            {
                cv.Rect(3, 4, 1, height - 8, edge);
                cv.Rect(width - 4, 4, 1, height - 8, edge);
                cv.Rect(5, height - 3, width - 10, 1, Color.Lerp(edge, Color.white, 0.25f));
            }
            if (state == 3)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (x > 2 && x < width - 3 && y > 2 && y < height - 3) cv.Set(x, y, Color.clear);
            }
            var texture = cv.ToTexture();
            texture.name = $"Button {role} {state}";
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(6, 6, 6, 6));
        }
    }
}
