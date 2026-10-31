using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        // A continuous footprint for each tier, cached in world-pixel coordinates. Adjacent chunks see identical
        // coverage, including at negative coordinates; the expanded border contains the whole projected volume.
        static byte[] lowerContour, upperContour;
        static int contourWidth, contourX, contourY, rise, skew;
        static bool hasRaised;

        static void PrepareContours()
        {
            contourWidth = n * T; contourX = ox * T; contourY = oy * T;
            rise = Mathf.Clamp(S.cliffRise, 16, 32); skew = Mathf.Clamp(S.cliffSkew, 0, 8);
            int length = contourWidth * contourWidth;
            if (lowerContour == null || lowerContour.Length != length)
            { lowerContour = new byte[length]; upperContour = new byte[length]; }
            float radius = Mathf.Clamp(S.cornerRadius, 1, T / 2);
            hasRaised = false;
            for (int y = 0; y < contourWidth; y++) for (int x = 0; x < contourWidth; x++)
            {
                float fx = (contourX + x + .5f) / T - .5f, fy = (contourY + y + .5f) / T - .5f;
                int tx = Mathf.FloorToInt(fx), ty = Mathf.FloorToInt(fy);
                float ax = Mathf.Clamp01((fx - tx - .5f) * T / (2 * radius) + .5f);
                float ay = Mathf.Clamp01((fy - ty - .5f) * T / (2 * radius) + .5f);
                int a = WorldGen.Level(G(tx, ty)), b = WorldGen.Level(G(tx + 1, ty));
                int c = WorldGen.Level(G(tx, ty + 1)), d = WorldGen.Level(G(tx + 1, ty + 1));
                int index = y * contourWidth + x;
                lowerContour[index] = Coverage(1, a, b, c, d, ax, ay);
                upperContour[index] = Coverage(2, a, b, c, d, ax, ay);
                hasRaised |= lowerContour[index] >= 128;
            }
        }

        static byte Coverage(int level, int a, int b, int c, int d, float x, float y) =>
            (byte)Mathf.RoundToInt(255 * Mathf.Lerp(Mathf.Lerp(a >= level ? 1 : 0, b >= level ? 1 : 0, x),
                Mathf.Lerp(c >= level ? 1 : 0, d >= level ? 1 : 0, x), y));

        static int Contour(int level, int x, int y)
        {
            int ix = x - contourX, iy = y - contourY;
            if ((uint)ix >= contourWidth || (uint)iy >= contourWidth) return 0;
            return (level == 2 ? upperContour : lowerContour)[iy * contourWidth + ix];
        }

        static int ContourDot(int wx, int wy)
        {
            if (!hasRaised) return Flat(wx, wy);
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

        static int ContourTop(int tier, int gx, int gy)
        {
            int tx = MathX.FloorDiv(gx, T), ty = MathX.FloorDiv(gy, T);
            var material = ThemeGround(tx, ty); var look = Look(material); var cliff = Cliff(tx, ty);
            bool grass = cliff.lip < 0;
            int lip = grass ? R(look.ramp, look.ramp.Length - 1) : cliff.lip;
            bool south = Contour(tier, gx, gy - 1) < 128;
            bool west = Contour(tier, gx - 1, gy) < 128, east = Contour(tier, gx + 1, gy) < 128;
            bool north = Contour(tier, gx, gy + 1) < 128;
            if (south) return lip;
            if (west || east || north) return S.outline;
            // A continuous, uneven two/three-pixel cap joins top and face instead of independent tile rims.
            int fringe = 2 + (int)(Hs(MathX.FloorDiv(gx, 3), MathX.FloorDiv(gy, 3), 7813) % 2);
            if (Contour(tier, gx, gy - fringe) < 128)
                return grass && Hs(gx / 2, gy / 2, 7814) % 4 == 0 ? R(look.ramp, 2) : lip;
            if (Contour(tier, gx - 2, gy) < 128 || Contour(tier, gx, gy + 2) < 128) return lip;
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
            if (depth % rise >= rise - 4) color = Darker(cliff.ramp, color);
            if (z <= 1) return S.outline;
            return color;
        }

        static int Lighter(int[] ramp, int color)
        {
            for (int i = 0; i < ramp.Length - 1; i++) if (ramp[i] == color) return ramp[i + 1];
            return color;
        }
    }
}
