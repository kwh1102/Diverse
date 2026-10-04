using UnityEngine;

namespace Diverse
{
    public enum Ground : byte { Grass, DarkGrass, Path, Sand, Snow, Swamp, Ash, Plaza, Water, DeepWater, Wood }

    public enum Biome : byte { Meadow, Forest, Dunes, Tundra, Marsh, Ashland }

    public static partial class Art
    {
        // ───────────────────────────── 지면 ─────────────────────────────

        public static void GroundColors(Ground g, out Color32 a, out Color32 b, out Color32 c)
        {
            switch (g)
            {
                case Ground.Grass: a = Pal.Hex("7cc56b"); b = Pal.Hex("6ab45c"); c = Pal.Hex("93d67a"); break;
                case Ground.DarkGrass: a = Pal.Hex("4f9a58"); b = Pal.Hex("43874c"); c = Pal.Hex("5fae64"); break;
                case Ground.Path: a = Pal.Hex("d8b47e"); b = Pal.Hex("c9a26c"); c = Pal.Hex("e5c793"); break;
                case Ground.Sand: a = Pal.Hex("f0d79a"); b = Pal.Hex("e3c685"); c = Pal.Hex("f8e6b4"); break;
                case Ground.Snow: a = Pal.Hex("eaf2fb"); b = Pal.Hex("d5e2f1"); c = Pal.Hex("ffffff"); break;
                case Ground.Swamp: a = Pal.Hex("6f7f5a"); b = Pal.Hex("5d6c4a"); c = Pal.Hex("80916a"); break;
                case Ground.Ash: a = Pal.Hex("6e6372"); b = Pal.Hex("5d5361"); c = Pal.Hex("80758a"); break;
                case Ground.Plaza: a = Pal.Hex("cbbfb0"); b = Pal.Hex("b5a898"); c = Pal.Hex("ddd2c4"); break;
                case Ground.Wood: a = Pal.Hex("b07f55"); b = Pal.Hex("946644"); c = Pal.Hex("c4936a"); break;
                case Ground.DeepWater: a = Pal.Hex("3f78c9"); b = Pal.Hex("3567b0"); c = Pal.Hex("5a92dc"); break;
                default: a = Pal.Hex("5aa0e0"); b = Pal.Hex("4b8fd1"); c = Pal.Hex("8cc6f2"); break;
            }
        }

        /// <summary>
        /// 청크 하나의 지면을 텍스처 한 장으로 굽는다. 타일 경계에 디더링/테두리를 넣어 도트 느낌을 살린다.
        /// </summary>
        public static Texture2D BakeGround(Ground[,] tiles, int size, int wx0, int wy0, int seed)
        {
            int T = PPU;
            var px = new Color32[size * T * size * T];
            int W = size * T;
            for (int ty = 0; ty < size; ty++)
                for (int tx = 0; tx < size; tx++)
                {
                    var g = tiles[tx + 1, ty + 1];
                    GroundColors(g, out var a, out var b, out var c);
                    bool water = g == Ground.Water || g == Ground.DeepWater;
                    for (int y = 0; y < T; y++)
                        for (int x = 0; x < T; x++)
                        {
                            int gx = (wx0 + tx) * T + x, gy = (wy0 + ty) * T + y;
                            float n = Hash.F(gx, gy, seed);
                            Color32 col = a;
                            if (n < 0.18f) col = b;
                            else if (n > 0.94f) col = c;

                            // 이웃 타일과의 경계 처리
                            Ground l = tiles[tx, ty + 1], r = tiles[tx + 2, ty + 1], d = tiles[tx + 1, ty], u = tiles[tx + 1, ty + 2];
                            if (!water)
                            {
                                if (x == 0 && l != g && Pri(l) > Pri(g)) col = Mix(col, l, gx, gy, seed);
                                if (x == T - 1 && r != g && Pri(r) > Pri(g)) col = Mix(col, r, gx, gy, seed);
                                if (y == 0 && d != g && Pri(d) > Pri(g)) col = Mix(col, d, gx, gy, seed);
                                if (y == T - 1 && u != g && Pri(u) > Pri(g)) col = Mix(col, u, gx, gy, seed);
                            }
                            else
                            {
                                // 물가: 위쪽 땅과 맞닿으면 거품/모래 테두리, 잔물결
                                bool landUp = u != Ground.Water && u != Ground.DeepWater;
                                bool landL = l != Ground.Water && l != Ground.DeepWater;
                                bool landR = r != Ground.Water && r != Ground.DeepWater;
                                bool landD = d != Ground.Water && d != Ground.DeepWater;
                                if (landUp && y >= T - 3) col = y == T - 1 ? Pal.Hex("3b2f3a") : Pal.Hex("2f5fa8");
                                else if ((landL && x == 0) || (landR && x == T - 1) || (landD && y == 0)) col = Pal.Hex("bfe6ff");
                                else if (((gx / 3 + gy * 2) % 13 == 0) && n > 0.5f) col = c;
                            }

                            // 풀 지면에 작은 풀잎 무늬
                            if ((g == Ground.Grass || g == Ground.DarkGrass) && Hash.F(gx / 2, gy / 2, seed + 77) > 0.985f && y > 1)
                            {
                                col = b;
                                px[(ty * T + y - 1) * W + tx * T + x] = b;
                            }
                            px[(ty * T + y) * W + tx * T + x] = col;
                        }
                }
            var tex = new Texture2D(W, W, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        static int Pri(Ground g) => g switch
        {
            Ground.Water or Ground.DeepWater => -1,
            Ground.Path => 6, Ground.Plaza => 7, Ground.Wood => 8,
            Ground.Snow => 5, Ground.Sand => 4, Ground.Ash => 3, Ground.Swamp => 2, Ground.DarkGrass => 1,
            _ => 0,
        };

        static Color32 Mix(Color32 baseCol, Ground other, int gx, int gy, int seed)
        {
            GroundColors(other, out var a, out _, out _);
            return Hash.F(gx, gy, seed + 5) > 0.5f ? a : baseCol;
        }

        // ───────────────────────────── 자연물 ─────────────────────────────

        public static Sprite Tree(Biome b, int variant) => Get($"tree_{b}_{variant}", () =>
        {
            switch (b)
            {
                case Biome.Tundra: return Pine(Pal.Hex("4f8a7a"), Pal.Hex("335f56"), true, variant);
                case Biome.Forest: return variant % 2 == 0 ? Pine(Pal.Hex("3f8a4f"), Pal.Hex("2a6138"), false, variant) : RoundTree(Pal.Hex("4c9a52"), Pal.Hex("33703b"), Pal.Hex("6cbf63"), variant);
                case Biome.Dunes: return Cactus(variant);
                case Biome.Marsh: return RoundTree(Pal.Hex("6a7f4a"), Pal.Hex("4a5a33"), Pal.Hex("8a9f5e"), variant, true);
                case Biome.Ashland: return DeadTree(variant);
                default:
                    return variant % 3 == 2
                        ? RoundTree(Pal.Hex("f2a7c3"), Pal.Hex("d07a9e"), Pal.Hex("ffd1e1"), variant)
                        : RoundTree(Pal.Hex("5fb85a"), Pal.Hex("3f8a43"), Pal.Hex("88d670"), variant);
            }
        });

        static Sprite RoundTree(Color32 leaf, Color32 shade, Color32 hi, int v, bool droopy = false)
        {
            var cv = new PixelCanvas(34, 44);
            var rng = new Rng((uint)(v * 7919 + 13));
            cv.Rect(15, 0, 5, 14, Pal.Wood);
            cv.Rect(15, 0, 2, 14, Pal.WoodDark);
            cv.Rect(13, 0, 9, 2, Pal.Wood);
            float cy = 26;
            cv.Ellipse(17, cy, 15, 12, shade);
            for (int i = 0; i < 6; i++)
                cv.Circle(17 + rng.Range(-9f, 9f), cy + rng.Range(-4f, 7f), rng.Range(5f, 8f), leaf);
            for (int i = 0; i < 5; i++)
                cv.Circle(13 + rng.Range(-6f, 6f), cy + rng.Range(2f, 8f), rng.Range(2f, 3.5f), hi);
            if (droopy)
                for (int i = 0; i < 6; i++) cv.Rect(6 + i * 4, (int)cy - 12 - (i % 2) * 2, 1, 5, shade);
            cv.InnerShade(leaf, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(17, 1));
        }

        static Sprite Pine(Color32 leaf, Color32 shade, bool snow, int v)
        {
            var cv = new PixelCanvas(28, 46);
            cv.Rect(12, 0, 4, 8, Pal.WoodDark);
            for (int layer = 0; layer < 4; layer++)
            {
                int baseY = 6 + layer * 9;
                int half = 13 - layer * 3;
                for (int y = 0; y < 12; y++)
                {
                    int w = Mathf.Max(1, half - y);
                    cv.Rect(14 - w, baseY + y, w * 2, 1, y < 2 ? shade : leaf);
                    if (snow && y >= 9 - layer) cv.Rect(14 - w, baseY + y, w * 2, 1, Pal.White);
                }
            }
            cv.InnerShade(leaf, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(14, 1));
        }

        static Sprite Cactus(int v)
        {
            var cv = new PixelCanvas(20, 30);
            var g = Pal.Hex("5fae64"); var s = Pal.Hex("3f8a4f");
            cv.Rect(7, 0, 6, 26, g);
            cv.Ellipse(10, 26, 3, 2, g);
            cv.Rect(2, 10, 5, 3, g); cv.Rect(2, 10, 3, 9, g);
            if (v % 2 == 0) { cv.Rect(13, 14, 5, 3, g); cv.Rect(15, 14, 3, 8, g); }
            for (int y = 2; y < 25; y += 4) cv.Set(9, y, Pal.Hex("d7f2a0"));
            if (v % 3 == 0) cv.Circle(10, 28, 2, Pal.Hex("ff7fa8"));
            cv.InnerShade(g, s, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(10, 1));
        }

        static Sprite DeadTree(int v)
        {
            var cv = new PixelCanvas(28, 36);
            var w = Pal.Hex("5a4a52"); var s = Pal.Hex("3d3239");
            cv.ThickLine(new Vector2(14, 0), new Vector2(14, 22), 2.2f, w);
            cv.ThickLine(new Vector2(14, 14), new Vector2(5, 26), 1.3f, w);
            cv.ThickLine(new Vector2(14, 18), new Vector2(23, 30), 1.3f, w);
            cv.ThickLine(new Vector2(8, 22), new Vector2(4, 32), 0.8f, w);
            if (v % 2 == 0) cv.Circle(20, 27, 1.5f, Pal.Hex("ff8a3d"));
            cv.InnerShade(w, s, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(14, 1));
        }

        public static Sprite Rock(Biome b, int v) => Get($"rock_{b}_{v}", () =>
        {
            var cv = new PixelCanvas(20, 16);
            Color32 a = b == Biome.Tundra ? Pal.Hex("b8c8dc") : b == Biome.Dunes ? Pal.Hex("d9a873") : b == Biome.Ashland ? Pal.Hex("5a5060") : Pal.Hex("a7a3b3");
            Color32 s = Pal.Mul(a, 0.72f);
            var rng = new Rng((uint)(v * 31 + 7));
            cv.Ellipse(10, 6, 8.5f, 5.5f, a);
            cv.Ellipse(8 + rng.Range(-2f, 2f), 9, 5, 4, a);
            cv.Ellipse(7, 9, 2, 1.4f, Pal.Mul(a, 1.18f));
            if (b == Biome.Tundra) cv.Rect(4, 11, 9, 2, Pal.White);
            if (b == Biome.Meadow && v % 2 == 0) { cv.Set(14, 9, Pal.Hex("6fbf5a")); cv.Set(15, 8, Pal.Hex("6fbf5a")); }
            cv.InnerShade(a, s, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(10, 2));
        });

        public static Sprite Bush(Biome b, int v) => Get($"bush_{b}_{v}", () =>
        {
            var cv = new PixelCanvas(18, 12);
            Color32 a = b == Biome.Marsh ? Pal.Hex("7a8f52") : b == Biome.Forest ? Pal.Hex("3f8a4f") : Pal.Hex("5fb85a");
            Color32 s = Pal.Mul(a, 0.72f);
            cv.Ellipse(9, 5, 8, 4.5f, a);
            cv.Circle(5, 7, 3.5f, a); cv.Circle(12, 7.5f, 3.8f, a);
            if (v % 2 == 0) { cv.Set(6, 8, Pal.Hex("ff6b8a")); cv.Set(12, 9, Pal.Hex("ff6b8a")); cv.Set(9, 6, Pal.Hex("ff6b8a")); }
            cv.InnerShade(a, s, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(9, 1));
        });

        public static Sprite Flower(int v) => Get($"flower_{v}", () =>
        {
            var cv = new PixelCanvas(7, 7);
            Color32[] cols = { Pal.Hex("ff7fa8"), Pal.Hex("fff36b"), Pal.Hex("ffffff"), Pal.Hex("8fb8ff"), Pal.Hex("ffb36b") };
            var c = cols[v % cols.Length];
            cv.Set(3, 0, Pal.Hex("3f8a43")); cv.Set(3, 1, Pal.Hex("3f8a43")); cv.Set(3, 2, Pal.Hex("3f8a43"));
            cv.Set(2, 4, c); cv.Set(4, 4, c); cv.Set(3, 5, c); cv.Set(3, 3, c); cv.Set(3, 4, Pal.Hex("ffe08a"));
            return cv.ToSprite(new Vector2(3.5f, 0));
        });

        public static Sprite Tuft(Biome b, int v) => Get($"tuft_{b}_{v}", () =>
        {
            var cv = new PixelCanvas(9, 6);
            Color32 g = b switch
            {
                Biome.Tundra => Pal.Hex("cfe0f0"), Biome.Dunes => Pal.Hex("c9a26c"), Biome.Ashland => Pal.Hex("80758a"),
                Biome.Marsh => Pal.Hex("80916a"), Biome.Forest => Pal.Hex("3f8a4f"), _ => Pal.Hex("5aa850"),
            };
            cv.Line(1, 0, 0, 3, g); cv.Line(3, 0, 3, 5, g); cv.Line(5, 0, 6, 4, g); if (v % 2 == 0) cv.Line(7, 0, 8, 2, g);
            return cv.ToSprite(new Vector2(4.5f, 0));
        });

        // ───────────────────────────── 건물 / 구조물 ─────────────────────────────

        public static Sprite House(int v, Color32 roof) => Get($"house_{v}_{roof}", () =>
        {
            int W = 64, H = 64;
            var cv = new PixelCanvas(W, H);
            var wall = v % 2 == 0 ? Pal.Hex("f2e3c6") : Pal.Hex("e8d2b0");
            var wallS = Pal.Mul(wall, 0.82f);
            var roofS = Pal.Mul(roof, 0.7f);
            // 벽
            cv.Rect(6, 0, 52, 30, wall);
            cv.Rect(6, 0, 52, 3, wallS);
            for (int x = 6; x < 58; x += 13) cv.Rect(x, 0, 2, 30, Pal.Wood);
            cv.Rect(6, 28, 52, 2, Pal.Wood);
            // 문
            cv.Rect(27, 0, 10, 16, Pal.WoodDark);
            cv.Rect(28, 0, 8, 15, Pal.Wood);
            cv.Set(34, 7, Pal.Gold);
            cv.Ellipse(32, 15, 5, 2, Pal.WoodDark);
            // 창문
            for (int k = 0; k < 2; k++)
            {
                int wx = k == 0 ? 12 : 44;
                cv.Rect(wx, 12, 9, 9, Pal.WoodDark);
                cv.Rect(wx + 1, 13, 7, 7, Pal.Hex("ffe9a8"));
                cv.Rect(wx + 4, 13, 1, 7, Pal.WoodDark); cv.Rect(wx + 1, 16, 7, 1, Pal.WoodDark);
                cv.Rect(wx - 1, 11, 11, 2, Pal.Wood);
                cv.Rect(wx, 9, 9, 2, Pal.Hex("6fbf5a"));
                cv.Set(wx + 2, 10, Pal.Hex("ff7fa8")); cv.Set(wx + 6, 10, Pal.Hex("fff36b"));
            }
            // 지붕
            for (int y = 0; y < 30; y++)
            {
                int inset = y * 26 / 30;
                cv.Rect(1 + inset, 29 + y, 62 - inset * 2, 1, (y % 5 == 0) ? roofS : roof);
            }
            cv.Rect(0, 28, 64, 2, roofS);
            // 굴뚝
            cv.Rect(46, 44, 6, 12, Pal.Hex("a7a3b3")); cv.Rect(45, 55, 8, 2, Pal.Hex("8d899a"));
            cv.InnerShade(roof, roofS, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(32, 1));
        });

        public static Sprite Tower() => Get("tower", () =>
        {
            int W = 72, H = 200;
            var cv = new PixelCanvas(W, H);
            var stone = Pal.Hex("b3acc9"); var stoneS = Pal.Hex("7f789a"); var glow = Pal.Hex("c9b8ff");
            for (int y = 0; y < 170; y++)
            {
                int half = 30 - y / 9;
                cv.Rect(36 - half, y, half * 2, 1, stone);
                if (y % 12 == 0) cv.Rect(36 - half, y, half * 2, 1, stoneS);
            }
            for (int y = 6; y < 160; y += 12)
                for (int x = 0; x < W; x += 9)
                    cv.SetIfSolid(x + (y / 12 % 2) * 4, y + 5, stoneS);
            // 창 (층마다 빛)
            for (int f = 0; f < 6; f++)
            {
                int y = 24 + f * 24;
                cv.Rect(33, y, 6, 10, Pal.Hex("2a1d3f"));
                cv.Rect(34, y + 1, 4, 8, glow);
            }
            // 문
            cv.Rect(28, 0, 16, 22, Pal.Hex("2a1d3f"));
            cv.Ellipse(36, 22, 8, 5, Pal.Hex("2a1d3f"));
            cv.Rect(30, 0, 12, 20, Pal.Hex("4a3a6a"));
            // 꼭대기
            cv.Ellipse(36, 176, 14, 8, stoneS);
            cv.Circle(36, 186, 8, glow);
            cv.Circle(36, 186, 4, Pal.White);
            cv.InnerShade(stone, stoneS, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(36, 1));
        });

        public static Sprite Prop(string id) => Get($"prop_{id}", () => DrawProp(id));

        static Sprite DrawProp(string id)
        {
            switch (id)
            {
                case "board":
                {
                    var cv = new PixelCanvas(26, 26);
                    cv.Rect(3, 0, 3, 22, Pal.WoodDark); cv.Rect(20, 0, 3, 22, Pal.WoodDark);
                    cv.Rect(1, 8, 24, 15, Pal.Wood); cv.Rect(1, 8, 24, 2, Pal.WoodDark);
                    cv.Rect(4, 12, 7, 8, Pal.Hex("fff8ef")); cv.Rect(13, 11, 8, 6, Pal.Hex("f4e2b8")); cv.Rect(14, 18, 6, 3, Pal.Hex("fff8ef"));
                    cv.Set(7, 19, Pal.Blood); cv.Set(16, 16, Pal.Blood);
                    cv.Rect(0, 22, 26, 3, Pal.Hex("c23b4e"));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(13, 1));
                }
                case "well":
                {
                    var cv = new PixelCanvas(26, 30);
                    cv.Ellipse(13, 7, 11, 6, Pal.Hex("a7a3b3")); cv.Ellipse(13, 9, 8, 3.5f, Pal.Hex("3f78c9"));
                    cv.Rect(3, 6, 3, 18, Pal.Wood); cv.Rect(20, 6, 3, 18, Pal.Wood);
                    for (int y = 0; y < 6; y++) cv.Rect(1 + y, 22 + y, 24 - y * 2, 1, Pal.Hex("c23b4e"));
                    cv.Rect(12, 12, 2, 10, Pal.Hex("d9c4a0")); cv.Rect(10, 11, 6, 3, Pal.Wood);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(13, 1));
                }
                case "lamp":
                {
                    var cv = new PixelCanvas(10, 30);
                    cv.Rect(4, 0, 2, 24, Pal.Hex("3d3d4f")); cv.Rect(2, 0, 6, 2, Pal.Hex("3d3d4f"));
                    cv.Rect(2, 22, 6, 6, Pal.Hex("3d3d4f")); cv.Rect(3, 23, 4, 4, Pal.Hex("ffe08a")); cv.Rect(1, 28, 8, 1, Pal.Hex("3d3d4f"));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(5, 1));
                }
                case "fence":
                {
                    var cv = new PixelCanvas(18, 14);
                    cv.Rect(1, 0, 3, 12, Pal.Wood); cv.Rect(14, 0, 3, 12, Pal.Wood);
                    cv.Rect(0, 4, 18, 2, Pal.Wood); cv.Rect(0, 8, 18, 2, Pal.Wood);
                    cv.Rect(0, 4, 18, 1, Pal.WoodDark);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(9, 1));
                }
                case "tent":
                {
                    var cv = new PixelCanvas(40, 30);
                    var c = Pal.Hex("c2603e"); var s = Pal.Hex("8a3f27");
                    for (int y = 0; y < 26; y++) { int half = 19 - y * 19 / 26; cv.Rect(20 - half, y, half * 2, 1, y % 6 == 0 ? s : c); }
                    for (int y = 0; y < 14; y++) { int half = 6 - y * 6 / 14; cv.Rect(20 - half, y, half * 2, 1, Pal.Hex("2a1d2f")); }
                    cv.Line(20, 26, 20, 29, Pal.WoodDark); cv.Rect(21, 27, 5, 2, Pal.Hex("f4c95d"));
                    cv.InnerShade(c, s, 1, -1);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(20, 1));
                }
                case "campfire":
                {
                    var cv = new PixelCanvas(16, 10);
                    cv.Line(2, 1, 13, 4, Pal.WoodDark); cv.Line(2, 4, 13, 1, Pal.Wood);
                    for (int i = 0; i < 6; i++) cv.Set(2 + i * 2, 0, Pal.Hex("8d899a"));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(8, 1));
                }
                case "chest":
                case "chest_open":
                {
                    bool open = id == "chest_open";
                    var cv = new PixelCanvas(18, 16);
                    cv.Rect(1, 0, 16, 9, Pal.Wood); cv.Rect(1, 0, 16, 2, Pal.WoodDark);
                    cv.Rect(1, 4, 16, 1, Pal.Gold); cv.Rect(8, 3, 2, 3, Pal.Gold);
                    if (open) { cv.Rect(1, 9, 16, 5, Pal.WoodDark); cv.Rect(2, 8, 14, 2, Pal.Hex("2a1d2f")); }
                    else { cv.Rect(1, 9, 16, 4, Pal.Wood); cv.Ellipse(9, 13, 8, 2, Pal.Wood); cv.Rect(1, 9, 16, 1, Pal.Gold); }
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(9, 1));
                }
                case "grave":
                {
                    var cv = new PixelCanvas(16, 22);
                    var s = Pal.Hex("a7a3b3");
                    cv.Rect(3, 0, 10, 14, s); cv.Ellipse(8, 14, 5, 4, s);
                    cv.Rect(6, 7, 4, 1, Pal.SteelDark); cv.Rect(7, 4, 2, 7, Pal.SteelDark);
                    cv.Rect(1, 0, 14, 2, Pal.Hex("6fbf5a")); cv.Set(2, 2, Pal.Hex("ff7fa8"));
                    cv.InnerShade(s, Pal.Mul(s, 0.72f), 1, -1);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(8, 1));
                }
                case "statue":
                {
                    var cv = new PixelCanvas(30, 50);
                    var s = Pal.Hex("d4cfdf"); var sh = Pal.Hex("a39dba");
                    cv.Rect(3, 0, 24, 10, sh); cv.Rect(5, 10, 20, 3, s);
                    cv.Ellipse(15, 20, 6, 7, s);        // 몸
                    cv.Ellipse(15, 31, 7, 6, s);        // 머리
                    cv.Ellipse(12, 41, 2, 6, s); cv.Ellipse(18, 41, 2, 6, s);  // 귀
                    cv.ThickLine(new Vector2(21, 18), new Vector2(27, 38), 1.1f, s);  // 들어 올린 검
                    cv.Rect(6, 3, 18, 3, Pal.Gold);
                    cv.InnerShade(s, sh, 1, -1);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(15, 1));
                }
                case "shrine":
                {
                    var cv = new PixelCanvas(26, 32);
                    var s = Pal.Hex("b3acc9");
                    cv.Rect(2, 0, 22, 5, Pal.Mul(s, 0.75f)); cv.Rect(6, 5, 14, 16, s);
                    cv.Circle(13, 26, 5, Pal.Hex("8fe3ff")); cv.Circle(13, 26, 2.5f, Pal.White);
                    cv.Rect(9, 9, 8, 8, Pal.Mul(s, 0.8f)); cv.Rect(11, 11, 4, 4, Pal.Hex("8fe3ff"));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(13, 1));
                }
                case "pillar":
                case "pillar_broken":
                {
                    bool broken = id == "pillar_broken";
                    var cv = new PixelCanvas(14, 34);
                    var s = Pal.Hex("c9c2d6"); var sh = Pal.Mul(s, 0.75f);
                    int h = broken ? 16 : 28;
                    cv.Rect(1, 0, 12, 3, sh); cv.Rect(3, 3, 8, h, s);
                    for (int x = 4; x < 11; x += 3) cv.Rect(x, 3, 1, h, sh);
                    if (!broken) cv.Rect(1, h + 3, 12, 3, sh);
                    else { cv.Set(3, h + 3, s); cv.Set(4, h + 4, s); cv.Set(8, h + 3, s); }
                    cv.Rect(2, 8, 3, 2, Pal.Hex("6fbf5a"));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(7, 1));
                }
                case "crate":
                {
                    var cv = new PixelCanvas(14, 14);
                    cv.Rect(0, 0, 14, 13, Pal.Wood); cv.Rect(0, 0, 14, 2, Pal.WoodDark);
                    cv.Line(1, 1, 12, 11, Pal.WoodDark); cv.Rect(0, 11, 14, 2, Pal.WoodDark);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(7, 1));
                }
                case "barrel":
                {
                    var cv = new PixelCanvas(12, 16);
                    cv.Ellipse(6, 7, 5.5f, 7, Pal.Wood); cv.Rect(1, 3, 10, 1, Pal.SteelDark); cv.Rect(1, 11, 10, 1, Pal.SteelDark);
                    cv.Ellipse(6, 13, 4, 1.5f, Pal.WoodDark);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(6, 1));
                }
                case "altar":
                {
                    var cv = new PixelCanvas(30, 20);
                    var s = Pal.Hex("4a3a6a");
                    cv.Rect(2, 0, 26, 8, s); cv.Rect(5, 8, 20, 4, Pal.Mul(s, 1.3f));
                    cv.Circle(15, 15, 4, Pal.ShadowEl); cv.Circle(15, 15, 2, Pal.White);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(15, 1));
                }
                case "anvil":
                {
                    var cv = new PixelCanvas(18, 12);
                    cv.Rect(6, 0, 6, 5, Pal.SteelDark); cv.Rect(1, 5, 16, 4, Pal.SteelShade); cv.Rect(0, 7, 4, 2, Pal.SteelShade);
                    cv.Rect(1, 8, 16, 1, Pal.Steel);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(9, 1));
                }
                case "stall":
                {
                    var cv = new PixelCanvas(34, 30);
                    cv.Rect(2, 0, 30, 12, Pal.Wood); cv.Rect(2, 10, 30, 2, Pal.WoodDark);
                    cv.Rect(3, 0, 2, 26, Pal.WoodDark); cv.Rect(29, 0, 2, 26, Pal.WoodDark);
                    for (int x = 0; x < 34; x += 6) { cv.Rect(x, 22, 6, 6, (x / 6) % 2 == 0 ? Pal.Hex("f26d6d") : Pal.White); }
                    cv.Circle(9, 14, 2.5f, Pal.Hex("ff8a3d")); cv.Circle(15, 14, 2.5f, Pal.Hex("8bc34a")); cv.Circle(22, 14, 2.5f, Pal.Hex("f4c95d"));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(17, 1));
                }
                case "gate":
                {
                    var cv = new PixelCanvas(40, 40);
                    var s = Pal.Hex("6b5c8a");
                    cv.Rect(2, 0, 7, 34, s); cv.Rect(31, 0, 7, 34, s); cv.Rect(0, 30, 40, 8, s);
                    cv.Rect(12, 32, 16, 4, Pal.Gold);
                    cv.Rect(9, 0, 22, 30, Pal.A(Pal.ShadowEl, 140));
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(20, 1));
                }
                case "cage":
                {
                    var cv = new PixelCanvas(20, 22);
                    cv.Rect(0, 0, 20, 2, Pal.WoodDark); cv.Rect(0, 19, 20, 2, Pal.WoodDark);
                    for (int x = 1; x < 20; x += 4) cv.Rect(x, 2, 2, 17, Pal.Wood);
                    cv.Outline(Pal.Outline);
                    return cv.ToSprite(new Vector2(10, 1));
                }
            }
            var d = new PixelCanvas(8, 8); d.Rect(0, 0, 8, 8, Pal.Blood); return d.ToSpriteCentered();
        }

        /// <summary>마을 사람 (동물 NPC). kind: owl(이야기꾼), cat(상인), bear(대장장이), rabbit(주민)</summary>
        public static Sprite Npc(string kind, int frame) => Get($"npc_{kind}_{frame}", () =>
        {
            var cv = new PixelCanvas(26, 30);
            int bob = frame == 1 ? 1 : 0;
            switch (kind)
            {
                case "owl":
                {
                    var f = Pal.Hex("a07850"); var s = Pal.Hex("6e5034");
                    cv.Ellipse(13, 10 - bob, 8, 9, f);
                    cv.Ellipse(13, 9 - bob, 5, 6, Pal.Hex("e8d2b0"));
                    for (int y = 4; y < 14; y += 3) cv.Rect(10, y - bob, 6, 1, Pal.Hex("c9b08a"));
                    cv.Circle(10, 16 - bob, 3, Pal.White); cv.Circle(16, 16 - bob, 3, Pal.White);
                    cv.Circle(10, 16 - bob, 1.5f, Pal.EyeDark); cv.Circle(16, 16 - bob, 1.5f, Pal.EyeDark);
                    cv.Rect(12, 13 - bob, 2, 2, Pal.Gold);
                    cv.Rect(6, 19 - bob, 3, 3, f); cv.Rect(17, 19 - bob, 3, 3, f);
                    cv.Rect(4, 2, 18, 2, Pal.Hex("3e6fb0"));   // 책
                    cv.Ring(16, 16 - bob, 3.4f, 1, Pal.Gold);   // 외눈 안경
                    cv.InnerShade(f, s, 1, -1);
                    break;
                }
                case "cat":
                {
                    var f = Pal.Hex("f2a65a"); var s = Pal.Hex("c27a38");
                    cv.Rect(9, 0, 3, 3, s); cv.Rect(14, 0, 3, 3, s);
                    cv.Ellipse(13, 7 - bob, 5, 5, Pal.Hex("3e6fb0"));
                    cv.Ellipse(13, 16 - bob, 7, 6, f);
                    cv.Rect(7, 20 - bob, 3, 4, f); cv.Rect(16, 20 - bob, 3, 4, f);
                    cv.Set(8, 22 - bob, Pal.EarPink); cv.Set(17, 22 - bob, Pal.EarPink);
                    cv.Rect(10, 16 - bob, 2, 2, Pal.EyeDark); cv.Rect(15, 16 - bob, 2, 2, Pal.EyeDark);
                    cv.Set(13, 14 - bob, Pal.Hex("e07a8a"));
                    cv.Line(19, 3, 23, 10, f);
                    cv.Rect(5, 5 - bob, 4, 6, Pal.Hex("8a5a3c"));   // 가방
                    cv.InnerShade(f, s, 1, -1);
                    break;
                }
                case "bear":
                {
                    var f = Pal.Hex("8a5a44"); var s = Pal.Hex("5e3a2c");
                    cv.Rect(8, 0, 4, 3, s); cv.Rect(14, 0, 4, 3, s);
                    cv.Ellipse(13, 8 - bob, 8, 6, Pal.Hex("6b6b7a"));   // 앞치마
                    cv.Ellipse(13, 18 - bob, 7, 6, f);
                    cv.Circle(8, 23 - bob, 2.2f, f); cv.Circle(18, 23 - bob, 2.2f, f);
                    cv.Ellipse(13, 15 - bob, 3, 2, Pal.Hex("d9b48a"));
                    cv.Set(13, 16 - bob, Pal.EyeDark);
                    cv.Rect(10, 18 - bob, 2, 2, Pal.EyeDark); cv.Rect(15, 18 - bob, 2, 2, Pal.EyeDark);
                    cv.Line(21, 4, 23, 14, Pal.WoodDark); cv.Rect(20, 13, 6, 3, Pal.SteelShade);  // 망치
                    cv.InnerShade(f, s, 1, -1);
                    break;
                }
                default:
                {
                    int vi = 0; foreach (char ch in kind) if (char.IsDigit(ch)) vi = vi * 10 + (ch - '0');
                    var c = DB.Costumes[vi % DB.Costumes.Count];
                    return Rabbit(c, frame == 0 ? Pose.Idle0 : Pose.Idle1);
                }
            }
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(13, 1));
        });

        // ───────────────────────────── 아이템 ─────────────────────────────

        public static Sprite Item(string id) => Get($"item_{id}", () =>
        {
            var cv = new PixelCanvas(10, 10);
            switch (id)
            {
                case "gold":
                    cv.Circle(5, 5, 4, Pal.GoldDark); cv.Circle(5, 5, 3, Pal.Gold); cv.Rect(4, 3, 2, 4, Pal.GoldDark); cv.Set(3, 6, Pal.White); break;
                case "xp":
                    cv.Set(5, 9, Pal.Frost); cv.Rect(4, 7, 3, 2, Pal.Frost); cv.Rect(3, 4, 5, 3, Pal.Hex("5ac8ff")); cv.Rect(4, 2, 3, 2, Pal.Hex("3a9be0")); cv.Set(5, 1, Pal.Hex("3a9be0")); cv.Set(4, 6, Pal.White); break;
                case "heart":
                    cv.Circle(3.5f, 6, 2.5f, Pal.Blood); cv.Circle(6.5f, 6, 2.5f, Pal.Blood);
                    for (int y = 1; y <= 5; y++) { int half = y; cv.Rect(5 - half, y, half * 2, 1, Pal.Blood); }
                    cv.Set(3, 7, Pal.White); break;
                case "shard":
                    cv.Line(5, 9, 2, 4, Pal.ShadowEl); cv.Line(5, 9, 8, 4, Pal.ShadowEl); cv.Line(2, 4, 5, 0, Pal.ShadowEl); cv.Line(8, 4, 5, 0, Pal.ShadowEl);
                    cv.Rect(4, 2, 3, 6, Pal.Hex("e6dcff")); cv.Rect(3, 4, 5, 2, Pal.Hex("c9b8ff")); break;
                case "potion":
                    cv.Rect(4, 7, 2, 2, Pal.Wood); cv.Circle(5, 4, 3.4f, Pal.Blood); cv.Set(4, 5, Pal.White); break;
                default:
                    cv.Circle(5, 5, 3, Pal.Gold); break;
            }
            cv.Outline(Pal.Outline);
            return cv.ToSpriteCentered();
        });
    }
}
