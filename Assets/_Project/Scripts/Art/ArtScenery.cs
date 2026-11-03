using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Theme scenery (deco_&lt;theme&gt;_0..3) and landmarks (landmark_&lt;theme&gt;) from the same shape kit as the props
    /// (Art/ArtObjects.cs): flat palette tones, a light side toward the top-left, one outline. Landmarks are big but
    /// keep the simple shapes; their size matches the theme's footprint.
    /// </summary>
    public static partial class Art
    {
        static readonly Tone Redrock = new Tone("a48656", "7a6457", "c1a572"), Water = new Tone("2986b8", "1f4f7a", "6cc0dc"),
            Lava = new Tone("f46616", "cb3416", "e1c549"), Moss = new Tone("70a744", "387840", "a8c44b"), Roof = new Tone("2986b8", "1f4f7a", "6cc0dc");

        static Sprite Scenery(string key)
        {
            var p = key.Split('_');
            if (p.Length == 2 && p[0] == "landmark") return Landmark(p[1]);
            if (p.Length == 3 && p[0] == "deco" && int.TryParse(p[2], out int v)) return Deco(p[1], v);
            return Prop("pillar_broken");
        }

        // ───────────── Theme decor ─────────────

        static Sprite Deco(string theme, int v)
        {
            PixelCanvas cv;
            switch (theme)
            {
                case "canyon":
                    switch (v)
                    {
                        case 0: cv = new PixelCanvas(22, 44); Mesa(cv, 11, 2, 9, 40, Sandstone); return Done(cv);
                        case 1: return DrawRockTone(17, 15, Sandstone);
                        case 2: cv = new PixelCanvas(18, 18); Canopy(cv, 9, 8, 8, 7, Redrock, 41, 7); return Done(cv);
                        default: return CactusTree(v);
                    }
                case "crystal":
                {
                    int[] w = { 28, 22, 13, 16 }, h = { 37, 40, 21, 28 };
                    cv = new PixelCanvas(w[v % 4], h[v % 4]);
                    float cx = cv.W / 2f;
                    Ball(cv, cx, 3, cv.W / 2f - 2, 3, StoneDark, false);
                    Shard(cv, cx, 2, Mathf.Max(5, cv.W * 0.38f), cv.H - 3, Crystal);
                    if (cv.W > 15) { Shard(cv, cx - cv.W * 0.28f, 2, cv.W * 0.24f, cv.H / 2, Crystal, -0.1f); Shard(cv, cx + cv.W * 0.28f, 2, cv.W * 0.22f, cv.H / 2 - 4, Crystal, 0.1f); }
                    return Done(cv);
                }
                case "fortress":
                    switch (v)
                    {
                        case 0:
                            cv = new PixelCanvas(40, 50);
                            Bricks(cv, 6, 1, 28, 40, Stone, 7, 4);
                            for (int x = 6; x < 34; x += 7) cv.Rect(x, 41, 4, 3 + (x * 5) % 6, Stone.m);
                            cv.Rect(30, 1, 4, 40, Stone.s); cv.Rect(16, 1, 8, 12, Obsidian.s); cv.Ellipse(20, 13, 4, 3, Obsidian.s);
                            cv.Rect(8, 30, 6, 8, Moss.m);
                            return Done(cv);
                        case 1:
                            cv = new PixelCanvas(28, 26);
                            Bricks(cv, 1, 1, 26, 16, Stone, 7, 4);
                            for (int x = 1; x < 27; x++) cv.Rect(x, 17, 1, (x * 11) % 7, Stone.m);
                            cv.Rect(18, 1, 8, 3, Moss.m);
                            return Done(cv);
                        case 2:
                            cv = new PixelCanvas(30, 38);
                            Box(cv, 3, 1, 24, 7, 2, Stone);
                            cv.Ellipse(15, 18, 7, 9, Stone.s); cv.Ellipse(14, 19, 6, 8, Stone.m);
                            cv.Ellipse(15, 30, 6, 6, Stone.s); cv.Ellipse(14, 31, 5, 5, Stone.m); cv.Rect(11, 30, 8, 2, Obsidian.s);
                            Thick(cv, 23, 10, 23, 34, 2, Stone.l); cv.Rect(20, 16, 7, 2, Stone.s);
                            return Done(cv);
                        default: return Prop("banner");
                    }
                case "glacier":
                    switch (v)
                    {
                        case 0: cv = new PixelCanvas(20, 37); Shard(cv, 10, 1, 9, 35, Ice); Shard(cv, 4, 1, 5, 16, Ice, -0.1f); Shard(cv, 16, 1, 5, 12, Ice, 0.1f); return Done(cv);
                        case 1: { var s = DrawRockTone(26, 22, Stone, true); return s; }
                        case 2:
                            cv = new PixelCanvas(24, 28);
                            Box(cv, 2, 1, 20, 18, 7, Ice);
                            cv.Line(6, 4, 10, 14, Ice.l); cv.Rect(2, 25, 20, 2, Snow.m);
                            return Done(cv);
                        default: cv = new PixelCanvas(18, 22); Canopy(cv, 9, 10, 8, 9, LeafTeal, 77, 6); cv.Ellipse(8, 17, 6, 2.5f, Snow.m); return Done(cv);
                    }
                case "mirror":
                {
                    int[] w = { 18, 20, 16, 13 }, h = { 34, 37, 40, 35 };
                    cv = new PixelCanvas(w[v % 4], h[v % 4]);
                    float cx = cv.W / 2f;
                    Shard(cv, cx, 1, cv.W - 4, cv.H - 2, Mirror, (v % 2 == 0 ? 1 : -1) * 0.03f);
                    for (int k = 4; k < cv.H - 6; k += 7) cv.Line(Mathf.RoundToInt(cx - 2), k, Mathf.RoundToInt(cx), k + 4, Mirror.l);
                    return Done(cv);
                }
                case "overgrowth":
                    switch (v)
                    {
                        case 0:
                            cv = new PixelCanvas(31, 36);
                            for (int i = 0; i < 7; i++)
                            {
                                float a = Mathf.Lerp(0.15f, Mathf.PI - 0.15f, i / 6f);
                                Thick(cv, 15.5f, 2, 15.5f + Mathf.Cos(a) * 14, 2 + Mathf.Sin(a) * 32, 3, i % 2 == 0 ? Moss.s : Moss.m);
                            }
                            return Done(cv);
                        case 1: return Mushroom(44, 42, Cloth);
                        case 2:
                            cv = new PixelCanvas(21, 26);
                            Thick(cv, 10, 1, 4, 22, 5, Leaf.m); Thick(cv, 10, 1, 17, 20, 5, Leaf.s); Thick(cv, 10, 1, 10, 24, 4, Leaf.l);
                            return Done(cv);
                        default: return Mushroom(18, 22, new Tone("e1c549", "a48656", "f2efe6"));
                    }
                case "swamp":
                    switch (v)
                    {
                        case 0: return DeadTree(v + 1, false);
                        case 1:
                            cv = new PixelCanvas(37, 36);
                            for (int i = 0; i < 5; i++) Thick(cv, 4 + i * 7, 1, 18 + (i - 2) * 2, 22, 3, i % 2 == 0 ? Bark.m : Bark.s);
                            Canopy(cv, 18, 26, 16, 9, LeafOlive, 51, 7);
                            return Done(cv);
                        case 2:
                            cv = new PixelCanvas(21, 26);
                            for (int i = 0; i < 6; i++) { int x = 2 + i * 3, hh = 14 + (i * 7) % 9; cv.Rect(x, 1, 1, hh, i % 2 == 0 ? Moss.s : Moss.m); if (i % 2 == 0) cv.Rect(x, hh - 4, 2, 5, Bark.s); }
                            return Done(cv);
                        default:
                            cv = new PixelCanvas(23, 18);
                            cv.Rect(2, 2, 18, 10, Bark.m); cv.Rect(2, 2, 18, 3, Bark.s); cv.Ellipse(20, 7, 2.5f, 5, Wood.l); cv.Ellipse(20, 7, 1, 2.5f, Bark.s);
                            cv.Rect(4, 11, 9, 3, Moss.m);
                            return Done(cv);
                    }
                case "volcano":
                    switch (v)
                    {
                        case 0: return DeadTree(v + 3, true);
                        case 1: { cv = new PixelCanvas(23, 18); Ball(cv, 11.5f, 8, 10, 7, Obsidian); cv.Line(6, 3, 10, 9, Lava.m); cv.Line(10, 9, 15, 11, Lava.m); return Done(cv); }
                        case 2: cv = new PixelCanvas(15, 31); Shard(cv, 7.5f, 1, 10, 29, Obsidian); cv.Rect(7, 6, 1, 14, Lava.s); return Done(cv);
                        default:
                            cv = new PixelCanvas(24, 20);
                            Ball(cv, 7, 6, 6, 5, Obsidian); Ball(cv, 16, 7, 7, 6, Obsidian); Ball(cv, 11, 12, 6, 5, Obsidian);
                            cv.Set(9, 6, Lava.m); cv.Set(10, 7, Lava.m); cv.Set(17, 9, Lava.m);
                            return Done(cv);
                    }
            }
            return Prop("pillar_broken");
        }

        static Sprite DrawRockTone(int W, int H, Tone t, bool snow = false)
        {
            var cv = new PixelCanvas(W, H + 2);
            float cx = W / 2f;
            cv.Ellipse(cx, H * 0.45f, W / 2f - 1, H * 0.5f, t.s);
            cv.Ellipse(cx - 1.5f, H * 0.52f, W / 2f - 3, H * 0.42f, t.m);
            cv.Ellipse(cx - W * 0.16f, H * 0.68f, W * 0.18f, H * 0.16f, t.l);
            cv.Rect(0, 0, W, 1, default);
            if (snow) cv.Ellipse(cx - 1, H * 0.85f, W / 2f - 3, 3, Snow.m);
            return Done(cv);
        }

        static Sprite Mushroom(int W, int H, Tone cap)
        {
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            cv.Rect(Mathf.RoundToInt(cx - W * 0.12f), 1, Mathf.RoundToInt(W * 0.24f), H / 2, Snow.s);
            cv.Rect(Mathf.RoundToInt(cx - W * 0.12f), 1, Mathf.RoundToInt(W * 0.16f), H / 2, Snow.m);
            Ball(cv, cx, H * 0.66f, W / 2f - 1, H * 0.3f, cap);
            cv.Circle(cx - W * 0.2f, H * 0.7f, W * 0.06f + 0.5f, Snow.m); cv.Circle(cx + W * 0.15f, H * 0.8f, W * 0.05f + 0.5f, Snow.m);
            return Done(cv);
        }

        /// <summary>A flat-topped rock column: strata bands, a lit top, the right third in shade.</summary>
        static void Mesa(PixelCanvas cv, float cx, int y0, float halfW, int h, Tone t, Tone band = default, bool hasBand = false)
        {
            for (int k = 0; k < h; k++)
            {
                float half = halfW * (1 - 0.18f * k / h) + ((k * 7) % 5 == 0 ? 1 : 0);
                int a = Mathf.RoundToInt(cx - half), b = Mathf.RoundToInt(cx + half);
                bool stripe = (k / 5) % 3 == 1;
                var main = stripe && hasBand ? band.m : t.m;
                var shade = stripe && hasBand ? band.s : t.s;
                cv.Rect(a, y0 + k, b - a, 1, main);
                cv.Rect(b - Mathf.RoundToInt(half * 0.6f), y0 + k, Mathf.RoundToInt(half * 0.6f), 1, shade);
                if (k % 5 == 4) cv.Rect(a, y0 + k, b - a, 1, shade);
            }
            int topHalf = Mathf.RoundToInt(halfW * 0.82f);
            cv.Ellipse(cx, y0 + h, topHalf, 3, t.l);
        }

        // ───────────── Landmarks ─────────────

        static Sprite Landmark(string theme)
        {
            PixelCanvas cv;
            switch (theme)
            {
                case "canyon":
                {
                    cv = new PixelCanvas(96, 160);
                    Mesa(cv, 48, 2, 44, 150, Sandstone, Redrock, true);
                    cv.Ellipse(44, 14, 13, 14, Obsidian.s); cv.Rect(31, 2, 26, 12, Obsidian.s);          // a cave mouth
                    Canopy(cv, 30, 154, 9, 5, Moss, 12, 6);
                    return Done(cv);
                }
                case "crystal":
                {
                    cv = new PixelCanvas(126, 160);
                    Ball(cv, 63, 14, 58, 14, StoneDark);
                    Shard(cv, 63, 10, 34, 148, Crystal);
                    Shard(cv, 36, 10, 18, 80, Crystal, -0.12f);
                    Shard(cv, 92, 10, 20, 96, Crystal, 0.1f);
                    Shard(cv, 22, 8, 10, 40, Crystal, -0.2f); Shard(cv, 106, 8, 10, 46, Crystal, 0.18f);
                    for (int i = 0; i < 4; i++) cv.Rect(56 + i * 2, 50 + i * 22, 2, 10, Snow.m);
                    return Done(cv);
                }
                case "fortress":
                {
                    cv = new PixelCanvas(210, 178);
                    Ball(cv, 105, 14, 100, 14, Stone, false);
                    cv.Ellipse(105, 22, 96, 6, Moss.m);
                    Bricks(cv, 24, 20, 162, 60, Stone, 10, 5);
                    cv.Rect(170, 20, 16, 60, Stone.s);
                    for (int x = 24; x < 186; x += 10) cv.Rect(x, 80, 6, 6, Stone.m);
                    cv.Rect(24, 80, 162, 1, Stone.l);
                    foreach (var (tx, tw, th) in new[] { (18, 30, 96), (180, 30, 96), (88, 38, 130) })
                    {
                        Bricks(cv, tx - tw / 2 + (tx == 88 ? 17 : 0), 20, tw, th, Stone, 8, 5);
                        int left = tx - tw / 2 + (tx == 88 ? 17 : 0);
                        cv.Rect(left + tw - 6, 20, 6, th, Stone.s);
                        for (int k = 0; k < 34; k++)
                        {
                            float half = (tw / 2f + 3) * (1 - k / 34f);
                            int c = left + tw / 2;
                            cv.Rect(Mathf.RoundToInt(c - half), 20 + th + k, Mathf.RoundToInt(half), 1, Roof.m);
                            cv.Rect(c, 20 + th + k, Mathf.RoundToInt(half), 1, Roof.s);
                        }
                        cv.Rect(left + tw / 2 - 2, 20 + th - 30, 4, 10, Obsidian.s);
                    }
                    cv.Rect(91, 20, 28, 34, Obsidian.s); cv.Ellipse(105, 54, 14, 10, Obsidian.s);             // gate
                    cv.Rect(93, 20, 24, 30, Bark.s);
                    for (int x = 95; x < 117; x += 5) cv.Rect(x, 20, 1, 30, Obsidian.m);
                    foreach (int bx in new[] { 60, 146 }) { cv.Rect(bx, 44, 12, 26, Roof.m); cv.Rect(bx + 4, 52, 4, 4, Gold.m); }
                    return Done(cv);
                }
                case "glacier":
                {
                    cv = new PixelCanvas(194, 192);
                    Ball(cv, 97, 18, 92, 18, Snow, false);
                    Shard(cv, 97, 14, 44, 176, Ice);
                    Shard(cv, 58, 14, 28, 110, Ice, -0.08f); Shard(cv, 138, 14, 30, 124, Ice, 0.07f);
                    Shard(cv, 30, 12, 16, 60, Ice, -0.15f); Shard(cv, 166, 12, 16, 70, Ice, 0.14f);
                    for (int i = 0; i < 6; i++) cv.Ellipse(30 + i * 27, 24, 12, 4, Snow.m);
                    return Done(cv);
                }
                case "mirror":
                {
                    cv = new PixelCanvas(192, 200);
                    Box(cv, 30, 1, 132, 12, 4, StoneDark);
                    Box(cv, 40, 17, 112, 8, 3, StoneDark);
                    cv.Ellipse(96, 148, 50, 48, StoneDark.m); cv.Ellipse(104, 146, 40, 44, StoneDark.s);    // the arch
                    cv.Ellipse(90, 152, 30, 30, StoneDark.l);
                    cv.Rect(46, 28, 16, 120, StoneDark.m); cv.Rect(130, 28, 16, 120, StoneDark.s);
                    cv.Rect(62, 28, 68, 120, Mirror.m); cv.Ellipse(96, 148, 34, 34, Mirror.m);
                    cv.Rect(100, 28, 30, 120, Mirror.s);
                    for (int k = 0; k < 6; k++) Thick(cv, 66 + k * 10, 40, 80 + k * 10, 150, 2, Mirror.l);
                    cv.Rect(62, 28, 68, 6, Mirror.s);
                    foreach (var (sx, sy) in new[] { (22, 120), (170, 100), (30, 170), (160, 176) }) Shard(cv, sx, sy, 7, 16, Mirror);
                    return Done(cv);
                }
                case "overgrowth":
                {
                    cv = new PixelCanvas(178, 154);
                    for (int i = 0; i < 7; i++)
                    {
                        float dir = (i - 3) / 3f;
                        Thick(cv, 89 + dir * 14, 40, 89 + dir * 80, 2, 8 - Mathf.Abs(dir) * 3, i % 2 == 0 ? Bark.m : Bark.s);
                    }
                    cv.Rect(70, 2, 38, 70, Bark.m); cv.Rect(96, 2, 12, 70, Bark.s); cv.Rect(74, 6, 4, 60, Bark.l);
                    Canopy(cv, 89, 104, 76, 46, LeafDark, 3, 12);
                    Canopy(cv, 82, 112, 54, 32, Leaf, 9, 10);
                    for (int i = 0; i < 5; i++) cv.Rect(30 + i * 30, 56 - (i % 2) * 6, 2, 16, LeafDark.s);
                    return Done(cv);
                }
                case "swamp":
                {
                    cv = new PixelCanvas(194, 174);
                    Ball(cv, 97, 16, 92, 15, Water, false);
                    cv.Ellipse(97, 18, 86, 11, Water.m); cv.Rect(60, 16, 20, 1, Water.l); cv.Rect(120, 22, 26, 1, Water.l);
                    Box(cv, 34, 18, 126, 34, 6, StoneDark);
                    Box(cv, 52, 58, 90, 34, 6, StoneDark);
                    Box(cv, 72, 98, 50, 30, 6, StoneDark);
                    foreach (int y in new[] { 52, 92, 128 }) cv.Ellipse(97, y + 4, y == 52 ? 60 : y == 92 ? 44 : 24, 3, Moss.m);
                    cv.Rect(89, 18, 16, 22, Obsidian.s); cv.Ellipse(97, 40, 8, 6, Obsidian.s);
                    cv.Rect(84, 104, 26, 18, Obsidian.s); cv.Rect(95, 108, 4, 8, Hx("a8c44b"));
                    foreach (var (mx, my) in new[] { (30, 40), (166, 46), (50, 90), (148, 94) }) { cv.Rect(mx - 1, my - 8, 3, 8, Snow.s); Ball(cv, mx, my, 7, 4, Cloth); }
                    for (int i = 0; i < 6; i++) cv.Rect(40 + i * 22, 20, 2, 26 - (i % 3) * 6, LeafOlive.s);
                    return Done(cv);
                }
                case "volcano":
                {
                    cv = new PixelCanvas(210, 176);
                    int top = 150;
                    for (int y = 2; y < top; y++)
                    {
                        float u = (y - 2) / (float)(top - 2), half = Mathf.Lerp(100, 26, Mathf.Pow(u, 0.8f));
                        int a = Mathf.RoundToInt(105 - half), b = Mathf.RoundToInt(105 + half);
                        cv.Rect(a, y, b - a, 1, Obsidian.m);
                        cv.Rect(105 + Mathf.RoundToInt(half * 0.1f), y, b - 105 - Mathf.RoundToInt(half * 0.1f), 1, Obsidian.s);
                        cv.Rect(a, y, 3, 1, Obsidian.l);
                    }
                    cv.Ellipse(105, top, 26, 7, Obsidian.s); cv.Ellipse(105, top, 20, 4.5f, Lava.m); cv.Ellipse(103, top + 1, 10, 2, Lava.l);
                    for (int pass = 0; pass < 2; pass++)          // lava streams: the dark edge first, then the bright core
                        for (int s = 0; s < 2; s++)
                        {
                            float x = 100 + s * 10;
                            for (int y = top - 2; y > 10; y -= 2)
                            {
                                float nx = x + Mathf.Sin(y * 0.09f + s * 2) * 1.6f + (s == 0 ? -0.35f : 0.3f);
                                Thick(cv, x, y, nx, y - 2, pass == 0 ? 6 : 3, pass == 0 ? Lava.s : Lava.m);
                                x = nx;
                            }
                        }
                    for (int i = 0; i < 7; i++) Ball(cv, 20 + i * 28, 6 + (i % 2) * 2, 9, 6, Obsidian);
                    return Done(cv);
                }
            }
            return Prop("obelisk");
        }
    }
}
