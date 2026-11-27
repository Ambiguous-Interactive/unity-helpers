// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class GoldenImageTests
    {
        private static Color32[] Pixels(params Color32[] pixels)
        {
            return pixels;
        }

        [Test]
        public void IdenticalImagesHaveNoMismatches()
        {
            Color32[] image = Pixels(new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255));

            Assert.AreEqual(0, GoldenImage.CountMismatches(image, image, 0));
        }

        [Test]
        public void DifferenceAtToleranceIsAccepted()
        {
            Color32[] expected = Pixels(new Color32(10, 10, 10, 255));
            Color32[] actual = Pixels(new Color32(15, 10, 10, 255));

            Assert.AreEqual(0, GoldenImage.CountMismatches(expected, actual, 5));
            Assert.AreEqual(1, GoldenImage.CountMismatches(expected, actual, 4));
        }

        [Test]
        public void LengthDifferenceCountsAsMismatches()
        {
            Color32[] expected = Pixels(new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255));
            Color32[] actual = Pixels(new Color32(0, 0, 0, 255));

            Assert.AreEqual(1, GoldenImage.CountMismatches(expected, actual, 0));
            Assert.AreEqual(1, GoldenImage.CountMismatches(actual, expected, 0));
        }

        [Test]
        public void FlatRegionBeyondToleranceIsCounted()
        {
            Color32[] expected = Pixels(
                new Color32(32, 32, 32, 255),
                new Color32(32, 32, 32, 255),
                new Color32(32, 32, 32, 255)
            );
            Color32[] actual = Pixels(
                new Color32(32, 32, 32, 255),
                new Color32(64, 32, 32, 255),
                new Color32(32, 32, 32, 255)
            );

            Assert.AreEqual(
                1,
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 3, 0.5f)
            );
        }

        [Test]
        public void EdgePeakWithinFractionIsForgiven()
        {
            Color32[] expected = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(255, 255, 255, 255),
                new Color32(0, 0, 0, 255)
            );
            Color32[] actual = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(200, 200, 200, 255),
                new Color32(0, 0, 0, 255)
            );

            Assert.AreEqual(1, GoldenImage.CountMismatches(expected, actual, 0));
            Assert.AreEqual(
                0,
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 3, 0.25f)
            );
        }

        [Test]
        public void EdgePeakBeyondFractionIsCounted()
        {
            Color32[] expected = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(255, 255, 255, 255),
                new Color32(0, 0, 0, 255)
            );
            Color32[] actual = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(200, 200, 200, 255),
                new Color32(0, 0, 0, 255)
            );

            Assert.AreEqual(
                1,
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 3, 0.1f)
            );
        }

        [Test]
        public void InvalidWidthFallsBackToExactCount()
        {
            Color32[] expected = Pixels(new Color32(0, 0, 0, 255));
            Color32[] actual = Pixels(new Color32(10, 0, 0, 255));

            Assert.AreEqual(
                1,
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 0, 0.5f)
            );
        }

        [Test]
        public void ZeroFractionMatchesExactCount()
        {
            Color32[] expected = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(255, 255, 255, 255),
                new Color32(0, 0, 0, 255)
            );
            Color32[] actual = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(200, 200, 200, 255),
                new Color32(0, 0, 0, 255)
            );

            Assert.AreEqual(
                GoldenImage.CountMismatches(expected, actual, 0),
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 3, 0f)
            );
        }

        [Test]
        public void PartialFinalRowDoesNotThrow()
        {
            Color32[] expected = Pixels(
                new Color32(0, 0, 0, 255),
                new Color32(255, 255, 255, 255),
                new Color32(0, 0, 0, 255)
            );
            Color32[] actual = Pixels(
                new Color32(10, 0, 0, 255),
                new Color32(255, 255, 255, 255),
                new Color32(0, 0, 0, 255)
            );

            Assert.DoesNotThrow(() =>
            {
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 2, 2, 0.25f);
            });
        }

        [Test]
        public void ExplicitDimensionsLargerThanArrayDoNotThrow()
        {
            Color32[] expected = Pixels(new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255));
            Color32[] actual = Pixels(new Color32(10, 0, 0, 255), new Color32(255, 255, 255, 255));

            Assert.DoesNotThrow(() =>
            {
                GoldenImage.CountEdgeTolerantMismatches(expected, actual, 0, 3, 3, 0.25f);
            });
        }

        [Test]
        public void NullImagesAreHandled()
        {
            Assert.AreEqual(0, GoldenImage.CountMismatches(null, null, 0));
            Assert.AreEqual(
                1,
                GoldenImage.CountMismatches(null, Pixels(new Color32(0, 0, 0, 255)), 0)
            );
        }
    }
}
