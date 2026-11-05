using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>One prefab graphic traces occupied cells, including concavities and disconnected cells.</summary>
    public class InventoryFootprintGraphic : MaskableGraphic
    {
        [SerializeField] GridLayoutGroup grid;
        [SerializeField] float idleWidth = 1, activeWidth = 2, previewWidth = 2;
        [SerializeField] Color validFill = new Color(.3f, .9f, .5f, .16f), invalidFill = new Color(1, .3f, .3f, .22f);
        InventoryState bag;
        InventoryItem preview;
        int px, py;
        bool valid;
        public void Bind(InventoryState value) { bag = value; SetVerticesDirty(); }
        public void Preview(InventoryItem item, int x, int y, bool allowed)
        { preview = item; px = x; py = y; valid = allowed; SetVerticesDirty(); }
        public void ClearPreview() { preview = null; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (bag == null || grid == null) return;
            foreach (var weapon in bag.items.Where(i => i.kind == ItemKind.Weapon && i.Placed))
                Draw(vh, weapon, weapon.x, weapon.y, weapon == bag.Active ? activeWidth : idleWidth, false);
            if (preview != null) Draw(vh, preview, px, py, previewWidth, true);
        }
        void Draw(VertexHelper vh, InventoryItem item, int x, int y, float width, bool ghost)
        {
            var shape = item.kind == ItemKind.Weapon ? ActiveSkillGenerator.Shape(item) : null;
            var cells = new HashSet<Vector2Int>(shape?.Cells ?? new[] { Vector2Int.zero });
            Color edge = shape != null ? shape.outlineColor : color;
            if (ghost) edge.a = .95f;
            Vector2 pitch = grid.cellSize + grid.spacing;
            float scale = canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1;
            float Snap(float value) => Mathf.Round(value * scale) / scale;
            width = Mathf.Max(1, Mathf.Round(width * scale)) / scale;
            float half = width * .5f;
            foreach (var d in cells)
            {
                float left = Snap(grid.padding.left + (x + d.x) * pitch.x - grid.spacing.x * .5f);
                float top = Snap(-grid.padding.top - (y + d.y) * pitch.y + grid.spacing.y * .5f);
                float right = Snap(left + pitch.x), bottom = Snap(top - pitch.y);
                if (ghost) Quad(vh, left, bottom, right - left, top - bottom, valid ? validFill : invalidFill);
                // Centered strips share a square joint at every endpoint, including inward corners.
                if (!cells.Contains(d + Vector2Int.up)) Quad(vh, left - half, bottom - half, right - left + width, width, edge);
                if (!cells.Contains(d + Vector2Int.down)) Quad(vh, left - half, top - half, right - left + width, width, edge);
                if (!cells.Contains(d + Vector2Int.left)) Quad(vh, left - half, bottom - half, width, top - bottom + width, edge);
                if (!cells.Contains(d + Vector2Int.right)) Quad(vh, right - half, bottom - half, width, top - bottom + width, edge);
            }
        }
        static void Quad(VertexHelper vh, float x, float y, float w, float h, Color c)
        {
            int n = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y), c, Vector2.zero); vh.AddVert(new Vector3(x, y + h), c, Vector2.zero);
            vh.AddVert(new Vector3(x + w, y + h), c, Vector2.zero); vh.AddVert(new Vector3(x + w, y), c, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
