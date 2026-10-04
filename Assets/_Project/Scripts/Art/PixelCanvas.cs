using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// A small canvas for drawing pixel art in code.
    /// Draw with primitives such as Rect/Ellipse/Line, then generate a 1px outline automatically with Outline().
    /// Coordinates use a bottom-left origin (same as a Unity texture).
    /// </summary>
    public class PixelCanvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public PixelCanvas(int w, int h)
        {
            W = w; H = h; Px = new Color32[w * h];
        }

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public Color32 Get(int x, int y) => In(x, y) ? Px[y * W + x] : Clear;
        public bool Solid(int x, int y) => In(x, y) && Px[y * W + x].a > 0;

        public void Set(int x, int y, Color32 c)
        {
            if (!In(x, y)) return;
            if (c.a == 255 || c.a == 0) { Px[y * W + x] = c; return; }
            var d = Px[y * W + x];
            float a = c.a / 255f;
            Px[y * W + x] = new Color32(
                (byte)Mathf.Lerp(d.r, c.r, a), (byte)Mathf.Lerp(d.g, c.g, a), (byte)Mathf.Lerp(d.b, c.b, a),
                (byte)Mathf.Max(d.a, c.a));
        }

        /// <summary>Paint only pixels that are already filled (useful for shading).</summary>
        public void SetIfSolid(int x, int y, Color32 c) { if (Solid(x, y)) Set(x, y, c); }

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) Set(i, j, c);
        }

        public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            int x0 = Mathf.FloorToInt(cx - rx - 1), x1 = Mathf.CeilToInt(cx + rx + 1);
            int y0 = Mathf.FloorToInt(cy - ry - 1), y1 = Mathf.CeilToInt(cy + ry + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Set(x, y, c);
                }
        }

        public void Circle(float cx, float cy, float r, Color32 c) => Ellipse(cx, cy, r, r, c);

        public void Ring(float cx, float cy, float r, float thickness, Color32 c)
        {
            int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1);
            int y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    if (d <= r && d >= r - thickness) Set(x, y, c);
                }
        }

        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void ThickLine(Vector2 a, Vector2 b, float r, Color32 c)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 2) + 1;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                Circle(p.x, p.y, r, c);
            }
        }

        /// <summary>Recolor every filled pixel in the y range (vertical gradient/shading).</summary>
        public void ShadeRows(int y0, int y1, Color32 c)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = 0; x < W; x++)
                    if (Solid(x, y)) Px[y * W + x] = c;
        }

        /// <summary>Copy another canvas onto this one (transparent pixels are skipped).</summary>
        public void Stamp(PixelCanvas src, int ox, int oy, bool flipX = false)
        {
            for (int y = 0; y < src.H; y++)
                for (int x = 0; x < src.W; x++)
                {
                    var c = src.Px[y * src.W + x];
                    if (c.a == 0) continue;
                    Set(ox + (flipX ? src.W - 1 - x : x), oy + y, c);
                }
        }

        /// <summary>
        /// Draw a 1px outline around transparent pixels next to filled ones. Pass diagonal=true for a thicker outline.
        /// </summary>
        public void Outline(Color32 c, bool diagonal = false)
        {
            var copy = (Color32[])Px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (copy[y * W + x].a > 0) continue;
                    bool near = S(copy, x - 1, y) || S(copy, x + 1, y) || S(copy, x, y - 1) || S(copy, x, y + 1);
                    if (!near && diagonal)
                        near = S(copy, x - 1, y - 1) || S(copy, x + 1, y - 1) || S(copy, x - 1, y + 1) || S(copy, x + 1, y + 1);
                    if (near) Px[y * W + x] = c;
                }
        }

        bool S(Color32[] a, int x, int y) => x >= 0 && y >= 0 && x < W && y < H && a[y * W + x].a > 0;

        /// <summary>Darken the edge pixels inside the shape (light from the upper left → shade on the lower right).</summary>
        public void InnerShade(Color32 from, Color32 to, int dx = 1, int dy = -1)
        {
            var copy = (Color32[])Px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = copy[y * W + x];
                    if (!Same(c, from)) continue;
                    int nx = x + dx, ny = y + dy;
                    bool edge = !(nx >= 0 && ny >= 0 && nx < W && ny < H) || copy[ny * W + nx].a == 0;
                    if (edge) Px[y * W + x] = to;
                }
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        public void Replace(Color32 from, Color32 to)
        {
            for (int i = 0; i < Px.Length; i++) if (Same(Px[i], from)) Px[i] = to;
        }

        public Texture2D ToTexture()
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            t.SetPixels32(Px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>pivot is in pixel coordinates. PPU is fixed at 16.</summary>
        public Sprite ToSprite(Vector2 pivotPx, Vector4 border = default)
        {
            var t = ToTexture();
            return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(pivotPx.x / W, pivotPx.y / H), Art.PPU, 0, SpriteMeshType.FullRect, border);
        }

        public Sprite ToSpriteCentered() => ToSprite(new Vector2(W / 2f, H / 2f));
        public Sprite ToSpriteFeet() => ToSprite(new Vector2(W / 2f, 1));
    }

    public static class Pal
    {
        public static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c);
            return c;
        }

        public static Color32 Mul(Color32 c, float m) =>
            new Color32((byte)Mathf.Clamp(c.r * m, 0, 255), (byte)Mathf.Clamp(c.g * m, 0, 255), (byte)Mathf.Clamp(c.b * m, 0, 255), c.a);

        public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
        public static Color32 A(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        // Shared palette (based on a warm, soft fantasy tone)
        public static readonly Color32 Outline = Hex("2a1d2f");
        public static readonly Color32 OutlineSoft = Hex("3d2b45");
        public static readonly Color32 White = Hex("fff8ef");
        public static readonly Color32 Shadow = new Color32(30, 20, 45, 110);
        public static readonly Color32 EyeDark = Hex("221830");
        public static readonly Color32 Blush = Hex("f59a9a");
        public static readonly Color32 EarPink = Hex("f2a7b5");

        public static readonly Color32 Steel = Hex("d9e1ea");
        public static readonly Color32 SteelShade = Hex("8d9bb0");
        public static readonly Color32 SteelDark = Hex("5a6478");
        public static readonly Color32 Wood = Hex("8a5a3c");
        public static readonly Color32 WoodDark = Hex("5c3a2a");
        public static readonly Color32 Gold = Hex("f4c95d");
        public static readonly Color32 GoldDark = Hex("b8862f");

        public static readonly Color32 Fire = Hex("ff8a3d");
        public static readonly Color32 Frost = Hex("8fe3ff");
        public static readonly Color32 Lightning = Hex("fff36b");
        public static readonly Color32 ShadowEl = Hex("b18cff");
        public static readonly Color32 Holy = Hex("fff2b3");
        public static readonly Color32 Wind = Hex("b8ffcf");
        public static readonly Color32 Blood = Hex("ff5a6e");
    }
}
