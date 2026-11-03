using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>Large, named exploration places. Candidate choice never reads GroundAt or terrain height, avoiding
    /// generation recursion and making place reservations independent of chunk loading order.</summary>
    public sealed class ExplorationLayout
    {
        readonly WorldGen world;
        readonly Dictionary<Vector2Int, StructureSpec> cells = new Dictionary<Vector2Int, StructureSpec>();
        readonly Dictionary<string, List<ExplorationSiteDef>> pools = new Dictionary<string, List<ExplorationSiteDef>>();
        readonly Dictionary<string, ExplorationSiteDef> definitions = new Dictionary<string, ExplorationSiteDef>();
        readonly Dictionary<string, Vector2[]> trails = new Dictionary<string, Vector2[]>();
        WorldRulesDef Rules => DB.WorldRules;
        int Cell => Mathf.Max(56, Rules.explorationCell);
        float TrailReach => Mathf.Max(Rules.explorationTrailConnectDistance, Rules.explorationTrailNeighbourDistance);

        public ExplorationLayout(WorldGen world)
        {
            this.world = world;
            foreach (var d in Rules.exploration)
            {
                definitions[d.id] = d;
                if (!pools.TryGetValue(d.theme, out var pool)) pools[d.theme] = pool = new List<ExplorationSiteDef>();
                pool.Add(d);
            }
        }

        public ExplorationSiteDef Definition(StructureSpec s) => definitions.TryGetValue(s.exploration, out var d) ? d : null;

        public StructureSpec At(int cx, int cy)
        {
            var key = new Vector2Int(cx, cy);
            if (cells.TryGetValue(key, out var found)) return found;
            if (cells.Count > 2048) cells.Clear();
            return cells[key] = Propose(cx, cy);
        }

        StructureSpec Propose(int cx, int cy)
        {
            uint hash = Hash.U(cx, cy, world.seed + 9811);
            float jitter = Mathf.Min(Rules.explorationJitter, Cell * .08f);
            var center = new Vector2((cx + .5f) * Cell, (cy + .5f) * Cell) +
                new Vector2((Hash.F(cx, cy, world.seed + 9812) * 2 - 1) * jitter,
                    (Hash.F(cx, cy, world.seed + 9813) * 2 - 1) * jitter);
            center = new Vector2(Mathf.Round(center.x), Mathf.Round(center.y));
            var theme = world.ThemeAt(center.x, center.y);
            if (!pools.TryGetValue(theme.id, out var pool) || pool.Count == 0) return null;
            var def = pool[(int)(hash % (uint)pool.Count)];
            float radius = def.halfSize.magnitude + 1;
            if (center.magnitude < WorldGen.TownRadius + radius + Rules.explorationClearance) return null;
            if (world.Terrain.RoadDistance(center) < radius + Rules.roadWidth + 2) return null;
            if (world.Zones != null) foreach (var fixedSite in world.Zones.sites)
                if (Vector2.Distance(center, fixedSite.center) < radius + fixedSite.radius + Rules.explorationClearance) return null;
            // Preserve rivers: never replace their path with a settlement platform. Water does not depend on sites.
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                if (world.Terrain.Water(center + new Vector2(x * def.halfSize.x, y * def.halfSize.y)) != 0) return null;
            return new StructureSpec
            {
                id = $"explore:{cx}:{cy}", kind = StructKind.Exploration, exploration = def.id,
                center = center, radius = radius, theme = theme, biome = theme.biome,
                tier = Mathf.RoundToInt(WorldGen.TierAt(center.magnitude)), name = def.displayName, landmark = true,
            };
        }

        public IEnumerable<StructureSpec> Near(Vector2 point, float extra = 0)
        {
            int cx = Mathf.FloorToInt(point.x / Cell), cy = Mathf.FloorToInt(point.y / Cell);
            int reach = Mathf.Max(1, Mathf.CeilToInt((extra + Cell * .5f) / Cell));
            for (int y = cy - reach; y <= cy + reach; y++) for (int x = cx - reach; x <= cx + reach; x++)
            {
                var s = At(x, y);
                if (s != null && (s.center - point).sqrMagnitude <= (s.radius + extra) * (s.radius + extra)) yield return s;
            }
        }

        public float Clearance(Vector2 p)
        {
            float distance = float.MaxValue;
            foreach (var s in Near(p, Rules.explorationClearance + 4))
                distance = Mathf.Min(distance, Vector2.Distance(p, s.center) - s.radius);
            return distance;
        }

        public float TerrainOpenness(Vector2 p)
        {
            float open = 1, margin = Mathf.Max(1, Rules.explorationClearance);
            foreach (var s in Near(p, margin + TrailReach))
            {
                var d = Definition(s); var q = p - s.center;
                float distance = Mathf.Max(Mathf.Abs(q.x) - d.halfSize.x, Mathf.Abs(q.y) - d.halfSize.y);
                open = Mathf.Min(open, Mathf.Clamp01(distance / margin));
                float trail = TrailDistance(p, s, out _) - Rules.explorationTrailWidth - 1;
                open = Mathf.Min(open, Mathf.Clamp01(trail / margin));
            }
            return open;
        }

        public bool TrailAt(Vector2 p, out bool horizontal)
        {
            foreach (var s in Near(p, TrailReach + 2))
            {
                if (TrailDistance(p, s, out horizontal) < Rules.explorationTrailWidth) return true;
            }
            horizontal = false; return false;
        }

        public Vector2[] Trail(StructureSpec site)
        {
            if (trails.TryGetValue(site.id, out var path)) return path;
            if (trails.Count > 2048) trails.Clear();
            var def = Definition(site);
            // Authored places enter from the south. Bend outside the footprint before approaching a road to the north.
            var entrance = site.center + Vector2.down * (def.halfSize.y - .5f);
            var lead = site.center + Vector2.down * (def.halfSize.y + Rules.explorationTrailBend);
            var target = world.Terrain.NearestRoadPoint(lead);
            float roadDistance = Vector2.Distance(lead, target);
            bool connected = roadDistance <= Rules.explorationTrailConnectDistance;
            if (!connected)
            {
                float best = float.MaxValue;
                // Join a nearby place closer to the main network. Strictly decreasing road distance prevents cycles,
                // and only reads site proposals (never their trails), preserving load-order independence.
                foreach (var neighbour in Near(site.center, Rules.explorationTrailNeighbourDistance))
                {
                    if (neighbour.id == site.id) continue;
                    var otherDef = Definition(neighbour);
                    var otherLead = neighbour.center + Vector2.down * (otherDef.halfSize.y + Rules.explorationTrailBend);
                    float otherRoadDistance = Vector2.Distance(otherLead, world.Terrain.NearestRoadPoint(otherLead));
                    var otherEntrance = neighbour.center + Vector2.down * (otherDef.halfSize.y - .5f);
                    float distance = Vector2.Distance(lead, otherEntrance);
                    if (otherRoadDistance >= roadDistance - 1 || distance > Rules.explorationTrailNeighbourDistance) continue;
                    float cost = distance + otherRoadDistance * .15f;
                    if (cost < best) { best = cost; target = otherEntrance; connected = true; }
                }
            }
            var points = new List<Vector2> { entrance, lead };
            if (target.y > lead.y)
            {
                float side = target.x < site.center.x ? -1 : 1;
                var corner = new Vector2(site.center.x + side * (def.halfSize.x + Rules.explorationTrailBend), lead.y);
                points.Add(corner);
                points.Add(new Vector2(corner.x, target.y));
            }
            points.Add(target);
            if (!connected)
            {
                // Far-off places only leave a short directional approach, not a road spanning the whole map.
                float remaining = Rules.explorationTrailLength;
                var shortPath = new List<Vector2> { entrance };
                for (int i = 1; i < points.Count && remaining > 0; i++)
                {
                    float length = Vector2.Distance(points[i - 1], points[i]);
                    shortPath.Add(Vector2.Lerp(points[i - 1], points[i], Mathf.Min(1, remaining / Mathf.Max(.001f, length))));
                    remaining -= length;
                }
                points = shortPath;
            }
            return trails[site.id] = points.ToArray();
        }

        float TrailDistance(Vector2 p, StructureSpec site, out bool horizontal)
        {
            float best = float.MaxValue; horizontal = false;
            var points = Trail(site);
            for (int i = 1; i < points.Length; i++)
            {
                var a = points[i - 1]; var ab = points[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                float distance = Vector2.Distance(p, a + ab * t);
                if (distance < best) { best = distance; horizontal = Mathf.Abs(ab.x) > Mathf.Abs(ab.y); }
            }
            return best;
        }

        public bool TryGround(int tx, int ty, out Ground ground)
        {
            var p = new Vector2(tx + .5f, ty + .5f);
            foreach (var s in Near(p))
            {
                var d = Definition(s); var q = p - s.center;
                if (Mathf.Abs(q.x) > d.halfSize.x || Mathf.Abs(q.y) > d.halfSize.y) continue;
                // Bevel outer corners on the same macro grid as natural land; the final pixel fringe finishes them.
                if (Mathf.Abs(q.x) + Mathf.Abs(q.y) > d.halfSize.x + d.halfSize.y - d.cornerCut) continue;
                ground = Sample(d, s.theme, q); return true;
            }
            ground = default; return false;
        }

        public static Ground Sample(ExplorationSiteDef d, ThemeDef theme, Vector2 p)
        {
            for (int i = d.grounds.Count - 1; i >= 0; i--)
                if (d.grounds[i].Contains(p))
                    return d.kind == ExplorationKind.Hamlet && d.grounds[i].material == ExplorationMaterial.Road
                        ? DB.WorldRules.settlementRoad : d.grounds[i].Resolve(theme);
            return d.kind == ExplorationKind.Hamlet ? DB.WorldRules.settlementGround : theme.ground;
        }
    }
}
