using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Image-model picture → game pixel art: remove the background (when the model ignored "transparent"), crop to the
    /// subject, shrink with coverage-weighted averaging, reduce to a small palette (k-means) and add the game's 1px outline.
    /// Animation frames of one subject are converted together: same scale and one shared palette, so nothing flickers.
    /// </summary>
    public static class Pixelizer
    {
        public static PixelCanvas Convert(Texture2D src, RectInt region, int target, bool byHeight, int colors = 14) =>
            ConvertSet(src, new[] { region }, target, byHeight, colors)[0];

        /// <summary>
        /// Several cells of one subject (poses). The first cell sets the scale (its longest side or height = target px);
        /// the others keep that scale so the character doesn't grow or shrink between frames. A null entry = empty cell.
        /// </summary>
        public static PixelCanvas[] ConvertSet(Texture2D src, RectInt[] regions, int target, bool byHeight, int colors = 14, bool outline = true)
        {
            var all = src.GetPixels32();
            var cells = new (Color32[] px, int w, int h, RectInt box)[regions.Length];
            float scale = 0;
            for (int i = 0; i < regions.Length; i++)
            {
                var r = regions[i];
                var px = new Color32[r.width * r.height];
                for (int y = 0; y < r.height; y++)
                    for (int x = 0; x < r.width; x++)
                        px[y * r.width + x] = all[(r.y + y) * src.width + r.x + x];
                RemoveBackground(px, r.width, r.height);
                var box = Bounds(px, r.width, r.height);
                cells[i] = (px, r.width, r.height, box);
                if (scale == 0 && box.width > 0) scale = target / (float)(byHeight ? box.height : Mathf.Max(box.width, box.height));
            }
            var result = new PixelCanvas[regions.Length];
            if (scale == 0) return result;
            for (int i = 0; i < cells.Length; i++)
            {
                var (px, w, _, box) = cells[i];
                if (box.width == 0) continue;
                result[i] = Downsample(px, w, box, scale);
            }
            Quantize(result, colors);
            if (outline) foreach (var cv in result) cv?.Outline(Pal.Outline);
            return result;
        }

        /// <summary>Opaque texture swatch (ground): the whole cell shrunk to size×size, few colours, no outline.</summary>
        public static PixelCanvas Swatch(Texture2D src, RectInt r, int size, int colors = 6)
        {
            var all = src.GetPixels32();
            var cv = new PixelCanvas(size, size);
            for (int oy = 0; oy < size; oy++)
                for (int ox = 0; ox < size; ox++)
                {
                    int x0 = r.x + ox * r.width / size, x1 = r.x + (ox + 1) * r.width / size;
                    int y0 = r.y + oy * r.height / size, y1 = r.y + (oy + 1) * r.height / size;
                    float cr = 0, cg = 0, cb = 0; int n = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) { var c = all[y * src.width + x]; cr += c.r; cg += c.g; cb += c.b; n++; }
                    n = Mathf.Max(1, n);
                    cv.Px[oy * size + ox] = new Color32((byte)(cr / n), (byte)(cg / n), (byte)(cb / n), 255);
                }
            Quantize(new[] { cv }, colors);
            // Ground sits under everything: pull every colour 45% toward the average so the floor stays calm and readable
            float mr = 0, mg = 0, mb = 0;
            foreach (var c in cv.Px) { mr += c.r; mg += c.g; mb += c.b; }
            var mean = new Color32((byte)(mr / cv.Px.Length), (byte)(mg / cv.Px.Length), (byte)(mb / cv.Px.Length), 255);
            for (int i = 0; i < cv.Px.Length; i++) cv.Px[i] = Pal.Lerp(cv.Px[i], mean, 0.45f);
            return cv;
        }

        static RectInt Bounds(Color32[] px, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 128) { x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y); }
            return x1 < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        static PixelCanvas Downsample(Color32[] px, int w, RectInt box, float scale)
        {
            int ow = Mathf.Clamp(Mathf.RoundToInt(box.width * scale), 1, 128), oh = Mathf.Clamp(Mathf.RoundToInt(box.height * scale), 1, 128);
            var cv = new PixelCanvas(ow + 2, oh + 2);
            for (int oy = 0; oy < oh; oy++)
                for (int ox = 0; ox < ow; ox++)
                {
                    int sx0 = box.x + ox * box.width / ow, sx1 = Mathf.Max(sx0 + 1, box.x + (ox + 1) * box.width / ow);
                    int sy0 = box.y + oy * box.height / oh, sy1 = Mathf.Max(sy0 + 1, box.y + (oy + 1) * box.height / oh);
                    float r = 0, g = 0, b = 0; int n = 0, total = 0;
                    for (int y = sy0; y < sy1; y++)
                        for (int x = sx0; x < sx1; x++)
                        {
                            total++;
                            var c = px[y * w + x];
                            if (c.a <= 128) continue;
                            r += c.r; g += c.g; b += c.b; n++;
                        }
                    if (n == 0 || n < total * 0.42f) continue;
                    cv.Px[(oy + 1) * cv.W + ox + 1] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            return cv;
        }

        /// <summary>The model sometimes paints a flat backdrop anyway: flood-fill it away from the borders.</summary>
        static void RemoveBackground(Color32[] px, int w, int h)
        {
            int opaqueBorder = 0, border = 0;
            for (int x = 0; x < w; x++) { border += 2; if (px[x].a > 128) opaqueBorder++; if (px[(h - 1) * w + x].a > 128) opaqueBorder++; }
            if (opaqueBorder < border * 0.6f) return;     // already transparent
            var key = px[0];
            var seen = new bool[w * h];
            var stack = new Stack<int>();
            for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (seen[i]) continue;
                seen[i] = true;
                var c = px[i];
                if (Mathf.Abs(c.r - key.r) + Mathf.Abs(c.g - key.g) + Mathf.Abs(c.b - key.b) > 60) continue;
                px[i].a = 0;
                int x = i % w, y = i / w;
                if (x > 0) stack.Push(i - 1);
                if (x < w - 1) stack.Push(i + 1);
                if (y > 0) stack.Push(i - w);
                if (y < h - 1) stack.Push(i + w);
            }
        }

        /// <summary>k-means palette reduction over the opaque pixels of all canvases together (one shared palette).</summary>
        static void Quantize(PixelCanvas[] canvases, int k)
        {
            var idx = new List<(PixelCanvas cv, int i)>();
            foreach (var cv in canvases)
                if (cv != null)
                    for (int i = 0; i < cv.Px.Length; i++) if (cv.Px[i].a > 0) idx.Add((cv, i));
            if (idx.Count <= k) return;
            var centers = new Vector3[k];
            for (int c = 0; c < k; c++) { var p = idx[c * idx.Count / k]; var px = p.cv.Px[p.i]; centers[c] = new Vector3(px.r, px.g, px.b); }
            var assign = new int[idx.Count];
            for (int iter = 0; iter < 8; iter++)
            {
                var sum = new Vector3[k]; var cnt = new int[k];
                for (int j = 0; j < idx.Count; j++)
                {
                    var p = idx[j].cv.Px[idx[j].i]; var v = new Vector3(p.r, p.g, p.b);
                    int best = 0; float bd = float.MaxValue;
                    for (int c = 0; c < k; c++) { float d = (centers[c] - v).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
                    assign[j] = best; sum[best] += v; cnt[best]++;
                }
                for (int c = 0; c < k; c++) if (cnt[c] > 0) centers[c] = sum[c] / cnt[c];
            }
            for (int j = 0; j < idx.Count; j++)
            {
                var c = centers[assign[j]];
                idx[j].cv.Px[idx[j].i] = new Color32((byte)c.x, (byte)c.y, (byte)c.z, 255);
            }
        }
    }
}
