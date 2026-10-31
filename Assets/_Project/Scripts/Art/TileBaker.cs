using System.Collections.Generic;
using UnityEngine;

namespace Diverse
{
    /// <summary>
    /// A chunk's ground as pixel art, built from the art rules (DB.ArtStyle): every dot takes a palette colour.
    /// - Each ground has a pattern drawn from its palette ramp (grass blades and flowers, path specks, cobbles, planks…),
    ///   continuous across tiles (patterns use world dot coordinates).
    /// - Grounds meet in soft organic borders, not tile steps: a dot belongs to a ground when that ground covers enough of
    ///   the four nearest tile centres (blended), nudged by smooth noise. The higher ground gets a darker rim along the
    ///   border. Liquids are bordered the same way; land just north of a liquid shows a short bank, the liquid a foam
    ///   (or glowing crust) line where it meets land.
    /// - The 3/4 view: cliffs stand one or two levels (16 dots each) high. Every dot shows the frontmost (southernmost)
    ///   column that reaches it: its top (rounded outer corners, an outline where it drops away, a fringe along its south
    ///   lip) or its south wall (the theme's wall pattern, a dark foot line). The ground at a cliff's foot is shaded.
    /// Custom-mode worlds: a designed ground texture replaces the pattern, snapped to the palette.
    /// </summary>
    public static class TileBaker
    {
        public const int T = 16;            // dots per tile
        const int B = 3;                    // tiles of the neighbours read around the chunk
        const int Half = 8;                 // dots per half level (a cliff level is two)

        static ArtStyleDef S => DB.ArtStyle;
        static Color32[] pal;
        static readonly Dictionary<Ground, GroundLook> looks = new Dictionary<Ground, GroundLook>();
        static readonly Dictionary<int, CliffLook> cliffs = new Dictionary<int, CliffLook>();
        static readonly Dictionary<int, byte> snap = new Dictionary<int, byte>();
        static int maxBank;

        // Tiles of the chunk being baked (with the border)
        static Ground[] g;
        static byte[] th;
        static int n, ox, oy;               // map size, world tile of map index 0

        static void Prepare()
        {
            if (pal != null && pal.Length == S.palette.Length && pal.Length > 0) return;
            pal = S.palette.Length > 0 ? S.palette : new[] { new Color32(0, 0, 0, 255), new Color32(128, 128, 128, 255), new Color32(255, 255, 255, 255) };
            looks.Clear(); cliffs.Clear(); snap.Clear();
            maxBank = 0;
            foreach (var l in S.grounds) { looks[l.ground] = l; maxBank = Mathf.Max(maxBank, l.bank != null ? l.bank.Length : 0); }
            for (int i = 0; i < DB.Themes.Count; i++)
            {
                var c = S.cliffs.Find(x => x.theme == DB.Themes[i].id) ?? (S.cliffs.Count > 0 ? S.cliffs[0] : new CliffLook());
                cliffs[i] = c;
            }
        }

        /// <summary>Forget cached looks (the art rules changed in the editor).</summary>
        public static void Reset() { pal = null; }

        public static Texture2D Bake(WorldStreamer w, Vector2Int k, Texture2D into = null)
        {
            Prepare();
            int N = WorldGen.Chunk;
            n = N + 2 * B; ox = k.x * N - B; oy = k.y * N - B;
            if (g == null || g.Length != n * n) { g = new Ground[n * n]; th = new byte[n * n]; }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    g[y * n + x] = w.GroundAtTile(ox + x, oy + y);
                    th[y * n + x] = (byte)w.Gen.ThemeAt(ox + x + 0.5f, oy + y + 0.5f).index;
                }
            int W = N * T;
            var px = new Color32[W * W];
            int wx0 = k.x * N * T, wy0 = k.y * N * T;
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = pal[Mathf.Clamp(Dot(wx0 + x, wy0 + y), 0, pal.Length - 1)];
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
        static Ground ThemeGround(int tx, int ty) => DB.Themes[Mathf.Clamp(Theme(tx, ty), 0, DB.Themes.Count - 1)].ground;
        static CliffLook Cliff(int tx, int ty) => cliffs.TryGetValue(Theme(tx, ty), out var c) ? c : new CliffLook();

        /// <summary>Height in half levels: ground 0, Cliff 2, Peak 4 (liquids lie flat; their bank shows the drop).</summary>
        static int H(Ground t) => WorldGen.Level(t) * 2;

        /// <summary>The ground a tile shows on the flat (raised tiles count as their theme's ground).</summary>
        static Ground FG(int tx, int ty) { var t = G(tx, ty); return H(t) > 0 ? ThemeGround(tx, ty) : t; }

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

        // ───────────── One dot ─────────────

        static int Dot(int wx, int wy)
        {
            int tx = MathX.FloorDiv(wx, T), lx = wx - tx * T;
            // The frontmost raised column reaching this dot: column j spans dots [j*T, j*T + T + h*Half)
            int jFrom = MathX.FloorDiv(wy - T - 4 * Half, T), jTo = MathX.FloorDiv(wy, T);
            for (int j = jFrom; j <= jTo; j++)
            {
                var t = G(tx, j);
                int h = H(t);
                if (h <= 0 || wy < j * T || wy >= j * T + T + h * Half) continue;
                int top = j * T + h * Half;
                if (wy >= top)
                {
                    int ly = wy - top;
                    if (Cut(tx, j, h, lx, ly)) continue;
                    return Raised(tx, j, h, wx, wy - h * Half, lx, ly);
                }
                if (Cut(tx, j, h, lx, 0)) continue;
                return Face(tx, j, h, wx, top - 1 - wy);
            }
            return Flat(wx, wy);
        }

        // ───────────── Cliffs ─────────────

        /// <summary>Is top dot (lx, ly) of raised tile (tx, ty) outside its rounded outer corners?</summary>
        static bool Cut(int tx, int ty, int h, int lx, int ly)
        {
            int r = S.cornerRadius;
            if (r <= 0) return false;
            bool w = lx < r && H(G(tx - 1, ty)) < h, e = lx > T - 1 - r && H(G(tx + 1, ty)) < h;
            if (!w && !e) return false;
            bool s = ly < r && H(G(tx, ty - 1)) < h, nn = ly > T - 1 - r && H(G(tx, ty + 1)) < h;
            if (!s && !nn) return false;
            float x = w ? r - 0.5f - lx : lx - (T - 1 - r) + 0.5f;
            float y = s ? r - 0.5f - ly : ly - (T - 1 - r) + 0.5f;
            return x * x + y * y > r * r;
        }

        /// <summary>Is top dot (lx, ly) of tile (tx, ty), possibly over the tile's edge, part of a top at least h high?</summary>
        static bool Inside(int tx, int ty, int h, int lx, int ly)
        {
            if (lx < 0) { tx--; lx += T; } else if (lx >= T) { tx++; lx -= T; }
            if (ly < 0) { ty--; ly += T; } else if (ly >= T) { ty++; ly -= T; }
            int ht = H(G(tx, ty));
            return ht >= h && !Cut(tx, ty, ht, lx, ly);
        }

        /// <summary>The top of raised tile (tx, ty) at ground dot (gx, gy), local (lx, ly) with ly 0 = its south edge.</summary>
        static int Raised(int tx, int ty, int h, int gx, int gy, int lx, int ly)
        {
            var tg = ThemeGround(tx, ty);
            var look = Look(tg);
            var cl = Cliff(tx, ty);
            bool grassy = cl.lip < 0;
            int lip = grassy ? R(look.ramp, look.ramp.Length - 1) : cl.lip;
            // An outline where the land drops away to the west, east and north
            if (!Inside(tx, ty, h, lx - 1, ly) || !Inside(tx, ty, h, lx + 1, ly) || !Inside(tx, ty, h, lx, ly + 1)) return S.outline;
            // The south lip: a jagged fringe two dots deep
            if (!Inside(tx, ty, h, lx, ly - 1)) return lip;
            if (!Inside(tx, ty, h, lx, ly - 2)) return Hs(MathX.FloorDiv(gx, 2), ty, 7711) % 3 != 0 ? lip : Pattern(look, tg, gx, gy);
            // The other edges: a one-dot rim inside the outline (rock rims only)
            if (!grassy && (!Inside(tx, ty, h, lx - 2, ly) || !Inside(tx, ty, h, lx + 2, ly) || !Inside(tx, ty, h, lx, ly + 2))) return lip;
            return Pattern(look, tg, gx, gy);
        }

        /// <summary>The south wall of column (tx, ty), d dots below its top edge.</summary>
        static int Face(int tx, int ty, int h, int gx, int d)
        {
            int visible = (h - H(G(tx, ty - 1))) * Half;      // dots of wall shown above the ground in front
            var cl = Cliff(tx, ty);
            if (cl.lip < 0)
            {
                // Grass hangs over the edge in short strands
                uint hs = Hs(gx, ty, 7721) % 5;
                if (d < (hs == 0 ? 2 : hs == 1 ? 1 : 0)) return Shade(Look(ThemeGround(tx, ty)));
            }
            if (d >= visible - 1) return S.outline;                              // the foot line
            int col = Wall(cl, gx, d);
            if (d >= visible - 4) col = Darker(cl.ramp, col);                     // shaded foot
            var front = FG(tx, ty - 1);
            var fl = Look(front);
            if (WorldGen.IsLiquid(front) && fl.edge >= 0 && d >= visible - 2) col = fl.edge;   // where it stands in a liquid
            return col;
        }

        static int Darker(int[] r, int c)
        {
            for (int i = 1; i < r.Length; i++) if (r[i] == c) return r[i - 1];
            return r.Length > 0 ? r[0] : c;
        }

        /// <summary>A wall pattern at wall dot (u along the wall, v down from the top edge).</summary>
        static int Wall(CliffLook cl, int u, int v)
        {
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
                    int brk = 5 + (int)(Hs(id, 1, 7432) % 7);
                    if (v == brk && Hf(u, id, 7433) > 0.25f) return R(r, 0);
                    int c = Hf(id, v > brk ? 1 : 0, 7434) > 0.5f ? R(r, 1) : R(r, 2);
                    if (lx == seam + 1 && v < brk) c = R(r, 3);
                    if (cl.accent >= 0 && Hf(MathX.FloorDiv(u, 3), MathX.FloorDiv(v, 3), 7441) > 0.9f) c = cl.accent;
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
        static Ground Mat(int gx, int gy, out bool rim)
        {
            rim = false;
            var own = FG(MathX.FloorDiv(gx, T), MathX.FloorDiv(gy, T));
            float fx = (gx + 0.5f) / T - 0.5f, fy = (gy + 0.5f) / T - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float ax = fx - x0, ay = fy - y0;
            cand[0] = FG(x0, y0); cand[1] = FG(x0 + 1, y0); cand[2] = FG(x0, y0 + 1); cand[3] = FG(x0 + 1, y0 + 1);
            if (cand[0] == cand[1] && cand[0] == cand[2] && cand[0] == cand[3]) return own;
            for (int i = 0; i < 4; i++) if (cand[i] == Ground.Wood) return own;       // bridges keep their tile shape
            wt[0] = (1 - ax) * (1 - ay); wt[1] = ax * (1 - ay); wt[2] = (1 - ax) * ay; wt[3] = ax * ay;
            float nudge = (Noise.Perlin(gx, gy, 7801, 0.11f) - 0.5f) * 2f * S.edgeNoise + (Hf(gx, gy, 7802) - 0.5f) * 0.05f;
            // Try the candidates from the highest priority down
            int done = int.MaxValue;
            for (int pass = 0; pass < 4; pass++)
            {
                int p = int.MinValue; Ground c = own;
                for (int i = 0; i < 4; i++) { int q = Pri(cand[i]); if (q < done && q > p) { p = q; c = cand[i]; } }
                if (p == int.MinValue) break;
                done = p;
                float m = 0;
                for (int i = 0; i < 4; i++) if (Pri(cand[i]) >= p) m += wt[i];
                float v = m + nudge - 0.5f;
                if (v > 0 || m >= 0.999f) { rim = v < S.edgeRim && m < 0.999f; return c; }
            }
            return own;
        }

        static bool Liquid(Ground t) => WorldGen.IsLiquid(t);

        static bool NearLiquid(int tx, int ty)
        {
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (Liquid(FG(tx + dx, ty + dy))) return true;
            return false;
        }
        static bool NearLand(int tx, int ty)
        {
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (!Liquid(FG(tx + dx, ty + dy))) return true;
            return false;
        }
        static bool LandAt(int gx, int gy) => !Liquid(Mat(gx, gy, out _));

        static int Flat(int gx, int gy)
        {
            int tx = MathX.FloorDiv(gx, T), ty = MathX.FloorDiv(gy, T);
            var m = Mat(gx, gy, out bool rim);
            var look = Look(m);
            if (Liquid(m))
            {
                // Foam (or a glowing crust) where it meets land
                if (look.edge >= 0 && NearLand(tx, ty) && (LandAt(gx - 1, gy) || LandAt(gx + 1, gy) || LandAt(gx, gy + 1) || LandAt(gx, gy - 1))) return look.edge;
                return Pattern(look, m, gx, gy);
            }
            // A bank where a liquid lies just south
            if (maxBank > 0 && NearLiquid(tx, ty))
                for (int k = 1; k <= maxBank; k++)
                {
                    var below = Mat(gx, gy - k, out _);
                    if (!Liquid(below)) continue;
                    if (m == Ground.Wood) { if (k <= 2) return R(look.ramp, 0); break; }
                    var bank = Look(below).bank;
                    if (bank == null || k > bank.Length) break;
                    return bank[k - 1] >= 0 ? bank[k - 1] : Shade(look);
                }
            int c = rim ? Shade(look) : Pattern(look, m, gx, gy);
            // Shade at the foot of a cliff
            int hn = H(G(tx, ty + 1));
            if (hn > 0 && gy - ty * T >= T - S.footShade && !Cut(tx, ty + 1, hn, gx - tx * T, 0)) c = Shade(look);
            return c;
        }

        // ───────────── Ground patterns ─────────────

        /// <summary>A grass tuft (three blades fanning up) on a sparse grid, or -1.</summary>
        static int Tuft(int gx, int gy, int chance, int blade, int root, int salt)
        {
            int cx = MathX.FloorDiv(gx, 9), cy = MathX.FloorDiv(gy, 9);
            uint hs = Hs(cx, cy, salt);
            if ((hs & 0xFF) >= chance) return -1;
            int u = gx - cx * 9 - (int)((hs >> 8) % 4), v = gy - cy * 9 - (int)((hs >> 12) % 5);
            if (v == 0 && u == 2) return root;
            if (v == 1 && u >= 1 && u <= 3) return u == 2 ? root : blade;
            if (v == 2 && (u == 0 || u == 2 || u == 4)) return blade;
            return -1;
        }

        static int Pattern(GroundLook look, Ground t, int gx, int gy)
        {
            // A designed ground (custom mode) wins, snapped to the palette
            if (DesignRuntime.GroundTexel(t, gx, gy, out var texel)) return Snap(texel);
            var r = look.ramp;
            int b = R(r, 1), sh = Shade(look);
            switch (look.pattern)
            {
                case TilePattern.Tufts:
                {
                    int tf = Tuft(gx, gy, 95, sh, R(r, 0), 7501);
                    if (tf >= 0) return tf;
                    if (look.accent >= 0)
                    {
                        // Flowers in twos and threes: four petals round a heart
                        int cx = MathX.FloorDiv(gx, 16), cy = MathX.FloorDiv(gy, 16);
                        uint hs = Hs(cx, cy, 7505);
                        if ((hs & 0xFF) < 34)
                        {
                            int u = gx - cx * 16 - 2 - (int)((hs >> 8) % 7), v = gy - cy * 16 - 3 - (int)((hs >> 12) % 7);
                            int count = 2 + (int)((hs >> 16) % 2);
                            for (int i = 0; i < count; i++)
                            {
                                int fu = u - (i == 1 ? 4 : i == 2 ? 1 : 0), fv = v - (i == 1 ? -1 : i == 2 ? -4 : 0);
                                if (fu == 0 && fv == 0) return look.accent;
                                if (Mathf.Abs(fu) + Mathf.Abs(fv) == 1) return look.petal >= 0 ? look.petal : look.accent;
                            }
                        }
                    }
                    float f = Hf(gx, gy, 7503);
                    if (f > 0.993f) return R(r, 2);
                    if (f < 0.006f) return sh;
                    return b;
                }
                case TilePattern.Pebbles:
                {
                    int cx = MathX.FloorDiv(gx, 6), cy = MathX.FloorDiv(gy, 6);
                    uint hs = Hs(cx, cy, 7511);
                    int u = gx - cx * 6, v = gy - cy * 6;
                    int px = 1 + (int)((hs >> 8) % 4), py = 1 + (int)((hs >> 12) % 4);
                    if ((hs & 0xFF) < 70 && v == py && (u == px || ((hs >> 16) & 1) == 1 && u == px + 1)) return R(r, 0);
                    if ((hs & 0xFF) > 250 && v == py && u == px) return R(r, 2);
                    return b;
                }
                case TilePattern.Ripples:
                {
                    int row = MathX.FloorDiv(gy, 6), v = gy - row * 6;
                    int seg = MathX.FloorDiv(gx + row * 5, 11), u = gx + row * 5 - seg * 11;
                    float f = Hf(seg, row, 7521);
                    if (v == 3 && u < 3 && f < 0.14f) return R(r, 2);
                    if (look.accent >= 0 && v == 3 && u == 5 && f > 0.96f) return look.accent;
                    return b;
                }
                case TilePattern.Speckles:
                {
                    float f = Hf(gx, gy, 7531);
                    if (f > 0.975f) return R(r, 2);
                    if (f < 0.02f) return R(r, 0);
                    if (look.accent >= 0 && f > 0.5f && f < 0.5035f) return look.accent;
                    return b;
                }
                case TilePattern.Tiles:
                {
                    // Worn floor: faint tile lines, sparse dry tufts, rare embers
                    if (Mod(gx, T) == 0 || Mod(gy, T) == 0) return R(r, 0);
                    int tf = Tuft(gx, gy, 55, R(r, 2), sh, 7535);
                    if (tf >= 0) return tf;
                    float f = Hf(gx, gy, 7536);
                    if (f < 0.01f) return sh;
                    if (look.accent >= 0 && f > 0.9994f) return look.accent;
                    return b;
                }
                case TilePattern.Cobbles:
                {
                    // Rounded stones in staggered rows, a gap of mortar between them
                    int row = MathX.FloorDiv(gy, 7), v = gy - row * 7;
                    int off = (int)(Hs(row, 0, 7491) % 9);
                    int sx = MathX.FloorDiv(gx + off, 9), u = gx + off - sx * 9;
                    bool corner = (u == 0 || u == 7) && (v == 0 || v == 5);
                    if (u == 8 || v == 6 || corner) return R(r, 0);
                    if (v == 5 || u == 0) return R(r, 3);
                    return Hf(sx, row, 7492) > 0.7f ? R(r, 1) : R(r, 2);
                }
                case TilePattern.Slabs:
                {
                    int row = MathX.FloorDiv(gy, 12), v = gy - row * 12;
                    int off = (int)(Hs(row, 0, 7543) % 12);
                    int sx = MathX.FloorDiv(gx + off, 14), u = gx + off - sx * 14;
                    if (v == 0 || u == 13) return look.accent >= 0 && Hf(gx, gy, 7541) > 0.7f ? look.accent : R(r, 0);
                    if (v == 11 || u == 0) return R(r, 2);
                    return b;
                }
                case TilePattern.Planks:
                {
                    int sx = MathX.FloorDiv(gx, 4), u = gx - sx * 4;
                    if (u == 3) return R(r, 0);
                    int cut = (int)(Hs(sx, 0, 7551) % 16);
                    if (MathX.FloorDiv(gy - cut, 16) * 16 + cut == gy) return R(r, 0);
                    return u == 0 ? R(r, 2) : R(r, 1);
                }
                case TilePattern.Cracks:
                {
                    int cx = MathX.FloorDiv(gx, 12), cy = MathX.FloorDiv(gy, 12);
                    uint hs = Hs(cx, cy, 7561);
                    int u = gx - cx * 12, v = gy - cy * 12;
                    if ((hs & 0xFF) < 110)
                    {
                        int a = 2 + (int)((hs >> 8) % 8);
                        int along = a + (v * ((hs >> 16) % 2 == 0 ? 1 : -1)) / 2;
                        if (u == along && v > 1 && v < 10) return R(r, 2);
                    }
                    return Hf(gx, gy, 7562) > 0.985f ? R(r, 2) : b;
                }
                case TilePattern.Puddles:
                {
                    float p = Noise.Perlin(gx, gy, 7571, 0.045f);
                    if (p > 0.7f) return R(r, 0);
                    if (p > 0.67f) return R(r, 2);
                    return Hf(gx, gy, 7572) > 0.98f ? R(r, 2) : b;
                }
                case TilePattern.Crust:
                {
                    // Molten: the bright base with darker streaks drifting sideways, rare glints
                    float p = Noise.Perlin(gx * 0.55f, gy, 7581, 0.16f) * 0.65f + Noise.Perlin(gx, gy, 7583, 0.3f) * 0.35f;
                    if (p > 0.56f) return R(r, 1);
                    if (Hf(gx, gy, 7582) > 0.996f) return R(r, 3);
                    return R(r, 2);
                }
                case TilePattern.Strata:
                {
                    int w = (int)(Noise.Perlin(gx, 0, 7591, 0.08f) * 4);
                    int band = MathX.FloorDiv(gy + w, 6), v = gy + w - band * 6;
                    if (v == 0 && Hf(gx / 5, band, 7592) > 0.4f) return R(r, 0);
                    return band % 2 == 0 ? b : R(r, 2);
                }
                case TilePattern.Sparkles:
                {
                    int cx = MathX.FloorDiv(gx, 9), cy = MathX.FloorDiv(gy, 9);
                    uint hs = Hs(cx, cy, 7601);
                    int u = gx - cx * 9, v = gy - cy * 9;
                    int px = 2 + (int)((hs >> 8) % 5), py = 2 + (int)((hs >> 12) % 5);
                    if ((hs & 0xFF) < 40 && ((u == px && Mathf.Abs(v - py) <= 1) || (v == py && Mathf.Abs(u - px) <= 1))) return u == px && v == py ? R(r, 3) : R(r, 2);
                    if ((hs & 0xFF) > 200 && u == px && v == py) return R(r, 0);
                    return b;
                }
                default: return b;
            }
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
