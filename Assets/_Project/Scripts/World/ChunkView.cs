using System.Collections.Generic;
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

        public void Build(WorldStreamer world, Vector2Int k)
        {
            w = world;
            key = k;
            int N = WorldGen.Chunk;
            var origin = new Vector2(k.x * N, k.y * N);
            transform.position = Vector3.zero;

            // 1) 지면
            var tiles = w.GroundOf(k);
            groundTex = Art.BakeGround(tiles, N, k.x * N, k.y * N, w.Gen.seed);
            ground.sprite = Sprite.Create(groundTex, new Rect(0, 0, N * Art.PPU, N * Art.PPU), Vector2.zero, Art.PPU, 0, SpriteMeshType.FullRect);
            ground.transform.position = origin;

            // 2) 구조물 영역 계산 (장식이 구조물 위에 생기지 않도록)
            var structs = new List<StructureSpec>(w.Gen.StructuresNear(origin + Vector2.one * N / 2f, N));
            foreach (var s in w.Gen.StructuresInChunk(k.x, k.y))
                World.I.BuildStructure(this, s);

            // 3) 장식
            var rng = new Rng(Hash.U(k.x, k.y, w.Gen.seed + 4242));
            for (int ty = 0; ty < N; ty++)
                for (int tx = 0; tx < N; tx++)
                {
                    int wx = k.x * N + tx, wy = k.y * N + ty;
                    var g = tiles[tx + 1, ty + 1];
                    if (WorldGen.IsWater(g) || g == Ground.Path || g == Ground.Plaza || g == Ground.Wood) continue;
                    var p = new Vector2(wx + 0.5f, wy + 0.5f);
                    if (InStructure(structs, p, 1.5f)) continue;
                    if (p.y > 0 && p.y < WorldGen.TowerPos.y && Mathf.Abs(p.x - Mathf.Sin(p.y * 0.08f) * 3f) < 3f) continue;
                    var biome = w.Gen.BiomeAt(p.x, p.y);
                    float density = Noise.Perlin(p.x, p.y, w.Gen.seed + 333, 0.05f);
                    float r = Hash.F(wx, wy, w.Gen.seed + 17);

                    float treeChance = biome switch
                    {
                        Biome.Forest => 0.16f * density * 2f, Biome.Meadow => 0.035f * density * 2f, Biome.Tundra => 0.06f * density * 2f,
                        Biome.Marsh => 0.05f, Biome.Dunes => 0.012f, _ => 0.02f,
                    };
                    if (p.magnitude < WorldGen.TownRadius + 3) treeChance = 0;

                    if (r < treeChance && tx % 2 == 0 && ty % 2 == 1)
                    {
                        // Only the trunk blocks — the canopy is drawn over the player
                        AddDecor(Art.Tree(biome, (int)(Hash.U(wx, wy, 5) % 4)), p, true, 1.0f, 0.6f);
                    }
                    else if (r < treeChance + 0.0025f)
                    {
                        AddDecor(Art.Rock(biome, (int)(Hash.U(wx, wy, 6) % 3)), p, true, 0.85f, 0.55f);
                    }
                    else if (r < treeChance + 0.03f && biome != Biome.Dunes && biome != Biome.Ashland)
                    {
                        AddDecor(Art.Bush(biome, (int)(Hash.U(wx, wy, 7) % 2)), p + new Vector2(rng.Range(-0.3f, 0.3f), 0), false, 0, 0);
                    }
                    else if (r < treeChance + 0.11f)
                    {
                        var sp = biome == Biome.Meadow && Hash.F(wx, wy, 9) > 0.5f ? Art.Flower((int)(Hash.U(wx, wy, 8) % 5)) : Art.Tuft(biome, (int)(Hash.U(wx, wy, 8) % 2));
                        AddDecor(sp, p + new Vector2(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f)), false, 0, 0, flat: true);
                    }
                }
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
            if (groundTex != null) Destroy(groundTex);
        }
    }
}
