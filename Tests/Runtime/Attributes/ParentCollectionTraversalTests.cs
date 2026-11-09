// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using static WallstopStudios.UnityHelpers.Core.Attributes.RelationalComponentProcessor;

    [TestFixture]
    [Category("Fast")]
    public sealed class ParentCollectionTraversalTests : CommonTestBase
    {
        private static List<T> Query<T>(
            Transform owner,
            bool onlyAncestors,
            bool includeInactive,
            string nameFilter,
            int maximum
        )
            where T : class
        {
            List<T> results = new();
            Transform current = onlyAncestors ? owner.parent : owner;
            int depth = onlyAncestors ? 1 : 0;
            while (current != null && depth <= 3)
            {
                if (
                    (includeInactive || current.gameObject.activeInHierarchy)
                    && (nameFilter == null || current.name.Contains(nameFilter))
                )
                {
                    foreach (Component component in current.GetComponents<Component>())
                    {
                        if (component is T value)
                        {
                            results.Add(value);
                            if (0 < maximum && maximum <= results.Count)
                            {
                                return results;
                            }
                        }
                    }
                }
                current = current.parent;
                ++depth;
            }
            return results;
        }

        [Test]
        public void FilteredCollectionsMatchIndependentQueriesAfterAncestorChanges(
            [Values(false, true)] bool onlyAncestors,
            [Values(false, true)] bool inactive,
            [Values(false, true)] bool includeInactive,
            [Values(false, true)] bool nameFiltered,
            [Values(0, 1, 2)] int maximum
        )
        {
            GameObject owner = Track(new GameObject("MatchOwner"));
            ParentDepthBoundaryTarget target = owner.AddComponent<ParentDepthBoundaryTarget>();
            GameObject current = owner;
            for (int depth = 0; depth < 6; ++depth)
            {
                TestInterfaceComponent component = current.AddComponent<TestInterfaceComponent>();
                component.enabled = false;
                current.AddComponent<RelationalExactComponent>();
                if (depth == 2)
                {
                    current.AddComponent<TestInterfaceComponent>();
                    current.AddComponent<RelationalExactComponent>();
                }
                if (depth == 1 && inactive)
                {
                    current.SetActive(false);
                }
                string parentName = depth % 2 == 0 ? "SkipParent" : "MatchParent";
                GameObject parent = Track(new GameObject(parentName));
                current.transform.SetParent(parent.transform);
                current = parent;
            }
            string nameFilter = nameFiltered ? "Match" : null;
            FieldMetadata<ParentComponentAttribute>[] fields = ParentCollectionQuery.Create(
                typeof(ParentDepthBoundaryTarget),
                elementType: null,
                onlyAncestors,
                maxDepth: 3,
                maximum,
                includeInactive,
                nameFilter
            );
            Assert.AreEqual(9, fields.Length);
            object originalList = null;
            object originalSet = null;
            for (int pass = 0; pass < 3; ++pass)
            {
                if (pass == 2)
                {
                    Transform removed = owner.transform.parent;
                    owner.transform.SetParent(removed.parent);
                    Track(removed.gameObject);
                    UnityEngine.Object.DestroyImmediate(removed.gameObject);
                }
                ParentComponentExtensions.AssignParentComponents(target, fields);
                List<TestInterfaceComponent> sealedExpected = Query<TestInterfaceComponent>(
                    owner.transform,
                    onlyAncestors,
                    includeInactive,
                    nameFilter,
                    maximum
                );
                List<RelationalExactComponent> baseExpected = Query<RelationalExactComponent>(
                    owner.transform,
                    onlyAncestors,
                    includeInactive,
                    nameFilter,
                    maximum
                );
                List<ITestInterface> interfaceExpected = Query<ITestInterface>(
                    owner.transform,
                    onlyAncestors,
                    includeInactive,
                    nameFilter,
                    maximum
                );
                CollectionAssert.AreEqual(sealedExpected, target.sealedArray);
                CollectionAssert.AreEqual(sealedExpected, target.sealedList);
                CollectionAssert.AreEquivalent(sealedExpected, target.sealedSet);
                CollectionAssert.AreEqual(baseExpected, target.baseArray);
                CollectionAssert.AreEqual(baseExpected, target.baseList);
                CollectionAssert.AreEquivalent(baseExpected, target.baseSet);
                CollectionAssert.AreEqual(interfaceExpected, target.interfaceArray);
                CollectionAssert.AreEqual(interfaceExpected, target.interfaceList);
                CollectionAssert.AreEquivalent(interfaceExpected, target.interfaceSet);
                if (pass == 0)
                {
                    originalList = target.sealedList;
                    originalSet = target.sealedSet;
                }
                Assert.AreSame(originalList, target.sealedList);
                Assert.AreSame(originalSet, target.sealedSet);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ParentlessSearchRootCapsMultipleMatches(bool onlyAncestors)
        {
            GameObject owner = Track(new GameObject("ParentlessOwner"));
            ParentDepthBoundaryTarget target = owner.AddComponent<ParentDepthBoundaryTarget>();
            GameObject root = onlyAncestors ? Track(new GameObject("ParentlessRoot")) : owner;
            if (onlyAncestors)
            {
                owner.transform.SetParent(root.transform);
            }
            TestInterfaceComponent first = root.AddComponent<TestInterfaceComponent>();
            root.AddComponent<TestInterfaceComponent>();
            root.AddComponent<TestInterfaceComponent>();
            Assert.IsTrue(root.transform.parent == null);
            FieldMetadata<ParentComponentAttribute>[] fields = ParentCollectionQuery.Create(
                typeof(ParentDepthBoundaryTarget),
                typeof(TestInterfaceComponent),
                onlyAncestors,
                maxDepth: 1,
                maxCount: 1
            );
            Assert.AreEqual(3, fields.Length);
            ParentComponentExtensions.AssignParentComponents(target, fields);
            TestInterfaceComponent[] expected = { first };
            CollectionAssert.AreEqual(expected, target.sealedArray);
            CollectionAssert.AreEqual(expected, target.sealedList);
            CollectionAssert.AreEquivalent(expected, target.sealedSet);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BoundedQueriesCollectBeforeInvokingSetComparers(bool limitCount)
        {
            GameObject owner = Track(new GameObject("ParentCollectionSnapshot"));
            ParentDepthBoundaryTarget target = owner.AddComponent<ParentDepthBoundaryTarget>();
            List<RelationalExactComponent> expected = new();
            Transform current = owner.transform;
            for (int index = 0; index < 4; ++index)
            {
                GameObject parent = Track(new GameObject("Parent" + index));
                expected.Add(parent.AddComponent<RelationalExactComponent>());
                current.SetParent(parent.transform);
                current = parent.transform;
            }
            GameObject changedParent = expected[1].gameObject;
            bool changed = false;
            target.baseSet = new HashSet<RelationalExactComponent>(
                new RelationalCollectionCallbackComparer<RelationalExactComponent>(() =>
                {
                    if (!changed)
                    {
                        changed = true;
                        changedParent.AddComponent<RelationalExactComponent>();
                    }
                })
            );
            FieldMetadata<ParentComponentAttribute>[] fields = ParentCollectionQuery.Create(
                typeof(ParentDepthBoundaryTarget),
                typeof(RelationalExactComponent),
                onlyAncestors: true,
                maxDepth: limitCount ? 0 : 3,
                maxCount: limitCount ? 3 : 0
            );
            Assert.AreEqual(3, fields.Length);
            foreach (FieldMetadata<ParentComponentAttribute> field in fields)
            {
                if (field.kind == FieldKind.HashSet)
                {
                    ParentComponentExtensions.AssignParentComponents(target, new[] { field });
                }
            }
            expected.RemoveAt(3);
            Assert.IsTrue(changed);
            CollectionAssert.AreEquivalent(expected, target.baseSet);
            Assert.AreEqual(2, changedParent.GetComponents<RelationalExactComponent>().Length);
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void BoundedSealedListsAndSetsAllocateNoTemporaryArrays(
            [Values(0, 1, 16)] int count,
            [Values(false, true)] bool limitCount
        )
        {
            object[] retained = new object[1];
            object existing = new();
            Action allocate = () => retained[0] = new object();
            Action reuse = () => retained[0] = existing;
            const int iterations = 128;
            const int capacity = 4096;
            long positive = ProfilerAllocationCounter.Measure(allocate, iterations, capacity);
            if (positive == 0)
            {
                Assert.Ignore("The allocation recorder cannot observe fresh retained objects.");
            }
            Assert.AreEqual(iterations, positive);
            Assert.That(ProfilerAllocationCounter.Measure(reuse, iterations, capacity), Is.Zero);
            GameObject owner = Track(new GameObject("ParentCollectionAllocations"));
            ParentDepthBoundaryTarget target = owner.AddComponent<ParentDepthBoundaryTarget>();
            Transform current = owner.transform;
            for (int index = 0; index < count; ++index)
            {
                GameObject parent = Track(new GameObject("Parent" + index));
                parent.AddComponent<TestInterfaceComponent>();
                current.SetParent(parent.transform);
                current = parent.transform;
            }
            FieldMetadata<ParentComponentAttribute>[] fields = ParentCollectionQuery.Create(
                typeof(ParentDepthBoundaryTarget),
                typeof(TestInterfaceComponent),
                onlyAncestors: true,
                maxDepth: 2,
                maxCount: limitCount ? 1 : 0,
                includeArrays: false
            );
            Assert.AreEqual(2, fields.Length);
            Action assign = () => ParentComponentExtensions.AssignParentComponents(target, fields);
            Assert.That(ProfilerAllocationCounter.Measure(assign, iterations, capacity), Is.Zero);
            Assert.AreEqual(Math.Min(count, limitCount ? 1 : 2), target.sealedList.Count);
            Assert.AreEqual(Math.Min(count, limitCount ? 1 : 2), target.sealedSet.Count);
        }
#endif
    }
}
