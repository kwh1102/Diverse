using UnityEngine;

namespace Diverse
{
    /// <summary>One procedural world-effect quad: no particles, textures or per-frame managed allocations.</summary>
    public class FrontierMist : MonoBehaviour
    {
        Mesh mesh;
        Material material;
        MeshRenderer view;
        Transform surface;

        void LateUpdate()
        {
            var player = Game.I != null ? Game.I.Player : null;
            var camera = CameraRig.I;
            float bound = player != null ? player.Bound : 0;
            var style = DB.ArtStyle;
            bool visible = bound > 0 && camera != null &&
                player.Pos.magnitude + camera.Cam.orthographicSize * Mathf.Sqrt(1 + camera.Cam.aspect * camera.Cam.aspect) + style.frontierMistWidth > bound;
            if (view != null) view.enabled = visible;
            if (!visible) return;
            if (view == null)
            {
                var shader = Resources.Load<Shader>("Shaders/FrontierMist");
                if (shader == null) return;
                material = new Material(shader) { name = "Mist of Oblivion (runtime)" };
                mesh = new Mesh { name = "Frontier mist quad" };
                mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                var go = new GameObject("Mist of Oblivion", typeof(MeshFilter), typeof(MeshRenderer));
                surface = go.transform; surface.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                view = go.GetComponent<MeshRenderer>(); view.sharedMaterial = material; view.sortingOrder = 32000;
                view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.receiveShadows = false;
            }
            var rect = camera.ViewRect();
            surface.position = new Vector3(rect.center.x, rect.center.y, 0);
            // Cover camera look-ahead and render-texture captures with a different aspect ratio as well.
            surface.localScale = new Vector3(rect.width * 2 + 2, rect.height * 2 + 2, 1);
            material.SetFloat("_Bound", bound);
            material.SetFloat("_Width", style.frontierMistWidth);
            material.SetFloat("_Drift", Time.time * style.frontierMistSpeed);
            material.SetColor("_MistColor", style.frontierMistColor);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }
    }
}
