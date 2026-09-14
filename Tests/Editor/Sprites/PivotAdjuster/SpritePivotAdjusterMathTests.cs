// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpritePivotAdjusterMathTests
    {
        [TestCase(255, 257, false, TestName = "ParallelPolicy.BelowThreshold.IsSequential")]
        [TestCase(256, 256, true, TestName = "ParallelPolicy.AtThreshold.IsParallel")]
        [TestCase(65_536, 1, false, TestName = "ParallelPolicy.SingleRow.IsSequential")]
        [TestCase(32_768, 2, true, TestName = "ParallelPolicy.TwoRowsAtThreshold.IsParallel")]
        [TestCase(0, 256, false, TestName = "ParallelPolicy.ZeroWidth.IsSequential")]
        [TestCase(-1, 256, false, TestName = "ParallelPolicy.NegativeWidth.IsSequential")]
        public void ParallelPolicyMatchesMeasuredBoundary(int width, int height, bool expected)
        {
            Assert.AreEqual(
                expected,
                SpritePivotAdjuster.ShouldCalculateCenterOfMassInParallel(width, height)
            );
        }

        [Test]
        public void Color32SequentialAndParallelPathsMatch()
        {
            const int width = 256;
            const int height = 256;
            Color32[] pixels = new Color32[width * height];
            pixels[0] = new Color32(255, 255, 255, 255);
            pixels[pixels.Length - 1] = new Color32(255, 255, 255, 255);

            Vector2 sequential = SpritePivotAdjuster.CalculateCenterOfMass(
                pixels,
                width,
                height,
                0,
                parallel: false
            );
            Vector2 parallel = SpritePivotAdjuster.CalculateCenterOfMass(
                pixels,
                width,
                height,
                0,
                parallel: true
            );

            Vector2 expected = new(127.5f / width, 127.5f / height);
            Assert.AreEqual(expected, sequential);
            Assert.AreEqual(sequential, parallel);
        }

        [Test]
        public void ColorSequentialAndParallelPathsMatch()
        {
            const int width = 256;
            const int height = 256;
            Color[] pixels = new Color[width * height];
            pixels[width + 2] = new Color(1f, 1f, 1f, 0.25f);
            pixels[(height - 2) * width + width - 3] = Color.white;

            Vector2 sequential = SpritePivotAdjuster.CalculateCenterOfMass(
                pixels,
                width,
                height,
                0.1f,
                parallel: false
            );
            Vector2 parallel = SpritePivotAdjuster.CalculateCenterOfMass(
                pixels,
                width,
                height,
                0.1f,
                parallel: true
            );

            Vector2 expected = new(127.5f / width, 127.5f / height);
            Assert.AreEqual(expected, sequential);
            Assert.AreEqual(sequential, parallel);
        }

        [Test]
        public void InvalidPixelShapesReturnCenter()
        {
            Vector2 expected = new(0.5f, 0.5f);

            Assert.AreEqual(
                expected,
                SpritePivotAdjuster.CalculateCenterOfMass((Color32[])null, 1, 1, 0, parallel: false)
            );
            Assert.AreEqual(
                expected,
                SpritePivotAdjuster.CalculateCenterOfMass(new Color[3], 2, 2, 0f, parallel: true)
            );
        }
    }
#endif
}
