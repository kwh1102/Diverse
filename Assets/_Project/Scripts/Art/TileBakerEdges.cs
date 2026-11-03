using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        // These maps belong to the current bake, just like the contour buffers. Coordinates are world pixels,
        // so a shore or a grass fringe is identical on either side of a chunk boundary.
        static byte[] surfaceMaterials, surfaceDistance, surfaceOther, surfaceDirection, bridgeAxes;
        static int surfaceWidth, surfaceX, surfaceY, surfacePadding;
        static readonly int[] edgeX = { 0, -1, 0, 1 };
        static readonly int[] edgeY = { -1, 0, 1, 0 };

        static bool Grass(Ground m) => m is Ground.Grass or Ground.DarkGrass;
        static bool Paving(Ground m) => m is Ground.Plaza or Ground.Flagstone;
        static bool SameSurface(Ground a, Ground b) => a == b || WorldGen.IsWater(a) && WorldGen.IsWater(b) ||
            Paving(a) && Paving(b) || Grass(a) && Grass(b);

        static Ground MicroMaterial(int x, int y)
        {
            Ground material = MacroMat(x, y, out _);
            int tx = MathX.FloorDiv(x, T), ty = MathX.FloorDiv(y, T);
            if ((neighbours[Mathf.Clamp(ty - oy, 0, n - 1) * n + Mathf.Clamp(tx - ox, 0, n - 1)] & 4) != 0) return material;
            // Two- or three-pixel runs, at most one pixel either side of the original border. The large shape,
            // corners and navigation stay intact; only the silhouette of the material is finished by hand-sized bites.
            int dx = (int)(Hs(MathX.FloorDiv(y, 3), 0, 8611) % 3) - 1;
            int dy = (int)(Hs(MathX.FloorDiv(x, 4), 0, 8612) % 3) - 1;
            Ground moved = MacroMat(x + dx, y + dy, out _);
            return material == Ground.Wood || moved == Ground.Wood ? material : moved;
        }

        static System.Collections.Generic.IEnumerable<int> PrepareSurfaceSteps(WorldStreamer world)
        {
            surfacePadding = Mathf.Clamp(Mathf.Max(S.groundFringe, S.shoreWidth), 2, 8) + 2;
            surfaceWidth = (n - 2 * B) * T + 2 * surfacePadding;
            surfaceX = (ox + B) * T - surfacePadding; surfaceY = (oy + B) * T - surfacePadding;
            int count = surfaceWidth * surfaceWidth;
            if (surfaceMaterials == null || surfaceMaterials.Length != count)
            {
                surfaceMaterials = new byte[count]; surfaceDistance = new byte[count];
                surfaceOther = new byte[count]; surfaceDirection = new byte[count];
            }
            System.Array.Clear(surfaceDistance, 0, count);
            for (int y = 0; y < surfaceWidth; y++)
            {
                int ty = Mathf.Clamp(MathX.FloorDiv(surfaceY+y,T)-oy,0,n-1);
                for (int x = 0; x < surfaceWidth;)
                {
                    int tx = Mathf.Clamp(MathX.FloorDiv(surfaceX+x,T)-ox,0,n-1);
                    int end = Mathf.Min(surfaceWidth, (tx+ox+1)*T-surfaceX);
                    if ((neighbours[ty*n+tx]&4) != 0)
                        for (;x<end;x++) surfaceMaterials[y*surfaceWidth+x] = (byte)flatGround[ty*n+tx];
                    else
                        for (;x<end;x++) surfaceMaterials[y*surfaceWidth+x] = (byte)MicroMaterial(surfaceX+x,surfaceY+y);
                }
                yield return 0;
            }
            int reach = surfacePadding - 2;
            for (int y = surfacePadding; y < surfaceWidth - surfacePadding; y++)
            {
                for (int x = surfacePadding; x < surfaceWidth - surfacePadding; x++)
                {
                    int i = y * surfaceWidth + x;
                    Ground m = (Ground)surfaceMaterials[i];
                    int tx = MathX.FloorDiv(surfaceX + x, T), ty = MathX.FloorDiv(surfaceY + y, T);
                    if ((neighbours[(ty - oy) * n + tx - ox] & 4) != 0) continue;
                    bool found = false;
                    for (int d = 1; d <= reach && !found; d++) for (int dir = 0; dir < 4; dir++)
                    {
                        Ground other = (Ground)surfaceMaterials[i + (edgeY[dir] * surfaceWidth + edgeX[dir]) * d];
                        if (SameSurface(m, other)) continue;
                        surfaceDistance[i] = (byte)d; surfaceOther[i] = (byte)other; surfaceDirection[i] = (byte)dir;
                        found = true; break;
                    }
                }
                yield return 0;
            }
            if (bridgeAxes == null || bridgeAxes.Length != n * n) bridgeAxes = new byte[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                if (g[y * n + x] != Ground.Wood) continue;
                bridgeAxes[y * n + x] = (byte)(world.Gen.BridgeHorizontalAt(ox + x, oy + y) ? 0 : 1);
                }
                yield return 0;
            }
        }

        static Ground Mat(int x, int y, out bool rim)
        {
            int ix = x - surfaceX, iy = y - surfaceY; rim = false;
            if ((uint)ix < surfaceWidth && (uint)iy < surfaceWidth)
                return (Ground)surfaceMaterials[iy * surfaceWidth + ix];
            return MicroMaterial(x, y);
        }

        static bool HorizontalBridge(int x, int y)
        {
            int tx = Mathf.Clamp(MathX.FloorDiv(x, T) - ox, 0, n - 1);
            int ty = Mathf.Clamp(MathX.FloorDiv(y, T) - oy, 0, n - 1);
            return bridgeAxes[ty * n + tx] == 0;
        }

        static int SurfaceEdge(Ground material, GroundLook look, int x, int y, int color)
        {
            int i = (y - surfaceY) * surfaceWidth + x - surfaceX;
            int d = surfaceDistance[i];
            if (d == 0) return color;
            Ground other = (Ground)surfaceOther[i]; int dir = surfaceDirection[i];
            uint fringe = Hs(MathX.FloorDiv(dir % 2 == 0 ? x : y, 3), 0, 8621);
            var r = look.ramp;
            if (material == Ground.Wood)
            {
                bool side = HorizontalBridge(x, y) ? dir % 2 == 0 : dir % 2 == 1;
                if (!side || !Liquid(other)) return d == 1 ? R(r, 0) : color;
                int along = HorizontalBridge(x, y) ? x : y;
                // Raised side beams, riveted posts and a dark contact under the deck, inside its existing footprint.
                if (Mod(along, 16) < 3 && d <= 4) return d == 1 ? R(r, 0) : d == 4 ? R(r, 2) : R(r, 1);
                if (d == 1 || d == 3) return R(r, 0);
                if (d == 2) return R(r, 2);
                if (d == 4) return Shade(look);
                return color;
            }
            if (Liquid(material))
            {
                if (Liquid(other)) return color;
                if (other == Ground.Wood) return d <= 3 ? R(r, 0) : color;
                if (d > S.shoreWidth) return color;
                if (material == Ground.Lava)
                    return d == 1 ? R(r, 0) : d <= 2 + fringe % 2 ? R(r, 3) : color;
                if (material == Ground.Toxic)
                    return d == 1 ? R(r, 0) : d <= 3 && fringe % 3 == 0 ? R(r, 3) : Over(look);
                if (d == 1) return R(r, 0);
                if (d == 2 && fringe % 4 != 0) return R(r, 2);
                if (d == 3 && fringe % 5 == 1) return R(r, 3);
                // Short foam marks sit within a shallow shelf, rather than outlining the entire river in white.
                if (d <= 4 + fringe % 3) return Over(look);
                return color;
            }
            if (Liquid(other))
            {
                if (Paving(material)) return d == 1 ? R(r, 0) : d == 2 ? R(r, 2) : color;
                if (material is Ground.Snow or Ground.Ice) return d == 1 ? R(r, 0) : d < 4 ? R(r, 2) : color;
                var bank = Look(other).bank;
                if (dir == 0 && bank != null && bank.Length > 0 && d <= bank.Length)
                    return bank[bank.Length - d] >= 0 ? bank[bank.Length - d] : Under(look);
                if (d == 1) return Under(look);
                if (d == 2 && fringe % 3 != 0) return Over(look);
                return color;
            }
            if (d > S.groundFringe) return color;
            if (Grass(material))
            {
                if (d == 1) return fringe % 3 == 0 ? Shade(look) : R(r, 2);
                if (d == 2 && fringe % 3 == 0) return Under(look);
                if (d <= 4 && fringe % 5 == 1) return Over(look);
                return color;
            }
            if (Grass(other))
            {
                var grass = Look(other);
                if (d == 1 && fringe % 4 == 0) return Shade(grass);
                if (d == 1) return Under(look);
                if (d == 2 && fringe % 3 == 0) return Under(look);
            }
            if (Paving(material))
            {
                if (d == 1) return R(r, 0);
                if (d == 2 && dir is 1 or 2) return R(r, 2);
            }
            else if (d == 1 && fringe % 3 != 0) return Under(look);
            return color;
        }
    }
}
