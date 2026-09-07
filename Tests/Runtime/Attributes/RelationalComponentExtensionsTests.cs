// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentExtensionsTests : CommonTestBase
    {
        [Test]
        public void ExactTypeAssignmentMatchesDirectQueriesAcrossShapesAndCacheStates(
            [Values(1, 4)] int depth,
            [Values(false, true)] bool inactive,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = ReflectionHelpers.OverrideReflectionCapabilities(
                capability == 0,
                capability == 1
            );
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldSetterCache();
            GameObject root = CreateExactTypeCandidates("ExactRoot");
            GameObject ancestor = root;
            for (int level = 1; level < depth; level++)
            {
                GameObject next = CreateExactTypeCandidates("ExactAncestor");
                next.transform.SetParent(ancestor.transform);
                ancestor = next;
            }
            GameObject owner = CreateExactTypeCandidates("ExactOwner");
            owner.transform.SetParent(ancestor.transform);
            RelationalExactTypeTester tester = owner.AddComponent<RelationalExactTypeTester>();
            GameObject first = CreateExactTypeCandidates("ExactFirstChild");
            first.transform.SetParent(owner.transform);
            GameObject grandchild = CreateExactTypeCandidates("ExactGrandchild");
            grandchild.transform.SetParent(first.transform);
            GameObject second = CreateExactTypeCandidates("ExactSecondChild");
            second.transform.SetParent(owner.transform);
            root.SetActive(!inactive);
            AssertExactTypeAssignmentMatchesDirectQueries(tester);
            AssertExactTypeAssignmentMatchesDirectQueries(tester);
            CreateExactTypeCandidates("ExactUnrelated");
            AssertExactTypeAssignmentMatchesDirectQueries(tester);
            second.transform.SetAsFirstSibling();
            AssertExactTypeAssignmentMatchesDirectQueries(tester);
            foreach (RelationalExactComponent candidate in ExactComponentsOn(first.transform))
            {
                UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Rebinding must discard destroyed candidates before teardown.
            }
            foreach (RelationalExactComponent candidate in ExactComponentsOn(second.transform))
            {
                UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Rebinding must discard destroyed candidates before teardown.
            }
            AssertExactTypeAssignmentMatchesDirectQueries(tester);
            foreach (
                RelationalExactComponent candidate in root.GetComponentsInChildren<RelationalExactComponent>(
                    true
                )
            )
            {
                UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Rebinding must discard destroyed candidates before teardown.
            }
            AssertExactTypeAssignmentMatchesDirectQueries(tester);
        }

        private GameObject CreateExactTypeCandidates(string name)
        {
            GameObject candidate = Track(new GameObject(name));
            candidate.AddComponent<RelationalExactDerivedComponent>();
            candidate.AddComponent<RelationalExactComponent>();
            candidate.AddComponent<RelationalExactComponent>();
            return candidate;
        }

        private static void AssertExactTypeAssignmentMatchesDirectQueries(
            RelationalExactTypeTester tester
        )
        {
            List<RelationalExactComponent> sibling = ExactComponentsOn(tester.transform);
            List<RelationalExactComponent> parent = new();
            Transform ancestor = tester.transform.parent;
            while (ancestor != null)
            {
                parent.AddRange(ExactComponentsOn(ancestor));
                ancestor = ancestor.parent;
            }
            List<RelationalExactComponent> child = new();
            Queue<Transform> pending = new();
            foreach (Transform directChild in tester.transform)
            {
                pending.Enqueue(directChild);
            }
            while (0 < pending.Count)
            {
                Transform current = pending.Dequeue();
                child.AddRange(ExactComponentsOn(current));
                foreach (Transform descendant in current)
                {
                    pending.Enqueue(descendant);
                }
            }
            tester.AssignRelationalComponents();
            Assert.AreSame(sibling.Count == 0 ? null : sibling[0], tester.siblingExactSingle);
            CollectionAssert.AreEqual(sibling, tester.siblingExactArray);
            CollectionAssert.AreEqual(sibling, tester.siblingExactList);
            CollectionAssert.AreEquivalent(sibling, tester.siblingExactSet);
            Assert.IsTrue(tester.siblingBaseSingle == null);
            CollectionAssert.IsEmpty(tester.siblingBaseArray);
            CollectionAssert.IsEmpty(tester.siblingBaseList);
            CollectionAssert.IsEmpty(tester.siblingBaseSet);
            Assert.IsTrue(tester.siblingComponentSingle == null);
            CollectionAssert.IsEmpty(tester.siblingComponentArray);
            CollectionAssert.IsEmpty(tester.siblingComponentList);
            CollectionAssert.IsEmpty(tester.siblingComponentSet);
            Assert.IsTrue(tester.siblingInterfaceSingle == null);
            CollectionAssert.IsEmpty(tester.siblingInterfaceArray);
            CollectionAssert.IsEmpty(tester.siblingInterfaceList);
            CollectionAssert.IsEmpty(tester.siblingInterfaceSet);
            Assert.AreSame(parent.Count == 0 ? null : parent[0], tester.parentExactSingle);
            CollectionAssert.AreEqual(parent, tester.parentExactArray);
            CollectionAssert.AreEqual(parent, tester.parentExactList);
            CollectionAssert.AreEquivalent(parent, tester.parentExactSet);
            Assert.IsTrue(tester.parentBaseSingle == null);
            CollectionAssert.IsEmpty(tester.parentBaseArray);
            CollectionAssert.IsEmpty(tester.parentBaseList);
            CollectionAssert.IsEmpty(tester.parentBaseSet);
            Assert.IsTrue(tester.parentComponentSingle == null);
            CollectionAssert.IsEmpty(tester.parentComponentArray);
            CollectionAssert.IsEmpty(tester.parentComponentList);
            CollectionAssert.IsEmpty(tester.parentComponentSet);
            Assert.IsTrue(tester.parentInterfaceSingle == null);
            CollectionAssert.IsEmpty(tester.parentInterfaceArray);
            CollectionAssert.IsEmpty(tester.parentInterfaceList);
            CollectionAssert.IsEmpty(tester.parentInterfaceSet);
            Assert.AreSame(child.Count == 0 ? null : child[0], tester.childExactSingle);
            CollectionAssert.AreEqual(child, tester.childExactArray);
            CollectionAssert.AreEqual(child, tester.childExactList);
            CollectionAssert.AreEquivalent(child, tester.childExactSet);
            Assert.IsTrue(tester.childBaseSingle == null);
            CollectionAssert.IsEmpty(tester.childBaseArray);
            CollectionAssert.IsEmpty(tester.childBaseList);
            CollectionAssert.IsEmpty(tester.childBaseSet);
            Assert.IsTrue(tester.childComponentSingle == null);
            CollectionAssert.IsEmpty(tester.childComponentArray);
            CollectionAssert.IsEmpty(tester.childComponentList);
            CollectionAssert.IsEmpty(tester.childComponentSet);
            Assert.IsTrue(tester.childInterfaceSingle == null);
            CollectionAssert.IsEmpty(tester.childInterfaceArray);
            CollectionAssert.IsEmpty(tester.childInterfaceList);
            CollectionAssert.IsEmpty(tester.childInterfaceSet);
        }

        private static List<RelationalExactComponent> ExactComponentsOn(Transform owner)
        {
            List<RelationalExactComponent> expected = new();
            foreach (Component candidate in owner.GetComponents<Component>())
            {
                if (candidate != null && candidate.GetType() == typeof(RelationalExactComponent))
                {
                    expected.Add((RelationalExactComponent)candidate);
                }
            }
            return expected;
        }

        [Test]
        public void AssignmentMatchesDirectQueriesAcrossCacheAndHierarchyChanges(
            [Values(1, 4)] int depth,
            [Values(false, true)] bool inactive,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = ReflectionHelpers.OverrideReflectionCapabilities(
                capability == 0,
                capability == 1
            );
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldSetterCache();

            GameObject root = Track(new GameObject("OracleRoot", typeof(Rigidbody)));
            root.GetComponent<Rigidbody>().isKinematic = true;
            GameObject ancestor = root;
            for (int level = 1; level < depth; level++)
            {
                GameObject next = Track(new GameObject("OracleAncestor"));
                next.transform.SetParent(ancestor.transform);
                ancestor = next;
            }

            GameObject owner = Track(
                new GameObject(
                    "OracleOwner",
                    typeof(RelationalComponentTester),
                    typeof(BoxCollider),
                    typeof(CapsuleCollider),
                    typeof(Rigidbody)
                )
            );
            owner.GetComponent<Rigidbody>().isKinematic = true;
            owner.transform.SetParent(ancestor.transform);
            GameObject child = Track(new GameObject("OracleFirstChild", typeof(CapsuleCollider)));
            child.transform.SetParent(owner.transform);
            GameObject otherChild = Track(
                new GameObject("OracleSecondChild", typeof(CapsuleCollider))
            );
            otherChild.transform.SetParent(owner.transform);
            root.SetActive(!inactive);
            RelationalComponentTester tester = owner.GetComponent<RelationalComponentTester>();

            AssertAssignmentMatchesDirectQueries(tester);
            AssertAssignmentMatchesDirectQueries(tester);

            GameObject unrelated = Track(
                new GameObject("OracleUnrelated", typeof(BoxCollider), typeof(CapsuleCollider))
            );
            unrelated.SetActive(!inactive);
            AssertAssignmentMatchesDirectQueries(tester);

            otherChild.transform.SetAsFirstSibling();
            AssertAssignmentMatchesDirectQueries(tester);
            Assert.AreSame(otherChild.GetComponent<CapsuleCollider>(), tester.childCollider);
        }

        private static void AssertAssignmentMatchesDirectQueries(RelationalComponentTester tester)
        {
            Rigidbody parent = tester.transform.parent.GetComponentInParent<Rigidbody>(true);
            BoxCollider sibling = tester.GetComponent<BoxCollider>();
            CapsuleCollider child = null;
            foreach (
                CapsuleCollider candidate in tester.GetComponentsInChildren<CapsuleCollider>(true)
            )
            {
                if (candidate.transform != tester.transform)
                {
                    child = candidate;
                    break;
                }
            }

            tester.AssignRelationalComponents();

            Assert.AreSame(parent, tester.parentBody);
            Assert.AreSame(sibling, tester.siblingCollider);
            Assert.AreSame(child, tester.childCollider);
        }

        [Test]
        public void AssignRelationalComponentsResolvesParentSiblingAndChild()
        {
            GameObject parent = Track(new GameObject("RelationalParent", typeof(Rigidbody)));
            Rigidbody parentBody = parent.GetComponent<Rigidbody>();

            GameObject middle = new(
                "RelationalMiddle",
                typeof(BoxCollider),
                typeof(RelationalComponentTester)
            );
            middle = Track(middle);
            middle.transform.SetParent(parent.transform);
            BoxCollider siblingCollider = middle.GetComponent<BoxCollider>();

            GameObject child = Track(new GameObject("RelationalChild", typeof(CapsuleCollider)));
            child.transform.SetParent(middle.transform);
            CapsuleCollider childCollider = child.GetComponent<CapsuleCollider>();

            RelationalComponentTester tester = middle.GetComponent<RelationalComponentTester>();
            tester.AssignRelationalComponents();

            Assert.AreSame(parentBody, tester.parentBody);
            Assert.AreSame(siblingCollider, tester.siblingCollider);
            Assert.AreSame(childCollider, tester.childCollider);

            return;
        }
    }
}
