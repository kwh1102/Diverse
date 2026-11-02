using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// 청크 하나의 화면 표현. 지면 텍스처 1장 + 장식(나무/바위/풀) + 구조물 + 몬스터 군집.
    /// 언로드 시 막아둔 타일과 생성한 적을 정리한다.
    /// </summary>
    public class ChunkView : MonoBehaviour
    {
        public Vector2Int key;
        readonly List<Solid> solids = new List<Solid>();
        readonly List<Enemy> spawned = new List<Enemy>();
        readonly List<Interactable> interactables = new List<Interactable>();
        [SerializeField] SpriteRenderer ground;     // baked per chunk at runtime
        Texture2D groundTex;
        WorldStreamer w;

        static readonly ProfilerMarker BuildMarker = new ProfilerMarker("Diverse.ChunkBuild");   // read by DesignPerf / PerfProbe

        public void Build(WorldStreamer world, Vector2Int k)
        {
            using var _perf = BuildMarker.Auto();
            w = world;
            key = k;
            int N = WorldGen.Chunk;
            var origin = new Vector2(k.x * N, k.y * N);
            transform.position = Vector3.zero;

            // 1) 지면
            // Pixel-art tiles from the art rules (TileBaker): patterns, tidy edges, cliffs and banks in the 3/4 view
            groundTex = w.TakeBakedGround(k) ?? TileBaker.Bake(w, k);
            ground.sprite = Sprite.Create(groundTex, new Rect(0, 0, N * Art.PPU, N * Art.PPU), Vector2.zero, Art.PPU, 0, SpriteMeshType.FullRect);
            ground.sharedMaterial = Art.SpriteMat;
            ground.transform.position = origin;

            // 2) Authored structure layouts; scenery reserves their complete surroundings.
            foreach (var s in w.Gen.StructuresInChunk(k.x, k.y))
                World.I.BuildStructure(this, s);

            // 3) Deterministic, silhouette-aware scenery; flat detail is already in the baked ground.
            w.Scenery.Build(this);
            World.I.OnChunkBuilt(this);
        }

        /// <summary>
        /// Place a decoration. When block is set, a footprint of bw×bh (world units) centered just above p blocks movement.
        /// Footprints default to ellipses (natural shapes); pass shape for tents (triangle) or crates (rect).
        /// </summary>
        public SpriteRenderer AddDecor(Sprite s, Vector2 p, bool block, float bw, float bh, bool flat = false, string name = "decor", Footprint shape = Footprint.Ellipse, float scale = 0)
        {
            if (scale <= 0) scale = w.Gen.ThemeAt(p.x, p.y).SceneryScale(s.name);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(Mathf.Round(p.x * Art.PPU) / Art.PPU, Mathf.Round(p.y * Art.PPU) / Art.PPU, 0);
            var sr = Art.MakeRenderer(go, s, flat ? -31000 : Art.SortY(p.y));
            go.transform.localScale = Vector3.one * scale;
            var profile = w.Gen.ThemeAt(p.x, p.y).scenery;
            if (!flat && profile.enabled && s.rect.height / Art.PPU * scale >= profile.occlusionHeight)
                go.AddComponent<SceneryOcclusion>().Init(sr, profile);
            if (block && bw > 0) solids.Add(w.AddSolid(shape, p + new Vector2(0, bh * 0.5f), bw, bh));
            if (block || !flat && s.rect.height >= 12)
            {
                // A shadow under everything that stands: tall things cast their silhouette toward the lower right
                var sh = new GameObject("shadow");
                sh.transform.SetParent(go.transform, false);
                sh.transform.localPosition = new Vector3(0, 0.05f, 0);
                Art.MakeRenderer(sh, s.rect.height >= 18 ? Art.CastShadow(s) : Art.Shadow(Mathf.RoundToInt(s.rect.width * 0.7f)), -30000);
            }
            return sr;
        }

        void OnDestroy()
        {
            // Sprite.Create wrappers are native Unity objects: destroying their renderer does not release them.
            if (ground != null && ground.sprite != null) Destroy(ground.sprite);
            if (groundTex != null) Destroy(groundTex);
        }

        public void BlockArea(Vector2 center, float wdt, float hgt, Footprint shape = Footprint.Rect) => solids.Add(w.AddSolid(shape, center, wdt, hgt));

        public void Register(Enemy e) => spawned.Add(e);
        public void Register(Interactable i) => interactables.Add(i);

        public void Unload()
        {
            foreach (var s in solids) w.RemoveSolid(s);
            solids.Clear();
            foreach (var e in spawned) if (e != null && e.Alive) e.Despawn();
            spawned.Clear();
            foreach (var i in interactables) if (i != null) Interactable.All.Remove(i);
            w.KeepBakedGround(key, groundTex);   // re-used if the player walks back here
            groundTex = null;
        }
    }
}
