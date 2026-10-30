using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Review aid: every zone sprite the game will load (Resources/Sprites, as imported) on contact sheets, 3x, on a
    /// mid-grey checker so dark and light pictures both show → Logs/ArtSheets/*.png, plus sizes.txt (key, size, pivot).
    /// Batch: Unity -batchmode -nographics -executeMethod Diverse.EditorTools.ArtContactSheet.RunBatch
    /// </summary>
    public static class ArtContactSheet
    {
        const string Out = "Logs/ArtSheets";
        const int Scale = 3, Pad = 4;

        [MenuItem("Diverse/구역 아트 검수 시트 (Logs/ArtSheets)", priority = 94)]
        public static void RunMenu() { Run(); EditorUtility.RevealInFinder(Out); }

        public static void RunBatch() { Run(); EditorApplication.Exit(0); }

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            var all = Resources.LoadAll<Sprite>("Sprites").GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
            var sizes = new StringBuilder();
            foreach (var s in all.Values.OrderBy(s => s.name))
                sizes.AppendLine($"{s.name}\t{s.rect.width}x{s.rect.height}\tpivot {s.pivot.x / s.rect.width:0.00},{s.pivot.y / s.rect.height:0.00}\treadable {s.texture.isReadable}");
            File.WriteAllText(Path.Combine(Out, "sizes.txt"), sizes.ToString());

            // Enemies: one row each, frames 0..4
            var enemyRows = DB.Enemies.Values.OrderBy(e => e.boss).ThenBy(e => e.id)
                .Select(e => Enumerable.Range(0, 5).Select(f => all.TryGetValue($"enemy_{e.sprite}_{f}", out var sp) ? sp : null).ToList()).ToList();
            for (int i = 0; i < enemyRows.Count; i += 16) Sheet("enemies_" + (i / 16 + 1), enemyRows.Skip(i).Take(16).ToList());
            var biomes = System.Enum.GetValues(typeof(Biome)).Cast<Biome>().ToList();
            Sheet("scenery", biomes.Select(b => new[] { "tree_0", "tree_1", "tree_2", "tree_3", "rock_0", "rock_1", "rock_2", "bush_0", "bush_1", "tuft_0", "tuft_1" }
                .Select(k => { var p = k.Split('_'); return all.TryGetValue($"{p[0]}_{b}_{p[1]}", out var sp) ? sp : null; }).ToList()).ToList());
            Sheet("effects", System.Enum.GetValues(typeof(BuildStyle)).Cast<BuildStyle>()
                .Select(z => new[] { "shot", "hit", "burst", "mote" }.Select(k => all.TryGetValue(ZoneArtDef.FxKey(z, k), out var sp) ? sp : null).ToList()).ToList());
            Sheet("props", all.Keys.Where(k => k.StartsWith("prop_")).OrderBy(k => k).Select((k, i) => (k, i)).GroupBy(x => x.i / 8)
                .Select(g => g.Select(x => all[x.k]).ToList()).ToList());
            // Grounds: each texture tiled 2x2 mirrored, as the game lays it
            var grounds = System.Enum.GetValues(typeof(Ground)).Cast<Ground>()
                .Select(g => new[] { 0, 1 }.Select(v => all.TryGetValue($"ground_{g}_{v}", out var sp) ? sp : null).ToList()).Where(r => r.Any(x => x != null)).ToList();
            Sheet("grounds", grounds, tile: true);
        }

        static void Sheet(string name, List<List<Sprite>> rows, bool tile = false)
        {
            int cellW = 0, cellH = 0;
            foreach (var r in rows) foreach (var s in r) if (s != null) { cellW = Mathf.Min(80, Mathf.Max(cellW, (int)s.rect.width * (tile ? 2 : 1))); cellH = Mathf.Min(80, Mathf.Max(cellH, (int)s.rect.height * (tile ? 2 : 1))); }
            if (cellW == 0) return;
            int cols = rows.Max(r => r.Count);
            int W = cols * (cellW * Scale + Pad) + Pad, H = rows.Count * (cellH * Scale + Pad) + Pad;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = ((x / 12 + y / 12) % 2 == 0) ? new Color32(118, 118, 124, 255) : new Color32(140, 140, 146, 255);
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Count; c++)
                {
                    var s = rows[r][c];
                    int ox = Pad + c * (cellW * Scale + Pad), oy = H - (r + 1) * (cellH * Scale + Pad);
                    if (s == null)
                    {
                        for (int i = 0; i < 6 * Scale; i++) { Put(px, W, H, ox + i, oy + i, new Color32(255, 0, 255, 255)); Put(px, W, H, ox + 6 * Scale - i, oy + i, new Color32(255, 0, 255, 255)); }
                        continue;
                    }
                    var src = s.texture.isReadable ? s.texture.GetPixels32() : null;
                    if (src == null) continue;
                    int sw = s.texture.width, rw = Mathf.Min(cellW, (int)s.rect.width), rh = Mathf.Min(cellH, (int)s.rect.height), rx = (int)s.rect.x, ry = (int)s.rect.y;
                    int reps = tile ? 2 : 1;
                    for (int ty = 0; ty < reps; ty++)
                        for (int tx = 0; tx < reps; tx++)
                            for (int y = 0; y < rh; y++)
                                for (int x = 0; x < rw; x++)
                                {
                                    int sx = tx == 1 ? rw - 1 - x : x, sy = ty == 1 ? rh - 1 - y : y;
                                    var col = src[(ry + sy) * sw + rx + sx];
                                    if (col.a < 10) continue;
                                    for (int k = 0; k < Scale; k++)
                                        for (int l = 0; l < Scale; l++)
                                            Put(px, W, H, ox + (tx * rw + x) * Scale + l, oy + (ty * rh + y) * Scale + k, col);
                                }
                    // pivot mark
                    if (!tile) for (int i = -2; i <= 2; i++) Put(px, W, H, ox + (int)(s.pivot.x * Scale) + i, oy + (int)(s.pivot.y * Scale), new Color32(255, 40, 40, 255));
                }
            Debug.Log($"[Sheet] {name} {W}x{H}");
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void Put(Color32[] px, int W, int H, int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = c; }
    }
}
