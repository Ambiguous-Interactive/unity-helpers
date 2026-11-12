// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Performance")]
    public sealed class SpriteInkRegionTimingTests
    {
        private static long Measure(SpritePixelRegion region, Vector2 expected, int calls)
        {
            int matches = 0;
            long start = Stopwatch.GetTimestamp();
            for (int call = 0; call < calls; ++call)
            {
                bool found = SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    region,
                    0f,
                    out Vector2 centre,
                    out _
                );
                matches += found && centre == expected ? 1 : 0;
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            Assert.That(matches, Is.EqualTo(calls));
            return elapsed;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CaptureInkTraversal(int pattern)
        {
            if (!Stopwatch.IsHighResolution)
            {
                Assert.Ignore("A high-resolution timer is required for this capture.");
            }
            long first = Stopwatch.GetTimestamp();
            long next = first;
            for (int spin = 0; spin < 10000 && first == next; ++spin)
            {
                next = Stopwatch.GetTimestamp();
            }
            Assert.That(first, Is.LessThan(next));
            const int width = 256;
            const int height = 256;
            Color32[] pixels = new Color32[width * height];
            for (int row = 0; row < height; ++row)
            {
                for (int column = 0; column < width; ++column)
                {
                    bool black = pattern switch
                    {
                        0 => true,
                        1 => ((row + column) & 1) == 0,
                        2 => row == height / 2,
                        _ => row == 0 && column == 0,
                    };
                    pixels[row * width + column] = black
                        ? new Color32(0, 0, 0, 255)
                        : new Color32(255, 255, 255, pattern == 3 ? (byte)127 : (byte)255);
                }
            }
            Vector2 expected = pattern switch
            {
                0 => new Vector2(width / 2f, height / 2f),
                1 => new Vector2(0.5f, 0.5f),
                2 => new Vector2(width / 2f, height / 2f + 0.5f),
                _ => new Vector2(0.5f, 0.5f),
            };
            SpritePixelRegion region = new(
                pixels,
                width,
                new RectInt(0, 0, width, height),
                Vector2.zero,
                Vector2.one
            );
            for (int warm = 0; warm < 64; ++warm)
            {
                bool found = SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    region,
                    0f,
                    out Vector2 centre,
                    out string error
                );
                Assert.That(found, Is.True, error);
                Assert.That(centre, Is.EqualTo(expected));
            }
            int calls = 1;
            long elapsed = Measure(region, expected, calls);
            while (elapsed < Stopwatch.Frequency / 50 && calls < 1048576)
            {
                calls *= 2;
                elapsed = Measure(region, expected, calls);
            }
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(Stopwatch.Frequency / 50));
            string[] samples = new string[21];
            for (int round = 0; round < samples.Length; ++round)
            {
                long ticks = Measure(region, expected, calls);
                double nanoseconds = ticks * (1000000000.0 / Stopwatch.Frequency) / calls;
                samples[round] = nanoseconds.ToString("R", CultureInfo.InvariantCulture);
            }
            // Loaded assembly identity distinguishes stale editor images from the source on disk.
            Guid identity = typeof(SpriteArtBounds).Assembly.ManifestModule.ModuleVersionId;
            string rounds = string.Join(",", samples);
            UnityEngine.Debug.Log(
                $"INK-ROWS pattern={pattern} calls={calls} mvid={identity} nanoseconds={rounds}"
            );
        }
    }
}
