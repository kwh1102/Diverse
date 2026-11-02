using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static partial class WorldArtOverhaul
    {
        static void ImportResidents()
        {
            foreach (var group in new[] { ("villagers", "villager", 10, 5), ("wanderers", "wanderer", 6, 3) })
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.LoadImage(File.ReadAllBytes("ArtSource/Residents/" + group.Item1 + ".png"));
                for (int i = 0; i < group.Item3; i++)
                    for (int frame = 0; frame < 2; frame++)
                        Export(source, Grid(source, i, group.Item4, 2), $"npc_{group.Item2}{i}_{frame}", 25,
                            canvas: new Vector2Int(34, 30), frame: frame);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [MenuItem("Diverse/Art/Import Resident and Wanderer Art")]
        public static void ApplyResidents()
        {
            foreach (string key in File.ReadAllLines(Source + "managed.txt")) managed.Add(key.Trim());
            palette = DB.ArtStyle.palette.Take(66).ToArray(); nearest.Clear(); imports.Clear(); rowBands.Clear(); columnBands.Clear();
            ImportResidents();
            var town = Load("actors");
            string[] townsfolk = { "bear", "cat", "otter" };
            for (int i = 0; i < townsfolk.Length; i++) for (int frame = 0; frame < 2; frame++)
                Export(town,Grid(town,13+i,4,4),$"npc_{townsfolk[i]}_{frame}",24,canvas:new Vector2Int(36,30),frame:frame);
            UnityEngine.Object.DestroyImmediate(town);
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
            Art.ResetLooks(); AssetDatabase.SaveAssets();
            Debug.Log("[ResidentArt] Imported 10 residents and 6 wanderers, two idle frames each; retained existing GUIDs.");
        }

        public static void RunBatchResidents()
        {
            try { ApplyResidents(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
