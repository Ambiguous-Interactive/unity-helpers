// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    /// <summary>
    /// Shares the direct-query exact-binding oracle across EditMode and PlayMode fixtures.
    /// </summary>
    public abstract class RelationalExactBindingTestBase : CommonTestBase
    {
        protected void VerifyExactTypeAssignment(int depth, bool inactive, int capability)
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
    }
}
