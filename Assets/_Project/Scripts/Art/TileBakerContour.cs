using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        // A continuous footprint for each tier, cached in world-pixel coordinates. Adjacent chunks see identical
        // coverage, including at negative coordinates; the expanded border contains the whole projected volume.
        static byte[] lowerContour, upperContour;
        static int[] nextLower, nextUpper;
        static int contourWidth, contourX, contourY, rise, skew;
        static bool hasRaised;

        static System.Collections.Generic.IEnumerable<int> PrepareContourSteps()
        {
            contourWidth = n * T; contourX = ox * T; contourY = oy * T;
            rise = Mathf.Clamp(S.cliffRise, 16, 32); skew = Mathf.Clamp(S.cliffSkew, 0, 8);
            int length = contourWidth * contourWidth;
            if (lowerContour == null || lowerContour.Length != length)
            { lowerContour = new byte[length]; upperContour = new byte[length]; nextLower = new int[length]; nextUpper = new int[length]; }
            int radius = Mathf.Clamp(S.cornerRadius, 1, T / 2);
            hasRaised = false;
            System.Array.Clear(lowerContour, 0, length); System.Array.Clear(upperContour, 0, length);
            for (int ty = 0; ty < n; ty++)
            {
                for (int tx = 0; tx < n; tx++)
                {
                int level = WorldGen.Level(g[ty * n + tx]);
                if (level == 0) continue;
                hasRaised = true;
                for (int tier = 1; tier <= level; tier++)
                {
                    bool left = WorldGen.Level(G(ox + tx - 1, oy + ty)) < tier;
                    bool right = WorldGen.Level(G(ox + tx + 1, oy + ty)) < tier;
                    bool down = WorldGen.Level(G(ox + tx, oy + ty - 1)) < tier;
                    bool up = WorldGen.Level(G(ox + tx, oy + ty + 1)) < tier;
                    var into = tier == 1 ? lowerContour : upperContour;
                    for (int y = 0; y < T; y++) for (int x = 0; x < T; x++)
                    {
                        if (left && down && x + y < radius || right && down && T - 1 - x + y < radius ||
                            left && up && x + T - 1 - y < radius || right && up && 2 * T - 2 - x - y < radius) continue;
                        int wx = (ox + tx) * T + x, wy = (oy + ty) * T + y;
                        if (left && x == 0 && Hs(MathX.FloorDiv(wy, 3), 0, 8811) % 3 == 0 ||
                            right && x == T - 1 && Hs(MathX.FloorDiv(wy, 4), 0, 8812) % 3 == 0 ||
                            down && y == 0 && Hs(MathX.FloorDiv(wx, 3), 0, 8813) % 3 == 0 ||
                            up && y == T - 1 && Hs(MathX.FloorDiv(wx, 4), 0, 8814) % 3 == 0) continue;
                        into[(ty * T + y) * contourWidth + tx * T + x] = 255;
                    }
                }
                }
                yield return 0;
            }
            // For a straight RPG camera a column has no horizontal shear. A suffix lookup finds the first solid
            // sample on a view ray in O(1), replacing up to 64 contour probes for every output pixel.
            if (skew == 0 && hasRaised)
                for (int x = 0; x < contourWidth; x++)
                {
                    int a = int.MaxValue, b = int.MaxValue;
                    for (int y = contourWidth - 1; y >= 0; y--)
                    {
                        int i = y * contourWidth + x;
                        if (lowerContour[i] >= 128) a = y;
                        if (upperContour[i] >= 128) b = y;
                        nextLower[i] = a; nextUpper[i] = b;
                    }
                    if (x % 8 == 7) yield return 0;
                }
        }

        static int Contour(int level, int x, int y)
        {
            int ix = x - contourX, iy = y - contourY;
            if ((uint)ix >= contourWidth || (uint)iy >= contourWidth) return 0;
            return (level == 2 ? upperContour : lowerContour)[iy * contourWidth + ix];
        }

        static int ContourDot(int wx, int wy)
        {
            if (!hasRaised) return Flat(wx, wy);
            if (skew == 0) return ColumnDot(wx, wy);
            // Trace the short oblique extrusion from the camera side. The first volume hit is either a top or a
            // side face, so concave bends and stacked tiers occlude in the same order as the connected terrain.
            for (int z = 2 * rise; z > 0; z--)
            {
                int tier = z > rise ? 2 : 1;
                int gx = wx - (z * skew + rise / 2) / rise, gy = wy - z;
                if (Contour(tier, gx, gy) < 128) continue;
                int actual = Contour(2, gx, gy) >= 128 ? 2 : 1;
                if (z == actual * rise) return ContourTop(actual, gx, gy);
                return ContourWall(actual, gx, gy, z);
            }
            int color = Flat(wx, wy);
            // Contact shade follows the projected foot, including the curved sides.
            if (S.footShade > 0 && !WorldGen.IsLiquid(Mat(wx, wy, out _)) && Contour(1, wx, wy - S.footShade) >= 128)
                color = Shade(Look(Mat(wx, wy, out _)));
            return color;
        }

        static int ColumnDot(int wx, int wy)
        {
            int x = wx - contourX, y = wy - contourY;
            int a = nextLower[Mathf.Max(0, y - rise) * contourWidth + x];
            int b = nextUpper[Mathf.Max(0, y - 2 * rise) * contourWidth + x];
            int hit = Mathf.Min(a, b);
            if (hit < y)
            {
                int gy = hit + contourY, z = y - hit;
                int tier = upperContour[hit * contourWidth + x] >= 128 ? 2 : 1;
                return z == tier * rise ? ContourTop(tier, wx, gy) : ContourWall(tier, wx, gy, z);
            }
            int color = Flat(wx, wy);
            if (S.footShade > 0 && !Liquid(Mat(wx, wy, out _)) && Contour(1, wx, wy - S.footShade) >= 128)
                color = Shade(Look(Mat(wx, wy, out _)));
            return color;
        }

        static int ContourTop(int tier, int gx, int gy)
        {
            int tx = MathX.FloorDiv(gx, T), ty = MathX.FloorDiv(gy, T);
            var material = ThemeGround(tx, ty); var look = Look(material); var cliff = Cliff(tx, ty);
            bool grass = cliff.lip < 0;
            int lip = grass ? R(look.ramp, 2) : cliff.lip;
            bool south = Contour(tier, gx, gy - 1) < 128;
            bool west = Contour(tier, gx - 1, gy) < 128, east = Contour(tier, gx + 1, gy) < 128;
            bool north = Contour(tier, gx, gy + 1) < 128;
            uint edge = Hs(MathX.FloorDiv(gx, 3), MathX.FloorDiv(gy, 3), 7813);
            if (south) return grass && edge % 4 == 0 ? Over(look) : lip;
            if (west || north) return edge % 3 == 0 ? Over(look) : lip;
            if (east) return Shade(look);
            // A continuous, uneven two/three-pixel cap joins top and face instead of independent tile rims.
            int fringe = 1 + (int)(edge % 3);
            if (Contour(tier, gx, gy - fringe) < 128)
                return grass && edge % 3 != 0 ? Over(look) : lip;
            if (Contour(tier, gx - 2, gy) < 128 || Contour(tier, gx, gy + 2) < 128) return Over(look);
            return Pattern(look, material, gx, gy);
        }

        static int ContourWall(int tier, int gx, int gy, int z)
        {
            int tx = MathX.FloorDiv(gx, T), ty = MathX.FloorDiv(gy, T);
            var cliff = Cliff(tx, ty); int depth = tier * rise - z;
            int color = Wall(cliff, gx, depth);
            // Surface direction comes from the continuous footprint: left-facing returns catch more light,
            // right-facing returns recede. A corner therefore reads as one solid rock mass from above.
            int dx = Contour(tier, gx + 2, gy) - Contour(tier, gx - 2, gy);
            int dy = Contour(tier, gx, gy + 2) - Contour(tier, gx, gy - 2);
            if (dx > Mathf.Abs(dy) / 2 + 16) color = Lighter(cliff.ramp, color);
            else if (dx < -Mathf.Abs(dy) / 2 - 16) color = Darker(cliff.ramp, color);
            if (cliff.lip < 0 && depth < 1 + (Hs(gx / 2, gy / 2, 7817) % 3))
                return Shade(Look(ThemeGround(tx, ty)));
            if (depth % rise >= rise - 2) color = Darker(cliff.ramp, color);
            if (z <= 1) return R(cliff.ramp, 0);
            return color;
        }

        static int Lighter(int[] ramp, int color)
        {
            for (int i = 0; i < ramp.Length - 1; i++) if (ramp[i] == color) return ramp[i + 1];
            return color;
        }
    }
}
