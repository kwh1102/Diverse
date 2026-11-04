using UnityEngine;
using UnityEngine.UI;

namespace Diverse
{
    /// <summary>A broad light sweep integrated into a chamfered frame, without separate orbiting particles.</summary>
    public sealed class ButtonBorderLight : MaskableGraphic
    {
        public float Strength = 1;
        readonly Vector2[] corners = new Vector2[9];

        void Update() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 4 || rect.height <= 4) return;
            float left = rect.xMin + 2, right = rect.xMax - 2;
            float bottom = rect.yMin + 2, top = rect.yMax - 2;
            float cut = Mathf.Min(4, rect.height * 0.2f);
            corners[0] = new Vector2(left + cut, top);
            corners[1] = new Vector2(right - cut, top);
            corners[2] = new Vector2(right, top - cut);
            corners[3] = new Vector2(right, bottom + cut);
            corners[4] = new Vector2(right - cut, bottom);
            corners[5] = new Vector2(left + cut, bottom);
            corners[6] = new Vector2(left, bottom + cut);
            corners[7] = new Vector2(left, top - cut);
            corners[8] = corners[0];
            float perimeter = 0;
            for (int i = 0; i < 8; i++) perimeter += Vector2.Distance(corners[i], corners[i + 1]);
            if (perimeter < 1) return;
            float walked = 0;
            for (int side = 0; side < 8; side++)
            {
                var start = corners[side]; var end = corners[side + 1];
                var direction = (end - start).normalized;
                var inward = new Vector2(direction.y, -direction.x);
                float length = Vector2.Distance(start, end);
                int segments = Mathf.Max(1, Mathf.CeilToInt(length / 3));
                for (int i = 0; i < segments; i++)
                {
                    float t0 = i / (float)segments, t1 = (i + 1f) / segments;
                    var a = Vector2.Lerp(start, end, t0); var b = Vector2.Lerp(start, end, t1);
                    var ca = Light((walked + length * t0) / perimeter);
                    var cb = Light((walked + length * t1) / perimeter);
                    var haloA = ca; haloA.a *= 0.13f;
                    var haloB = cb; haloB.a *= 0.13f;
                    Quad(mesh, a - inward * 2.5f, b - inward * 2.5f, b + inward * 2.5f, a + inward * 2.5f, haloA, haloB);
                    haloA.a *= 1.4f; haloB.a *= 1.4f;
                    Quad(mesh, a - inward * 1.5f, b - inward * 1.5f, b + inward * 1.5f, a + inward * 1.5f, haloA, haloB);
                    Quad(mesh, a - inward * 0.5f, b - inward * 0.5f, b + inward * 0.5f, a + inward * 0.5f, ca, cb);
                }
                walked += length;
            }
        }

        Color Light(float fraction)
        {
            float wave = Mathf.Pow(0.5f + 0.5f * Mathf.Cos((fraction - Time.unscaledTime * 0.14f) * Mathf.PI * 2), 5);
            var tint = Color.Lerp(color, Color.white, wave * 0.7f);
            tint.a = Strength * (0.22f + wave * 0.65f);
            return tint;
        }

        static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color first, Color second)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, first, Vector2.zero); mesh.AddVert(b, second, Vector2.zero);
            mesh.AddVert(c, second, Vector2.zero); mesh.AddVert(d, first, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
