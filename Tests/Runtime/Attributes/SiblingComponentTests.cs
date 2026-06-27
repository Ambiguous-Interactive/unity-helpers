// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Attributes.Components;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SiblingComponentTests : CommonTestBase
    {
        [Test]
        public void AssignSiblingComponentsPopulatesSupportedFieldShapes()
        {
            GameObject root = Track(new GameObject("SiblingAssignments"));
            BoxCollider first = root.AddComponent<BoxCollider>();
            BoxCollider second = root.AddComponent<BoxCollider>();
            SiblingAssignmentComponent tester = root.AddComponent<SiblingAssignmentComponent>();

            tester.AssignSiblingComponents();

            Assert.AreSame(first, tester.single);

            CollectionAssert.AreEquivalent(new[] { first, second }, tester.array);
            CollectionAssert.AreEquivalent(new[] { first, second }, tester.list);

            Assert.IsTrue(tester.optional == null);
            return;
        }

        [Test]
        public void AssignSiblingComponentsLogsErrorWhenRequiredSiblingMissing()
        {
            GameObject root = Track(
                new GameObject("SiblingMissing", typeof(SiblingMissingComponent))
            );
            SiblingMissingComponent tester = root.GetComponent<SiblingMissingComponent>();

            ExpectMissingRelationalComponentError(
                "SiblingMissing",
                "SiblingMissingComponent",
                "sibling",
                "UnityEngine.Rigidbody",
                "required"
            );

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.required == null);
            return;
        }

        [Test]
        public void SkipIfAssignedPreservesExistingValues()
        {
            GameObject root = Track(new GameObject("SiblingSkipIfAssigned"));
            BoxCollider first = root.AddComponent<BoxCollider>();
            BoxCollider second = root.AddComponent<BoxCollider>();
            SiblingSkipIfAssignedTester tester = root.AddComponent<SiblingSkipIfAssignedTester>();

            // Pre-assign values that should NOT be overwritten
            tester.preAssignedSibling = second;
            tester.preAssignedSiblingArray = new[] { second };
            tester.preAssignedSiblingList = new List<BoxCollider> { second };

            // Call assignment
            tester.AssignSiblingComponents();

            // Verify pre-assigned values were preserved (SkipIfAssigned = true)
            Assert.AreSame(second, tester.preAssignedSibling);
            Assert.AreEqual(1, tester.preAssignedSiblingArray.Length);
            Assert.AreSame(second, tester.preAssignedSiblingArray[0]);
            Assert.AreEqual(1, tester.preAssignedSiblingList.Count);
            Assert.AreSame(second, tester.preAssignedSiblingList[0]);

            // Verify normal assignments (without skipIfAssigned) were assigned
            Assert.AreSame(first, tester.normalSibling);

            return;
        }

        [Test]
        public void SkipIfAssignedDoesNotSkipEmptyCollections()
        {
            GameObject root = Track(new GameObject("SiblingSkipEmpty"));
            _ = root.AddComponent<BoxCollider>();
            SiblingSkipIfAssignedTester tester = root.AddComponent<SiblingSkipIfAssignedTester>();

            // Pre-assign EMPTY collections (should be overwritten)
            tester.preAssignedSiblingArray = Array.Empty<BoxCollider>();
            tester.preAssignedSiblingList = new List<BoxCollider>();

            tester.AssignSiblingComponents();

            // Empty collections should have been overwritten
            Assert.AreEqual(1, tester.preAssignedSiblingArray.Length);
            Assert.AreEqual(1, tester.preAssignedSiblingList.Count);
            return;
        }

        [Test]
        public void SkipIfAssignedWithNullUnityObjectStillAssigns()
        {
            GameObject root = Track(new GameObject("SiblingSkipNull"));
            BoxCollider collider = root.AddComponent<BoxCollider>();
            SiblingSkipIfAssignedTester tester = root.AddComponent<SiblingSkipIfAssignedTester>();

            // Explicitly set to null (destroyed Unity object)
            tester.preAssignedSibling = null;

            tester.AssignSiblingComponents();

            // Null Unity object should have been reassigned
            Assert.AreSame(collider, tester.preAssignedSibling);

            return;
        }

        [Test]
        public void OptionalSiblingDoesNotLogErrorWhenMissing()
        {
            GameObject root = Track(
                new GameObject("SiblingOptional", typeof(SiblingOptionalTester))
            );
            SiblingOptionalTester tester = root.GetComponent<SiblingOptionalTester>();

            // Should NOT log error for optional component
            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.optionalCollider == null);
            return;
        }

        [Test]
        public void MultipleSiblingComponentsOfSameType()
        {
            GameObject root = Track(new GameObject("SiblingMultiple"));
            BoxCollider first = root.AddComponent<BoxCollider>();
            BoxCollider second = root.AddComponent<BoxCollider>();
            BoxCollider third = root.AddComponent<BoxCollider>();
            SiblingMultipleTester tester = root.AddComponent<SiblingMultipleTester>();

            tester.AssignSiblingComponents();

            // Single should return first one found
            Assert.IsTrue(tester.single != null);
            Assert.IsTrue(
                tester.single == first || tester.single == second || tester.single == third
            );

            // Array and List should contain all instances
            Assert.AreEqual(3, tester.array.Length);
            Assert.AreEqual(3, tester.list.Count);
            CollectionAssert.Contains(tester.array, first);
            CollectionAssert.Contains(tester.array, second);
            CollectionAssert.Contains(tester.array, third);
            return;
        }

        [Test]
        public void SiblingComponentIncludesSelf()
        {
            GameObject root = Track(new GameObject("SiblingSelf", typeof(SpriteRenderer)));
            SpriteRenderer selfRenderer = root.GetComponent<SpriteRenderer>();
            SiblingSelfInclusionTester tester = root.AddComponent<SiblingSelfInclusionTester>();

            tester.AssignSiblingComponents();

            // Sibling search should include the component itself
            Assert.AreSame(selfRenderer, tester.siblingRenderer);
            CollectionAssert.AreEquivalent(new[] { selfRenderer }, tester.rendererArray);
            CollectionAssert.AreEquivalent(new[] { selfRenderer }, tester.rendererList);

            return;
        }

        [Test]
        public void SiblingComponentExcludesOtherGameObjects()
        {
            GameObject root = Track(new GameObject("SiblingExclude"));
            BoxCollider rootCollider = root.AddComponent<BoxCollider>();
            SiblingExclusionTester tester = root.AddComponent<SiblingExclusionTester>();

            GameObject child = Track(new GameObject("SiblingChild", typeof(BoxCollider)));
            child.transform.SetParent(root.transform);

            GameObject sibling = Track(new GameObject("SiblingSibling", typeof(BoxCollider)));
            sibling.transform.SetParent(root.transform.parent);

            tester.AssignSiblingComponents();

            // Should only find components on the same GameObject
            Assert.AreEqual(1, tester.colliders.Length);
            CollectionAssert.Contains(tester.colliders, rootCollider);
            CollectionAssert.DoesNotContain(tester.colliders, child.GetComponent<BoxCollider>());
            CollectionAssert.DoesNotContain(tester.colliders, sibling.GetComponent<BoxCollider>());
            return;
        }

        [Test]
        public void SiblingComponentWithOnlyOneComponent()
        {
            GameObject root = Track(new GameObject("SiblingOne", typeof(BoxCollider)));
            BoxCollider collider = root.GetComponent<BoxCollider>();
            SiblingOneTester tester = root.AddComponent<SiblingOneTester>();

            tester.AssignSiblingComponents();

            Assert.AreSame(collider, tester.single);
            CollectionAssert.AreEquivalent(new[] { collider }, tester.array);
            CollectionAssert.AreEquivalent(new[] { collider }, tester.list);

            return;
        }

        [Test]
        public void CacheIsolationBetweenDifferentComponentTypes()
        {
            GameObject root = Track(new GameObject("SiblingCache", typeof(BoxCollider)));
            SiblingCacheIsolationTesterA testerA =
                root.AddComponent<SiblingCacheIsolationTesterA>();
            SiblingCacheIsolationTesterB testerB =
                root.AddComponent<SiblingCacheIsolationTesterB>();
            BoxCollider collider = root.GetComponent<BoxCollider>();

            testerA.AssignSiblingComponents();
            testerB.AssignSiblingComponents();

            // Both should have their own cached field info
            Assert.AreSame(collider, testerA.siblingCollider);
            Assert.AreSame(collider, testerB.siblingCollider);

            return;
        }

        [Test]
        public void RepeatedAssignmentsAreIdempotent()
        {
            GameObject root = Track(new GameObject("SiblingIdempotent"));
            _ = root.AddComponent<BoxCollider>();
            _ = root.AddComponent<BoxCollider>();
            SiblingMultipleTester tester = root.AddComponent<SiblingMultipleTester>();

            tester.AssignSiblingComponents();
            BoxCollider[] firstAssignment = tester.array.ToArray();
            List<BoxCollider> firstListAssignment = tester.list.ToList();

            tester.AssignSiblingComponents();
            BoxCollider[] secondAssignment = tester.array;

            // Repeated calls should produce same results
            CollectionAssert.AreEquivalent(firstAssignment, secondAssignment);
            CollectionAssert.AreEquivalent(firstListAssignment, tester.list);

            return;
        }

        [Test]
        public void SiblingComponentWithMixedComponentTypes()
        {
            GameObject root = new("SiblingMixed");
            Track(root);
            root.AddComponent<BoxCollider>();
            root.AddComponent<SpriteRenderer>();
            root.AddComponent<Rigidbody>();
            SiblingMixedTester tester = root.AddComponent<SiblingMixedTester>();

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.siblingCollider != null);
            Assert.IsTrue(tester.siblingRenderer != null);
            Assert.IsTrue(tester.siblingRigidBody != null);

            return;
        }

        [Test]
        public void SiblingComponentDoesNotFindDisabledBehaviours()
        {
            GameObject root = new("SiblingDisabled", typeof(BoxCollider));
            Track(root);
            BoxCollider collider = root.GetComponent<BoxCollider>();
            collider.enabled = false;
            SiblingDisabledTester tester = root.AddComponent<SiblingDisabledTester>();

            tester.AssignSiblingComponents();

            // Disabled Behaviour components should still be found
            // (GetComponent doesn't filter by enabled state)
            Assert.AreSame(collider, tester.siblingCollider);

            return;
        }

        [Test]
        public void SiblingComponentWithNoMatchingTypeReturnsNull()
        {
            GameObject root = new("SiblingNoMatch", typeof(SiblingNoMatchTester));
            Track(root);
            SiblingNoMatchTester tester = root.GetComponent<SiblingNoMatchTester>();

            const string owner = "SiblingNoMatch";
            const string ownerType = "SiblingNoMatchTester";
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "sibling",
                "UnityEngine.BoxCollider",
                "siblingCollider"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "sibling",
                "UnityEngine.BoxCollider[]",
                "colliderArray"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "sibling",
                "System.Collections.Generic.List`1[UnityEngine.BoxCollider]",
                "colliderList"
            );

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.siblingCollider == null);
            Assert.AreEqual(0, tester.colliderArray.Length);
            Assert.AreEqual(0, tester.colliderList.Count);

            return;
        }

        [Test]
        public void SiblingComponentFindsComponentsInOrder()
        {
            GameObject root = new("SiblingOrder");
            Track(root);

            // Add components in specific order
            BoxCollider first = root.AddComponent<BoxCollider>();
            BoxCollider second = root.AddComponent<BoxCollider>();
            BoxCollider third = root.AddComponent<BoxCollider>();
            SiblingOrderTester tester = root.AddComponent<SiblingOrderTester>();

            tester.AssignSiblingComponents();

            // GetComponents returns in the order they were added
            Assert.AreEqual(3, tester.colliders.Count);
            Assert.AreSame(first, tester.colliders[0]);
            Assert.AreSame(second, tester.colliders[1]);
            Assert.AreSame(third, tester.colliders[2]);

            return;
        }

        [Test]
        public void IncludeInactiveFindsAllComponentsOnActiveGameObject()
        {
            GameObject root = new("SiblingIncludeInactive");
            Track(root);
            BoxCollider first = root.AddComponent<BoxCollider>();
            BoxCollider second = root.AddComponent<BoxCollider>();
            second.enabled = false;
            SiblingIncludeInactiveTester tester = root.AddComponent<SiblingIncludeInactiveTester>();

            tester.AssignSiblingComponents();

            // includeInactive=true should find both enabled and disabled components
            Assert.IsTrue(tester.includeInactiveSingle != null);
            Assert.AreEqual(2, tester.includeInactiveArray.Length);
            CollectionAssert.Contains(tester.includeInactiveArray, first);
            CollectionAssert.Contains(tester.includeInactiveArray, second);
            Assert.AreEqual(2, tester.includeInactiveList.Count);
            CollectionAssert.Contains(tester.includeInactiveList, first);
            CollectionAssert.Contains(tester.includeInactiveList, second);

            return;
        }

        [Test]
        public void ExcludeInactiveFiltersDisabledComponents()
        {
            GameObject root = new("SiblingExcludeInactive");
            Track(root);
            BoxCollider first = root.AddComponent<BoxCollider>();
            BoxCollider second = root.AddComponent<BoxCollider>();
            second.enabled = false;
            SiblingExcludeInactiveTester tester = root.AddComponent<SiblingExcludeInactiveTester>();

            tester.AssignSiblingComponents();

            // includeInactive=false should filter out disabled components
            Assert.AreSame(first, tester.activeOnlySingle);
            Assert.AreEqual(1, tester.activeOnlyArray.Length);
            Assert.AreSame(first, tester.activeOnlyArray[0]);
            Assert.AreEqual(1, tester.activeOnlyList.Count);
            Assert.AreSame(first, tester.activeOnlyList[0]);

            return;
        }

        [Test]
        public void ExcludeInactiveOnInactiveGameObjectFindsNothing()
        {
            GameObject root = new("SiblingInactiveGameObject");
            Track(root);
            root.SetActive(false);
            SiblingExcludeInactiveTester tester = root.AddComponent<SiblingExcludeInactiveTester>();

            const string owner = "SiblingInactiveGameObject";
            const string ownerType = "SiblingExcludeInactiveTester";
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "sibling",
                "UnityEngine.BoxCollider",
                "activeOnlySingle"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "sibling",
                "UnityEngine.BoxCollider[]",
                "activeOnlyArray"
            );
            ExpectMissingRelationalComponentError(
                owner,
                ownerType,
                "sibling",
                "System.Collections.Generic.List`1[UnityEngine.BoxCollider]",
                "activeOnlyList"
            );

            tester.AssignSiblingComponents();

            // includeInactive=false on inactive GameObject should find nothing
            Assert.IsTrue(tester.activeOnlySingle == null);
            Assert.AreEqual(0, tester.activeOnlyArray.Length);
            Assert.AreEqual(0, tester.activeOnlyList.Count);

            return;
        }

        [Test]
        public void IncludeInactiveOnInactiveGameObjectFindsComponents()
        {
            GameObject root = new("SiblingInactiveGameObjectInclude");
            Track(root);
            root.SetActive(false);
            // Add sibling components while inactive to validate IncludeInactive behavior
            root.AddComponent<BoxCollider>();
            root.AddComponent<BoxCollider>();
            SiblingIncludeInactiveTester tester = root.AddComponent<SiblingIncludeInactiveTester>();

            tester.AssignSiblingComponents();

            // includeInactive=true on inactive GameObject should still find components
            Assert.IsTrue(tester.includeInactiveSingle != null);
            Assert.AreEqual(2, tester.includeInactiveArray.Length);
            Assert.AreEqual(2, tester.includeInactiveList.Count);

            return;
        }

        [Test]
        public void MixedActiveInactiveComponentsFilteredCorrectly()
        {
            GameObject root = new("SiblingMixedActive");
            Track(root);
            BoxCollider first = root.AddComponent<BoxCollider>();
            first.enabled = true;
            BoxCollider second = root.AddComponent<BoxCollider>();
            second.enabled = false;
            BoxCollider third = root.AddComponent<BoxCollider>();
            third.enabled = true;
            BoxCollider fourth = root.AddComponent<BoxCollider>();
            fourth.enabled = false;

            SiblingMixedActiveTester tester = root.AddComponent<SiblingMixedActiveTester>();

            tester.AssignSiblingComponents();

            // includeInactive=false should only find enabled components
            Assert.AreEqual(2, tester.activeOnly.Length);
            CollectionAssert.Contains(tester.activeOnly, first);
            CollectionAssert.Contains(tester.activeOnly, third);
            CollectionAssert.DoesNotContain(tester.activeOnly, second);
            CollectionAssert.DoesNotContain(tester.activeOnly, fourth);

            // includeInactive=true should find all components
            Assert.AreEqual(4, tester.includeInactive.Length);
            CollectionAssert.Contains(tester.includeInactive, first);
            CollectionAssert.Contains(tester.includeInactive, second);
            CollectionAssert.Contains(tester.includeInactive, third);
            CollectionAssert.Contains(tester.includeInactive, fourth);

            return;
        }

        [Test]
        public void IncludeInactiveFindsBehavioursRegardlessOfEnabledState()
        {
            GameObject root = new("SiblingBehaviours");
            Track(root);
            SiblingTestBehaviour first = root.AddComponent<SiblingTestBehaviour>();
            first.enabled = true;
            SiblingTestBehaviour second = root.AddComponent<SiblingTestBehaviour>();
            second.enabled = false;
            SiblingBehaviourTester tester = root.AddComponent<SiblingBehaviourTester>();

            tester.AssignSiblingComponents();

            // includeInactive=true should find both enabled and disabled behaviours
            Assert.AreEqual(2, tester.allBehaviours.Length);
            CollectionAssert.Contains(tester.allBehaviours, first);
            CollectionAssert.Contains(tester.allBehaviours, second);

            return;
        }

        [Test]
        public void ExcludeInactiveFiltersBehavioursByEnabledState()
        {
            GameObject root = new("SiblingBehavioursFiltered");
            Track(root);
            SiblingTestBehaviour first = root.AddComponent<SiblingTestBehaviour>();
            first.enabled = true;
            SiblingTestBehaviour second = root.AddComponent<SiblingTestBehaviour>();
            second.enabled = false;
            SiblingTestBehaviour third = root.AddComponent<SiblingTestBehaviour>();
            third.enabled = true;
            SiblingBehaviourFilterTester tester = root.AddComponent<SiblingBehaviourFilterTester>();

            tester.AssignSiblingComponents();

            // includeInactive=false should only find enabled behaviours
            Assert.AreEqual(2, tester.activeBehaviours.Length);
            CollectionAssert.Contains(tester.activeBehaviours, first);
            CollectionAssert.Contains(tester.activeBehaviours, third);
            CollectionAssert.DoesNotContain(tester.activeBehaviours, second);

            return;
        }

        [Test]
        public void AssignSiblingComponentsNullsPreAssignedConcreteFieldWhenNoSiblingFound()
        {
            GameObject root = Track(new GameObject("SiblingNullConcrete"));
            SiblingOverwriteNullTester tester = root.AddComponent<SiblingOverwriteNullTester>();

            GameObject other = Track(new GameObject("OtherObject"));
            BoxCollider otherCollider = other.AddComponent<BoxCollider>();

            tester.concreteField = otherCollider;
            Assert.IsTrue(tester.concreteField != null);

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.concreteField == null);

            return;
        }

        [Test]
        public void AssignSiblingComponentsNullsPreAssignedInterfaceFieldWhenNoSiblingFound()
        {
            GameObject root = Track(new GameObject("SiblingNullInterface"));
            SiblingOverwriteNullTester tester = root.AddComponent<SiblingOverwriteNullTester>();

            GameObject other = Track(new GameObject("OtherObject"));
            TestInterfaceComponent otherInterface = other.AddComponent<TestInterfaceComponent>();

            tester.interfaceField = otherInterface;
            Assert.IsTrue(tester.interfaceField != null);

            tester.AssignSiblingComponents();

            Assert.IsTrue(tester.interfaceField == null);

            return;
        }
    }
}
