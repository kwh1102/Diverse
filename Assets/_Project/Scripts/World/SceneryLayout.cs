using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// World-coordinate proposals with priority-based spacing. A proposal checks its neighbours even when their chunks
    /// are unloaded, so silhouettes, roads and structure clearances are identical in every loading order.
    /// Flat grass detail lives in the ground texture, not thousands of separate GameObjects.
    /// </summary>
    public sealed partial class SceneryLayout
    {
        public sealed class Placement
        {
            public Sprite sprite;
            public DecorEntry entry;
            public Vector2 position;
            public Rect visual;
            public uint priority;
            public int level;
            public float scale = 1;
            public Rect spacing;
            public Rect footprint;
        }

        readonly WorldStreamer world;
        readonly Dictionary<Vector2Int, Placement> proposals = new Dictionary<Vector2Int, Placement>();
        WorldRulesDef Rules => DB.WorldRules;
        int Cell => Mathf.Max(2, Rules.sceneryCell);

        public SceneryLayout(WorldStreamer world) => this.world = world;

        public void Build(ChunkView view)
        {
            BuildGroups(view);
            int cell = Cell, n = WorldGen.Chunk;
            int x0 = MathX.FloorDiv(view.key.x * n - cell, cell), y0 = MathX.FloorDiv(view.key.y * n - cell, cell);
            int x1 = MathX.FloorDiv((view.key.x + 1) * n, cell), y1 = MathX.FloorDiv((view.key.y + 1) * n, cell);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var p = At(x, y);
                if (p == null || WorldStreamer.ChunkOf(p.position) != view.key || !Accepted(x, y, p)) continue;
                var d = p.entry;
                var at = p.position + new Vector2(p.level * WorldGen.FaceSkew, p.level * WorldGen.FaceHeight);
                view.AddDecor(p.sprite, at, p.level == 0 && d.block, d.width, d.depth, false, "scenery", scale: p.scale);
            }
            // Bounded cache; evicting proposals cannot change their deterministic result.
            if (proposals.Count > 6000) proposals.Clear();
        }

        public bool Accepted(int cx, int cy, Placement p)
        {
            // Include enlarged neighbours across negative-coordinate chunk corners, even when their chunk is unloaded.
            int reach = Mathf.CeilToInt(MaxVisualExtent / Cell) + 1;
            for (int dy = -reach; dy <= reach; dy++) for (int dx = -reach; dx <= reach; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var other = At(cx + dx, cy + dy);
                if (other == null || other.level != p.level) continue;
                bool wins = other.priority < p.priority || other.priority == p.priority && (dy < 0 || dy == 0 && dx < 0);
                if (wins && Conflicts(other, p)) return false;
            }
            return true;
        }

        float maxVisualExtent;
        float MaxVisualExtent
        {
            get
            {
                if (maxVisualExtent > 0) return maxVisualExtent;
                foreach (var theme in DB.Themes) foreach (var d in theme.decor)
                {
                    if (d.flat || d.chance <= 0) continue;
                    for (uint v = 0; v < d.variants; v++)
                    {
                        var sprite = Art.Decor(d.Key(v));
                        if (sprite != null) maxVisualExtent = Mathf.Max(maxVisualExtent,
                            Mathf.Max(sprite.rect.width, sprite.rect.height) / Art.PPU *
                            Mathf.Max(d.visualScale, d.mediumScale, d.rareScale, d.spacingScale) * (1 + d.scaleVariation) + Rules.sceneryGap);
                    }
                }
                return Mathf.Max(4, maxVisualExtent);
            }
        }

        public static bool Conflicts(Placement a, Placement b) => a.level == b.level &&
            (a.spacing.Overlaps(b.spacing) || a.footprint.Overlaps(b.footprint));

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
            if (level == 0 && (!world.OpenTile(tx, ty) || world.Gen.Terrain.RoadClearance(at) < Rules.sceneryRoadMargin)) return null;
            foreach (var structure in world.Gen.StructuresNear(at, 8))
                if ((structure.center - at).sqrMagnitude < (structure.radius + 2) * (structure.radius + 2)) return null;

            var theme = level > 0 ? world.Gen.CliffThemeAt(at.x, at.y, true) : world.Gen.ThemeAt(at.x, at.y);
            if (theme.scenery.enabled && ReservedByGroup(at)) return null;
            float grove = SceneryHabitat.Sample(at, theme, seed);
            float roll = Hash.F(cx, cy, seed + 17), acc = 0;
            // Selection is normalized, so early props cannot consume the whole probability budget of trees.
            DecorEntry selected = null;
            if (theme.scenery.enabled)
            {
                float total = 0;
                foreach (var d in theme.decor) total += Weight(d, theme, ground, level, grove);
                if (Hash.F(cx, cy, seed + 19) >= Mathf.Clamp01(total * cell * cell * theme.density)) return null;
                float choice = roll * total;
                foreach (var d in theme.decor)
                {
                    choice -= Weight(d, theme, ground, level, grove);
                    if (choice < 0) { selected = d; break; }
                }
            }
            foreach (var d in theme.decor)
            {
                if (d.flat || level > 0 && !d.onCliffs) continue;
                bool plant = d.sprite.StartsWith("tree_") || d.sprite.StartsWith("bush_");
                if (plant && ground is Ground.Flagstone or Ground.Mirror or Ground.Plaza) continue;
                if (theme.scenery.enabled) { if (d != selected) continue; }
                else
                {
                    float cluster = d.clustered ? Mathf.SmoothStep(.12f, 1.7f, Mathf.InverseLerp(.3f, .7f, grove)) : .65f;
                    acc += d.chance * cell * cell * theme.density * cluster;
                    if (roll >= acc) continue;
                }
                var sprite = Art.Decor(d.Key(h));
                if (sprite == null) return null;
                float variation = Hash.F(cx, cy, seed + 23);
                float scale = theme.scenery.enabled ? d.IndividualScale(variation, Hash.F(cx, cy, seed + 29), grove) : 1;
                // Quantized visual size, fixed contact footprint: a canopy can overhang without widening the trunk.
                scale = Mathf.Round(sprite.rect.height * scale) / sprite.rect.height;
                float imageWidth = sprite.rect.width / Art.PPU * scale, imageHeight = sprite.rect.height / Art.PPU * scale;
                float width = theme.scenery.enabled ? d.width : Mathf.Max(d.width, imageWidth);
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
                var visual = new Rect(at.x - imageWidth * .5f - gap * .5f, at.y - gap * .5f, imageWidth + gap, imageHeight + gap);
                bool canopy = theme.scenery.enabled && d.role is SceneryRole.Canopy or SceneryRole.Understory;
                float reservedScale = theme.scenery.enabled && d.spacingScale > 0 ?
                    d.spacingScale * Mathf.Lerp(1 - d.scaleVariation, 1 + d.scaleVariation, variation) : scale;
                reservedScale = Mathf.Round(sprite.rect.height * reservedScale) / sprite.rect.height;
                float reservedWidth = sprite.rect.width / Art.PPU * reservedScale, reservedHeight = sprite.rect.height / Art.PPU * reservedScale;
                float spaceWidth = canopy ? reservedWidth * theme.scenery.canopySpacing : reservedWidth;
                float spaceHeight = canopy ? reservedHeight * theme.scenery.canopySpacing : reservedHeight;
                float footGap = theme.scenery.enabled ? theme.scenery.footGap : gap;
                return new Placement
                {
                    sprite = sprite, entry = d, position = at, level = level, priority = h,
                    scale = scale, visual = visual,
                    spacing = theme.scenery.enabled ? new Rect(at.x - (spaceWidth + gap) * .5f, at.y - gap * .5f,
                        spaceWidth + gap, spaceHeight + gap) : visual,
                    footprint = new Rect(at.x - (d.width + footGap) * .5f, at.y - footGap * .5f, d.width + footGap, depth + footGap),
                };
            }
            return null;
        }

        static float Weight(DecorEntry d, ThemeDef theme, Ground ground, int level, float patch)
        {
            if (d.flat || d.chance <= 0 || level > 0 && !d.onCliffs || d.role == SceneryRole.Supply) return 0;
            if ((d.sprite.StartsWith("tree_") || d.sprite.StartsWith("bush_")) && ground is Ground.Flagstone or Ground.Mirror or Ground.Plaza) return 0;
            float isolated = theme.scenery.isolatedChance;
            float affinity = d.role switch
            {
                SceneryRole.Canopy => Mathf.Lerp(isolated, 1.8f, patch * patch),
                SceneryRole.Understory => Mathf.Lerp(isolated, 1.3f, patch),
                SceneryRole.Boulder => Mathf.Lerp(isolated, 1.2f, patch * patch),
                SceneryRole.Relic => Mathf.Lerp(isolated, 1, patch * patch), _ => .15f,
            };
            return d.chance * affinity;
        }
    }
}
