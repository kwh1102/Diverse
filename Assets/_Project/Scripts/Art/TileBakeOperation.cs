using System.Diagnostics;
using UnityEngine;

namespace Diverse
{
    public static partial class TileBaker
    {
        static Operation activeOperation;

        static void ReleaseScratchReferences()
        {
            g = null; flatGround = null; th = null; neighbours = null; habitatCover = null;
            lowerContour = null; upperContour = null; nextLower = null; nextUpper = null;
            surfaceMaterials = null; surfaceDistance = null; surfaceOther = null; surfaceDirection = null; bridgeAxes = null;
        }

        /// <summary>
        /// Main-thread, time-sliced version of the exact same rasterizer. Unity textures are created only when complete.
        /// No worker touches Unity objects; captures can interleave safely using independent scratch arrays.
        /// </summary>
        public sealed class Operation
        {
            Ground[] savedGround, savedFlat;
            byte[] savedThemes, savedNeighbours, savedLower, savedUpper;
            byte[] savedHabitat;
            byte[] savedMaterials, savedDistance, savedOther, savedDirection, savedBridgeAxes;
            int savedSurfaceWidth, savedSurfaceX, savedSurfaceY, savedPadding;
            int[] savedNextLower, savedNextUpper;
            int savedN, savedOx, savedOy, savedWidth, savedX, savedY, savedRise, savedSkew;
            bool savedRaised;
            readonly Color32[] pixels;
            readonly Vector2Int origin;
            readonly int width;
            int pixel;
            bool captured, prepared;
            System.Collections.Generic.IEnumerator<int> preparation;
            public bool Complete { get; private set; }
            public Texture2D Texture { get; private set; }
            public double MaxSliceMs { get; private set; }
            public double PreparationMs { get; private set; }

            public Operation(WorldStreamer world, Vector2Int chunk)
            {
                if (activeOperation != null) throw new System.InvalidOperationException("Only one streaming bake may run at a time.");
                origin = chunk * WorldGen.Chunk; width = WorldGen.Chunk * T;
                preparation = PrepareRegionSteps(world, origin, WorldGen.Chunk).GetEnumerator();
                pixels = new Color32[width * width]; activeOperation = this;
            }

            void Capture()
            {
                savedGround = g; savedFlat = flatGround; savedThemes = th; savedNeighbours = neighbours;
                savedHabitat = habitatCover;
                savedLower = lowerContour; savedUpper = upperContour; savedNextLower = nextLower; savedNextUpper = nextUpper;
                savedN = n; savedOx = ox; savedOy = oy; savedWidth = contourWidth; savedX = contourX; savedY = contourY;
                savedRise = rise; savedSkew = skew; savedRaised = hasRaised;
                savedMaterials = surfaceMaterials; savedDistance = surfaceDistance; savedOther = surfaceOther;
                savedDirection = surfaceDirection; savedBridgeAxes = bridgeAxes;
                savedSurfaceWidth = surfaceWidth; savedSurfaceX = surfaceX; savedSurfaceY = surfaceY; savedPadding = surfacePadding;
                captured = true;
            }

            void Restore()
            {
                g = savedGround; flatGround = savedFlat; th = savedThemes; neighbours = savedNeighbours;
                habitatCover = savedHabitat;
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
                // Reuse the previous completed bake's scratch buffers. Only an interleaved synchronous capture
                // needs separate buffers; discarding them for every streamed chunk generated megabytes of garbage.
                if (captured) Restore();
                long start = Stopwatch.GetTimestamp();
                double ticks = Mathf.Max(.1f, budgetMs) * Stopwatch.Frequency / 1000.0;
                do
                {
                    if (!prepared)
                    {
                        long before = Stopwatch.GetTimestamp();
                        if (!preparation.MoveNext()) { prepared = true; preparation.Dispose(); preparation = null; }
                        PreparationMs += (Stopwatch.GetTimestamp()-before)*1000.0/Stopwatch.Frequency;
                    }
                    else
                    {
                        int until = Mathf.Min(pixels.Length, pixel + 256);
                        for (; pixel < until; pixel++)
                            pixels[pixel] = pal[Mathf.Clamp(ContourDot(origin.x*T + pixel%width, origin.y*T + pixel/width), 0, pal.Length-1)];
                    }
                } while (pixel < pixels.Length && Stopwatch.GetTimestamp() - start < ticks);
                Capture();
                MaxSliceMs = System.Math.Max(MaxSliceMs, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                if (pixel < pixels.Length) return false;
                Texture = new Texture2D(width, width, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                Texture.SetPixels32(pixels); Texture.Apply(false, false);
                Complete = true; activeOperation = null; return true;
            }

            public void Cancel()
            {
                preparation?.Dispose(); preparation = null;
                if (activeOperation == this) activeOperation = null;
            }
        }
    }
}
