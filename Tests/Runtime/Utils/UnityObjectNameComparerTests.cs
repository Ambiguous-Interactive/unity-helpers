// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityObjectNameComparerTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator CompareOrdersByNaturalNumberSuffix()
        {
            GameObject first = Create("Item2");
            GameObject second = Create("Item10");

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(first, second);

            Assert.Less(comparison, 0);
            Assert.Greater(UnityObjectNameComparer<GameObject>.Instance.Compare(second, first), 0);
            yield break;
        }

        [UnityTest]
        public IEnumerator CompareBreaksTiesUsingInstanceId()
        {
            GameObject first = Create("Shared");
            GameObject second = Create("Shared");

            int comparison = UnityObjectNameComparer<GameObject>.Instance.Compare(first, second);

            Assert.AreNotEqual(0, comparison);
            Assert.AreEqual(
                -comparison,
                UnityObjectNameComparer<GameObject>.Instance.Compare(second, first)
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator CompareHandlesNullValues()
        {
            GameObject first = Create("Solo");

            Assert.Greater(UnityObjectNameComparer<GameObject>.Instance.Compare(first, null), 0);
            Assert.Less(UnityObjectNameComparer<GameObject>.Instance.Compare(null, first), 0);
            Assert.AreEqual(0, UnityObjectNameComparer<GameObject>.Instance.Compare(null, null));
            yield break;
        }

        private GameObject Create(string name)
        {
            return Track(new GameObject(name));
        }
    }
}
