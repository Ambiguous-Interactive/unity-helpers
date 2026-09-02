// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using Helper;
    using Random;
    using Unity.Collections;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Defines the method used for averaging colors from multiple pixels or sources.
    /// </summary>
    /// <remarks>
    /// Different averaging methods produce different perceptual results:
    /// - LAB provides the most perceptually accurate averaging
    /// - HSV preserves color vibrancy
    /// - Weighted accounts for human luminance perception
    /// - Dominant finds the most common color cluster
    /// </remarks>
    public enum ColorAveragingMethod
    {
        /// <summary>CIE L*a*b* space averaging - most perceptually accurate for human vision.</summary>
        LAB = 0,

        /// <summary>HSV space averaging - preserves vibrant colors better.</summary>
        HSV = 1,

        /// <summary>Weighted RGB averaging using perceived luminance coefficients.</summary>
        Weighted = 2,

        /// <summary>Find most dominant color cluster using bucket-based analysis.</summary>
        Dominant = 3,
    }

    /// <summary>
    /// Provides extension methods for Unity's Color type, including color averaging, hex conversion, and complementary color generation.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe as they operate on value types and use no shared state.
    /// Reference: https://sharpsnippets.wordpress.com/2014/03/11/c-extension-complementary-color/
    /// </remarks>
    public static class ColorExtensions
    {
        /// <summary>
        /// Converts a Unity Color to a hexadecimal color string representation.
        /// </summary>
        /// <param name="color">The color to convert.</param>
        /// <param name="includeAlpha">If true, includes the alpha channel in the output (RGBA). If false, only RGB is included.</param>
        /// <returns>A hexadecimal color string starting with '#' (e.g., "#FF00FF" or "#FF00FFAA").</returns>
        /// <remarks>
        /// Null Handling: N/A - operates on value types.
        /// Thread Safety: Thread-safe, no shared state.
        /// Performance: O(1) - simple arithmetic and string formatting.
        /// Allocations: Allocates one string for the result.
        /// Edge Cases: Color component values outside [0,1] are clamped to valid range, and NaN encodes to 00.
        /// Each channel is rounded to the nearest of the 256 representable values via
        /// <see cref="ColorQuantization.ToByte(float)"/>, so the result matches Unity's own
        /// <see cref="ColorUtility.ToHtmlStringRGBA(Color)"/> and the <see cref="Color32"/> the same color casts to.
        /// </remarks>
        public static string ToHex(this in Color color, bool includeAlpha = true)
        {
            byte r = ColorQuantization.ToByte(color.r);
            byte g = ColorQuantization.ToByte(color.g);
            byte b = ColorQuantization.ToByte(color.b);

            if (!includeAlpha)
            {
                return $"#{r:X2}{g:X2}{b:X2}";
            }

            byte a = ColorQuantization.ToByte(color.a);
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }

        /// <summary>
        /// Calculates the average color of a sprite's texture pixels using the specified averaging method.
        /// </summary>
        /// <param name="sprite">The sprite whose average color to calculate.</param>
        /// <param name="method">The color averaging method to use (default: LAB for perceptually accurate results).</param>
        /// <param name="alphaCutoff">Pixels with alpha below this threshold are excluded from calculation (default: 0.01).</param>
        /// <returns>The calculated average color with full alpha (1.0) for most methods.</returns>
        /// <remarks>
        /// Null Handling: Returns black (0,0,0,1) if sprite is null.
        /// Thread Safety: NOT thread-safe - calls MakeReadable() which modifies texture import settings.
        /// Performance: O(n) where n is the number of pixels. Can be expensive for large textures.
        /// Allocations: Allocates Color[] array from texture.GetPixels().
        /// Edge Cases: Empty sprites or sprites with all transparent pixels may return black.
        /// </remarks>
        public static Color GetAverageColor(
            this Sprite sprite,
            ColorAveragingMethod method = ColorAveragingMethod.LAB,
            float alphaCutoff = 0.01f
        )
        {
            return Enumerables.Of(sprite).GetAverageColor(method, alphaCutoff);
        }

        /// <summary>
        /// Calculates the average color across all pixels in multiple sprites using the specified averaging method.
        /// </summary>
        /// <param name="sprites">The collection of sprites to analyze.</param>
        /// <param name="method">The color averaging method to use (default: LAB for perceptually accurate results).</param>
        /// <param name="alphaCutoff">Pixels with alpha below this threshold are excluded from calculation (default: 0.01).</param>
        /// <returns>The calculated average color across all sprite pixels with full alpha (1.0) for most methods.</returns>
        /// <remarks>
        /// Null Handling: Null sprites and textures are filtered out automatically.
        /// Thread Safety: NOT thread-safe - calls MakeReadable() which modifies texture import settings.
        /// Performance: O(n*m) where n is the number of sprites and m is pixels per sprite. Can be very expensive.
        /// Allocations: Allocates Color[] array for each texture via GetPixels(), plus LINQ enumerators.
        /// Edge Cases: Empty collection or all transparent pixels returns black. Duplicate textures are processed multiple times.
        /// </remarks>
        public static Color GetAverageColor(
            this IEnumerable<Sprite> sprites,
            ColorAveragingMethod method = ColorAveragingMethod.LAB,
            float alphaCutoff = 0.01f
        )
        {
            if (sprites == null)
            {
                return method == ColorAveragingMethod.Weighted ? Color.clear : Color.black;
            }

            switch (method)
            {
                case ColorAveragingMethod.LAB:
                    return AverageSpritesLAB(sprites, alphaCutoff);
                case ColorAveragingMethod.HSV:
                    return AverageSpritesHSV(sprites, alphaCutoff);
                case ColorAveragingMethod.Weighted:
                    return AverageSpritesWeighted(sprites, alphaCutoff);
                case ColorAveragingMethod.Dominant:
                    return DominantColorFromSprites(sprites, alphaCutoff);
                default:
                    return Color.black;
            }

            static Color AverageSpritesLAB(IEnumerable<Sprite> sprites, float alphaCutoff)
            {
                /*
                    The comparison below asks the same question per pixel that this asks once:
                    which stored channels fall under the normalized cutoff.
                */
                byte alphaThreshold = ColorQuantization.ToThresholdByte(alphaCutoff);
                double l = 0;
                double a = 0;
                double b = 0;
                int count = 0;
                foreach (Sprite sprite in sprites)
                {
                    if (sprite == null)
                    {
                        continue;
                    }
                    Texture2D texture = sprite.texture;
                    if (texture == null)
                    {
                        continue;
                    }
                    texture.MakeReadable();
                    int expectedBytes = texture.width * texture.height * 4;
                    NativeArray<byte> rawBytes = texture.GetRawTextureData<byte>();
                    if (rawBytes.Length == expectedBytes)
                    {
                        NativeArray<Color32> raw = texture.GetRawTextureData<Color32>();
                        int n = raw.Length;
                        for (int i = 0; i < n; ++i)
                        {
                            Color32 c = raw[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            Color cf = c;
                            LABColor lab = RGBToLAB(cf);
                            l += lab.l;
                            a += lab.a;
                            b += lab.b;
                            ++count;
                        }
                    }
                    else
                    {
                        Color32[] px = texture.GetPixels32();
                        for (int i = 0; i < px.Length; ++i)
                        {
                            Color32 c = px[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            Color cf = c;
                            LABColor lab = RGBToLAB(cf);
                            l += lab.l;
                            a += lab.a;
                            b += lab.b;
                            ++count;
                        }
                    }
                }

                count = Mathf.Max(count, 1);
                return LABToRGB(l / count, a / count, b / count);
            }

            static Color AverageSpritesHSV(IEnumerable<Sprite> sprites, float alphaCutoff)
            {
                /*
                    The comparison below asks the same question per pixel that this asks once:
                    which stored channels fall under the normalized cutoff.
                */
                byte alphaThreshold = ColorQuantization.ToThresholdByte(alphaCutoff);
                float sumCos = 0f;
                float sumSin = 0f;
                float sumS = 0f;
                float sumV = 0f;
                int count = 0;
                foreach (Sprite sprite in sprites)
                {
                    if (sprite == null)
                    {
                        continue;
                    }
                    Texture2D texture = sprite.texture;
                    if (texture == null)
                    {
                        continue;
                    }
                    texture.MakeReadable();
                    int expectedBytes = texture.width * texture.height * 4;
                    NativeArray<byte> rawBytes = texture.GetRawTextureData<byte>();
                    if (rawBytes.Length == expectedBytes)
                    {
                        NativeArray<Color32> raw = texture.GetRawTextureData<Color32>();
                        int n = raw.Length;
                        for (int i = 0; i < n; ++i)
                        {
                            Color32 c = raw[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            Color cf = c;
                            Color.RGBToHSV(cf, out float h, out float s, out float v);
                            float hueRad = h * 2f * Mathf.PI;
                            sumCos += Mathf.Cos(hueRad);
                            sumSin += Mathf.Sin(hueRad);
                            sumS += s;
                            sumV += v;
                            ++count;
                        }
                    }
                    else
                    {
                        Color32[] px = texture.GetPixels32();
                        for (int i = 0; i < px.Length; ++i)
                        {
                            Color32 c = px[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            Color cf = c;
                            Color.RGBToHSV(cf, out float h, out float s, out float v);
                            float hueRad = h * 2f * Mathf.PI;
                            sumCos += Mathf.Cos(hueRad);
                            sumSin += Mathf.Sin(hueRad);
                            sumS += s;
                            sumV += v;
                            ++count;
                        }
                    }
                }

                if (count == 0)
                {
                    return Color.black;
                }

                float avgHueRad = Mathf.Atan2(sumSin / count, sumCos / count);
                if (avgHueRad < 0f)
                {
                    avgHueRad += 2f * Mathf.PI;
                }
                float avgHue = avgHueRad / (2f * Mathf.PI);
                float avgS = sumS / count;
                float avgV = sumV / count;
                return Color.HSVToRGB(avgHue, avgS, avgV);
            }

            static Color AverageSpritesWeighted(IEnumerable<Sprite> sprites, float alphaCutoff)
            {
                /*
                    The comparison below asks the same question per pixel that this asks once:
                    which stored channels fall under the normalized cutoff.
                */
                byte alphaThreshold = ColorQuantization.ToThresholdByte(alphaCutoff);
                const float rW = 0.299f;
                const float gW = 0.587f;
                const float bW = 0.114f;
                float total = 0f;
                float r = 0f;
                float g = 0f;
                float b = 0f;
                float a = 0f;
                foreach (Sprite sprite in sprites)
                {
                    if (sprite == null)
                    {
                        continue;
                    }
                    Texture2D texture = sprite.texture;
                    if (texture == null)
                    {
                        continue;
                    }
                    texture.MakeReadable();
                    int expectedBytes = texture.width * texture.height * 4;
                    NativeArray<byte> rawBytes = texture.GetRawTextureData<byte>();
                    if (rawBytes.Length == expectedBytes)
                    {
                        NativeArray<Color32> raw = texture.GetRawTextureData<Color32>();
                        int n = raw.Length;
                        for (int i = 0; i < n; ++i)
                        {
                            Color32 c = raw[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            float rf = ColorQuantization.ToNormalized(c.r);
                            float gf = ColorQuantization.ToNormalized(c.g);
                            float bf = ColorQuantization.ToNormalized(c.b);
                            float af = ColorQuantization.ToNormalized(c.a);
                            float w = rf * rW + gf * gW + bf * bW;
                            r += rf * w;
                            g += gf * w;
                            b += bf * w;
                            a += af * w;
                            total += w;
                        }
                    }
                    else
                    {
                        Color32[] px = texture.GetPixels32();
                        for (int i = 0; i < px.Length; ++i)
                        {
                            Color32 c = px[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            float rf = ColorQuantization.ToNormalized(c.r);
                            float gf = ColorQuantization.ToNormalized(c.g);
                            float bf = ColorQuantization.ToNormalized(c.b);
                            float af = ColorQuantization.ToNormalized(c.a);
                            float w = rf * rW + gf * gW + bf * bW;
                            r += rf * w;
                            g += gf * w;
                            b += bf * w;
                            a += af * w;
                            total += w;
                        }
                    }
                }
                if (0f < total)
                {
                    r /= total;
                    g /= total;
                    b /= total;
                    a /= total;
                }
                return new Color(r, g, b, a);
            }

            static Color DominantColorFromSprites(IEnumerable<Sprite> sprites, float alphaCutoff)
            {
                /*
                    The comparison below asks the same question per pixel that this asks once:
                    which stored channels fall under the normalized cutoff.
                */
                byte alphaThreshold = ColorQuantization.ToThresholdByte(alphaCutoff);
                using PooledResource<Dictionary<FastVector3Int, int>> bucketsLease =
                    DictionaryBuffer<FastVector3Int, int>.Dictionary.Get(
                        out Dictionary<FastVector3Int, int> buckets
                    );
                const int bucketSize = 32;
                bool hasSamples = false;

                foreach (Sprite sprite in sprites)
                {
                    if (sprite == null)
                    {
                        continue;
                    }
                    Texture2D texture = sprite.texture;
                    if (texture == null)
                    {
                        continue;
                    }
                    texture.MakeReadable();
                    int expectedBytes = texture.width * texture.height * 4;
                    NativeArray<byte> rawBytes = texture.GetRawTextureData<byte>();
                    if (rawBytes.Length == expectedBytes)
                    {
                        NativeArray<Color32> raw = texture.GetRawTextureData<Color32>();
                        int n = raw.Length;
                        for (int i = 0; i < n; ++i)
                        {
                            Color32 c = raw[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            FastVector3Int bucket = new(
                                Mathf.RoundToInt(c.r / (float)bucketSize),
                                Mathf.RoundToInt(c.g / (float)bucketSize),
                                Mathf.RoundToInt(c.b / (float)bucketSize)
                            );
                            buckets.AddOrUpdate(
                                bucket,
                                static _ => 0,
                                static (_, value) => value + 1
                            );
                            hasSamples = true;
                        }
                    }
                    else
                    {
                        Color32[] px = texture.GetPixels32();
                        for (int i = 0; i < px.Length; ++i)
                        {
                            Color32 c = px[i];
                            if (c.a <= alphaThreshold)
                            {
                                continue;
                            }
                            FastVector3Int bucket = new(
                                Mathf.RoundToInt(c.r / (float)bucketSize),
                                Mathf.RoundToInt(c.g / (float)bucketSize),
                                Mathf.RoundToInt(c.b / (float)bucketSize)
                            );
                            buckets.AddOrUpdate(
                                bucket,
                                static _ => 0,
                                static (_, value) => value + 1
                            );
                            hasSamples = true;
                        }
                    }
                }

                KeyValuePair<FastVector3Int, int>? largest = null;
                foreach (KeyValuePair<FastVector3Int, int> kv in buckets)
                {
                    largest ??= kv;
                    if (largest.Value.Value < kv.Value)
                    {
                        largest = kv;
                    }
                }
                if (!hasSamples || largest == null)
                {
                    return Color.black;
                }
                FastVector3Int dominant = largest.Value.Key;
                return new Color(
                    BucketToChannel(dominant.x, bucketSize),
                    BucketToChannel(dominant.y, bucketSize),
                    BucketToChannel(dominant.z, bucketSize),
                    1f
                );
            }
        }

        /// <summary>
        /// Calculates the average color of a collection of pixels using the specified averaging method.
        /// </summary>
        /// <param name="pixels">The collection of color values to average.</param>
        /// <param name="method">The color averaging method to use (default: LAB for perceptually accurate results).</param>
        /// <param name="alphaCutoff">Pixels with alpha below this threshold are excluded from calculation (default: 0.01).</param>
        /// <returns>The calculated average color. Alpha is 1.0 for LAB/HSV methods, weighted average for Weighted method.</returns>
        /// <exception cref="InvalidEnumArgumentException">Thrown when an invalid ColorAveragingMethod is provided.</exception>
        /// <remarks>
        /// Null Handling: Individual null colors are not handled; ensure the collection doesn't contain null.
        /// Thread Safety: Thread-safe when pixels collection is not modified during execution.
        /// Performance: O(n) where n is pixel count. LAB/HSV involve color space conversions. Dominant uses bucket clustering.
        /// Allocations: Optimized for IReadOnlyList and HashSet. LAB/HSV use stack allocations. Dominant uses pooled dictionary.
        /// Edge Cases: Empty collection or all transparent pixels returns black (0,0,0,1) for LAB/HSV/Dominant, (0,0,0,0) for Weighted.
        /// </remarks>
        public static Color GetAverageColor(
            this IEnumerable<Color> pixels,
            ColorAveragingMethod method = ColorAveragingMethod.LAB,
            float alphaCutoff = 0.01f
        )
        {
            return method switch
            {
                ColorAveragingMethod.LAB => AverageInLABSpace(pixels, alphaCutoff),
                ColorAveragingMethod.HSV => AverageInHSVSpace(pixels, alphaCutoff),
                ColorAveragingMethod.Weighted => WeightedRGBAverage(pixels, alphaCutoff),
                ColorAveragingMethod.Dominant => GetDominantColor(pixels, alphaCutoff),
                _ => throw new InvalidEnumArgumentException(
                    nameof(method),
                    (int)method,
                    typeof(ColorAveragingMethod)
                ),
            };
        }

        // CIE L*a*b* space averaging - most perceptually accurate
        private static Color AverageInLABSpace(IEnumerable<Color> pixels, float alphaCutoff)
        {
            double l = 0;
            double a = 0;
            double b = 0;
            int count = 0;
            switch (pixels)
            {
                case IReadOnlyList<Color> colorList:
                {
                    for (int i = 0; i < colorList.Count; i++)
                    {
                        Color pixel = colorList[i];
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        LABColor lab = RGBToLAB(pixel);
                        l += lab.l;
                        a += lab.a;
                        b += lab.b;
                        ++count;
                    }

                    break;
                }
                case HashSet<Color> colorSet:
                {
                    foreach (Color pixel in colorSet)
                    {
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        LABColor lab = RGBToLAB(pixel);
                        l += lab.l;
                        a += lab.a;
                        b += lab.b;
                        ++count;
                    }

                    break;
                }
                default:
                {
                    foreach (Color pixel in pixels)
                    {
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        LABColor lab = RGBToLAB(pixel);
                        l += lab.l;
                        a += lab.a;
                        b += lab.b;
                        ++count;
                    }

                    break;
                }
            }

            count = Mathf.Max(count, 1);
            return LABToRGB(l / count, a / count, b / count);
        }

        // HSV space averaging - good for preserving vibrant colors
        private static Color AverageInHSVSpace(IEnumerable<Color> pixels, float alphaCutoff)
        {
            float sumCos = 0f;
            float sumSin = 0f;
            float sumS = 0f;
            float sumV = 0f;
            int count = 0;

            Accumulate(pixels);

            if (count == 0)
            {
                return Color.black;
            }

            float averageHueRadians = Mathf.Atan2(sumSin / count, sumCos / count);
            if (averageHueRadians < 0f)
            {
                averageHueRadians += 2f * Mathf.PI;
            }

            float averageHue = averageHueRadians / (2f * Mathf.PI);
            float averageS = sumS / count;
            float averageV = sumV / count;

            return Color.HSVToRGB(averageHue, averageS, averageV);

            void Accumulate(IEnumerable<Color> source)
            {
                switch (source)
                {
                    case IReadOnlyList<Color> colorList:
                    {
                        for (int i = 0; i < colorList.Count; ++i)
                        {
                            Color pixel = colorList[i];
                            if (pixel.a <= alphaCutoff)
                            {
                                continue;
                            }

                            Color.RGBToHSV(pixel, out float h, out float s, out float v);
                            float hueRadians = h * 2f * Mathf.PI;
                            sumCos += Mathf.Cos(hueRadians);
                            sumSin += Mathf.Sin(hueRadians);
                            sumS += s;
                            sumV += v;
                            ++count;
                        }

                        break;
                    }
                    case HashSet<Color> colorSet:
                    {
                        foreach (Color pixel in colorSet)
                        {
                            if (pixel.a <= alphaCutoff)
                            {
                                continue;
                            }

                            Color.RGBToHSV(pixel, out float h, out float s, out float v);
                            float hueRadians = h * 2f * Mathf.PI;
                            sumCos += Mathf.Cos(hueRadians);
                            sumSin += Mathf.Sin(hueRadians);
                            sumS += s;
                            sumV += v;
                            ++count;
                        }

                        break;
                    }
                    default:
                    {
                        foreach (Color pixel in source)
                        {
                            if (pixel.a <= alphaCutoff)
                            {
                                continue;
                            }

                            Color.RGBToHSV(pixel, out float h, out float s, out float v);
                            float hueRadians = h * 2f * Mathf.PI;
                            sumCos += Mathf.Cos(hueRadians);
                            sumSin += Mathf.Sin(hueRadians);
                            sumS += s;
                            sumV += v;
                            ++count;
                        }

                        break;
                    }
                }
            }
        }

        // Weighted RGB averaging using perceived luminance
        private static Color WeightedRGBAverage(IEnumerable<Color> pixels, float alphaCutoff)
        {
            // Use perceived luminance weights
            const float rWeight = 0.299f;
            const float gWeight = 0.587f;
            const float bWeight = 0.114f;

            float totalWeight = 0f;
            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 0f;

            /*
                Kept alongside the weighted sums for the case where every surviving pixel weighs
                nothing. Luma is zero for black, so a wholly black sprite drove totalWeight to zero,
                skipped the division, and returned the accumulators untouched - Color.clear, from
                opaque input.
            */
            int count = 0;
            float plainR = 0f;
            float plainG = 0f;
            float plainB = 0f;
            float plainA = 0f;

            switch (pixels)
            {
                case IReadOnlyList<Color> colorList:
                {
                    for (int i = 0; i < colorList.Count; i++)
                    {
                        Color pixel = colorList[i];
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        float weight = pixel.r * rWeight + pixel.g * gWeight + pixel.b * bWeight;
                        r += pixel.r * weight;
                        g += pixel.g * weight;
                        b += pixel.b * weight;
                        a += pixel.a * weight;
                        totalWeight += weight;
                        plainR += pixel.r;
                        plainG += pixel.g;
                        plainB += pixel.b;
                        plainA += pixel.a;
                        ++count;
                    }

                    break;
                }
                case HashSet<Color> colorSet:
                {
                    foreach (Color pixel in colorSet)
                    {
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }
                        float weight = pixel.r * rWeight + pixel.g * gWeight + pixel.b * bWeight;
                        r += pixel.r * weight;
                        g += pixel.g * weight;
                        b += pixel.b * weight;
                        a += pixel.a * weight;
                        totalWeight += weight;
                        plainR += pixel.r;
                        plainG += pixel.g;
                        plainB += pixel.b;
                        plainA += pixel.a;
                        ++count;
                    }

                    break;
                }
                default:
                {
                    foreach (Color pixel in pixels)
                    {
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        float weight = pixel.r * rWeight + pixel.g * gWeight + pixel.b * bWeight;
                        r += pixel.r * weight;
                        g += pixel.g * weight;
                        b += pixel.b * weight;
                        a += pixel.a * weight;
                        totalWeight += weight;
                        plainR += pixel.r;
                        plainG += pixel.g;
                        plainB += pixel.b;
                        plainA += pixel.a;
                        ++count;
                    }

                    break;
                }
            }

            if (0f < totalWeight)
            {
                r /= totalWeight;
                g /= totalWeight;
                b /= totalWeight;
                a /= totalWeight;
                return new Color(r, g, b, a);
            }

            if (0 < count)
            {
                return new Color(plainR / count, plainG / count, plainB / count, plainA / count);
            }

            return Color.clear;
        }

        // Find dominant color using simple clustering
        private static Color GetDominantColor(IEnumerable<Color> pixels, float alphaCutoff)
        {
            using PooledResource<Dictionary<FastVector3Int, int>> colorBucketResource =
                DictionaryBuffer<FastVector3Int, int>.Dictionary.Get(
                    out Dictionary<FastVector3Int, int> cache
                );
            const int bucketSize = 32; // Adjust for different precision
            bool hasSamples = false;

            switch (pixels)
            {
                case IReadOnlyList<Color> colorList:
                {
                    for (int i = 0; i < colorList.Count; i++)
                    {
                        Color pixel = colorList[i];
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        FastVector3Int bucket = new(
                            Mathf.RoundToInt(pixel.r * 255 / bucketSize),
                            Mathf.RoundToInt(pixel.g * 255 / bucketSize),
                            Mathf.RoundToInt(pixel.b * 255 / bucketSize)
                        );

                        cache.AddOrUpdate(bucket, static _ => 0, static (_, value) => value + 1);
                        hasSamples = true;
                    }

                    break;
                }
                case HashSet<Color> colorSet:
                {
                    foreach (Color pixel in colorSet)
                    {
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        FastVector3Int bucket = new(
                            Mathf.RoundToInt(pixel.r * 255 / bucketSize),
                            Mathf.RoundToInt(pixel.g * 255 / bucketSize),
                            Mathf.RoundToInt(pixel.b * 255 / bucketSize)
                        );

                        cache.AddOrUpdate(bucket, static _ => 0, static (_, value) => value + 1);
                        hasSamples = true;
                    }

                    break;
                }
                default:
                {
                    foreach (Color pixel in pixels)
                    {
                        if (pixel.a <= alphaCutoff)
                        {
                            continue;
                        }

                        FastVector3Int bucket = new(
                            Mathf.RoundToInt(pixel.r * 255 / bucketSize),
                            Mathf.RoundToInt(pixel.g * 255 / bucketSize),
                            Mathf.RoundToInt(pixel.b * 255 / bucketSize)
                        );

                        cache.AddOrUpdate(bucket, static _ => 0, static (_, value) => value + 1);
                        hasSamples = true;
                    }

                    break;
                }
            }

            KeyValuePair<FastVector3Int, int>? largest = null;
            if (0 < cache.Count)
            {
                foreach (KeyValuePair<FastVector3Int, int> bucketEntry in cache)
                {
                    largest ??= bucketEntry;
                    if (largest.Value.Value < bucketEntry.Value)
                    {
                        largest = bucketEntry;
                    }
                }
            }

            if (!hasSamples || largest == null)
            {
                return Color.black;
            }

            FastVector3Int dominantBucket = largest.Value.Key;
            return new Color(
                BucketToChannel(dominantBucket.x, bucketSize),
                BucketToChannel(dominantBucket.y, bucketSize),
                BucketToChannel(dominantBucket.z, bucketSize),
                1f
            );
        }

        /*
            The topmost bucket represents channel 256, which is not a channel. Without the clamp a
            dominant white returns 1.0039 and the caller's "color" is outside the range it declares.
        */
        private static float BucketToChannel(int bucket, int bucketSize)
        {
            return ColorQuantization.ToNormalized((byte)Mathf.Min(bucket * bucketSize, 255));
        }

        private static LABColor RGBToLAB(Color rgb)
        {
            // First convert to XYZ
            double r =
                0.04045 < rgb.r ? Mathf.Pow((rgb.r + 0.055f) / 1.055f, 2.4f) : rgb.r / 12.92f;
            double g =
                0.04045 < rgb.g ? Mathf.Pow((rgb.g + 0.055f) / 1.055f, 2.4f) : rgb.g / 12.92f;
            double b =
                0.04045 < rgb.b ? Mathf.Pow((rgb.b + 0.055f) / 1.055f, 2.4f) : rgb.b / 12.92f;

            double x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
            double y = r * 0.2126 + g * 0.7152 + b * 0.0722;
            double z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;

            x = 0.008856 < x ? Mathf.Pow((float)x, 1f / 3f) : 7.787 * x + 16f / 116f;
            y = 0.008856 < y ? Mathf.Pow((float)y, 1f / 3f) : 7.787 * y + 16f / 116f;
            z = 0.008856 < z ? Mathf.Pow((float)z, 1f / 3f) : 7.787 * z + 16f / 116f;

            return new LABColor(116 * y - 16, 500 * (x - y), 200 * (y - z));
        }

        private static Color LABToRGB(double l, double a, double b)
        {
            double y = (l + 16) / 116;
            double x = a / 500 + y;
            double z = y - b / 200;

            double x3 = x * x * x;
            double y3 = y * y * y;
            double z3 = z * z * z;

            x = 0.95047 * (0.008856 < x3 ? x3 : (x - 16.0 / 116.0) / 7.787);
            y = 0.008856 < y3 ? y3 : (y - 16.0 / 116.0) / 7.787;
            z = 1.08883 * (0.008856 < z3 ? z3 : (z - 16.0 / 116.0) / 7.787);

            double r = x * 3.2406 + y * -1.5372 + z * -0.4986;
            double g = x * -0.9689 + y * 1.8758 + z * 0.0415;
            double b2 = x * 0.0557 + y * -0.2040 + z * 1.0570;

            r = 0.0031308 < r ? 1.055 * Mathf.Pow((float)r, 1 / 2.4f) - 0.055 : 12.92 * r;
            g = 0.0031308 < g ? 1.055 * Mathf.Pow((float)g, 1 / 2.4f) - 0.055 : 12.92 * g;
            b2 = 0.0031308 < b2 ? 1.055 * Mathf.Pow((float)b2, 1 / 2.4f) - 0.055 : 12.92 * b2;

            return new Color(
                Mathf.Clamp01((float)r),
                Mathf.Clamp01((float)g),
                Mathf.Clamp01((float)b2),
                1f
            );
        }

        /// <summary>
        /// Generates a complementary color by rotating the hue by 180 degrees in HSV space.
        /// Handles grayscale colors specially and supports optional randomization.
        /// </summary>
        /// <param name="source">The source color to find the complement of.</param>
        /// <param name="random">Optional random number generator for adding variance (default: null for no randomization).</param>
        /// <param name="variance">
        /// The amount of random variance to apply (default: 0 for none).
        /// If non-zero, variance is applied as +/- range to each RGB component.
        /// If zero with random provided, multiplies each component by random value.
        /// </param>
        /// <returns>The complementary color with the same alpha as the source.</returns>
        /// <remarks>
        /// Null Handling: random can be null (no randomization applied).
        /// Thread Safety: Thread-safe if the provided IRandom is thread-safe.
        /// Performance: O(1) - constant time color space conversion and arithmetic.
        /// Allocations: No heap allocations.
        /// Edge Cases: Grayscale colors (RGB values within 20/255 of each other) are converted to contrasting colors
        /// rather than complementary - dark grays become cyan-ish, light grays become yellow.
        /// </remarks>
        public static Color GetComplement(
            this in Color source,
            IRandom random = null,
            float variance = 0f
        )
        {
            Color inputColor = source;
            /*
                If RGB values are close to each other by a diff less than 10%, then if RGB values are lighter side,
                decrease the blue by 50% (eventually it will increase in conversion below), if RBB values are on the
                darker side, decrease yellow by about 50% (it will increase in conversion)
             */
            float avgColorValue = (source.r + source.g + source.b) / 3;
            float rDiff = Mathf.Abs(source.r - avgColorValue);
            float gDiff = Mathf.Abs(source.g - avgColorValue);
            float bDiff = Mathf.Abs(source.b - avgColorValue);
            const float greyDelta = 20 / 255f;
            //The color is a shade of gray
            if (rDiff < greyDelta && gDiff < greyDelta && bDiff < greyDelta)
            {
                // Color is dark
                if (avgColorValue < 123 / 255f)
                {
                    inputColor.b = 220 / 255f;
                    inputColor.g = 230 / 255f;
                    inputColor.r = 50 / 255f;
                }
                else
                {
                    inputColor.r = 255 / 255f;
                    inputColor.g = 255 / 255f;
                    inputColor.b = 50 / 255f;
                }
            }

            if (random != null)
            {
                if (variance != 0)
                {
                    variance = Mathf.Abs(variance);

                    float minR = Mathf.Clamp01(inputColor.r - variance);
                    float maxR = Mathf.Clamp01(inputColor.r + variance);
                    inputColor.r = random.NextFloat(minR, maxR);

                    float minG = Mathf.Clamp01(inputColor.g - variance);
                    float maxG = Mathf.Clamp01(inputColor.g + variance);
                    inputColor.g = random.NextFloat(minG, maxG);

                    float minB = Mathf.Clamp01(inputColor.b - variance);
                    float maxB = Mathf.Clamp01(inputColor.b + variance);
                    inputColor.b = random.NextFloat(minB, maxB);
                }
                else
                {
                    inputColor.r *= GetRandomScale(random, inputColor.r);
                    inputColor.g *= GetRandomScale(random, inputColor.g);
                    inputColor.b *= GetRandomScale(random, inputColor.b);
                }
            }

            Color.RGBToHSV(inputColor, out float h, out float s, out float v);
            h = h < 0.5f ? h + 0.5f : h - 0.5f;
            Color result = Color.HSVToRGB(h, s, v);
            return result;

            static float GetRandomScale(IRandom rng, float component)
            {
                if (component <= 0f || float.IsNaN(component))
                {
                    return rng.NextFloat();
                }

                float maxScale = 1f / component;
                if (maxScale <= 0f || float.IsInfinity(maxScale))
                {
                    return rng.NextFloat();
                }

                return rng.NextFloat(maxScale);
            }
        }

        // Helper struct for LAB color space
        private readonly struct LABColor
        {
            public readonly double l;
            public readonly double a;
            public readonly double b;

            public LABColor(double l, double a, double b)
            {
                this.l = l;
                this.a = a;
                this.b = b;
            }
        }
    }
}
