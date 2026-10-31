using System;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Regional terrain materials, using the reference's closed outlines and quiet 2–4 tone pixel clusters.</summary>
    public static class BiomeTerrainStyle
    {
        static readonly (string name, string hex)[] Colors =
        {
            ("forest floor", "315f45"), ("forest deep moss", "254a3e"), ("forest lit moss", "4f8050"),
            ("bog silt", "465447"), ("bog wet silt", "3b4941"), ("bog lichen", "687a50"),
            ("snow shade", "aabfc6"), ("snow", "d8e1d6"), ("snow crest", "edf0d8"),
            ("ice deep", "466b7d"), ("ice", "709fae"), ("ice face", "add2ce"),
            ("sandstone shade", "8b654b"), ("sandstone", "bc9464"), ("sandstone crest", "d5b777"),
            ("mineral shade", "494b60"), ("mineral bed", "73778c"), ("mineral crest", "9ca7ad"),
            ("mirror shade", "303447"), ("mirror stone", "494f67"), ("mirror reflection", "6d7b93"),
            ("ruin mortar", "3e4148"), ("ruin flagstone", "73716e"), ("ruin worn edge", "918b7e"),
            ("bog liquid", "748c42"), ("bog liquid glint", "b2bd59"),
            ("mineral floor shade", "65697e"), ("mineral floor light", "818a99"),
        };

        public static void Configure(ArtStyleDef s)
        {
            const int start = 38;
            if (s.palette.Length < start + Colors.Length) Array.Resize(ref s.palette, start + Colors.Length);
            if (s.paletteNames.Length < s.palette.Length) Array.Resize(ref s.paletteNames, s.palette.Length);
            for (int i = 0; i < Colors.Length; i++)
            { s.palette[start + i] = Pal.Hex(Colors[i].hex); s.paletteNames[start + i] = Colors[i].name; }

            void GroundStyle(Ground ground, TilePattern pattern, int shade, int accent, int[] ramp, int edge = -1, int[] bank = null)
            {
                var look = s.grounds.Find(g => g.ground == ground);
                if (look == null) { look = new GroundLook { ground = ground }; s.grounds.Add(look); }
                look.pattern = pattern; look.shade = shade; look.accent = accent; look.ramp = ramp;
                look.edge = edge; look.bank = bank ?? Array.Empty<int>(); look.petal = -1;
            }
            void CliffStyle(string theme, WallPattern pattern, int accent, int lip, params int[] ramp)
            {
                var look = s.cliffs.Find(c => c.theme == theme);
                if (look == null) { look = new CliffLook { theme = theme }; s.cliffs.Add(look); }
                look.wall = pattern; look.accent = accent; look.lip = lip; look.ramp = ramp;
            }

            GroundStyle(Ground.DarkGrass, TilePattern.Moss, 39, 19, new[] { 39, 38, 40, 13 });
            GroundStyle(Ground.Swamp, TilePattern.Puddles, 42, 43, new[] { 42, 41, 43, 38 });
            GroundStyle(Ground.Toxic, TilePattern.Puddles, 39, 63, new[] { 39, 62, 43, 63 }, 43, new[] { 42, 17 });
            GroundStyle(Ground.Snow, TilePattern.Snowdrifts, 44, -1, new[] { 44, 45, 46, 49 });
            GroundStyle(Ground.Ice, TilePattern.Cracks, 47, -1, new[] { 47, 48, 49, 46 });
            GroundStyle(Ground.Sand, TilePattern.Ripples, 19, -1, new[] { 19, 32, 52, 10 });
            GroundStyle(Ground.Redrock, TilePattern.Sandstone, 50, 19, new[] { 50, 51, 52, 32 });
            GroundStyle(Ground.Crystal, TilePattern.CrystalBed, 64, 49, new[] { 64, 54, 65, 31 });
            GroundStyle(Ground.Flagstone, TilePattern.Slabs, 59, 40, new[] { 59, 60, 61, 7 });
            GroundStyle(Ground.Plaza, TilePattern.Cobbles, 59, -1, new[] { 59, 60, 61, 20 });
            GroundStyle(Ground.Mirror, TilePattern.MirrorPaving, 56, 49, new[] { 56, 57, 58, 55 });
            // Common banks and paths use the same chunky clusters, with no bright continuous foam band.
            GroundStyle(Ground.Water, TilePattern.Ripples, 25, -1, new[] { 25, 26, 27, 28 }, 25, new[] { 17, 18, 19 });
            GroundStyle(Ground.DeepWater, TilePattern.Ripples, 25, -1, new[] { 25, 25, 26, 27 }, 25, new[] { 17, 18, 19 });

            CliffStyle("overgrowth", WallPattern.Roots, 40, -1, 39, 2, 5, 18);
            CliffStyle("swamp", WallPattern.Roots, 43, 43, 39, 42, 17, 41);
            CliffStyle("glacier", WallPattern.Facets, 46, 46, 47, 48, 49, 45);
            CliffStyle("canyon", WallPattern.Strata, 19, 52, 17, 50, 51, 52);
            CliffStyle("crystal", WallPattern.Facets, 49, 55, 29, 53, 54, 55);
            CliffStyle("fortress", WallPattern.Bricks, 40, 61, 2, 59, 60, 61);
            CliffStyle("mirror", WallPattern.Facets, 49, 58, 0, 56, 57, 58);

            // Surface materials only: preserve theme identities, enemies, terrain height/noise and passability.
            SetThemeSurface("fortress", Ground.Flagstone, Ground.Grass);
            SetThemeSurface("crystal", Ground.Crystal, Ground.Flagstone);
        }

        static void SetThemeSurface(string id, Ground ground, Ground alternate)
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeData>("Assets/_Project/Data/Themes/" + id + ".asset");
            theme.def.ground = ground; theme.def.groundAlt = alternate;
            EditorUtility.SetDirty(theme);
        }
    }
}
