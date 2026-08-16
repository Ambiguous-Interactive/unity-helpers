// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Core.Helper
{
#if UNITY_EDITOR

    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EditorCacheHelperColorComparerTests
    {
        /// <remarks>
        /// This exact pair is what broke the old comparer. The channels sit a couple of ULPs apart
        /// either side of 127.5, so <see cref="EditorCacheHelper.AreColorsEqual"/> - a float
        /// comparison - calls them equal while the hash, which quantizes, puts them in different
        /// buckets. A dictionary keyed that way holds two entries for a key it considers single.
        /// Widening the pair to anything <c>Mathf.Approximately</c> rejects makes this test pass
        /// against the defect, so the first assertion pins the property the fixture depends on.
        /// </remarks>
        [Test]
        public void ColorsEitherSideOfAChannelBoundaryAreDistinctKeys()
        {
            EditorCacheHelper.ColorComparer comparer = new();
            Color below = new(0.5f - 1e-7f, 0f, 0f, 1f);
            Color above = new(0.5f + 1e-7f, 0f, 0f, 1f);

            Assert.IsTrue(
                EditorCacheHelper.AreColorsEqual(below, above),
                "The fixture no longer reproduces the pair the float comparison called equal."
            );
            Assert.AreNotEqual(
                ColorQuantization.ToByte(below.r),
                ColorQuantization.ToByte(above.r),
                "The fixture no longer straddles a channel boundary."
            );

            Assert.IsFalse(comparer.Equals(below, above));
            Assert.AreNotEqual(comparer.GetHashCode(below), comparer.GetHashCode(above));

            Dictionary<Color, int> cache = new(comparer) { [below] = 1, [above] = 2 };
            Assert.AreEqual(2, cache.Count);
        }

        /// <remarks>
        /// Exhaustive rather than random: the pair that violates the contract has to straddle a
        /// channel boundary while staying inside <c>Mathf.Approximately</c>'s tolerance, and a
        /// uniform fuzz finds roughly three of those in two million draws. Walking the 255 boundaries
        /// directly hits every one of them.
        /// </remarks>
        [Test]
        public void EqualityAndHashingAgreeAtEveryChannelBoundary()
        {
            EditorCacheHelper.ColorComparer comparer = new();
            int straddling = 0;
            for (int channel = 0; channel < byte.MaxValue; ++channel)
            {
                float boundary = (channel + 0.5f) / 255f;
                Color below = new(NextDown(boundary), 0f, 0f, 1f);
                Color above = new(NextUp(boundary), 0f, 0f, 1f);

                if (ColorQuantization.ToByte(below.r) == ColorQuantization.ToByte(above.r))
                {
                    continue;
                }

                ++straddling;
                Assert.IsTrue(
                    EditorCacheHelper.AreColorsEqual(below, above),
                    $"Boundary {channel} left the tolerance that makes this case interesting."
                );
                Assert.IsFalse(
                    comparer.Equals(below, above),
                    $"Boundary {channel} compared equal across a channel it does not share."
                );
                Assert.AreNotEqual(
                    comparer.GetHashCode(below),
                    comparer.GetHashCode(above),
                    $"Boundary {channel} hashed together across a channel it does not share."
                );
            }

            Assert.AreEqual(
                byte.MaxValue,
                straddling,
                "Every channel boundary should have produced a straddling pair."
            );
        }

        [Test]
        public void ColorsSharingEveryQuantizedChannelShareOneEntry()
        {
            EditorCacheHelper.ColorComparer comparer = new();
            Color a = new(0.5f, 0.25f, 0.75f, 1f);
            Color b = new(0.5f + 1e-4f, 0.25f - 1e-4f, 0.75f, 1f);

            Assert.IsTrue(comparer.Equals(a, b));
            Assert.AreEqual(comparer.GetHashCode(a), comparer.GetHashCode(b));

            Dictionary<Color, int> cache = new(comparer) { [a] = 1 };
            cache[b] = 2;
            Assert.AreEqual(1, cache.Count);
            Assert.AreEqual(2, cache[a]);
        }

        private static float NextUp(float value)
        {
            return value + Mathf.Max(Mathf.Abs(value), 1e-6f) * 4e-7f;
        }

        private static float NextDown(float value)
        {
            return value - Mathf.Max(Mathf.Abs(value), 1e-6f) * 4e-7f;
        }
    }

#endif
}
