// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Performance")]
    [Timeout(120000)]
    public sealed class SpriteRegionReadTimingTests : CommonTestBase
    {
        private const int WarmupCalls = 64;
        private const int RoundCount = 21;
        private const double MinimumMilliseconds = 20d;
        private SpriteAlphaMask _mask;
        private Bounds _bounds;

        private static double RunBatch(Action operation, int iterations)
        {
            Stopwatch watch = new();
            watch.Start();
            for (int iteration = 0; iteration < iterations; ++iteration)
            {
                operation();
            }
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds;
        }

        [TestCase(1024, 16, false, TextureFormat.RGBA32)]
        [TestCase(1024, 256, false, TextureFormat.RGBA32)]
        [TestCase(32, 32, false, TextureFormat.RGBA32)]
        [TestCase(1024, 16, true, TextureFormat.RGBA32)]
        [TestCase(1024, 256, true, TextureFormat.RGBA32)]
        [TestCase(32, 32, true, TextureFormat.RGBA32)]
        [TestCase(1024, 16, false, TextureFormat.RGB24)]
        [TestCase(1024, 256, false, TextureFormat.RGB24)]
        [TestCase(32, 32, false, TextureFormat.RGB24)]
        [TestCase(1024, 16, true, TextureFormat.RGB24)]
        [TestCase(1024, 256, true, TextureFormat.RGB24)]
        [TestCase(32, 32, true, TextureFormat.RGB24)]
        public void MeasureOpaqueRegionRead(
            int textureSize,
            int spriteSize,
            bool bounds,
            TextureFormat format
        )
        {
            Texture2D texture = Track(new Texture2D(textureSize, textureSize, format, false));
            Color32[] pixels = new Color32[textureSize * textureSize];
            for (int index = 0; index < pixels.Length; ++index)
            {
                pixels[index] = new Color32(17, 31, 47, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            int origin = (textureSize - spriteSize) / 2;
            Sprite sprite = Track(
                Sprite.Create(
                    texture,
                    new Rect(origin, origin, spriteSize, spriteSize),
                    new Vector2(0.25f, 0.75f),
                    100f
                )
            );
            Action operation;
            if (bounds)
            {
                operation = () =>
                {
                    if (
                        !SpriteArtBounds.TryGetLocalArtBounds(sprite, out _bounds, out string error)
                    )
                    {
                        Assert.Fail(error);
                    }
                };
            }
            else
            {
                operation = () =>
                {
                    if (!SpriteMaskReader.TryRead(sprite, out _mask, out string error))
                    {
                        Assert.Fail(error);
                    }
                };
            }
            RunBatch(operation, WarmupCalls);
            int iterations = 1;
            while (RunBatch(operation, iterations) < MinimumMilliseconds)
            {
                Assert.Less(
                    iterations,
                    1 << 20,
                    "Calibration did not reach its measurement floor."
                );
                iterations *= 2;
            }
            double[] samples = new double[RoundCount];
            for (int round = 0; round < RoundCount; ++round)
            {
                samples[round] = RunBatch(operation, iterations) * 1000000d / iterations;
            }
            if (bounds)
            {
                Assert.AreEqual(spriteSize / 100f, _bounds.size.x, 1e-5f);
                Assert.AreEqual(spriteSize / 100f, _bounds.size.y, 1e-5f);
            }
            else
            {
                Assert.AreEqual(spriteSize, _mask.Width);
                Assert.AreEqual(spriteSize, _mask.Height);
                foreach (bool pixel in _mask.Pixels)
                {
                    Assert.IsTrue(pixel);
                }
            }
            TestContext.WriteLine(
                $"SPRITE_REGION_ID|{typeof(SpriteMaskReader).Assembly.ManifestModule.ModuleVersionId}"
            );
            TestContext.WriteLine(
                $"SPRITE_REGION|{textureSize}|{spriteSize}|{bounds}|{iterations}|{format}"
            );
            foreach (double sample in samples)
            {
                TestContext.WriteLine(sample.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }
}
