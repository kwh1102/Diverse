using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>The actual icon palette is the shared colour identity of a skill, including older saved skills.</summary>
    public static class ActiveSkillStyle
    {
        public static void Ensure(ActiveSkillData a)
        {
            if (a == null || a.visualPalette?.Length == 4) return;
            Color32 primary = Pal.Hex(a.color);
            var icon = DesignStore.Image(a.iconImage);
            if (icon != null && icon.isReadable)
            {
                var body = icon.GetPixels32().Where(p => {
                    Color.RGBToHSV(p, out float h, out float s, out float v);
                    return p.a > 128 && s > .3f && v > .42f;
                }).GroupBy(p => (p.r / 32, p.g / 32, p.b / 32)).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (body != null) primary = body.First();
            }
            string Hex(Color32 c) => ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();
            a.color = Hex(primary);
            a.visualPalette = new[] { "302638", Hex(Pal.Mul(primary, .65f)), Hex(primary), Hex(Pal.Lerp(primary, Pal.White, .7f)) };
        }
        public static string Prompt(ActiveSkillData a)
        {
            Ensure(a);
            return "Use this primary material shade ramp: " + string.Join(", ", a.visualPalette.Select(c => "#" + c)) +
                ". Keep the icon concept's secondary material colours, 3-4 flat tones per material; no global monochrome recolouring. Main body colour #" + a.color + ". Keep the same material, shape language and chunky silhouettes as its icon concept: " + a.iconPrompt + ". ";
        }
        public static Color32 Tone(ActiveSkillData a, float value)
        {
            Ensure(a);
            return Pal.Hex(a.visualPalette[value < .22f ? 0 : value < .5f ? 1 : value < .85f ? 2 : 3]);
        }
    }
}
