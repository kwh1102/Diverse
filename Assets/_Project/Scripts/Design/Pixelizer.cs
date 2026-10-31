using System.Collections.Generic;
using System.Linq;
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
        public static PixelCanvas[] ConvertSet(Texture2D src, RectInt[] regions, int target, bool byHeight, int colors = 14, bool outline = true, bool alignFacing = false)
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
                KeepSubject(px, r.width, r.height);
                var box = Bounds(px, r.width, r.height);
                cells[i] = (px, r.width, r.height, box);
                if (scale == 0 && box.width > 0) scale = target / (float)(byHeight ? box.height : Mathf.Max(box.width, box.height));
            }
            var result = new PixelCanvas[regions.Length];
            if (scale == 0) return result;
            // Palette first (from the full-resolution subject, shared by every pose), then each output dot takes the palette
            // colour most of its block has: edges stay crisp and colours stay pure instead of averaging into mud.
            var palette = PaletteOf(cells.Where(c => c.box.width > 0).Select(c => (c.px, c.w, c.box)), colors);
            for (int i = 0; i < cells.Length; i++)
            {
                var (px, w, _, box) = cells[i];
                if (box.width == 0) continue;
                result[i] = MajorityDownsample(px, w, box, scale, palette);
                Despeckle(result[i]);
            }
            // Poses of one subject must face the same way as the first (the image model sometimes turns one around)
            if (alignFacing) for (int i = 1; i < result.Length; i++) if (result[i] != null && result[0] != null && FacesOpposite(result[i], result[0])) Mirror(result[i]);
            if (outline) foreach (var cv in result) cv?.Outline(Pal.Outline);
            return result;
        }

        /// <summary>
        /// Keep only this cell's own subject: the biggest blob that doesn't touch the cell edge, plus small parts close to it
        /// (a hat, a held item). Blobs cut off at the edge are pieces of the neighbouring figure that spilled over the grid line.
        /// </summary>
        static void KeepSubject(Color32[] px, int w, int h)
        {
            var label = new int[w * h];
            var comps = new List<(int size, RectInt box, bool edge)>();
            var stack = new Stack<int>();
            for (int start = 0; start < px.Length; start++)
            {
                if (px[start].a <= 128 || label[start] != 0) continue;
                int id = comps.Count + 1, size = 0, x0 = w, y0 = h, x1 = 0, y1 = 0;
                bool edge = false;
                stack.Push(start); label[start] = id;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % w, y = i / w;
                    size++;
                    x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y);
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) edge = true;
                    void Visit(int j) { if (px[j].a > 128 && label[j] == 0) { label[j] = id; stack.Push(j); } }
                    if (x > 0) Visit(i - 1);
                    if (x < w - 1) Visit(i + 1);
                    if (y > 0) Visit(i - w);
                    if (y < h - 1) Visit(i + w);
                }
                comps.Add((size, new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1), edge));
            }
            if (comps.Count <= 1) return;
            int main = -1;
            for (int c = 0; c < comps.Count; c++)
                if (!comps[c].edge && (main < 0 || comps[c].size > comps[main].size)) main = c;
            if (main < 0) for (int c = 0; c < comps.Count; c++) if (main < 0 || comps[c].size > comps[main].size) main = c;
            var mb = comps[main].box;
            int padX = mb.width / 6 + 2, padY = mb.height / 6 + 2;
            var near = new RectInt(mb.x - padX, mb.y - padY, mb.width + padX * 2, mb.height + padY * 2);
            var keep = new bool[comps.Count + 1];
            for (int c = 0; c < comps.Count; c++)
                keep[c + 1] = c == main || (!comps[c].edge && comps[c].box.Overlaps(near));
            for (int i = 0; i < px.Length; i++)
                if (label[i] != 0 && !keep[label[i]]) px[i].a = 0;
        }

        /// <summary>
        /// An already-pixelized sprite: keep the biggest blob and whatever overlaps its box (repairing older pictures that
        /// still carry a piece of a neighbouring figure).
        /// </summary>
        public static void CleanSprite(PixelCanvas cv)
        {
            var px = cv.Px;
            var label = new int[px.Length];
            var comps = new List<(int size, RectInt box)>();
            var stack = new Stack<int>();
            for (int start = 0; start < px.Length; start++)
            {
                if (px[start].a == 0 || label[start] != 0) continue;
                int id = comps.Count + 1, size = 0, x0 = cv.W, y0 = cv.H, x1 = 0, y1 = 0;
                stack.Push(start); label[start] = id;
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i % cv.W, y = i / cv.W;
                    size++;
                    x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y);
                    void Visit(int j) { if (px[j].a > 0 && label[j] == 0) { label[j] = id; stack.Push(j); } }
                    if (x > 0) Visit(i - 1);
                    if (x < cv.W - 1) Visit(i + 1);
                    if (y > 0) Visit(i - cv.W);
                    if (y < cv.H - 1) Visit(i + cv.W);
                }
                comps.Add((size, new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1)));
            }
            if (comps.Count <= 1) return;
            int main = 0;
            for (int c = 1; c < comps.Count; c++) if (comps[c].size > comps[main].size) main = c;
            for (int i = 0; i < px.Length; i++)
                if (label[i] != 0 && label[i] - 1 != main && !comps[label[i] - 1].box.Overlaps(comps[main].box)) px[i] = default;
        }

        /// <summary>Does this pose's silhouette match the reference better when mirrored? (feet aligned, centred)</summary>
        public static bool FacesOpposite(PixelCanvas pose, PixelCanvas reference)
        {
            int same = 0, flipped = 0;
            int dx = (reference.W - pose.W) / 2;
            for (int y = 0; y < Mathf.Min(pose.H, reference.H); y++)
                for (int x = 0; x < pose.W; x++)
                {
                    int rx = x + dx;
                    if (rx < 0 || rx >= reference.W || !reference.Solid(rx, y)) continue;
                    if (pose.Solid(x, y)) same++;
                    if (pose.Solid(pose.W - 1 - x, y)) flipped++;
                }
            return flipped > same * 1.12f + 4;
        }

        public static void Mirror(PixelCanvas cv)
        {
            for (int y = 0; y < cv.H; y++)
                for (int x = 0; x < cv.W / 2; x++)
                {
                    int a = y * cv.W + x, b = y * cv.W + cv.W - 1 - x;
                    (cv.Px[a], cv.Px[b]) = (cv.Px[b], cv.Px[a]);
                }
        }

        static Vector3 V(Color32 c) => new Vector3(c.r, c.g, c.b);

        static int Nearest(Vector3[] pal, Color32 c)
        {
            var v = V(c);
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pal.Length; i++) { float d = (pal[i] - v).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>k-means palette over (a sample of) the opaque source pixels.</summary>
        static Vector3[] PaletteOf(IEnumerable<(Color32[] px, int w, RectInt box)> cells, int k)
        {
            var sample = new List<Vector3>();
            foreach (var (px, w, box) in cells)
            {
                int step = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(box.width * box.height / 6000f)));
                for (int y = box.y; y < box.y + box.height; y += step)
                    for (int x = box.x; x < box.x + box.width; x += step)
                        if (px[y * w + x].a > 128) sample.Add(V(px[y * w + x]));
            }
            if (sample.Count == 0) return new[] { Vector3.zero };
            k = Mathf.Min(k, sample.Count);
            var centers = new Vector3[k];
            for (int c = 0; c < k; c++) centers[c] = sample[c * sample.Count / k];
            var assign = new int[sample.Count];
            for (int iter = 0; iter < 10; iter++)
            {
                var sum = new Vector3[k]; var cnt = new int[k];
                for (int j = 0; j < sample.Count; j++)
                {
                    int best = 0; float bd = float.MaxValue;
                    for (int c = 0; c < k; c++) { float d = (centers[c] - sample[j]).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
                    assign[j] = best; sum[best] += sample[j]; cnt[best]++;
                }
                for (int c = 0; c < k; c++) if (cnt[c] > 0) centers[c] = sum[c] / cnt[c];
            }
            return centers;
        }

        static PixelCanvas MajorityDownsample(Color32[] px, int w, RectInt box, float scale, Vector3[] pal)
        {
            int ow = Mathf.Clamp(Mathf.RoundToInt(box.width * scale), 1, 256), oh = Mathf.Clamp(Mathf.RoundToInt(box.height * scale), 1, 256);   // up to landmark size
            var cv = new PixelCanvas(ow + 2, oh + 2);
            var votes = new int[pal.Length];
            for (int oy = 0; oy < oh; oy++)
                for (int ox = 0; ox < ow; ox++)
                {
                    int sx0 = box.x + ox * box.width / ow, sx1 = Mathf.Max(sx0 + 1, box.x + (ox + 1) * box.width / ow);
                    int sy0 = box.y + oy * box.height / oh, sy1 = Mathf.Max(sy0 + 1, box.y + (oy + 1) * box.height / oh);
                    int step = Mathf.Max(1, (sx1 - sx0) / 6);   // big blocks: a 6x6-ish sample is plenty for a vote
                    System.Array.Clear(votes, 0, votes.Length);
                    int n = 0, total = 0;
                    for (int y = sy0; y < sy1; y += step)
                        for (int x = sx0; x < sx1; x += step)
                        {
                            total++;
                            var c = px[y * w + x];
                            if (c.a <= 128) continue;
                            votes[Nearest(pal, c)]++; n++;
                        }
                    if (n == 0 || n < total * 0.42f) continue;
                    int win = 0;
                    for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[win]) win = i;
                    var p = pal[win];
                    cv.Px[(oy + 1) * cv.W + ox + 1] = new Color32((byte)p.x, (byte)p.y, (byte)p.z, 255);
                }
            return cv;
        }

        /// <summary>Drop lone dots floating in the air and recolour single odd dots surrounded by one colour (image-model noise).</summary>
        static void Despeckle(PixelCanvas cv)
        {
            var copy = (Color32[])cv.Px.Clone();
            for (int y = 1; y < cv.H - 1; y++)
                for (int x = 1; x < cv.W - 1; x++)
                {
                    var c = copy[y * cv.W + x];
                    if (c.a == 0) continue;
                    var n = new[] { copy[y * cv.W + x - 1], copy[y * cv.W + x + 1], copy[(y - 1) * cv.W + x], copy[(y + 1) * cv.W + x] };
                    int solid = n.Count(q => q.a > 0);
                    if (solid == 0) { cv.Px[y * cv.W + x] = default; continue; }
                    if (solid == 4 && n.All(q => q.r == n[0].r && q.g == n[0].g && q.b == n[0].b) && (c.r != n[0].r || c.g != n[0].g || c.b != n[0].b))
                        cv.Px[y * cv.W + x] = n[0];
                }
        }

        /// <summary>Opaque texture swatch (ground): the whole cell shrunk to size×size, few colours, no outline.</summary>
        public static PixelCanvas Swatch(Texture2D src, RectInt r, int size, int colors = 6, float calm = 0.45f)
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
            // Ground sits under everything: pull every colour toward the average (45% by default) so the floor stays calm and readable
            float mr = 0, mg = 0, mb = 0;
            foreach (var c in cv.Px) { mr += c.r; mg += c.g; mb += c.b; }
            var mean = new Color32((byte)(mr / cv.Px.Length), (byte)(mg / cv.Px.Length), (byte)(mb / cv.Px.Length), 255);
            for (int i = 0; i < cv.Px.Length; i++) cv.Px[i] = Pal.Lerp(cv.Px[i], mean, calm);
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
