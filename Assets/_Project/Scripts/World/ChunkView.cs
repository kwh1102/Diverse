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
            var tiles = w.GroundOf(k);
            groundTex = w.TakeBakedGround(k) ?? Art.BakeGround(tiles, N, k.x * N, k.y * N, w.Gen.seed);
            ground.sprite = Sprite.Create(groundTex, new Rect(0, 0, N * Art.PPU, N * Art.PPU), Vector2.zero, Art.PPU, 0, SpriteMeshType.FullRect);
            ground.transform.position = origin;

            // 2) 구조물 영역 계산 (장식이 구조물 위에 생기지 않도록)
            var structs = new List<StructureSpec>(w.Gen.StructuresNear(origin + Vector2.one * N / 2f, N));
            structs.RemoveAll(s => s.kind is StructKind.Keep or StructKind.Village);   // their yards are overgrown: scenery grows there by tile
            foreach (var s in w.Gen.StructuresInChunk(k.x, k.y))
                World.I.BuildStructure(this, s);

            // 3) 장식
            var rng = new Rng(Hash.U(k.x, k.y, w.Gen.seed + 4242));
            for (int ty = 0; ty < N; ty++)
                for (int tx = 0; tx < N; tx++)
                {
                    int wx = k.x * N + tx, wy = k.y * N + ty;
                    var g = tiles[tx + 1, ty + 1];
                    if (WorldGen.IsWater(g) || WorldGen.IsSolid(g) || g == Ground.Path || g == Ground.Plaza || g == Ground.Wood || g == Ground.Ford || g == Ground.Rubble) continue;
                    var p = new Vector2(wx + 0.5f, wy + 0.5f);
                    if (InStructure(structs, p, 1.5f)) continue;
                    if (p.y > 0 && p.y < WorldGen.TowerPos.y && Mathf.Abs(p.x - Mathf.Sin(p.y * 0.08f) * 3f) < 3f) continue;
                    // A designed world arranges its own scenery by layout (streets, groves, rows…) instead of the biome rules
                    if (DesignLayout.Active)
                    {
                        if (p.magnitude > WorldGen.TownRadius + 3) DesignLayout.Decorate(this, tiles, tx, ty, wx, wy, p, w.Gen.seed);
                        continue;
                    }
                    var biome = w.Gen.BiomeAt(p.x, p.y);
                    float density = Noise.Perlin(p.x, p.y, w.Gen.seed + 333, 0.05f);
                    float r = Hash.F(wx, wy, w.Gen.seed + 17);

                    // Foothills: loose boulders and nothing else — the range's silhouette spreads out into the land
                    if (g == Ground.Scree)
                    {
                        if (r < 0.055f && (tx + ty) % 2 == 0) AddDecor(Art.Rock(biome == Biome.Meadow || biome == Biome.Forest ? Biome.Ashland : biome, (int)(Hash.U(wx, wy, 6) % 3)), p, true, 0.85f, 0.55f);
                        else if (r < 0.08f) AddDecor(Art.Tuft(Biome.Ashland, (int)(Hash.U(wx, wy, 8) % 2)), p + new Vector2(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f)), false, 0, 0, flat: true);
                        continue;
                    }
                    // Riverside wetland: reeds, marsh shrubs, a drooping tree now and then
                    if (g == Ground.Swamp && biome != Biome.Marsh)
                    {
                        if (r < 0.02f && tx % 2 == 0 && ty % 2 == 1) AddDecor(Art.Tree(Biome.Marsh, (int)(Hash.U(wx, wy, 5) % 4)), p, true, 1.0f, 0.6f);
                        else if (r < 0.05f) AddDecor(Art.Bush(Biome.Marsh, (int)(Hash.U(wx, wy, 7) % 2)), p + new Vector2(rng.Range(-0.3f, 0.3f), 0), false, 0, 0);
                        else if (r < 0.18f) AddDecor(Art.Tuft(Biome.Marsh, (int)(Hash.U(wx, wy, 8) % 2)), p + new Vector2(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f)), false, 0, 0, flat: true);
                        continue;
                    }

                    float treeChance = biome switch
                    {
                        // The drawn scenery is big and detailed: sparse placement keeps the ground readable and the paths open
                        Biome.Forest => 0.075f * density * 2f, Biome.Meadow => 0.03f * density * 2f, Biome.Tundra => 0.04f * density * 2f,
                        Biome.Marsh => 0.03f, Biome.Dunes => 0.01f, Biome.Starlit => 0.025f * density * 2f, Biome.Hive => 0.025f, _ => 0.015f,
                    };
                    if (p.magnitude < WorldGen.TownRadius + 3) treeChance = 0;
                    // Deep woods: a trunk on half the lattice cells (every other column and row, so there is always a gap to
                    // slip through), undergrowth between them, clearings where the forest noise dips
                    else if (treeChance > 0 && g == Ground.DarkGrass && w.Gen.ForestAt(wx, wy, biome))
                    {
                        if (tx % 2 == 0 && ty % 2 == 1) { if (r < 0.55f) AddDecor(Art.Tree(biome, (int)(Hash.U(wx, wy, 5) % 4)), p, true, 1.0f, 0.6f); }
                        else if (r < 0.04f) AddDecor(Art.Bush(biome, (int)(Hash.U(wx, wy, 7) % 2)), p + new Vector2(rng.Range(-0.3f, 0.3f), 0), false, 0, 0);
                        else if (r < 0.12f) AddDecor(Art.Tuft(biome, (int)(Hash.U(wx, wy, 8) % 2)), p + new Vector2(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f)), false, 0, 0, flat: true);
                        continue;
                    }

                    if (r < treeChance && tx % 2 == 0 && ty % 2 == 1)
                    {
                        // Only the trunk blocks — the canopy is drawn over the player
                        AddDecor(Art.Tree(biome, (int)(Hash.U(wx, wy, 5) % 4)), p, true, 1.0f, 0.6f);
                    }
                    else if (r < treeChance + 0.003f)
                    {
                        AddDecor(Art.Rock(biome, (int)(Hash.U(wx, wy, 6) % 3)), p, true, 0.85f, 0.55f);
                    }
                    else if (r < treeChance + 0.015f)
                    {
                        AddDecor(Art.Bush(biome, (int)(Hash.U(wx, wy, 7) % 2)), p + new Vector2(rng.Range(-0.3f, 0.3f), 0), false, 0, 0);
                    }
                    else if (r < treeChance + 0.05f)
                    {
                        var sp = biome == Biome.Meadow && Hash.F(wx, wy, 9) > 0.5f ? Art.Flower((int)(Hash.U(wx, wy, 8) % 5)) : Art.Tuft(biome, (int)(Hash.U(wx, wy, 8) % 2));
                        AddDecor(sp, p + new Vector2(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f)), false, 0, 0, flat: true);
                    }
                }
            // Keeps and villages spill over several chunks: each chunk places the props on its own tiles
            foreach (var site in w.Gen.Land.SitesNear(origin + Vector2.one * N / 2f, N))
                World.I.BuildSiteProps(this, site, tiles);
            World.I.OnChunkBuilt(this);
        }

        static bool InStructure(List<StructureSpec> list, Vector2 p, float pad)
        {
            foreach (var s in list) if (Vector2.Distance(s.center, p) < s.radius + pad) return true;
            if (Vector2.Distance(p, WorldGen.TowerPos) < 12) return true;
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
            if (block)
            {
                var sh = new GameObject("shadow");
                sh.transform.SetParent(go.transform, false);
                sh.transform.localPosition = new Vector3(0, 0.05f, 0);
                Art.MakeRenderer(sh, Art.Shadow(Mathf.RoundToInt(s.rect.width * 0.7f)), -30000);
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
