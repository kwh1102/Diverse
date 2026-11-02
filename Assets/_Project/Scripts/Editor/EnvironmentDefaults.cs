using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    public static class EnvironmentDefaults
    {
        const string Source = "ArtSource/Environment/effects-source.png";
        const string Atlas = "Assets/_Project/Resources/Environment/ambient.png";

        [MenuItem("Diverse/World/Apply Environment Animation")]
        public static void Apply()
        {
            ImportSheet();
            var asset = AssetDatabase.LoadAssetAtPath<WorldRulesData>("Assets/_Project/Data/WorldRules.asset");
            var settings = asset.def.environment = new EnvironmentDef { enabled = true };
            void Rule(string key, EnvironmentMotion motion, EnvironmentEffect effect = EnvironmentEffect.None,
                float y = 0, float scale = 1, float interval = 4)
                => settings.objects.Add(new EnvironmentObjectDef { sprite = key, motion = motion, effect = effect,
                    offset = new Vector2(0, y), scale = scale, interval = interval });
            Rule("tree_Dunes_*", EnvironmentMotion.Still);
            Rule("tree_Crystal_*", EnvironmentMotion.Shimmer);
            Rule("tree_*", EnvironmentMotion.Foliage);
            Rule("bush_Tundra_*", EnvironmentMotion.Shimmer);
            Rule("bush_Crystal_*", EnvironmentMotion.Shimmer);
            Rule("bush_*", EnvironmentMotion.Foliage);
            Rule("flower_*", EnvironmentMotion.Foliage);
            Rule("prop_banner", EnvironmentMotion.Cloth); Rule("explore_fortress_1", EnvironmentMotion.Cloth);
            Rule("explore_canyon_3", EnvironmentMotion.Cloth);
            Rule("prop_campfire", EnvironmentMotion.Still, EnvironmentEffect.Flame, .2f, .7f, 2);
            Rule("prop_lamp", EnvironmentMotion.Shimmer);
            Rule("prop_warpstone", EnvironmentMotion.Shimmer, EnvironmentEffect.Portal, 1.1f, .6f, .3f);
            Rule("explore_volcano_0", EnvironmentMotion.Shimmer, EnvironmentEffect.Smoke, 1, .65f, 2.5f);
            Rule("explore_volcano_3", EnvironmentMotion.Shimmer, EnvironmentEffect.Flame, .35f, .6f, 2);
            Rule("explore_overgrowth_0", EnvironmentMotion.Foliage); Rule("explore_overgrowth_1", EnvironmentMotion.Foliage);
            Rule("explore_swamp_2", EnvironmentMotion.Foliage);
            Rule("explore_glacier_2", EnvironmentMotion.Foliage);
            Rule("explore_crystal_*", EnvironmentMotion.Shimmer);
            Rule("explore_mirror_0", EnvironmentMotion.Shimmer); Rule("explore_mirror_2", EnvironmentMotion.Shimmer);
            Rule("explore_mirror_3", EnvironmentMotion.Shimmer);
            for (int v = 0; v < 4; v++)
            {
                Rule("deco_overgrowth_" + v, EnvironmentMotion.Foliage); Rule("deco_swamp_" + v, EnvironmentMotion.Foliage);
                Rule("deco_glacier_" + v, v % 2 == 0 ? EnvironmentMotion.Foliage : EnvironmentMotion.Shimmer);
                Rule("deco_crystal_" + v, EnvironmentMotion.Shimmer); Rule("deco_mirror_" + v, EnvironmentMotion.Shimmer);
                Rule("deco_volcano_" + v, v % 2 == 0 ? EnvironmentMotion.Foliage : EnvironmentMotion.Shimmer);
            }
            foreach (var theme in DB.Themes)
            {
                var profile = theme.environment = new EnvironmentThemeDef();
                switch (theme.id)
                {
                    case "overgrowth": profile.wind = 1.2f; profile.dust = new Color(.6f, .75f, .3f, .35f); profile.weatherVelocity = new Vector2(.35f, -.3f); break;
                    case "volcano": profile.wind = .55f; profile.accent = new Color(1, .55f, .15f, 1); profile.dust = new Color(.7f, .55f, .4f, .3f); profile.weatherRate = 2; profile.weatherVelocity = new Vector2(.2f, .25f); break;
                    case "crystal": profile.wind = .7f; profile.accent = new Color(.5f, .85f, 1, 1); profile.dust = new Color(.5f, .8f, 1, .4f); profile.weatherRate = .7f; profile.weatherVelocity = new Vector2(.05f, .15f); break;
                    case "glacier": profile.wind = .8f; profile.accent = new Color(.75f, .9f, 1, 1); profile.dust = new Color(.9f, .95f, 1, .5f); profile.weatherRate = 4; profile.weatherSize = .055f; profile.weatherVelocity = new Vector2(.5f, -.4f); break;
                    case "canyon": profile.wind = .25f; profile.dust = new Color(.8f, .63f, .36f, .3f); profile.weatherRate = 1.5f; profile.weatherVelocity = new Vector2(.6f, .05f); break;
                    case "fortress": profile.wind = .85f; profile.dust = new Color(.7f, .7f, .6f, .22f); profile.weatherRate = .5f; break;
                    case "mirror": profile.wind = .6f; profile.accent = new Color(.7f, .6f, 1, 1); profile.dust = new Color(.6f, .65f, 1, .3f); profile.weatherRate = .7f; profile.weatherVelocity = new Vector2(.08f, .1f); break;
                    case "swamp": profile.wind = 1.1f; profile.accent = new Color(.65f, .85f, .4f, 1); profile.dust = new Color(.6f, .7f, .5f, .1f); profile.weatherRate = .6f; profile.weatherSize = .4f; profile.weatherVelocity = new Vector2(.16f, 0); break;
                }
                var data = AssetDatabase.LoadAssetAtPath<ThemeData>("Assets/_Project/Data/Themes/" + theme.id + ".asset");
                EditorUtility.SetDirty(data);
            }
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
            Debug.Log("[Environment] Installed identity-preserving motion in nine themes, shared 128x128 sheet and capped native particles.");
        }

        public static void RunBatch()
        {
            try { Apply(); EnvironmentChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void ImportSheet()
        {
            var source = new Texture2D(2, 2); source.LoadImage(File.ReadAllBytes(Source));
            var input = source.GetPixels32(); var output = new Color32[128 * 128]; var palette = DB.ArtStyle.palette;
            var snapped = new Dictionary<uint, Color32>();
            for (int row = 0; row < 4; row++)
            {
                var boxes = new RectInt[4]; int maxWidth = 1, maxHeight = 1;
                for (int frame = 0; frame < 4; frame++)
                {
                    int x0 = frame * source.width / 4, x1 = (frame + 1) * source.width / 4;
                    int y0 = (3 - row) * source.height / 4, y1 = (4 - row) * source.height / 4;
                    int left = x1, right = x0, bottom = y1, top = y0;
                    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (input[y * source.width + x].a >= 192)
                    { left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
                    if (right <= left || top <= bottom) throw new InvalidDataException("Empty effect frame " + row + "/" + frame);
                    boxes[frame] = new RectInt(left, bottom, right - left + 1, top - bottom + 1);
                    maxWidth = Mathf.Max(maxWidth, right - left + 1); maxHeight = Mathf.Max(maxHeight, top - bottom + 1);
                }
                float ratio = (row == 3 ? 24f : 20f) / Mathf.Max(maxWidth, maxHeight);
                for (int frame = 0; frame < 4; frame++)
                {
                    var box = boxes[frame]; int w = Mathf.Max(1, Mathf.RoundToInt(box.width * ratio)), h = Mathf.Max(1, Mathf.RoundToInt(box.height * ratio));
                    int dx = frame * 32 + (32 - w) / 2, dy = (3 - row) * 32 + (row == 3 ? (32 - h) / 2 : 1);
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    {
                        var color = input[(box.y + Mathf.Min(box.height - 1, Mathf.FloorToInt((y + .5f) / ratio))) * source.width +
                            box.x + Mathf.Min(box.width - 1, Mathf.FloorToInt((x + .5f) / ratio))];
                        if (color.a < 192) continue;
                        uint key = (uint)(color.r << 16 | color.g << 8 | color.b);
                        if (!snapped.TryGetValue(key, out var native))
                        {
                            int distance = int.MaxValue; native = palette[0];
                            foreach (var p in palette)
                            {
                                int r = color.r - p.r, g = color.g - p.g, b = color.b - p.b;
                                int d = r * r * 3 + g * g * 4 + b * b * 2;
                                if (d < distance) { distance = d; native = p; }
                            }
                            native.a = 255; snapped[key] = native;
                        }
                        output[(dy + y) * 128 + dx + x] = native;
                    }
                }
            }
            var atlas = new Texture2D(128, 128, TextureFormat.RGBA32, false); atlas.SetPixels32(output); atlas.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Atlas)); File.WriteAllBytes(Atlas, atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(Atlas, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Atlas);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Art.PPU; importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.alphaIsTransparency = true; importer.SaveAndReimport();
        }
    }
}
