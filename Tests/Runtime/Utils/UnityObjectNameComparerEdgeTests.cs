// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityObjectNameComparerEdgeTests : CommonTestBase
    {
        // Tracking handled by CommonTestBase

        [UnityTest]
        public IEnumerator CompareTreatsOnlyTrailingNumbersAsNumeric()
        {
            GameObject a = Create("Item001");
            GameObject b = Create("Item10");

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(a, b);
            Assert.Less(comparison, 0);
            yield break;
        }

        [UnityTest]
        public IEnumerator CompareFallsBackToCaseInsensitiveWhenNoNumbers()
        {
            GameObject a = Create("alpha");
            GameObject b = Create("Beta");

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(a, b);
            Assert.Less(comparison, 0);
            yield break;
        }

        [UnityTest]
        public IEnumerator CompareOrdersWhenOnlyOneHasTrailingNumber()
        {
            GameObject a = Create("Item");
            GameObject b = Create("Item2");

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(a, b);
            Assert.Less(comparison, 0);
            yield break;
        }

        [UnityTest]
        public IEnumerator CompareOrdersByPrefixBeforeNumeric()
        {
            GameObject a = Create("Item2");
            GameObject b = Create("Another10");

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(a, b);
            Assert.Greater(comparison, 0);
            yield break;
        }

        private static IEnumerable<TestCaseData> UnparsableNumericSuffixes()
        {
            // A millisecond epoch stamp overflows int; a name is not required to fit one.
            yield return new TestCaseData("Save_1755720000000", "Save_1755720000001", -1).SetName(
                "Suffix.ThirteenDigits.OrdersNumerically"
            );
            yield return new TestCaseData("Save_9", "Save_1755720000000", -1).SetName(
                "Suffix.ShortVersusOverflowing.OrdersNumerically"
            );
            yield return new TestCaseData(
                "Chunk_99999999999999999999",
                "Chunk_99999999999999999998",
                1
            ).SetName("Suffix.TwentyDigits.OrdersNumerically");
            // \d matches every Unicode digit; int.Parse accepts only ASCII ones.
            yield return new TestCaseData("Enemy\u0663", "Enemy\u0664", -1).SetName(
                "Suffix.ArabicIndicDigits.OrdersAsText"
            );
            yield return new TestCaseData("Item\uFF13", "Item\uFF13", 0).SetName(
                "Suffix.FullWidthDigits.OrdersAsText"
            );
        }

        [TestCaseSource(nameof(UnparsableNumericSuffixes))]
        public void CompareOrdersNamesNoIntegerCanHold(string first, string second, int expected)
        {
            GameObject a = Create(first);
            GameObject b = Create(second);

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(a, b);

            Assert.AreEqual(expected, Math.Sign(comparison));
            Assert.AreEqual(
                -Math.Sign(comparison),
                Math.Sign(UnityObjectNameComparer<GameObject>.Instance.Compare(b, a))
            );
        }

        [Test]
        public void SortByNameOrdersOverflowingSuffixesWithoutThrowing()
        {
            List<GameObject> objects = new()
            {
                Create("Save_1755720000002"),
                Create("Save_1755720000000"),
                Create("Save_1755720000001"),
            };

            objects.SortByName();

            Assert.AreEqual(
                new[] { "Save_1755720000000", "Save_1755720000001", "Save_1755720000002" },
                objects.Select(gameObject => gameObject.name).ToArray()
            );
        }

        [Test]
        public void CompareTreatsADestroyedObjectAsAbsent()
        {
            GameObject live = Create("Alpha1");
            GameObject destroyed = Create("Alpha2");
            Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: the destroyed reference is the subject

            Assert.Greater(
                UnityObjectNameComparer<GameObject>.Instance.Compare(live, destroyed),
                0
            );
            Assert.Less(UnityObjectNameComparer<GameObject>.Instance.Compare(destroyed, live), 0);
            Assert.AreEqual(
                0,
                UnityObjectNameComparer<GameObject>.Instance.Compare(destroyed, null)
            );
        }

        private GameObject Create(string name)
        {
            return Track(new GameObject(name));
        }
    }
}
