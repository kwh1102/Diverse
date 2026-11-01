using System.Diagnostics;
using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        static Operation activeOperation;

        static void ReleaseScratchReferences()
        {
            g = null; flatGround = null; th = null; neighbours = null;
            lowerContour = null; upperContour = null; nextLower = null; nextUpper = null;
            surfaceMaterials = null; surfaceDistance = null; surfaceOther = null; surfaceDirection = null; bridgeAxes = null;
        }

        /// <summary>
        /// Main-thread, time-sliced version of the exact same rasterizer. Unity textures are created only when complete.
        /// No worker touches Unity objects; captures can interleave safely using independent scratch arrays.
        /// </summary>
        public sealed class Operation
        {
            readonly Ground[] savedGround, savedFlat;
            readonly byte[] savedThemes, savedNeighbours, savedLower, savedUpper;
            readonly byte[] savedMaterials, savedDistance, savedOther, savedDirection, savedBridgeAxes;
            readonly int savedSurfaceWidth, savedSurfaceX, savedSurfaceY, savedPadding;
            readonly int[] savedNextLower, savedNextUpper;
            readonly int savedN, savedOx, savedOy, savedWidth, savedX, savedY, savedRise, savedSkew;
            readonly bool savedRaised;
            readonly Color32[] pixels;
            readonly Vector2Int origin;
            readonly int width;
            int row;
            public bool Complete { get; private set; }
            public Texture2D Texture { get; private set; }
            public double MaxSliceMs { get; private set; }

            public Operation(WorldStreamer world, Vector2Int chunk)
            {
                if (activeOperation != null) throw new System.InvalidOperationException("Only one streaming bake may run at a time.");
                origin = chunk * WorldGen.Chunk; width = WorldGen.Chunk * T;
                PrepareRegion(world, origin, WorldGen.Chunk);
                savedGround = g; savedFlat = flatGround; savedThemes = th; savedNeighbours = neighbours;
                savedLower = lowerContour; savedUpper = upperContour; savedNextLower = nextLower; savedNextUpper = nextUpper;
                savedN = n; savedOx = ox; savedOy = oy; savedWidth = contourWidth; savedX = contourX; savedY = contourY;
                savedRise = rise; savedSkew = skew; savedRaised = hasRaised;
                savedMaterials = surfaceMaterials; savedDistance = surfaceDistance; savedOther = surfaceOther;
                savedDirection = surfaceDirection; savedBridgeAxes = bridgeAxes;
                savedSurfaceWidth = surfaceWidth; savedSurfaceX = surfaceX; savedSurfaceY = surfaceY; savedPadding = surfacePadding;
                pixels = new Color32[width * width]; activeOperation = this;
            }

            void Restore()
            {
                g = savedGround; flatGround = savedFlat; th = savedThemes; neighbours = savedNeighbours;
                lowerContour = savedLower; upperContour = savedUpper; nextLower = savedNextLower; nextUpper = savedNextUpper;
                n = savedN; ox = savedOx; oy = savedOy; contourWidth = savedWidth; contourX = savedX; contourY = savedY;
                rise = savedRise; skew = savedSkew; hasRaised = savedRaised;
                surfaceMaterials = savedMaterials; surfaceDistance = savedDistance; surfaceOther = savedOther;
                surfaceDirection = savedDirection; bridgeAxes = savedBridgeAxes;
                surfaceWidth = savedSurfaceWidth; surfaceX = savedSurfaceX; surfaceY = savedSurfaceY; surfacePadding = savedPadding;
            }

            public bool Step(float budgetMs)
            {
                if (Complete) return true;
                if (activeOperation != this) throw new System.InvalidOperationException("Cancelled bake operation.");
                Restore(); long start = Stopwatch.GetTimestamp();
                double ticks = Mathf.Max(.1f, budgetMs) * Stopwatch.Frequency / 1000.0;
                do
                {
                    int until = Mathf.Min(width, row + 4);
                    for (; row < until; row++) for (int x = 0; x < width; x++)
                        pixels[row * width + x] = pal[Mathf.Clamp(ContourDot(origin.x * T + x, origin.y * T + row), 0, pal.Length - 1)];
                } while (row < width && Stopwatch.GetTimestamp() - start < ticks);
                MaxSliceMs = System.Math.Max(MaxSliceMs, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                if (row < width) return false;
                Texture = new Texture2D(width, width, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                Texture.SetPixels32(pixels); Texture.Apply(false, false);
                Complete = true; activeOperation = null; return true;
            }

            public void Cancel() { if (activeOperation == this) activeOperation = null; }
        }
    }
}
