using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>
    /// Pixel map renderer shared by the world map and the minimap: one map pixel per world tile, painted into a
    /// Texture2D shown by a RawImage, with pooled Text markers on top. Explored-only filtering is up to the caller.
    /// </summary>
    public class MapCanvas : MonoBehaviour
    {
        [SerializeField] RawImage image;
        [SerializeField] Text markerTemplate;       // icon glyph; child "Name" holds the optional label
        [SerializeField] Color background = new Color(0.08f, 0.06f, 0.11f);

        sealed class MapTile
        {
            public readonly Color32[] pixels = new Color32[WorldGen.Chunk * WorldGen.Chunk];
            public int filled;
        }
        static readonly Dictionary<Vector2Int, MapTile> tiles = new Dictionary<Vector2Int, MapTile>();
        static readonly Queue<Vector2Int> tileOrder = new Queue<Vector2Int>();
        static WorldGen cachedWorld;
        static int cacheRevision;
        const int CachedChunks = 4096;
        [SerializeField, Min(.1f)] float terrainBudgetMs = 2;
        [SerializeField, Min(.02f)] float repaintInterval = .08f;
        readonly Dictionary<Vector2Int, Color32[]> visibleTiles = new Dictionary<Vector2Int, Color32[]>();
        int paintedX = int.MinValue, paintedY, paintedStep, paintedRevision = -1;
        int paintedCacheRevision = -1;
        float paintedBound = -1, nextPaint;
        bool incomplete;
        readonly List<Text> markers = new List<Text>();
        Texture2D tex;
        Material mistMaterial;
        Color32[] px;
        int used;
        MapRouteGraphic route;

        public void DrawRoute(Player player)
        {
            if (route == null)
            {
                var go = new GameObject("Movement route", typeof(RectTransform), typeof(CanvasRenderer), typeof(MapRouteGraphic));
                go.transform.SetParent(Rect, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                go.transform.SetAsFirstSibling();
                route = go.GetComponent<MapRouteGraphic>();
                route.raycastTarget = false;
            }
            route.Points.Clear();
            if (player != null && player.MovementPath.Count > 0)
            {
                route.Points.Add(WorldToLocal(player.Pos));
                foreach (var point in player.MovementPath) route.Points.Add(WorldToLocal(point));
            }
            route.SetVerticesDirty();
        }

        public Vector2 Center;          // world point at the middle
        public float PixelsPerUnit = 3; // screen px per world unit (canvas units)
        public RectTransform Rect => (RectTransform)image.transform;

        /// <summary>Repaint the visible area. include(cx, cy) decides which chunks are drawn.</summary>
        const int MaxTexels = 512;

        public void Paint(System.Func<int, int, bool> include, int revision = 0, float frontier = 0)
        {
            used = 0;
            if (cachedWorld != WorldStreamer.I.Gen) { ClearCache(); cachedWorld = WorldStreamer.I.Gen; }
            var size = Rect.rect.size;
            // One texel per world tile when zoomed in (point-filtered = crisp). Zoomed far out, one texel covers several
            // tiles (step) so the texture stays ≤ MaxTexels. The texel size must be the same on both axes and the texture
            // must cover the whole view; capping only the texel count (the old code) shrank the painted area while the
            // markers kept the real scale, so map icons drifted away from the terrain at the widest zoom.
            float visW = size.x / PixelsPerUnit, visH = size.y / PixelsPerUnit;              // world units on screen
            int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(visW, visH) / (MaxTexels - 2)));
            int tw = Mathf.Clamp(Mathf.CeilToInt(visW / step) + 2, 8, MaxTexels), th = Mathf.Clamp(Mathf.CeilToInt(visH / step) + 2, 8, MaxTexels);
            if (tex == null || tex.width != tw || tex.height != th)
            {
                if (tex != null) Destroy(tex);
                tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                px = new Color32[tw * th];
                image.texture = tex;
                paintedX = int.MinValue;
            }
            // Texture origin snapped to whole texels (world units, multiple of step)
            int x0 = MathX.FloorDiv(Mathf.FloorToInt(Center.x - visW / 2f), step) * step;
            int y0 = MathX.FloorDiv(Mathf.FloorToInt(Center.y - visH / 2f), step) * step;
            // Sub-tile panning changes only the UVs. Terrain is uploaded at most 12.5 times per second.
            float fx = (Center.x - visW / 2f - x0) / step, fy = (Center.y - visH / 2f - y0) / step;
            image.uvRect = new UnityEngine.Rect(fx / tw, fy / th, visW / step / tw, visH / step / th);
            bool changed = x0 != paintedX || y0 != paintedY || step != paintedStep || revision != paintedRevision || frontier != paintedBound || paintedCacheRevision != cacheRevision;
            if ((!changed && !incomplete) || Time.unscaledTime < nextPaint && paintedX != int.MinValue && paintedStep == step)
            {
                image.uvRect = new UnityEngine.Rect((Center.x - visW / 2f - paintedX) / step / tw,
                    (Center.y - visH / 2f - paintedY) / step / th, visW / step / tw, visH / step / th);
                SetMist(frontier, paintedX, paintedY, tw * step, th * step);
                return;
            }
            nextPaint = Time.unscaledTime + repaintInterval;
            paintedX = x0; paintedY = y0; paintedStep = step; paintedRevision = revision; paintedBound = frontier;
            paintedCacheRevision = cacheRevision;
            int N = WorldGen.Chunk;
            Color32 bg = background;
            // Include tests happen once per chunk, never once per pixel. Map generation never fills the live ground cache.
            visibleTiles.Clear(); incomplete = false;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            double budget = terrainBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0;
            for (int cy = MathX.FloorDiv(y0, N); cy <= MathX.FloorDiv(y0 + (th - 1) * step, N); cy++)
                for (int cx = MathX.FloorDiv(x0, N); cx <= MathX.FloorDiv(x0 + (tw - 1) * step, N); cx++)
                {
                    if (!include(cx, cy)) continue;
                    var key = new Vector2Int(cx, cy);
                    if (!tiles.TryGetValue(key, out var tile) || tile.filled < N * N)
                    {
                        if (System.Diagnostics.Stopwatch.GetTimestamp() - started >= budget) { incomplete = true; continue; }
                        tile = Tile(cx, cy, started, budget);
                    }
                    if (tile.filled < N * N) { incomplete = true; continue; }
                    visibleTiles[key] = tile.pixels;
                }
            SetMist(frontier, x0, y0, tw * step, th * step);
            for (int y = 0; y < th; y++)
            {
                int wy = y0 + y * step, cy = MathX.FloorDiv(wy, N);
                for (int x = 0; x < tw;)
                {
                    int wx = x0 + x * step, cx = MathX.FloorDiv(wx, N);
                    int end = Mathf.Min(tw, Mathf.CeilToInt(((cx + 1) * N - x0) / (float)step));
                    if (visibleTiles.TryGetValue(new Vector2Int(cx, cy), out var colors))
                        for (; x < end; x++) px[y * tw + x] = colors[(wy - cy * N) * N + x0 + x * step - cx * N];
                    else
                        for (; x < end; x++) px[y * tw + x] = bg;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            // uvRect in texels: where the view's left/bottom edge falls inside the texture, and how much of it is visible
        }

        void SetMist(float bound, float x, float y, float width, float height)
        {
            if (bound <= 0) { if (mistMaterial != null) image.material = null; return; }
            if (mistMaterial == null)
            {
                var shader = Resources.Load<Shader>("Shaders/MapMist");
                if (shader == null) return;
                mistMaterial = new Material(shader) { name = "World map mist (runtime)" };
            }
            image.material = mistMaterial;
            mistMaterial.SetVector("_WorldRect", new Vector4(x, y, width, height));
            mistMaterial.SetFloat("_Bound", bound);
            mistMaterial.SetFloat("_Width", Mathf.Max(3 / PixelsPerUnit, 2));
            mistMaterial.SetColor("_MistColor", DB.ArtStyle.frontierMapColor);
        }

        static MapTile Tile(int cx, int cy, long started, double budget)
        {
            var k = new Vector2Int(cx, cy);
            int N = WorldGen.Chunk;
            if (!tiles.TryGetValue(k, out var t))
            {
                // Evict individual entries, avoiding the old all-at-once regeneration spike.
                while (tiles.Count >= CachedChunks) tiles.Remove(tileOrder.Dequeue());
                tiles[k] = t = new MapTile(); tileOrder.Enqueue(k);
            }
            var gen = WorldStreamer.I.Gen;
            while (t.filled < N * N)
            {
                int x = t.filled % N, y = t.filled / N;
                Art.GroundColors(gen.GroundAt(cx * N + x, cy * N + y), out var a, out var b, out _);
                t.pixels[t.filled++] = Hash.F(cx * N + x, cy * N + y, 9) > 0.8f ? b : a;
                if (System.Diagnostics.Stopwatch.GetTimestamp() - started >= budget) break;
            }
            return t;
        }

        /// <summary>Forget cached tiles (new world seed).</summary>
        public static void ClearCache() { tiles.Clear(); tileOrder.Clear(); cachedWorld = null; cacheRevision++; }

        public Vector2 WorldToLocal(Vector2 w) => new Vector2((w.x - Center.x) * PixelsPerUnit, (w.y - Center.y) * PixelsPerUnit) + Rect.rect.center;

        public Vector2 LocalToWorld(Vector2 local) => Center + (local - Rect.rect.center) / PixelsPerUnit;

        /// <summary>Place a marker; call after Paint each frame. clampEdge keeps it on the border when off-screen.</summary>
        public void Marker(Vector2 world, string icon, Color col, string name, bool clampEdge, int fontSize = 0)
        {
            var p = WorldToLocal(world);
            var r = Rect.rect;
            bool outside = !r.Contains(p);
            if (outside && !clampEdge) return;
            if (outside) { p.x = Mathf.Clamp(p.x, r.xMin + 6, r.xMax - 6); p.y = Mathf.Clamp(p.y, r.yMin + 6, r.yMax - 6); }
            UIKit.Pool(markerTemplate, markers, Mathf.Max(markers.Count, used + 1));
            var m = markers[used++];
            UIKit.Show(m, true);
            m.rectTransform.localPosition = p;
            m.text = icon;
            m.color = col;
            m.fontSize = fontSize > 0 ? fontSize : markerTemplate.fontSize;   // pooled: reset sizes set by earlier markers
            var label = m.transform.Find("Name")?.GetComponent<Text>();
            if (label != null)
            {
                UIKit.Show(label, name != null && !outside);
                if (name != null) { label.text = name; label.color = col; }
            }
        }

        /// <summary>Hide markers not placed this frame.</summary>
        public void EndMarkers()
        {
            for (int i = used; i < markers.Count; i++) UIKit.Show(markers[i], false);
        }

        void OnDestroy() { if (tex != null) Destroy(tex); if (mistMaterial != null) Destroy(mistMaterial); }
    }

    // Clip every segment to the map before building a constant-width UI line.
    public class MapRouteGraphic : MaskableGraphic
    {
        public readonly List<Vector2> Points = new List<Vector2>();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            for (int i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1];
                var d = Points[i] - a;
                float lo = 0, hi = 1;
                if (!Clip(-d.x, a.x - rect.xMin + 1, ref lo, ref hi) ||
                    !Clip(d.x, rect.xMax - a.x + 1, ref lo, ref hi) ||
                    !Clip(-d.y, a.y - rect.yMin + 1, ref lo, ref hi) ||
                    !Clip(d.y, rect.yMax - a.y + 1, ref lo, ref hi)) continue;
                var start = a + d * lo;
                var end = a + d * hi;
                if ((end - start).sqrMagnitude < 0.001f) continue;
                var normal = new Vector2(-d.y, d.x).normalized;
                // Keep the two-pixel stroke inside the map even without a parent mask.
                start.x = Mathf.Clamp(start.x, rect.xMin + 1, rect.xMax - 1);
                start.y = Mathf.Clamp(start.y, rect.yMin + 1, rect.yMax - 1);
                end.x = Mathf.Clamp(end.x, rect.xMin + 1, rect.xMax - 1);
                end.y = Mathf.Clamp(end.y, rect.yMin + 1, rect.yMax - 1);
                int n = vh.currentVertCount;
                vh.AddVert(start - normal, Color.white, Vector2.zero);
                vh.AddVert(start + normal, Color.white, Vector2.zero);
                vh.AddVert(end + normal, Color.white, Vector2.zero);
                vh.AddVert(end - normal, Color.white, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2);
                vh.AddTriangle(n, n + 2, n + 3);
            }
        }

        static bool Clip(float p, float q, ref float lo, ref float hi)
        {
            if (Mathf.Abs(p) < 0.00001f) return q >= 0;
            float t = q / p;
            if (p < 0) { if (t > hi) return false; lo = Mathf.Max(lo, t); }
            else { if (t < lo) return false; hi = Mathf.Min(hi, t); }
            return true;
        }
    }
}
