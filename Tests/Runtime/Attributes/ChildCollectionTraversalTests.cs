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

    [TestFixture]
    [Category("Fast")]
    public sealed class ChildCollectionTraversalTests : CommonTestBase
    {
        [TestCase(false, 1)]
        [TestCase(true, 0)]
        public void SingleQueryEntriesPreserveMissingComponentHandling(bool missing, int expected)
        {
            GameObject root = Track(new GameObject("SingleChildQuery"));
            ChildComponentAttribute attribute = new();
            RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute> metadata = new(
                field: null,
                attribute,
                new RelationalComponentProcessor.FilterParameters(attribute),
                accessor: null,
                RelationalComponentProcessor.FieldKind.Array,
                typeof(Component),
                arrayCreator: null,
                listCreator: null,
                hashSetCreator: null,
                hashSetAdder: null,
                hashSetClearer: null,
                isInterface: false
            );
            List<Component> source = new() { missing ? null : root.transform };
            Assert.AreEqual(
                expected,
                ChildComponentExtensions.FilterChildren(root.transform, metadata, source)
            );
        }

        [Test]
        public void CollectionQueriesMatchIndependentOracleAcrossHierarchyChanges(
            [Values(0, 1, 4)] int width,
            [Values(1, 3)] int depth,
            [Values(false, true)] bool inactive,
            [Values(false, true)] bool sealedType
        )
        {
            GameObject root = Track(new GameObject("ChildCollectionOracle"));
            root.AddComponent<BoxCollider>();
            root.AddComponent<SpriteRenderer>();
            RelationalChildCollectionTarget target = sealedType
                ? null
                : root.AddComponent<RelationalChildCollectionTarget>();
            RelationalChildSealedCollectionTarget sealedTarget = sealedType
                ? root.AddComponent<RelationalChildSealedCollectionTarget>()
                : null;
            Component owner = sealedType ? sealedTarget : target;
            List<Transform> level = new() { root.transform };
            for (int currentDepth = 0; currentDepth < depth; ++currentDepth)
            {
                List<Transform> next = new();
                foreach (Transform parent in level)
                {
                    for (int sibling = 0; sibling < width; ++sibling)
                    {
                        GameObject child = Track(new GameObject($"Child{currentDepth}-{sibling}"));
                        child.transform.SetParent(parent);
                        child.AddComponent<BoxCollider>();
                        child.AddComponent<BoxCollider>();
                        child.AddComponent<SpriteRenderer>();
                        child.SetActive(!inactive || sibling != 0);
                        next.Add(child.transform);
                    }
                }
                level = next;
            }
            object originalList = null;
            object originalSet = null;
            for (int repetition = 0; repetition < 3; ++repetition)
            {
                if (repetition == 0)
                {
                    ChildComponentExtensions.ClearCachedFieldMetadata();
                }
                if (0 < root.transform.childCount)
                {
                    root.transform.GetChild(0).SetAsLastSibling();
                }
                if (repetition == 2 && 0 < root.transform.childCount)
                {
                    GameObject removedChild = root.transform.GetChild(0).gameObject;
                    Track(removedChild);
                    UnityEngine.Object.DestroyImmediate(removedChild);
                }
                owner.AssignChildComponents();
                if (sealedType)
                {
                    List<SpriteRenderer> expected = Query<SpriteRenderer>(root.transform);
                    CollectionAssert.AreEqual(expected, sealedTarget.array);
                    CollectionAssert.AreEqual(expected, sealedTarget.list);
                    CollectionAssert.AreEquivalent(expected, sealedTarget.set);
                    if (repetition == 0)
                    {
                        originalList = sealedTarget.list;
                        originalSet = sealedTarget.set;
                    }
                    Assert.AreSame(originalList, sealedTarget.list);
                    Assert.AreSame(originalSet, sealedTarget.set);
                }
                else
                {
                    List<BoxCollider> expected = Query<BoxCollider>(root.transform);
                    CollectionAssert.AreEqual(expected, target.array);
                    CollectionAssert.AreEqual(expected, target.list);
                    CollectionAssert.AreEquivalent(expected, target.set);
                    if (repetition == 0)
                    {
                        originalList = target.list;
                        originalSet = target.set;
                    }
                    Assert.AreSame(originalList, target.list);
                    Assert.AreSame(originalSet, target.set);
                }
            }
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void ReusedSealedListsAndSetsHaveNoProfilerAllocationEvents(
            [Values(0, 1, 64)] int count,
            [Values(false, true)] bool bounded
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
            GameObject root = Track(new GameObject("ChildCollectionAllocations"));
            RelationalChildSealedCollectionTarget owner =
                root.AddComponent<RelationalChildSealedCollectionTarget>();
            for (int index = 0; index < count; ++index)
            {
                GameObject child = Track(new GameObject($"Child{index}", typeof(SpriteRenderer)));
                child.transform.SetParent(root.transform);
            }
            List<RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute>> selected =
                new();
            foreach (
                RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute> field in ChildComponentExtensions.GetOrCreateFields(
                    owner.GetType()
                )
            )
            {
                if (field.kind != RelationalComponentProcessor.FieldKind.Array)
                {
                    selected.Add(field);
                }
            }
            Assert.AreEqual(2, selected.Count);
            RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute>[] fields =
                selected.ToArray();
            int[] originalDepths = new int[fields.Length];
            for (int index = 0; index < fields.Length; ++index)
            {
                originalDepths[index] = fields[index].attribute.MaxDepth;
            }
            try
            {
                foreach (
                    RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute> field in fields
                )
                {
                    field.attribute.MaxDepth = bounded ? 1 : 0;
                }
                Action assign = () => ChildComponentExtensions.AssignChildComponents(owner, fields);
                Assert.That(
                    ProfilerAllocationCounter.Measure(assign, iterations, capacity),
                    Is.Zero
                );
                Assert.AreEqual(count, owner.list.Count);
                Assert.AreEqual(count, owner.set.Count);
            }
            finally
            {
                for (int index = 0; index < fields.Length; ++index)
                {
                    fields[index].attribute.MaxDepth = originalDepths[index];
                }
            }
        }
#endif

        private static List<T> Query<T>(Transform root)
            where T : Component
        {
            List<T> results = new();
            Queue<Transform> pending = new();
            foreach (Transform child in root)
            {
                pending.Enqueue(child);
            }
            while (pending.TryDequeue(out Transform current))
            {
                results.AddRange(current.GetComponents<T>());
                foreach (Transform child in current)
                {
                    pending.Enqueue(child);
                }
            }
            return results;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BoundedQueriesCollectBeforeInvokingSetComparers(bool limitCount)
        {
            GameObject root = Track(new GameObject("BoundedCollectionSnapshot"));
            RelationalChildCollectionTarget target =
                root.AddComponent<RelationalChildCollectionTarget>();
            List<BoxCollider> expected = new();
            for (int index = 0; index < 4; ++index)
            {
                GameObject child = Track(new GameObject($"Child{index}"));
                child.transform.SetParent(root.transform);
                expected.Add(child.AddComponent<BoxCollider>());
            }
            GameObject changedChild = expected[1].gameObject;
            bool changed = false;
            target.set = new HashSet<BoxCollider>(
                new RelationalCollectionCallbackComparer<BoxCollider>(() =>
                {
                    if (!changed)
                    {
                        changed = true;
                        changedChild.AddComponent<BoxCollider>();
                    }
                })
            );
            RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute>[] fields =
                ChildComponentExtensions.GetOrCreateFields(typeof(RelationalChildCollectionTarget));
            if (limitCount)
            {
                expected.RemoveAt(3);
            }
            foreach (
                RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute> field in fields
            )
            {
                if (field.kind != RelationalComponentProcessor.FieldKind.HashSet)
                {
                    continue;
                }
                int originalMaxCount = field.attribute.MaxCount;
                int originalMaxDepth = field.attribute.MaxDepth;
                try
                {
                    field.attribute.MaxCount = limitCount ? 3 : 0;
                    field.attribute.MaxDepth = limitCount ? 0 : 1;
                    ChildComponentExtensions.AssignChildComponents(target, new[] { field });
                    CollectionAssert.AreEquivalent(expected, target.set);
                    Assert.AreEqual(2, changedChild.GetComponents<BoxCollider>().Length);
                }
                finally
                {
                    field.attribute.MaxCount = originalMaxCount;
                    field.attribute.MaxDepth = originalMaxDepth;
                }
            }
            Assert.IsTrue(changed);
        }

        [Test]
        public void ReentrantSetComparersKeepNestedQueryBuffersSeparate(
            [Values(false, true)] bool sealedType
        )
        {
            GameObject outerRoot = Track(new GameObject("OuterCollection"));
            GameObject innerRoot = Track(new GameObject("InnerCollection"));
            Component outer = sealedType
                ? outerRoot.AddComponent<RelationalChildSealedCollectionTarget>()
                : outerRoot.AddComponent<RelationalChildCollectionTarget>();
            Component inner = sealedType
                ? innerRoot.AddComponent<RelationalChildSealedCollectionTarget>()
                : innerRoot.AddComponent<RelationalChildCollectionTarget>();
            Type elementType = sealedType ? typeof(SpriteRenderer) : typeof(BoxCollider);
            for (int index = 0; index < 4; ++index)
            {
                GameObject child = Track(new GameObject($"Outer{index}", elementType));
                child.transform.SetParent(outerRoot.transform);
            }
            for (int index = 0; index < 2; ++index)
            {
                GameObject child = Track(new GameObject($"Inner{index}", elementType));
                child.transform.SetParent(innerRoot.transform);
            }
            int callbacks = 0;
            Action nested = () =>
            {
                inner.AssignChildComponents();
                ++callbacks;
            };
            if (sealedType)
            {
                RelationalChildSealedCollectionTarget target =
                    (RelationalChildSealedCollectionTarget)outer;
                target.set = new HashSet<SpriteRenderer>(
                    new RelationalCollectionCallbackComparer<SpriteRenderer>(nested)
                );
                outer.AssignChildComponents();
                CollectionAssert.AreEquivalent(
                    Query<SpriteRenderer>(outerRoot.transform),
                    target.set
                );
                RelationalChildSealedCollectionTarget innerTarget =
                    (RelationalChildSealedCollectionTarget)inner;
                CollectionAssert.AreEquivalent(
                    Query<SpriteRenderer>(innerRoot.transform),
                    innerTarget.set
                );
            }
            else
            {
                RelationalChildCollectionTarget target = (RelationalChildCollectionTarget)outer;
                target.set = new HashSet<BoxCollider>(
                    new RelationalCollectionCallbackComparer<BoxCollider>(nested)
                );
                outer.AssignChildComponents();
                CollectionAssert.AreEquivalent(Query<BoxCollider>(outerRoot.transform), target.set);
                RelationalChildCollectionTarget innerTarget =
                    (RelationalChildCollectionTarget)inner;
                CollectionAssert.AreEquivalent(
                    Query<BoxCollider>(innerRoot.transform),
                    innerTarget.set
                );
            }
            Assert.That(callbacks, Is.GreaterThan(0));
        }
    }
}
