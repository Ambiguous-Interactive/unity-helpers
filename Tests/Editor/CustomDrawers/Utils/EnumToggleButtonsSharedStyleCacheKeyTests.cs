// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EnumToggleButtonsSharedStyleCacheKeyTests
    {
        /// <remarks>
        /// The key used to compare colors with <c>Mathf.Approximately</c> while hashing the raw
        /// float channels. Approximate equality is not transitive and cannot back any hash, so the
        /// pair below - two colors a couple of ULPs apart either side of the 127/128 boundary -
        /// compared equal and hashed apart, which is a broken equality contract and lets the style
        /// cache hold two entries for a key it considers single.
        /// </remarks>
        [Test]
        public void KeysEitherSideOfAChannelBoundaryAreDistinct()
        {
            Color below = new(0.5f - 1e-7f, 0f, 0f, 1f);
            Color above = new(0.5f + 1e-7f, 0f, 0f, 1f);

            Assert.AreNotEqual(
                ColorQuantization.ToByte(below.r),
                ColorQuantization.ToByte(above.r),
                "The fixture no longer straddles a channel boundary."
            );

            EnumToggleButtonsShared.ButtonStyleCacheKey first = new(
                EnumToggleButtonsShared.ButtonSegment.Left,
                true,
                below,
                Color.black,
                Color.gray,
                Color.white
            );
            EnumToggleButtonsShared.ButtonStyleCacheKey second = new(
                EnumToggleButtonsShared.ButtonSegment.Left,
                true,
                above,
                Color.black,
                Color.gray,
                Color.white
            );

            Assert.IsFalse(first.Equals(second));
            Assert.AreNotEqual(first.GetHashCode(), second.GetHashCode());

            Dictionary<EnumToggleButtonsShared.ButtonStyleCacheKey, int> cache = new(
                new EnumToggleButtonsShared.ButtonStyleCacheKeyComparer()
            )
            {
                [first] = 1,
                [second] = 2,
            };
            Assert.AreEqual(2, cache.Count);
        }

        /// <remarks>
        /// Exhaustive over the channel boundaries rather than random, for the reason given in
        /// <c>EditorCacheHelperColorComparerTests</c>: a uniform fuzz finds a straddling pair a
        /// handful of times in millions of draws, while walking the 255 boundaries hits every one.
        /// Each of the four colors is swept independently so no channel is left untested.
        /// </remarks>
        [Test]
        public void EqualityAndHashingAgreeAtEveryChannelBoundary()
        {
            int straddling = 0;
            for (int slot = 0; slot < 4; ++slot)
            {
                for (int channel = 0; channel < byte.MaxValue; ++channel)
                {
                    float boundary = (channel + 0.5f) / 255f;
                    Color below = new(NextDown(boundary), 0.25f, 0.75f, 1f);
                    Color above = new(NextUp(boundary), 0.25f, 0.75f, 1f);
                    if (ColorQuantization.ToByte(below.r) == ColorQuantization.ToByte(above.r))
                    {
                        continue;
                    }

                    ++straddling;
                    EnumToggleButtonsShared.ButtonStyleCacheKey first = BuildKey(slot, below);
                    EnumToggleButtonsShared.ButtonStyleCacheKey second = BuildKey(slot, above);

                    Assert.IsFalse(
                        first.Equals(second),
                        $"Slot {slot} boundary {channel} compared equal across a channel it does not share."
                    );
                    Assert.AreNotEqual(
                        first.GetHashCode(),
                        second.GetHashCode(),
                        $"Slot {slot} boundary {channel} hashed together across a channel it does not share."
                    );
                }
            }

            Assert.AreEqual(
                4 * byte.MaxValue,
                straddling,
                "Every channel boundary should have produced a straddling pair in every colour slot."
            );
        }

        [Test]
        public void EqualKeysAlwaysHashTogether()
        {
            System.Random random = new(20260816);
            for (int iteration = 0; iteration < 20000; ++iteration)
            {
                Color selectedBackground = NextColor(random);
                Color selectedText = NextColor(random);
                Color inactiveBackground = NextColor(random);
                Color inactiveText = NextColor(random);

                EnumToggleButtonsShared.ButtonStyleCacheKey first = new(
                    EnumToggleButtonsShared.ButtonSegment.Middle,
                    false,
                    selectedBackground,
                    selectedText,
                    inactiveBackground,
                    inactiveText
                );
                EnumToggleButtonsShared.ButtonStyleCacheKey second = new(
                    EnumToggleButtonsShared.ButtonSegment.Middle,
                    false,
                    Nudge(selectedBackground, random),
                    Nudge(selectedText, random),
                    Nudge(inactiveBackground, random),
                    Nudge(inactiveText, random)
                );

                if (!first.Equals(second))
                {
                    continue;
                }

                Assert.AreEqual(
                    first.GetHashCode(),
                    second.GetHashCode(),
                    $"Iteration {iteration} produced equal keys with different hash codes."
                );
            }
        }

        [Test]
        public void DifferentSegmentsAndStatesRemainDistinct()
        {
            EnumToggleButtonsShared.ButtonStyleCacheKey left = new(
                EnumToggleButtonsShared.ButtonSegment.Left,
                true,
                Color.red,
                Color.white,
                Color.gray,
                Color.black
            );
            EnumToggleButtonsShared.ButtonStyleCacheKey right = new(
                EnumToggleButtonsShared.ButtonSegment.Right,
                true,
                Color.red,
                Color.white,
                Color.gray,
                Color.black
            );
            EnumToggleButtonsShared.ButtonStyleCacheKey inactive = new(
                EnumToggleButtonsShared.ButtonSegment.Left,
                false,
                Color.red,
                Color.white,
                Color.gray,
                Color.black
            );

            Assert.IsFalse(left.Equals(right));
            Assert.IsFalse(left.Equals(inactive));
        }

        [Test]
        public void AlphaDifferencesProduceDistinctKeys()
        {
            EnumToggleButtonsShared.ButtonStyleCacheKey opaque = new(
                EnumToggleButtonsShared.ButtonSegment.Single,
                true,
                new Color(0.3f, 0.6f, 0.9f, 1f),
                Color.black,
                Color.gray,
                Color.white
            );
            EnumToggleButtonsShared.ButtonStyleCacheKey translucent = new(
                EnumToggleButtonsShared.ButtonSegment.Single,
                true,
                new Color(0.3f, 0.6f, 0.9f, 0.5f),
                Color.black,
                Color.gray,
                Color.white
            );

            Assert.IsFalse(opaque.Equals(translucent));
            Assert.AreNotEqual(opaque.GetHashCode(), translucent.GetHashCode());
        }

        private static EnumToggleButtonsShared.ButtonStyleCacheKey BuildKey(int slot, Color color)
        {
            Color selectedBackground = slot == 0 ? color : Color.red;
            Color selectedText = slot == 1 ? color : Color.green;
            Color inactiveBackground = slot == 2 ? color : Color.blue;
            Color inactiveText = slot == 3 ? color : Color.yellow;
            return new EnumToggleButtonsShared.ButtonStyleCacheKey(
                EnumToggleButtonsShared.ButtonSegment.Middle,
                true,
                selectedBackground,
                selectedText,
                inactiveBackground,
                inactiveText
            );
        }

        private static Color NextColor(System.Random random)
        {
            return new Color(
                (float)random.NextDouble(),
                (float)random.NextDouble(),
                (float)random.NextDouble(),
                (float)random.NextDouble()
            );
        }

        private static Color Nudge(Color color, System.Random random)
        {
            float scale = (float)((random.NextDouble() - 0.5) * 4e-7);
            return new Color(
                color.r + (scale * color.r),
                color.g + (scale * color.g),
                color.b + (scale * color.b),
                color.a + (scale * color.a)
            );
        }

        private static float NextUp(float value)
        {
            return value + (Mathf.Max(Mathf.Abs(value), 1e-6f) * 4e-7f);
        }

        private static float NextDown(float value)
        {
            return value - (Mathf.Max(Mathf.Abs(value), 1e-6f) * 4e-7f);
        }
    }
#endif
}
