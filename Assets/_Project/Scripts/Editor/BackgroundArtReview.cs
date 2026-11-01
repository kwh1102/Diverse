using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Background-only art configuration. Does not import or modify any actor/prop sprites.</summary>
    public static class BackgroundArtReview
    {
        public static void Configure(ArtStyleDef s)
        {
            var colors = new List<Color32>(s.palette); var names = new List<string>(s.paletteNames);
            while (names.Count < colors.Count) names.Add("palette " + names.Count);
            int Color(string name, string hex)
            {
                name = "surface " + name;
                int index = names.IndexOf(name);
                if (index < 0) { index = colors.Count; colors.Add(Pal.Hex(hex)); names.Add(name); }
                else colors[index] = Pal.Hex(hex);
                return index;
            }
            void Tones(Ground ground, string shade, string light)
            {
                var look = s.grounds.Find(g => g.ground == ground);
                if (look == null) return;
                look.undertone = Color(ground + " soft shade", shade);
                look.overtone = Color(ground + " soft light", light);
            }
            Tones(Ground.Grass, "638556", "779761");
            Tones(Ground.DarkGrass, "48694e", "5f805c");
            Tones(Ground.Path, "b89a69", "ceb17b");
            Tones(Ground.Sand, "ceb582", "e7ce9e");
            Tones(Ground.Snow, "d0dce0", "eef0e1");
            Tones(Ground.Swamp, "515a40", "717858");
            Tones(Ground.Ash, "474b55", "5c6069");
            Tones(Ground.Plaza, "7b8780", "939d91");
            Tones(Ground.Flagstone, "7b8780", "939d91");
            Tones(Ground.Water, "48839a", "6ba9b7");
            Tones(Ground.DeepWater, "30596e", "447b94");
            Tones(Ground.Wood, "94734f", "b59363");
            Tones(Ground.Ice, "7eacbf", "b1d2d8");
            Tones(Ground.Crystal, "706a8c", "8e83a2");
            Tones(Ground.Mirror, "484859", "5b5a73");
            Tones(Ground.Redrock, "aa7b60", "c99b76");
            Tones(Ground.Toxic, "4b6749", "809b5d");
            Tones(Ground.Lava, "ca6534", "f5b251");
            foreach (var g in s.grounds)
            {
                if (g.ground is Ground.Plaza or Ground.Flagstone)
                    g.ramp = new[] { Color("paving mortar", "5d6d66"), 60, Color("paving bevel", "a3ab99") };
                if (g.ground == Ground.Grass) { g.accent = 21; g.petal = 10; }
                if (g.ground == Ground.Wood) { g.ramp = new[] { 17, 19, 20 }; g.shade = 18; }
            }
            s.palette = colors.ToArray(); s.paletteNames = names.ToArray();
            s.groundDetail = .62f; s.groundFringe = 5; s.shoreWidth = 6; s.surfacePatchSize = 38;
        }

        [MenuItem("Diverse/Art/Apply Background Material Pass")]
        public static void Apply()
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtStyleData>("Assets/_Project/Data/ArtStyle.asset");
            Configure(data.def); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            TileBaker.Reset(); Art.ResetLooks();
            Debug.Log("[BackgroundArt] Applied clustered surfaces, material fringes, shallow water and bridge details. Actor sprites unchanged.");
        }

        public static void RunBatch()
        {
            try { Apply(); WorldVisualChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
