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

        static readonly Dictionary<Vector2Int, Color32[]> tiles = new Dictionary<Vector2Int, Color32[]>();
        readonly List<Text> markers = new List<Text>();
        Texture2D tex;
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
        const int MaxTexels = 1024;

        public void Paint(System.Func<int, int, bool> include)
        {
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
            }
            // Texture origin snapped to whole texels (world units, multiple of step)
            int x0 = MathX.FloorDiv(Mathf.FloorToInt(Center.x - visW / 2f), step) * step;
            int y0 = MathX.FloorDiv(Mathf.FloorToInt(Center.y - visH / 2f), step) * step;
            int N = WorldGen.Chunk;
            Color32 bg = background;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    int wx = x0 + x * step, wy = y0 + y * step;
                    int cx = MathX.FloorDiv(wx, N), cy = MathX.FloorDiv(wy, N);
                    px[y * tw + x] = include(cx, cy) ? Tile(cx, cy)[(wy - cy * N) * N + (wx - cx * N)] : bg;
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            // uvRect in texels: where the view's left/bottom edge falls inside the texture, and how much of it is visible
            float fx = (Center.x - visW / 2f - x0) / step, fy = (Center.y - visH / 2f - y0) / step;
            image.uvRect = new UnityEngine.Rect(fx / tw, fy / th, visW / step / tw, visH / step / th);
            used = 0;
        }

        static Color32[] Tile(int cx, int cy)
        {
            var k = new Vector2Int(cx, cy);
            if (tiles.TryGetValue(k, out var t)) return t;
            int N = WorldGen.Chunk;
            t = new Color32[N * N];
            var ground = WorldStreamer.I.GroundOf(k);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Art.GroundColors(ground[x + 1, y + 1], out var a, out var b, out _);
                    t[y * N + x] = Hash.F(cx * N + x, cy * N + y, 9) > 0.8f ? b : a;
                }
            if (tiles.Count > 3000) tiles.Clear();
            tiles[k] = t;
            return t;
        }

        /// <summary>Forget cached tiles (new world seed).</summary>
        public static void ClearCache() => tiles.Clear();

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

        void OnDestroy() { if (tex != null) Destroy(tex); }
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
