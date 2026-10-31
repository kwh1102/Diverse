using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse.EditorTools
{
    /// <summary>First reference-art review slice. Source sheets stay outside Assets; PNG GUIDs stay intact.</summary>
    public static class ReferenceArtImport
    {
        const string Source = "ArtSource/ReferenceRedesign/";
        const string Sprites = "Assets/_Project/Resources/Sprites/";
        static readonly HashSet<string> managed = new HashSet<string>();
        static Color32[] palette;
        static readonly string[] poses = { "Idle0", "Idle1", "Run0", "Run1", "Run2", "Run3", "Hurt", "Cast", "AttackWind", "AttackHit" };

        public static bool IsManaged(string key)
        {
            if (managed.Count == 0 && File.Exists(Source + "managed.txt"))
                foreach (var line in File.ReadAllLines(Source + "managed.txt")) managed.Add(line.Trim());
            return managed.Contains(key);
        }

        [MenuItem("Diverse/Art/Apply Reference Redesign (first review)")]
        public static void Apply()
        {
            Style();
            Art.ResetLooks(); TileBaker.Reset();
            var hero = Load("hero");
            for (int i = 0; i < poses.Length; i++)
                Export(hero, Grid(hero, i, 5, 2), "rabbit_white_" + poses[i], 27, 32, 32);
            UnityEngine.Object.DestroyImmediate(hero);

            var enemies = Load("enemies");
            // Native animation contract: idle, walk, windup, hurt, alternate walk.
            int[] frames = { 0, 1, 2, 4, 3 };
            for (int i = 0; i < 5; i++)
            {
                Export(enemies, Grid(enemies, frames[i], 5, 2), "enemy_jelly_" + i, i == 1 ? 15 : i == 2 ? 10 : 12, 26, 20);
                Export(enemies, Grid(enemies, 5 + frames[i], 5, 2), "enemy_wisp_" + i, i == 1 ? 27 : i == 2 ? 20 : 24, 32, 32);
            }
            UnityEngine.Object.DestroyImmediate(enemies);

            var objects = Load("objects");
            // Hand-checked source rectangles: generative sheets are not assumed to obey a perfect grid.
            Rect Box(float x, float y, float w, float h) => new Rect(x * objects.width, y * objects.height, w * objects.width, h * objects.height);
            for (int i = 0; i < 4; i++)
            {
                Export(objects, Box(i % 2 == 0 ? 0 : .30f, 0, i % 2 == 0 ? .29f : .21f, .35f), "tree_Meadow_" + i, 40);
                if (i < 2) Export(objects, Box(.54f, .13f, .17f, .19f), "bush_Meadow_" + i, 16);
                Export(objects, Box(.54f, .35f, .19f, .32f), "tree_Ashland_" + i, 38);
            }
            for (int i = 0; i < 3; i++)
            {
                Export(objects, Box(.80f, .17f, .14f, .14f), "rock_Meadow_" + i, 10 + i * 2);
                Export(objects, Box(.76f, .45f, .235f, .205f), "rock_Ashland_" + i, 18 + i * 2);
            }
            for (int i = 0; i < 2; i++) Export(objects, Box(.76f, .45f, .235f, .205f), "bush_Ashland_" + i, 12 + i * 2);
            Export(objects, Box(.06f, .40f, .15f, .23f), "prop_board", 22);
            for (int i = 0; i < 5; i++) Export(objects, Box(.33f, .44f, .13f, .18f), "flower_" + i, 9 + i % 2);
            Export(objects, Box(.015f, .70f, .31f, .27f), "prop_reference_bridge", 26);
            Export(objects, Box(.34f, .70f, .27f, .29f), "prop_reference_pond", 34);
            Export(objects, Box(.64f, .76f, .12f, .18f), "prop_chest", 17);
            Export(objects, Box(.82f, .71f, .17f, .26f), "prop_campfire", 21);
            for (int i = 0; i < 4; i++)
                Export(objects, i % 2 == 0 ? Box(.54f, .35f, .19f, .32f) : Box(.76f, .45f, .235f, .205f), "deco_volcano_" + i, i % 2 == 0 ? 34 : 22);
            UnityEngine.Object.DestroyImmediate(objects);
            File.WriteAllLines(Source + "managed.txt", managed.OrderBy(k => k));
            SkinUI();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[ReferenceArt] Imported " + managed.Count + " reference sprites; art style and both UI prefabs updated.");
        }

        public static void RunBatch()
        {
            try { Apply(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static Texture2D Load(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(Source + file + ".png"))) throw new IOException(file);
            return t;
        }

        // Rectangles use top-left source coordinates, matching image editors.
        static Rect Grid(Texture2D t, int index, int cols, int rows) =>
            new Rect(index % cols * t.width / (float)cols, index / cols * t.height / (float)rows, t.width / (float)cols, t.height / (float)rows);

        static void Export(Texture2D source, Rect cell, string key, int height, int canvasW = 0, int canvasH = 0)
        {
            var px = source.GetPixels32();
            int x0 = Mathf.Clamp(Mathf.CeilToInt(cell.xMin), 0, source.width - 1), x1 = Mathf.Min(source.width, Mathf.FloorToInt(cell.xMax));
            int y0 = Mathf.Max(0, source.height - Mathf.FloorToInt(cell.yMax)), y1 = Mathf.Min(source.height, source.height - Mathf.CeilToInt(cell.yMin));
            int left = x1, right = x0, bottom = y1, top = y0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
                if (px[y * source.width + x].a >= 180)
                { left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
            if (right < left || top < bottom) throw new InvalidDataException("Empty reference cell: " + key);
            int sw = right - left + 1, sh = top - bottom + 1;
            int dw = Mathf.Max(2, Mathf.RoundToInt(sw * height / (float)sh)), dh = height;
            if (canvasW > 0 && dw > canvasW - 2) { dh = Mathf.RoundToInt(dh * (canvasW - 2f) / dw); dw = canvasW - 2; }
            int w = canvasW > 0 ? canvasW : dw + 2, h = canvasH > 0 ? canvasH : dh + 2;
            var output = new Color32[w * h];
            // Area majority in palette space removes resampling noise without inventing intermediate colours.
            for (int y = 0; y < dh; y++) for (int x = 0; x < dw; x++)
            {
                int ax = left + x * sw / dw, bx = left + (x + 1) * sw / dw;
                int ay = bottom + y * sh / dh, by = bottom + (y + 1) * sh / dh;
                var votes = new int[palette.Length]; int opaque = 0, count = 0;
                for (int sy = ay; sy < by; sy++) for (int sx = ax; sx < bx; sx++)
                {
                    count++; var c = px[sy * source.width + sx];
                    if (c.a < 180) continue;
                    votes[Snap(c)]++; opaque++;
                }
                if (count == 0 || opaque < count * .42f) continue;
                int best = 0; for (int k = 1; k < votes.Length; k++) if (votes[k] > votes[best]) best = k;
                output[(y + 1) * w + x + (w - dw) / 2] = palette[best];
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.SetPixels32(output); tex.Apply();
            string path = Sprites + key + ".png";
            var bytes = tex.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(tex);
            // Avoid needless writes and release stale editor texture mappings on Windows before a retry.
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
                for (int attempt = 0; ; attempt++)
                {
                    try { File.WriteAllBytes(path, bytes); break; }
                    catch (IOException) when (attempt < 8)
                    {
                        EditorUtility.UnloadUnusedAssetsImmediate();
                        GC.Collect(); System.Threading.Thread.Sleep(100);
                    }
                }
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false; importer.isReadable = true; importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = Art.PPU;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = new Vector2(.5f, 1f / h);
            importer.SetTextureSettings(settings); importer.SaveAndReimport();
            managed.Add(key);
        }

        static void Style()
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtStyleData>("Assets/_Project/Data/ArtStyle.asset");
            var s = data.def;
            // Keep index meanings intact for existing themes and procedural art.
            string[] colors = { "242633", "2a2b36", "34343f", "3d3c46", "48464e", "4b4b55", "5b5a61", "6c6462", "94867c", "c6b793", "e1cc99", "194e41", "347441", "598e40", "75a543", "85ae47", "acc44d", "564640", "7c6658", "a58b58", "c2a571", "ecd05e", "802c22", "cc3c16", "ff7417", "23587a", "2c86b4", "6bb3cb", "c8e4db", "4b3c60", "866493", "bea1c5", "dac18b", "3e3d47", "dda9bd", "af7086" };
            for (int i = 0; i < colors.Length && i < s.palette.Length; i++) s.palette[i] = Pal.Hex(colors[i]);
            palette = s.palette;
            var grass = s.grounds.Find(g => g.ground == Ground.Grass); grass.ramp = new[] { 13, 14, 15, 16 }; grass.shade = 13;
            var ash = s.grounds.Find(g => g.ground == Ground.Ash); ash.ramp = new[] { 33, 4, 5 }; ash.accent = -1;
            var lava = s.grounds.Find(g => g.ground == Ground.Lava); lava.ramp = new[] { 22, 23, 24, 21 }; lava.edge = 23;
            s.shadowAlpha = .32f; s.edgeNoise = .13f; s.edgeRim = .045f;
            s.cliffRise = 24; s.cliffSkew = 4; s.cornerRadius = 8; s.footShade = 3;
            var meadow = s.cliffs.Find(c => c.theme == "meadow"); meadow.ramp = new[] { 2, 5, 6, 7 }; meadow.accent = 18;
            var volcano = s.cliffs.Find(c => c.theme == "volcano"); volcano.ramp = new[] { 0, 1, 2, 3 }; volcano.lip = 19;
            var volcanicTheme = AssetDatabase.LoadAssetAtPath<ThemeData>("Assets/_Project/Data/Themes/volcano.asset");
            volcanicTheme.def.groundAlt = Ground.Ash;
            EditorUtility.SetDirty(volcanicTheme);
            s.prompt = "Match ArtSource/ReferenceRedesign/reference.png: chunky low-resolution top-down pixel art, compact beige rabbit with red scarf, round layered green trees, warm tan paths, vertical charcoal stone cliffs, flat olive grass, orange lava. Dark closed outlines, 2-4 flat tones, no gradients or painted microtexture.";
            EditorUtility.SetDirty(data);
            TerrainArtReview.Apply(); palette = data.def.palette; TileBaker.Reset();
        }

        // Importing must not initialize DB/Art.Hand: that loads every source sprite and locks PNGs on Windows.
        static int Snap(Color32 c)
        {
            int best = 0, distance = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                int r = c.r - palette[i].r, g = c.g - palette[i].g, b = c.b - palette[i].b;
                int d = r * r * 3 + g * g * 4 + b * b * 2;
                if (d < distance) { distance = d; best = i; }
            }
            return best;
        }

        static void SkinUI()
        {
            // Native nine-slice UI textures, using the same stone/tan materials as the reference bridge.
            UIBuilder.RedrawFrames();
            foreach (string name in new[] { "MenuCanvas", "GameCanvas" })
            {
                string path = "Assets/_Project/Prefabs/UI/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var t in root.GetComponentsInChildren<Text>(true))
                    {
                        t.color = Pal.Hex("e1cc99");
                        foreach (var shadow in t.GetComponents<Shadow>()) shadow.effectColor = new Color32(36, 38, 51, 220);
                    }
                    foreach (var button in root.GetComponentsInChildren<Button>(true))
                    {
                        var c = button.colors; c.normalColor = Color.white; c.highlightedColor = new Color32(255, 232, 174, 255);
                        c.selectedColor = c.highlightedColor; c.pressedColor = new Color32(173, 151, 112, 255); button.colors = c;
                    }
                    // Serialized HUD fills: material colours, kept in the prefab rather than layout code.
                    var hud = root.GetComponentInChildren<HudView>(true);
                    if (hud != null)
                    {
                        var so = new SerializedObject(hud);
                        void Fill(string field, string hex)
                        {
                            if (so.FindProperty(field)?.objectReferenceValue is Image img) img.color = Pal.Hex(hex);
                        }
                        Fill("hpFill", "cc3c16"); Fill("shieldFill", "6bb3cb"); Fill("xpFill", "acc44d"); Fill("bossFill", "cc3c16");
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
    }
}
