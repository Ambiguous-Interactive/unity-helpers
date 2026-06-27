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
    /// Tests to ensure backward compatibility after refactoring to use base classes.
    /// These tests verify that all existing functionality still works exactly as before.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentBackwardCompatibilityTests : CommonTestBase
    {
        [Test]
        public void ParentBasicFunctionalityUnchanged()
        {
            GameObject root = Track(new GameObject("Root", typeof(SpriteRenderer)));
            GameObject child = Track(new GameObject("Child", typeof(BasicParentTester)));
            child.transform.SetParent(root.transform);

            BasicParentTester tester = child.GetComponent<BasicParentTester>();
            tester.AssignParentComponents();

            Assert.IsTrue(tester.parent != null);
            Assert.AreSame(root.GetComponent<SpriteRenderer>(), tester.parent);

            return;
        }

        [Test]
        public void ParentArrayStillWorks()
        {
            GameObject root = Track(new GameObject("Root", typeof(SpriteRenderer)));
            GameObject parent = Track(new GameObject("Parent", typeof(SpriteRenderer)));
            parent.transform.SetParent(root.transform);
            GameObject child = Track(new GameObject("Child", typeof(ParentArrayTester)));
            child.transform.SetParent(parent.transform);

            ParentArrayTester tester = child.GetComponent<ParentArrayTester>();
            tester.AssignParentComponents();

            Assert.AreEqual(2, tester.parents.Length);

            return;
        }

        [Test]
        public void ParentListStillWorks()
        {
            GameObject root = Track(new GameObject("Root", typeof(SpriteRenderer)));
            GameObject child = Track(new GameObject("Child", typeof(ParentListTester)));
            child.transform.SetParent(root.transform);

            ParentListTester tester = child.GetComponent<ParentListTester>();
            tester.AssignParentComponents();

            Assert.AreEqual(1, tester.parents.Count);

            return;
        }

        [Test]
        public void ParentOptionalStillWorks()
        {
            GameObject orphan = Track(new GameObject("Orphan", typeof(ParentOptionalTester)));
            ParentOptionalTester tester = orphan.GetComponent<ParentOptionalTester>();

            // Should NOT log error
            tester.AssignParentComponents();

            Assert.IsTrue(tester.optionalRenderer == null);

            return;
        }

        [Test]
        public void ParentOnlyAncestorsStillWorks()
        {
            GameObject root = Track(new GameObject("Root", typeof(SpriteRenderer)));
            GameObject child = new(
                "Child",
                typeof(SpriteRenderer),
                typeof(ParentOnlyAncestorsTester)
            );
            child = Track(child);
            child.transform.SetParent(root.transform);

            ParentOnlyAncestorsTester tester = child.GetComponent<ParentOnlyAncestorsTester>();
            tester.AssignParentComponents();

            // Should not include self
            Assert.AreSame(root.GetComponent<SpriteRenderer>(), tester.ancestorOnly);

            return;
        }

        [Test]
        public void ParentIncludeInactiveStillWorks()
        {
            GameObject root = Track(new GameObject("Root", typeof(SpriteRenderer)));
            GameObject inactive = Track(new GameObject("Inactive", typeof(SpriteRenderer)));
            inactive.SetActive(false);
            inactive.transform.SetParent(root.transform);
            GameObject child = Track(new GameObject("Child", typeof(ParentInactiveTester)));
            child.transform.SetParent(inactive.transform);

            ParentInactiveTester tester = child.GetComponent<ParentInactiveTester>();
            tester.AssignParentComponents();

            // Should find inactive parent
            Assert.IsTrue(tester.inactiveOnly != null);
            Assert.AreSame(inactive.GetComponent<SpriteRenderer>(), tester.inactiveOnly);

            // Should skip inactive parent
            Assert.IsTrue(tester.activeOnly != null);
            Assert.AreSame(root.GetComponent<SpriteRenderer>(), tester.activeOnly);

            return;
        }

        [Test]
        public void ChildBasicFunctionalityUnchanged()
        {
            GameObject root = Track(new GameObject("Root", typeof(BasicChildTester)));
            BasicChildTester tester = root.GetComponent<BasicChildTester>();

            GameObject child = Track(new GameObject("Child", typeof(SpriteRenderer)));
            child.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.IsTrue(tester.child != null);
            Assert.AreSame(child.GetComponent<SpriteRenderer>(), tester.child);

            return;
        }

        [Test]
        public void ChildArrayStillWorks()
        {
            GameObject root = Track(new GameObject("Root", typeof(ChildArrayTester)));
            ChildArrayTester tester = root.GetComponent<ChildArrayTester>();

            for (int i = 0; i < 3; i++)
            {
                GameObject child = Track(new GameObject($"Child{i}", typeof(SpriteRenderer)));
                child.transform.SetParent(root.transform);
            }

            tester.AssignChildComponents();

            Assert.AreEqual(3, tester.children.Length);

            return;
        }

        [Test]
        public void ChildListStillWorks()
        {
            GameObject root = new("Root", typeof(ChildListTester));
            Track(root);
            ChildListTester tester = root.GetComponent<ChildListTester>();

            GameObject child = new("Child", typeof(SpriteRenderer));
            Track(child);
            child.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            Assert.AreEqual(1, tester.children.Count);

            return;
        }

        [Test]
        public void ChildOnlyDescendantsStillWorks()
        {
            GameObject root = new(
                "Root",
                typeof(SpriteRenderer),
                typeof(ChildOnlyDescendantsTester)
            );
            Track(root);
            ChildOnlyDescendantsTester tester = root.GetComponent<ChildOnlyDescendantsTester>();

            GameObject child = new("Child", typeof(SpriteRenderer));
            Track(child);
            child.transform.SetParent(root.transform);

            tester.AssignChildComponents();

            // Should not include self
            Assert.AreSame(child.GetComponent<SpriteRenderer>(), tester.descendantOnly);

            return;
        }

        [Test]
        public void ChildBreadthFirstOrderMaintained()
        {
            GameObject root = new("Root", typeof(SpriteRenderer), typeof(ChildOrderTester));
            Track(root);
            ChildOrderTester tester = root.GetComponent<ChildOrderTester>();

            GameObject child1 = new("Child1", typeof(SpriteRenderer));
            Track(child1);
            child1.transform.SetParent(root.transform);

            GameObject child2 = new("Child2", typeof(SpriteRenderer));
            Track(child2);
            child2.transform.SetParent(root.transform);

            GameObject grandchild = new("Grandchild", typeof(SpriteRenderer));
            Track(grandchild);
            grandchild.transform.SetParent(child1.transform);

            tester.AssignChildComponents();

            // BFS order: root, child1, child2, grandchild
            Assert.AreEqual(4, tester.children.Length);
            Assert.AreSame(root.GetComponent<SpriteRenderer>(), tester.children[0]);
            // Grandchild should be last (after both children)
            Assert.AreSame(grandchild.GetComponent<SpriteRenderer>(), tester.children[3]);

            return;
        }

        [Test]
        public void SiblingBasicFunctionalityUnchanged()
        {
            GameObject root = new("Root");
            Track(root);
            root.AddComponent<BoxCollider>();
            BasicSiblingTester tester = root.AddComponent<BasicSiblingTester>();

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.sibling != null);

            return;
        }

        [Test]
        public void SiblingArrayStillWorks()
        {
            GameObject root = new("Root");
            Track(root);
            root.AddComponent<BoxCollider>();
            root.AddComponent<BoxCollider>();
            SiblingArrayTester tester = root.AddComponent<SiblingArrayTester>();

            tester.AssignSiblingComponents();

            Assert.AreEqual(2, tester.siblings.Length);

            return;
        }

        [Test]
        public void SiblingListStillWorks()
        {
            GameObject root = new("Root");
            Track(root);
            root.AddComponent<BoxCollider>();
            SiblingListTester tester = root.AddComponent<SiblingListTester>();

            tester.AssignSiblingComponents();

            Assert.AreEqual(1, tester.siblings.Count);

            return;
        }

        [Test]
        public void SiblingIncludeInactiveStillWorks()
        {
            GameObject root = new("Root");
            Track(root);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.enabled = false;
            SiblingInactiveTester tester = root.AddComponent<SiblingInactiveTester>();

            tester.AssignSiblingComponents();

            // Should find disabled component with IncludeInactive=true
            Assert.IsTrue(tester.includeInactive != null);

            // Should not find disabled component with IncludeInactive=false
            Assert.IsTrue(tester.excludeInactive == null);

            return;
        }

        [Test]
        public void AssignRelationalComponentsStillWorksForAll()
        {
            GameObject root = new("Root", typeof(SpriteRenderer));
            Track(root);
            root.AddComponent<BoxCollider>();

            GameObject child = new("Child", typeof(SpriteRenderer), typeof(AllRelationalTester));
            Track(child);
            child.transform.SetParent(root.transform);
            child.AddComponent<BoxCollider>();

            AllRelationalTester tester = child.GetComponent<AllRelationalTester>();
            tester.AssignRelationalComponents();

            // Parent should be assigned
            Assert.IsTrue(tester.parentRenderer != null);
            Assert.AreSame(root.GetComponent<SpriteRenderer>(), tester.parentRenderer);

            // Self should be assigned as child
            Assert.IsTrue(tester.childRenderer != null);
            Assert.AreSame(child.GetComponent<SpriteRenderer>(), tester.childRenderer);

            // Sibling should be assigned
            Assert.IsTrue(tester.siblingCollider != null);
            Assert.AreSame(child.GetComponent<BoxCollider>(), tester.siblingCollider);

            return;
        }

        [Test]
        public void SkipIfAssignedStillWorks()
        {
            GameObject root = new("Root", typeof(SpriteRenderer));
            Track(root);
            GameObject child = new("Child", typeof(SkipIfAssignedTester));
            Track(child);
            child.transform.SetParent(root.transform);

            SkipIfAssignedTester tester = child.GetComponent<SkipIfAssignedTester>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            // Pre-assign a value
            SpriteRenderer dummyRenderer = new GameObject("Dummy").AddComponent<SpriteRenderer>();
            Track(dummyRenderer.gameObject);
            tester.preAssigned = dummyRenderer;

            tester.AssignParentComponents();

            // Should preserve pre-assigned value
            Assert.AreSame(dummyRenderer, tester.preAssigned);

            // Should assign normal value
            Assert.AreSame(rootRenderer, tester.normal);

            return;
        }
    }
}
