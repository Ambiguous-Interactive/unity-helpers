// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpriteInkRegionTests
    {
        private static IEnumerable<TestCaseData> Centres()
        {
            yield return new TestCaseData(new[] { "b" }, 1f, 0.5f, 0.5f).SetName(
                "Ink.SinglePixel.Centre"
            );
            yield return new TestCaseData(new[] { "bbb", "bbb" }, 1f, 1.5f, 1f).SetName(
                "Ink.SolidRectangle.Centre"
            );
            yield return new TestCaseData(new[] { "bw", "wb" }, 0.5f, 0.5f, 0.5f).SetName(
                "Ink.DiagonalRegions.StaySeparate"
            );
            yield return new TestCaseData(new[] { "b.wbb" }, 0f, 4f, 0.5f).SetName(
                "Ink.LargestRegion.Wins"
            );
            yield return new TestCaseData(new[] { "b.b" }, 0f, 0.5f, 0.5f).SetName(
                "Ink.EqualSize.FirstSeedWins"
            );
            yield return new TestCaseData(new[] { "bw", "bb" }, 0f, 5f / 6f, 7f / 6f).SetName(
                "Ink.Centroid.IsNotBoundingBoxCentre"
            );
            yield return new TestCaseData(new[] { "bwggw" }, 0.4f, 3f, 0.5f).SetName(
                "Ink.RareDarkColour.IsIneligible"
            );
            yield return new TestCaseData(new[] { "bwggw" }, 0f, 0.5f, 0.5f).SetName(
                "Ink.ZeroShare.AcceptsRareColour"
            );
            yield return new TestCaseData(new[] { "bwttt" }, 0.5f, 0.5f, 0.5f).SetName(
                "Ink.Share.IgnoresTranslucentPixels"
            );
            yield return new TestCaseData(new[] { "re" }, 0f, 1.5f, 0.5f).SetName(
                "Ink.EqualLuma.LowestRgbWins"
            );
            yield return new TestCaseData(new[] { "bbgg" }, 0.5f, 1f, 0.5f).SetName(
                "Ink.ExactShare.IsEligible"
            );
            yield return new TestCaseData(new[] { "bwg" }, 0.25f, 0.5f, 0.5f).SetName(
                "Ink.Darkest.UsesRgbNotPopulation"
            );
        }

        private static IEnumerable<TestCaseData> InvalidRegions()
        {
            yield return new TestCaseData(default(SpritePixelRegion)).SetName(
                "Ink.Region.Default.Refused"
            );
            Color32[] pixels = { new(0, 0, 0, 255) };
            foreach (
                RectInt region in new[]
                {
                    new RectInt(-1, 0, 1, 1),
                    new RectInt(0, -1, 1, 1),
                    new RectInt(0, 0, 0, 1),
                    new RectInt(0, 0, 1, 0),
                    new RectInt(1, 0, 1, 1),
                    new RectInt(0, 1, 1, 1),
                    new RectInt(0, 0, int.MaxValue, int.MaxValue),
                    new RectInt(int.MaxValue, 0, 1, 1),
                }
            )
            {
                yield return new TestCaseData(
                    new SpritePixelRegion(pixels, 1, region, Vector2.zero, Vector2.one)
                ).SetName(
                    $"Ink.Region.{region.x}.{region.y}.{region.width}.{region.height}.Refused"
                );
            }
            yield return new TestCaseData(
                new SpritePixelRegion(pixels, 0, new RectInt(0, 0, 1, 1), Vector2.zero, Vector2.one)
            ).SetName("Ink.TextureWidth.Zero.Refused");
            yield return new TestCaseData(
                new SpritePixelRegion(
                    new Color32[3],
                    2,
                    new RectInt(0, 0, 1, 1),
                    Vector2.zero,
                    Vector2.one
                )
            ).SetName("Ink.TexturePixels.PartialRow.Refused");
            foreach (
                float value in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity }
            )
            {
                yield return new TestCaseData(
                    new SpritePixelRegion(
                        pixels,
                        1,
                        new RectInt(0, 0, 1, 1),
                        new Vector2(value, 0),
                        Vector2.one
                    )
                ).SetName($"Ink.Pivot.{value}.Refused");
                yield return new TestCaseData(
                    new SpritePixelRegion(
                        pixels,
                        1,
                        new RectInt(0, 0, 1, 1),
                        Vector2.zero,
                        new Vector2(1, value)
                    )
                ).SetName($"Ink.Units.{value}.Refused");
            }
            foreach (float value in new[] { 0f, -1f })
            {
                yield return new TestCaseData(
                    new SpritePixelRegion(
                        pixels,
                        1,
                        new RectInt(0, 0, 1, 1),
                        Vector2.zero,
                        new Vector2(value, 1)
                    )
                ).SetName($"Ink.Units.{value}.Refused");
            }
        }

        private static SpritePixelRegion ReadRows(string[] rows)
        {
            int width = rows[0].Length;
            Color32[] pixels = new Color32[width * rows.Length];
            for (int y = 0; y < rows.Length; ++y)
            {
                Assert.That(rows[y].Length, Is.EqualTo(width));
                for (int x = 0; x < width; ++x)
                {
                    pixels[y * width + x] = rows[y][x] switch
                    {
                        'b' => new Color32(0, 0, 0, 255),
                        'w' => new Color32(255, 255, 255, 255),
                        'g' => new Color32(128, 128, 128, 255),
                        't' => new Color32(0, 0, 0, 254),
                        'r' => new Color32(15, 0, 7, 255),
                        'e' => new Color32(0, 9, 0, 255),
                        '.' => new Color32(0, 0, 0, 0),
                        _ => throw new System.ArgumentException("Unknown pixel fixture."),
                    };
                }
            }
            return new SpritePixelRegion(
                pixels,
                width,
                new RectInt(0, 0, width, rows.Length),
                Vector2.zero,
                Vector2.one
            );
        }

        [TestCaseSource(nameof(Centres))]
        public void ColourShareConnectivityAndTiesHaveStableCentres(
            string[] rows,
            float share,
            float x,
            float y
        )
        {
            SpritePixelRegion pixels = ReadRows(rows);
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    pixels,
                    share,
                    out Vector2 centre,
                    out string error
                ),
                Is.True,
                error
            );
            Assert.That(error, Is.Null);
            Assert.That(centre.x, Is.EqualTo(x).Within(0.000001f));
            Assert.That(centre.y, Is.EqualTo(y).Within(0.000001f));
        }

        [TestCaseSource(nameof(InvalidRegions))]
        public void InvalidRegionsFailBeforeAllocatingFromTheirDimensions(object value)
        {
            SpritePixelRegion pixels = (SpritePixelRegion)value;
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    pixels,
                    0f,
                    out Vector2 centre,
                    out string error
                ),
                Is.False
            );
            Assert.That(centre, Is.EqualTo(Vector2.zero));
            Assert.That(string.IsNullOrWhiteSpace(error), Is.False);
        }

        [TestCase(-1f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidSharesFail(float share)
        {
            SpritePixelRegion pixels = ReadRows(new[] { "b" });
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    pixels,
                    share,
                    out Vector2 centre,
                    out string error
                ),
                Is.False
            );
            Assert.That(centre, Is.EqualTo(Vector2.zero));
            Assert.That(string.IsNullOrWhiteSpace(error), Is.False);
        }

        [TestCase(".", 0f)]
        [TestCase("tt", 0f)]
        [TestCase("bw", 1f)]
        public void MissingOpaqueOrEligibleColourFails(string row, float share)
        {
            SpritePixelRegion pixels = ReadRows(new[] { row });
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    pixels,
                    share,
                    out Vector2 centre,
                    out string error
                ),
                Is.False
            );
            Assert.That(centre, Is.EqualTo(Vector2.zero));
            Assert.That(string.IsNullOrWhiteSpace(error), Is.False);
        }

        [Test]
        public void SubregionReadsIgnoreOutsideColoursAndApplyLocalMapping()
        {
            SpritePixelRegion all = ReadRows(new[] { "bbbb", "bwwb", "bwwb" });
            SpritePixelRegion selected = new(
                all.Pixels,
                4,
                new RectInt(1, 1, 2, 2),
                new Vector2(0.5f, 1),
                new Vector2(0.25f, 0.5f)
            );
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    selected,
                    1f,
                    out Vector2 centre,
                    out string error
                ),
                Is.True,
                error
            );
            Assert.That(centre, Is.EqualTo(new Vector2(0.125f, 0f)));
        }

        [Test]
        public void ReusedScratchFlagsAreClearedAndOnlyTheLogicalPrefixIsVisited()
        {
            SpritePixelRegion pixels = ReadRows(new[] { "bb.w", "bwww" });
            for (int iteration = 0; iteration < 8; ++iteration)
            {
                using (PooledArray<bool> dirty = SystemArrayPool<bool>.Get(8, out bool[] flags))
                {
                    for (int index = 0; index < flags.Length; ++index)
                    {
                        flags[index] = true;
                    }
                }
                Assert.That(
                    SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                        pixels,
                        0f,
                        out Vector2 centre,
                        out string error
                    ),
                    Is.True,
                    error
                );
                Assert.That(centre.x, Is.EqualTo(5f / 6f).Within(0.000001f));
                Assert.That(centre.y, Is.EqualTo(5f / 6f).Within(0.000001f));
            }
        }

        [Test]
        public void NonrepresentableLocalCentreFails()
        {
            SpritePixelRegion data = ReadRows(new[] { "b" });
            SpritePixelRegion pixels = new(
                data.Pixels,
                1,
                new RectInt(0, 0, 1, 1),
                new Vector2(-float.MaxValue, 0),
                new Vector2(float.MaxValue, 1)
            );
            Assert.That(
                SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    pixels,
                    0f,
                    out Vector2 centre,
                    out string error
                ),
                Is.False
            );
            Assert.That(centre, Is.EqualTo(Vector2.zero));
            Assert.That(error, Does.Contain("represented"));
        }

        [Test]
        public void EveryThreeByThreeMaskMatchesAnIndependentReachabilityOracle()
        {
            const int width = 3;
            const int count = width * width;
            for (int pattern = 0; pattern < 1 << count; ++pattern)
            {
                Color32[] colours = new Color32[count];
                bool[] selected = new bool[count];
                bool[,] reachable = new bool[count, count];
                for (int index = 0; index < count; ++index)
                {
                    bool black = (pattern & (1 << index)) != 0;
                    byte channel = black ? (byte)0 : byte.MaxValue;
                    colours[index] = new Color32(channel, channel, channel, 255);
                    selected[index] = black || pattern == 0;
                }
                for (int first = 0; first < count; ++first)
                {
                    for (int last = 0; last < count; ++last)
                    {
                        int distance =
                            System.Math.Abs(first % width - last % width)
                            + System.Math.Abs(first / width - last / width);
                        reachable[first, last] = selected[first] && selected[last] && distance <= 1;
                    }
                }
                for (int middle = 0; middle < count; ++middle)
                {
                    for (int first = 0; first < count; ++first)
                    {
                        for (int last = 0; last < count; ++last)
                        {
                            reachable[first, last] =
                                reachable[first, last]
                                || (reachable[first, middle] && reachable[middle, last]);
                        }
                    }
                }
                int largest = 0;
                double expectedX = 0;
                double expectedY = 0;
                for (int first = 0; first < count; ++first)
                {
                    int size = 0;
                    int sumX = 0;
                    int sumY = 0;
                    for (int last = 0; last < count; ++last)
                    {
                        if (reachable[first, last])
                        {
                            ++size;
                            sumX += last % width;
                            sumY += last / width;
                        }
                    }
                    if (largest < size)
                    {
                        largest = size;
                        expectedX = (double)sumX / size + 0.5;
                        expectedY = (double)sumY / size + 0.5;
                    }
                }
                SpritePixelRegion pixels = new(
                    colours,
                    width,
                    new RectInt(0, 0, width, width),
                    Vector2.zero,
                    Vector2.one
                );
                Assert.That(
                    SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                        pixels,
                        0f,
                        out Vector2 centre,
                        out string error
                    ),
                    Is.True,
                    error
                );
                Assert.That(centre.x, Is.EqualTo(expectedX).Within(0.000001), $"pattern {pattern}");
                Assert.That(centre.y, Is.EqualTo(expectedY).Within(0.000001), $"pattern {pattern}");
            }
        }
    }
}
