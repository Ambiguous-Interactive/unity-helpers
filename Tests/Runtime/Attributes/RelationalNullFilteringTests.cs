// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class RelationalNullFilteringTests : CommonTestBase
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void MissingAndDestroyedEntriesAreRemovedBeforeLimits(int maximum)
        {
            GameObject expiredOwner = Track(new GameObject("Expired"));
            BoxCollider expired = expiredOwner.AddComponent<BoxCollider>();
            DestroyTrackedObjects();
            Assert.IsTrue(expired == null);
            GameObject owner = Track(new GameObject("Live"));
            BoxCollider first = owner.AddComponent<BoxCollider>();
            BoxCollider second = owner.AddComponent<BoxCollider>();
            List<Component> components = new() { null, expired, first, null, second, null };
            SiblingComponentAttribute attribute = new() { MaxCount = maximum };
            int count = RelationalComponentProcessor.FilterComponentsInPlace(
                components,
                attribute,
                typeof(Component),
                false
            );
            Component[] expected =
                maximum == 1 ? new Component[] { first } : new Component[] { first, second };
            Assert.AreEqual(expected.Length, count);
            CollectionAssert.AreEqual(expected, components);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void EntirelyMissingCollectionsRemainEmpty(int maximum)
        {
            List<Component> components = new() { null, null };
            SiblingComponentAttribute attribute = new() { MaxCount = maximum };
            int count = RelationalComponentProcessor.FilterComponentsInPlace(
                components,
                attribute,
                typeof(Component),
                false
            );
            Assert.AreEqual(0, count);
            Assert.IsEmpty(components);
        }
    }
}
