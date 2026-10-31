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
        readonly List<Transform> sway = new List<Transform>();   // designed-world decorations that move in the wind
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
            var tiles = w.GroundOf(k);
            groundTex = w.TakeBakedGround(k) ?? TileBaker.Bake(w, k);
            ground.sprite = Sprite.Create(groundTex, new Rect(0, 0, N * Art.PPU, N * Art.PPU), Vector2.zero, Art.PPU, 0, SpriteMeshType.FullRect);
            ground.sharedMaterial = Art.SpriteMat;
            ground.transform.position = origin;

            // 2) 구조물 영역 계산 (장식이 구조물 위에 생기지 않도록)
            var structs = new List<StructureSpec>(w.Gen.StructuresNear(origin + Vector2.one * N / 2f, N));
            foreach (var s in w.Gen.StructuresInChunk(k.x, k.y))
                World.I.BuildStructure(this, s);

            // 3) Scenery from the theme of each tile (ThemeDef.decor): open ground only, a few kinds also on cliff tops
            var rng = new Rng(Hash.U(k.x, k.y, w.Gen.seed + 4242));
            for (int ty = 0; ty < N; ty++)
                for (int tx = 0; tx < N; tx++)
                {
                    int wx = k.x * N + tx, wy = k.y * N + ty;
                    var g = tiles[tx + 1, ty + 1];
                    if (WorldGen.IsLiquid(g) || g == Ground.Path || g == Ground.Plaza || g == Ground.Wood) continue;
                    var p = new Vector2(wx + 0.5f, wy + 0.5f);
                    if (InStructure(structs, p, 1.5f)) continue;
                    // A designed world arranges its own scenery by layout (streets, groves, rows…) instead of the theme rules
                    if (DesignLayout.Active)
                    {
                        if (p.magnitude > WorldGen.TownRadius + 3 && WorldGen.Level(g) == 0) DesignLayout.Decorate(this, tiles, tx, ty, wx, wy, p, w.Gen.seed);
                        continue;
                    }
                    int level = WorldGen.Level(g);
                    // A cliff top shows only where the ground in front of it isn't as high; ground hidden behind a cliff gets nothing
                    if (level > 0 ? WorldGen.Level(tiles[tx + 1, ty]) >= level : !w.OpenTile(wx, wy)) continue;
                    var theme = w.Gen.ThemeAt(p.x, p.y);
                    float density = Noise.Perlin(p.x, p.y, w.Gen.seed + 333, 0.05f) * 2f;   // groves and clearings
                    float r = Hash.F(wx, wy, w.Gen.seed + 17);
                    bool nearTown = p.magnitude < WorldGen.TownRadius + 3;
                    float acc = 0;
                    foreach (var d in theme.decor)
                    {
                        float chance = d.chance * theme.density * (d.clustered ? density : 1);
                        if (r >= (acc += chance)) continue;
                        if (level > 0 && !d.onCliffs) break;
                        bool big = d.block && d.width >= 0.9f;
                        if (big && (nearTown || tx % 2 != 0 || ty % 2 != 1)) break;      // big things on a sparse grid, none in town
                        var sp = Art.Decor(d.Key(Hash.U(wx, wy, 5)));
                        var at = p + (d.flat ? new Vector2(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f)) : big ? Vector2.zero : new Vector2(rng.Range(-0.3f, 0.3f), 0));
                        if (level > 0) AddDecor(sp, at + Vector2.up * (level * WorldGen.FaceHeight), false, 0, 0, d.flat);   // on the raised top
                        else AddDecor(sp, at, d.block, d.width, d.depth, d.flat);
                        break;
                    }
                }
            World.I.OnChunkBuilt(this);
        }

        static bool InStructure(List<StructureSpec> list, Vector2 p, float pad)
        {
            foreach (var s in list) if (Vector2.Distance(s.center, p) < s.radius + pad) return true;
            return false;
        }

        /// <summary>
        /// Place a decoration. When block is set, a footprint of bw×bh (world units) centered just above p blocks movement.
        /// Footprints default to ellipses (natural shapes); pass shape for tents (triangle) or crates (rect).
        /// </summary>
        public SpriteRenderer AddDecor(Sprite s, Vector2 p, bool block, float bw, float bh, bool flat = false, string name = "decor", Footprint shape = Footprint.Ellipse)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(Mathf.Round(p.x * Art.PPU) / Art.PPU, Mathf.Round(p.y * Art.PPU) / Art.PPU, 0);
            var sr = Art.MakeRenderer(go, s, flat ? -31000 : Art.SortY(p.y));
            if (!flat && name == "decor" && DesignLayout.Active) sway.Add(go.transform);
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

        void Update()
        {
            if (sway.Count == 0) return;
            float t = Time.time;
            foreach (var s in sway)
                if (s != null) s.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.4f + s.position.x * 0.9f + s.position.y * 0.4f) * 1.6f);
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
