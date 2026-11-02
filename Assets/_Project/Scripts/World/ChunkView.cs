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
        Texture2D environmentMask;
        WorldStreamer w;
        IEnumerator<int> building;
        public bool Ready { get; private set; }
        public string BuildPhase { get; private set; }

        static readonly ProfilerMarker BuildMarker = new ProfilerMarker("Diverse.ChunkBuild");   // read by DesignPerf / PerfProbe

        public void Build(WorldStreamer world, Vector2Int k)
        {
            BeginBuild(world,k);
            while (!StepBuild(float.MaxValue)) { }
        }

        public void BeginBuild(WorldStreamer world, Vector2Int k)
        {
            w = world; key = k; Ready = false;
            building = BuildSteps().GetEnumerator();
        }

        public bool StepBuild(float budgetMs)
        {
            using var _perf = BuildMarker.Auto();
            if (Ready) return true;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            double ticks = budgetMs*System.Diagnostics.Stopwatch.Frequency/1000.0;
            do
            {
                if (!building.MoveNext())
                {
                    building.Dispose(); building = null; Ready = true;
                    BuildPhase = "activate"; gameObject.SetActive(true); return true;
                }
            } while (System.Diagnostics.Stopwatch.GetTimestamp()-started < ticks);
            return false;
        }

        IEnumerable<int> BuildSteps()
        {
            var k = key;
            int N = WorldGen.Chunk;
            var origin = new Vector2(k.x * N, k.y * N);
            transform.position = Vector3.zero;
            BuildPhase = "ground";

            // 1) 지면
            // Pixel-art tiles from the art rules (TileBaker): patterns, tidy edges, cliffs and banks in the 3/4 view
            groundTex = w.TakeBakedGround(k) ?? TileBaker.Bake(w, k);
            ground.sprite = Sprite.Create(groundTex, new Rect(0, 0, N * Art.PPU, N * Art.PPU), Vector2.zero, Art.PPU, 0, SpriteMeshType.FullRect);
            ground.sharedMaterial = Art.SpriteMat;
            ground.transform.position = origin;
            environmentMask = w.Environment?.BindGround(ground, k);
            yield return 0;

            // 2) Authored structure layouts; scenery reserves their complete surroundings.
            foreach (var s in w.Gen.StructuresInChunk(k.x, k.y))
            {
                BuildPhase = "structure " + s.kind;
                World.I.BuildStructure(this, s);
                yield return 0;
            }

            // 3) Deterministic, silhouette-aware scenery; flat detail is already in the baked ground.
            BuildPhase = "scenery";
            foreach (int step in w.Scenery.BuildSteps(this)) yield return step;
            BuildPhase = "world memory";
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
            if (!flat) w.Environment?.Register(sr, s.name, w.Gen.ThemeAt(p.x, p.y));
            return sr;
        }

        void OnDestroy()
        {
            // Sprite.Create wrappers are native Unity objects: destroying their renderer does not release them.
            if (ground != null && ground.sprite != null) Destroy(ground.sprite);
            if (groundTex != null) Destroy(groundTex);
            if (environmentMask != null) Destroy(environmentMask);
        }

        public void BlockArea(Vector2 center, float wdt, float hgt, Footprint shape = Footprint.Rect) => solids.Add(w.AddSolid(shape, center, wdt, hgt));

        public void Register(Enemy e) => spawned.Add(e);
        public void Register(Interactable i) => interactables.Add(i);

        public void Unload()
        {
            building?.Dispose(); building = null; Ready = false;
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
