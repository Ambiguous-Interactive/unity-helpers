// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Components;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ParentComponentTests : CommonTestBase
    {
        [Test]
        public void Nominal()
        {
            GameObject root = Track(
                new GameObject("PartComponentTest - Root", typeof(SpriteRenderer))
            );
            GameObject parentLevel1 = Track(new GameObject("ParentLevel1", typeof(SpriteRenderer)));
            parentLevel1.transform.SetParent(root.transform);
            GameObject parentLevel2 = Track(new GameObject("ParentLevel2", typeof(SpriteRenderer)));
            parentLevel2.transform.SetParent(parentLevel1.transform);
            GameObject parentLevel3 = new(
                "ParentLevel3",
                typeof(SpriteRenderer),
                typeof(ExpectParentSpriteRenderers)
            );
            parentLevel3 = Track(parentLevel3);
            parentLevel3.transform.SetParent(parentLevel2.transform);

            ExpectParentSpriteRenderers expect =
                parentLevel3.GetComponent<ExpectParentSpriteRenderers>();
            expect.AssignParentComponents();

            Assert.AreEqual(4, expect.exclusiveParentList.Count);
            Assert.IsTrue(
                expect.exclusiveParentList.Contains(parentLevel3.GetComponent<SpriteRenderer>())
            );
            Assert.IsTrue(
                expect.exclusiveParentList.Contains(parentLevel2.GetComponent<SpriteRenderer>())
            );
            Assert.IsTrue(
                expect.exclusiveParentList.Contains(parentLevel1.GetComponent<SpriteRenderer>())
            );
            Assert.IsTrue(expect.exclusiveParentList.Contains(root.GetComponent<SpriteRenderer>()));
            Assert.IsTrue(
                expect.exclusiveParentList.ToHashSet().SetEquals(expect.exclusiveParentArray)
            );

            Assert.AreEqual(3, expect.inclusiveParentList.Count);
            Assert.IsTrue(
                expect.inclusiveParentList.Contains(parentLevel2.GetComponent<SpriteRenderer>())
            );
            Assert.IsTrue(
                expect.inclusiveParentList.Contains(parentLevel1.GetComponent<SpriteRenderer>())
            );
            Assert.IsTrue(expect.inclusiveParentList.Contains(root.GetComponent<SpriteRenderer>()));
            Assert.IsTrue(
                expect.inclusiveParentList.ToHashSet().SetEquals(expect.inclusiveParentArray)
            );

            Assert.IsTrue(expect.exclusiveParent != null);
            Assert.AreEqual(expect.GetComponent<SpriteRenderer>(), expect.exclusiveParent);

            Assert.IsTrue(expect.inclusiveParent != null);
            Assert.AreEqual(parentLevel2.GetComponent<SpriteRenderer>(), expect.inclusiveParent);

            return;
        }

        [Test]
        public void IncludeInactiveControlsAncestorSelection()
        {
            GameObject activeRoot = Track(
                new GameObject("ParentActiveRoot", typeof(SpriteRenderer))
            );
            SpriteRenderer rootRenderer = activeRoot.GetComponent<SpriteRenderer>();

            GameObject inactiveParent = Track(
                new GameObject("ParentInactive", typeof(SpriteRenderer))
            );
            inactiveParent.transform.SetParent(activeRoot.transform);
            inactiveParent.SetActive(false);
            SpriteRenderer inactiveRenderer = inactiveParent.GetComponent<SpriteRenderer>();

            GameObject child = new(
                "ParentChild",
                typeof(SpriteRenderer),
                typeof(ParentAssignmentTester)
            );
            child = Track(child);
            child.transform.SetParent(inactiveParent.transform);
            ParentAssignmentTester tester = child.GetComponent<ParentAssignmentTester>();
            SpriteRenderer childRenderer = child.GetComponent<SpriteRenderer>();

            tester.AssignParentComponents();

            Assert.AreSame(rootRenderer, tester.ancestorsActiveOnly);
            Assert.AreSame(inactiveRenderer, tester.ancestorsIncludeInactive);
            CollectionAssert.AreEquivalent(
                new[] { childRenderer, inactiveRenderer, rootRenderer },
                tester.allParents
            );

            return;
        }

        [Test]
        public void MissingRequiredParentLogsError()
        {
            GameObject orphan = Track(new GameObject("ParentOrphan", typeof(ParentMissingTester)));
            ParentMissingTester tester = orphan.GetComponent<ParentMissingTester>();

            ExpectMissingRelationalComponentError(
                "ParentOrphan",
                "ParentMissingTester",
                "parent",
                "UnityEngine.SpriteRenderer",
                "requiredRenderer"
            );

            tester.AssignParentComponents();
            Assert.IsTrue(tester.requiredRenderer == null);
            return;
        }

        [Test]
        public void SkipIfAssignedPreservesExistingValues()
        {
            GameObject root = Track(new GameObject("SkipIfAssignedRoot", typeof(SpriteRenderer)));
            GameObject child = new(
                "SkipIfAssignedChild",
                typeof(SpriteRenderer),
                typeof(ParentSkipIfAssignedTester)
            );
            child = Track(child);
            child.transform.SetParent(root.transform);

            ParentSkipIfAssignedTester tester = child.GetComponent<ParentSkipIfAssignedTester>();
            SpriteRenderer childRenderer = child.GetComponent<SpriteRenderer>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            // Pre-assign values that should NOT be overwritten
            tester.preAssignedParent = childRenderer;
            tester.preAssignedParentArray = new[] { childRenderer };
            tester.preAssignedParentList = new List<SpriteRenderer> { childRenderer };

            // Call assignment
            tester.AssignParentComponents();

            // Verify pre-assigned values were preserved (SkipIfAssigned = true)
            Assert.AreSame(childRenderer, tester.preAssignedParent);
            Assert.AreEqual(1, tester.preAssignedParentArray.Length);
            Assert.AreSame(childRenderer, tester.preAssignedParentArray[0]);
            Assert.AreEqual(1, tester.preAssignedParentList.Count);
            Assert.AreSame(childRenderer, tester.preAssignedParentList[0]);

            // Verify normal assignments (without skipIfAssigned) were assigned
            Assert.AreSame(rootRenderer, tester.normalParent);

            return;
        }

        [Test]
        public void SkipIfAssignedDoesNotSkipEmptyCollections()
        {
            GameObject root = Track(new GameObject("SkipEmptyRoot", typeof(SpriteRenderer)));
            GameObject child = Track(
                new GameObject("SkipEmptyChild", typeof(ParentSkipIfAssignedTester))
            );
            child.transform.SetParent(root.transform);

            ParentSkipIfAssignedTester tester = child.GetComponent<ParentSkipIfAssignedTester>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            // Pre-assign EMPTY collections (should be overwritten)
            tester.preAssignedParentArray = Array.Empty<SpriteRenderer>();
            tester.preAssignedParentList = new List<SpriteRenderer>();

            tester.AssignParentComponents();

            // Empty collections should have been overwritten
            Assert.AreEqual(1, tester.preAssignedParentArray.Length);
            Assert.AreSame(rootRenderer, tester.preAssignedParentArray[0]);
            Assert.AreEqual(1, tester.preAssignedParentList.Count);
            Assert.AreSame(rootRenderer, tester.preAssignedParentList[0]);

            return;
        }

        [Test]
        public void SkipIfAssignedWithNullUnityObjectStillAssigns()
        {
            GameObject root = new("SkipNullRoot", typeof(SpriteRenderer));
            Track(root);
            GameObject child = new("SkipNullChild", typeof(ParentSkipIfAssignedTester));
            Track(child);
            child.transform.SetParent(root.transform);

            ParentSkipIfAssignedTester tester = child.GetComponent<ParentSkipIfAssignedTester>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            // Explicitly set to null (destroyed Unity object)
            tester.preAssignedParent = null;

            tester.AssignParentComponents();

            // Null Unity object should have been reassigned
            Assert.AreSame(rootRenderer, tester.preAssignedParent);

            return;
        }

        [Test]
        public void OptionalParentDoesNotLogErrorWhenMissing()
        {
            GameObject orphan = new("OptionalOrphan", typeof(ParentOptionalTester));
            Track(orphan);
            ParentOptionalTester tester = orphan.GetComponent<ParentOptionalTester>();

            // Should NOT log error for optional component
            tester.AssignParentComponents();

            Assert.IsTrue(tester.optionalRenderer == null);
            return;
        }

        [Test]
        public void OnlyAncestorsExcludesSelf()
        {
            GameObject root = new("OnlyAncestorsRoot", typeof(SpriteRenderer));
            Track(root);
            GameObject child = new(
                "OnlyAncestorsChild",
                typeof(SpriteRenderer),
                typeof(ParentOnlyAncestorsTester)
            );
            Track(child);
            child.transform.SetParent(root.transform);

            ParentOnlyAncestorsTester tester = child.GetComponent<ParentOnlyAncestorsTester>();
            SpriteRenderer childRenderer = child.GetComponent<SpriteRenderer>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            tester.AssignParentComponents();

            // onlyAncestors=true should exclude self
            Assert.AreSame(rootRenderer, tester.ancestorOnly);
            CollectionAssert.AreEquivalent(new[] { rootRenderer }, tester.ancestorOnlyArray);

            // onlyAncestors=false should include self
            Assert.AreSame(childRenderer, tester.includeSelf);
            CollectionAssert.AreEquivalent(
                new[] { childRenderer, rootRenderer },
                tester.includeSelfArray
            );

            return;
        }

        [Test]
        public void OnlyAncestorsWithNoParentReturnsNothing()
        {
            GameObject orphan = new("OnlyAncestorsOrphan", typeof(ParentOnlyAncestorsTester));
            Track(orphan);
            ParentOnlyAncestorsTester tester = orphan.GetComponent<ParentOnlyAncestorsTester>();

            const string owner = "OnlyAncestorsOrphan";
            const string ownerType = "ParentOnlyAncestorsTester";
            // onlyAncestors=true fields have no parent; includeSelf fields find no SpriteRenderer on self.
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "parent",
                "UnityEngine.SpriteRenderer",
                "ancestorOnly"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "parent",
                "UnityEngine.SpriteRenderer[]",
                "ancestorOnlyArray"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "parent",
                "UnityEngine.SpriteRenderer",
                "includeSelf"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "parent",
                "UnityEngine.SpriteRenderer[]",
                "includeSelfArray"
            );

            tester.AssignParentComponents();

            Assert.IsTrue(tester.ancestorOnly == null);
            Assert.AreEqual(0, tester.ancestorOnlyArray.Length);

            return;
        }

        [Test]
        public void MultipleParentComponentsReturnedInCorrectOrder()
        {
            GameObject grandParent = new("GrandParent", typeof(SpriteRenderer));
            Track(grandParent);
            GameObject parent = new("Parent", typeof(SpriteRenderer));
            Track(parent);
            parent.transform.SetParent(grandParent.transform);
            GameObject child = new("Child", typeof(SpriteRenderer), typeof(ParentMultipleTester));
            Track(child);
            child.transform.SetParent(parent.transform);

            ParentMultipleTester tester = child.GetComponent<ParentMultipleTester>();
            SpriteRenderer childRenderer = child.GetComponent<SpriteRenderer>();
            SpriteRenderer parentRenderer = parent.GetComponent<SpriteRenderer>();
            SpriteRenderer grandParentRenderer = grandParent.GetComponent<SpriteRenderer>();

            tester.AssignParentComponents();

            // Should return in parent hierarchy order (closest first)
            Assert.AreEqual(3, tester.allParents.Length);
            Assert.AreSame(childRenderer, tester.allParents[0]);
            Assert.AreSame(parentRenderer, tester.allParents[1]);
            Assert.AreSame(grandParentRenderer, tester.allParents[2]);

            Assert.AreEqual(3, tester.allParentsList.Count);
            Assert.AreSame(childRenderer, tester.allParentsList[0]);
            Assert.AreSame(parentRenderer, tester.allParentsList[1]);
            Assert.AreSame(grandParentRenderer, tester.allParentsList[2]);

            return;
        }

        [Test]
        public void DeepHierarchyHandledCorrectly()
        {
            GameObject root = new("DeepRoot", typeof(SpriteRenderer));
            Track(root);
            GameObject current = root;

            // Create deep hierarchy (10 levels)
            for (int i = 0; i < 10; i++)
            {
                GameObject next = new($"DeepLevel{i}", typeof(SpriteRenderer));
                Track(next);
                next.transform.SetParent(current.transform);
                current = next;
            }

            GameObject leaf = new("DeepLeaf", typeof(ParentMultipleTester));
            Track(leaf);
            leaf.transform.SetParent(current.transform);

            ParentMultipleTester tester = leaf.GetComponent<ParentMultipleTester>();
            tester.AssignParentComponents();

            // Should find all 11 parents (10 levels + root)
            Assert.AreEqual(11, tester.allParents.Length);
            Assert.AreEqual(11, tester.allParentsList.Count);

            return;
        }

        [Test]
        public void InactiveParentComponentExcludedWhenIncludeInactiveFalse()
        {
            GameObject root = new("InactiveRoot", typeof(SpriteRenderer));
            Track(root);
            GameObject inactiveParent = new("InactiveParent", typeof(SpriteRenderer));
            Track(inactiveParent);
            inactiveParent.transform.SetParent(root.transform);
            inactiveParent.SetActive(false);
            GameObject child = new("InactiveChild", typeof(ParentInactiveTester));
            Track(child);
            child.transform.SetParent(inactiveParent.transform);

            ParentInactiveTester tester = child.GetComponent<ParentInactiveTester>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            tester.AssignParentComponents();

            // includeInactive=false should skip inactive parent
            Assert.AreSame(rootRenderer, tester.activeOnly);
            CollectionAssert.AreEquivalent(new[] { rootRenderer }, tester.activeOnlyArray);

            return;
        }

        [Test]
        public void DisabledBehaviourNotFilteredByIncludeInactive()
        {
            GameObject root = new("DisabledRoot", typeof(BoxCollider));
            Track(root);
            BoxCollider rootCollider = root.GetComponent<BoxCollider>();
            rootCollider.enabled = false;

            GameObject child = new("DisabledChild", typeof(ParentDisabledBehaviourTester));
            Track(child);
            child.transform.SetParent(root.transform);

            ParentDisabledBehaviourTester tester =
                child.GetComponent<ParentDisabledBehaviourTester>();
            tester.AssignParentComponents();

            // Disabled Behaviour (BoxCollider) should still be found
            // includeInactive only affects GameObject.activeInHierarchy
            Assert.AreSame(rootCollider, tester.parentCollider);

            return;
        }

        [Test]
        public void CacheIsolationBetweenDifferentComponentTypes()
        {
            GameObject root = new("CacheRoot", typeof(SpriteRenderer));
            Track(root);
            GameObject child = new(
                "CacheChild",
                typeof(ParentCacheIsolationTesterA),
                typeof(ParentCacheIsolationTesterB)
            );
            Track(child);
            child.transform.SetParent(root.transform);

            ParentCacheIsolationTesterA testerA = child.GetComponent<ParentCacheIsolationTesterA>();
            ParentCacheIsolationTesterB testerB = child.GetComponent<ParentCacheIsolationTesterB>();

            testerA.AssignParentComponents();
            testerB.AssignParentComponents();

            // Both should have their own cached field info
            Assert.IsTrue(testerA.parentRenderer != null);
            Assert.IsTrue(testerB.parentRenderer != null);
            Assert.AreSame(testerA.parentRenderer, testerB.parentRenderer);

            return;
        }

        [Test]
        public void RepeatedAssignmentsAreIdempotent()
        {
            GameObject root = new("IdempotentRoot", typeof(SpriteRenderer));
            Track(root);
            GameObject child = new("IdempotentChild", typeof(ParentMultipleTester));
            Track(child);
            child.transform.SetParent(root.transform);

            ParentMultipleTester tester = child.GetComponent<ParentMultipleTester>();
            SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();

            tester.AssignParentComponents();
            SpriteRenderer[] firstAssignment = tester.allParents;

            tester.AssignParentComponents();
            SpriteRenderer[] secondAssignment = tester.allParents;

            // Repeated calls should produce same results
            CollectionAssert.AreEqual(firstAssignment, secondAssignment);

            return;
        }

        [Test]
        public void AssignParentComponentsNullsPreAssignedConcreteFieldWhenNoParentFound()
        {
            GameObject other = Track(new GameObject("OtherObject", typeof(BoxCollider)));
            BoxCollider otherCollider = other.GetComponent<BoxCollider>();

            GameObject orphan = Track(
                new GameObject("OrphanConcrete", typeof(ParentOverwriteNullTester))
            );
            ParentOverwriteNullTester tester = orphan.GetComponent<ParentOverwriteNullTester>();

            tester.concreteField = otherCollider;
            Assert.IsTrue(tester.concreteField != null);

            tester.AssignParentComponents();

            Assert.IsTrue(tester.concreteField == null);

            return;
        }

        [Test]
        public void AssignParentComponentsNullsPreAssignedInterfaceFieldWhenNoParentFound()
        {
            GameObject other = Track(new GameObject("OtherObject", typeof(TestInterfaceComponent)));
            ITestInterface otherInterface = other.GetComponent<TestInterfaceComponent>();

            GameObject orphan = Track(
                new GameObject("OrphanInterface", typeof(ParentOverwriteNullTester))
            );
            ParentOverwriteNullTester tester = orphan.GetComponent<ParentOverwriteNullTester>();

            tester.interfaceField = otherInterface;
            Assert.IsTrue(tester.interfaceField != null);

            tester.AssignParentComponents();

            Assert.IsTrue(tester.interfaceField == null);

            return;
        }
    }
}
