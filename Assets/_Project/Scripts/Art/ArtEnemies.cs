using UnityEngine;

namespace Diverse
{
    public static partial class Art
    {
        /// <summary>적 스프라이트 2프레임(대기/이동 교차) + 피격 프레임.</summary>
        public static Sprite Enemy(string key, int frame) => Get($"enemy_{key}_{frame}", () => DrawEnemy(key, frame));

        static Sprite DrawEnemy(string key, int f)
        {
            switch (key)
            {
                case "shroom": return Shroom(f, 1f, Pal.Hex("e8584f"), Pal.Hex("b23a3a"), false);
                case "boss_shroom": return Shroom(f, 2.1f, Pal.Hex("8bc34a"), Pal.Hex("5a8a2e"), true);
                case "jelly": return Jelly(f);
                case "fox": return Fox(f, false);
                case "boss_fox": return Fox(f, true);
                case "raccoon": return Raccoon(f);
                case "wisp": return Wisp(f, Pal.Hex("b18cff"), Pal.Hex("e6dcff"));
                case "frostling": return Wisp(f, Pal.Hex("7fd6ff"), Pal.Hex("e8fbff"));
                case "boar": return Boar(f);
                case "bat": return Bat(f);
                case "boss_knight": return Knight(f);
            }
            return Shroom(f, 1, Pal.Hex("e8584f"), Pal.Hex("b23a3a"), false);
        }

        static void Eyes(PixelCanvas cv, int x, int y, int gap, bool angry, Color32 col)
        {
            cv.Rect(x, y, 2, 2, col); cv.Rect(x + gap, y, 2, 2, col);
            cv.Set(x, y + 1, Pal.White); cv.Set(x + gap, y + 1, Pal.White);
            if (angry) { cv.Line(x - 1, y + 3, x + 1, y + 2, Pal.Outline); cv.Line(x + gap + 2, y + 3, x + gap, y + 2, Pal.Outline); }
        }

        static Sprite Shroom(int f, float s, Color32 cap, Color32 capShade, bool boss)
        {
            int w = Mathf.RoundToInt(20 * s), h = Mathf.RoundToInt(20 * s);
            var cv = new PixelCanvas(w + 2, h + 2);
            float cx = (w + 2) / 2f;
            int bob = f == 1 ? 1 : 0;
            // 다리
            cv.Ellipse(cx - 3 * s, 2, 2 * s, 1.5f * s, Pal.Hex("d8c8b0"));
            cv.Ellipse(cx + 3 * s + (f == 1 ? 1 : 0), 2, 2 * s, 1.5f * s, Pal.Hex("d8c8b0"));
            // 몸통
            cv.Ellipse(cx, 6 * s - bob, 5 * s, 4.5f * s, Pal.Hex("f5ead6"));
            // 갓
            cv.Ellipse(cx, 12 * s - bob, 9 * s, 5.5f * s, cap);
            cv.Rect((int)(cx - 9 * s), (int)(9 * s - bob), (int)(18 * s), (int)(1 * s + 1), capShade);
            // 점
            cv.Circle(cx - 4 * s, 13 * s - bob, 1.6f * s, Pal.White);
            cv.Circle(cx + 3 * s, 15 * s - bob, 1.3f * s, Pal.White);
            cv.Circle(cx + 6 * s, 11.5f * s - bob, 1f * s, Pal.White);
            Eyes(cv, (int)(cx - 3 * s), (int)(5 * s - bob), (int)(4 * s), boss || f == 2, Pal.EyeDark);
            if (boss)
            {
                cv.Rect((int)(cx - 4), (int)(17 * s - bob), 9, 3, Pal.Gold);
                cv.Set((int)cx - 4, (int)(17 * s - bob) + 3, Pal.Gold); cv.Set((int)cx, (int)(17 * s - bob) + 3, Pal.Gold); cv.Set((int)cx + 4, (int)(17 * s - bob) + 3, Pal.Gold);
                cv.Line((int)(cx - 7 * s), (int)(4 * s), (int)(cx - 10 * s), (int)(1 * s), Pal.Hex("6b4a2e"));
                cv.Line((int)(cx + 7 * s), (int)(4 * s), (int)(cx + 10 * s), (int)(1 * s), Pal.Hex("6b4a2e"));
            }
            cv.InnerShade(cap, capShade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(cx, 1));
        }

        static Sprite Jelly(int f)
        {
            var cv = new PixelCanvas(20, 18);
            var body = Pal.Hex("6fe0b8"); var shade = Pal.Hex("3fae8a");
            float sq = f == 1 ? 1.2f : (f == 2 ? 0.85f : 1f);
            cv.Ellipse(10, 6 / sq, 8 * sq, 6 / sq, body);
            cv.Rect(2, 1, 16, 2, body);
            cv.Ellipse(7, 9 / sq, 2f, 1.4f, Pal.A(Pal.White, 200));
            Eyes(cv, 7, (int)(5 / sq), 5, f == 2, Pal.EyeDark);
            cv.InnerShade(body, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(10, 1));
        }

        static Sprite Fox(int f, bool boss)
        {
            float s = boss ? 1.7f : 1f;
            int W = Mathf.RoundToInt(28 * s), H = Mathf.RoundToInt(28 * s);
            var cv = new PixelCanvas(W, H);
            var fur = boss ? Pal.Hex("e8553a") : Pal.Hex("f08a3e");
            var shade = boss ? Pal.Hex("a83322") : Pal.Hex("b85f26");
            var cloth = boss ? Pal.Hex("2d2a3a") : Pal.Hex("5c4a7a");
            float cx = W / 2f;
            int lf = f == 1 ? 2 : 0;
            // 꼬리
            cv.ThickLine(new Vector2(cx - 5 * s, 6 * s), new Vector2(cx - 10 * s, 12 * s), 2.6f * s, fur);
            cv.Circle(cx - 10.5f * s, 13 * s, 2.2f * s, Pal.White);
            if (boss) { cv.Circle(cx - 11 * s, 15 * s, 2.4f * s, Pal.Fire); cv.Circle(cx - 11 * s, 16 * s, 1.2f * s, Pal.Lightning); }
            // 다리
            cv.Rect((int)(cx - 3 * s) + lf, 0, (int)(2 * s), (int)(3 * s), shade);
            cv.Rect((int)(cx + 1 * s) - lf, 0, (int)(2 * s), (int)(3 * s), shade);
            // 몸 (조끼)
            cv.Ellipse(cx, 7 * s, 4.5f * s, 4.5f * s, cloth);
            cv.Ellipse(cx + 1 * s, 7 * s, 2 * s, 3 * s, Pal.White);
            cv.Rect((int)(cx - 4 * s), (int)(4 * s), (int)(9 * s), 1, Pal.Gold);
            // 머리
            float hy = 15 * s;
            cv.Ellipse(cx + 1 * s, hy, 6 * s, 5 * s, fur);
            // 귀
            for (int i = 0; i < 5 * s; i++)
            {
                cv.Rect((int)(cx - 4 * s + i * 0.3f), (int)(hy + 3 * s + i), (int)Mathf.Max(1, 3 * s - i * 0.6f), 1, fur);
                cv.Rect((int)(cx + 3 * s + i * 0.3f), (int)(hy + 3 * s + i), (int)Mathf.Max(1, 3 * s - i * 0.6f), 1, fur);
            }
            // 주둥이
            cv.Ellipse(cx + 5 * s, hy - 2 * s, 3 * s, 2 * s, Pal.White);
            cv.Set((int)(cx + 7.5f * s), (int)(hy - 1.5f * s), Pal.EyeDark);
            // 두건/안대
            cv.Rect((int)(cx - 5 * s), (int)(hy + 1 * s), (int)(11 * s), (int)(1.5f * s), boss ? Pal.Blood : Pal.Hex("3e6fb0"));
            Eyes(cv, (int)(cx + 1 * s), (int)(hy - 0.5f * s), (int)(3 * s), true, boss ? Pal.Gold : Pal.EyeDark);
            // 단검/대도
            if (boss) cv.ThickLine(new Vector2(cx + 6 * s, 5 * s), new Vector2(cx + 13 * s, 13 * s), 1.1f, Pal.Steel);
            else cv.Line((int)(cx + 4 * s), (int)(5 * s), (int)(cx + 9 * s), (int)(9 * s), Pal.Steel);
            cv.InnerShade(fur, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(cx, 1));
        }

        static Sprite Raccoon(int f)
        {
            var cv = new PixelCanvas(24, 24);
            var fur = Pal.Hex("8c8a98"); var shade = Pal.Hex("5f5d6b");
            int lf = f == 1 ? 1 : 0;
            // 꼬리 줄무늬
            for (int i = 0; i < 4; i++) cv.Circle(4 + i * 0.5f, 5 + i * 2, 2.2f, i % 2 == 0 ? fur : Pal.Hex("3a3846"));
            cv.Rect(9 + lf, 0, 2, 3, shade); cv.Rect(13 - lf, 0, 2, 3, shade);
            cv.Ellipse(12, 6, 4.5f, 4, Pal.Hex("7a5a3a"));
            cv.Rect(8, 5, 9, 1, Pal.WoodDark);
            cv.Ellipse(12.5f, 13, 6, 5, fur);
            cv.Ellipse(9, 18, 1.8f, 2, fur); cv.Ellipse(16, 18, 1.8f, 2, fur);
            cv.Rect(8, 12, 10, 3, Pal.Hex("3a3846"));      // 가면
            Eyes(cv, 10, 12, 4, true, Pal.Hex("ffe08a"));
            cv.Ellipse(14, 10, 2, 1.4f, Pal.White);
            cv.Set(15, 10, Pal.EyeDark);
            // 새총
            cv.Line(17, 5, 21, 9, Pal.Wood); cv.Line(21, 9, 22, 11, Pal.Wood); cv.Line(21, 9, 20, 11, Pal.Wood);
            cv.InnerShade(fur, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(12, 1));
        }

        static Sprite Wisp(int f, Color32 col, Color32 core)
        {
            var cv = new PixelCanvas(20, 24);
            float bob = f == 1 ? 1 : 0;
            cv.Ellipse(10, 12 + bob, 7, 7, col);
            for (int i = 0; i < 3; i++)
                cv.Ellipse(5 + i * 5, 5 + bob + (i == 1 ? -1 : 0) + (f == 1 ? (i % 2) : 0), 2.2f, 3, col);
            cv.Ellipse(10, 13 + bob, 4, 4, core);
            cv.Rect(7, 12 + (int)bob, 2, 3, Pal.EyeDark); cv.Rect(11, 12 + (int)bob, 2, 3, Pal.EyeDark);
            cv.Outline(Pal.Mul(col, 0.4f));
            return cv.ToSprite(new Vector2(10, 1));
        }

        static Sprite Boar(int f)
        {
            var cv = new PixelCanvas(32, 24);
            var fur = Pal.Hex("8a5a44"); var shade = Pal.Hex("5e3a2c"); var stone = Pal.Hex("9aa0ac");
            int lf = f == 1 ? 2 : 0;
            cv.Rect(8 + lf, 0, 3, 4, shade); cv.Rect(20 - lf, 0, 3, 4, shade);
            cv.Ellipse(15, 9, 11, 7, fur);
            // 돌 갑옷
            cv.Ellipse(12, 13, 7, 4, stone); cv.Ellipse(18, 14, 5, 3, Pal.Hex("b9bfca"));
            cv.Line(9, 13, 15, 15, Pal.SteelDark);
            // 머리
            cv.Ellipse(25, 8, 5, 5, fur);
            cv.Ellipse(28.5f, 6, 2.4f, 2, Pal.Hex("e8a0a0"));
            cv.Line(27, 4, 30, 9, Pal.White); // 엄니
            Eyes(cv, 24, 9, 3, true, Pal.Hex("ff6b5a"));
            cv.InnerShade(fur, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(16, 1));
        }

        static Sprite Bat(int f)
        {
            var cv = new PixelCanvas(22, 14);
            var body = Pal.Hex("5a4a7a"); var wing = Pal.Hex("3d3058");
            int up = f == 1 ? 3 : 0;
            for (int s = -1; s <= 1; s += 2)
            {
                cv.ThickLine(new Vector2(11, 7), new Vector2(11 + s * 9, 7 + 3 - up * 1.5f), 1.6f, wing);
                cv.ThickLine(new Vector2(11 + s * 9, 7 + 3 - up * 1.5f), new Vector2(11 + s * 6, 4 + up), 1.2f, wing);
            }
            cv.Circle(11, 7, 3.6f, body);
            cv.Set(9, 11, body); cv.Set(13, 11, body);
            cv.Set(10, 7, Pal.Hex("ff6b8a")); cv.Set(12, 7, Pal.Hex("ff6b8a"));
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(11, 0));
        }

        static Sprite Knight(int f)
        {
            var cv = new PixelCanvas(40, 46);
            var armor = Pal.Hex("4a4a62"); var shade = Pal.Hex("2e2e40"); var trim = Pal.Hex("9d8cff");
            int lf = f == 1 ? 2 : 0;
            // 망토
            for (int y = 0; y < 26; y++) cv.Rect(8 - y / 6, 4 + y, 10 + y / 3, 1, Pal.Hex("3a1f4a"));
            cv.Rect(13 + lf, 0, 5, 6, shade); cv.Rect(22 - lf, 0, 5, 6, shade);
            cv.Ellipse(20, 15, 9, 10, armor);
            cv.Rect(12, 10, 17, 2, trim);
            cv.Ellipse(20, 31, 7, 7, armor);           // 투구
            cv.Rect(15, 30, 11, 2, Pal.EyeDark);
            cv.Rect(17, 30, 2, 2, trim); cv.Rect(22, 30, 2, 2, trim);
            cv.Rect(19, 37, 3, 5, trim);                // 장식
            cv.Ellipse(11, 20, 3.5f, 3.5f, armor);     // 어깨
            cv.Ellipse(29, 20, 3.5f, 3.5f, armor);
            // 대검
            cv.ThickLine(new Vector2(31, 14), new Vector2(38, 42), 1.3f, Pal.Hex("cfc6ff"));
            cv.Rect(28, 12, 8, 2, trim);
            cv.InnerShade(armor, shade, 1, -1);
            cv.Outline(Pal.Outline);
            return cv.ToSprite(new Vector2(20, 1));
        }
    }
}
