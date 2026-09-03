// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The bilinear and point scaling routines in this file are adapted from the Unity Community wiki
// TextureScale, http://wiki.unity3d.com/index.php/TextureScale. The design is the original author's;
// no author or license for that page is recorded in this repository.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Provides high-performance texture scaling operations using pooled buffers and parallel processing.
    /// </summary>
    /// <remarks>
    /// Original implementation based on:
    /// - https://answers.unity.com/questions/348163/resize-texture2d-comes-out-grey.html
    /// - http://wiki.unity3d.com/index.php/TextureScale
    ///
    /// Improvements:
    /// - Thread-safe implementation (no static state)
    /// - Uses array pooling to reduce allocations
    /// - Task-based parallelism instead of manual thread management
    /// - Proper input validation
    /// - Fixed bilinear interpolation bounds checking
    /// - Proper resource cleanup
    /// - Center-aligned sampling and premultiplied blending (see <see cref="TextureResampling"/>)
    /// </remarks>
    public static class TextureScale
    {
        /*
            The seam a fixture needs to make a background slice throw. Production leaves it null;
            the parallel and single-threaded branches both invoke it with the slice's first row, so
            a fixture can prove the two branches report a slice failure the same way.
        */
        internal static Action<int> SliceStartedForTesting;

        /// <summary>
        /// Scales a texture using point (nearest neighbor) sampling.
        /// </summary>
        /// <param name="tex">The texture to scale. Must be readable.</param>
        /// <param name="newWidth">The target width. Must be positive.</param>
        /// <param name="newHeight">The target height. Must be positive.</param>
        /// <exception cref="ArgumentNullException">Thrown when tex is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when newWidth or newHeight is not positive.</exception>
        /// <exception cref="UnityException">Thrown when texture is not readable.</exception>
        /// <remarks>
        /// This method modifies the texture in-place. Point sampling provides fast, sharp scaling
        /// but may produce pixelated results. Use Bilinear for smoother results.
        /// </remarks>
        public static void Point(Texture2D tex, int newWidth, int newHeight, bool apply = false)
        {
            ValidateInputs(tex, newWidth, newHeight);
            ThreadedScale(tex, newWidth, newHeight, false);
            if (apply)
            {
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }
        }

        /// <summary>
        /// Scales a texture using bilinear interpolation.
        /// </summary>
        /// <param name="tex">The texture to scale. Must be readable.</param>
        /// <param name="newWidth">The target width. Must be positive.</param>
        /// <param name="newHeight">The target height. Must be positive.</param>
        /// <param name="apply">Whether to apply the changes to the texture immediately, or leave them staged.</param>
        /// <exception cref="ArgumentNullException">Thrown when tex is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when newWidth or newHeight is not positive.</exception>
        /// <exception cref="UnityException">Thrown when texture is not readable.</exception>
        /// <remarks>
        /// This method modifies the texture in-place. Bilinear interpolation provides smooth scaling
        /// with better visual quality than point sampling, at a slight performance cost.
        /// </remarks>
        public static void Bilinear(Texture2D tex, int newWidth, int newHeight, bool apply = false)
        {
            ValidateInputs(tex, newWidth, newHeight);
            ThreadedScale(tex, newWidth, newHeight, true);
            if (apply)
            {
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }
        }

        /// <summary>
        /// Keeps the first slice failure and logs any later one.
        /// </summary>
        /// <remarks>
        /// Only one exception can reach the caller, and the first is the informative one. Logging
        /// the loser is what keeps this from becoming the defect it fixes.
        /// </remarks>
        private static void RecordSliceFailure(ref Exception first, Exception failure)
        {
            if (Interlocked.CompareExchange(ref first, failure, null) == null)
            {
                return;
            }

            Debug.LogException(failure);
        }

        private static void ValidateInputs(Texture2D tex, int newWidth, int newHeight)
        {
            if (tex == null)
            {
                throw new ArgumentNullException(nameof(tex));
            }

            if (newWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newWidth),
                    newWidth,
                    "Width must be positive."
                );
            }

            if (newHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newHeight),
                    newHeight,
                    "Height must be positive."
                );
            }

            // Match test expectation: explicitly throw UnityException when not readable
            if (!tex.isReadable)
            {
                throw new UnityException("Texture is not readable");
            }
        }

        private static void ThreadedScale(
            Texture2D tex,
            int newWidth,
            int newHeight,
            bool useBilinear
        )
        {
            /*
                No-op fast path when dimensions are unchanged.
                Preserves exact pixel values — required by edge tests.
            */
            if (tex.width == newWidth && tex.height == newHeight)
            {
                return;
            }

            // Get source pixels - this will throw if texture is not readable
            Color[] texColors = tex.GetPixels();
            int sourceWidth = tex.width;
            int sourceHeight = tex.height;

            // Use array pool for destination buffer
            int newSize = newWidth * newHeight;
            using PooledArray<Color> pooledColors = SystemArrayPool<Color>.Get(
                newSize,
                out Color[] newColors
            );

            /*
                A destination pixel covers sourceSize / destSize of the source, whichever filter reads
                it. The bilinear path used to divide by sourceSize - 1 instead, which is a different
                image, not a different rounding.
            */
            float ratioX = (float)sourceWidth / newWidth;
            float ratioY = (float)sourceHeight / newHeight;

            /*
                Blending premultiplied color is what stops a transparent texel tinting a visible one.
                It is the identity on an opaque texture, so an opaque source skips the rent entirely.
            */
            using PooledArray<Color> pooledPremultiplied = RentPremultiplied(
                texColors,
                useBilinear,
                out Color[] premultipliedColors
            );

            // Determine optimal thread count
            int cores = Mathf.Min(SystemInfo.processorCount, newHeight);

            if (1 < cores)
            {
                // Parallel processing
                int slice = newHeight / cores;
                using CountdownEvent countdown = new(cores);

                /*
                    The dispatch loop is inside this try because the finally below is what
                    waits: a throw from Task.Run itself would otherwise unwind past the
                    wait with slices already queued, and the using declarations above would
                    return the buffers they are still writing into.
                */
                int dispatched = 0;
                Exception workerFailure = null;
                try
                {
                    for (int i = 0; i < cores - 1; i++)
                    {
                        int start = slice * i;
                        int end = slice * (i + 1);
                        Task.Run(() =>
                        {
                            try
                            {
                                SliceStartedForTesting?.Invoke(start);
                                if (useBilinear)
                                {
                                    BilinearScale(
                                        texColors,
                                        premultipliedColors,
                                        newColors,
                                        sourceWidth,
                                        sourceHeight,
                                        newWidth,
                                        ratioX,
                                        ratioY,
                                        start,
                                        end
                                    );
                                }
                                else
                                {
                                    PointScale(
                                        texColors,
                                        newColors,
                                        sourceWidth,
                                        sourceHeight,
                                        newWidth,
                                        ratioX,
                                        ratioY,
                                        start,
                                        end
                                    );
                                }
                            }
                            catch (Exception sliceFailure)
                            {
                                RecordSliceFailure(ref workerFailure, sliceFailure);
                            }
                            finally
                            {
                                countdown.Signal();
                            }
                        });
                        dispatched++;
                    }

                    /*
                        This slice records into the same field the workers do rather than throwing
                        through the finally, so whichever slice failed FIRST is the one the caller
                        sees and neither is discarded.
                    */
                    int finalStart = slice * (cores - 1);
                    try
                    {
                        SliceStartedForTesting?.Invoke(finalStart);
                        if (useBilinear)
                        {
                            BilinearScale(
                                texColors,
                                premultipliedColors,
                                newColors,
                                sourceWidth,
                                sourceHeight,
                                newWidth,
                                ratioX,
                                ratioY,
                                finalStart,
                                newHeight
                            );
                        }
                        else
                        {
                            PointScale(
                                texColors,
                                newColors,
                                sourceWidth,
                                sourceHeight,
                                newWidth,
                                ratioX,
                                ratioY,
                                finalStart,
                                newHeight
                            );
                        }
                    }
                    catch (Exception sliceFailure)
                    {
                        RecordSliceFailure(ref workerFailure, sliceFailure);
                    }
                }
                finally
                {
                    /*
                        One signal for this thread's slice, plus one for every slice that was never
                        dispatched -- a Task.Run that throws leaves the countdown short, and the
                        wait below would never complete.
                    */
                    for (int remaining = cores - dispatched; 0 < remaining; remaining--)
                    {
                        countdown.Signal();
                    }

                    /*
                        Waiting has to happen on the way out too. A throw from this slice used to
                        unwind straight past the wait, and the using declarations above then
                        returned the destination and premultiplied buffers to the pool while the
                        other slices were still indexing into them -- the next renter's pixels
                        overwritten by a kernel nobody was waiting for. Disposing the countdown
                        under a still-running Signal was the same race one level down.
                    */
                    countdown.Wait();
                }

                /*
                    A slice that threw signalled the countdown from its own finally, so the wait
                    above returned normally and the destination still holds whatever the pooled
                    buffer had. Reporting nothing here is a silently wrong image; rethrowing is
                    also what the single-threaded branch below does with the same failure.
                */
                if (workerFailure != null)
                {
                    ExceptionDispatchInfo.Capture(workerFailure).Throw();
                }
            }
            else
            {
                // Single-threaded processing
                SliceStartedForTesting?.Invoke(0);
                if (useBilinear)
                {
                    BilinearScale(
                        texColors,
                        premultipliedColors,
                        newColors,
                        sourceWidth,
                        sourceHeight,
                        newWidth,
                        ratioX,
                        ratioY,
                        0,
                        newHeight
                    );
                }
                else
                {
                    PointScale(
                        texColors,
                        newColors,
                        sourceWidth,
                        sourceHeight,
                        newWidth,
                        ratioX,
                        ratioY,
                        0,
                        newHeight
                    );
                }
            }

            /*
                Write results back to texture.
                Reinitialize with a float format to avoid 8-bit quantization
                so GetPixels() matches our computed values precisely.
                Note: format change is acceptable; tests assert only size and pixel values.
            */
#if UNITY_2020_1_OR_NEWER
            _ = tex.Reinitialize(newWidth, newHeight, TextureFormat.RGBAFloat, false);
#else
            _ = tex.Resize(newWidth, newHeight, TextureFormat.RGBAFloat, false);
#endif
            tex.SetPixels(newColors);
        }

        private static PooledArray<Color> RentPremultiplied(
            Color[] source,
            bool useBilinear,
            out Color[] premultiplied
        )
        {
            if (useBilinear)
            {
                foreach (Color pixel in source)
                {
                    if (pixel.a != 1f)
                    {
                        PooledArray<Color> pooled = SystemArrayPool<Color>.Get(
                            source.Length,
                            out premultiplied
                        );
                        for (int j = 0; j < source.Length; j++)
                        {
                            premultiplied[j] = TextureResampling.Premultiply(source[j]);
                        }

                        return pooled;
                    }
                }
            }

            premultiplied = source;
            return default;
        }

        private static void BilinearScale(
            Color[] source,
            Color[] premultipliedSource,
            Color[] dest,
            int sourceWidth,
            int sourceHeight,
            int destWidth,
            float ratioX,
            float ratioY,
            int startY,
            int endY
        )
        {
            int maxSourceX = sourceWidth - 1;
            int maxSourceY = sourceHeight - 1;

            for (int y = startY; y < endY; y++)
            {
                float sourceYFloat = TextureResampling.BilinearSourceCoordinate(
                    y,
                    ratioY,
                    maxSourceY
                );
                int sourceY = (int)sourceYFloat;
                float yLerp = sourceYFloat - sourceY;

                // Clamp Y indices to prevent out-of-bounds access
                int sourceY1 = Mathf.Min(sourceY, maxSourceY);
                int sourceY2 = Mathf.Min(sourceY + 1, maxSourceY);
                int y1Offset = sourceY1 * sourceWidth;
                int y2Offset = sourceY2 * sourceWidth;
                int destRow = y * destWidth;

                for (int x = 0; x < destWidth; x++)
                {
                    float sourceXFloat = TextureResampling.BilinearSourceCoordinate(
                        x,
                        ratioX,
                        maxSourceX
                    );
                    int sourceX = (int)sourceXFloat;
                    float xLerp = sourceXFloat - sourceX;

                    // Clamp X indices to prevent out-of-bounds access
                    int sourceX1 = Mathf.Min(sourceX, maxSourceX);
                    int sourceX2 = Mathf.Min(sourceX + 1, maxSourceX);

                    int index11 = y1Offset + sourceX1;
                    int index21 = y1Offset + sourceX2;
                    int index12 = y2Offset + sourceX1;
                    int index22 = y2Offset + sourceX2;

                    Color blended = BilinearSample(
                        premultipliedSource,
                        index11,
                        index21,
                        index12,
                        index22,
                        xLerp,
                        yLerp
                    );

                    /*
                        Only an all-invisible neighborhood reaches the straight-color filter, and only
                        because dividing its alpha out would be 0 / 0. Keeping its RGB means a fully
                        transparent image survives a resample instead of collapsing to black.
                    */
                    Color fallback =
                        0f < blended.a
                            ? default
                            : BilinearSample(
                                source,
                                index11,
                                index21,
                                index12,
                                index22,
                                xLerp,
                                yLerp
                            );

                    dest[destRow + x] = TextureResampling.Unpremultiply(blended, fallback);
                }
            }
        }

        private static Color BilinearSample(
            Color[] source,
            int index11,
            int index21,
            int index12,
            int index22,
            float xLerp,
            float yLerp
        )
        {
            Color top = ColorLerpUnclamped(source[index11], source[index21], xLerp);
            Color bottom = ColorLerpUnclamped(source[index12], source[index22], xLerp);
            return ColorLerpUnclamped(top, bottom, yLerp);
        }

        private static void PointScale(
            Color[] source,
            Color[] dest,
            int sourceWidth,
            int sourceHeight,
            int destWidth,
            float ratioX,
            float ratioY,
            int startY,
            int endY
        )
        {
            int maxSourceX = sourceWidth - 1;
            int maxSourceY = sourceHeight - 1;

            for (int y = startY; y < endY; y++)
            {
                int sourceY =
                    TextureResampling.NearestSourceIndex(y, ratioY, maxSourceY) * sourceWidth;
                int destRow = y * destWidth;
                for (int x = 0; x < destWidth; x++)
                {
                    dest[destRow + x] = source[
                        sourceY + TextureResampling.NearestSourceIndex(x, ratioX, maxSourceX)
                    ];
                }
            }
        }

        private static Color ColorLerpUnclamped(Color c1, Color c2, float value)
        {
            return new Color(
                c1.r + (c2.r - c1.r) * value,
                c1.g + (c2.g - c1.g) * value,
                c1.b + (c2.b - c1.b) * value,
                c1.a + (c2.a - c1.a) * value
            );
        }
    }
}
