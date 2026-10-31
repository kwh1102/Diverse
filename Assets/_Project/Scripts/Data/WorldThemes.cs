using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>The 8 build types a zone can favour (the combat side of a zone; its look is a ThemeDef).</summary>
    public enum CombatType { MeleeRapid, MeleeBurst, RangedRapid, RangedBurst, Speed, Tank, Evasion, Area }

    /// <summary>One kind of scenery a theme scatters (a tree, rock, mushroom…). sprite may contain {v} for variants.</summary>
    [Serializable]
    public class DecorEntry
    {
        public string sprite = "rock_Meadow_{v}";
        [Min(1)] public int variants = 1;
        [Tooltip("Chance per open tile (before the theme's density)")]
        [Range(0, 0.3f)] public float chance = 0.01f;
        [Tooltip("Blocks movement (footprint below)")]
        public bool block;
        public float width = 0.8f, depth = 0.5f;
        [Tooltip("Lies on the ground (drawn under everything, no shadow)")]
        public bool flat;
        [Tooltip("Gathers in dense patches (forests) instead of spreading evenly")]
        public bool clustered;
        [Tooltip("Also grows on top of cliffs")]
        public bool onCliffs;

        public string Key(uint hash) => variants > 1 ? sprite.Replace("{v}", (hash % (uint)variants).ToString()) : sprite.Replace("{v}", "0");
    }

    /// <summary>
    /// The look of a zone: ground, scenery, cliffs, water, its landmark and the monsters living there.
    /// Separate from CombatTypeDef so a build type can later get other themes (and a theme can serve other types).
    /// </summary>
    [Serializable]
    public class ThemeDef
    {
        public string id = "meadow";
        public string displayName = "바람결 초원";
        [Tooltip("Short name for border regions (\"화산·빙하 경계\")")]
        public string prefix = "초원";
        [Tooltip("Art set for trees / rocks / bushes / tufts (tree_<biome>_0 …)")]
        public Biome biome = Biome.Meadow;
        public Color mapColor = new Color(0.45f, 0.7f, 0.3f);

        [Header("Ground")]
        public Ground ground = Ground.Grass;
        public Ground groundAlt = Ground.DarkGrass;
        [Range(0, 1)] public float altShare = 0.3f;
        [Tooltip("What fills rivers and lakes here (Water, Lava, Toxic, Ice…)")]
        public Ground liquid = Ground.Water;
        public Ground road = Ground.Path;

        [Header("Terrain (added to the world rules)")]
        [Tooltip("More (+) or fewer (-) mountain ranges")]
        [Range(-0.5f, 0.5f)] public float mountainBias;
        [Range(-0.5f, 0.5f)] public float waterBias;
        [Tooltip("Flat-topped rock islands (mesas) between the ranges")]
        [Range(0, 1)] public float mesas;

        [Header("Scenery")]
        [Range(0, 3)] public float density = 1;
        public List<DecorEntry> decor = new List<DecorEntry>();

        [Header("Landmark (the zone's heart; its pure boss waits in front of it)")]
        public string landmarkName = "";
        public string landmarkSprite = "";
        [Tooltip("Blocking footprint of the landmark (width, depth) in tiles")]
        public Vector2 landmarkFootprint = new Vector2(6, 3);
        [Tooltip("Open ground kept clear around the landmark (no mountains, water or scenery)")]
        public float arenaRadius = 16;
        public Ground arenaGround = Ground.Plaza;

        [Header("Monsters")]
        public List<string> enemies = new List<string> { "shroom" };
        public string boss = "boss_shroom";

        [NonSerialized] public int index;      // position in DB.Themes (the ground shader's cliff texture layer)
    }

    /// <summary>A build type a zone favours. Its mechanics come later; for now it picks the zone's default theme.</summary>
    [Serializable]
    public class CombatTypeDef
    {
        public CombatType type;
        public string displayName = "근거리 공속";
        public ThemeData theme;
        [TextArea] public string summary = "";
    }

    /// <summary>World layout, terrain and spawning values (Data/WorldRules.asset).</summary>
    [Serializable]
    public class WorldRulesDef
    {
        [Header("Zones (5 slices around the spawn)")]
        [Tooltip("The spawn's own neutral land")]
        public float centerRadius = 46;
        [Tooltip("Width of the mixed band on each side of a zone border (tiles)")]
        public float blendWidth = 40;
        [Tooltip("How far the borders wander from straight spokes")]
        public float borderWiggle = 30;
        [Tooltip("Distance range of each zone's landmark (pure boss)")]
        public Vector2 landmarkDistance = new Vector2(250, 360);
        [Tooltip("Distance range of the border sites (mixed elite bosses)")]
        public Vector2 borderSiteDistance = new Vector2(140, 210);

        [Header("Mountain ranges")]
        [Tooltip("Spacing of ranges: lower = longer, farther apart")]
        public float rangeFrequency = 0.0055f;
        [Tooltip("How much the range lines bend (tiles)")]
        public float rangeWarp = 32;
        [Tooltip("Large-scale mask deciding where ranges exist at all")]
        public float rangeMaskFrequency = 0.003f;
        [Range(0, 1)] public float rangeMaskThreshold = 0.46f;
        [Tooltip("Crest strength needed for rock (higher = thinner ranges)")]
        [Range(0.5f, 0.99f)] public float cliffLevel = 0.88f;
        [Tooltip("Crest strength for the upper tier")]
        [Range(0.5f, 1f)] public float peakLevel = 0.95f;
        [Tooltip("Saddles: low points where a range can be crossed (spacing in tiles, radius in tiles)")]
        public float passSpacing = 80;
        public float passRadius = 7;
        [Tooltip("Mountains fade out this far around the spawn, roads and landmarks (tiles)")]
        public float clearance = 12;

        [Header("Water")]
        public float riverFrequency = 0.006f;
        [Range(0.002f, 0.05f)] public float riverWidth = 0.012f;
        public float lakeFrequency = 0.018f;
        [Range(0.5f, 0.95f)] public float lakeLevel = 0.73f;

        [Header("Roads (spawn → landmarks and border sites)")]
        public float roadWidth = 1.6f;
        public float roadWander = 12;

        [Header("Monster spawning (around the player, by theme)")]
        public int maxAlive = 12;
        [Tooltip("Extra live monsters per frontier ring")]
        public int maxAlivePerRing = 2;
        [Tooltip("Spawn ring around the player (outside the screen)")]
        public Vector2 spawnRadius = new Vector2(15, 24);
        [Tooltip("Monsters farther than this from the player vanish")]
        public float despawnRadius = 38;
        [Tooltip("Seconds between spawn attempts")]
        public float spawnInterval = 0.9f;
        public Vector2Int packSize = new Vector2Int(1, 3);
        [Range(0, 0.5f)] public float eliteChancePerRing = 0.07f;
        [Tooltip("No monsters spawn this close to the town")]
        public float safeRadius = 26;
    }
}
