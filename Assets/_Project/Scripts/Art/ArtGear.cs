using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Weapons, the shield, thrown things and pickups as dot templates (same keys and outline as Art/ArtHero.cs):
    /// sized for the 22-dot hero, flat palette tones. Weapons point right (+x) with the pivot on the grip.
    /// Keys: k grip · d grip wrap · g guard/gold · m steel · l steel light · n steel shade · c gem · w white · r red.
    /// </summary>
    public static partial class Art
    {
        static class Gear
        {
            public static readonly string[] Greatsword =
            {
                ".....g................",
                ".....gmmmmmmmmmmmmm...",
                "kdkdkgllllllllllllllm.",
                "kdkdkgmmmmmmmmmmmmmmmm",
                ".....gnnnnnnnnnnnnnn..",
                ".....g................",
            };
            public static readonly string[] Sword =
            {
                "...g..........",
                "kdkgmmmmmmmmm.",
                "kdkglllllllllm",
                "...gnnnnnnnn..",
                "...g..........",
            };
            public static readonly string[] Dagger = { ".g......", "kgmmmmm.", "kglllllm", ".gnnnn.." };
            public static readonly string[] Katana =
            {
                ".....g.............",
                "kdkdkgmmmmmmmmmmmm.",
                "kdkdkgllllllllllllm",
                ".....gnnnnnnnnnnn..",
            };
            public static readonly string[] Staff =
            {
                "...............cc...",
                "..............cwcc..",
                "kkkkkgkkkkkkgkcccccc",
                "kkkkkgkkkkkkgkcccccn",
                "..............ccccn.",
                "...............cnn..",
            };
            public static readonly string[] Crossbow =
            {
                "........n....",
                "........nw...",
                "........n.w..",
                "kkkkkkkkkmmmm",
                "kkkkkkkkkd...",
                "...kk...n.w..",
                "...kk...nw...",
                "........n....",
            };
            public static readonly string[] Shield =
            {
                ".gggggggg.",
                "gmmmmmmmng",
                "gmmrrrrmng",
                "gmmrwwrmng",
                "gmmrrrrmng",
                "gmmmmmmmng",
                ".gmmmmmng.",
                "..gmmmng..",
                "...gmng...",
                "....gg....",
            };
            public static readonly string[] Bolt = { "......l.", "kkkkkmmm", "......n." };
            public static readonly string[] Knife = { "kkmmmm.", "kklllll", "...nn.." };

            public static readonly string[] Coin = { "..gggg..", ".gwgggg.", "gwggdggg", "gggdgggn", "gggdgggn", "ggggdggn", ".gggggn.", "..nnnn.." };
            public static readonly string[] Heart = { ".rr...rr.", "rwrr.rrrr", "rwrrrrrrd", "rrrrrrrrd", ".rrrrrrd.", "..rrrrd..", "...rrd...", "....d...." };
            public static readonly string[] Gem = { "...c...", "..cwc..", ".cwccc.", "ccccccn", ".ccccn.", "..ccn..", "...n..." };
            public static readonly string[] Warp = { "..mm...", ".mmmm..", ".mccm..", ".mcwm..", ".mccm..", ".mccmn.", ".mmmmn.", "mmmmmmn" };
        }

        static Sprite Plate(string[] t, Dictionary<char, Color32> keys, Vector2 pivot)
        {
            // A dot of room on every side for the outline
            var f = new Figure(t[0].Length + 2, t.Length + 2);
            f.Stamp(t, 1, 1);
            var cv = f.Paint(keys);
            cv.Outline(OutlineColor);
            return cv.ToSprite(pivot + Vector2.one);
        }

        static Dictionary<char, Color32> GearKeys(string gem = "c79bd6", string gemShade = "8a5fa8") => K(
            ('k', "7a6457"), ('d', "544440"), ('g', "e1c549"), ('m', "c9c6cc"), ('l', "f2efe6"), ('n', "8a8a94"),
            ('c', gem), ('w', "f2efe6"), ('r', "cb3416"));

        public static Sprite Weapon(WeaponKind k) => Get($"weapon_{k}", () =>
        {
            switch (k)
            {
                case WeaponKind.Greatsword: return Plate(Gear.Greatsword, GearKeys(), new Vector2(2, 2.5f));
                case WeaponKind.SwordShield: return Plate(Gear.Sword, GearKeys(), new Vector2(1.5f, 2.5f));
                case WeaponKind.Dagger: return Plate(Gear.Dagger, GearKeys(), new Vector2(0.5f, 2));
                case WeaponKind.Katana: return Plate(Gear.Katana, GearKeys(), new Vector2(2, 2));
                case WeaponKind.Staff:
                {
                    var keys = GearKeys();
                    keys['n'] = Hx("8a5fa8");
                    return Plate(Gear.Staff, keys, new Vector2(6, 3));
                }
                default: return Plate(Gear.Crossbow, GearKeys(), new Vector2(4, 4));
            }
        });

        public static Sprite Shield() => Get("shield", () => Plate(Gear.Shield, GearKeys(), new Vector2(5, 5)));

        // ───────────── Thrown things ─────────────

        public static Sprite Bolt() => Get("bolt", () => Plate(Gear.Bolt, GearKeys(), new Vector2(4, 1.5f)));
        public static Sprite Knife() => Get("knife", () => Plate(Gear.Knife, GearKeys(), new Vector2(3.5f, 1.5f)));

        // ───────────── Pickups ─────────────

        public static Sprite Item(string id) => Get($"item_{id}", () =>
        {
            var keys = K(('g', "e1c549"), ('n', "a48656"), ('d', "a48656"), ('w', "f2efe6"), ('r', "cb3416"), ('c', "6cc0dc"), ('m', "4a4b55"));
            switch (id)
            {
                case "gold": return Plate(Gear.Coin, keys, new Vector2(4, 4));
                case "heart": keys['d'] = Hx("572526"); return Plate(Gear.Heart, keys, new Vector2(4.5f, 4));
                case "xp": keys['n'] = Hx("2986b8"); return Plate(Gear.Gem, keys, new Vector2(3.5f, 3.5f));
                case "warpstone": keys['n'] = Hx("3a3e4c"); return Plate(Gear.Warp, keys, new Vector2(3.5f, 4));
                default: return Plate(Gear.Coin, keys, new Vector2(4, 4));
            }
        });
    }
}
