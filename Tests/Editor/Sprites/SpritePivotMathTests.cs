// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;

    [TestFixture]
    public sealed class SpritePivotMathTests
    {
        [TestCase(2f, 3f, 8f, 12f, 0.25f, 0.25f)]
        [TestCase(0f, 0f, 8f, 12f, 0f, 0f)]
        [TestCase(8f, 12f, 8f, 12f, 1f, 1f)]
        [TestCase(-8f, 24f, 8f, 12f, -1f, 2f)]
        public void NormalizesEachAxisWithoutClampingAuthoredPivots(
            float x,
            float y,
            float width,
            float height,
            float expectedX,
            float expectedY
        )
        {
            Assert.IsTrue(
                SpritePivotCopierAPI.TryNormalizePivot(
                    new Vector2(x, y),
                    new Vector2(width, height),
                    out Vector2 result
                )
            );
            Assert.AreEqual(new Vector2(expectedX, expectedY), result);
        }

        [TestCase(0f, 1f, 0f, 1f)]
        [TestCase(0f, 1f, 1f, 0f)]
        [TestCase(0f, 1f, -1f, 1f)]
        [TestCase(float.NaN, 1f, 1f, 1f)]
        [TestCase(0f, float.PositiveInfinity, 1f, 1f)]
        [TestCase(0f, 1f, float.PositiveInfinity, 1f)]
        [TestCase(0f, 1f, 1f, float.NaN)]
        [TestCase(float.MaxValue, 1f, float.Epsilon, 1f)]
        public void RefusesMalformedOrUnrepresentableCoordinates(
            float x,
            float y,
            float width,
            float height
        )
        {
            Assert.IsFalse(
                SpritePivotCopierAPI.TryNormalizePivot(
                    new Vector2(x, y),
                    new Vector2(width, height),
                    out Vector2 result
                )
            );
            Assert.AreEqual(Vector2.zero, result);
        }
    }
}
