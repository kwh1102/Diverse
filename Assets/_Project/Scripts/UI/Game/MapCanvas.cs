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

        public Vector2 Center;          // world point at the middle
        public float PixelsPerUnit = 3; // screen px per world unit (canvas units)
        public RectTransform Rect => (RectTransform)image.transform;

        /// <summary>Repaint the visible area. include(cx, cy) decides which chunks are drawn.</summary>
        public void Paint(System.Func<int, int, bool> include)
        {
            var size = Rect.rect.size;
            // One texel per world tile when zoomed in, so the texture stays small (point-filtered = crisp)
            int tw = Mathf.Clamp(Mathf.CeilToInt(size.x / PixelsPerUnit), 8, 1024), th = Mathf.Clamp(Mathf.CeilToInt(size.y / PixelsPerUnit), 8, 1024);
            if (tex == null || tex.width != tw || tex.height != th)
            {
                if (tex != null) Destroy(tex);
                tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                px = new Color32[tw * th];
                image.texture = tex;
            }
            int x0 = Mathf.FloorToInt(Center.x - tw / 2f), y0 = Mathf.FloorToInt(Center.y - th / 2f);
            int N = WorldGen.Chunk;
            Color32 bg = background;
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                {
                    int wx = x0 + x, wy = y0 + y;
                    int cx = MathX.FloorDiv(wx, N), cy = MathX.FloorDiv(wy, N);
                    px[y * tw + x] = include(cx, cy) ? Tile(cx, cy)[(wy - cy * N) * N + (wx - cx * N)] : bg;
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            // Sub-tile offset so panning is smooth even though the texture snaps to whole tiles
            float fx = (Center.x - tw / 2f) - x0, fy = (Center.y - th / 2f) - y0;
            image.uvRect = new UnityEngine.Rect(fx / tw, fy / th, size.x / PixelsPerUnit / tw, size.y / PixelsPerUnit / th);
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
            if (fontSize > 0) m.fontSize = fontSize;
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
}
