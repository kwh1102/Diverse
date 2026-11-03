using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    public sealed partial class SceneryLayout
    {
        public sealed class Group
        {
            public Vector2 center;
            public ThemeDef theme;
            public int level;
            public List<Placement> members = new List<Placement>();
        }

        readonly Dictionary<Vector2Int, Group> groups = new Dictionary<Vector2Int, Group>();
        int GroupCell => Mathf.Max(24, Rules.sceneryGroupCell);

        public Group GroupAt(int x, int y)
        {
            var key = new Vector2Int(x, y);
            if (groups.TryGetValue(key, out var cached)) return cached;
            if (groups.Count > 2048) groups.Clear();
            foreach (var candidate in ProposeGroupSteps(x,y)) if (candidate != null) cached = candidate;
            return groups[key] = cached;
        }

        IEnumerable<int> ResolveGroupSteps(int x,int y)
        {
            var key = new Vector2Int(x,y);
            if (groups.ContainsKey(key)) yield break;
            if (groups.Count > 2048) groups.Clear();
            Group result = null;
            foreach (var candidate in ProposeGroupSteps(x,y))
            {
                if (candidate != null) result = candidate;
                yield return 0;
            }
            groups[key] = result;
        }

        IEnumerable<Group> ProposeGroupSteps(int x, int y)
        {
            int seed = world.Gen.seed, cell = GroupCell;
            var center = new Vector2((x + .25f + Hash.F(x, y, seed + 6101) * .5f) * cell,
                (y + .25f + Hash.F(x, y, seed + 6103) * .5f) * cell);
            if (center.magnitude < WorldGen.TownRadius + 12) yield break;
            var theme = world.Gen.ThemeAt(center.x, center.y); var s = theme.scenery;
            if (!s.enabled || s.group.Count == 0 || Hash.F(x, y, seed + 6105) >= s.groupChance) yield break;
            if (world.Gen.Terrain.RoadClearance(center) < s.groupRadius + Rules.sceneryRoadMargin) yield break;
            foreach (var structure in world.Gen.StructuresNear(center, s.groupRadius + 2))
                if (Vector2.Distance(center, structure.center) < structure.radius + s.groupRadius + 2) yield break;
            int level = WorldGen.Level(world.GroundAtTile(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y)));
            // Workplaces sit on the traversable lowland; natural cliff-top clusters use the normal scenery pass.
            if (level != 0) yield break;
            float mirror = Hash.F(x, y, seed + 6107) < .5f ? -1 : 1;
            var group = new Group { center = center, theme = theme, level = level };
            foreach (var prop in s.group)
            {
                var at = center + new Vector2(prop.position.x * mirror, prop.position.y);
                at = new Vector2(Mathf.Round(at.x * Art.PPU) / Art.PPU, Mathf.Round(at.y * Art.PPU) / Art.PPU);
                var sprite = Art.Decor(prop.sprite);
                if (sprite == null) yield break;
                float scale = theme.SceneryScale(prop.sprite, focal: prop.position.sqrMagnitude <= 4);
                for (int sy = -1; sy <= 1; sy++) for (int sx = -1; sx <= 1; sx++)
                {
                    var q = at + new Vector2(sx * (prop.footprint.x * .5f + .4f), sy * (prop.footprint.y * .5f + .4f));
                    var ground = world.GroundAtTile(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y));
                    yield return null;
                    if (WorldGen.Level(ground) != 0 || WorldGen.IsLiquid(ground) || !world.OpenTile(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y)) ||
                        world.Gen.Terrain.RoadClearance(q) < Rules.sceneryRoadMargin || ground is Ground.Path or Ground.Plaza or Ground.Wood) yield break;
                }
                group.members.Add(new Placement { sprite = sprite, position = at, scale = scale,
                    entry = new DecorEntry { block = prop.blocks, width = prop.footprint.x, depth = prop.footprint.y },
                    visual = new Rect(at.x - sprite.rect.width / Art.PPU * scale * .5f, at.y,
                        sprite.rect.width / Art.PPU * scale, sprite.rect.height / Art.PPU * scale) });
            }
            yield return group;
        }

        public bool ReservedByGroup(Vector2 p)
        {
            int cx = Mathf.FloorToInt(p.x / GroupCell), cy = Mathf.FloorToInt(p.y / GroupCell);
            for (int y = cy - 1; y <= cy + 1; y++) for (int x = cx - 1; x <= cx + 1; x++)
            {
                var group = GroupAt(x, y);
                if (group != null && Vector2.Distance(p, group.center) < group.theme.scenery.groupRadius + 2) return true;
            }
            return false;
        }

        IEnumerable<int> BuildGroupSteps(ChunkView view)
        {
            int n = WorldGen.Chunk, cell = GroupCell;
            int x0 = MathX.FloorDiv(view.key.x * n, cell), y0 = MathX.FloorDiv(view.key.y * n, cell);
            int x1 = MathX.FloorDiv((view.key.x + 1) * n, cell), y1 = MathX.FloorDiv((view.key.y + 1) * n, cell);
            for (int y = y0 - 1; y <= y1 + 1; y++) for (int x = x0 - 1; x <= x1 + 1; x++)
            {
                foreach (int step in ResolveGroupSteps(x,y)) yield return step;
                var group = GroupAt(x, y);
                yield return 0;
                if (group == null) continue;
                foreach (var p in group.members)
                    if (WorldStreamer.ChunkOf(p.position) == view.key)
                    {
                        view.AddDecor(p.sprite, p.position, p.entry.block, p.entry.width, p.entry.depth,
                            name: group.theme.scenery.groupName, scale: p.scale);
                        yield return 0;
                    }
                yield return 0;
            }
        }

        /// <summary>Negative samples are progress checkpoints; the last nonnegative value is the final cover.</summary>
        public IEnumerable<float> GroundCoverSteps(Vector2 p, ThemeDef theme)
        {
            if (!theme.scenery.enabled || p.magnitude < WorldGen.TownRadius + 6) { yield return 0; yield break; }
            float cover = SceneryHabitat.Sample(p, theme, world.Gen.seed) * theme.scenery.groundStrength;
            int cx = Mathf.FloorToInt(p.x / GroupCell), cy = Mathf.FloorToInt(p.y / GroupCell);
            for (int y = cy - 1; y <= cy + 1; y++) for (int x = cx - 1; x <= cx + 1; x++)
            {
                foreach (int step in ResolveGroupSteps(x,y)) yield return -1;
                var group = GroupAt(x, y);
                if (group != null) cover = Mathf.Max(cover, Mathf.Clamp01(1 - Vector2.Distance(p, group.center) / group.theme.scenery.groupRadius));
            }
            yield return cover;
        }
    }
}
