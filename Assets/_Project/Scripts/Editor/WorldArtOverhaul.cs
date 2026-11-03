using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>Reproducible production import. Source sheets stay outside Resources; existing GUIDs are retained.</summary>
    public static partial class WorldArtOverhaul
    {
        public const string Source = "ArtSource/WorldOverhaul/";
        const string Output = "Assets/_Project/Resources/Sprites/";
        static readonly HashSet<string> managed = new HashSet<string>();
        static readonly Dictionary<string, Vector2> imports = new Dictionary<string, Vector2>();
        static Color32[] palette;
        static readonly Dictionary<int, int> nearest = new Dictionary<int, int>();
        static Texture2D sampledSource;
        static Color32[] sourcePixels;
        static readonly Dictionary<Texture2D, float[]> rowBands = new Dictionary<Texture2D, float[]>();
        static readonly Dictionary<(Texture2D, int), float[]> columnBands = new Dictionary<(Texture2D, int), float[]>();

        public static bool IsManaged(string key)
        {
            if (managed.Count == 0 && File.Exists(Source + "managed.txt"))
                foreach (string line in File.ReadAllLines(Source + "managed.txt")) managed.Add(line.Trim());
            return managed.Contains(key);
        }

        [MenuItem("Diverse/Art/Apply Cute World Overhaul")]
        public static void Apply()
        {
            Configure();
            imports.Clear(); rowBands.Clear(); columnBands.Clear();
            ImportScenery(); ImportActors(); ImportHero(); ImportProps(); ImportLandmarks();
            if (File.Exists("ArtSource/Exploration/props.png")) ImportExploration();
            File.WriteAllLines(Source + "managed.txt", managed.OrderBy(x => x));
            AssetDatabase.Refresh();
            foreach (var item in imports)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(item.Key);
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = Art.PPU; imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false; imp.isReadable = true; imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                var settings = new TextureImporterSettings(); imp.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom; settings.spritePivot = item.Value;
                settings.spriteMeshType = SpriteMeshType.FullRect; imp.SetTextureSettings(settings); imp.SaveAndReimport();
            }
            Art.ResetLooks(); TileBaker.Reset(); AssetDatabase.SaveAssets();
            Debug.Log("[WorldOverhaul] Imported " + imports.Count + " native pixel sprites, preserved GUIDs, configured all 9 regions.");
        }

        public static void Configure()
        {
            var data = AssetDatabase.LoadAssetAtPath<ArtStyleData>("Assets/_Project/Data/ArtStyle.asset");
            var s = data.def;
            BiomeTerrainStyle.Configure(s);
            void ColorAt(int i, string hex) => s.palette[i] = Pal.Hex(hex);
            string[] common = { "28313b", "303a43", "3c454d", "49515a", "50575f", "5b626b", "737b83", "979c9e", "b5b5aa", "e1d2ae", "f7efd7",
                "2c5449", "4e7953", "6b8d5b", "86a65f", "a5c372", "c8dc8b", "57483f", "88694e", "ac875a", "c3a56e", "efd37a",
                "914137", "d65b40", "f89a42", "346078", "4f91ad", "8ec8d1", "d5ecdf", "49445e", "80709b", "b8a4cc", "dec491", "48505a", "e5b6c0", "b987a1" };
            for (int i = 0; i < common.Length; i++) ColorAt(i, common[i]);
            ColorAt(38, "527458"); ColorAt(39, "45644d"); ColorAt(40, "6d8e60");
            ColorAt(59, "727e78"); ColorAt(60, "87928a"); ColorAt(61, "98a095");
            s.cliffSkew = 0; s.cliffRise = 16; s.cornerRadius = 3; s.groundCorner = 3;
            s.edgeNoise = 0; s.edgeRim = .035f; s.groundDetail = .24f; s.footShade = 2; s.shadowAlpha = .23f;
            var grass = s.grounds.Find(g => g.ground == Ground.Grass);
            grass.ramp = new[] { 12, 13, 14, 16 }; grass.shade = 12; grass.accent = -1;
            var path = s.grounds.Find(g => g.ground == Ground.Path);
            path.ramp = new[] { 19, 20, 32 }; path.shade = 19;
            foreach (var g in s.grounds.Where(g => g.ground is Ground.Plaza or Ground.Flagstone))
            { g.ramp = new[] { 59, 60, 61 }; g.shade = 59; g.accent = -1; }
            foreach (var c in s.cliffs)
            {
                if (c.theme is "meadow" or "overgrowth" or "fortress")
                { c.wall = WallPattern.Earth; c.ramp = new[] { 17, 18, 19, 20 }; c.lip = -1; c.accent = -1; }
            }
            s.prompt = "Cute Sephiria-inspired low-resolution pixel art; puffy silhouettes; large flat pixel clusters; 3-4 material tones; upper-left light; charcoal one-pixel outline; straight RPG camera; no realism, gradients, dithering or fine texture.";
            BackgroundArtReview.Configure(s);
            // The five approved source atlases were imported against these 66 colours. Extra background
            // intermediates must not recolour actors when this importer is run again.
            palette = s.palette.Take(66).ToArray(); EditorUtility.SetDirty(data);
            nearest.Clear();

            var rules = AssetDatabase.LoadAssetAtPath<WorldRulesData>("Assets/_Project/Data/WorldRules.asset");
            rules.def.terrainStep = 2; rules.def.cliffLevel = .845f; rules.def.peakLevel = .95f;
            rules.def.rangeMaskThreshold = .405f; rules.def.rangeFrequency = .008f;
            rules.def.roadWidth = 2.0f; rules.def.sceneryCell = 3; rules.def.sceneryGap = .35f; rules.def.sceneryRoadMargin = 1.2f;
            EditorUtility.SetDirty(rules);
            foreach (string pathName in AssetDatabase.FindAssets("t:ThemeData", new[] { "Assets/_Project/Data/Themes" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ThemeData>(AssetDatabase.GUIDToAssetPath(pathName)); var t = asset.def;
                t.cliffTop = t.ground;
                if (t.id == "fortress") { t.cliffTop = Ground.Grass; t.altShare = .14f; }
                if (t.id == "crystal") { t.groundAlt = Ground.Crystal; t.altShare = 0; }
                if (t.id == "meadow") t.altShare = .13f;
                if (t.id == "overgrowth") { t.cliffTop = Ground.DarkGrass; t.altShare = .2f; }
                foreach (var d in t.decor)
                {
                    if (d.sprite.StartsWith("bush_")) d.chance = .018f;
                    if (d.sprite.StartsWith("rock_")) d.chance = .011f;
                    if (d.sprite.StartsWith("tree_")) d.chance = t.id is "overgrowth" or "swamp" ? .06f : .043f;
                }
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets(); TileBaker.Reset();
        }

        static Texture2D Load(string name)
        {
            string path = Source + name + ".png";
            if (!File.Exists(path)) throw new FileNotFoundException("Missing production source atlas", path);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(path)); return t;
        }

        static Rect Grid(Texture2D t, int index, int columns, int rows)
        {
            if (!rowBands.TryGetValue(t, out var bands))
            {
                bands = new float[rows + 1]; bands[rows] = 1;
                var pixels = t.GetPixels32(); var occupancy = new int[t.height];
                for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++)
                    if (pixels[(t.height - 1 - y) * t.width + x].a >= 200) occupancy[y]++;
                // Generative sheets have equal columns but not exact row boundaries. Locate the real empty gutters
                // near each expected separator rather than slicing feet/roofs off at a nominal grid line.
                for (int r = 1; r < rows; r++)
                {
                    int center = t.height * r / rows, best = center; float score = float.MaxValue;
                    int reach = t.height / (rows * 3);
                    for (int y = center - reach; y <= center + reach; y++)
                    {
                        if (y < 3 || y >= t.height - 3) continue;
                        int sum = 0; for (int d = -3; d <= 3; d++) sum += occupancy[y + d];
                        float candidate = sum + Mathf.Abs(y - center) * .001f;
                        if (candidate < score) { score = candidate; best = y; }
                    }
                    bands[r] = best / (float)t.height;
                }
                rowBands[t] = bands;
            }
            int rowIndex = index / columns;
            if (!columnBands.TryGetValue((t, rowIndex), out var edges))
            {
                edges = new float[columns + 1]; edges[columns] = 1;
                var pixels = t.GetPixels32(); var occupancy = new int[t.width];
                int top = Mathf.RoundToInt(bands[rowIndex] * t.height), bottom = Mathf.RoundToInt(bands[rowIndex + 1] * t.height);
                for (int y = top; y < bottom; y++) for (int x = 0; x < t.width; x++)
                    if (pixels[(t.height - 1 - y) * t.width + x].a >= 200) occupancy[x]++;
                for (int c = 1; c < columns; c++)
                {
                    int center = t.width * c / columns, best = center; float score = float.MaxValue;
                    int reach = t.width / (columns * 3);
                    for (int x = center - reach; x <= center + reach; x++)
                    {
                        if (x < 3 || x >= t.width - 3) continue;
                        int sum = 0; for (int d = -3; d <= 3; d++) sum += occupancy[x + d];
                        float candidate = sum + Mathf.Abs(x - center) * .001f;
                        if (candidate < score) { score = candidate; best = x; }
                    }
                    edges[c] = best / (float)t.width;
                }
                columnBands[(t, rowIndex)] = edges;
            }
            int colIndex = index % columns;
            return new Rect(edges[colIndex] * t.width, bands[rowIndex] * t.height,
                (edges[colIndex + 1] - edges[colIndex]) * t.width, (bands[rowIndex + 1] - bands[rowIndex]) * t.height);
        }

        static void ImportScenery()
        {
            var t = Load("scenery");
            int[] trees = { 0, 1, 6, 4, 5, 3, 7, 2, 0 };
            foreach (Biome b in Enum.GetValues(typeof(Biome)))
            {
                int index = trees[(int)b];
                for (int v = 0; v < 4; v++)
                    Export(t, Grid(t, index, 4, 4), $"tree_{b}_{v}", 38 + v % 2 * 2, variant: v, tint: b == Biome.Mirror ? "mirror" : null);
                int bush = b switch { Biome.Forest => 1, Biome.Marsh => 2, Biome.Tundra => 3, Biome.Crystal => 3, _ => 0 };
                for (int v = 0; v < 2; v++)
                    Export(t, Grid(t, bush + 8, 4, 4), $"bush_{b}_{v}", 11 + v * 2, variant: v, tint: b == Biome.Mirror ? "mirror" : b == Biome.Ashland ? "ash" : null);
                int rock = b switch { Biome.Ashland => 2, Biome.Crystal or Biome.Mirror => 3, Biome.Tundra => 1, _ => 0 };
                for (int v = 0; v < 3; v++) Export(t, Grid(t, rock + 12, 4, 4), $"rock_{b}_{v}", 9 + v * 2, variant: v);
            }
            // Regional specials retain their own identity, but share the new simple silhouette vocabulary.
            foreach (string theme in new[] { "overgrowth", "swamp", "volcano", "glacier", "canyon", "crystal", "mirror" })
                for (int v = 0; v < 4; v++)
                {
                    int index = theme switch { "overgrowth" => v % 2 == 0 ? 1 : 9, "swamp" => v % 2 == 0 ? 5 : 10,
                        "volcano" => v % 2 == 0 ? 3 : 14, "glacier" => v % 2 == 0 ? 4 : 11,
                        "canyon" => v % 2 == 0 ? 6 : 12, "crystal" => 7, _ => 15 };
                    Export(t, Grid(t, index, 4, 4), $"deco_{theme}_{v}",
                        index < 8 ? 30 + v % 2 * 4 : 14 + v % 2 * 4, variant: v);
                }
            UnityEngine.Object.DestroyImmediate(t);
        }

        static void ImportActors()
        {
            var t = Load("actors");
            string[] mobs = { "shroom", "jelly", "fox", "raccoon", "wisp", "frostling", "boar", "bat", "frog", "boss_shroom", "boss_fox", "boss_knight" };
            int[] heights = { 18, 13, 20, 18, 23, 23, 19, 15, 17, 38, 40, 42 };
            for (int i = 0; i < mobs.Length; i++) for (int frame = 0; frame < 5; frame++)
                Export(t, Grid(t, i, 4, 4), "enemy_" + mobs[i] + "_" + frame,
                    heights[i], canvas: new Vector2Int(i >= 9 ? 64 : 36, i >= 9 ? 48 : 32), frame: frame);
            string[] npcs = { "owl", "bear", "cat", "otter" };
            for (int i = 0; i < npcs.Length; i++) for (int f = 0; f < 2; f++)
                Export(t, Grid(t, i + 12, 4, 4), $"npc_{npcs[i]}_{f}", 24, canvas: new Vector2Int(36, 30), frame: f);
            UnityEngine.Object.DestroyImmediate(t);
        }

        static void ImportHero()
        {
            var t = Load("hero");
            string[] poses = { "Idle0", "Idle1", "Run0", "Run1", "Run2", "Run3", "Hurt", "Cast", "AttackWind", "AttackHit" };
            foreach (string guid in AssetDatabase.FindAssets("t:CostumeData", new[] { "Assets/_Project/Data/Costumes" }))
            {
                var c = AssetDatabase.LoadAssetAtPath<CostumeData>(AssetDatabase.GUIDToAssetPath(guid)).def;
                for (int i = 0; i < poses.Length; i++)
                    Export(t, Grid(t, i, 5, 2), $"rabbit_{c.id}_{poses[i]}", 28, canvas: new Vector2Int(36, 34), costume: c.id == "white" ? null : c);
                Export(t, Grid(t, 0, 5, 2), "elder_" + c.id, 27, canvas: new Vector2Int(36, 34), costume: c);
            }
            ImportResidents();
            UnityEngine.Object.DestroyImmediate(t);
        }

        static void ImportProps()
        {
            var t = Load("props");
            string[] keys = { "house_0_c2603e", "house_1_4f7bd9", "prop_stall", "prop_tent", "prop_shrine", "prop_obelisk", "deco_fortress_0", "prop_wall",
                "prop_chest", "prop_well", "prop_board", "prop_lamp", "prop_pillar_broken", "prop_pillar", "prop_barrel", "prop_crate" };
            int[] heights = { 66, 66, 38, 38, 34, 40, 46, 25, 15, 30, 22, 36, 24, 36, 18, 16 };
            for (int i = 0; i < keys.Length; i++) Export(t, Grid(t, i, 4, 4), keys[i], heights[i]);
            Export(t, Grid(t, 1, 4, 4), "house_0_8c5bd6", 66);
            Export(t, Grid(t, 0, 4, 4), "house_1_6aa36a", 66);
            Export(t, Grid(t, 5, 4, 4), "prop_warpstone", 32);
            Export(t, Grid(t, 7, 4, 4), "deco_fortress_1", 25);
            Export(t, Grid(t, 12, 4, 4), "deco_fortress_2", 24);
            // The fourth fortress object is an intact architectural column, aligned by the scenery compositor.
            Export(t, Grid(t, 13, 4, 4), "deco_fortress_3", 36);
            UnityEngine.Object.DestroyImmediate(t);
        }

        static void ImportLandmarks()
        {
            var t = Load("landmarks");
            string[] ids = { "overgrowth", "volcano", "crystal", "glacier", "canyon", "fortress", "mirror", "swamp" };
            int[] heights = { 128, 104, 128, 112, 128, 112, 120, 108 };
            for (int i = 0; i < ids.Length; i++) Export(t, Grid(t, i, 4, 2), "landmark_" + ids[i], heights[i]);
            UnityEngine.Object.DestroyImmediate(t);
        }

        // Source rectangles use top-left coordinates. Import collapses a magnified sheet into an actual native pixel
        // asset by area-majority voting in palette space, with binary alpha; there is no bilinear filtering or dithering.
        static void Export(Texture2D source, Rect cell, string key, int height, Vector2Int canvas = default, int frame = 0, int variant = 0, string tint = null, CostumeDef costume = null, int nativeWidth = 0)
        {
            if (sampledSource != source) { sampledSource = source; sourcePixels = source.GetPixels32(); }
            var px = sourcePixels;
            int x0 = Mathf.CeilToInt(cell.xMin), x1 = Mathf.Min(source.width, Mathf.FloorToInt(cell.xMax));
            int y0 = Mathf.Max(0, source.height - Mathf.FloorToInt(cell.yMax)), y1 = Mathf.Min(source.height, source.height - Mathf.CeilToInt(cell.yMin));
            int left = x1, right = x0, bottom = y1, top = y0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (px[y * source.width + x].a >= 200)
            { left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
            if (right <= left || top <= bottom) throw new InvalidDataException("Empty source cell: " + key);
            int sw = right - left + 1, sh = top - bottom + 1;
            int dh = height, dw = Mathf.RoundToInt(sw * height / (float)sh);
            if (nativeWidth > 0) { dw = nativeWidth; dh = Mathf.RoundToInt(sh * dw / (float)sw); height = dh; }
            if (frame == 2) { dh = Mathf.RoundToInt(dh * .88f); dw = Mathf.RoundToInt(dw * 1.08f); }
            int w = canvas.x > 0 ? canvas.x : dw + 4, h = canvas.y > 0 ? canvas.y : height + 4;
            if (dw > w - 4) { dh = Mathf.RoundToInt(dh * (w - 4f) / dw); dw = w - 4; }
            int dy = 1 + (frame is 1 or 4 ? 1 : 0), ox = (w - dw) / 2;
            var result = new Color32[w * h]; var votes = new int[palette.Length];
            for (int y = 0; y < dh; y++) for (int x = 0; x < dw; x++)
            {
                Array.Clear(votes, 0, votes.Length); int opaque = 0, count = 0;
                int ax = left + x * sw / dw, bx = Mathf.Max(ax + 1, left + (x + 1) * sw / dw);
                int ay = bottom + y * sh / dh, by = Mathf.Max(ay + 1, bottom + (y + 1) * sh / dh);
                for (int sy = ay; sy < by; sy++) for (int sx = ax; sx < bx; sx++)
                {
                    count++; var c = px[sy * source.width + sx]; if (c.a < 200) continue;
                    votes[Nearest(c)]++; opaque++;
                }
                if (opaque < count * .45f) continue;
                int best = 0; for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[best]) best = i;
                var color = palette[best];
                if (costume != null && (best is 8 or 9 or 10 or 17 or 18 or 19 or 20 or 32))
                {
                    Color32 fur = best is 10 ? Pal.Lerp(costume.fur, palette[10], .3f) : best is 8 or 17 or 18 or 19 ? costume.furShade : costume.fur;
                    color = palette[Nearest(fur)];
                }
                if (tint == "mirror" && best >= 11 && best <= 16) color = palette[best <= 12 ? 29 : best <= 14 ? 30 : 31];
                if (tint == "ash" && best >= 11 && best <= 16) color = palette[best <= 12 ? 2 : best <= 14 ? 4 : 6];
                int outX = variant % 2 == 1 ? w - 1 - ox - x : ox + x;
                if (frame is 1 or 4 && y < 3) outX += (x < dw / 2 ? -1 : 1) * (frame == 1 ? 1 : -1);
                if (y + dy < h && outX >= 0 && outX < w) result[(y + dy) * w + outX] = color;
            }
            PreserveNpcEyes(key, result, w, h, frame);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.SetPixels32(result); tex.Apply();
            string path = Output + key + ".png"; byte[] bytes = tex.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(tex);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
            imports[path] = new Vector2(.5f, 1f / h); managed.Add(key);
        }

        static int Nearest(Color32 c)
        {
            int key = c.r << 16 | c.g << 8 | c.b;
            if (nearest.TryGetValue(key, out int found)) return found;
            int best = 0, distance = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                int r = c.r - palette[i].r, g = c.g - palette[i].g, b = c.b - palette[i].b;
                int d = r * r * 3 + g * g * 4 + b * b * 2;
                if (d < distance) { best = i; distance = d; }
            }
            return nearest[key] = best;
        }

        public static void RunBatch()
        {
            try { Apply(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunBatchStyle()
        {
            try { Configure(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunBatchValidated()
        {
            try { Apply(); WorldVisualChecks.Run(); WorldOverview.RunBatch(); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
