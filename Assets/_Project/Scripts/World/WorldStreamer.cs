using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public enum Footprint { Ellipse, Rect, Tent }

    /// <summary>
    /// The ground contact area of an obstacle (tree, rock, wall...). Movement collides with the real shape,
    /// so round objects are round and tents are triangles instead of tile-rounded rectangles.
    /// </summary>
    public class Solid
    {
        public Footprint kind;
        public Vector2 c, half;           // center, half size
        public Vector2 min, max;          // bounding box

        public Solid(Footprint kind, Vector2 c, Vector2 half)
        {
            this.kind = kind; this.c = c; this.half = half;
            min = c - half; max = c + half;
        }

        /// <summary>Is p inside the shape grown by pad?</summary>
        public bool Contains(Vector2 p, float pad = 0)
        {
            Vector2 d = p - c;
            switch (kind)
            {
                case Footprint.Rect:
                    return Mathf.Abs(d.x) <= half.x + pad && Mathf.Abs(d.y) <= half.y + pad;
                case Footprint.Tent:
                {
                    // Triangle: wide base at the bottom, apex at the top
                    if (d.y < -half.y - pad || d.y > half.y + pad) return false;
                    float k = Mathf.Clamp01((d.y + half.y) / (2 * half.y));
                    return Mathf.Abs(d.x) <= half.x * (1 - k) + pad;
                }
                default:
                {
                    float rx = half.x + pad, ry = half.y + pad;
                    return d.x * d.x / (rx * rx) + d.y * d.y / (ry * ry) <= 1;
                }
            }
        }
    }

    /// <summary>
    /// Chunk streaming. Keeps only the chunks around the player loaded and unloads distant ones.
    /// Obstacles are shapes (Solid) for movement; a tile grid derived from them is used for pathfinding.
    /// </summary>
    public class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer I { get; private set; }
        public WorldGen Gen { get; private set; }
        public int loadRadius = 2;          // chunks to load in each direction (5x5)
        public int unloadRadius = 3;

        readonly Dictionary<Vector2Int, ChunkView> chunks = new Dictionary<Vector2Int, ChunkView>();
        readonly Dictionary<Vector2Int, Ground[,]> groundCache = new Dictionary<Vector2Int, Ground[,]>();
        // Obstacle shapes, indexed by every tile their bounding box touches (point queries)
        readonly Dictionary<Vector2Int, List<Solid>> solidCells = new Dictionary<Vector2Int, List<Solid>>();
        // Tiles whose center is covered by an obstacle (pathfinding). Counted because shapes can overlap.
        readonly Dictionary<Vector2Int, int> blocked = new Dictionary<Vector2Int, int>();
        const float PathPad = 0.3f;         // ~ a body radius, so paths keep clear of shapes
        readonly Queue<Vector2Int> buildQueue = new Queue<Vector2Int>();
        Transform root;

        public static WorldStreamer Create(int seed)
        {
            var go = new GameObject("World");
            I = go.AddComponent<WorldStreamer>();
            I.Gen = new WorldGen(seed);
            I.root = go.transform;
            return I;
        }

        public void Clear()
        {
            foreach (var c in chunks.Values) if (c != null) Destroy(c.gameObject);
            chunks.Clear();
            blocked.Clear();
            solidCells.Clear();
            buildQueue.Clear();
        }

        public static Vector2Int ChunkOf(Vector2 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x / WorldGen.Chunk), Mathf.FloorToInt(p.y / WorldGen.Chunk));

        /// <summary>Build the chunks around the starting point right away (no stutter at game start).</summary>
        public void Warm(Vector2 p)
        {
            var c = ChunkOf(p);
            for (int y = -loadRadius; y <= loadRadius; y++)
                for (int x = -loadRadius; x <= loadRadius; x++)
                    Ensure(new Vector2Int(c.x + x, c.y + y));
        }

        public void Tick(Vector2 focus)
        {
            var c = ChunkOf(focus);
            // Queue chunks to load, nearest first
            for (int r = 0; r <= loadRadius; r++)
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != r) continue;
                        var k = new Vector2Int(c.x + x, c.y + y);
                        if (!chunks.ContainsKey(k) && !buildQueue.Contains(k)) buildQueue.Enqueue(k);
                    }
            // Build at most one per frame (spreads the load)
            if (buildQueue.Count > 0) Ensure(buildQueue.Dequeue());

            // Unload
            List<Vector2Int> remove = null;
            foreach (var kv in chunks)
            {
                if (Mathf.Abs(kv.Key.x - c.x) > unloadRadius || Mathf.Abs(kv.Key.y - c.y) > unloadRadius)
                    (remove ??= new List<Vector2Int>()).Add(kv.Key);
            }
            if (remove != null)
                foreach (var k in remove)
                {
                    var view = chunks[k];
                    view.Unload();
                    Destroy(view.gameObject);
                    chunks.Remove(k);
                }
        }

        void Ensure(Vector2Int k)
        {
            if (chunks.ContainsKey(k)) return;
            var go = new GameObject($"Chunk {k.x},{k.y}");
            go.transform.SetParent(root, false);
            var view = go.AddComponent<ChunkView>();
            chunks[k] = view;
            view.Build(this, k);
        }

        public bool IsLoaded(Vector2 p) => chunks.ContainsKey(ChunkOf(p));

        public ChunkView ChunkAt(Vector2 p) => chunks.TryGetValue(ChunkOf(p), out var c) ? c : null;

        // ───────────── Terrain queries ─────────────

        /// <summary>Tile array for a chunk (+1 border). Cached once computed.</summary>
        public Ground[,] GroundOf(Vector2Int k)
        {
            if (groundCache.TryGetValue(k, out var g)) return g;
            int N = WorldGen.Chunk;
            g = new Ground[N + 2, N + 2];
            for (int y = -1; y <= N; y++)
                for (int x = -1; x <= N; x++)
                    g[x + 1, y + 1] = Gen.GroundAt(k.x * N + x, k.y * N + y);
            if (groundCache.Count > 400) groundCache.Clear();
            groundCache[k] = g;
            return g;
        }

        public Ground GroundAtTile(int tx, int ty)
        {
            var k = new Vector2Int(MathX.FloorDiv(tx, WorldGen.Chunk), MathX.FloorDiv(ty, WorldGen.Chunk));
            var g = GroundOf(k);
            return g[tx - k.x * WorldGen.Chunk + 1, ty - k.y * WorldGen.Chunk + 1];
        }

        public Solid AddSolid(Footprint kind, Vector2 center, float w, float h)
        {
            var s = new Solid(kind, center, new Vector2(w / 2, h / 2));
            for (int y = Mathf.FloorToInt(s.min.y); y <= Mathf.FloorToInt(s.max.y); y++)
                for (int x = Mathf.FloorToInt(s.min.x); x <= Mathf.FloorToInt(s.max.x); x++)
                {
                    var t = new Vector2Int(x, y);
                    if (!solidCells.TryGetValue(t, out var list)) solidCells[t] = list = new List<Solid>(2);
                    list.Add(s);
                }
            ForPathTiles(s, t => blocked[t] = blocked.TryGetValue(t, out var n) ? n + 1 : 1);
            return s;
        }

        public void RemoveSolid(Solid s)
        {
            for (int y = Mathf.FloorToInt(s.min.y); y <= Mathf.FloorToInt(s.max.y); y++)
                for (int x = Mathf.FloorToInt(s.min.x); x <= Mathf.FloorToInt(s.max.x); x++)
                {
                    var t = new Vector2Int(x, y);
                    if (solidCells.TryGetValue(t, out var list) && list.Remove(s) && list.Count == 0) solidCells.Remove(t);
                }
            ForPathTiles(s, t => { if (blocked.TryGetValue(t, out var n)) { if (n <= 1) blocked.Remove(t); else blocked[t] = n - 1; } });
        }

        /// <summary>Tiles treated as blocked by pathfinding: tile center inside the (slightly grown) shape.</summary>
        static void ForPathTiles(Solid s, System.Action<Vector2Int> f)
        {
            for (int y = Mathf.FloorToInt(s.min.y - PathPad); y <= Mathf.FloorToInt(s.max.y + PathPad); y++)
                for (int x = Mathf.FloorToInt(s.min.x - PathPad); x <= Mathf.FloorToInt(s.max.x + PathPad); x++)
                    if (s.Contains(new Vector2(x + 0.5f, y + 0.5f), PathPad)) f(new Vector2Int(x, y));
        }

        bool HitsSolid(Vector2 p)
        {
            if (!solidCells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)), out var list)) return false;
            foreach (var s in list) if (s.Contains(p)) return true;
            return false;
        }

        bool WaterAt(int tx, int ty)
        {
            if (!chunks.ContainsKey(new Vector2Int(MathX.FloorDiv(tx, WorldGen.Chunk), MathX.FloorDiv(ty, WorldGen.Chunk)))) return false;
            return WorldGen.IsWater(GroundAtTile(tx, ty));
        }

        /// <summary>Tile-level walkability (pathfinding grid).</summary>
        public bool Walkable(int tx, int ty, bool swim = false)
        {
            if (blocked.ContainsKey(new Vector2Int(tx, ty))) return false;
            return swim || !WaterAt(tx, ty);
        }

        /// <summary>Point-level walkability (movement): exact obstacle shapes + water tiles.</summary>
        public bool Walkable(Vector2 p, bool swim = false)
        {
            if (HitsSolid(p)) return false;
            return swim || !WaterAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        }

        /// <summary>나무·바위·벽처럼 투사체도 막는 장애물인지 (물은 제외).</summary>
        public bool IsSolidObstacle(Vector2 p) => HitsSolid(p);

        /// <summary>
        /// Collision-aware movement. Slides along walls (resolving X and Y separately).
        /// swim: can cross water (raft). bound: max distance from the origin (0 = none); moving back inward is always allowed.
        /// </summary>
        public Vector2 Move(Vector2 from, Vector2 delta, float radius, bool swim = false, float bound = 0)
        {
            Vector2 p = from;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.2f));
            Vector2 d = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var nx = new Vector2(p.x + d.x, p.y);
                if (Free(nx, radius, swim) && InBound(p, nx, bound)) p = nx;
                var ny = new Vector2(p.x, p.y + d.y);
                if (Free(ny, radius, swim) && InBound(p, ny, bound)) p = ny;
            }
            return p;
        }

        static bool InBound(Vector2 from, Vector2 to, float bound) =>
            bound <= 0 || to.sqrMagnitude <= bound * bound || to.sqrMagnitude <= from.sqrMagnitude;

        public bool Free(Vector2 p, float r, bool swim = false)
        {
            r *= 0.7f;
            return Walkable(p, swim)
                && Walkable(new Vector2(p.x - r, p.y - r * 0.5f), swim) && Walkable(new Vector2(p.x + r, p.y - r * 0.5f), swim)
                && Walkable(new Vector2(p.x - r, p.y + r * 0.5f), swim) && Walkable(new Vector2(p.x + r, p.y + r * 0.5f), swim);
        }

        /// <summary>Line of sight (for ranged enemies, blinks).</summary>
        public bool LineClear(Vector2 a, Vector2 b, bool swim = false)
        {
            float d = Vector2.Distance(a, b);
            int n = Mathf.CeilToInt(d / 0.4f);
            for (int i = 1; i < n; i++)
                if (!Walkable(Vector2.Lerp(a, b, i / (float)n), swim)) return false;
            return true;
        }

        /// <summary>Can a body walk straight from a to b? (both sides of the body checked, for path smoothing)</summary>
        public bool PathClear(Vector2 a, Vector2 b, bool swim = false, float r = 0.3f)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            var side = new Vector2(-d.y, d.x) / len * r;
            int n = Mathf.CeilToInt(len / 0.25f);
            for (int i = 1; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                if (!Walkable(p, swim) || !Walkable(p + side, swim) || !Walkable(p - side, swim)) return false;
            }
            return true;
        }

        /// <summary>Furthest reachable point in a straight line (used for blinks/dashes).</summary>
        public Vector2 Raycast(Vector2 from, Vector2 dir, float dist, float radius, bool swim = false)
        {
            Vector2 last = from;
            int n = Mathf.CeilToInt(dist / 0.2f);
            for (int i = 1; i <= n; i++)
            {
                var p = from + dir * (dist * i / n);
                if (!Free(p, radius, swim)) break;
                last = p;
            }
            return last;
        }

        public Vector2 NearestWalkable(Vector2 p, float radius, float maxSearch = 6, bool swim = false)
        {
            if (Free(p, radius, swim)) return p;
            for (float r = 0.5f; r <= maxSearch; r += 0.5f)
                for (int a = 0; a < 16; a++)
                {
                    var q = p + MathX.Dir(a * 22.5f) * r;
                    if (Free(q, radius, swim)) return q;
                }
            return p;
        }
    }

    /// <summary>
    /// A* pathfinding on the tile grid. 8-way, with corner-cutting prevention.
    /// Search scope is limited (open-world clicks can be far away).
    /// </summary>
    public static class PathFinder
    {
        struct Node { public Vector2Int p; public float f; }
        static readonly List<Node> open = new List<Node>();
        static readonly Dictionary<Vector2Int, Vector2Int> came = new Dictionary<Vector2Int, Vector2Int>();
        static readonly Dictionary<Vector2Int, float> g = new Dictionary<Vector2Int, float>();
        static readonly Vector2Int[] dirs =
        {
            new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1),
            new Vector2Int(1,1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(-1,-1),
        };

        /// <param name="swim">water is walkable (raft)</param>
        /// <param name="bound">max distance from the origin (0 = unlimited)</param>
        public static List<Vector2> Find(WorldStreamer w, Vector2 from, Vector2 to, int maxNodes = 4000, bool swim = false, float bound = 0)
        {
            var result = new List<Vector2>();
            if (bound > 0 && to.magnitude > bound - 0.5f && to.magnitude > from.magnitude) to = to.normalized * Mathf.Max(from.magnitude, bound - 0.5f);
            var s = new Vector2Int(Mathf.FloorToInt(from.x), Mathf.FloorToInt(from.y));
            var t = new Vector2Int(Mathf.FloorToInt(to.x), Mathf.FloorToInt(to.y));
            float boundSq = bound > 0 ? Mathf.Max(bound * bound, from.sqrMagnitude + 1) : float.MaxValue;
            bool Ok(int x, int y) => w.Walkable(x, y, swim) && (x + 0.5f) * (x + 0.5f) + (y + 0.5f) * (y + 0.5f) <= boundSq;

            // If the destination is blocked, go to the nearest open tile
            if (!w.Walkable(t.x, t.y, swim))
            {
                var alt = w.NearestWalkable(to, 0.3f, 4, swim);
                t = new Vector2Int(Mathf.FloorToInt(alt.x), Mathf.FloorToInt(alt.y));
                to = alt;
            }
            if (w.PathClear(from, to, swim)) { result.Add(to); return result; }

            open.Clear(); came.Clear(); g.Clear();
            open.Add(new Node { p = s, f = H(s, t) });
            g[s] = 0;
            Vector2Int best = s; float bestH = H(s, t);
            int expanded = 0;
            while (open.Count > 0 && expanded < maxNodes)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++) if (open[i].f < open[bi].f) bi = i;
                var cur = open[bi].p;
                open.RemoveAt(bi);
                expanded++;
                if (cur == t) { best = t; break; }
                float h = H(cur, t);
                if (h < bestH) { bestH = h; best = cur; }
                foreach (var d in dirs)
                {
                    var n = cur + d;
                    if (!Ok(n.x, n.y)) continue;
                    if (d.x != 0 && d.y != 0 && (!Ok(cur.x + d.x, cur.y) || !Ok(cur.x, cur.y + d.y))) continue;
                    float ng = g[cur] + (d.x != 0 && d.y != 0 ? 1.4142f : 1f);
                    if (g.TryGetValue(n, out var old) && ng >= old) continue;
                    g[n] = ng;
                    came[n] = cur;
                    open.Add(new Node { p = n, f = ng + H(n, t) });
                }
            }

            // Reconstruct path (if unreachable, go to the closest reachable point)
            var path = new List<Vector2Int>();
            var c = best;
            while (c != s && came.ContainsKey(c)) { path.Add(c); c = came[c]; }
            path.Reverse();
            foreach (var p in path) result.Add(new Vector2(p.x + 0.5f, p.y + 0.5f));
            if (best == t && result.Count > 0) result[result.Count - 1] = to;
            Smooth(w, from, result, swim);
            return result;
        }

        static float H(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            return (dx + dy) + (1.4142f - 2) * Mathf.Min(dx, dy);
        }

        /// <summary>Remove unnecessary bends (string pulling).</summary>
        static void Smooth(WorldStreamer w, Vector2 from, List<Vector2> pts, bool swim)
        {
            if (pts.Count < 3) return;
            var outList = new List<Vector2>();
            Vector2 anchor = from;
            int i = 0;
            while (i < pts.Count)
            {
                int far = i;
                for (int j = pts.Count - 1; j > i; j--)
                    if (w.PathClear(anchor, pts[j], swim)) { far = j; break; }
                outList.Add(pts[far]);
                anchor = pts[far];
                i = far + 1;
            }
            pts.Clear();
            pts.AddRange(outList);
        }
    }
}
