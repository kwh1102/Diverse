using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// World-coordinate proposals with priority-based spacing. A proposal checks its neighbours even when their chunks
    /// are unloaded, so silhouettes, roads and structure clearances are identical in every loading order.
    /// Flat grass detail lives in the ground texture, not thousands of separate GameObjects.
    /// </summary>
    public sealed class SceneryLayout
    {
        public sealed class Placement
        {
            public Sprite sprite;
            public DecorEntry entry;
            public Vector2 position;
            public Rect visual;
            public uint priority;
            public int level;
        }

        readonly WorldStreamer world;
        readonly Dictionary<Vector2Int, Placement> proposals = new Dictionary<Vector2Int, Placement>();
        WorldRulesDef Rules => DB.WorldRules;
        int Cell => Mathf.Max(2, Rules.sceneryCell);

        public SceneryLayout(WorldStreamer world) => this.world = world;

        public void Build(ChunkView view)
        {
            int cell = Cell, n = WorldGen.Chunk;
            int x0 = MathX.FloorDiv(view.key.x * n - cell, cell), y0 = MathX.FloorDiv(view.key.y * n - cell, cell);
            int x1 = MathX.FloorDiv((view.key.x + 1) * n, cell), y1 = MathX.FloorDiv((view.key.y + 1) * n, cell);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var p = At(x, y);
                if (p == null || WorldStreamer.ChunkOf(p.position) != view.key || !Accepted(x, y, p)) continue;
                var d = p.entry;
                var at = p.position + new Vector2(p.level * WorldGen.FaceSkew, p.level * WorldGen.FaceHeight);
                view.AddDecor(p.sprite, at, p.level == 0 && d.block, d.width, d.depth, false, "scenery");
            }
            // Bounded cache; evicting proposals cannot change their deterministic result.
            if (proposals.Count > 6000) proposals.Clear();
        }

        public bool Accepted(int cx, int cy, Placement p)
        {
            // Derive the search reach from the largest neighbouring sprite (regional scenery is <= 4 tiles wide/tall).
            // Three cells also covers trees whose canopy crosses a negative-coordinate chunk corner.
            for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var other = At(cx + dx, cy + dy);
                if (other == null || other.level != p.level) continue;
                bool wins = other.priority < p.priority || other.priority == p.priority && (dy < 0 || dy == 0 && dx < 0);
                if (wins && other.visual.Overlaps(p.visual)) return false;
            }
            return true;
        }

        public Placement At(int cx, int cy)
        {
            var key = new Vector2Int(cx, cy);
            if (proposals.TryGetValue(key, out var cached)) return cached;
            return proposals[key] = Propose(cx, cy);
        }

        Placement Propose(int cx, int cy)
        {
            int seed = world.Gen.seed, cell = Cell;
            uint h = Hash.U(cx, cy, seed + 4242);
            var at = new Vector2(cx * cell + .5f + Hash.F(cx, cy, seed + 4243) * (cell - 1),
                cy * cell + .5f + Hash.F(cx, cy, seed + 4245) * (cell - 1));
            at = new Vector2(Mathf.Round(at.x * Art.PPU) / Art.PPU, Mathf.Round(at.y * Art.PPU) / Art.PPU);
            if (at.magnitude < WorldGen.TownRadius + 4) return null;
            int tx = Mathf.FloorToInt(at.x), ty = Mathf.FloorToInt(at.y);
            Ground ground = world.GroundAtTile(tx, ty);
            int level = WorldGen.Level(ground);
            if (WorldGen.IsLiquid(ground) || ground is Ground.Path or Ground.Plaza or Ground.Wood) return null;
            if (level == 0 && (!world.OpenTile(tx, ty) || world.Gen.Terrain.RoadDistance(at) < Rules.roadWidth + Rules.sceneryRoadMargin)) return null;
            foreach (var structure in world.Gen.StructuresNear(at, 8))
                if ((structure.center - at).sqrMagnitude < (structure.radius + 2) * (structure.radius + 2)) return null;

            var theme = level > 0 ? world.Gen.CliffThemeAt(at.x, at.y) : world.Gen.ThemeAt(at.x, at.y);
            float grove = Noise.Perlin(at.x, at.y, seed + 333, .045f);
            float roll = Hash.F(cx, cy, seed + 17), acc = 0;
            foreach (var d in theme.decor)
            {
                if (d.flat || level > 0 && !d.onCliffs) continue;
                bool plant = d.sprite.StartsWith("tree_") || d.sprite.StartsWith("bush_");
                if (plant && ground is Ground.Flagstone or Ground.Mirror or Ground.Plaza) continue;
                float cluster = d.clustered ? Mathf.SmoothStep(.12f, 1.7f, Mathf.InverseLerp(.3f, .7f, grove)) : .65f;
                acc += d.chance * cell * cell * theme.density * cluster;
                if (roll >= acc) continue;
                var sprite = Art.Decor(d.Key(h));
                if (sprite == null) return null;
                float width = Mathf.Max(d.width, sprite.rect.width / Art.PPU);
                float depth = Mathf.Max(.35f, d.depth);
                // The base must fit wholly on one connected surface, with a small lip/shore setback.
                for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                {
                    var sample = at + new Vector2((x - 1) * (width * .5f + .25f), (y - 1) * (depth * .5f + .3f));
                    int sx = Mathf.FloorToInt(sample.x), sy = Mathf.FloorToInt(sample.y);
                    var g = world.GroundAtTile(sx, sy);
                    if (WorldGen.Level(g) != level || WorldGen.IsLiquid(g) || g is Ground.Path or Ground.Wood or Ground.Plaza) return null;
                    if (level == 0 && !world.OpenTile(sx, sy)) return null;
                }
                float gap = Rules.sceneryGap;
                return new Placement
                {
                    sprite = sprite, entry = d, position = at, level = level, priority = h,
                    visual = new Rect(at.x - width * .5f - gap * .5f, at.y - gap * .5f,
                        width + gap, sprite.rect.height / Art.PPU + gap),
                };
            }
            return null;
        }
    }
}
