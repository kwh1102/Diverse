using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Draws a DesignPack locally with PixelCanvas: closed-set shapes + palette (or a tiny pixel grid), then the same
    /// outline/shading as the hand-made art. Nothing here costs an API call, so previews are free and instant.
    /// </summary>
    public static class DesignArt
    {
        public static readonly string[] Hats = { "none", "crown", "wizard", "cap", "band", "horns", "flower", "helmet", "halo", "ribbon", "leaf", "glasses" };
        public static readonly string[] WeaponShapes = { "builtin", "blade", "curved", "needle", "stick", "spear", "axe", "hammer", "club", "wand", "bow" };
        public static readonly string[] Tips = { "point", "round", "flat", "fork", "orb", "star", "none" };
        public static readonly string[] Holds = { "Hand", "Mouth", "Hidden" };
        public static readonly string[] Origins = { "Hand", "Mouth", "Eyes", "Above" };
        public static readonly string[] ProjectileShapes = { "builtin", "weapon", "bolt", "needle", "orb", "star", "shard", "ring", "wave", "heart", "leaf", "note", "bubble" };

        static Color32 C(string hex, Color32 fallback) => string.IsNullOrEmpty(hex) ? fallback : Pal.Hex(hex);
        static Color32 Light(Color32 c) => Pal.Lerp(c, Pal.White, 0.45f);

        // ───────────────────────── Weapon ─────────────────────────

        public static bool HasWeapon(WeaponLook w) => w != null && (w.pixels != null || w.shape != "builtin");

        /// <summary>Weapon sprite pointing right, pivot at the grip. null = keep the chassis' original art.</summary>
        public static Sprite Weapon(WeaponLook w, int maxLength = DesignGuard.MaxWeaponLength, bool centered = false)
        {
            if (!HasWeapon(w)) return null;
            var cv = w.pixels != null ? Grid(w.pixels, w.palette, maxLength) : null;
            Vector2 pivot;
            if (cv != null) pivot = new Vector2(Mathf.Min(2, cv.W / 2f), cv.H / 2f);
            else cv = DrawWeapon(w, maxLength, out pivot);
            if (centered) pivot = new Vector2(cv.W / 2f, cv.H / 2f);
            return cv.ToSprite(pivot);
        }

        static int DefaultLength(string shape) => shape switch
        {
            "needle" => 12, "wand" => 18, "bow" => 18, "axe" => 22, "hammer" => 22, "club" => 20, "spear" => 30, "curved" => 28, "stick" => 28, _ => 24,
        };

        static int DefaultThickness(string shape) => shape switch
        {
            "needle" => 1, "stick" => 2, "spear" => 1, "axe" => 1, "hammer" => 2, "wand" => 1, "bow" => 2, "club" => 3, _ => 3,
        };

        static PixelCanvas DrawWeapon(WeaponLook w, int maxLength, out Vector2 pivot)
        {
            int L = Mathf.Min(w.length > 0 ? w.length : DefaultLength(w.shape), maxLength);
            int T = w.thickness > 0 ? w.thickness : DefaultThickness(w.shape);
            var main = C(w.main, Pal.Steel);
            var shade = C(w.shade, Pal.Mul(main, 0.72f));
            var accent = C(w.accent, Pal.Gold);
            var grip = C(w.grip, Pal.WoodDark);
            var glow = C(w.glow, Pal.Hex("c9b8ff"));

            if (w.shape == "bow")
            {
                var b = new PixelCanvas(12, L + 2);
                for (int y = 1; y <= L; y++)
                {
                    float t = (y - 1f) / (L - 1f);
                    int x = 2 + Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * 6);
                    b.Rect(x, y, Mathf.Max(1, T), 1, main);
                }
                b.Line(2, 1, 2, L, Pal.Lerp(accent, Pal.White, 0.6f));
                b.Rect(7, L / 2, Mathf.Max(1, T) + 1, 3, grip);
                Finish(b, main, shade);
                pivot = new Vector2(6, (L + 2) / 2f);
                return b;
            }

            int H = Mathf.Max(T + 8, w.shape is "axe" or "hammer" ? 16 : w.shape == "curved" ? 12 : 10);
            var cv = new PixelCanvas(L + 7, H);
            int yc = H / 2, x0 = 1, xe = x0 + L - 1;
            int half = Mathf.Max(0, (T - 1) / 2);
            pivot = new Vector2(x0 + 2, yc + 0.5f);

            void Shaft(int from, int to, int th, Color32 col) => cv.Rect(from, yc - (th - 1) / 2, Mathf.Max(1, to - from + 1), th, col);

            switch (w.shape)
            {
                case "needle":
                    Shaft(x0 + 1, xe - 1, T, main);
                    cv.Set(x0, yc, main); cv.Set(xe, yc, main);
                    if (T > 1) cv.Rect(x0 + 2, yc + T / 2, L - 4, 1, Light(main));
                    if (!string.IsNullOrEmpty(w.accent)) cv.Rect(x0 + L / 4, yc - (T - 1) / 2, 1, T, accent);
                    pivot = new Vector2(x0 + L * 0.3f, yc + 0.5f);
                    break;
                case "blade":
                case "curved":
                {
                    int g = Mathf.Clamp(L / 5, 3, 6);
                    Shaft(x0, x0 + g - 1, 2, grip);
                    cv.Rect(x0 + g, yc - half - 2, 2, T + 4, accent);
                    for (int x = x0 + g + 2; x <= xe; x++)
                    {
                        float t = (x - x0 - g - 2f) / Mathf.Max(1, xe - x0 - g - 2);
                        int lift = w.shape == "curved" ? Mathf.RoundToInt(t * t * 3) : 0;
                        int th = T;
                        if (w.tip == "point" && xe - x < T) th = Mathf.Max(1, xe - x + 1);
                        cv.Rect(x, yc - (th - 1) / 2 + lift, 1, th, main);
                        if (th > 2) cv.Set(x, yc - (th - 1) / 2 + lift + th - 1, Light(main));
                    }
                    if (w.tip != "point") Tip(cv, w.tip, xe, yc + (w.shape == "curved" ? 3 : 0), T, main, accent, glow);
                    pivot = new Vector2(x0 + 1, yc + 0.5f);
                    break;
                }
                case "spear":
                {
                    int head = Mathf.Clamp(L / 4, 4, 8);
                    Shaft(x0, xe - head, Mathf.Max(1, T), grip);
                    for (int x = xe - head + 1; x <= xe; x++)
                    {
                        int h = Mathf.Max(0, Mathf.RoundToInt((xe - x) * 3f / head));
                        cv.Rect(x, yc - h, 1, h * 2 + 1, main);
                    }
                    cv.Rect(xe - head, yc - 1, 1, 3, accent);
                    pivot = new Vector2(x0 + L * 0.3f, yc + 0.5f);
                    break;
                }
                case "axe":
                    Shaft(x0, xe - 1, Mathf.Max(1, T), grip);
                    cv.Ellipse(xe - 3, yc + 2.5f, 3.5f, 5f, main);
                    cv.Rect(xe - 6, yc - 1, 4, 3, accent);
                    for (int y = yc - 2; y <= yc + 7; y++) cv.SetIfSolid(xe, y, Light(main));
                    pivot = new Vector2(x0 + L * 0.25f, yc + 0.5f);
                    break;
                case "hammer":
                {
                    Shaft(x0, xe - 4, Mathf.Max(1, T), grip);
                    int hh = Mathf.Max(T * 2 + 6, 9);
                    cv.Rect(xe - 5, yc - hh / 2, 6, hh, main);
                    cv.Rect(xe - 5, yc - 1, 6, 2, accent);
                    cv.Rect(xe, yc - hh / 2, 1, hh, Light(main));
                    pivot = new Vector2(x0 + L * 0.25f, yc + 0.5f);
                    break;
                }
                case "club":
                    for (int x = x0; x <= xe; x++)
                    {
                        float t = (x - x0) / (float)Mathf.Max(1, L - 1);
                        int th = Mathf.RoundToInt(Mathf.Lerp(2, T + 3, t));
                        cv.Rect(x, yc - (th - 1) / 2, 1, th, x - x0 < L / 4 ? grip : main);
                    }
                    Tip(cv, w.tip == "point" ? "none" : w.tip, xe, yc, T + 3, main, accent, glow);
                    break;
                case "wand":
                    Shaft(x0, xe - 3, Mathf.Max(1, T), grip);
                    cv.Rect(x0 + L / 2, yc - 1, 2, 3, accent);
                    Tip(cv, w.tip is "point" or "none" or "flat" ? "orb" : w.tip, xe - 2, yc, 3, main, accent, glow);
                    pivot = new Vector2(x0 + L * 0.35f, yc + 0.5f);
                    break;
                default: // stick
                    Shaft(x0, xe, T, main);
                    cv.Rect(x0 + L / 3, yc - (T - 1) / 2, 2, T, accent);
                    if (T > 1) cv.Rect(x0, yc - (T - 1) / 2, L, 1, shade);
                    Tip(cv, w.tip, xe, yc, T, main, accent, glow);
                    pivot = new Vector2(x0 + L * 0.35f, yc + 0.5f);
                    break;
            }
            Finish(cv, main, shade);
            return cv;
        }

        static void Tip(PixelCanvas cv, string tip, int x, int yc, int T, Color32 main, Color32 accent, Color32 glow)
        {
            switch (tip)
            {
                case "point": cv.Set(x + 1, yc, main); cv.Set(x + 2, yc, Light(main)); break;
                case "round": cv.Circle(x + 0.5f, yc + 0.5f, T / 2f + 1.2f, main); break;
                case "orb":
                    cv.Circle(x + 2.5f, yc + 0.5f, 3.2f, glow);
                    cv.Circle(x + 2.5f, yc + 0.5f, 1.6f, Light(glow));
                    cv.Set(x + 2, yc + 1, Pal.White);
                    break;
                case "star":
                    cv.Rect(x + 1, yc - 2, 1, 5, accent); cv.Rect(x - 1, yc, 5, 1, accent); cv.Set(x + 1, yc, Pal.White);
                    break;
                case "fork":
                    cv.Line(x - 3, yc + 2, x + 1, yc + 2, main); cv.Line(x - 3, yc - 2, x + 1, yc - 2, main); cv.Rect(x - 3, yc - 2, 1, 5, main);
                    break;
            }
        }

        static void Finish(PixelCanvas cv, Color32 main, Color32 shade)
        {
            cv.InnerShade(main, shade, 1, -1);
            cv.Outline(Pal.Outline);
        }

        /// <summary>Pixel grid ('.' clear, '1'..'9' palette) → canvas, cropped, optionally downscaled, outlined.</summary>
        static PixelCanvas Grid(string[] rows, string[] palette, int maxW)
        {
            int h = rows.Length, w = rows.Max(r => r.Length);
            var cols = palette.Select(Pal.Hex).ToArray();
            int step = Mathf.Max(1, Mathf.CeilToInt(w / (float)maxW));   // nearest-neighbour shrink for small copies
            int cw = Mathf.CeilToInt(w / (float)step), ch = Mathf.CeilToInt(h / (float)step);
            var cv = new PixelCanvas(cw + 2, ch + 2);
            for (int y = 0; y < h; y += step)
                for (int x = 0; x < rows[y].Length; x += step)
                {
                    int idx = rows[y][x] - '1';
                    if (idx < 0 || idx >= cols.Length) continue;
                    cv.Set(x / step + 1, ch - y / step, cols[idx]);
                }
            cv = Crop(cv, 1);
            cv.Outline(Pal.Outline);
            return cv;
        }

        static PixelCanvas Crop(PixelCanvas src, int pad)
        {
            int x0 = src.W, y0 = src.H, x1 = -1, y1 = -1;
            for (int y = 0; y < src.H; y++)
                for (int x = 0; x < src.W; x++)
                    if (src.Solid(x, y)) { x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) return src;
            var cv = new PixelCanvas(x1 - x0 + 1 + pad * 2, y1 - y0 + 1 + pad * 2);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    cv.Px[(y - y0 + pad) * cv.W + (x - x0 + pad)] = src.Px[y * src.W + x];
            return cv;
        }

        // ───────────────────────── Projectile ─────────────────────────

        /// <summary>Shot sprite pointing right, centered. null = keep the original bolt/knife.</summary>
        public static Sprite Projectile(ProjectileLook p, WeaponLook weapon)
        {
            if (p == null) return null;
            if (p.pixels != null) return Grid(p.pixels, p.palette, DesignGuard.MaxProjectile).ToSpriteCentered();
            if (p.shape == "builtin") return null;
            if (p.shape == "weapon") return Weapon(weapon, 16, centered: true);

            int S = p.size > 0 ? p.size : 8;
            var main = C(p.main, Pal.Hex("fff1b0"));
            var accent = C(p.accent, Pal.White);
            var cv = new PixelCanvas(S + 6, S + 4);
            float cx = (S + 6) / 2f, cy = (S + 4) / 2f;
            switch (p.shape)
            {
                case "bolt":
                    cv.Rect(1, (int)cy, S + 2, 1, main);
                    cv.Rect(1, (int)cy - 1, 2, 3, accent);
                    cv.Rect(S + 2, (int)cy - 1, 2, 3, Light(main)); cv.Set(S + 4, (int)cy, Light(main));
                    break;
                case "needle":
                    cv.Rect(1, (int)cy, S + 3, 1, main); cv.Set(S + 4, (int)cy, accent);
                    break;
                case "orb":
                    cv.Circle(cx, cy, S / 2f, Pal.A(main, 140));
                    cv.Circle(cx, cy, S / 2f - 1.2f, main);
                    cv.Circle(cx, cy, S / 4f, accent);
                    break;
                case "star":
                    for (int k = 0; k < 5; k++)
                    {
                        float a = Mathf.PI / 2 + k * Mathf.PI * 2 / 5;
                        cv.ThickLine(new Vector2(cx, cy), new Vector2(cx + Mathf.Cos(a) * S / 2f, cy + Mathf.Sin(a) * S / 2f), 0.6f, main);
                    }
                    cv.Circle(cx, cy, S / 4.5f, main);
                    cv.Set((int)cx, (int)cy, accent);
                    break;
                case "shard":
                    for (int x = 0; x <= S + 2; x++)
                    {
                        int h = Mathf.RoundToInt((1 - Mathf.Abs(x - (S + 2) / 2f) / ((S + 2) / 2f)) * S / 4f);
                        cv.Rect(x + 1, (int)cy - h, 1, h * 2 + 1, main);
                    }
                    cv.Rect(3, (int)cy, S - 2, 1, accent);
                    break;
                case "ring":
                    cv.Ring(cx, cy, S / 2f, 2, main);
                    break;
                case "wave":
                    for (int y = 0; y < cv.H; y++)
                        for (int x = 0; x < cv.W; x++)
                        {
                            float dx = x + 0.5f - (cx - S / 3f), dy = y + 0.5f - cy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (dx > 0 && d <= S / 2f + 0.5f && d >= S / 2f - 2f) cv.Set(x, y, d > S / 2f - 0.8f ? accent : main);
                        }
                    break;
                case "heart":
                    cv.Circle(cx - S / 5f, cy + S / 8f, S / 4f, main);
                    cv.Circle(cx + S / 5f, cy + S / 8f, S / 4f, main);
                    for (int y = 0; y < S / 2; y++) cv.Rect((int)(cx - S / 2.4f) + y, (int)(cy - y), Mathf.Max(1, (int)(S / 1.2f) - y * 2), 1, main);
                    cv.Set((int)(cx - S / 5f), (int)(cy + S / 4f), accent);
                    break;
                case "leaf":
                    cv.Ellipse(cx, cy, S / 2f + 1, S / 4f + 0.5f, main);
                    cv.Line(2, (int)cy, S + 3, (int)cy, accent);
                    break;
                case "note":
                    cv.Ellipse(cx - 1, cy - S / 4f, S / 4f + 0.5f, S / 5f + 0.5f, main);
                    cv.Rect((int)(cx - 1 + S / 4f), (int)(cy - S / 4f), 1, S / 2 + 2, main);
                    cv.Rect((int)(cx - 1 + S / 4f), (int)(cy + S / 4f), 3, 1, accent);
                    break;
                case "bubble":
                    cv.Circle(cx, cy, S / 2f, Pal.A(main, 120));
                    cv.Ring(cx, cy, S / 2f, 1, main);
                    cv.Set((int)(cx - S / 5f), (int)(cy + S / 5f), Pal.White);
                    break;
            }
            if (p.shape != "orb" && p.shape != "bubble") cv.Outline(Pal.Outline);
            return cv.ToSpriteCentered();
        }

        // ───────────────────────── Hero decorations ─────────────────────────

        /// <summary>Headwear/accessory on the rabbit (called from Art.DrawRabbit before shading). (cx, hy) = head center, ey = eye row.</summary>
        public static void Decorate(PixelCanvas cv, HeroLook h, int cx, int hy, int ey, Color32 accent)
        {
            if (h == null) return;
            var col = C(h.hatColor, accent);
            var dark = Pal.Mul(col, 0.7f);
            switch (h.hat)
            {
                case "crown":
                    cv.Rect(cx - 3, hy + 5, 7, 2, col);
                    cv.Set(cx - 3, hy + 7, col); cv.Set(cx, hy + 7, col); cv.Set(cx + 3, hy + 7, col);
                    cv.Set(cx, hy + 6, Pal.Blood);
                    break;
                case "wizard":
                    cv.Rect(cx - 6, hy + 4, 14, 1, dark);                       // brim
                    for (int k = 1; k <= 9; k++)                               // cone, tip leaning back
                    {
                        int w = Mathf.Max(1, 9 - k);
                        cv.Rect(cx - 3 + k / 2 - Mathf.Max(0, k - 6) * 2, hy + 4 + k, w, 1, col);
                    }
                    cv.Rect(cx - 3, hy + 5, 8, 1, Pal.Lerp(col, Pal.Holy, 0.5f));   // band
                    break;
                case "cap":
                    cv.Ellipse(cx + 0.5f, hy + 4.5f, 6f, 2.4f, col);
                    cv.Rect(cx + 3, hy + 3, 6, 1, dark);
                    break;
                case "band":
                    cv.Rect(cx - 6, hy + 2, 13, 2, col);
                    cv.Rect(cx - 8, hy + 1, 2, 1, col); cv.Rect(cx - 9, hy, 2, 1, dark);
                    break;
                case "horns":
                    cv.Line(cx - 4, hy + 4, cx - 6, hy + 7, col); cv.Set(cx - 6, hy + 8, Pal.Lerp(col, Pal.White, 0.5f));
                    cv.Line(cx + 5, hy + 4, cx + 7, hy + 7, col); cv.Set(cx + 7, hy + 8, Pal.Lerp(col, Pal.White, 0.5f));
                    break;
                case "flower":
                    cv.Ellipse(cx - 3f, hy + 5, 2.2f, 1.6f, col);
                    cv.Ellipse(cx - 0.5f, hy + 5, 1.2f, 1.2f, Pal.Gold);
                    break;
                case "helmet":
                    for (int y = 0; y < 5; y++) cv.Rect(cx - 6 + y / 2, hy + 1 + y, 13 - y, 1, y == 0 ? dark : col);
                    cv.Rect(cx - 6, hy + 1, 13, 1, Pal.Lerp(col, Pal.White, 0.35f));
                    break;
                case "halo":
                    cv.Ring(cx + 0.5f, hy + 13.5f, 3.6f, 1, Pal.Lerp(col, Pal.Holy, 0.5f));
                    break;
                case "ribbon":
                    cv.Rect(cx + 1, hy + 6, 2, 2, dark);
                    cv.Line(cx - 2, hy + 8, cx + 1, hy + 7, col); cv.Line(cx - 2, hy + 5, cx + 1, hy + 6, col);
                    cv.Line(cx + 3, hy + 7, cx + 6, hy + 8, col); cv.Line(cx + 3, hy + 6, cx + 6, hy + 5, col);
                    break;
                case "leaf":
                    cv.Ellipse(cx + 1, hy + 7, 2.2f, 1.1f, C(h.hatColor, Pal.Hex("6fbf5a")));
                    cv.Set(cx, hy + 6, Pal.WoodDark);
                    break;
                case "glasses":
                    cv.Ring(cx + 2, ey + 1.5f, 2, 1, col); cv.Ring(cx + 6, ey + 1.5f, 2, 1, col);
                    cv.Set(cx + 4, ey + 2, col);
                    break;
            }
        }

        // ───────────────────────── Generated pictures → animation frames ─────────────────────────

        static bool IsOutline(Color32 c) => c.r == Pal.Outline.r && c.g == Pal.Outline.g && c.b == Pal.Outline.b;

        /// <summary>Copy a picture onto a frame canvas: feet on the canvas bottom, centered, optionally lifted / squashed / tinted.</summary>
        /// <param name="shift">x offset: +1 lunges forward (attacks), -1 is knocked back (hurt).</param>
        static void Draw(PixelCanvas cv, Texture2D t, int lift, int squash, System.Func<Color32, Color32> tint, int shift = 0)
        {
            int w = t.width, h = t.height, ox = (cv.W - w) / 2 + shift;
            var src = t.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = src[y * w + x];
                    if (c.a == 0) continue;
                    if (tint != null && !IsOutline(c)) c = tint(c);
                    // squash: the upper part sinks by up to `squash` px (breathing / landing)
                    int sink = squash > 0 && y > h / 3 ? Mathf.RoundToInt(squash * (y - h / 3f) / (h - h / 3f)) : 0;
                    cv.Set(ox + x, y + lift - sink, c);
                }
        }

        /// <summary>
        /// Hero poses from the generated sheet: idle, step A, step B, attack wind-up, attack strike, hurt (older packs: 4 cells,
        /// the 4th being one action pose). Walking alternates the two steps with lifted passing frames so the legs really move;
        /// attacks wind up then strike (a punch / kick for bare hands); getting hit uses the hurt pose knocked back a pixel.
        /// All frames share one canvas so the feet never jump.
        /// </summary>
        public static Sprite HeroPose(Texture2D[] f, Pose pose, bool elder)
        {
            Texture2D Pick(int i) => i < f.Length && f[i] != null ? f[i] : f[0];
            int W = 0, H = 0;
            foreach (var t in f) if (t != null) { W = Mathf.Max(W, t.width); H = Mathf.Max(H, t.height); }
            var cv = new PixelCanvas(W + 4, H + 3);
            System.Func<Color32, Color32> hurt = c => Pal.Lerp(c, new Color32(255, 120, 130, 255), 0.35f);
            System.Func<Color32, Color32> tint = null;
            if (elder) tint = c => { byte g = (byte)((c.r + c.g + c.b) / 3); return Pal.Lerp(c, new Color32(g, g, g, 255), 0.6f); };
            bool sheet = f.Length > 1, six = f.Length >= 6;
            switch (pose)
            {
                case Pose.Run0: Draw(cv, Pick(1), sheet ? 0 : 2, 0, tint); break;
                case Pose.Run1: Draw(cv, Pick(0), sheet ? 2 : 1, 0, tint); break;
                case Pose.Run2: Draw(cv, Pick(2), sheet ? 0 : 2, 0, tint); break;
                case Pose.Run3: Draw(cv, Pick(0), sheet ? 2 : 1, 0, tint); break;
                case Pose.Idle1: Draw(cv, Pick(0), 0, 1, tint); break;
                case Pose.AttackWind: Draw(cv, six ? Pick(3) : Pick(0), 0, six ? 0 : 2, tint); break;
                case Pose.AttackHit:
                case Pose.Cast: Draw(cv, six ? Pick(4) : Pick(3), 0, 0, tint, 1); break;
                case Pose.Hurt: Draw(cv, six ? Pick(5) : Pick(0), 1, six ? 0 : 1, hurt, -1); break;
                default: Draw(cv, Pick(0), 0, elder ? 1 : 0, tint); break;
            }
            return cv.ToSprite(new Vector2(cv.W / 2f, 1));
        }

        /// <summary>
        /// Enemy frames, as the hand-made enemies use them: 0 = standing, 1 = moving, 2 = attacking (wind-up), 3 = hit
        /// (standing, flashed and knocked back). Missing poses fall back to a squashed / tinted standing picture.
        /// </summary>
        public static Sprite EnemyFrame(Texture2D idle, Texture2D move, Texture2D attack, int frame)
        {
            int W = idle.width, H = idle.height;
            foreach (var t in new[] { move, attack }) if (t != null) { W = Mathf.Max(W, t.width); H = Mathf.Max(H, t.height); }
            var cv = new PixelCanvas(W + 2, H + 2);
            switch (frame)
            {
                case 1: Draw(cv, move ?? idle, 0, move == null ? 1 : 0, null); break;
                case 2: Draw(cv, attack ?? move ?? idle, 0, 0, attack == null ? c => Pal.Lerp(c, new Color32(255, 90, 90, 255), 0.3f) : null, 1); break;
                case 3: Draw(cv, idle, 1, 1, c => Pal.Lerp(c, Pal.White, 0.4f), -1); break;
                default: Draw(cv, idle, 0, 0, null); break;
            }
            return cv.ToSprite(new Vector2(cv.W / 2f, 1));
        }

        // ───────────────────────── Generated effects ─────────────────────────

        public static readonly string[] Ambients = { "none", "snow", "petals", "leaves", "bubbles", "embers", "sparkles", "rain", "dust", "notes" };

        /// <summary>(scale, alpha, reveal) per frame. reveal sweeps the picture from top to bottom (a swing being drawn).</summary>
        static readonly (float s, float a, float r)[] SwingKeys = { (0.85f, 1, 0.35f), (0.95f, 1, 0.7f), (1, 1, 1), (1.06f, 0.7f, 1), (1.1f, 0.35f, 1) };
        static readonly (float s, float a, float r)[] ImpactKeys = { (0.45f, 1, 1), (0.8f, 1, 1), (1, 0.75f, 1), (1.15f, 0.4f, 1) };
        static readonly (float s, float a, float r)[] TrailKeys = { (1, 0.85f, 1), (0.8f, 0.55f, 1), (0.55f, 0.3f, 1) };
        static readonly (float s, float a, float r)[] MuzzleKeys = { (0.7f, 1, 1), (1, 0.8f, 1), (1.15f, 0.4f, 1) };

        public enum EffectKind { Swing, Impact, Trail, Muzzle }

        /// <summary>One generated effect picture → an animation (growing, fading; swings sweep in) whose width is widthPx.</summary>
        public static Sprite[] Effect(Texture2D t, EffectKind kind, int widthPx, float stretchX = 1)
        {
            var keys = kind switch { EffectKind.Swing => SwingKeys, EffectKind.Impact => ImpactKeys, EffectKind.Trail => TrailKeys, _ => MuzzleKeys };
            int bw = Mathf.Max(4, Mathf.RoundToInt(widthPx * stretchX)), bh = Mathf.Max(4, Mathf.RoundToInt(widthPx * t.height / (float)t.width));
            int cw = Mathf.CeilToInt(bw * 1.25f) + 2, ch = Mathf.CeilToInt(bh * 1.25f) + 2;
            var src = t.GetPixels32();
            var frames = new Sprite[keys.Length];
            for (int k = 0; k < keys.Length; k++)
            {
                var (s, a, reveal) = keys[k];
                int sw = Mathf.Max(1, Mathf.RoundToInt(bw * s)), sh = Mathf.Max(1, Mathf.RoundToInt(bh * s));
                int x0 = (cw - sw) / 2, y0 = (ch - sh) / 2;
                var cv = new PixelCanvas(cw, ch);
                for (int y = 0; y < sh; y++)
                    for (int x = 0; x < sw; x++)
                    {
                        var c = src[(y * t.height / sh) * t.width + x * t.width / sw];
                        if (c.a < 40) continue;
                        if (reveal < 1)
                        {
                            float ang = Mathf.Atan2(y - sh / 2f, x - sw / 2f) * Mathf.Rad2Deg;   // +90 top … -90 bottom
                            if ((90 - Mathf.Clamp(ang, -90, 90)) / 180f > reveal) continue;
                        }
                        c.a = (byte)(c.a * a);
                        cv.Px[(y0 + y) * cw + x0 + x] = c;
                    }
                frames[k] = cv.ToSpriteCentered();
            }
            return frames;
        }

        // ───────────────────────── Palette transforms ─────────────────────────

        public static Color32 Shift(Color32 c, PaletteShift s)
        {
            if (c.a == 0 || s == null || s.Identity) return c;
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            h = Mathf.Repeat(h + s.hueShift / 360f, 1f);
            sat = Mathf.Clamp01(sat * s.saturation);
            v = Mathf.Clamp01(v * s.brightness);
            var r = Color.HSVToRGB(h, sat, v);
            if (!string.IsNullOrEmpty(s.tint) && s.tintAmount > 0) r = Color.Lerp(r, (Color)Pal.Hex(s.tint), s.tintAmount);
            var o = (Color32)r;
            o.a = c.a;
            return o;
        }

        /// <summary>Move greenish pixels to another hue (foliage recolor that keeps trunks, flowers and outlines).</summary>
        public static Color32 Foliage(Color32 c, Color32 target)
        {
            if (c.a == 0) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (h < 0.18f || h > 0.5f || s < 0.2f) return c;
            Color.RGBToHSV(target, out float th, out float ts, out float tv);
            var o = (Color32)Color.HSVToRGB(th, Mathf.Clamp01(ts * s / 0.55f), Mathf.Clamp01(v * Mathf.Max(0.25f, tv) / 0.65f));
            o.a = c.a;
            return o;
        }

        /// <summary>Copy a sprite with every pixel mapped (outline kept). Unreadable textures (imported PNGs) come back unchanged.</summary>
        public static Sprite Recolor(Sprite src, System.Func<Color32, Color32> map)
        {
            if (src == null || !src.texture.isReadable) return src;
            var r = src.textureRect;
            int w = (int)r.width, h = (int)r.height;
            var px = src.texture.GetPixels32();
            var cv = new PixelCanvas(w, h);
            int tw = src.texture.width;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[((int)r.y + y) * tw + (int)r.x + x];
                    bool outline = c.r == Pal.Outline.r && c.g == Pal.Outline.g && c.b == Pal.Outline.b;
                    cv.Px[y * w + x] = outline ? c : map(c);
                }
            return cv.ToSprite(src.pivot, src.border);
        }

        /// <summary>Small strip of grass / path / water + a tree for the studio preview (uses the active world look).</summary>
        public static Sprite GroundSample() => Art.Get("design_ground_sample", () =>
        {
            var cv = new PixelCanvas(48, 24);
            Ground[] rows = { Ground.Grass, Ground.Grass, Ground.Path, Ground.Grass, Ground.Water, Ground.DeepWater };
            for (int y = 0; y < 24; y++)
            {
                Art.GroundColors(rows[Mathf.Min(rows.Length - 1, (23 - y) / 4)], out var a, out var b, out var c);
                for (int x = 0; x < 48; x++)
                {
                    int n = (x * 7 + y * 13) % 11;
                    cv.Set(x, y, n == 0 ? b : n == 5 ? c : a);
                }
            }
            return cv.ToSpriteCentered();
        });
    }
}
