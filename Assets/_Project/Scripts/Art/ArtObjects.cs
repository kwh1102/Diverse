using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Scenery and props by the art rules (Data/ArtStyle.asset): built from a few round shapes at the 1-dot scale, each
    /// in three palette tones (shade, base, a light patch toward the top-left), closed by one dark outline. Trees are
    /// lumpy canopies, stones faceted blobs, crystals pointed shards. The pivot is the bottom centre (where it stands).
    /// Sizes match the earlier pictures so placement and collisions don't change. Landmarks and theme decor: ArtScenery.cs.
    /// </summary>
    public static partial class Art
    {
        /// <summary>Three tones of one material: base, shade (bottom-right), light (top-left).</summary>
        struct Tone
        {
            public Color32 m, s, l;
            public Tone(string main, string shade, string light) { m = Pc(Pal.Hex(main)); s = Pc(Pal.Hex(shade)); l = Pc(Pal.Hex(light)); }
        }

        static Color32 Hx(string hex) => Pc(Pal.Hex(hex));
        static float Rn(int a, int b, int salt) => Hash.F(a, b, salt);

        static readonly Tone Leaf = new Tone("589543", "387840", "84ac49"), LeafDark = new Tone("387840", "194e41", "589543"),
            LeafPink = new Tone("e7a9cf", "b86f9c", "f2efe6"), LeafTeal = new Tone("6cc0dc", "2986b8", "c8ecef"), LeafOlive = new Tone("589543", "194e41", "70a744"),
            Bark = new Tone("7a6457", "544440", "a48656"), BarkDark = new Tone("3a3e4c", "272834", "4a4b55"), BarkPurple = new Tone("8a5fa8", "4b3566", "c79bd6"),
            Stone = new Tone("8a8a94", "5c5a61", "c9c6cc"), StoneDark = new Tone("4a4b55", "3a3e4c", "5c5a61"), Sandstone = new Tone("c1a572", "a48656", "d9c38c"),
            Snow = new Tone("f2efe6", "c9c6cc", "f2efe6"), Ice = new Tone("6cc0dc", "2986b8", "c8ecef"), Crystal = new Tone("c79bd6", "8a5fa8", "f2efe6"),
            Wood = new Tone("a48656", "7a6457", "c1a572"), Cloth = new Tone("cb3416", "572526", "f46616"), Gold = new Tone("e1c549", "a48656", "f2efe6"),
            Cactus = new Tone("70a744", "387840", "a8c44b"), Obsidian = new Tone("2f2e39", "21212c", "4a4b55"), Mirror = new Tone("8a5fa8", "4b3566", "c8ecef");

        // ───────────── Shape kit ─────────────

        /// <summary>A round body: its shade, the base a little toward the light, a light patch at the top-left.</summary>
        static void Ball(PixelCanvas cv, float cx, float cy, float rx, float ry, Tone t, bool light = true)
        {
            cv.Ellipse(cx, cy, rx, ry, t.s);
            cv.Ellipse(cx - rx * 0.12f, cy + ry * 0.14f, rx * 0.84f, ry * 0.8f, t.m);
            if (light && rx > 3) cv.Ellipse(cx - rx * 0.42f, cy + ry * 0.42f, Mathf.Max(1, rx * 0.28f), Mathf.Max(1, ry * 0.24f), t.l);
        }

        /// <summary>A leafy crown: a core with bumps round the rim, shaded below, lit on its upper-left bumps.</summary>
        static void Canopy(PixelCanvas cv, float cx, float cy, float rx, float ry, Tone t, int seed, int bumps = 7)
        {
            var bs = new List<Vector3>();
            for (int i = 0; i < bumps; i++)
            {
                float a = Mathf.PI * 2 * i / bumps + Rn(seed, i, 901) * 0.6f;
                float r = Mathf.Min(rx, ry) * (0.42f + 0.14f * Rn(seed, i, 902));
                bs.Add(new Vector3(cx + Mathf.Cos(a) * (rx - r * 0.75f), cy + Mathf.Sin(a) * (ry - r * 0.75f), r));
            }
            cv.Ellipse(cx, cy, rx * 0.85f, ry * 0.85f, t.s);
            foreach (var b in bs) cv.Circle(b.x, b.y, b.z, t.s);
            cv.Ellipse(cx - 1, cy + 1.5f, rx * 0.78f - 1, ry * 0.72f - 1, t.m);
            foreach (var b in bs) if (b.y > cy - ry * 0.35f) cv.Circle(b.x - 1, b.y + 1, b.z - 1.4f, t.m);
            foreach (var b in bs) if (b.y > cy && b.x < cx + rx * 0.2f) cv.Circle(b.x - 1.5f, b.y + 1.5f, Mathf.Max(1, b.z * 0.42f), t.l);
        }

        /// <summary>A pointed shard standing on (cx, y0): lit left face, shaded right face.</summary>
        static void Shard(PixelCanvas cv, float cx, int y0, float w, int h, Tone t, float lean = 0)
        {
            for (int k = 0; k < h; k++)
            {
                float u = k / (float)h, half = w * 0.5f * (u < 0.75f ? 1 : (1 - u) / 0.25f);
                float x = cx + lean * k;
                int a = Mathf.RoundToInt(x - half), b = Mathf.RoundToInt(x + half);
                int mid = Mathf.RoundToInt(x);
                if (b < a) continue;
                cv.Rect(a, y0 + k, Mathf.Max(1, mid - a), 1, t.m);
                cv.Rect(mid, y0 + k, Mathf.Max(1, b - mid + 1), 1, t.s);
                if (k % 5 == 2 && mid - a > 1) cv.Set(a + 1, y0 + k, t.l);
            }
        }

        static void Thick(PixelCanvas cv, float x0, float y0, float x1, float y1, float w, Color32 c)
        {
            int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))) + 1;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                cv.Circle(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), w * 0.5f, c);
            }
        }

        /// <summary>An upright box seen at 3/4: front face (shade bottom row), top face (light).</summary>
        static void Box(PixelCanvas cv, int x, int y, int w, int h, int top, Tone t)
        {
            cv.Rect(x, y, w, h, t.m);
            cv.Rect(x + w - 2, y, 2, h, t.s);
            cv.Rect(x, y, w, 1, t.s);
            cv.Rect(x, y + h, w, top, t.l);
        }

        static void Bricks(PixelCanvas cv, int x, int y, int w, int h, Tone t, int bw = 6, int bh = 3)
        {
            cv.Rect(x, y, w, h, t.m);
            for (int yy = 0; yy < h; yy++)
            {
                int row = yy / bh;
                if (yy % bh == bh - 1) { cv.Rect(x, y + yy, w, 1, t.s); continue; }
                for (int xx = 0; xx < w; xx++)
                    if ((xx + (row % 2) * (bw / 2)) % bw == bw - 1) cv.Set(x + xx, y + yy, t.s);
            }
        }

        /// <summary>
        /// The shadow a standing sprite casts (light from the top-left): its silhouette laid on the ground toward the lower
        /// right, flat, in the art rules' shadow colour. Unreadable sprites get the round contact shadow.
        /// </summary>
        public static Sprite CastShadow(Sprite s) => Get("cast_" + s.name, () =>
        {
            var tex = s.texture;
            if (tex == null || !tex.isReadable) return Shadow(Mathf.RoundToInt(s.rect.width * 0.7f));
            var r = s.rect;
            int W = (int)r.width, H = (int)r.height;
            const float lean = 0.55f, flat = 0.28f;           // how far the top falls to the right / below the base
            int down = Mathf.CeilToInt(H * flat) + 2, wide = W + Mathf.CeilToInt(H * lean) + 2;
            var cv = new PixelCanvas(wide, down + 3);
            var px = tex.GetPixels32();
            var sc = Pi(DB.ArtStyle.shadow);
            sc.a = (byte)(DB.ArtStyle.shadowAlpha * 255);
            int baseY = Mathf.RoundToInt(s.pivot.y);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (px[((int)r.y + y) * tex.width + (int)r.x + x].a < 128) continue;
                    float h = Mathf.Max(0, y - baseY);
                    int sx = Mathf.RoundToInt(x + h * lean), sy = Mathf.RoundToInt(down - h * flat);
                    cv.Set(sx, sy, sc); cv.Set(sx, sy + 1, sc);
                }
            return cv.ToSprite(new Vector2(s.pivot.x, down + 0.5f));
        });

        static Sprite Done(PixelCanvas cv, bool outline = true)
        {
            if (outline) cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(cv.W / 2f, 1));
        }

        // ───────────── Nature ─────────────

        public static Sprite Tree(Biome b, int v) => Get($"tree_{b}_{v}", () => DrawTree(b, v));

        static Sprite DrawTree(Biome b, int v)
        {
            switch (b)
            {
                case Biome.Tundra: return Pine(v, true);
                case Biome.Forest: return v % 2 == 0 ? Pine(v, false) : RoundTree(v, LeafDark, Bark, false);
                case Biome.Dunes: return CactusTree(v);
                case Biome.Marsh: return RoundTree(v, LeafOlive, Bark, true);
                case Biome.Ashland: return DeadTree(v, true);
                case Biome.Crystal: return CrystalTree(v);
                case Biome.Fortress: return Cypress(v);
                case Biome.Mirror: return RoundTree(v, LeafTeal, BarkPurple, false);
                default: return RoundTree(v, v % 4 == 2 ? LeafPink : Leaf, Bark, false);
            }
        }

        static void Trunk(PixelCanvas cv, float cx, int h, int w, Tone t)
        {
            int x = Mathf.RoundToInt(cx - w / 2f);
            cv.Rect(x - 1, 1, w + 2, 2, t.s);                  // roots
            cv.Rect(x, 1, w, h, t.m);
            cv.Rect(x + w - 2, 1, 2, h, t.s);
            cv.Rect(x + 1, 3, 1, h - 4, t.l);
        }

        static Sprite RoundTree(int v, Tone leaf, Tone bark, bool droopy)
        {
            int W = 34 + (v % 2) * 6, H = 46 + (v % 3) * 4;
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            Trunk(cv, cx, 16, 6, bark);
            float rx = W / 2f - 2, ry = (H - 14) / 2f;
            Canopy(cv, cx, H - ry - 2, rx, ry, leaf, v * 13 + 5, 8);
            if (droopy)
                for (int i = 0; i < 6; i++)
                {
                    int x = Mathf.RoundToInt(4 + i * (W - 8) / 5f);
                    int len = 5 + (int)(Rn(v, i, 903) * 6);
                    cv.Rect(x, H - 2 * (int)ry - len + 2, 2, len, leaf.s);
                }
            return Done(cv);
        }

        static Sprite Pine(int v, bool snow)
        {
            int W = 26 + (v % 2) * 4, H = 48 + (v % 3) * 5;
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            Trunk(cv, cx, 8, 4, Bark);
            var t = LeafDark;
            int tiers = 3, top = H - 2;
            for (int i = 0; i < tiers; i++)
            {
                int y0 = 6 + i * (H - 10) / (tiers + 1);
                int h = (H - 6) - y0 - (tiers - 1 - i) * 3;
                if (i < tiers - 1) h = (H - 10) / (tiers + 1) + 10;
                float w = (W - 2) * (1 - i * 0.22f);
                for (int k = 0; k < h && y0 + k <= top; k++)
                {
                    float half = w / 2f * (1 - k / (float)h);
                    int a = Mathf.RoundToInt(cx - half), m = Mathf.RoundToInt(cx), bb = Mathf.RoundToInt(cx + half);
                    cv.Rect(a, y0 + k, Mathf.Max(1, m - a), 1, t.m);
                    cv.Rect(m, y0 + k, Mathf.Max(1, bb - m), 1, t.s);
                    if (k < 2) cv.Rect(a, y0 + k, Mathf.Max(1, bb - a), 1, t.s);
                    if (snow && k > h - 4 - i) cv.Rect(a, y0 + k, Mathf.Max(1, bb - a), 1, Snow.m);
                    else if (snow && k == 2 && (a + y0) % 3 != 0) cv.Rect(a + 1, y0 + k, Mathf.Max(1, (bb - a) / 2), 1, Snow.s);
                }
            }
            return Done(cv);
        }

        static Sprite CactusTree(int v)
        {
            int W = 22, H = 34 + (v % 3) * 6;
            var cv = new PixelCanvas(W, H);
            int cx = W / 2 - 3;
            cv.Rect(cx, 1, 6, H - 4, Cactus.m); cv.Ellipse(cx + 3, H - 4, 3, 3, Cactus.m);
            cv.Rect(cx + 4, 1, 2, H - 4, Cactus.s);
            cv.Rect(cx + 1, 3, 1, H - 8, Cactus.l);
            int ay = 10 + v % 4 * 2;
            cv.Rect(cx - 5, ay, 5, 3, Cactus.m); cv.Rect(cx - 5, ay, 3, 10, Cactus.m); cv.Rect(cx - 4, ay + 2, 1, 6, Cactus.l);
            if (v % 2 == 0) { cv.Rect(cx + 6, ay + 5, 5, 3, Cactus.s); cv.Rect(cx + 8, ay + 5, 3, 9, Cactus.s); }
            cv.Set(cx + 2, H - 2, Hx("e7a9cf")); cv.Set(cx + 3, H - 1, Hx("e7a9cf"));
            return Done(cv);
        }

        static Sprite DeadTree(int v, bool embers)
        {
            int W = 26 + (v % 2) * 4, H = 44 + (v % 3) * 3;
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            Thick(cv, cx, 1, cx + (v % 2 == 0 ? 1 : -1), H - 10, 5, BarkDark.m);
            Thick(cv, cx + 1.5f, 1, cx + 1.5f, H - 12, 2, BarkDark.s);
            for (int i = 0; i < 4; i++)
            {
                float y = H * (0.4f + i * 0.13f), dir = i % 2 == 0 ? -1 : 1;
                float ex = cx + dir * (W / 2f - 3) * (0.7f + Rn(v, i, 911) * 0.3f), ey = y + 6 + Rn(v, i, 912) * 6;
                Thick(cv, cx, y, ex, ey, 2, BarkDark.m);
                if (embers) cv.Set(Mathf.RoundToInt(ex), Mathf.RoundToInt(ey), Hx("f46616"));
            }
            if (embers) { cv.Set(Mathf.RoundToInt(cx), H / 3, Hx("f46616")); cv.Set(Mathf.RoundToInt(cx) - 1, H / 3 + 1, Hx("cb3416")); }
            return Done(cv);
        }

        static Sprite CrystalTree(int v)
        {
            int W = 26 + (v % 2) * 4, H = 46 + (v % 3) * 4;
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            Ball(cv, cx, 5, W / 2f - 3, 5, StoneDark, false);
            Shard(cv, cx - 1, 4, 9, H - 6, Crystal);
            Shard(cv, cx - 7, 4, 6, H / 2, Crystal, -0.08f);
            Shard(cv, cx + 6, 4, 6, H / 2 + 4, Crystal, 0.08f);
            return Done(cv);
        }

        static Sprite Cypress(int v)
        {
            int W = 16 + (v % 2) * 4, H = 46 + (v % 3) * 4;
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            Trunk(cv, cx, 6, 3, Bark);
            Canopy(cv, cx, H / 2f + 3, W / 2f - 1, H / 2f - 5, LeafDark, v * 7 + 3, 9);
            return Done(cv);
        }

        public static Sprite Rock(Biome b, int v) => Get($"rock_{b}_{v}", () => DrawRock(b, v));

        static Sprite DrawRock(Biome b, int v)
        {
            int W = 28 - v * 5, H = 26 - v * 5;
            var cv = new PixelCanvas(W, H + 6);
            float cx = W / 2f;
            var t = b switch
            {
                Biome.Ashland => StoneDark, Biome.Dunes => Sandstone, Biome.Tundra => Stone, Biome.Mirror => Mirror,
                Biome.Crystal => StoneDark, _ => Stone,
            };
            cv.Ellipse(cx, H * 0.45f, W / 2f - 1, H * 0.5f, t.s);
            cv.Ellipse(cx - 1.5f, H * 0.52f, W / 2f - 3, H * 0.42f, t.m);
            cv.Ellipse(cx - W * 0.16f, H * 0.68f, W * 0.18f, H * 0.16f, t.l);
            cv.Rect(0, 0, W, 1, default);
            cv.Line(Mathf.RoundToInt(cx + 2), Mathf.RoundToInt(H * 0.6f), Mathf.RoundToInt(cx + 4), Mathf.RoundToInt(H * 0.3f), t.s);
            switch (b)
            {
                case Biome.Tundra: cv.Ellipse(cx - 1, H * 0.86f, W / 2f - 4, 2.5f, Snow.m); break;
                case Biome.Marsh: case Biome.Forest: case Biome.Meadow: cv.Ellipse(cx - 3, H * 0.85f, W * 0.22f, 2, Leaf.m); break;
                case Biome.Crystal: Shard(cv, cx + 2, Mathf.RoundToInt(H * 0.6f), 5, 9, Crystal); break;
                case Biome.Ashland: cv.Line(Mathf.RoundToInt(cx - 4), 4, Mathf.RoundToInt(cx - 1), 9, Hx("f46616")); break;
            }
            return Done(cv);
        }

        public static Sprite Bush(Biome b, int v) => Get($"bush_{b}_{v}", () => DrawBush(b, v));

        static Sprite DrawBush(Biome b, int v)
        {
            int W = v == 0 ? 24 : 19, H = v == 0 ? 20 : 16;
            var cv = new PixelCanvas(W, H);
            float cx = W / 2f;
            switch (b)
            {
                case Biome.Crystal:
                    Shard(cv, cx - 3, 1, 6, H - 4, Crystal, -0.05f); Shard(cv, cx + 3, 1, 5, H - 7, Crystal, 0.06f);
                    return Done(cv);
                case Biome.Fortress:
                    cv.Rect(2, 1, W - 4, H - 5, LeafDark.s); cv.Rect(2, 3, W - 6, H - 6, LeafDark.m); cv.Rect(3, H - 6, W / 3, 2, LeafDark.l);
                    return Done(cv);
            }
            var t = b switch
            {
                Biome.Forest => LeafDark, Biome.Dunes => Sandstone, Biome.Ashland => StoneDark, Biome.Marsh => LeafOlive, Biome.Mirror => LeafTeal, _ => Leaf,
            };
            Canopy(cv, cx, H / 2f, W / 2f - 1, H / 2f - 1, t, v * 31 + (int)b, 6);
            if (b == Biome.Tundra) cv.Ellipse(cx - 1, H - 4, W / 2f - 4, 2, Snow.m);
            if (b == Biome.Meadow && v == 0) { cv.Set((int)cx - 3, H / 2 + 2, Hx("cb3416")); cv.Set((int)cx + 3, H / 2 + 4, Hx("cb3416")); cv.Set((int)cx, H / 2 - 1, Hx("cb3416")); }
            if (b == Biome.Ashland) { cv.Set((int)cx - 2, H / 2, Hx("f46616")); cv.Set((int)cx + 3, H / 2 + 3, Hx("f46616")); }
            return Done(cv);
        }

        public static Sprite Tuft(Biome b, int v) => Get($"tuft_{b}_{v}", () =>
        {
            var cv = new PixelCanvas(11, 11);
            var t = b switch
            {
                Biome.Dunes => Sandstone, Biome.Ashland => StoneDark, Biome.Tundra => Snow, Biome.Crystal => Crystal, Biome.Mirror => LeafTeal,
                Biome.Marsh or Biome.Forest => LeafDark, _ => Leaf,
            };
            int n = 3 + v;
            for (int i = 0; i < n; i++)
            {
                float x = 5 + (i - (n - 1) / 2f) * 2f;
                int h = 4 + (int)(Rn(i, v + (int)b * 3, 921) * 5);
                Thick(cv, 5.5f, 1, x + (x - 5) * 0.4f, h, 1, i % 2 == 0 ? t.s : t.m);
            }
            return cv.ToSprite(new Vector2(5.5f, 1));
        });

        public static Sprite Flower(int v) => Get($"flower_{v}", () =>
        {
            var cv = new PixelCanvas(11, 13);
            string[] petal = { "e1c549", "f2efe6", "e7a9cf", "c79bd6", "cb3416" };
            var p = Hx(petal[Mathf.Abs(v) % petal.Length]);
            var heart = v % petal.Length == 0 ? Hx("f2efe6") : Hx("e1c549");
            void One(int x, int y)
            {
                cv.Rect(x, 1, 1, y - 1, Leaf.s);
                cv.Set(x - 1, y, p); cv.Set(x + 1, y, p); cv.Set(x, y - 1, p); cv.Set(x, y + 1, p); cv.Set(x, y, heart);
            }
            One(3, 7); One(7, 9);
            if (v % 2 == 1) One(5, 4);
            return cv.ToSprite(new Vector2(5.5f, 1));
        });

        // ───────────── Houses ─────────────

        public static Sprite House(int v, Color32 roof) => Get($"house_{v}_{ColorUtility.ToHtmlStringRGB(roof).ToLowerInvariant()}", () =>
        {
            int W = v == 0 ? 76 : 72, H = v == 0 ? 68 : 68;
            var cv = new PixelCanvas(W, H);
            var r = RoofColor(roof);
            var rs = Darker(r);
            var rl = Lighter(r);
            var wall = new Tone("d9c38c", "a48656", "f2efe6");
            int wx = 8, ww = W - 16, wh = 26;
            cv.Rect(wx, 1, ww, wh, wall.m);
            cv.Rect(wx + ww - 3, 1, 3, wh, wall.s);
            cv.Rect(wx, 1, ww, 2, Stone.s);                                      // footing
            for (int x = wx + 4; x < wx + ww - 4; x += 6) cv.Rect(x, 3, 1, wh - 4, wall.s);   // timber
            // Door and windows
            int dx = W / 2 - 4;
            cv.Rect(dx, 1, 9, 14, Bark.s); cv.Rect(dx + 1, 1, 7, 13, Bark.m); cv.Set(dx + 6, 7, Gold.m);
            foreach (int x in new[] { wx + 5, wx + ww - 15 })
            {
                cv.Rect(x, 12, 10, 9, Bark.s); cv.Rect(x + 1, 13, 8, 7, Hx("2986b8")); cv.Rect(x + 1, 18, 3, 2, Hx("6cc0dc")); cv.Rect(x + 5, 13, 1, 7, Bark.s);
            }
            // Roof: a slope seen from the front, eaves overhanging, tile rows, a lit ridge
            int ry0 = wh - 2, ry1 = H - 3;
            for (int y = ry0; y <= ry1; y++)
            {
                float u = (y - ry0) / (float)(ry1 - ry0);
                int inset = Mathf.RoundToInt(u * (v == 0 ? 14 : 22));
                int a = 2 + inset, b = W - 2 - inset;
                cv.Rect(a, y, b - a, 1, (y - ry0) % 5 == 4 ? rs : r);
                cv.Rect(b - 3, y, 3, 1, rs);
                if (y == ry0 || y == ry0 + 1) cv.Rect(a, y, b - a, 1, rs);
                if (y >= ry1 - 1) cv.Rect(a, y, b - a, 1, rl);
            }
            if (v == 1)
            {
                // A gable window in the roof
                int gx = W / 2 - 6, gy = ry0 + 10;
                cv.Rect(gx, gy, 12, 10, wall.m); cv.Rect(gx + 3, gy + 2, 6, 5, Hx("2986b8"));
                for (int k = 0; k < 6; k++) cv.Rect(gx - 1 + k, gy + 10 + k, 14 - 2 * k, 1, rs);
            }
            // Chimney
            cv.Rect(W - 22, H - 16, 6, 14, Stone.m); cv.Rect(W - 18, H - 16, 2, 14, Stone.s); cv.Rect(W - 23, H - 3, 8, 2, Stone.s);
            return Done(cv);
        });

        /// <summary>A roof keeps its hue: the nearest saturated palette colour of the same family.</summary>
        static Color32 RoofColor(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            string hex = h < 0.07f || h > 0.92f ? "cb3416" : h < 0.2f ? "e1c549" : h < 0.45f ? "70a744" : h < 0.68f ? "2986b8" : "8a5fa8";
            return Hx(hex);
        }

        static Color32 Darker(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Pc(Color.HSVToRGB(h, Mathf.Min(1, s * 1.1f), v * 0.62f));
        }
        static Color32 Lighter(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Pc(Color.HSVToRGB(h, s * 0.6f, Mathf.Min(1, v * 1.25f)));
        }

        // ───────────── Props ─────────────

        public static Sprite Prop(string id) => Get($"prop_{id}", () => DrawProp(id));

        static Sprite DrawProp(string id)
        {
            PixelCanvas cv;
            switch (id)
            {
                case "board":
                    cv = new PixelCanvas(28, 32);
                    cv.Rect(4, 1, 3, 26, Bark.s); cv.Rect(21, 1, 3, 26, Bark.s);
                    Box(cv, 2, 10, 24, 16, 2, Wood);
                    cv.Rect(5, 14, 7, 6, Snow.m); cv.Rect(14, 13, 8, 8, Hx("e1c549")); cv.Rect(6, 16, 5, 1, Stone.s); cv.Rect(15, 16, 6, 1, Bark.s);
                    return Done(cv);
                case "well":
                    cv = new PixelCanvas(32, 44);
                    Ball(cv, 16, 9, 13, 8, Stone, false);
                    Bricks(cv, 3, 3, 26, 10, Stone, 6, 4);
                    cv.Ellipse(16, 13, 12, 4, Stone.l); cv.Ellipse(16, 13, 9, 2.5f, Hx("1f4f7a"));
                    cv.Rect(4, 13, 3, 20, Bark.m); cv.Rect(25, 13, 3, 20, Bark.s);
                    for (int k = 0; k < 9; k++) cv.Rect(1 + k, 32 + k, 30 - 2 * k, 1, k < 2 ? Cloth.s : Cloth.m);
                    cv.Rect(15, 18, 2, 14, Bark.s); cv.Rect(13, 18, 6, 4, Wood.m);
                    return Done(cv);
                case "lamp":
                    cv = new PixelCanvas(12, 36);
                    cv.Rect(4, 1, 4, 3, StoneDark.s); cv.Rect(5, 3, 2, 24, StoneDark.m);
                    cv.Rect(2, 26, 8, 8, StoneDark.s); cv.Rect(3, 27, 6, 6, Hx("e1c549")); cv.Rect(4, 29, 2, 3, Snow.m); cv.Rect(1, 34, 10, 1, StoneDark.s);
                    return Done(cv);
                case "fence":
                    cv = new PixelCanvas(20, 14);
                    cv.Rect(1, 1, 3, 12, Wood.m); cv.Rect(16, 1, 3, 12, Wood.s); cv.Rect(0, 5, 20, 2, Wood.s); cv.Rect(0, 9, 20, 2, Wood.m);
                    return Done(cv);
                case "tent":
                    cv = new PixelCanvas(44, 46);
                    for (int k = 0; k < 40; k++)
                    {
                        float half = 20 * (1 - k / 40f);
                        cv.Rect(Mathf.RoundToInt(22 - half), 1 + k, Mathf.RoundToInt(half), 1, Cloth.m);
                        cv.Rect(22, 1 + k, Mathf.RoundToInt(half), 1, Cloth.s);
                        if (k % 8 == 4) cv.Rect(Mathf.RoundToInt(22 - half), 1 + k, Mathf.RoundToInt(half * 2), 1, Hx("f2efe6"));
                    }
                    for (int k = 0; k < 14; k++) cv.Rect(22 - (14 - k) / 3, 1 + k, (14 - k) / 3 * 2, 1, Bark.s);
                    cv.Rect(21, 40, 2, 5, Bark.m); cv.Rect(23, 42, 4, 3, Hx("e1c549"));
                    return Done(cv);
                case "raft":
                    cv = new PixelCanvas(28, 24);
                    for (int i = 0; i < 6; i++) { cv.Rect(1 + i * 4 + (i % 2), 3, 4, 16, i % 2 == 0 ? Wood.m : Wood.s); cv.Rect(1 + i * 4, 17, 3, 2, Wood.l); }
                    cv.Rect(0, 6, 28, 2, Bark.s); cv.Rect(0, 14, 28, 2, Bark.s);
                    return Done(cv);
                case "obelisk":
                    cv = new PixelCanvas(28, 58);
                    Box(cv, 4, 1, 20, 6, 2, StoneDark);
                    Shard(cv, 14, 8, 12, 48, StoneDark);
                    cv.Rect(12, 24, 4, 4, Hx("c79bd6")); cv.Rect(13, 34, 2, 6, Hx("c79bd6"));
                    return Done(cv);
                case "wall":
                    cv = new PixelCanvas(36, 28);
                    Bricks(cv, 1, 1, 34, 20, Stone, 8, 4);
                    for (int x = 1; x < 35; x += 8) cv.Rect(x, 21, 5, 4, Stone.m);
                    cv.Rect(1, 21, 34, 1, Stone.l);
                    cv.Rect(26, 3, 8, 6, Leaf.m);
                    return Done(cv);
                case "banner":
                    cv = new PixelCanvas(22, 44);
                    cv.Rect(3, 1, 2, 42, Bark.s); cv.Rect(2, 41, 4, 2, Gold.m);
                    cv.Rect(5, 18, 14, 22, Hx("2986b8")); cv.Rect(15, 18, 4, 22, Hx("1f4f7a"));
                    for (int k = 0; k < 4; k++) cv.Rect(5 + k, 18 - k, 14 - 2 * k, 1, default);
                    cv.Rect(9, 28, 5, 5, Gold.m);
                    return Done(cv);
                case "campfire":
                    cv = new PixelCanvas(20, 20);
                    Thick(cv, 3, 3, 17, 6, 3, Bark.m); Thick(cv, 3, 6, 17, 3, 3, Bark.s);
                    Shard(cv, 10, 5, 10, 13, new Tone("f46616", "cb3416", "e1c549"));
                    Shard(cv, 10, 6, 5, 8, new Tone("e1c549", "f46616", "f2efe6"));
                    return Done(cv);
                case "chest":
                case "chest_open":
                {
                    bool open = id == "chest_open";
                    cv = new PixelCanvas(20, open ? 24 : 18);
                    Box(cv, 1, 1, 18, 9, 0, Wood);
                    cv.Rect(1, 6, 18, 1, Gold.s);
                    if (open) { cv.Rect(1, 10, 18, 3, Bark.s); cv.Rect(1, 13, 18, 9, Wood.s); cv.Rect(2, 14, 16, 7, Wood.m); cv.Rect(1, 10, 18, 2, Hx("e1c549")); }
                    else { cv.Rect(1, 10, 18, 6, Wood.m); cv.Rect(1, 15, 18, 1, Wood.l); cv.Rect(8, 7, 4, 5, Gold.m); cv.Rect(9, 8, 2, 2, Gold.s); }
                    cv.Rect(3, 1, 2, open ? 9 : 15, Gold.s); cv.Rect(15, 1, 2, open ? 9 : 15, Gold.s);
                    return Done(cv);
                }
                case "grave":
                    cv = new PixelCanvas(18, 20);
                    cv.Rect(3, 1, 12, 14, Stone.m); cv.Ellipse(9, 14, 6, 4, Stone.m); cv.Rect(12, 1, 3, 15, Stone.s);
                    cv.Rect(8, 6, 2, 7, Stone.s); cv.Rect(6, 10, 6, 2, Stone.s); cv.Rect(1, 1, 16, 2, Leaf.m);
                    return Done(cv);
                case "statue":
                    cv = new PixelCanvas(26, 50);
                    Box(cv, 2, 1, 22, 10, 2, Stone);
                    cv.Ellipse(13, 22, 7, 9, Stone.s); cv.Ellipse(12, 23, 6, 8, Stone.m);              // the rabbit hero in stone
                    cv.Ellipse(13, 34, 7, 6, Stone.s); cv.Ellipse(12, 35, 6, 5, Stone.m);
                    cv.Rect(9, 39, 3, 9, Stone.m); cv.Rect(14, 39, 3, 9, Stone.s);
                    cv.Rect(14, 34, 1, 2, Stone.s); cv.Rect(17, 34, 1, 2, Stone.s);
                    Thick(cv, 19, 18, 23, 36, 2, Stone.l);
                    return Done(cv);
                case "shrine":
                    cv = new PixelCanvas(28, 38);
                    Box(cv, 1, 1, 26, 5, 2, Stone);
                    cv.Rect(4, 8, 3, 20, Stone.m); cv.Rect(21, 8, 3, 20, Stone.s);
                    cv.Rect(1, 28, 26, 3, Cloth.s); cv.Rect(0, 31, 28, 3, Cloth.m); cv.Rect(3, 34, 22, 2, Cloth.s);
                    cv.Ellipse(14, 16, 4, 4, Hx("c79bd6")); cv.Ellipse(13, 17, 2, 2, Hx("f2efe6"));
                    return Done(cv);
                case "pillar":
                case "pillar_broken":
                {
                    bool broken = id == "pillar_broken";
                    int h = broken ? 18 : 34;
                    cv = new PixelCanvas(18, h + 4);
                    Box(cv, 1, 1, 16, 4, 1, Stone);
                    cv.Rect(3, 5, 12, h - 6, Stone.m); cv.Rect(12, 5, 3, h - 6, Stone.s);
                    for (int x = 5; x < 12; x += 3) cv.Rect(x, 6, 1, h - 8, Stone.s);
                    if (broken) { for (int x = 3; x < 15; x++) cv.Rect(x, h - 2, 1, (x * 7) % 4, Stone.m); cv.Rect(14, 1, 3, 3, Stone.m); }
                    else Box(cv, 1, h - 2, 16, 2, 1, Stone);
                    return Done(cv);
                }
                case "crate":
                    cv = new PixelCanvas(16, 18);
                    Box(cv, 1, 1, 14, 12, 3, Wood);
                    cv.Line(2, 2, 13, 11, Bark.s); cv.Rect(1, 6, 14, 1, Bark.s);
                    return Done(cv);
                case "barrel":
                    cv = new PixelCanvas(14, 18);
                    cv.Rect(2, 1, 10, 14, Wood.m); cv.Rect(1, 3, 12, 10, Wood.m); cv.Rect(9, 1, 3, 14, Wood.s);
                    cv.Rect(1, 4, 12, 1, StoneDark.m); cv.Rect(1, 11, 12, 1, StoneDark.m); cv.Ellipse(7, 15, 5, 1.5f, Wood.l);
                    return Done(cv);
                case "altar":
                case "altar_empty":
                {
                    bool full = id == "altar";
                    cv = new PixelCanvas(32, full ? 42 : 34);
                    Box(cv, 2, 1, 28, 6, 2, StoneDark);
                    Box(cv, 6, 9, 20, 14, 3, StoneDark);
                    cv.Rect(10, 13, 12, 2, Hx("8a5fa8")); cv.Rect(10, 18, 12, 1, Hx("8a5fa8"));
                    if (full) { Shard(cv, 16, 26, 8, 14, Crystal); }
                    return Done(cv);
                }
                case "anvil":
                    cv = new PixelCanvas(20, 22);
                    Box(cv, 5, 1, 10, 9, 0, Bark);
                    cv.Rect(7, 10, 6, 3, StoneDark.s);
                    cv.Rect(2, 13, 16, 5, StoneDark.m); cv.Rect(0, 16, 4, 2, StoneDark.m); cv.Rect(2, 17, 16, 1, StoneDark.l); cv.Rect(14, 13, 4, 5, StoneDark.s);
                    return Done(cv);
                case "stall":
                    cv = new PixelCanvas(42, 48);
                    cv.Rect(3, 1, 3, 40, Bark.s); cv.Rect(36, 1, 3, 40, Bark.s);
                    Box(cv, 2, 1, 38, 14, 3, Wood);
                    for (int i = 0; i < 6; i++) cv.Circle(7 + i * 5.5f, 19, 2.4f, i % 2 == 0 ? Hx("cb3416") : Hx("e1c549"));
                    for (int k = 0; k < 9; k++)
                        for (int x = 0; x < 42; x++)
                            cv.Set(x, 36 + k, (x / 6) % 2 == 0 ? (k < 2 ? Cloth.s : Cloth.m) : (k < 2 ? Snow.s : Snow.m));
                    for (int x = 0; x < 42; x += 6) cv.Ellipse(x + 3, 36, 3, 1.5f, (x / 6) % 2 == 0 ? Cloth.s : Snow.s);
                    return Done(cv);
                case "gate":
                    cv = new PixelCanvas(40, 40);
                    Bricks(cv, 1, 1, 8, 34, Stone, 4, 4); Bricks(cv, 31, 1, 8, 34, Stone, 4, 4);
                    cv.Rect(1, 30, 38, 6, Stone.m); cv.Rect(1, 30, 38, 1, Stone.s); cv.Rect(1, 35, 38, 2, Stone.l);
                    for (int x = 1; x < 39; x += 6) cv.Rect(x, 37, 4, 2, Stone.m);
                    return Done(cv);
                case "cage":
                    cv = new PixelCanvas(24, 30);
                    Box(cv, 1, 1, 22, 3, 1, StoneDark);
                    for (int x = 2; x < 23; x += 4) cv.Rect(x, 4, 1, 21, StoneDark.l);
                    cv.Ellipse(12, 25, 11, 4, StoneDark.m); cv.Rect(11, 28, 2, 2, StoneDark.s);
                    return Done(cv);
                case "warpstone":
                    cv = new PixelCanvas(18, 32);
                    Box(cv, 2, 1, 14, 4, 1, StoneDark);
                    Shard(cv, 9, 5, 12, 26, StoneDark);
                    cv.Rect(8, 10, 2, 12, Hx("6cc0dc")); cv.Rect(7, 15, 4, 2, Hx("c8ecef"));
                    return Done(cv);
            }
            cv = new PixelCanvas(8, 8);
            Box(cv, 1, 1, 6, 5, 1, Stone);
            return Done(cv);
        }
    }
}
