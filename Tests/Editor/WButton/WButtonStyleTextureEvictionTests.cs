// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;

    /// <summary>
    /// The colored style cache owns the 1x1 textures its <see cref="GUIStyle"/> states point at and
    /// destroys them when it evicts an entry. That is the only way to bound it -- dropping the
    /// managed reference alone leaks the native allocation permanently -- but it is also the one
    /// change here that can blank a button if the ownership is not exact.
    /// </summary>
    /// <remarks>
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/701">#701</see>.
    /// Each entry owning its own textures is what makes eviction safe by construction rather than
    /// by coordination: no two entries share one, so evicting either cannot reach the other's
    /// background. These assert exactly that, because arguing it is what the previous attempt did.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WButtonStyleTextureEvictionTests
    {
        [SetUp]
        public void SetUp()
        {
            WButtonStyles.TestHooks.ClearColoredStyleCaches();
        }

        [TearDown]
        public void TearDown()
        {
            WButtonStyles.TestHooks.ClearColoredStyleCaches();
        }

        [Test]
        public void TheCacheStopsAtItsBound()
        {
            int bound = WButtonStyles.TestHooks.MaxColoredButtonStyleCount;
            Assert.Greater(bound, 0, "the bound must be positive for this to measure anything");

            for (int index = 0; index < bound * 2; ++index)
            {
                _ = WButtonStyles.GetColoredButtonStyle(DistinctColor(index), Color.white);
            }

            Assert.AreEqual(bound, WButtonStyles.TestHooks.ColoredButtonStyleCount);
        }

        /// <summary>
        /// The entry a caller just asked for is renewed as most recently used, so the churn that
        /// evicts everything else cannot reach it -- which is why a style fetched inside one OnGUI
        /// pass is safe to draw with.
        /// </summary>
        [Test]
        public void ChurnDoesNotDestroyTheTexturesOfARenewedEntry()
        {
            Color kept = new(0.125f, 0.25f, 0.375f, 1f);
            GUIStyle style = WButtonStyles.GetColoredButtonStyle(kept, Color.white);
            Texture2D background = style.normal.background;
            Assert.IsTrue(background != null, "the probe must have had a live background");

            int bound = WButtonStyles.TestHooks.MaxColoredButtonStyleCount;
            GUIStyle firstChurned = WButtonStyles.GetColoredButtonStyle(
                DistinctColor(0),
                Color.white
            );
            Texture2D firstChurnedBackground = firstChurned.normal.background;

            for (int index = 1; index < bound + 2; ++index)
            {
                _ = WButtonStyles.GetColoredButtonStyle(DistinctColor(index), Color.white);
                _ = WButtonStyles.GetColoredButtonStyle(kept, Color.white);
            }

            Assert.IsTrue(
                firstChurnedBackground == null,
                "the churn evicted nothing, so surviving proves nothing"
            );
            Assert.IsTrue(
                background != null,
                "the renewed entry's texture was destroyed while its style was still cached"
            );
            Assert.AreSame(style, WButtonStyles.GetColoredButtonStyle(kept, Color.white));
        }

        /// <summary>
        /// Eviction has to destroy the native texture, not merely drop the reference: an unreachable
        /// 1x1 that nothing can release is a worse outcome than the unbounded cache this replaced.
        /// </summary>
        [Test]
        public void EvictionDestroysTheTexturesTheEntryOwned()
        {
            Color evicted = new(0.0625f, 0.125f, 0.1875f, 1f);
            GUIStyle style = WButtonStyles.GetColoredButtonStyle(evicted, Color.white);
            Texture2D background = style.normal.background;
            Assert.IsTrue(background != null, "the probe must have had a live background");

            int bound = WButtonStyles.TestHooks.MaxColoredButtonStyleCount;
            for (int index = 0; index < bound + 1; ++index)
            {
                _ = WButtonStyles.GetColoredButtonStyle(DistinctColor(index), Color.white);
            }

            Assert.AreEqual(
                bound,
                WButtonStyles.TestHooks.ColoredButtonStyleCount,
                "the churn never reached the bound, so nothing was evicted"
            );
            Assert.IsTrue(background == null, "the evicted entry's texture outlived its entry");
        }

        [Test]
        public void ClearingDestroysEveryTextureItHeld()
        {
            GUIStyle style = WButtonStyles.GetColoredButtonStyle(
                new Color(0.5f, 0.25f, 0.75f, 1f),
                Color.white
            );
            Texture2D background = style.normal.background;
            Assert.IsTrue(background != null, "the probe must have had a live background");
            Assert.AreEqual(1, WButtonStyles.TestHooks.ColoredButtonStyleCount);

            WButtonStyles.TestHooks.ClearColoredStyleCaches();

            Assert.AreEqual(0, WButtonStyles.TestHooks.ColoredButtonStyleCount);
            Assert.IsTrue(background == null, "Clear dropped a texture without destroying it");
        }

        /*
            Spread probes across color channels so quantization cannot collapse them and prevent the cache from
            filling.
        */
        private static Color DistinctColor(int index)
        {
            return new Color(
                (index % 32) / 32f,
                ((index / 32) % 32) / 32f,
                ((index / 1024) % 32) / 32f,
                1f
            );
        }
    }
}
#endif
