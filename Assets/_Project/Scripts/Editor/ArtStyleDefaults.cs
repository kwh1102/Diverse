using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Diverse.EditorTools
{
    /// <summary>
    /// Creates Data/ArtStyle.asset (the game's art rules) when it is missing and registers it in GameDatabase.
    /// Edits made in the Inspector are kept: an existing asset is only overwritten by "Reset Art Style To Defaults".
    /// Batch: Unity -batchmode -executeMethod Diverse.EditorTools.ArtStyleDefaults.RunBatch (RunBatchReset to overwrite)
    /// </summary>
    public static class ArtStyleDefaults
    {
        const string Path = "Assets/_Project/Data/ArtStyle.asset";

        // One palette for everything, taken from the approved example picture: soft greens, warm tans, cool dark stone,
        // a navy outline; the extra blues, whites and purples are mixed to sit with them
        static readonly (string name, string hex)[] Palette =
        {
            ("outline", "171a26"), ("stone deep", "21212c"), ("stone darker", "272834"), ("stone dark", "2f2e39"), ("ash", "3e3a41"),
            ("stone", "3a3e4c"), ("stone light", "4a4b55"), ("stone pale", "5c5a61"), ("grey", "8a8a94"), ("grey pale", "c9c6cc"), ("white", "f2efe6"),
            ("green deep", "194e41"), ("green dark", "387840"), ("green shade", "589543"), ("grass", "70a744"), ("grass light", "84ac49"), ("lime", "a8c44b"),
            ("earth dark", "544440"), ("earth", "7a6457"), ("tan dark", "a48656"), ("tan", "c1a572"), ("yellow", "e1c549"),
            ("red deep", "572526"), ("red", "cb3416"), ("orange", "f46616"),
            ("blue deep", "1f4f7a"), ("blue", "2986b8"), ("blue light", "6cc0dc"), ("ice", "c8ecef"),
            ("purple deep", "4b3566"), ("purple", "8a5fa8"), ("lilac", "c79bd6"),
            ("sand", "d9c38c"), ("ash line", "39373f"), ("pink", "e7a9cf"), ("pink dark", "b86f9c"),
        };

        static GroundLook L(Ground g, TilePattern p, int shade, params int[] ramp) => new GroundLook { ground = g, pattern = p, ramp = ramp, shade = shade };
        static CliffLook C(string theme, WallPattern w, int accent, int lip, params int[] ramp) => new CliffLook { theme = theme, wall = w, ramp = ramp, accent = accent, lip = lip };
        static GroundLook With(this GroundLook l, int accent = -1, int petal = -1, int edge = -1, int[] bank = null)
        {
            l.accent = accent; l.petal = petal; l.edge = edge; l.bank = bank ?? new int[0];
            return l;
        }

        static ArtStyleDef Defaults() => new ArtStyleDef
        {
            palette = System.Array.ConvertAll(Palette, p => Pal.Hex(p.hex)),
            paletteNames = System.Array.ConvertAll(Palette, p => p.name),
            outline = 0, shadow = 1, shadowAlpha = 0.45f,
            grounds = new List<GroundLook>
            {
                L(Ground.Grass, TilePattern.Tufts, 13, 12, 14, 15, 16).With(accent: 21, petal: 20),
                L(Ground.DarkGrass, TilePattern.Tufts, 12, 11, 13, 14),
                L(Ground.Path, TilePattern.Pebbles, 19, 19, 20, 32),
                L(Ground.Sand, TilePattern.Ripples, 19, 19, 32, 10),
                L(Ground.Snow, TilePattern.Speckles, 8, 8, 9, 10),
                L(Ground.Swamp, TilePattern.Puddles, 11, 11, 12, 17),
                L(Ground.Ash, TilePattern.Tiles, 3, 33, 4, 6).With(accent: 24),
                L(Ground.Plaza, TilePattern.Cobbles, 5, 5, 6, 7, 8),
                L(Ground.Water, TilePattern.Ripples, 25, 25, 26, 27, 28).With(accent: 21, edge: 27, bank: new[] { 17, 18, 18, -1 }),
                L(Ground.DeepWater, TilePattern.Ripples, 0, 0, 25, 26, 27).With(edge: 27, bank: new[] { 17, 18, 18, -1 }),
                L(Ground.Wood, TilePattern.Planks, 17, 17, 18, 19),
                L(Ground.Cliff, TilePattern.Speckles, 5, 5, 6, 7),
                L(Ground.Peak, TilePattern.Speckles, 6, 6, 7, 8),
                L(Ground.Lava, TilePattern.Crust, 22, 22, 23, 24, 21).With(edge: 22, bank: new[] { 0, 2 }),
                L(Ground.Toxic, TilePattern.Puddles, 13, 13, 15, 16, 10).With(edge: 16, bank: new[] { 0, 11, -1 }),
                L(Ground.Ice, TilePattern.Cracks, 27, 27, 28, 10),
                L(Ground.Crystal, TilePattern.Sparkles, 8, 8, 9, 31, 10),
                L(Ground.Flagstone, TilePattern.Slabs, 5, 5, 6, 7).With(accent: 13),
                L(Ground.Mirror, TilePattern.Cracks, 0, 0, 29, 30),
                L(Ground.Redrock, TilePattern.Strata, 17, 17, 18, 19),
            },
            edgeNoise = 0.28f, edgeRim = 0.07f,
            cliffs = new List<CliffLook>
            {
                C("meadow", WallPattern.Columns, 17, -1, 2, 5, 6, 7),
                C("overgrowth", WallPattern.Columns, 12, -1, 2, 5, 6, 7),
                C("volcano", WallPattern.Columns, -1, 18, 0, 1, 2, 3),
                C("crystal", WallPattern.Strata, 31, 9, 0, 29, 30, 31),
                C("glacier", WallPattern.Blocks, -1, 28, 25, 26, 27, 28),
                C("canyon", WallPattern.Strata, -1, 20, 17, 18, 19, 20),
                C("fortress", WallPattern.Bricks, -1, 7, 2, 5, 6, 7),
                C("mirror", WallPattern.Columns, 27, 30, 0, 29, 30),
                C("swamp", WallPattern.Columns, 17, -1, 1, 3, 17, 18),
            },
            cornerRadius = 5, footShade = 2,
            characterHeight = new Vector2Int(16, 20),
            prompt = "Clean low-resolution pixel art for a top-down action roguelike: tiles are 16 x 16 pixels, characters 16 to 20 pixels tall, " +
                     "every pixel a big visible square. One limited palette for everything (soft greens, warm tans, cool dark grey stone, a dark navy " +
                     "outline), each material shaded with 2 to 4 flat tones, simple bold readable shapes, clean 1-pixel dark outlines, light from the " +
                     "top-left, no gradients, no noise, no dithering texture, no fine detail. Straight top-down 3/4 view with the grid aligned to the " +
                     "screen (never isometric).",
        };

        [MenuItem("Diverse/Art/Create Missing Art Style")]
        public static void Create() => Make(false);

        [MenuItem("Diverse/Art/Reset Art Style To Defaults")]
        public static void Reset() => Make(true);

        static void Make(bool overwrite)
        {
            var a = AssetDatabase.LoadAssetAtPath<ArtStyleData>(Path);
            if (a == null) { a = ScriptableObject.CreateInstance<ArtStyleData>(); a.def = Defaults(); AssetDatabase.CreateAsset(a, Path); }
            else if (overwrite) { a.def = Defaults(); EditorUtility.SetDirty(a); }
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Resources/GameDatabase.asset");
            db.artStyle = a;
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            TileBaker.Reset();
            Debug.Log("[ArtStyleDefaults] art style ready");
        }

        public static void RunBatch() => Batch(false);
        public static void RunBatchReset() => Batch(true);

        static void Batch(bool overwrite)
        {
            try { Make(overwrite); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
