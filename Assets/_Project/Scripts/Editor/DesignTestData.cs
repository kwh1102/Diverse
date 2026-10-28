using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// A design pack with every picture slot filled by small locally drawn canvases (what the image jobs would store), so the
    /// smoke test and UI screenshots can exercise the pictured world without the image API: a city of buildings along a street
    /// grid, two-variant ground, generated effects, 6-pose hero and 3-pose enemies.
    /// </summary>
    public static class DesignTestData
    {
        static string Blob(int w, int h, Color32 c)
        {
            var cv = new PixelCanvas(w, h);
            cv.Ellipse(w / 2f, h / 2f, w / 2f - 1, h / 2f - 1, c);
            cv.Outline(Pal.Outline);
            return DesignStore.SaveImage(cv);
        }

        static string Building(int w, int h, Color32 wall, Color32 roof)
        {
            var cv = new PixelCanvas(w, h);
            cv.Rect(1, 1, w - 2, h * 2 / 3, wall);
            for (int y = h * 2 / 3; y < h - 1; y++) { int inset = (y - h * 2 / 3); cv.Rect(1 + inset, y, Mathf.Max(1, w - 2 - inset * 2), 1, roof); }
            for (int y = 4; y < h * 2 / 3 - 3; y += 6)
                for (int x = 3; x < w - 5; x += 6) cv.Rect(x, y, 3, 3, Pal.Hex("ffe9a8"));
            cv.Outline(Pal.Outline);
            return DesignStore.SaveImage(cv);
        }

        static string Tile(Color32 c)
        {
            var cv = new PixelCanvas(32, 32);
            for (int i = 0; i < cv.Px.Length; i++) cv.Px[i] = (i * 7919) % 13 == 0 ? Pal.Mul(c, 0.9f) : c;
            return DesignStore.SaveImage(cv);
        }

        public static DesignPack Pictured(Player p)
        {
            var pack = (DesignStore.PackOfCostume(p.Costume.id) ?? DesignPack.Default()).Clone();
            pack.hero.frames = Enumerable.Range(0, 6).Select(i => Blob(14, 24 + i, Pal.Hex("9b6bd9"))).ToArray();
            pack.hero.image = pack.hero.frames[0];
            if (!DesignCombat.IsDesigned(pack.weapon)) pack.weapon.moves = new List<string> { "slash", "wide" };
            pack.weapon.image = Blob(20, 6, Pal.Hex("d9e1ea"));
            pack.attack.projectile.image = Blob(8, 8, Pal.Hex("fff36b"));
            pack.attack.effectImages = Enumerable.Range(0, 4).Select(i => Blob(24, 24, Pal.Hex("7ff3ff"))).ToArray();
            pack.world.tileImages = new[] { "7d8a96", "84919c", "4b4f58", "53575f", "4f86c9", "5a8fd0", "a69a86", "ae a28e".Replace(" ", "") }
                .Select(h => Tile(Pal.Hex(h))).ToArray();
            pack.world.layout = "city";
            pack.world.themePrompt = "a grey concrete city";
            var walls = new[] { "e8d2b0", "c9d6e6", "f2c4b0", "d6e8c9" };
            pack.world.decorImages = new[]
            {
                Building(30, 48, Pal.Hex(walls[0]), Pal.Hex("8c5bd6")), Building(26, 44, Pal.Hex(walls[1]), Pal.Hex("c2603e")),
                Building(34, 40, Pal.Hex(walls[2]), Pal.Hex("3f78c9")), Building(24, 48, Pal.Hex(walls[3]), Pal.Hex("5a6478")),
                Blob(6, 18, Pal.Hex("3d3d4f")), Blob(14, 10, Pal.Hex("8a5a3c")), Blob(10, 14, Pal.Hex("e8584f")),
                Blob(10, 13, Pal.Hex("5fae64")), Blob(12, 10, Pal.Hex("f4c95d")), Blob(8, 12, Pal.Hex("9a96a6")),
                Blob(6, 6, Pal.Hex("d9e1ea")), Blob(7, 5, Pal.Hex("fff2b3")),
            };
            pack.world.decorSizes = new[] { "large", "large", "large", "large", "medium", "medium", "medium", "small", "small", "small", "tiny", "tiny" };
            pack.world.decorPlaces = new[] { "road", "road", "road", "road", "road", "scatter", "road", "scatter", "scatter", "scatter", "scatter", "scatter" };
            pack.world.townImages = Enumerable.Range(0, 4).Select(i => Building(40, 58 - i * 8, Pal.Hex(walls[i]), Pal.Hex("8c5bd6"))).ToArray();
            pack.world.ambient = "dust";
            pack.enemies.art = DB.Enemies.Values.Select(e => new EnemyArt { id = e.id, image = Blob(14, 14, Pal.Hex("9be05a")), image2 = Blob(16, 12, Pal.Hex("9be05a")), image3 = Blob(18, 13, Pal.Hex("9be05a")) }).ToList();
            return pack;
        }
    }
}
