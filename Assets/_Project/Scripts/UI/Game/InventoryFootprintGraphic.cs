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
            float inheritedScale = canvas != null ? Mathf.Abs(rectTransform.lossyScale.x) / Mathf.Max(.0001f, Mathf.Abs(canvas.transform.lossyScale.x)) : 1;
            float scale = canvas != null ? Mathf.Max(.01f, canvas.scaleFactor * inheritedScale) : 1;
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
                bool bottomEdge=!cells.Contains(d+Vector2Int.up),topEdge=!cells.Contains(d+Vector2Int.down);
                bool leftEdge=!cells.Contains(d+Vector2Int.left),rightEdge=!cells.Contains(d+Vector2Int.right);
                if(bottomEdge) Rim(vh,left-half,bottom-half,right-left+width,width,edge,true);
                if(topEdge) Rim(vh,left-half,top-half,right-left+width,width,edge,true);
                if(leftEdge) Rim(vh,left-half,bottom-half,width,top-bottom+width,edge,false);
                if(rightEdge) Rim(vh,right-half,bottom-half,width,top-bottom+width,edge,false);
                // Small corner fittings decorate the continuous footprint, never the internal cell boundaries.
                Color light=Color.Lerp(edge,Color.white,.55f);
                void Corner(float cx,float cy)
                {Quad(vh,cx-1.5f,cy-1.5f,3,3,new Color(.14f,.11f,.19f,1));Quad(vh,cx-1,cy-1,2,2,light);Quad(vh,cx-.5f,cy,1,.5f,Color.white);}
                if(item==bag.Active && !ghost)
                {
                    if(leftEdge&&topEdge)Corner(left,top);if(rightEdge&&topEdge)Corner(right,top);
                    if(leftEdge&&bottomEdge)Corner(left,bottom);if(rightEdge&&bottomEdge)Corner(right,bottom);
                }
            }
        }
        static void Rim(VertexHelper vh,float x,float y,float w,float h,Color edge,bool horizontal)
        {
            var shadow=Color.Lerp(edge,Color.black,.72f);shadow.a=.95f;
            Quad(vh,x-.5f,y-.5f,w+1,h+1,shadow);
            Quad(vh,x,y,w,h,edge);
            var light=Color.Lerp(edge,Color.white,.35f);
            Quad(vh,x,y+(horizontal?h-.5f:0),horizontal?w:.5f,horizontal ? .5f : h,light);
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
