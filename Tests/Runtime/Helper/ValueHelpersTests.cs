// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ValueHelpersTests : CommonTestBase
    {
        [Test]
        public void IsAssignedReturnsFalseForNullReferences()
        {
            Assert.IsFalse(ValueHelpers.IsAssigned(null));
        }

        [Test]
        public void IsAssignedReturnsFalseForWhitespaceStrings()
        {
            Assert.IsFalse(ValueHelpers.IsAssigned("   "));
        }

        [Test]
        public void IsAssignedReturnsTrueForNonEmptyStrings()
        {
            Assert.IsTrue(ValueHelpers.IsAssigned("value"));
        }

        [Test]
        public void IsAssignedEvaluatesCollections()
        {
            Assert.IsFalse(ValueHelpers.IsAssigned(new List<int>()));
            Assert.IsTrue(ValueHelpers.IsAssigned(new List<int> { 1, 2 }));
        }

        [Test]
        public void IsAssignedEvaluatesEnumerables()
        {
            IEnumerable<int> empty = System.Array.Empty<int>();
            IEnumerable<int> populated = new[] { 1 };

            Assert.IsFalse(ValueHelpers.IsAssigned(empty));
            Assert.IsTrue(ValueHelpers.IsAssigned(populated));
        }

        [Test]
        public void IsAssignedReturnsFalseForDestroyedUnityObjects()
        {
            GameObject go = Track(new GameObject("ValueHelpers_Destroyed"));
            Assert.IsTrue(ValueHelpers.IsAssigned(go));

            Object.DestroyImmediate(go); // UNH-SUPPRESS: Test verifies IsAssigned returns false after destruction

            Assert.IsFalse(ValueHelpers.IsAssigned(go));
        }
    }
}
