// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpriteBoundaryTracerTests
    {
        private static SpriteAlphaMask Parse(string input, int width)
        {
            bool[] pixels = new bool[input.Length];
            for (int index = 0; index < input.Length; ++index)
            {
                pixels[index] = input[index] == '1';
            }
            return new SpriteAlphaMask(
                pixels,
                width,
                input.Length / width,
                Vector2.zero,
                Vector2.one
            );
        }

        private static double SignedArea(Vector2[] path)
        {
            double area = 0;
            Vector2 previous = path[path.Length - 1];
            foreach (Vector2 point in path)
            {
                area += (double)previous.x * point.y - (double)point.x * previous.y;
                previous = point;
            }
            return area * 0.5;
        }

        private static bool Contains(Vector2[] path, Vector2 point)
        {
            bool inside = false;
            Vector2 previous = path[path.Length - 1];
            foreach (Vector2 current in path)
            {
                if (
                    (point.y < current.y) != (point.y < previous.y)
                    && point.x
                        < (previous.x - current.x)
                            * (point.y - current.y)
                            / (previous.y - current.y)
                            + current.x
                )
                {
                    inside = !inside;
                }
                previous = current;
            }
            return inside;
        }

        [TestCase(1, 1)]
        [TestCase(5, 3)]
        [TestCase(3840, 2160)]
        public void SolidRectangleHasFourExactCorners(int width, int height)
        {
            bool[] pixels = new bool[width * height];
            Array.Fill(pixels, true);
            SpriteAlphaMask mask = new(
                pixels,
                width,
                height,
                new Vector2(0.5f, 1),
                new Vector2(0.25f, 0.5f)
            );
            Assert.IsTrue(SpriteBoundaryTracer.TryTrace(mask, out List<Vector2[]> paths));
            Assert.AreEqual(1, paths.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(-0.125f, -0.5f),
                    new Vector2((width - 0.5f) * 0.25f, -0.5f),
                    new Vector2((width - 0.5f) * 0.25f, (height - 1) * 0.5f),
                    new Vector2(-0.125f, (height - 1) * 0.5f),
                },
                paths[0]
            );
        }

        [TestCase("000000000", 0, 0)]
        [TestCase("111101111", 2, 8)]
        [TestCase("100010001", 1, 3)]
        [TestCase("101000101", 4, 4)]
        [TestCase("111101110", 2, 7)]
        public void TracesHolesIslandsAndDiagonalSaddles(string input, int expectedPaths, int area)
        {
            SpriteAlphaMask mask = Parse(input, 3);
            Assert.IsTrue(SpriteBoundaryTracer.TryTrace(mask, out List<Vector2[]> paths));
            Assert.AreEqual(expectedPaths, paths.Count);
            double totalArea = 0;
            foreach (Vector2[] path in paths)
            {
                totalArea += SignedArea(path);
                Assert.LessOrEqual(4, path.Length);
            }
            Assert.AreEqual(area, totalArea, 0.00001);
        }

        [TestCase(0, 2)]
        [TestCase(1, 2)]
        [TestCase(1.01f, 1)]
        [TestCase(9, 1)]
        [TestCase(9.01f, 0)]
        public void MinimumAreaFiltersOuterPathsAndHoles(float minimumArea, int expectedPaths)
        {
            Assert.IsTrue(
                SpriteBoundaryTracer.TryTrace(
                    Parse("111101111", 3),
                    out List<Vector2[]> paths,
                    minimumArea
                )
            );
            Assert.AreEqual(expectedPaths, paths.Count);
        }

        [Test]
        public void ExhaustiveSmallMasksPreserveAreaAndEveryPixelCenter()
        {
            for (int pattern = 0; pattern < 512; ++pattern)
            {
                bool[] pixels = new bool[9];
                int expectedArea = 0;
                for (int index = 0; index < pixels.Length; ++index)
                {
                    pixels[index] = (pattern & (1 << index)) != 0;
                    if (pixels[index])
                    {
                        ++expectedArea;
                    }
                }
                SpriteAlphaMask mask = new(pixels, 3, 3, Vector2.zero, Vector2.one);
                Assert.IsTrue(
                    SpriteBoundaryTracer.TryTrace(mask, out List<Vector2[]> paths),
                    $"Pattern {pattern}"
                );
                double area = 0;
                foreach (Vector2[] path in paths)
                {
                    area += SignedArea(path);
                }
                Assert.AreEqual(expectedArea, area, 0.00001, $"Pattern {pattern}");
                for (int index = 0; index < pixels.Length; ++index)
                {
                    Vector2 point = new(index % 3 + 0.5f, index / 3 + 0.5f);
                    bool inside = false;
                    foreach (Vector2[] path in paths)
                    {
                        inside ^= Contains(path, point);
                    }
                    Assert.AreEqual(pixels[index], inside, $"Pattern {pattern}, pixel {index}");
                }
            }
        }

        [TestCase(-1)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void RejectsInvalidArea(float minimumArea)
        {
            Assert.IsFalse(
                SpriteBoundaryTracer.TryTrace(Parse("1", 1), out List<Vector2[]> paths, minimumArea)
            );
            Assert.AreEqual(0, paths.Count);
        }

        [Test]
        public void RejectsMissingMismatchedOverflowingAndNonfiniteMasks()
        {
            SpriteAlphaMask[] masks =
            {
                default,
                new(null, 1, 1, Vector2.zero, Vector2.one),
                new(new bool[1], int.MaxValue, int.MaxValue, Vector2.zero, Vector2.one),
                new(new bool[1], -1, -1, Vector2.zero, Vector2.one),
                new(new bool[1], 1, 1, Vector2.zero, Vector2.zero),
                new(new bool[1], 1, 1, new Vector2(float.NaN, 0), Vector2.one),
                new(new[] { true }, 1, 1, new Vector2(1e20f, 1e20f), Vector2.one),
                new(
                    new[] { true },
                    1,
                    1,
                    new Vector2(float.MaxValue, 0),
                    new Vector2(float.MaxValue, 1)
                ),
            };
            foreach (SpriteAlphaMask mask in masks)
            {
                Assert.IsFalse(SpriteBoundaryTracer.TryTrace(mask, out List<Vector2[]> paths));
                Assert.AreEqual(0, paths.Count);
            }
        }
    }
}
