using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// Chunk streaming. Keeps only the chunks around the player loaded and unloads distant ones.
    /// Collision (obstacles) is managed as a tile grid → also used for pathfinding.
    /// </summary>
    public class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer I { get; private set; }
        public WorldGen Gen { get; private set; }
        public int loadRadius = 2;          // chunks to load in each direction (5x5)
        public int unloadRadius = 3;

        readonly Dictionary<Vector2Int, ChunkView> chunks = new Dictionary<Vector2Int, ChunkView>();
        readonly Dictionary<Vector2Int, Ground[,]> groundCache = new Dictionary<Vector2Int, Ground[,]>();
        // Blocked tiles (trees, rocks, building walls). Added/removed per chunk.
        readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
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

        public void Block(Vector2Int t) => blocked.Add(t);
        public void Unblock(Vector2Int t) => blocked.Remove(t);

        public void BlockRect(Vector2 center, float w, float h, List<Vector2Int> record)
        {
            int x0 = Mathf.FloorToInt(center.x - w / 2), x1 = Mathf.CeilToInt(center.x + w / 2) - 1;
            int y0 = Mathf.FloorToInt(center.y - h / 2), y1 = Mathf.CeilToInt(center.y + h / 2) - 1;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var t = new Vector2Int(x, y);
                    blocked.Add(t);
                    record?.Add(t);
                }
        }

        public bool Walkable(int tx, int ty)
        {
            if (blocked.Contains(new Vector2Int(tx, ty))) return false;
            if (!chunks.ContainsKey(new Vector2Int(MathX.FloorDiv(tx, WorldGen.Chunk), MathX.FloorDiv(ty, WorldGen.Chunk)))) return true;
            return !WorldGen.IsWater(GroundAtTile(tx, ty));
        }

        public bool Walkable(Vector2 p) => Walkable(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

        /// <summary>나무·바위·벽처럼 투사체도 막는 장애물인지 (물은 제외).</summary>
        public bool IsSolidObstacle(Vector2 p) => blocked.Contains(new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)));

        /// <summary>
        /// Collision-aware movement. Slides along walls (resolving X and Y separately).
        /// </summary>
        public Vector2 Move(Vector2 from, Vector2 delta, float radius)
        {
            Vector2 p = from;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.2f));
            Vector2 d = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var nx = new Vector2(p.x + d.x, p.y);
                if (Free(nx, radius)) p = nx;
                var ny = new Vector2(p.x, p.y + d.y);
                if (Free(ny, radius)) p = ny;
            }
            return p;
        }

        public bool Free(Vector2 p, float r)
        {
            r *= 0.7f;
            return Walkable(new Vector2(p.x - r, p.y - r * 0.5f)) && Walkable(new Vector2(p.x + r, p.y - r * 0.5f))
                && Walkable(new Vector2(p.x - r, p.y + r * 0.5f)) && Walkable(new Vector2(p.x + r, p.y + r * 0.5f));
        }

        /// <summary>Line of sight (for ranged enemies, blinks).</summary>
        public bool LineClear(Vector2 a, Vector2 b)
        {
            float d = Vector2.Distance(a, b);
            int n = Mathf.CeilToInt(d / 0.4f);
            for (int i = 1; i < n; i++)
                if (!Walkable(Vector2.Lerp(a, b, i / (float)n))) return false;
            return true;
        }

        /// <summary>Furthest reachable point in a straight line (used for blinks/dashes).</summary>
        public Vector2 Raycast(Vector2 from, Vector2 dir, float dist, float radius)
        {
            Vector2 last = from;
            int n = Mathf.CeilToInt(dist / 0.2f);
            for (int i = 1; i <= n; i++)
            {
                var p = from + dir * (dist * i / n);
                if (!Free(p, radius)) break;
                last = p;
            }
            return last;
        }

        public Vector2 NearestWalkable(Vector2 p, float radius, float maxSearch = 6)
        {
            if (Free(p, radius)) return p;
            for (float r = 0.5f; r <= maxSearch; r += 0.5f)
                for (int a = 0; a < 16; a++)
                {
                    var q = p + MathX.Dir(a * 22.5f) * r;
                    if (Free(q, radius)) return q;
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

        public static List<Vector2> Find(WorldStreamer w, Vector2 from, Vector2 to, int maxNodes = 4000)
        {
            var result = new List<Vector2>();
            var s = new Vector2Int(Mathf.FloorToInt(from.x), Mathf.FloorToInt(from.y));
            var t = new Vector2Int(Mathf.FloorToInt(to.x), Mathf.FloorToInt(to.y));

            // If the destination is blocked, go to the nearest open tile
            if (!w.Walkable(t.x, t.y))
            {
                var alt = w.NearestWalkable(to, 0.3f, 4);
                t = new Vector2Int(Mathf.FloorToInt(alt.x), Mathf.FloorToInt(alt.y));
                to = alt;
            }
            if (w.LineClear(from, to)) { result.Add(to); return result; }

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
                    if (!w.Walkable(n.x, n.y)) continue;
                    if (d.x != 0 && d.y != 0 && (!w.Walkable(cur.x + d.x, cur.y) || !w.Walkable(cur.x, cur.y + d.y))) continue;
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
            Smooth(w, from, result);
            return result;
        }

        static float H(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            return (dx + dy) + (1.4142f - 2) * Mathf.Min(dx, dy);
        }

        /// <summary>Remove unnecessary bends (string pulling).</summary>
        static void Smooth(WorldStreamer w, Vector2 from, List<Vector2> pts)
        {
            if (pts.Count < 3) return;
            var outList = new List<Vector2>();
            Vector2 anchor = from;
            int i = 0;
            while (i < pts.Count)
            {
                int far = i;
                for (int j = pts.Count - 1; j > i; j--)
                    if (w.LineClear(anchor, pts[j])) { far = j; break; }
                outList.Add(pts[far]);
                anchor = pts[far];
                i = far + 1;
            }
            pts.Clear();
            pts.AddRange(outList);
        }
    }
}
