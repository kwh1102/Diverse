using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diverse
{
    /// <summary>Isolated icon cells only. Backgrounds are removed before pixelization and validated afterward.</summary>
    public static class ActiveIconProcessor
    {
        public static PixelCanvas Read(Texture2D source, RectInt region, out string why)
        {
            why = "읽을 수 없는 아이콘"; if (source == null || !source.isReadable || region.width < 4 || region.height < 4) return null;
            var cfg = ActiveSkillGenerator.Rules;
            var all = source.GetPixels32(); int w = region.width, h = region.height;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = all[(region.y + y) * source.width + region.x + x];
            var border = new List<int>();
            for (int x = 0; x < w; x++) { border.Add(x); border.Add((h - 1) * w + x); }
            for (int y = 1; y < h - 1; y++) { border.Add(y * w); border.Add(y * w + w - 1); }
            // Include several dominant border colours to remove flat mattes and checkerboard backdrops.
            var keys = border.Where(i => px[i].a > 128).GroupBy(i => (px[i].r / 24, px[i].g / 24, px[i].b / 24))
                .Where(g => g.Count() >= border.Count * cfg.iconMatteBorderShare).OrderByDescending(g => g.Count()).Take(3)
                .Select(g => px[g.First()]).ToArray();
            var seen = new bool[px.Length]; var todo = new Stack<int>(border);
            while (todo.Count > 0)
            {
                int i = todo.Pop(); if (seen[i]) continue; seen[i] = true; var p = px[i];
                bool background = p.a < 128 || keys.Any(c => Mathf.Abs(c.r - p.r) + Mathf.Abs(c.g - p.g) + Mathf.Abs(c.b - p.b) <= cfg.iconMatteTolerance);
                if (!background) continue;
                px[i] = default; int x = i % w, y = i / w;
                if (x > 0) todo.Push(i - 1); if (x + 1 < w) todo.Push(i + 1);
                if (y > 0) todo.Push(i - w); if (y + 1 < h) todo.Push(i + w);
            }
            if (border.Count(i => px[i].a > 128) > border.Count * cfg.iconBorderLimit) { why = "칸 경계에 그림·배경이 닿음"; return null; }
            int xmin = w, ymin = h, xmax = -1, ymax = -1, solid = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (px[y * w + x].a > 128)
            { xmin = Mathf.Min(xmin, x); ymin = Mathf.Min(ymin, y); xmax = Mathf.Max(xmax, x); ymax = Mathf.Max(ymax, y); solid++; }
            if (solid < 8) { why = "비어 있는 아이콘"; return null; }
            if (solid / (float)((xmax - xmin + 1) * (ymax - ymin + 1)) > cfg.iconSolidLimit) { why = "불투명한 사각 배경 의심"; return null; }
            var cleaned = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                cleaned.SetPixels32(px); cleaned.Apply();
                var cv = Pixelizer.ConvertSet(cleaned, new[] { new RectInt(0, 0, w, h) }, cfg.iconPixels - 4, false, cfg.iconPaletteColors, true)[0];
                if (cv == null) { why = "도트 변환 결과 없음"; return null; }
                var padded = new PixelCanvas(cfg.iconPixels, cfg.iconPixels);
                padded.Stamp(cv, (padded.W - cv.W) / 2, (padded.H - cv.H) / 2);
                why = "투명도·셀 경계 검사 통과"; return padded;
            }
            finally { Object.Destroy(cleaned); }
        }
    }
}
