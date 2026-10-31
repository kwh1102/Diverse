using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        static int RegionalPattern(GroundLook l, Ground ground, int x, int y)
        {
            var r = l.ramp; int b = R(r, 1);
            switch (l.pattern)
            {
                case TilePattern.Moss:
                {
                    int tuft = Tuft(x, y, 65, R(r, 2), R(r, 0), 8201);
                    if (tuft >= 0) return tuft;
                    int cx = MathX.FloorDiv(x, 15), cy = MathX.FloorDiv(y, 15); uint h = Hs(cx, cy, 8202);
                    int u = Mod(x, 15) - 3 - (int)((h >> 8) % 6), v = Mod(y, 15) - 3 - (int)((h >> 12) % 6);
                    if ((h & 3) == 0 && v >= 0 && v <= 4 && Mathf.Abs(u) <= 2 &&
                        (u == 0 || Mathf.Abs(u) == 1 + v % 2 && v % 2 == 0)) return R(r, 2);
                    if ((h & 15) == 5 && u >= 0 && u < 2 && v >= 0 && v < 2 && l.accent >= 0) return l.accent;
                    return Noise.Perlin(x, y, 8203, .038f) > .69f ? R(r, 0) : b;
                }
                case TilePattern.Snowdrifts:
                {
                    float p = Noise.Perlin(MathX.FloorDiv(x, 2) * 2, MathX.FloorDiv(y, 2) * 2, 8211, .038f);
                    if (p < .29f) return R(r, 0);
                    if (p > .67f) return R(r, 2);
                    int cx = MathX.FloorDiv(x, 25), cy = MathX.FloorDiv(y, 21);
                    int u = Mod(x, 25), v = Mod(y, 21); uint h = Hs(cx, cy, 8212);
                    if ((h & 7) == 0 && v == 3 + u / 9 && u > 6 && u < 14) return R(r, 0);
                    return b;
                }
                case TilePattern.CrystalBed:
                {
                    int cx = MathX.FloorDiv(x, 19), cy = MathX.FloorDiv(y, 19); uint h = Hs(cx, cy, 8221);
                    int u = Mod(x, 19) - 5 - (int)((h >> 8) % 8), v = Mod(y, 19) - 4 - (int)((h >> 12) % 8);
                    if ((h & 3) == 0 && v >= 0 && v <= 5 && Mathf.Abs(u) + Mathf.Abs(v - 2) / 2 <= 1)
                        return u < 0 ? R(r, 3) : l.accent;
                    if ((h & 7) == 5 && u >= 0 && u <= 3 && v == 2) return R(r, 0);
                    float face = Noise.Perlin(x, y, 8222, .048f);
                    return face < .24f ? R(r, 0) : face > .79f ? R(r, 2) : b;
                }
                case TilePattern.MirrorPaving:
                {
                    int row = MathX.FloorDiv(y, 23), v = Mod(y, 23), shift = (int)(Hs(row, 0, 8231) % 13);
                    int col = MathX.FloorDiv(x + shift, 27), u = Mod(x + shift, 27); uint h = Hs(col, row, 8232);
                    if (v == 1 && u > 2 && u < 24 || u == 25 - v / 5 && v > 4 && v < 20) return R(r, 0);
                    if ((h & 3) == 0 && v == 4 && u > 5 && u < 13) return R(r, 2);
                    if ((h & 15) == 7 && u == 9 && (v == 11 || v == 12)) return l.accent;
                    return (h & 7) == 1 && v > 10 && u < 7 + v / 3 ? R(r, 2) : b;
                }
                case TilePattern.Sandstone:
                {
                    int row = MathX.FloorDiv(y, 23), v = Mod(y, 23), shift = (int)(Hs(row, 0, 8241) % 11);
                    int col = MathX.FloorDiv(x + shift, 29), u = Mod(x + shift, 29); uint h = Hs(col, row, 8242);
                    if ((h & 3) == 0 && v == 4 + u / 12 && u > 4 && u < 23) return R(r, 0);
                    if ((h & 7) == 2 && u > 6 && u < 10 && v > 8 && v < 11) return R(r, 0);
                    if ((h & 7) == 5 && u > 3 + v / 3 && u < 24 && v > 12 && v < 17) return R(r, 2);
                    return b;
                }
                case TilePattern.Puddles:
                {
                    float p = Noise.Perlin(MathX.FloorDiv(x, 2) * 2, MathX.FloorDiv(y, 2) * 2, 8251, .041f);
                    bool liquid = WorldGen.IsLiquid(ground);
                    if (p > .69f) return R(r, 0);
                    if (p > .64f) return R(r, 2);
                    int tuft = liquid ? -1 : Tuft(x, y, 37, R(r, 2), R(r, 0), 8252);
                    if (tuft >= 0) return tuft;
                    uint h = Hs(MathX.FloorDiv(x, 21), MathX.FloorDiv(y, 21), 8253);
                    if ((h & 7) == 0 && Mod(y, 21) == 5 && Mod(x, 21) > 6 && Mod(x, 21) < 10) return liquid ? R(r, 3) : R(r, 0);
                    return b;
                }
            }
            if (ground == Ground.Sand && l.pattern == TilePattern.Ripples)
            {
                int row = MathX.FloorDiv(y, 12), v = Mod(y, 12), shift = (int)(Hs(row, 0, 8261) % 19);
                int col = MathX.FloorDiv(x + shift, 29), u = Mod(x + shift, 29); uint h = Hs(col, row, 8262);
                if ((h & 3) == 0 && v == 3 + u / 11 && u < 14) return R(r, 0);
                if ((h & 15) == 1 && u > 9 && u < 12 && v == 8) return R(r, 2);
                return b;
            }
            if (ground == Ground.Ice && l.pattern == TilePattern.Cracks)
            {
                int row = MathX.FloorDiv(y, 22), v = Mod(y, 22), shift = (int)(Hs(row, 0, 8271) % 11);
                int col = MathX.FloorDiv(x + shift, 25), u = Mod(x + shift, 25); uint h = Hs(col, row, 8272);
                int crack = 6 + (int)((h >> 8) % 7) + v / 4;
                if ((h & 3) != 3 && u == crack && v > 3 && v < 19) return R(r, 0);
                if ((h & 7) == 0 && v == 8 && u > crack && u < crack + 6) return R(r, 0);
                if (u == crack + 1 && v > 5 && v < 13) return R(r, 2);
                return (h & 7) == 2 && u < 5 + v / 4 ? R(r, 2) : b;
            }
            if (l.pattern == TilePattern.Slabs)
            {
                int row = MathX.FloorDiv(y, 15), v = Mod(y, 15), shift = (int)(Hs(row, 0, 8281) % 13);
                int col = MathX.FloorDiv(x + shift, 21), u = Mod(x + shift, 21); uint h = Hs(col, row, 8282);
                bool corner = u < 2 && v < 2 || u > 17 && v > 11;
                if (u == 20 || v == 14 || corner) return l.accent >= 0 && (h & 7) == 1 && u > 13 ? l.accent : R(r, 0);
                if ((u == 1 || v == 12) && !corner) return R(r, 2);
                if ((h & 15) == 3 && u == 8 + v / 3 && v > 4 && v < 10) return R(r, 0);
                return (h & 7) == 5 ? R(r, 2) : b;
            }
            return -1;
        }

        static int RegionalWall(CliffLook cl, int x, int y)
        {
            var r = cl.ramp;
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
