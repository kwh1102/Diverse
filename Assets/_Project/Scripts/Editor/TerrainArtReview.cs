using System;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class TerrainArtReview
    {
        [MenuItem("Diverse/Art/Apply Terrain Depth Review")]
        public static void Apply()
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtStyleData>("Assets/_Project/Data/ArtStyle.asset");
            var s = data.def;
            s.cliffRise = 24; s.cliffSkew = 4; s.cornerRadius = 8; s.footShade = 3;
            s.edgeNoise = .13f; s.edgeRim = .045f;
            s.palette[4] = Pal.Hex("403e46"); s.palette[5] = Pal.Hex("484850");
            if (s.palette.Length < 38)
            {
                Array.Resize(ref s.palette, 38); Array.Resize(ref s.paletteNames, 38);
            }
            s.palette[36] = Pal.Hex("34343e"); s.paletteNames[36] = "basalt fissure";
            s.palette[37] = Pal.Hex("47454e"); s.paletteNames[37] = "basalt worn face";
            var ash = s.grounds.Find(g => g.ground == Ground.Ash);
            ash.shade = 36; ash.ramp = new[] { 36, 4, 37 }; ash.accent = -1;
            var volcano = s.cliffs.Find(c => c.theme == "volcano");
            volcano.ramp = new[] { 0, 1, 2, 3 }; volcano.lip = 19;
            BiomeTerrainStyle.Configure(s);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); TileBaker.Reset();
            Debug.Log("[TerrainArtReview] Connected contours, 24px rise / 4px oblique return, basalt ground applied.");
        }

        public static void RunBatch()
        {
            try { Apply(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
