using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        static int Under(GroundLook l) => l.undertone >= 0 ? l.undertone : R(l.ramp, 1);
        static int Over(GroundLook l) => l.overtone >= 0 ? l.overtone : R(l.ramp, 1);

        static readonly ushort[] surfaceStamps =
        {
            0x0e0, 0x3f8, 0x7fc, 0xffe, 0x7fe, 0x7f0, 0x1e0, 0x080,
            0x000, 0x03c, 0x0fe, 0x7fe, 0xffc, 0x7f8, 0x1f0, 0x0c0,
            0x030, 0x0f8, 0x3fc, 0x7f8, 0xff8, 0x7fc, 0x1f0, 0x060,
            0x000, 0x1e0, 0x7f0, 0xff8, 0x7fe, 0x3ff, 0x0fe, 0x030,
        };

        // Broad, stepped pools of colour plus smaller authored pixel motifs. No per-pixel random speckling:
        // every mark belongs to a cluster, and the quiet base is still most of the combat floor.
        static int SurfaceBody(GroundLook l, int x, int y, int salt)
        {
            int size = Mathf.Max(16, S.surfacePatchSize);
            int cx = MathX.FloorDiv(x, size), cy = MathX.FloorDiv(y, size);
            uint h = Hs(cx, cy, salt);
            bool water = WorldGen.IsWater(l.ground);
            int scale = Mathf.Max(1, size / 13);
            int width = 12 * scale, height = 8 * (water ? 1 : scale);
            int u = Mod(x, size) - (size - width) / 2 - (int)((h >> 8) % 3) + 1;
            int v = Mod(y, size) - (size - height) / 2 - (int)((h >> 12) % 5) + 2;
            u = MathX.FloorDiv(u, scale); v = MathX.FloorDiv(v, water ? 1 : scale);
            if ((h & 3) < 3 && (uint)u < 12 && (uint)v < 8)
            {
                if ((h & 16) != 0) u = 11 - u;
                if ((surfaceStamps[(int)((h >> 20) % 4) * 8 + v] & (1 << u)) != 0)
                    return water || (h & 1) == 0 ? Under(l) : Over(l);
            }
            return R(l.ramp, 1);
        }

        static int SurfacePattern(GroundLook l, Ground ground, int x, int y)
        {
            var r = l.ramp;
            // Keep GroundLook.pattern editable: a material's default supplies the appropriate variant of a
            // shared pattern (sand/water ripples, solid/toxic puddles), while another selection changes the motifs.
            if (l.pattern == TilePattern.Plain) return R(r, 1);
            ground = l.pattern switch
            {
                TilePattern.Tufts => Ground.Grass, TilePattern.Moss => Ground.DarkGrass,
                TilePattern.Pebbles => Ground.Path, TilePattern.Cobbles => Ground.Plaza,
                TilePattern.Slabs => Ground.Flagstone, TilePattern.Planks => Ground.Wood,
                TilePattern.Snowdrifts => Ground.Snow, TilePattern.Cracks => Ground.Ice,
                TilePattern.Tiles or TilePattern.Speckles => Ground.Ash,
                TilePattern.CrystalBed or TilePattern.Sparkles => Ground.Crystal,
                TilePattern.MirrorPaving => Ground.Mirror, TilePattern.Strata or TilePattern.Sandstone => Ground.Redrock,
                TilePattern.Crust => Ground.Lava,
                _ => ground,
            };
            int b = SurfaceBody(l, x, y, 8701 + (int)ground);
            int cx = MathX.FloorDiv(x, 17), cy = MathX.FloorDiv(y, 15);
            uint h = Hs(cx, cy, 8721 + (int)ground);
            int u = Mod(x, 17) - 4 - (int)((h >> 8) % 8);
            int v = Mod(y, 15) - 4 - (int)((h >> 12) % 7);
            bool detail = (h & 255) < 230 * S.groundDetail;
            switch (ground)
            {
                case Ground.Grass:
                case Ground.DarkGrass:
                {
                    if (detail)
                    {
                        int type = (int)((h >> 20) % 3);
                        if (v == 0 && u >= -1 && u <= 1) return Under(l);
                        if (v == 1 && u >= -2 && u <= 2) return u == 0 ? R(r, 0) : Over(l);
                        if (v == 2 && (u == -3 || u == -1 || u == 2)) return Over(l);
                        if (v == 3 && (u == -2 || u == 1) && type != 1) return R(r, 2);
                        if (type == 2 && v == 1 && u >= 4 && u <= 6) return Under(l);
                        if (type == 2 && v == 2 && u == 5) return Over(l);
                    }
                    if ((h & 63) == 17 && l.accent >= 0)
                    {
                        if (u == 0 && v == 1) return l.accent;
                        if (Mathf.Abs(u) + Mathf.Abs(v - 1) == 1) return l.petal >= 0 ? l.petal : l.accent;
                    }
                    if (ground == Ground.DarkGrass && (h & 7) == 2 && v >= -1 && v < 2 && u >= -3 && u < 3)
                        return v == -1 ? Under(l) : Over(l);
                    return b;
                }
                case Ground.Path:
                case Ground.Sand:
                {
                    if (ground == Ground.Sand && detail && (h & 3) == 0)
                    {
                        int curve = Mathf.Abs(u) / 3;
                        if (u >= -5 && u <= 6 && v == curve) return Under(l);
                        if (u >= -3 && u <= 4 && v == curve + 1) return Over(l);
                    }
                    // Small paired pebbles, a worn depression and a lit grain, with no long line at every cell.
                    if (detail && u >= -1 && u <= 1 && v == 0) return Under(l);
                    if (detail && u >= 0 && u <= 1 && v == 1) return (h & 3) == 0 ? R(r, 2) : Over(l);
                    if (detail && u == 4 && v == 2) return Under(l);
                    if ((h & 15) == 6 && u >= -4 && u <= -2 && v == -2) return Under(l);
                    return b;
                }
                case Ground.Plaza:
                case Ground.Flagstone: return PavingPattern(l, x, y);
                case Ground.Wood: return BridgePattern(l, x, y);
                case Ground.Water:
                case Ground.DeepWater:
                {
                    int row = MathX.FloorDiv(y, 13), shift = (int)(Hs(row, 0, 8731) % 17);
                    int col = MathX.FloorDiv(x + shift, 27);
                    uint wave = Hs(col, row, 8732);
                    int a = Mod(x + shift, 27) - 4 - (int)((wave >> 8) % 9);
                    int z = Mod(y, 13) - 3 - (int)((wave >> 16) % 5);
                    int length = 4 + (int)((wave >> 12) % 6);
                    if ((wave & 3) != 3 && a >= 0 && a < length)
                    {
                        int step = a == 0 || a == length - 1 ? 1 : 0;
                        if (z == step) return Over(l);
                        if (z == step - 1 && a > 1 && a < length - 2) return Under(l);
                        if ((wave & 7) == 1 && z == 3 && a >= 2 && a < 5) return R(r, 2);
                    }
                    return b;
                }
                case Ground.Snow:
                {
                    if (detail && u >= -5 && u < 6 && v == 1 + Mathf.Abs(u) / 4) return Under(l);
                    if (detail && u >= -3 && u < 4 && v == 2 + Mathf.Abs(u) / 4) return Over(l);
                    if ((h & 15) == 7 && u >= 0 && u <= 1 && v == -1) return R(r, 2);
                    return b;
                }
                case Ground.Ice:
                case Ground.Crystal:
                case Ground.Mirror:
                case Ground.Ash:
                case Ground.Redrock:
                {
                    // Isolated fissures with a short branch, scattered chips and mineral flecks. The geology
                    // has no repeated rectangular seam enclosing every tile or large polygon reflection.
                    int cellX = MathX.FloorDiv(x, 31), cellY = MathX.FloorDiv(y, 29);
                    uint rock = Hs(cellX, cellY, 8741 + (int)ground);
                    int a = Mod(x, 31) - 8 - (int)((rock >> 8) % 12);
                    int z = Mod(y, 29) - 7 - (int)((rock >> 16) % 8);
                    int bend = z < 3 ? 0 : z < 6 ? 1 : -1;
                    if ((rock & 3) == 0 && z >= 0 && z < 9)
                    {
                        if (a == bend) return R(r, 0);
                        if (a == bend + 1 && z < 4) return Over(l);
                        if (z == 4 && a > 0 && a < 4) return Under(l);
                    }
                    if (detail && v == 0 && u >= -2 && u <= 1) return Under(l);
                    if (detail && v == 1 && u >= -1 && u <= 1) return Over(l);
                    if (ground == Ground.Crystal && (h & 31) == 3 && Mathf.Abs(u) + Mathf.Abs(v) <= 1)
                        return u == 0 && v == 0 ? R(r, 3) : R(r, 2);
                    if (ground == Ground.Mirror && (h & 15) == 2 && v == 2 && u >= -2 && u <= 2) return R(r, 2);
                    return b;
                }
                case Ground.Swamp:
                case Ground.Toxic:
                {
                    if ((h & 3) == 0 && Mathf.Abs(u) + 2 * Mathf.Abs(v) < 6)
                        return v == 1 ? Over(l) : Under(l);
                    if (detail && v == 0 && u >= -2 && u <= 2) return Over(l);
                    if (ground == Ground.Swamp && detail && v >= 1 && v <= 3 && (u == -1 || u == 1 && v < 3)) return R(r, 2);
                    if (ground == Ground.Toxic && (h & 15) == 3 && Mathf.Abs(u) + Mathf.Abs(v) == 1) return R(r, 3);
                    return b;
                }
                case Ground.Lava:
                {
                    int a = MathX.FloorDiv(x, 2) * 2, z = MathX.FloorDiv(y, 2) * 2;
                    float flow = Noise.Perlin(a, z * 1.4f, 8751, .075f);
                    if (flow > .62f) return R(r, 0);
                    if (flow > .54f) return R(r, 1);
                    if (flow < .32f) return R(r, 3);
                    return R(r, 2);
                }
                default: return b;
            }
        }

        static int PavingPattern(GroundLook l, int x, int y)
        {
            var r = l.ramp;
            int row = MathX.FloorDiv(y, 11), v = Mod(y, 11);
            int width = 16 + (int)(Hs(row, 0, 8761) % 3) * 2;
            int shift = (int)(Hs(row, 0, 8762) % 12);
            int col = MathX.FloorDiv(x + shift, width), u = Mod(x + shift, width);
            uint h = Hs(col, row, 8763);
            bool corner = u + v < 2 || width - 1 - u + v < 2 || u + 10 - v < 2 || width - 1 - u + 10 - v < 2;
            if (v == 0 || u == 0 || corner) return R(r, 0);
            if (v == 10 || u == 1) return R(r, 2);
            if (v == 1 || u == width - 1) return Under(l);
            int body = (h & 3) == 0 ? Under(l) : (h & 3) == 1 ? Over(l) : R(r, 1);
            if ((h & 7) == 2 && u > 3 && u < 8 && v >= 5 && v <= 7 && u + v < 13) body = Over(l);
            if (h % 13 == 2 && v >= 3 && v <= 6 && u == 6 + v / 2) return R(r, 0);
            if (h % 19 == 7 && v < 4 && u > width - 5) return Under(Look(Ground.DarkGrass));
            return body;
        }

        static int BridgePattern(GroundLook l, int x, int y)
        {
            var r = l.ramp;
            bool horizontal = HorizontalBridge(x, y);
            int along = horizontal ? x : y, across = horizontal ? y : x;
            int board = MathX.FloorDiv(along, 6), u = Mod(along, 6);
            int span = MathX.FloorDiv(across, 23), v = Mod(across, 23);
            uint h = Hs(board, span, 8771);
            if (u == 0) return R(r, 0);
            if (u == 1) return R(r, 2);
            if (v == 0 && (h & 3) == 0) return R(r, 0);
            if (u == 3 && v > 3 && v < 10 || u == 4 && v > 12 && v < 19) return Under(l);
            if ((h & 3) == 1 && u >= 2 && u <= 4 && v == 12) return R(r, 0);
            if ((h & 3) == 1 && u == 3 && (v == 11 || v == 13)) return Over(l);
            return (h & 3) == 0 ? Under(l) : (h & 3) == 1 ? Over(l) : R(r, 1);
        }
        static int RegionalWall(CliffLook cl, int x, int y)
        {
            var r = cl.ramp;
            if (cl.wall == WallPattern.Earth)
            {
                // Soil with embedded, bevelled stones of different shapes; courses do not make a brick grid.
                int row = y / 7, shift = (int)(Hs(row, 0, 8300) % 8), col = MathX.FloorDiv(x + shift, 11);
                int u = Mod(x + shift, 11), v = y % 7; uint h = Hs(col, row, 8301);
                int color = R(r, 1);
                int width = 4 + (int)((h >> 8) % 4), height = 3 + (int)((h >> 12) % 3);
                int a = u - 2, b = v - 1;
                bool stone = a >= 0 && a < width && b >= 0 && b < height && a + b > 0 && a + b < width + height - 3;
                if (stone) color = b == height - 1 || a == 0 ? R(r, 3) : b == 0 || a == width - 1 ? R(r, 0) : R(r, 2);
                if ((h & 3) == 1 && u == 0 && v >= 2 && v <= 4) color = R(r, 0);
                return color;
            }
            if (cl.wall == WallPattern.Facets)
            {
                int col = MathX.FloorDiv(x, 12), u = Mod(x, 12); uint h = Hs(col, 0, 8291);
                int cut = 3 + (int)(h % 5), bend = Mathf.Min(3, y / 9);
                if (u == 0) return R(r, 0);
                if (u == 1 && y < 18) return R(r, 3);
                int color = u < cut + bend ? R(r, 2) : R(r, 1);
                int fracture = 8 + (int)((h >> 8) % 11);
                if (y == fracture && u > 3 && u < 10) color = R(r, 0);
                if (cl.accent >= 0 && (h & 7) == 2 && y == fracture - 1 && u > 4 && u < 8) color = cl.accent;
                return color;
            }
            if (cl.wall == WallPattern.Roots)
            {
                int col = MathX.FloorDiv(x, 9), u = Mod(x, 9); uint h = Hs(col, 0, 8295);
                int color = u < 3 ? R(r, 2) : R(r, 1);
                if (u == 8) color = R(r, 0);
                int root = 2 + (int)(h % 4) + y / 8;
                if ((h & 3) != 3 && (u == root || u == root + 1 && y < 6) && y < 15) color = R(r, 3);
                if (cl.accent >= 0 && y < 4 + h % 4 && u < 4) color = cl.accent;
                return color;
            }
            return -1;
        }
    }
}
