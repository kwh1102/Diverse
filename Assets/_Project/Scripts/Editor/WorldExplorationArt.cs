using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static partial class WorldArtOverhaul
    {
        public static readonly string[] ExplorationThemes = { "meadow", "overgrowth", "volcano", "crystal", "glacier", "canyon", "fortress", "mirror", "swamp" };

        static Texture2D ExplorationSource(string name)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            image.LoadImage(File.ReadAllBytes("ArtSource/Exploration/" + name + ".png")); return image;
        }

        static void ImportExploration()
        {
            var props = ExplorationSource("props");
            int[] heights = { 18, 18, 23, 23, 22, 25, 18, 20, 22, 21, 22, 27, 25, 24, 28, 25,
                22, 18, 20, 17, 21, 23, 22, 28, 20, 30, 26, 21, 28, 25, 24, 20, 21, 22, 25, 28 };
            for (int row = 0; row < 9; row++) for (int col = 0; col < 4; col++)
                Export(props, Grid(props, row * 4 + col, 4, 9), $"explore_{ExplorationThemes[row]}_{col}", heights[row * 4 + col]);
            Object.DestroyImmediate(props);
            var anchors = ExplorationSource("anchors");
            for (int i = 0; i < 9; i++) Export(anchors, Grid(anchors, i, 3, 3), "explore_anchor_" + ExplorationThemes[i], 72);
            Object.DestroyImmediate(anchors);
            var states = ExplorationSource("states");
            foreach (string guid in AssetDatabase.FindAssets("t:CostumeData", new[] { "Assets/_Project/Data/Costumes" }))
            {
                var c = AssetDatabase.LoadAssetAtPath<CostumeData>(AssetDatabase.GUIDToAssetPath(guid)).def;
                for (int pose = 0; pose < 2; pose++)
                    Export(states, Grid(states, pose, 4, 1), "rabbit_" + c.id + "_Idle" + pose, 28,
                        canvas: new Vector2Int(36, 34), costume: c.id == "white" ? null : c);
            }
            Export(states, Grid(states, 2, 4, 1), "prop_chest", 15, nativeWidth: 22);
            Export(states, Grid(states, 3, 4, 1), "prop_chest_open", 23, nativeWidth: 22);
            ImportResidents();
            Object.DestroyImmediate(states);
        }

        [MenuItem("Diverse/Art/Import Exploration Art and Corrected States")]
        public static void ApplyExploration()
        {
            foreach (string key in File.ReadAllLines(Source + "managed.txt")) managed.Add(key.Trim());
            palette = DB.ArtStyle.palette.Take(66).ToArray(); nearest.Clear(); imports.Clear(); rowBands.Clear(); columnBands.Clear();
            ImportExploration();
            File.WriteAllLines(Source + "managed.txt", managed.OrderBy(k => k)); AssetDatabase.Refresh();
            foreach (var item in imports)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(item.Key);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = Art.PPU; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = true;
                importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = item.Value;
                settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
            Art.ResetLooks(); AssetDatabase.SaveAssets(); Debug.Log("[ExplorationArt] Imported 45 new props/anchors and corrected idle/chest states, retained existing GUIDs.");
        }
    }
}
