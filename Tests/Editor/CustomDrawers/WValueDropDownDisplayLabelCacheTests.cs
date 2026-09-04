// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System.Globalization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;

    /// <summary>
    /// Each entry holds the option array it was built from, so an unbounded cache keeps every
    /// object any dropdown ever offered alive for the life of the editor process.
    /// </summary>
    /// <remarks>
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/701">#701</see>
    /// records why this went unwritten before: <c>GetOrCreateDisplayLabels</c> was private with no
    /// seam and the cache had no count, so a test could only compare
    /// <c>BuildDisplayLabelsUncached</c> against itself and pass whether or not eviction happened.
    /// The seam now exists, so the bound is measured rather than argued.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WValueDropDownDisplayLabelCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            WValueDropDownDrawer.TestHooks.ClearCaches();
        }

        [TearDown]
        public void TearDown()
        {
            WValueDropDownDrawer.TestHooks.ClearCaches();
        }

        [Test]
        public void TheDisplayLabelCacheStopsAtItsBound()
        {
            int bound = WValueDropDownDrawer.TestHooks.MaxDisplayLabelsCacheCount;
            Assert.Greater(bound, 0, "the bound must be positive for this to measure anything");

            for (int index = 0; index < bound * 2; ++index)
            {
                _ = WValueDropDownDrawer.TestHooks.GetOrCreateDisplayLabels(
                    PropertyPath(index),
                    new object[] { index }
                );
            }

            Assert.AreEqual(bound, WValueDropDownDrawer.TestHooks.DisplayLabelsCacheCount);
        }

        /// <summary>
        /// Eviction may only cost the rebuild. A path whose entry was dropped must still answer
        /// with the labels its options describe, because the drawer asks again on the next repaint
        /// and a stale or empty answer would render the wrong option text.
        /// </summary>
        [Test]
        public void AnEvictedPathStillAnswersWithItsOwnLabels()
        {
            object[] options = { "alpha", "beta" };
            string[] before = WValueDropDownDrawer.TestHooks.GetOrCreateDisplayLabels(
                PropertyPath(-1),
                options
            );
            Assert.IsNotEmpty(before, "the probe must have produced labels to compare against");

            int bound = WValueDropDownDrawer.TestHooks.MaxDisplayLabelsCacheCount;
            for (int index = 0; index < bound + 1; ++index)
            {
                _ = WValueDropDownDrawer.TestHooks.GetOrCreateDisplayLabels(
                    PropertyPath(index),
                    new object[] { index }
                );
            }

            Assert.AreEqual(
                bound,
                WValueDropDownDrawer.TestHooks.DisplayLabelsCacheCount,
                "the churn never reached the bound, so nothing was evicted"
            );

            string[] after = WValueDropDownDrawer.TestHooks.GetOrCreateDisplayLabels(
                PropertyPath(-1),
                options
            );
            CollectionAssert.AreEqual(before, after);
        }

        private static string PropertyPath(int index)
        {
            return "probe." + index.ToString(CultureInfo.InvariantCulture);
        }
    }
#endif
}
