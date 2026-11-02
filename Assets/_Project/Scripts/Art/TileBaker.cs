using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// A chunk's ground as pixel art, built from the art rules (DB.ArtStyle): every dot takes a palette colour.
    /// - Each ground has a pattern drawn from its palette ramp (grass blades and flowers, path specks, cobbles, planks…),
    ///   continuous across tiles (patterns use world dot coordinates).
    /// - TileBakerEdges finishes stable, bevelled material shapes with short pixel fringes, banks and shallow shelves.
    ///   Water carries broken foam marks; wooden decks share the underlying road's orientation.
    /// - TileBakerContour projects connected, bevelled tiers using ArtStyle.cliffRise (straight RPG camera by default).
    ///   Each dot shows the nearest top or side surface, with a joined cap, directional rock shading and contact line.
    /// Legacy authored ground textures, when present, are snapped to the same palette.
    /// </summary>
    public static partial class TileBaker
    {
        public const int T = 16;            // dots per tile
        const int B = 5;                    // two 32px levels + one tile of contour sampling across chunk seams

        static ArtStyleDef S => DB.ArtStyle;
        static Color32[] pal;
        static readonly Dictionary<Ground, GroundLook> looks = new Dictionary<Ground, GroundLook>();
        static readonly Dictionary<int, CliffLook> cliffs = new Dictionary<int, CliffLook>();
        static readonly Dictionary<int, byte> snap = new Dictionary<int, byte>();

        // Tiles of the chunk being baked (with the border)
        static Ground[] g;
        static Ground[] flatGround;
        static byte[] th;
        static int n, ox, oy;               // map size, world tile of map index 0
        static byte[] neighbours;
        static byte[] habitatCover;

        static void Prepare()
        {
            if (pal != null && pal.Length == S.palette.Length && pal.Length > 0) return;
            pal = S.palette.Length > 0 ? S.palette : new[] { new Color32(0, 0, 0, 255), new Color32(128, 128, 128, 255), new Color32(255, 255, 255, 255) };
            looks.Clear(); cliffs.Clear(); snap.Clear();
            foreach (var l in S.grounds) looks[l.ground] = l;
            for (int i = 0; i < DB.Themes.Count; i++)
            {
                var c = S.cliffs.Find(x => x.theme == DB.Themes[i].id) ?? (S.cliffs.Count > 0 ? S.cliffs[0] : new CliffLook());
                cliffs[i] = c;
            }
        }

        /// <summary>Forget cached looks (the art rules changed in the editor).</summary>
        public static void Reset() { pal = null; }

        public static Texture2D Bake(WorldStreamer w, Vector2Int k, Texture2D into = null)
            => BakeRegion(w, k * WorldGen.Chunk, WorldGen.Chunk, into);

        static IEnumerable<int> PrepareRegionSteps(WorldStreamer w, Vector2Int tileOrigin, int tiles)
        {
            Prepare();
            int N = tiles;
            n = N + 2 * B; ox = tileOrigin.x - B; oy = tileOrigin.y - B;
            if (g == null || g.Length != n * n) { g = new Ground[n * n]; flatGround = new Ground[n * n]; th = new byte[n * n]; neighbours = new byte[n * n]; habitatCover = new byte[n * n]; }
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    g[y * n + x] = w.GroundAtTile(ox + x, oy + y);
                    foreach (var cliff in w.Gen.CliffThemeSteps(ox+x+.5f,oy+y+.5f,WorldGen.Level(g[y*n+x])>0))
                    {
                        if (cliff != null) th[y*n+x] = (byte)cliff.index;
                        yield return 0;
                    }
                    flatGround[y * n + x] = WorldGen.Level(g[y * n + x]) > 0 ? ThemeGround(ox + x, oy + y) : g[y * n + x];
                    var at = new Vector2(ox + x + .5f, oy + y + .5f);
                    var theme = w.Gen.ThemeAt(at.x, at.y);
                    var material = flatGround[y * n + x];
                    habitatCover[y*n+x] = 0;
                    if (!WorldGen.IsLiquid(material) && material is not (Ground.Path or Ground.Plaza or Ground.Wood))
                        foreach (float cover in w.Scenery.GroundCoverSteps(at,theme))
                        {
                            if (cover >= 0) habitatCover[y*n+x] = (byte)(255*cover);
                            yield return 0;
                        }
                    // A cold world query can be expensive; yield between individual terrain samples.
                    yield return 0;
                }
            }
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                byte flags = 0;
                bool uniform = true;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    var material = FG(ox + x + dx, oy + y + dy);
                    flags |= (byte)(Liquid(material) ? 1 : 2);
                    if (material != flatGround[y * n + x]) uniform = false;
                }
                if (uniform) flags |= 4;
                neighbours[y * n + x] = flags;
                }
                yield return 0;
            }
            foreach (int step in PrepareSurfaceSteps(w)) yield return step;
            foreach (int step in PrepareContourSteps()) yield return step;
        }

        /// <summary>Same world-coordinate renderer for one chunk or a contiguous square review region.</summary>
        public static Texture2D BakeRegion(WorldStreamer w, Vector2Int tileOrigin, int tiles, Texture2D into = null)
        {
            // A review capture may synchronously bake while a streaming operation is between frames. Give it separate
            // scratch buffers so it cannot overwrite the in-progress operation's world data.
            if (activeOperation != null) ReleaseScratchReferences();
            foreach (int step in PrepareRegionSteps(w, tileOrigin, tiles)) { }
            int N = tiles;
            int W = N * T;
            var px = new Color32[W * W];
            int wx0 = tileOrigin.x * T, wy0 = tileOrigin.y * T;
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = pal[Mathf.Clamp(ContourDot(wx0 + x, wy0 + y), 0, pal.Length - 1)];
            var tex = into != null && into.width == W ? into : new Texture2D(W, W, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ───────────── Tiles ─────────────

        static Ground G(int tx, int ty)
        {
            int x = Mathf.Clamp(tx - ox, 0, n - 1), y = Mathf.Clamp(ty - oy, 0, n - 1);
            return g[y * n + x];
        }
        static int Theme(int tx, int ty) => th[Mathf.Clamp(ty - oy, 0, n - 1) * n + Mathf.Clamp(tx - ox, 0, n - 1)];
        static Ground ThemeGround(int tx, int ty) => DB.Themes[Mathf.Clamp(Theme(tx, ty), 0, DB.Themes.Count - 1)].cliffTop;
        static CliffLook Cliff(int tx, int ty) => cliffs.TryGetValue(Theme(tx, ty), out var c) ? c : new CliffLook();

        /// <summary>The ground a tile shows on the flat (raised tiles count as their theme's ground).</summary>
        static Ground FG(int tx, int ty) => flatGround[Mathf.Clamp(ty - oy, 0, n - 1) * n + Mathf.Clamp(tx - ox, 0, n - 1)];

        /// <summary>Which ground reaches over which at a border (higher wins); liquids lowest.</summary>
        static int Pri(Ground t) => t switch
        {
            Ground.Wood => 9, Ground.Plaza => 8, Ground.Flagstone => 8, Ground.Path => 7, Ground.Snow => 5, Ground.Sand => 4, Ground.Redrock => 4,
            Ground.Ash => 3, Ground.Crystal => 3, Ground.Mirror => 3, Ground.Ice => 2, Ground.Swamp => 2, Ground.DarkGrass => 1,
            Ground.DeepWater => -2, Ground.Water => -1, Ground.Lava => -1, Ground.Toxic => -1, _ => 0,
        };

        static GroundLook Look(Ground t) => looks.TryGetValue(t, out var l) ? l : looks.TryGetValue(Ground.Grass, out var gl) ? gl : new GroundLook();
        static int R(int[] ramp, int i) => ramp == null || ramp.Length == 0 ? 0 : ramp[Mathf.Clamp(i, 0, ramp.Length - 1)];
        static int Shade(GroundLook l) => l.shade >= 0 ? l.shade : R(l.ramp, 0);
        static uint Hs(int a, int b, int salt) => Hash.U(a, b, salt);
        static float Hf(int a, int b, int salt) => (Hs(a, b, salt) & 0xFFFF) / 65536f;
        static int Mod(int a, int m) => a - MathX.FloorDiv(a, m) * m;

        static int Darker(int[] r, int c)
        {
            for (int i = 1; i < r.Length; i++) if (r[i] == c) return r[i - 1];
            return r.Length > 0 ? r[0] : c;
        }

        /// <summary>A wall pattern at wall dot (u along the wall, v down from the top edge).</summary>
        static int Wall(CliffLook cl, int u, int v)
        {
            int regional = RegionalWall(cl, u, v);
            if (regional >= 0) return regional;
            var r = cl.ramp;
            switch (cl.wall)
            {
                case WallPattern.Bricks:
                {
                    int row = v / 5, ry = v % 5, off = row % 2 == 0 ? 0 : 5;
                    int bx = MathX.FloorDiv(u + off, 10), lx = u + off - bx * 10;
                    if (ry == 4 || lx == 9) return R(r, 0);
                    if (ry == 0) return R(r, 3);
                    return Hf(bx, row, 7401) > 0.7f ? R(r, 1) : R(r, 2);
                }
                case WallPattern.Strata:
                {
                    int w = (int)(Noise.Perlin(u, 0, 7411, 0.12f) * 3);
                    int band = (v + w) / 4, by = (v + w) % 4;
                    if (by == 3) return R(r, 0);
                    int c = band % 3 == 0 ? R(r, 2) : band % 3 == 1 ? R(r, 1) : R(r, 3);
                    if (cl.accent >= 0 && Hf(u / 3, band, 7421) > 0.93f) c = cl.accent;
                    return c;
                }
                case WallPattern.Columns:
                {
                    // Tall slabs 5–9 dots wide split by dark seams, lit along their left side, broken once part-way down
                    int cell = MathX.FloorDiv(u, 7), lx = u - cell * 7;
                    int seam = (int)(Hs(cell, 0, 7431) % 3);
                    if (lx == seam) return R(r, 0);
                    int id = lx > seam ? cell : cell - 1;
                    int brk = 7 + (int)(Hs(id, 1, 7432) % 8);
                    if (v == brk && Hf(u, id, 7433) > 0.65f) return R(r, 0);
                    int c = Hf(id, v > brk ? 1 : 0, 7434) > 0.5f ? R(r, 1) : R(r, 2);
                    if (lx == seam + 1 && v < brk) c = R(r, 3);
                    if (cl.accent >= 0 && Hf(MathX.FloorDiv(u, 3), MathX.FloorDiv(v, 4), 7441) > 0.86f) c = cl.accent;
                    return c;
                }
                default:     // Blocks: two rows of big stones per level, staggered
                {
                    int row = v / 8, ry = v % 8;
                    int off = (int)(Hs(row, 0, 7451) % 7);
                    int bw = 11 + (int)(Hs(row, 1, 7452) % 4);
                    int bx = MathX.FloorDiv(u + off, bw), lx = u + off - bx * bw;
                    if (ry == 7 || lx == bw - 1) return R(r, 0);
                    if (ry == 0 || lx == 0) return R(r, 3);
                    int c = ry >= 5 ? R(r, 1) : R(r, 2);
                    if (cl.accent >= 0 && Hf(bx, row, 7461) > 0.8f && ry == 3 && lx > 2 && lx < 6) c = cl.accent;
                    return c;
                }
            }
        }

        // ───────────── Flat ground ─────────────

        static readonly Ground[] cand = new Ground[4];
        static readonly float[] wt = new float[4];

        /// <summary>
        /// The ground at dot (gx, gy): of the grounds on the four nearest tile centres, the highest one that covers more
        /// than half of them (blended by distance, nudged by noise). rim = the dot lies on that ground's border.
        /// </summary>
        static Ground MacroMat(int gx, int gy, out bool rim)
        {
            rim = false;
            int ownX = MathX.FloorDiv(gx, T), ownY = MathX.FloorDiv(gy, T);
            var own = FG(ownX, ownY);
            if ((neighbours[Mathf.Clamp(ownY - oy, 0, n - 1) * n + Mathf.Clamp(ownX - ox, 0, n - 1)] & 4) != 0) return own;
            float fx = (gx + 0.5f) / T - 0.5f, fy = (gy + 0.5f) / T - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float corner = Mathf.Max(1, S.groundCorner);
            float ax = Mathf.Clamp01((fx - x0 - .5f) * T / (2 * corner) + .5f);
            float ay = Mathf.Clamp01((fy - y0 - .5f) * T / (2 * corner) + .5f);
            cand[0] = FG(x0, y0); cand[1] = FG(x0 + 1, y0); cand[2] = FG(x0, y0 + 1); cand[3] = FG(x0 + 1, y0 + 1);
            if (cand[0] == cand[1] && cand[0] == cand[2] && cand[0] == cand[3]) return own;
            for (int i = 0; i < 4; i++) if (cand[i] == Ground.Wood) return own;       // bridges keep their tile shape
            wt[0] = (1 - ax) * (1 - ay); wt[1] = ax * (1 - ay); wt[2] = (1 - ax) * ay; wt[3] = ax * ay;
            // Stable bevelled corners, without per-pixel noise chewing into an otherwise straight edge.
            float nudge = 0;
            // Try the candidates from the highest priority down
            int done = int.MaxValue;
            for (int pass = 0; pass < 4; pass++)
            {
                int p = int.MinValue; Ground c = own;
                for (int i = 0; i < 4; i++) { int q = Pri(cand[i]); if (q < done && q > p) { p = q; c = cand[i]; } }
                if (p == int.MinValue) break;
                done = p;
                // Equal priority is not equal material. Choose the dominant material at this dot; taking the first
                // candidate made a neighbouring water texel cover lava in a tile-shaped blue patch.
                float dominant = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (Pri(cand[i]) != p) continue;
                    float share = 0;
                    for (int j = 0; j < 4; j++) if (cand[j] == cand[i]) share += wt[j];
                    if (share > dominant) { dominant = share; c = cand[i]; }
                }
                float m = 0;
                for (int i = 0; i < 4; i++) if (Pri(cand[i]) >= p) m += wt[i];
                float v = m + nudge - 0.5f;
                if (v > 0 || m >= 0.999f) { rim = v < S.edgeRim && m < 0.999f; return c; }
            }
            return own;
        }

        static bool Liquid(Ground t) => WorldGen.IsLiquid(t);

        static int Flat(int gx, int gy)
        {
            var material = Mat(gx, gy, out _);
            var look = Look(material);
            int color = Pattern(look, material, gx, gy);
            return SurfaceEdge(material, look, gx, gy, color);
        }

        static float HabitatAt(int gx, int gy)
        {
            float x = (gx + .5f) / T - .5f - ox, y = (gy + .5f) / T - .5f - oy;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float At(int a, int b) => habitatCover[Mathf.Clamp(b, 0, n - 1) * n + Mathf.Clamp(a, 0, n - 1)] / 255f;
            return Mathf.Lerp(Mathf.Lerp(At(ix, iy), At(ix + 1, iy), x - ix),
                Mathf.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), x - ix), y - iy);
        }
        // ───────────── Ground patterns ─────────────

        static int Pattern(GroundLook look, Ground t, int gx, int gy)
        {
            if (DesignRuntime.GroundTexel(t, gx, gy, out var texel)) return Snap(texel);
            return SurfacePattern(look, t, gx, gy);
        }
        /// <summary>Nearest palette colour (cached).</summary>
        public static byte Snap(Color32 c)
        {
            Prepare();
            int key = (c.r << 16) | (c.g << 8) | c.b;
            if (snap.TryGetValue(key, out var i)) return i;
            int best = 0, bd = int.MaxValue;
            for (int k = 0; k < pal.Length; k++)
            {
                int dr = pal[k].r - c.r, dg = pal[k].g - c.g, db = pal[k].b - c.b;
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bd) { bd = d; best = k; }
            }
            if (snap.Count > 20000) snap.Clear();
            return snap[key] = (byte)best;
        }

        public static Color32 PaletteColor(int i) { Prepare(); return pal[Mathf.Clamp(i, 0, pal.Length - 1)]; }
    }
}
