using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Monsters by the same rules as the hero (Art/ArtHero.cs): flat palette tones, two dot eyes, one dark outline.
    /// Small monsters are dot templates on a y-down grid; bosses are drawn from round shapes at the same 1-dot scale
    /// (a shade under a lighter fill). Frames: 0 idle, 1 and 4 the two steps, 2 the wind-up (squint, rise), 3 hurt (shut eyes).
    /// </summary>
    public static partial class Art
    {
        // Called every frame by every enemy: a per-key frame array so no lookup string is built per call
        static readonly Dictionary<string, Sprite[]> enemyFrames = new Dictionary<string, Sprite[]>();

        /// <summary>적 스프라이트: 0 대기, 1·4 이동(두 걸음), 2 공격, 3 피격.</summary>
        public static Sprite Enemy(string key, int frame)
        {
            if (!enemyFrames.TryGetValue(key, out var f)) enemyFrames[key] = f = new Sprite[8];
            if (frame < 0 || frame >= f.Length) return Get($"enemy_{key}_{frame}", () => DrawEnemy(key, frame));
            return f[frame] != null ? f[frame] : f[frame] = Get($"enemy_{key}_{frame}", () => DrawEnemy(key, frame));
        }

        /// <summary>Height in pixels of the hand-made enemy sprite (redrawn enemies keep it, so hitboxes still match).</summary>
        public static int EnemyHeight(string key) => Mathf.RoundToInt(DrawEnemy(key, 0).rect.height);

        static Sprite DrawEnemy(string key, int f)
        {
            switch (key)
            {
                case "shroom": return Shroom(f);
                case "boss_shroom": return BossShroom(f);
                case "jelly": return Jelly(f);
                case "fox": return Fox(f);
                case "boss_fox": return BossFox(f);
                case "raccoon": return Raccoon(f);
                case "wisp": return Wisp(f, false);
                case "frostling": return Wisp(f, true);
                case "boar": return Boar(f);
                case "bat": return Bat(f);
                case "boss_knight": return Knight(f);
                case "frog": return Frog(f);
            }
            return Shroom(f);
        }

        /// <summary>Rows the body sinks (steps, hurt) or rises (wind-up) on the y-down grid.</summary>
        static int Sink(int f) => f == 1 || f == 3 || f == 4 ? 1 : f == 2 ? -1 : 0;
        static void Face(Figure fig, int f) { if (f == 2) fig.Squint(); else if (f == 3) fig.Shut(); }

        static Sprite Finish(Figure fig, Dictionary<char, Color32> keys, int pivotX, int pivotY = 1)
        {
            var cv = fig.Paint(keys);
            cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(pivotX, pivotY));
        }

        static class Mob
        {
            public static readonly string[] Cap =
            {
                ".....ffffff.....",
                "...ffwwffffff...",
                "..fwwwffffwwff..",
                ".ffwwfffffwwfff.",
                "fffffffwwffffffs",
                "ffffffwwffffffss",
                ".ssssssssssssss.",
            };
            public static readonly string[] Stem =
            {
                ".bbbbbbbb.",
                "bbbebbebbc",
                "bbbebbebbc",
                "bbbbbbbbcc",
                ".bbbbbbcc.",
            };
            public static readonly string[] StemFoot = { "bb", "cc" };

            public static readonly string[] Jelly =
            {
                "....ffffffff....",
                "..ffffffffffff..",
                ".fllfffffffffff.",
                ".flffffffffffffs",
                "ffffffefffeffffs",
                "ffffffefffeffffs",
                "fffffffffffffffs",
                "sffffffffffffsss",
                ".ssssssssssssss.",
            };
            public static readonly string[] JellySquash =
            {
                ".....ffffffff.....",
                "..ffffffffffffff..",
                ".flffffffffffffff.",
                "fffffffefffefffffs",
                "fffffffefffefffffs",
                "ffffffffffffffffss",
                "sffffffffffffffsss",
                ".ssssssssssssssss.",
            };
            public static readonly string[] JellyTall =
            {
                "....ffffff....",
                "..ffffffffff..",
                ".fllffffffffs.",
                ".flfffffffffs.",
                "fffffefffeffss",
                "fffffefffeffss",
                "ffffffffffffss",
                "fffffffffffsss",
                "sffffffffffss.",
                ".ssssssssss...",
            };

            public static readonly string[] Ghost =
            {
                "....ffffff....",
                "..ffffffffff..",
                ".fllffffffffs.",
                ".flfffffffffs.",
                "fffffefffeffss",
                "fffffefffeffss",
                "ffffffffffffss",
                "ffffffffffffss",
                "fffffffffffsss",
                "ffsfffsfffsfss",
            };
            public static readonly string[] GhostTailA = { "ff.ff.sff.sfs.", "f...f...f...s." };
            public static readonly string[] GhostTailB = { ".ff.ff.ff.ss..", "..f...f...s..." };
            public static readonly string[] Spikes = { ".l...l...l.", ".l..lll..l.", "lll.lll.lll" };

            public static readonly string[] Frog =
            {
                "..ff......ff....",
                ".fwef....fwef...",
                ".fwef....fwef...",
                "ffffffffffffff..",
                "fffffffffffffff.",
                "ffffnnnnnnnfffs.",
                "fbbbbbbbbbbbffs.",
                "sbbbbbbbbbbbbss.",
                ".ssbbbbbbbbbss..",
            };
            public static readonly string[] FrogFeet = { "ss..........ss.." };

            public static readonly string[] BatBody = { "f....f", "ff..ff", "ffffff", "feffef", "ffffff", ".ffff." };
            public static readonly string[] WingUp = { "d......", "dd.....", "ddd....", "dddd...", "ddddd..", ".dddddd", "..d.d.d" };
            public static readonly string[] WingDown = { "..d.d.d", ".dddddd", "ddddd..", "dddd...", "ddd....", "dd.....", "d......" };

            public static readonly string[] Boar =
            {
                "........ssssssss........",
                "......ssffffffffss.ff...",
                "....ssffffffffffffffff..",
                "...sfffffffffffffffefw..",
                "..sffffffffffffffffeffb.",
                "..sfffffffffffffffffbbbb",
                "..sffffffffffffffffffwbb",
                "..sfffffffffffffffffff..",
                "...sffffffffffffffffs...",
                "....ssssssssssssssss....",
            };
            public static readonly string[] Leg = { "ff", "ff", "ss" };

            public static readonly string[] HeadFox =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "aaaaaaaaaaaa",
                "ffffffeffeff",
                "ffffffeffeff",
                "fffwwwwwwwwn",
                "sfwwwwwwwwws",
                ".sswwwwwwss.",
            };
            public static readonly string[] EarFox = { "f...", "ff..", "fwf.", "ffff" };
            public static readonly string[] FoxTail = { "...ff.", "..ffff", ".fffff", "ffffs.", "wwws..", "www..." };

            public static readonly string[] HeadRaccoon =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "dddddddddddd",
                "ddddddeddedd",
                "ddddddeddedd",
                "fffwwwwwwwwn",
                "sfwwwwwwwwws",
                ".sswwwwwwss.",
            };
            public static readonly string[] RingTail = { "...ff", "..dd.", ".ff..", "dd...", "ff..." };
            public static readonly string[] Blade = { "m", "m", "m", "m", "k" };
        }

        static Sprite Shroom(int f)
        {
            var g = new Figure(22, 22);
            int y = Sink(f);
            g.Stamp(Mob.StemFoot, 7, f == 1 ? 18 : 19);
            g.Stamp(Mob.StemFoot, 13, f == 4 ? 18 : 19);
            g.Stamp(Mob.Stem, 6, 14 + y);
            g.Stamp(Mob.Cap, 3, 7 + y);
            Face(g, f);
            return Finish(g, K(('f', "cb3416"), ('s', "572526"), ('w', "f2efe6"), ('b', "d9c38c"), ('c', "a48656")), 11);
        }

        static Sprite Jelly(int f)
        {
            var g = new Figure(20, 16);
            if (f == 1 || f == 4 || f == 3) g.Stamp(Mob.JellySquash, 1, 7);
            else if (f == 2) g.Stamp(Mob.JellyTall, 3, 5);
            else g.Stamp(Mob.Jelly, 2, 6);
            Face(g, f);
            return Finish(g, K(('f', "84ac49"), ('s', "589543"), ('l', "a8c44b")), 10);
        }

        static Sprite Wisp(int f, bool frost)
        {
            var g = new Figure(20, 24);
            int y = f == 1 ? -1 : f == 2 ? -2 : f == 3 ? 1 : 0;      // floats up and down
            if (frost) g.Stamp(Mob.Spikes, 4, 7 + y);
            g.Stamp(Mob.Ghost, 3, 9 + y);
            g.Stamp(f == 1 || f == 3 ? Mob.GhostTailB : Mob.GhostTailA, 3, 19 + y);
            Face(g, f);
            var keys = frost ? K(('f', "6cc0dc"), ('s', "2986b8"), ('l', "c8ecef")) : K(('f', "c79bd6"), ('s', "8a5fa8"), ('l', "f2efe6"));
            return Finish(g, keys, 10);
        }

        static Sprite Frog(int f)
        {
            var g = new Figure(22, 18);
            int y = f == 2 ? -2 : Sink(f);
            g.Stamp(Mob.FrogFeet, 3, 16);
            g.Stamp(Mob.Frog, 3, 7 + y);
            Face(g, f);
            var keys = K(('f', "589543"), ('s', "387840"), ('b', "c1a572"), ('w', "f2efe6"));
            keys['n'] = OutlineColor;
            return Finish(g, keys, 11);
        }

        static Sprite Bat(int f)
        {
            var g = new Figure(22, 14);
            bool up = f == 0 || f == 2 || f == 4;
            int y = f == 2 ? -1 : 0;
            g.Stamp(up ? Mob.WingUp : Mob.WingDown, 2, up ? 1 + y : 5 + y);
            g.Stamp(up ? Mob.WingUp : Mob.WingDown, 13, up ? 1 + y : 5 + y, true);
            g.Stamp(Mob.BatBody, 8, 4 + y);
            var keys = K(('f', "8a5fa8"), ('d', "4b3566"));
            keys['e'] = Pc(Pal.Hex(f == 3 ? "f2efe6" : "cb3416"));
            return Finish(g, keys, 11, 0);
        }

        static Sprite Boar(int f)
        {
            var g = new Figure(32, 24);
            int y = f == 2 ? -1 : f == 3 ? 1 : 0;
            int[] lx = { 7, 10, 18, 21 };
            for (int i = 0; i < 4; i++)
            {
                bool lift = (f == 1 && i % 2 == 0) || (f == 4 && i % 2 == 1);
                g.Stamp(Mob.Leg, lx[i], lift ? 19 : 20);
            }
            g.Stamp(Mob.Boar, 4, 10 + y);
            Face(g, f);
            return Finish(g, K(('f', "7a6457"), ('s', "544440"), ('b', "e7a9cf"), ('w', "f2efe6")), 16);
        }

        /// <summary>Two-legged bandits share the hero's build (body, feet, paw) under their own head.</summary>
        static void Biped(Figure g, int f, string[] head, string[] ear, string[] tail, bool roundEars)
        {
            int y = Sink(f);
            int lfx = 10, lfy = 27, rfx = 15, rfy = 27;
            if (f == 1) { lfx = 9; lfy = 26; rfx = 16; }
            if (f == 4) { lfx = 11; rfx = 14; rfy = 26; }
            g.Stamp(tail, 4, 18 + y);
            g.Stamp(Parts.Foot, lfx, lfy);
            g.Stamp(Parts.Foot, rfx, rfy);
            g.Stamp(Parts.Body, 10, 22 + y);
            g.Stamp(head, 8, 13 + y);
            if (roundEars) { g.Stamp(ear, 7, 11 + y); g.Stamp(ear, 15, 11 + y, true); }
            else { g.Stamp(ear, 8, 9 + y); g.Stamp(ear, 16, 9 + y, true); }
            int pawY = f == 2 ? 20 : 23;
            g.Stamp(Mob.Blade, 18, pawY - 5 + y);
            g.Stamp(Parts.Paw, 17, pawY + y);
            Face(g, f);
        }

        static Sprite Fox(int f)
        {
            var g = new Figure(26, 30);
            Biped(g, f, Mob.HeadFox, Mob.EarFox, Mob.FoxTail, false);
            var keys = K(('f', "f46616"), ('s', "cb3416"), ('w', "f2efe6"), ('a', "2986b8"), ('m', "c9c6cc"), ('k', "544440"));
            keys['n'] = OutlineColor;
            return Finish(g, keys, 13);
        }

        static Sprite Raccoon(int f)
        {
            var g = new Figure(26, 30);
            Biped(g, f, Mob.HeadRaccoon, Parts.EarRound, Mob.RingTail, true);
            var keys = K(('f', "8a8a94"), ('s', "5c5a61"), ('w', "c9c6cc"), ('d', "3a3e4c"), ('m', "c9c6cc"), ('k', "544440"));
            keys['e'] = Pc(Pal.Hex("f2efe6"));      // eyes shine out of the dark mask
            keys['n'] = OutlineColor;
            return Finish(g, keys, 13);
        }

        // ───────────── Bosses: round shapes at the same dot scale ─────────────

        /// <summary>A rounded part: its shade, then a slightly smaller lighter fill toward the light (top-left).</summary>
        static void Blob(PixelCanvas cv, float cx, float cy, float rx, float ry, Color32 main, Color32 shade)
        {
            cv.Ellipse(cx, cy, rx, ry, shade);
            cv.Ellipse(cx - 1, cy + 1, rx - 1, ry - 1, main);
        }

        /// <summary>Two eyes (2 wide, 4 tall), narrowed for the wind-up, shut into dashes when hurt.</summary>
        static void BossEyes(PixelCanvas cv, int x, int y, int gap, int f, Color32 col)
        {
            for (int i = 0; i < 2; i++)
            {
                int ex = x + i * gap;
                if (f == 3) cv.Rect(ex - 1, y, 4, 1, col);
                else if (f == 2) cv.Rect(ex, y, 2, 2, col);
                else cv.Rect(ex, y, 2, 4, col);
            }
        }

        static void Tri(PixelCanvas cv, int x0, int x1, int yBase, int h, Color32 c)
        {
            for (int k = 0; k < h; k++)
            {
                float t = k / (float)h;
                int a = Mathf.RoundToInt(Mathf.Lerp(x0, (x0 + x1) / 2f, t)), b = Mathf.RoundToInt(Mathf.Lerp(x1, (x0 + x1) / 2f, t));
                cv.Rect(a, yBase + k, Mathf.Max(1, b - a + 1), 1, c);
            }
        }

        static int Rise(int f) => f == 1 || f == 3 || f == 4 ? -1 : f == 2 ? 2 : 0;    // y up

        static Sprite BossShroom(int f)
        {
            var cv = new PixelCanvas(44, 44);
            int r = Rise(f);
            Color32 cap = Pc(Pal.Hex("70a744")), capS = Pc(Pal.Hex("387840")), spot = Pc(Pal.Hex("f2efe6")),
                    stem = Pc(Pal.Hex("d9c38c")), stemS = Pc(Pal.Hex("a48656")), gold = Pc(Pal.Hex("e1c549")), gem = Pc(Pal.Hex("cb3416"));
            cv.Ellipse(15, 3 + (f == 1 ? 1 : 0), 4, 2.5f, stemS);
            cv.Ellipse(29, 3 + (f == 4 ? 1 : 0), 4, 2.5f, stemS);
            Blob(cv, 22, 12 + r, 11, 8, stem, stemS);
            BossEyes(cv, 21, 11 + r, 6, f, OutlineColor);
            Blob(cv, 22, 26 + r, 20, 9, cap, capS);
            cv.Circle(12, 28 + r, 2.5f, spot); cv.Circle(22, 31 + r, 2, spot); cv.Circle(31, 27 + r, 3, spot); cv.Circle(17, 24 + r, 1.5f, spot);
            cv.Rect(16, 34 + r, 12, 3, gold);
            for (int i = 0; i < 3; i++) Tri(cv, 16 + i * 5, 17 + i * 5, 37 + r, 4, gold);
            cv.Rect(21, 35 + r, 2, 1, gem);
            cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(22, 1));
        }

        static Sprite BossFox(int f)
        {
            var cv = new PixelCanvas(48, 48);
            int r = Rise(f);
            Color32 fur = Pc(Pal.Hex("f46616")), furS = Pc(Pal.Hex("cb3416")), white = Pc(Pal.Hex("f2efe6")), whiteS = Pc(Pal.Hex("c9c6cc")),
                    vest = Pc(Pal.Hex("3a3e4c")), vestS = Pc(Pal.Hex("272834")), band = Pc(Pal.Hex("cb3416")), steel = Pc(Pal.Hex("c9c6cc")), steelS = Pc(Pal.Hex("8a8a94"));
            Blob(cv, 11, 20 + r, 7, 10, fur, furS);                                  // tail
            Blob(cv, 8, 28 + r, 4, 4, white, whiteS);
            cv.Ellipse(19, 3 + (f == 1 ? 1 : 0), 4, 2.5f, furS);
            cv.Ellipse(30, 3 + (f == 4 ? 1 : 0), 4, 2.5f, furS);
            Blob(cv, 24, 14 + r, 11, 10, vest, vestS);
            Blob(cv, 25, 14 + r, 5, 7, white, whiteS);
            Tri(cv, 15, 21, 36 + r, 9, furS); Tri(cv, 16, 20, 36 + r, 6, fur);       // ears
            Tri(cv, 28, 34, 36 + r, 9, furS); Tri(cv, 29, 33, 36 + r, 6, fur);
            Blob(cv, 25, 30 + r, 12, 9, fur, furS);
            Blob(cv, 29, 26 + r, 7, 4, white, whiteS);
            cv.Rect(35, 27 + r, 2, 2, OutlineColor);                                 // nose
            cv.Rect(14, 33 + r, 23, 2, band);
            BossEyes(cv, 23, 28 + r, 6, f, OutlineColor);
            int sy = f == 2 ? 20 : 12;                                               // the blade, raised for the wind-up
            for (int k = 0; k < 22; k++) { cv.Rect(38 + k / 4, sy + k + r, 2, 1, steel); cv.Set(39 + k / 4, sy + k + r, steelS); }
            cv.Rect(35, sy - 1 + r, 7, 2, Pc(Pal.Hex("e1c549")));
            Blob(cv, 37, sy - 1 + r, 3, 3, fur, furS);                              // paw
            cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(24, 1));
        }

        static Sprite Knight(int f)
        {
            var cv = new PixelCanvas(40, 46);
            int r = Rise(f);
            Color32 plate = Pc(Pal.Hex("5c5a61")), plateS = Pc(Pal.Hex("3a3e4c")), cape = Pc(Pal.Hex("4b3566")), capeS = Pc(Pal.Hex("272834")),
                    plume = Pc(Pal.Hex("8a5fa8")), glow = Pc(Pal.Hex(f == 2 ? "f46616" : "c79bd6")), steel = Pc(Pal.Hex("c9c6cc")), steelS = Pc(Pal.Hex("8a8a94"));
            Blob(cv, 14, 19 + r, 10, 15, cape, capeS);
            cv.Rect(13, 1 + (f == 1 ? 1 : 0), 5, 9, plateS); cv.Rect(22, 1 + (f == 4 ? 1 : 0), 5, 9, plateS);
            cv.Rect(13, 9 + (f == 1 ? 1 : 0), 4, 1, plate); cv.Rect(22, 9 + (f == 4 ? 1 : 0), 4, 1, plate);
            Blob(cv, 20, 18 + r, 10, 9, plate, plateS);
            cv.Rect(11, 15 + r, 19, 2, cape);                                      // belt
            Blob(cv, 21, 32 + r, 8, 8, plate, plateS);
            cv.Rect(15, 31 + r, 13, 3, OutlineColor);                              // visor slit
            if (f != 3) { cv.Rect(20, 32 + r, 2, 1, glow); cv.Rect(25, 32 + r, 2, 1, glow); }
            cv.Ellipse(18, 42 + r, 3, 3, plume); cv.Rect(19, 39 + r, 2, 2, plume);
            int sy = f == 2 ? 18 : 10;
            for (int k = 0; k < 26; k++) { cv.Rect(32 + k / 9, sy + k + r, 2, 1, steel); cv.Set(33 + k / 9, sy + k + r, steelS); }
            cv.Rect(29, sy - 1 + r, 7, 2, Pc(Pal.Hex("e1c549")));
            Blob(cv, 31, sy - 2 + r, 3, 3, plate, plateS);
            cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(20, 1));
        }
    }
}
