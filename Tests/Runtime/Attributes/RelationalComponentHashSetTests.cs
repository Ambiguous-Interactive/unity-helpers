// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    /// <summary>
    /// Tests for HashSet support in relational component attributes
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentHashSetTests : CommonTestBase
    {
        [Test]
        public void ParentHashSetFindsComponents()
        {
            GameObject root = Track(new GameObject("Root", typeof(SpriteRenderer)));
            GameObject parent1 = Track(new GameObject("Parent1", typeof(SpriteRenderer)));
            parent1.transform.SetParent(root.transform);
            GameObject child = Track(new GameObject("Child", typeof(ParentHashSetTester)));
            child.transform.SetParent(parent1.transform);

            ParentHashSetTester tester = child.GetComponent<ParentHashSetTester>();
            tester.AssignParentComponents();

            Assert.IsTrue(tester.parentRenderers != null);
            Assert.AreEqual(2, tester.parentRenderers.Count);

            return;
        }

        [Test]
        public void ChildHashSetFindsComponents()
        {
            GameObject root = Track(new GameObject("Root", typeof(ChildHashSetTester)));
            ChildHashSetTester tester = root.GetComponent<ChildHashSetTester>();

            for (int i = 0; i < 3; i++)
            {
                GameObject child = Track(new GameObject($"Child{i}", typeof(SpriteRenderer)));
                child.transform.SetParent(root.transform);
            }

            tester.AssignChildComponents();

            Assert.IsTrue(tester.childRenderers != null);
            Assert.AreEqual(3, tester.childRenderers.Count);

            return;
        }

        [Test]
        public void SiblingHashSetFindsComponents()
        {
            GameObject root = Track(new GameObject("Root"));

            for (int i = 0; i < 3; i++)
            {
                root.AddComponent<BoxCollider>();
            }

            SiblingHashSetTester tester = root.AddComponent<SiblingHashSetTester>();
            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.siblingColliders != null);
            Assert.AreEqual(3, tester.siblingColliders.Count);

            return;
        }

        [Test]
        public void HashSetAutomaticallyDeduplicates()
        {
            GameObject root = Track(
                new GameObject("Root", typeof(ChildHashSetDeduplicationTester))
            );
            ChildHashSetDeduplicationTester tester =
                root.GetComponent<ChildHashSetDeduplicationTester>();

            GameObject child = Track(new GameObject("Child", typeof(SpriteRenderer)));
            child.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.IsTrue(tester.uniqueChildren != null);
            Assert.AreEqual(1, tester.uniqueChildren.Count);

            return;
        }

        [Test]
        public void HashSetSupportsMaxCount()
        {
            GameObject root = Track(new GameObject("Root", typeof(ChildHashSetMaxCountTester)));
            ChildHashSetMaxCountTester tester = root.GetComponent<ChildHashSetMaxCountTester>();

            for (int i = 0; i < 5; i++)
            {
                GameObject child = Track(new GameObject($"Child{i}", typeof(SpriteRenderer)));
                child.transform.SetParent(root.transform);
            }

            tester.AssignChildComponents();

            Assert.AreEqual(2, tester.limitedChildren.Count);

            return;
        }

        [Test]
        public void HashSetSupportsInterfaces()
        {
            GameObject root = Track(new GameObject("Root", typeof(ChildHashSetInterfaceTester)));
            ChildHashSetInterfaceTester tester = root.GetComponent<ChildHashSetInterfaceTester>();

            GameObject child1 = Track(new GameObject("Child1", typeof(TestInterfaceComponent)));
            child1.transform.SetParent(root.transform);

            GameObject child2 = Track(new GameObject("Child2", typeof(AnotherInterfaceComponent)));
            child2.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.IsTrue(tester.interfaceChildren != null);
            Assert.AreEqual(2, tester.interfaceChildren.Count);

            return;
        }

        [Test]
        public void HashSetWorksWithFilters()
        {
            GameObject root = new("Root", typeof(ChildHashSetFilterTester));
            Track(root);
            ChildHashSetFilterTester tester = root.GetComponent<ChildHashSetFilterTester>();

            GameObject child1 = new("PlayerChild", typeof(SpriteRenderer));
            Track(child1);
            child1.tag = "Player";
            child1.transform.SetParent(root.transform);

            GameObject child2 = new("EnemyChild", typeof(SpriteRenderer));
            Track(child2);
            child2.tag = "Untagged";
            child2.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.AreEqual(1, tester.playerChildren.Count);

            return;
        }
    }
}
