using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Painted (3/4 view) world art from ArtSource/sheets (drawn by ArtSource/gen.ps1 with the image model):
    /// - object sheets → one sprite per cell in Resources/Sprites/&lt;key&gt;.png (128 px per unit, base-centred pivot).
    ///   Art.Get prefers these over the code-drawn pixel art, so trees, rocks, props, houses and items switch over by key.
    /// - ground sheets are skipped: the ground is drawn from the art rules (TileBaker, Data/ArtStyle.asset).
    /// - 2D lighting: a 2D Renderer for the URP assets, a neutral Global Light 2D in Game.unity, lamp and campfire lights.
    /// Sizes and prompts live in ArtSource/manifest.json.
    /// </summary>
    public static class PaintedArt
    {
        public const int PPU = Art.PPU;          // pixel art: 16 px per tile, like every other sprite
        static readonly Dictionary<string, float> pivots = new Dictionary<string, float>();
        const string SourceDir = "ArtSource";
        const string SpriteDir = "Assets/_Project/Resources/Sprites";

        [Serializable] class Item { public string key, fit; public float size, pivotY = -1; }
        [Serializable] class Sheet { public string file, kind; public int cols = 1, rows = 1; public List<Item> items = new List<Item>(); }
        [Serializable] class Manifest { public int dotScale = 1; public List<Sheet> sheets = new List<Sheet>(); }


        [MenuItem("Diverse/Art/Import Painted Art")]
        public static void ImportAll()
        {
            var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(SourceDir, "manifest.json")));
            int sprites = 0;
            foreach (var s in m.sheets)
            {
                var path = Path.Combine(SourceDir, "sheets", s.file + ".png");
                if (!File.Exists(path)) { Debug.LogWarning($"[PaintedArt] missing sheet {path}"); continue; }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                if (s.kind != "ground") sprites += CutSheet(tex, s, Mathf.Max(1, m.dotScale));
                UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.Refresh();
            foreach (var s in m.sheets.Where(x => x.kind != "ground"))
                foreach (var it in s.items) if (pivots.TryGetValue(it.key, out var pv)) ConfigureSprite($"{SpriteDir}/{it.key}.png", pv);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PaintedArt] {sprites} sprites imported");
        }

        // ───────────────────────── Object sheets ─────────────────────────

        /// <summary>
        /// Cells by connected pieces: every opaque blob is assigned to the grid cell its centre falls in, and a cell's
        /// sprite is the union of its blobs (a crystal's sparkles stay with the crystal, a wide stall may cross the line).
        /// </summary>
        static int CutSheet(Texture2D tex, Sheet s, int dot)
        {
            int W = tex.width, H = tex.height;
            var px = tex.GetPixels32();
            // Drop soft shadows / glow halos the model sometimes paints anyway: keep only pixels near solid ones
            var solid = new bool[W * H];
            for (int i = 0; i < px.Length; i++) solid[i] = px[i].a >= 215;
            var near = Dilate(solid, W, H, 3);
            for (int i = 0; i < px.Length; i++) if (!near[i]) px[i].a = 0;

            // Blobs on a coarse grid (4 px), merged across small gaps
            const int B = 4;
            int gw = W / B, gh = H / B;
            var coarse = new bool[gw * gh];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (px[y * W + x].a > 40) coarse[(y / B) * gw + x / B] = true;
            coarse = Dilate(coarse, gw, gh, 2);
            var label = new int[gw * gh];
            var boxes = new List<RectInt>();
            var stack = new Stack<int>();
            for (int i = 0; i < coarse.Length; i++)
            {
                if (!coarse[i] || label[i] != 0) continue;
                int id = boxes.Count + 1, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
                stack.Push(i); label[i] = id;
                while (stack.Count > 0)
                {
                    int c = stack.Pop(), cx = c % gw, cy = c / gw; count++;
                    x0 = Mathf.Min(x0, cx); x1 = Mathf.Max(x1, cx); y0 = Mathf.Min(y0, cy); y1 = Mathf.Max(y1, cy);
                    foreach (var n in new[] { c - 1, c + 1, c - gw, c + gw })
                    {
                        if (n < 0 || n >= coarse.Length || (n % gw == 0 && c % gw == gw - 1) || (c % gw == 0 && n % gw == gw - 1)) continue;
                        if (coarse[n] && label[n] == 0) { label[n] = id; stack.Push(n); }
                    }
                }
                boxes.Add(count >= 6 ? new RectInt(x0 * B, y0 * B, (x1 - x0 + 1) * B, (y1 - y0 + 1) * B) : default);
            }
            // Texture rows start at the bottom; manifest cells are read from the top
            int cw = W / s.cols, ch = H / s.rows;
            int CellOf(Vector2 c) => Mathf.Clamp(s.rows - 1 - (int)(c.y / ch), 0, s.rows - 1) * s.cols + Mathf.Clamp((int)(c.x / cw), 0, s.cols - 1);
            RectInt CellRect(int ci) => new RectInt(ci % s.cols * cw, (s.rows - 1 - ci / s.cols) * ch, cw, ch);
            var parts = boxes.Where(b => b.width > 0).Select(b => (box: b, cell: CellOf(b.center))).ToList();
            // Two objects that touch become one blob: split it where a wanted cell would otherwise stay empty
            for (int ci = 0; ci < s.items.Count && ci < s.cols * s.rows; ci++)
            {
                if (parts.Any(p => p.cell == ci)) continue;
                var cr = CellRect(ci);
                int pi = parts.FindIndex(p => p.box.Overlaps(cr));
                if (pi < 0) continue;
                var b = parts[pi].box;
                // Cut along the emptiest line near the cell edge (the gap between the two objects), not the edge itself
                int Gap(bool vertical, int edge)
                {
                    int best = edge, bestCount = int.MaxValue, reach = (vertical ? cw : ch) / 4;
                    for (int c = edge - reach; c <= edge + reach; c++)
                    {
                        int count = 0;
                        if (vertical) { if (c <= b.xMin || c >= b.xMax) continue; for (int y = b.yMin; y < b.yMax; y++) if (px[y * W + c].a > 40) count++; }
                        else { if (c <= b.yMin || c >= b.yMax) continue; for (int x = b.xMin; x < b.xMax; x++) if (px[c * W + x].a > 40) count++; }
                        if (count < bestCount || (count == bestCount && Mathf.Abs(c - edge) < Mathf.Abs(best - edge))) { bestCount = count; best = c; }
                    }
                    return best;
                }
                RectInt mine, rest;
                if (b.xMin < cr.xMin) { int c = Gap(true, cr.xMin); rest = new RectInt(b.xMin, b.yMin, c - b.xMin, b.height); mine = new RectInt(c, b.yMin, b.xMax - c, b.height); }
                else if (b.xMax > cr.xMax) { int c = Gap(true, cr.xMax); mine = new RectInt(b.xMin, b.yMin, c - b.xMin, b.height); rest = new RectInt(c, b.yMin, b.xMax - c, b.height); }
                else if (b.yMin < cr.yMin) { int c = Gap(false, cr.yMin); rest = new RectInt(b.xMin, b.yMin, b.width, c - b.yMin); mine = new RectInt(b.xMin, c, b.width, b.yMax - c); }
                else { int c = Gap(false, cr.yMax); mine = new RectInt(b.xMin, b.yMin, b.width, c - b.yMin); rest = new RectInt(b.xMin, c, b.width, b.yMax - c); }
                parts[pi] = (rest, parts[pi].cell);
                parts.Add((mine, ci));
            }
            var cells = new RectInt?[s.cols * s.rows];
            foreach (var (b, ci) in parts)
            {
                if (cells[ci] is RectInt r)
                {
                    int ax = Mathf.Min(r.xMin, b.xMin), ay = Mathf.Min(r.yMin, b.yMin);
                    cells[ci] = new RectInt(ax, ay, Mathf.Max(r.xMax, b.xMax) - ax, Mathf.Max(r.yMax, b.yMax) - ay);
                }
                else cells[ci] = b;
            }

            int made = 0;
            for (int i = 0; i < s.items.Count && i < cells.Length; i++)
            {
                if (!(cells[i] is RectInt box)) { Debug.LogWarning($"[PaintedArt] {s.file}: nothing in cell {i} for {s.items[i].key}"); continue; }
                var it = s.items[i];
                var crop = Crop(px, W, H, box, 6);
                // Pixel art: the subject shrunk by majority vote into a small palette with an outline, drawn with dots `dot` px
                // wide (big, simple dots) on the game's 16 px per tile. Pixelizer scales the longest side, or the height when fit = h
                float want = it.size * PPU / dot;
                int target = Mathf.Max(4, Mathf.RoundToInt(it.fit == "h" ? want : want * Mathf.Max(crop.width, crop.height) / crop.width));
                int colors = it.size >= 6 ? 16 : it.size >= 2.4f ? 12 : 10;
                var cv = Pixelizer.Convert(crop, new RectInt(0, 0, crop.width, crop.height), target, it.fit == "h", colors);
                UnityEngine.Object.DestroyImmediate(crop);
                if (cv == null) { Debug.LogWarning($"[PaintedArt] {it.key}: empty after pixelizing"); continue; }
                cv = Upscale(cv, dot);
                var outTex = cv.ToTexture();
                WriteFile($"{SpriteDir}/{it.key}.png", outTex.EncodeToPNG());
                // The base sits on the lowest drawn row (the canvas keeps a one-dot border for the outline)
                pivots[it.key] = (dot + (it.pivotY >= 0 ? it.pivotY : 0) * (cv.H - 2 * dot)) / (float)cv.H;
                UnityEngine.Object.DestroyImmediate(outTex);
                made++;
            }
            return made;
        }

        /// <summary>Every dot of a pixel canvas drawn s×s (big dots, the sprite stays at 16 px per tile).</summary>
        static PixelCanvas Upscale(PixelCanvas cv, int s)
        {
            if (s <= 1) return cv;
            var o = new PixelCanvas(cv.W * s, cv.H * s);
            for (int y = 0; y < o.H; y++)
                for (int x = 0; x < o.W; x++)
                    o.Px[y * o.W + x] = cv.Px[(y / s) * cv.W + x / s];
            return o;
        }

        /// <summary>Write a file, retrying a little while another process (indexer, virus scan) holds it open.</summary>
        static void WriteFile(string path, byte[] data)
        {
            for (int i = 0; ; i++)
            {
                try { File.WriteAllBytes(path, data); return; }
                catch (IOException) when (i < 20) { System.Threading.Thread.Sleep(250); }
            }
        }

        static bool[] Dilate(bool[] m, int w, int h, int r)
        {
            var tmp = new bool[m.Length]; var o = new bool[m.Length];
            for (int y = 0; y < h; y++)
            {
                int run = -1000;
                for (int x = 0; x < w; x++) { if (m[y * w + x]) run = x; tmp[y * w + x] = x - run <= r; }
                run = 100000;
                for (int x = w - 1; x >= 0; x--) { if (m[y * w + x]) run = x; if (run - x <= r) tmp[y * w + x] = true; }
            }
            for (int x = 0; x < w; x++)
            {
                int run = -1000;
                for (int y = 0; y < h; y++) { if (tmp[y * w + x]) run = y; o[y * w + x] = y - run <= r; }
                run = 100000;
                for (int y = h - 1; y >= 0; y--) { if (tmp[y * w + x]) run = y; if (run - y <= r) o[y * w + x] = true; }
            }
            return o;
        }

        static Texture2D Crop(Color32[] px, int W, int H, RectInt box, int pad)
        {
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int y = box.yMin; y < box.yMax && y < H; y++)
                for (int x = box.xMin; x < box.xMax && x < W; x++)
                    if (px[y * W + x].a > 8) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            int cw = x1 - x0 + 1 + pad * 2, ch = y1 - y0 + 1 + pad * 2;
            var c = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int sx = x0 - pad + x, sy = y0 - pad + y;
                    c[y * cw + x] = sx >= box.xMin && sx < box.xMax && sy >= box.yMin && sy < box.yMax && sx >= 0 && sy >= 0 && sx < W && sy < H ? px[sy * W + sx] : new Color32(0, 0, 0, 0);
                }
            var t = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            t.SetPixels32(c); t.Apply();
            return t;
        }

        /// <summary>Area-average resample with premultiplied alpha (no dark fringes when shrinking).</summary>
        static Texture2D Resample(Texture2D src, int tw, int th)
        {
            int sw = src.width, sh = src.height;
            var s = src.GetPixels();
            var o = new Color[tw * th];
            float fx = (float)sw / tw, fy = (float)sh / th;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    float ax = x * fx, ay = y * fy, bx = ax + fx, by = ay + fy;
                    float r = 0, g = 0, b = 0, a = 0, wsum = 0;
                    for (int yy = Mathf.FloorToInt(ay); yy < Mathf.CeilToInt(by) && yy < sh; yy++)
                    {
                        float wy = Mathf.Min(by, yy + 1) - Mathf.Max(ay, yy);
                        for (int xx = Mathf.FloorToInt(ax); xx < Mathf.CeilToInt(bx) && xx < sw; xx++)
                        {
                            float w = (Mathf.Min(bx, xx + 1) - Mathf.Max(ax, xx)) * wy;
                            var c = s[yy * sw + xx];
                            r += c.r * c.a * w; g += c.g * c.a * w; b += c.b * c.a * w; a += c.a * w; wsum += w;
                        }
                    }
                    o[y * tw + x] = a > 1e-5f ? new Color(r / a, g / a, b / a, a / wsum) : new Color(0, 0, 0, 0);
                }
            var t = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            t.SetPixels(o); t.Apply();
            return t;
        }

        static void ConfigureSprite(string path, float pivotY)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            var ts = new TextureImporterSettings();
            imp.ReadTextureSettings(ts);
            ts.spriteAlignment = (int)SpriteAlignment.Custom;
            ts.spritePivot = new Vector2(0.5f, pivotY);
            ts.spritePixelsPerUnit = PPU;
            ts.spriteMeshType = SpriteMeshType.Tight;
            ts.spriteExtrude = 1;
            imp.SetTextureSettings(ts);
            imp.filterMode = FilterMode.Point;        // pixel art: crisp dots (the sprite shader smooths only the dot edges)
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.isReadable = true;                    // design packs recolour hand art (DesignRuntime.Restyle)
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
        }

        // ───────────────────────── 2D lighting ─────────────────────────

        /// <summary>
        /// Switch both URP assets to a 2D Renderer (Light 2D needs it) and give Game.unity a neutral Global Light 2D. Existing renderers stay in the list.
        /// </summary>
        [MenuItem("Diverse/Art/Set Up 2D Lighting")]
        public static void SetUpLighting()
        {
            const string rendererPath = "Assets/Settings/Renderer2D.asset";
            var data = AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
            if (data == null)
            {
                var menus = typeof(Renderer2DData).Assembly.GetType("UnityEditor.Rendering.Universal.Renderer2DMenus")
                            ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Rendering.Universal.Renderer2DMenus")).FirstOrDefault(t => t != null);
                var create = menus.GetMethod("CreateRendererAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var typeEnum = create.GetParameters()[1].ParameterType;
                data = (Renderer2DData)create.Invoke(null, new object[] { rendererPath, Enum.Parse(typeEnum, "_2DRenderer"), false, "Renderer" });
            }
            foreach (var p in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(p);
                if (rp == null) continue;
                var so = new SerializedObject(rp);
                var list = so.FindProperty("m_RendererDataList");
                int idx = -1;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) idx = i;
                if (idx < 0) { idx = list.arraySize; list.InsertArrayElementAtIndex(idx); list.GetArrayElementAtIndex(idx).objectReferenceValue = data; }
                so.FindProperty("m_DefaultRendererIndex").intValue = idx;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(rp);
            }
            AssetDatabase.SaveAssets();

            const string scenePath = "Assets/_Project/Scenes/Game.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<Light2D>() == null)
            {
                // Daylight stays neutral so the palette shows as drawn; lamps and fires add their glow
                var go = new GameObject("Global Light 2D");
                var light = go.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Global;
                light.intensity = 1f;
                light.color = Color.white;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            // Campfires light their surroundings (Prefabs/World/Campfire.prefab → child "light")
            const string firePath = "Assets/_Project/Prefabs/World/Campfire.prefab";
            var fire = PrefabUtility.LoadPrefabContents(firePath);
            if (fire.GetComponentInChildren<Light2D>(true) == null)
            {
                var go = new GameObject("light");
                go.transform.SetParent(fire.transform, false);
                var l = go.AddComponent<Light2D>();
                l.lightType = Light2D.LightType.Point;
                l.color = new Color(1f, 0.72f, 0.42f);
                l.intensity = 0.7f;
                l.pointLightInnerRadius = 0.4f;
                l.pointLightOuterRadius = 4f;
                l.falloffIntensity = 0.6f;
                PrefabUtility.SaveAsPrefabAsset(fire, firePath);
            }
            PrefabUtility.UnloadPrefabContents(fire);
            Debug.Log("[PaintedArt] 2D lighting ready (Renderer2D default, Global Light 2D in Game.unity)");
        }

        /// <summary>Batch: import the art and set up lighting (Unity.exe -batchmode -executeMethod Diverse.EditorTools.PaintedArt.RunBatch).</summary>
        public static void RunBatch()
        {
            try { ImportAll(); SetUpLighting(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
