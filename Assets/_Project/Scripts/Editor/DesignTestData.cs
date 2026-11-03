using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// A design pack with every picture slot filled by small locally drawn canvases (what the image jobs would store), so the
    /// smoke test and UI screenshots can exercise a pictured hero and weapon without the image API: a 6-pose hero, weapon, shot
    /// and generated effects.
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

        public static DesignPack Pictured(Player p) => Pictured(DesignStore.PackOfCostume(p.Costume.id));

        public static DesignPack Pictured(DesignPack basePack)
        {
            var pack = (basePack ?? DesignPack.Default()).Clone();
            pack.hero.frames = Enumerable.Range(0, 6).Select(i => Blob(14, 24 + i, Pal.Hex("9b6bd9"))).ToArray();
            pack.hero.image = pack.hero.frames[0];
            if (!DesignCombat.IsDesigned(pack.weapon)) pack.weapon.moves = new List<string> { "slash", "wide" };
            pack.weapon.image = Blob(20, 6, Pal.Hex("d9e1ea"));
            pack.attack.projectile.image = Blob(8, 8, Pal.Hex("fff36b"));
            pack.attack.effectImages = Enumerable.Range(0, 4).Select(i => Blob(24, 24, Pal.Hex("7ff3ff"))).ToArray();
            return pack;
        }
    }
}
