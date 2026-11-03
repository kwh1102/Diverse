using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// The hero, the elders and the town folk as dot templates, following the art rules (Data/ArtStyle.asset): a small
    /// chibi figure 20-odd dots tall seen from the front at 3/4, filled with flat palette tones and closed by one dark
    /// outline added around the whole figure. Parts (head, ears, body, feet, accessories) are stamped on a 26×30 grid,
    /// rows top to bottom, facing right (the game flips the sprite to face left); the pivot is between the feet.
    /// Template keys: f fur · s fur shade · e eyes · c cloth · d cloth shade · a trim · w white · b belly · k wood · y beak.
    /// Costumes only change the colours (CostumeDef fur/outfit/accent, snapped to the palette) and the accessory (style).
    /// The baked PNGs in Resources/Sprites (menu Diverse/스프라이트 PNG로 굽기) win over these, so they can be repainted.
    /// </summary>
    public static partial class Art
    {
        const int FW = 26, FH = 30;

        static class Parts
        {
            public static readonly string[] Head =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "ffffffeffeff",
                "ffffffeffeff",
                "fffffffffffs",
                "sfffffffffss",
                ".ssffffffss.",
                "...ssssss...",
            };
            public static readonly string[] HeadHurt =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "fffffefefeff",
                "ffffffefffef",
                "fffffefefffs",
                "sfffffffffss",
                ".ssffffffss.",
                "...ssssss...",
            };
            public static readonly string[] HeadBlink =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "ffffffffffff",
                "ffffffeffeff",
                "fffffffffffs",
                "sfffffffffss",
                ".ssffffffss.",
                "...ssssss...",
            };
            public static readonly string[] HeadElder =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "ffffffffffff",
                "ffffffeeffee",
                "fffffwwwwwws",
                "sffffwwwwwss",
                ".ssfffwwwss.",
                "...ssssss...",
            };
            public static readonly string[] EarL = { "ff", "fs", "fs", "fs", "fs", "fs", "ff" };
            public static readonly string[] EarR = { "ff", "sf", "sf", "sf", "sf", "sf", "ff" };
            public static readonly string[] EarBent = { "....", "ff..", "ffff", ".sff", ".sf.", ".sf.", ".ff." };
            public static readonly string[] Body =
            {
                ".ffffff.",
                "ffffffff",
                "fffffffs",
                ".fffffs.",
            };
            public static readonly string[] Foot = { "fff", "sss" };
            public static readonly string[] Paw = { "ff", "fs" };
            public static readonly string[] Scarf = { "aaaaaaaa", "daaaaaad" };
            public static readonly string[] ScarfTail = { "aa", "da", "d." };
            public static readonly string[] Cape = { "..dd", ".ccd", "cccd", "cccd", "cccd", "ccdd", "cd.." };
            public static readonly string[] Belt = { "aaaaaaaa" };
            public static readonly string[] Band = { "cccccccccccc" };
            public static readonly string[] Crown = { "a.c.a", "aaaaa" };
            public static readonly string[] Flower = { ".a.", "aca", ".a." };
            public static readonly string[] Cane = { "kk", ".k", ".k", ".k", ".k", ".k", ".k", ".k", ".k", ".k" };

            // Town folk: a rounder head and a sturdier body
            public static readonly string[] Owl =
            {
                "f..........f",
                "ff........ff",
                "ffffffffffff",
                "fwwwffffwwwf",
                "fwewffffwewf",
                "fwwwfyyfwwwf",
                "ffffffyfffff",
                "fbbbbbbbbbbs",
                "sbbsbbbsbbbs",
                "sbbbbbbbbbbs",
                "ssbbsbbbsbss",
                ".ssbbbbbbss.",
                "..ssssssss..",
            };
            public static readonly string[] OwlFeet = { "yy....yy" };
            public static readonly string[] EarCat = { "f...", "ff..", "fff." };
            public static readonly string[] EarRound = { ".ff.", "ffff", "fsff" };
            public static readonly string[] HeadCat =
            {
                "..ffffffff..",
                ".ffsffffsff.",
                "ffffffffffff",
                "ffffffeffeff",
                "ffffffeffeff",
                "ffffbbbbbbfs",
                "sfffbbbebbss",
                ".ssfbbbbbss.",
                "...ssssss...",
            };
            public static readonly string[] HeadBear =
            {
                "..ffffffff..",
                ".ffffffffff.",
                "ffffffffffff",
                "ffffffeffeff",
                "ffffffbbbbfs",
                "fffffbbeebbs",
                "sffffbbbbbss",
                ".ssffffffss.",
                "...ssssss...",
            };
            public static readonly string[] Hat = { "...dddddd...", "..cccccccc..", "dddddddddddddd" };
            public static readonly string[] BodyNpc =
            {
                ".ffffffffff.",
                "ffccccccccff",
                "ffccccccccfs",
                "ffccccccccfs",
                ".fddddddddf.",
            };
            public static readonly string[] Hammer = { "kkk", "kkk", ".d.", ".d.", ".d." };
        }

        /// <summary>A figure being drawn: template keys on a y-down grid.</summary>
        sealed class Figure
        {
            public readonly int W, H;
            public readonly char[,] G;
            public Figure(int w = FW, int h = FH)
            {
                W = w; H = h; G = new char[w, h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) G[x, y] = '.';
            }

            public void Stamp(string[] part, int x, int y, bool mirror = false)
            {
                for (int r = 0; r < part.Length; r++)
                    for (int c = 0; c < part[r].Length; c++)
                    {
                        char k = part[r][mirror ? part[r].Length - 1 - c : c];
                        int px = x + c, py = y + r;
                        if (k != '.' && px >= 0 && py >= 0 && px < W && py < H) G[px, py] = k;
                    }
            }

            /// <summary>Colour the keys, close the figure with one outline, flip to the canvas (y up).</summary>
            public PixelCanvas Paint(Dictionary<char, Color32> keys)
            {
                var cv = new PixelCanvas(W, H);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (G[x, y] != '.' && keys.TryGetValue(G[x, y], out var c)) cv.Set(x, H - 1 - y, c);
                return cv;
            }

            /// <summary>Narrow every eye (a 1×2 'e' column) to its lower dot: a squint for a wind-up.</summary>
            public void Squint()
            {
                for (int y = 0; y < H - 1; y++)
                    for (int x = 1; x < W; x++)
                        if (G[x, y] == 'e' && G[x, y + 1] == 'e' && (y == 0 || G[x, y - 1] != 'e')) G[x, y] = G[x - 1, y];
            }

            /// <summary>Shut every eye into a short dash: the hurt face.</summary>
            public void Shut()
            {
                for (int y = 0; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                        if (G[x, y] == 'e' && G[x, y + 1] == 'e' && (y == 0 || G[x, y - 1] != 'e'))
                        {
                            char body = G[x - 1, y];
                            G[x, y] = body;
                            if (G[x - 1, y + 1] != '.') G[x - 1, y + 1] = 'e';
                            if (G[x + 1, y + 1] != '.' && G[x + 1, y + 1] != 'e') G[x + 1, y + 1] = 'e';
                        }
            }
        }

        static Dictionary<char, Color32> K(params (char key, string hex)[] keys)
        {
            var d = new Dictionary<char, Color32> { ['e'] = OutlineColor };
            foreach (var (k, h) in keys) d[k] = Pc(Pal.Hex(h));
            return d;
        }

        /// <summary>The nearest palette colour: everything a figure shows comes from the one palette.</summary>
        static Color32 Pc(Color32 c) => TileBaker.PaletteColor(TileBaker.Snap(c));
        static Color32 Pi(int i) => TileBaker.PaletteColor(i);
        /// <summary>Like Pc, keeping the colour's alpha.</summary>
        static Color32 Pa(Color32 c) { var p = Pc(c); p.a = c.a; return p; }
        static Color32 OutlineColor => Pi(DB.ArtStyle.outline);

        static Dictionary<char, Color32> Keys(Color32 fur, Color32 furShade, Color32 cloth, Color32 clothShade, Color32 trim)
        {
            fur = Pc(fur);
            // Eyes stay readable on dark fur
            bool dark = fur.r * 0.3f + fur.g * 0.59f + fur.b * 0.11f < 90;
            return new Dictionary<char, Color32>
            {
                ['f'] = fur, ['s'] = Pc(furShade), ['e'] = dark ? Pc(Pal.Hex("e1c549")) : OutlineColor,
                ['c'] = Pc(cloth), ['d'] = Pc(clothShade), ['a'] = Pc(trim),
                ['w'] = Pc(Pal.Hex("f2efe6")), ['k'] = Pc(Pal.Hex("7a6457")), ['y'] = Pc(Pal.Hex("e1c549")), ['b'] = Pc(Pal.Hex("c1a572")),
            };
        }

        /// <summary>The hero rabbit in one pose. style: 0 scarf · 1 scarf + cape · 2 headband + belt · 3 cape · 4 flower + belt · 5 crown + cape.</summary>
        static Sprite DrawRabbit(Color32 fur, Color32 furShade, Color32 outfit, Color32 outfitShade, Color32 accent, int style, Pose pose, bool elder, HeroLook look = null)
        {
            var f = new Figure();
            int bob = 0, sway = 0, lfx = 10, lfy = 27, rfx = 15, rfy = 27, pawX = 17, pawY = 23;
            var head = Parts.Head;
            switch (pose)
            {
                case Pose.Idle1: bob = 1; break;
                case Pose.Run0: lfx = 9; lfy = 26; rfx = 16; sway = -1; break;
                case Pose.Run1: bob = -1; lfx = 11; rfx = 14; rfy = 26; sway = -1; break;
                case Pose.Run2: lfx = 11; rfx = 16; rfy = 26; sway = -1; break;
                case Pose.Run3: bob = -1; lfy = 26; rfx = 14; sway = -1; break;
                case Pose.Hurt: bob = 1; head = Parts.HeadHurt; break;
                case Pose.Cast: pawY = 20; head = Parts.HeadBlink; break;
                case Pose.AttackWind: bob = 1; sway = 1; pawX = 15; pawY = 21; break;
                case Pose.AttackHit: sway = -1; pawX = 18; pawY = 24; break;
            }
            if (elder) { bob += 1; head = Parts.HeadElder; }
            int y = bob;
            if (style == 1 || style == 3 || style == 5) f.Stamp(Parts.Cape, 7, 21 + y);
            f.Stamp(Parts.Foot, lfx, lfy);
            f.Stamp(Parts.Foot, rfx, rfy);
            f.Stamp(Parts.Body, 10, 22 + y);
            if (style == 2 || style == 4) f.Stamp(Parts.Belt, 10, 24 + y);
            f.Stamp(head, 8, 13 + y);
            // Ears lean together with the motion, so they never pinch into each other
            f.Stamp(Parts.EarL, 10, 9 + y);
            f.Stamp(new[] { Parts.EarL[0], Parts.EarL[1], Parts.EarL[2] }, 10 + sway, 6 + y);
            if (pose == Pose.Hurt || elder) f.Stamp(Parts.EarBent, 14, 6 + y);
            else
            {
                f.Stamp(Parts.EarR, 15, 9 + y);
                f.Stamp(new[] { Parts.EarR[0], Parts.EarR[1], Parts.EarR[2] }, 15 + sway, 6 + y);
            }
            if (style == 0 || style == 1) { f.Stamp(Parts.Scarf, 10, 21 + y); f.Stamp(Parts.ScarfTail, 8, 22 + y); }
            if (style == 2) f.Stamp(Parts.Band, 8, 14 + y);
            if (style == 4) f.Stamp(Parts.Flower, 8, 12 + y);
            if (style == 5) f.Stamp(Parts.Crown, 11, 11 + y);
            if (elder) f.Stamp(Parts.Cane, 18, 18);
            else f.Stamp(Parts.Paw, pawX, pawY + y);

            var cv = f.Paint(Keys(fur, furShade, outfit, outfitShade, accent));
            // A designed hero's headwear (custom mode), at the head centre and eye row on the canvas (y up)
            DesignArt.Decorate(cv, look, 14, FH - 1 - (17 + y), FH - 1 - (16 + y), Pc(accent));
            cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(13, 1));
        }

        /// <summary>Town folk: owl (storyteller), cat (merchant), bear (smith), otter (ferryman). Frame 1 breathes in.</summary>
        static Sprite DrawNpc(string kind, int frame)
        {
            var f = new Figure();
            int y = frame == 1 ? 1 : 0;
            Dictionary<char, Color32> keys;
            switch (kind)
            {
                case "owl":
                    keys = Keys(Pal.Hex("7a6457"), Pal.Hex("544440"), Pal.Hex("c1a572"), Pal.Hex("a48656"), Pal.Hex("e1c549"));
                    f.Stamp(Parts.Owl, 7, 14 + y);
                    f.Stamp(Parts.OwlFeet, 9, 27);
                    break;
                case "cat":
                    keys = Keys(Pal.Hex("e1c549"), Pal.Hex("a48656"), Pal.Hex("2986b8"), Pal.Hex("1f4f7a"), Pal.Hex("f2efe6"));
                    f.Stamp(Parts.Foot, 9, 27); f.Stamp(Parts.Foot, 15, 27);
                    f.Stamp(Parts.BodyNpc, 7, 22 + y);
                    f.Stamp(Parts.HeadCat, 7, 13 + y);
                    f.Stamp(Parts.EarCat, 7, 10 + y); f.Stamp(Parts.EarCat, 15, 10 + y, true);
                    break;
                case "bear":
                    keys = Keys(Pal.Hex("7a6457"), Pal.Hex("544440"), Pal.Hex("4a4b55"), Pal.Hex("3a3e4c"), Pal.Hex("8a8a94"));
                    f.Stamp(Parts.Hammer, 19, 18 + y);
                    f.Stamp(Parts.Foot, 9, 27); f.Stamp(Parts.Foot, 15, 27);
                    f.Stamp(Parts.BodyNpc, 7, 22 + y);
                    f.Stamp(Parts.HeadBear, 7, 13 + y);
                    f.Stamp(Parts.EarRound, 6, 11 + y); f.Stamp(Parts.EarRound, 16, 11 + y, true);
                    break;
                default:     // otter
                    keys = Keys(Pal.Hex("a48656"), Pal.Hex("7a6457"), Pal.Hex("c1a572"), Pal.Hex("a48656"), Pal.Hex("e1c549"));
                    f.Stamp(Parts.Foot, 9, 27); f.Stamp(Parts.Foot, 15, 27);
                    f.Stamp(Parts.BodyNpc, 7, 22 + y);
                    f.Stamp(Parts.HeadBear, 7, 13 + y);
                    f.Stamp(Parts.Hat, 6, 10 + y);
                    break;
            }
            keys['k'] = Pc(Pal.Hex("7a6457"));
            var cv = f.Paint(keys);
            cv.Outline(OutlineColor);
            return cv.ToSprite(new Vector2(13, 1));
        }
    }
}
