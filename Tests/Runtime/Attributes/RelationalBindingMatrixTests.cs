// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [Category("Fast")]
    public sealed class RelationalBindingMatrixTests : CommonTestBase
    {
        private const int SiblingDirection = 0;
        private const int ParentDirection = 1;
        private const int ChildDirection = 2;

        private static void ClearBindingCaches()
        {
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
        }

        private static List<T> Query<T>(
            Transform owner,
            int direction,
            bool excludeSelf,
            bool activeOnly,
            bool filtered,
            int maxDepth,
            int maxCount
        )
            where T : class
        {
            List<T> result = new();
            Queue<(Transform transform, int depth)> pending = new();
            pending.Enqueue((owner, 0));
            while (pending.TryDequeue(out (Transform transform, int depth) item))
            {
                if (!excludeSelf || item.depth != 0 || direction == SiblingDirection)
                {
                    foreach (Component candidate in item.transform.GetComponents<Component>())
                    {
                        if (candidate == null || !(candidate is T value))
                        {
                            continue;
                        }
                        if (
                            activeOnly
                            && (
                                !candidate.gameObject.activeInHierarchy
                                || (
                                    direction != ParentDirection
                                    && candidate is Behaviour behaviour
                                    && !behaviour.enabled
                                )
                            )
                        )
                        {
                            continue;
                        }
                        if (
                            filtered
                            && (
                                !candidate.gameObject.CompareTag("Player")
                                || !candidate.gameObject.name.Contains("Match")
                            )
                        )
                        {
                            continue;
                        }
                        result.Add(value);
                        if (0 < maxCount && maxCount <= result.Count)
                        {
                            return result;
                        }
                    }
                }
                if (0 < maxDepth && maxDepth <= item.depth)
                {
                    continue;
                }
                if (direction == ParentDirection && item.transform.parent != null)
                {
                    pending.Enqueue((item.transform.parent, item.depth + 1));
                }
                else if (direction == ChildDirection)
                {
                    foreach (Transform child in item.transform)
                    {
                        pending.Enqueue((child, item.depth + 1));
                    }
                }
            }
            return result;
        }

        private static void AssertShapes<T>(
            List<T> expected,
            T single,
            T[] array,
            List<T> list,
            HashSet<T> set,
            string label
        )
            where T : class
        {
            Assert.AreSame(expected.Count == 0 ? null : expected[0], single, label + ".Single");
            Assert.IsTrue(array != null, label + ".Array must be assigned even when empty.");
            Assert.IsTrue(list != null, label + ".List must be assigned even when empty.");
            Assert.IsTrue(set != null, label + ".Set must be assigned even when empty.");
            CollectionAssert.AreEqual(expected, array, label + ".Array");
            CollectionAssert.AreEqual(expected, list, label + ".List");
            CollectionAssert.AreEquivalent(expected, set, label + ".Set");
        }

        private static void AssertBindings(RelationalBindingMatrixTarget target)
        {
            target.AssignRelationalComponents();
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    SiblingDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allSiblingConcreteSingle,
                target.allSiblingConcreteArray,
                target.allSiblingConcreteList,
                target.allSiblingConcreteSet,
                "allSiblingConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    SiblingDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allSiblingSealedSingle,
                target.allSiblingSealedArray,
                target.allSiblingSealedList,
                target.allSiblingSealedSet,
                "allSiblingSealed"
            );
            AssertShapes(
                Query<ITestInterface>(
                    target.transform,
                    SiblingDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allSiblingInterfaceSingle,
                target.allSiblingInterfaceArray,
                target.allSiblingInterfaceList,
                target.allSiblingInterfaceSet,
                "allSiblingInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ParentDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allParentConcreteSingle,
                target.allParentConcreteArray,
                target.allParentConcreteList,
                target.allParentConcreteSet,
                "allParentConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ParentDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allParentSealedSingle,
                target.allParentSealedArray,
                target.allParentSealedList,
                target.allParentSealedSet,
                "allParentSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ParentDirection, false, false, false, 0, 0),
                target.allParentInterfaceSingle,
                target.allParentInterfaceArray,
                target.allParentInterfaceList,
                target.allParentInterfaceSet,
                "allParentInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ChildDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allChildConcreteSingle,
                target.allChildConcreteArray,
                target.allChildConcreteList,
                target.allChildConcreteSet,
                "allChildConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ChildDirection,
                    false,
                    false,
                    false,
                    0,
                    0
                ),
                target.allChildSealedSingle,
                target.allChildSealedArray,
                target.allChildSealedList,
                target.allChildSealedSet,
                "allChildSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ChildDirection, false, false, false, 0, 0),
                target.allChildInterfaceSingle,
                target.allChildInterfaceArray,
                target.allChildInterfaceList,
                target.allChildInterfaceSet,
                "allChildInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    SiblingDirection,
                    true,
                    true,
                    false,
                    0,
                    0
                ),
                target.activeSiblingConcreteSingle,
                target.activeSiblingConcreteArray,
                target.activeSiblingConcreteList,
                target.activeSiblingConcreteSet,
                "activeSiblingConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    SiblingDirection,
                    true,
                    true,
                    false,
                    0,
                    0
                ),
                target.activeSiblingSealedSingle,
                target.activeSiblingSealedArray,
                target.activeSiblingSealedList,
                target.activeSiblingSealedSet,
                "activeSiblingSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, SiblingDirection, true, true, false, 0, 0),
                target.activeSiblingInterfaceSingle,
                target.activeSiblingInterfaceArray,
                target.activeSiblingInterfaceList,
                target.activeSiblingInterfaceSet,
                "activeSiblingInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ParentDirection,
                    true,
                    true,
                    false,
                    0,
                    0
                ),
                target.activeParentConcreteSingle,
                target.activeParentConcreteArray,
                target.activeParentConcreteList,
                target.activeParentConcreteSet,
                "activeParentConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ParentDirection,
                    true,
                    true,
                    false,
                    0,
                    0
                ),
                target.activeParentSealedSingle,
                target.activeParentSealedArray,
                target.activeParentSealedList,
                target.activeParentSealedSet,
                "activeParentSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ParentDirection, true, true, false, 0, 0),
                target.activeParentInterfaceSingle,
                target.activeParentInterfaceArray,
                target.activeParentInterfaceList,
                target.activeParentInterfaceSet,
                "activeParentInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ChildDirection,
                    true,
                    true,
                    false,
                    0,
                    0
                ),
                target.activeChildConcreteSingle,
                target.activeChildConcreteArray,
                target.activeChildConcreteList,
                target.activeChildConcreteSet,
                "activeChildConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ChildDirection,
                    true,
                    true,
                    false,
                    0,
                    0
                ),
                target.activeChildSealedSingle,
                target.activeChildSealedArray,
                target.activeChildSealedList,
                target.activeChildSealedSet,
                "activeChildSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ChildDirection, true, true, false, 0, 0),
                target.activeChildInterfaceSingle,
                target.activeChildInterfaceArray,
                target.activeChildInterfaceList,
                target.activeChildInterfaceSet,
                "activeChildInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    SiblingDirection,
                    true,
                    true,
                    true,
                    0,
                    2
                ),
                target.filteredSiblingConcreteSingle,
                target.filteredSiblingConcreteArray,
                target.filteredSiblingConcreteList,
                target.filteredSiblingConcreteSet,
                "filteredSiblingConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    SiblingDirection,
                    true,
                    true,
                    true,
                    0,
                    2
                ),
                target.filteredSiblingSealedSingle,
                target.filteredSiblingSealedArray,
                target.filteredSiblingSealedList,
                target.filteredSiblingSealedSet,
                "filteredSiblingSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, SiblingDirection, true, true, true, 0, 2),
                target.filteredSiblingInterfaceSingle,
                target.filteredSiblingInterfaceArray,
                target.filteredSiblingInterfaceList,
                target.filteredSiblingInterfaceSet,
                "filteredSiblingInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ParentDirection,
                    true,
                    true,
                    true,
                    0,
                    2
                ),
                target.filteredParentConcreteSingle,
                target.filteredParentConcreteArray,
                target.filteredParentConcreteList,
                target.filteredParentConcreteSet,
                "filteredParentConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ParentDirection,
                    true,
                    true,
                    true,
                    0,
                    2
                ),
                target.filteredParentSealedSingle,
                target.filteredParentSealedArray,
                target.filteredParentSealedList,
                target.filteredParentSealedSet,
                "filteredParentSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ParentDirection, true, true, true, 0, 2),
                target.filteredParentInterfaceSingle,
                target.filteredParentInterfaceArray,
                target.filteredParentInterfaceList,
                target.filteredParentInterfaceSet,
                "filteredParentInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ChildDirection,
                    true,
                    true,
                    true,
                    0,
                    2
                ),
                target.filteredChildConcreteSingle,
                target.filteredChildConcreteArray,
                target.filteredChildConcreteList,
                target.filteredChildConcreteSet,
                "filteredChildConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ChildDirection,
                    true,
                    true,
                    true,
                    0,
                    2
                ),
                target.filteredChildSealedSingle,
                target.filteredChildSealedArray,
                target.filteredChildSealedList,
                target.filteredChildSealedSet,
                "filteredChildSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ChildDirection, true, true, true, 0, 2),
                target.filteredChildInterfaceSingle,
                target.filteredChildInterfaceArray,
                target.filteredChildInterfaceList,
                target.filteredChildInterfaceSet,
                "filteredChildInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    SiblingDirection,
                    true,
                    false,
                    false,
                    0,
                    3
                ),
                target.boundedSiblingConcreteSingle,
                target.boundedSiblingConcreteArray,
                target.boundedSiblingConcreteList,
                target.boundedSiblingConcreteSet,
                "boundedSiblingConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    SiblingDirection,
                    true,
                    false,
                    false,
                    0,
                    3
                ),
                target.boundedSiblingSealedSingle,
                target.boundedSiblingSealedArray,
                target.boundedSiblingSealedList,
                target.boundedSiblingSealedSet,
                "boundedSiblingSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, SiblingDirection, true, false, false, 0, 3),
                target.boundedSiblingInterfaceSingle,
                target.boundedSiblingInterfaceArray,
                target.boundedSiblingInterfaceList,
                target.boundedSiblingInterfaceSet,
                "boundedSiblingInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ParentDirection,
                    true,
                    false,
                    false,
                    2,
                    3
                ),
                target.boundedParentConcreteSingle,
                target.boundedParentConcreteArray,
                target.boundedParentConcreteList,
                target.boundedParentConcreteSet,
                "boundedParentConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ParentDirection,
                    true,
                    false,
                    false,
                    2,
                    3
                ),
                target.boundedParentSealedSingle,
                target.boundedParentSealedArray,
                target.boundedParentSealedList,
                target.boundedParentSealedSet,
                "boundedParentSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ParentDirection, true, false, false, 2, 3),
                target.boundedParentInterfaceSingle,
                target.boundedParentInterfaceArray,
                target.boundedParentInterfaceList,
                target.boundedParentInterfaceSet,
                "boundedParentInterface"
            );
            AssertShapes(
                Query<RelationalExactComponent>(
                    target.transform,
                    ChildDirection,
                    true,
                    false,
                    false,
                    2,
                    3
                ),
                target.boundedChildConcreteSingle,
                target.boundedChildConcreteArray,
                target.boundedChildConcreteList,
                target.boundedChildConcreteSet,
                "boundedChildConcrete"
            );
            AssertShapes(
                Query<TestInterfaceComponent>(
                    target.transform,
                    ChildDirection,
                    true,
                    false,
                    false,
                    2,
                    3
                ),
                target.boundedChildSealedSingle,
                target.boundedChildSealedArray,
                target.boundedChildSealedList,
                target.boundedChildSealedSet,
                "boundedChildSealed"
            );
            AssertShapes(
                Query<ITestInterface>(target.transform, ChildDirection, true, false, false, 2, 3),
                target.boundedChildInterfaceSingle,
                target.boundedChildInterfaceArray,
                target.boundedChildInterfaceList,
                target.boundedChildInterfaceSet,
                "boundedChildInterface"
            );
        }

        [TearDown]
        public void ClearMetadataAfterCapabilityScope()
        {
            ClearBindingCaches();
        }

        [Test]
        public void BindingMatchesIndependentQueriesAcrossStatesAndMutations(
            [Values(0, 1, 3)] int matchesPerNode,
            [Values(1, 3)] int depth,
            [Values(false, true)] bool inactive,
            [Values(false, true)] bool disabled,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearBindingCaches();
            GameObject root = CreateCandidates("MatchRoot", matchesPerNode, disabled);
            GameObject ancestor = root;
            for (int level = 1; level < depth; ++level)
            {
                GameObject next = CreateCandidates("MatchAncestor", matchesPerNode, disabled);
                next.transform.SetParent(ancestor.transform);
                ancestor = next;
            }
            GameObject owner = CreateCandidates("MatchOwner", matchesPerNode, disabled);
            owner.transform.SetParent(ancestor.transform);
            RelationalBindingMatrixTarget target =
                owner.AddComponent<RelationalBindingMatrixTarget>();
            GameObject first = CreateCandidates("MatchFirst", matchesPerNode, disabled);
            first.transform.SetParent(owner.transform);
            GameObject deep = first;
            for (int level = 0; level < depth; ++level)
            {
                GameObject next = CreateCandidates("MatchDescendant", matchesPerNode, disabled);
                next.transform.SetParent(deep.transform);
                deep = next;
            }
            GameObject wrongName = CreateCandidates("Decoy", matchesPerNode, false);
            wrongName.transform.SetParent(owner.transform);
            GameObject wrongTag = CreateCandidates("MatchWrongTag", matchesPerNode, false);
            wrongTag.tag = "Untagged";
            wrongTag.transform.SetParent(owner.transform);
            GameObject second = CreateCandidates("MatchSecond", matchesPerNode, false);
            second.transform.SetParent(owner.transform);
            first.SetActive(!inactive);
            root.SetActive(!inactive);

            AssertBindings(target);
            AssertBindings(target);
            CreateCandidates("MatchUnrelated", matchesPerNode, false);
            AssertBindings(target);
            second.transform.SetAsFirstSibling();
            AssertBindings(target);
            root.SetActive(true);
            first.SetActive(true);
            foreach (
                RelationalExactComponent candidate in root.GetComponentsInChildren<RelationalExactComponent>(
                    true
                )
            )
            {
                candidate.enabled = !candidate.enabled;
            }
            AssertBindings(target);
            first.transform.SetParent(null);
            AssertBindings(target);
            foreach (
                Component candidate in owner.GetComponentsInParent<RelationalExactComponent>(true)
            )
            {
                UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Destroy candidates to verify stale bindings are replaced.
            }
            foreach (
                RelationalExactComponent candidate in owner.GetComponentsInChildren<RelationalExactComponent>(
                    true
                )
            )
            {
                UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Destroy candidates to verify stale bindings are replaced.
            }
            foreach (
                TestInterfaceComponent candidate in root.GetComponentsInChildren<TestInterfaceComponent>(
                    true
                )
            )
            {
                UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Remove the final interface matches to verify empty assignments.
            }
            AssertBindings(target);

            GameObject farParent = CreateCandidates("MatchFarParent", 1, false);
            GameObject middleParent = Track(new GameObject("EmptyMiddleParent"));
            middleParent.transform.SetParent(farParent.transform);
            GameObject nearParent = Track(new GameObject("EmptyNearParent"));
            nearParent.transform.SetParent(middleParent.transform);
            owner.transform.SetParent(nearParent.transform);
            GameObject nearChild = Track(new GameObject("EmptyNearChild"));
            nearChild.transform.SetParent(owner.transform);
            GameObject middleChild = Track(new GameObject("EmptyMiddleChild"));
            middleChild.transform.SetParent(nearChild.transform);
            GameObject farChild = CreateCandidates("MatchFarChild", 1, false);
            farChild.transform.SetParent(middleChild.transform);
            AssertBindings(target);
        }

        private GameObject CreateCandidates(string name, int count, bool disabled)
        {
            GameObject result = Track(new GameObject(name));
            result.tag = "Player";
            for (int index = 0; index < count; ++index)
            {
                result.AddComponent<RelationalExactDerivedComponent>().enabled = !disabled;
                result.AddComponent<RelationalExactComponent>().enabled = !disabled;
                result.AddComponent<TestInterfaceComponent>().enabled = !disabled;
            }
            return result;
        }
    }
}
