using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>How a ground's 16×16 tiles are patterned (drawn by TileBaker from the ground's palette ramp).</summary>
    public enum TilePattern { Plain, Tufts, Pebbles, Ripples, Speckles, Cobbles, Slabs, Planks, Cracks, Puddles, Crust, Strata, Sparkles, Tiles, Moss, Snowdrifts, CrystalBed, MirrorPaving, Sandstone }

    /// <summary>How a cliff wall is built (16 dots per level).</summary>
    public enum WallPattern { Blocks, Bricks, Strata, Columns, Facets, Roots }

    /// <summary>A ground's look: pattern + palette ramp (palette indices, dark → light; ramp[1] is the base colour).</summary>
    [Serializable]
    public class GroundLook
    {
        public Ground ground;
        public TilePattern pattern;
        public int[] ramp = { 0, 1, 2 };
        [Tooltip("Palette index of small accents (flower hearts, embers, glints); -1 = none")]
        public int accent = -1;
        [Tooltip("Palette index of flower petals around the accent; -1 = the accent")]
        public int petal = -1;
        [Tooltip("Palette index of this ground in shade (grass blades, the foot of a cliff); -1 = ramp[0]")]
        public int shade = -1;
        [Tooltip("Liquids: palette index of the line where they meet land (foam, a glowing crust); -1 = none")]
        public int edge = -1;
        [Tooltip("Liquids: the bank on the land just above (north of) them, from the liquid line upward; -1 = the land's shade")]
        public int[] bank = new int[0];
    }

    /// <summary>A theme's cliff wall: pattern + palette ramp (dark → light), optional accent (earth patches, crystals).</summary>
    [Serializable]
    public class CliffLook
    {
        public string theme = "meadow";
        public WallPattern wall;
        public int[] ramp = { 1, 2, 3, 4 };
        public int accent = -1;
        [Tooltip("Palette index of the rim along the cliff top's edges; -1 = a grassy fringe from the ground's lightest tone")]
        public int lip = -1;
    }

    /// <summary>
    /// The art rules of the whole game (Data/ArtStyle.asset): one fixed palette every picture is snapped to, 16 dots per
    /// tile, a 1-dot outline, light from the top-left, a straight top-down 3/4 view. Ground tiles, cliffs, characters,
    /// monsters, objects, effects and the custom-mode designs all follow it.
    /// </summary>
    [Serializable]
    public class ArtStyleDef
    {
        [Tooltip("The only colours any picture may use")]
        public Color32[] palette = new Color32[0];
        public string[] paletteNames = new string[0];
        [Tooltip("Palette index of outlines and the darkest contact lines")]
        public int outline = 0;
        [Tooltip("Round contact shadows under things that stand: palette index and opacity")]
        public int shadow = 1;
        [Range(0, 1)] public float shadowAlpha = 0.45f;

        [Header("Ground tiles")]
        public List<GroundLook> grounds = new List<GroundLook>();
        [Tooltip("How far ground borders wander from the tile grid (0 = smooth rounded borders)")]
        [Range(0, 0.45f)] public float edgeNoise = 0.28f;
        [Tooltip("Width of the darker rim inside a ground where it meets a lower one (0 = none)")]
        [Range(0, 0.3f)] public float edgeRim = 0.07f;

        [Header("Cliff projection")]
        public List<CliffLook> cliffs = new List<CliffLook>();
        [Tooltip("Vertical pixels per cliff level; navigation and raised decorations use the same value")]
        [Range(16, 32)] public int cliffRise = 24;
        [Tooltip("Horizontal pixels of projection per level, revealing the lit side face")]
        [Range(0, 8)] public int cliffSkew = 4;
        [Tooltip("Radius in dots of the rounded outer corners of cliff tops")]
        public int cornerRadius = 8;
        [Tooltip("Rows of shade on the ground at the foot of a cliff")]
        public int footShade = 2;

        [Header("Sizes in dots")]
        public Vector2Int characterHeight = new Vector2Int(16, 20);

        [Header("Picture generation (image model)")]
        [TextArea(3, 8)] public string prompt = "";
    }
}
