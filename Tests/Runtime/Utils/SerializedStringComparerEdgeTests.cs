// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializedStringComparerEdgeTests
    {
        // A comparer reached through a serialized field must not throw at lookup time.
        [Test]
        public void GetHashCodeHandlesNullForAllModes()
        {
            SerializedStringComparer.StringCompareMode[] allModes = Enum.GetValues(
                    typeof(SerializedStringComparer.StringCompareMode)
                )
                .OfType<SerializedStringComparer.StringCompareMode>()
                .ToArray();

            foreach (SerializedStringComparer.StringCompareMode mode in allModes)
            {
                SerializedStringComparer comparer = new(mode);
                Assert.AreEqual(0, comparer.GetHashCode(null), mode.ToString());
            }
        }

        [Test]
        public void EqualsNullHandlingConsistentAcrossModes()
        {
            SerializedStringComparer.StringCompareMode[] allModes = Enum.GetValues(
                    typeof(SerializedStringComparer.StringCompareMode)
                )
                .OfType<SerializedStringComparer.StringCompareMode>()
                .ToArray();

            foreach (SerializedStringComparer.StringCompareMode mode in allModes)
            {
                SerializedStringComparer comparer = new(mode);
                Assert.IsTrue(comparer.Equals(null, null), mode.ToString());
                Assert.IsFalse(comparer.Equals("a", null), mode.ToString());
                Assert.IsFalse(comparer.Equals(null, "a"), mode.ToString());
            }
        }

        /// <summary>
        /// A dictionary's keys are already in buckets chosen by whatever rule the comparer applied
        /// when they went in, so changing the mode afterwards makes them unreachable rather than
        /// re-sorting them. <c>Freeze</c> pins the rule so a later write cannot do that
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/646">#646</see>).
        /// </summary>
        [Test]
        public void FreezePinsTheComparisonRule()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase
            );
            Assert.IsFalse(comparer.IsFrozen, "a fresh comparer is not frozen");

            Dictionary<string, int> byName = new(comparer.Freeze()) { ["Alpha"] = 1 };
            Assert.IsTrue(comparer.IsFrozen, "Freeze did not mark the comparer frozen");

            comparer.compareMode = SerializedStringComparer.StringCompareMode.Ordinal;

            Assert.IsTrue(
                comparer.Equals("alpha", "ALPHA"),
                "a frozen comparer followed the new mode"
            );
            Assert.IsTrue(
                byName.TryGetValue("alpha", out int found),
                "the dictionary lost a key after the mode changed"
            );
            Assert.AreEqual(1, found);

            Assert.DoesNotThrow(() => comparer.Freeze());
            Assert.IsTrue(comparer.Equals("alpha", "ALPHA"), "re-freezing changed the pinned rule");
        }

        /// <summary>
        /// An unfrozen comparer still honours a mode change, which is what makes it authorable in
        /// the Inspector before anything is built with it.
        /// </summary>
        [Test]
        public void AnUnfrozenComparerFollowsItsMode()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.Ordinal
            );

            Assert.IsFalse(comparer.Equals("alpha", "ALPHA"));

            comparer.compareMode = SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase;

            Assert.IsTrue(comparer.Equals("alpha", "ALPHA"));
            Assert.AreEqual(comparer.GetHashCode("alpha"), comparer.GetHashCode("ALPHA"));
        }
    }
}
