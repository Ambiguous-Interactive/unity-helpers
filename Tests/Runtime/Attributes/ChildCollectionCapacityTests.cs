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
    public sealed class ChildCollectionCapacityTests : CommonTestBase
    {
        [Test]
        public void ExistingCollectionsKeepCapacityWhenMatchesDisappear()
        {
            GameObject root = Track(new GameObject("ReuseRoot"));
            ChildCollectionCapacityTarget target =
                root.AddComponent<ChildCollectionCapacityTarget>();
            GameObject oldMatch = Track(new GameObject("OldMatch"));
            List<Transform> list = new(128) { oldMatch.transform };
            HashSet<Transform> set = new(128) { oldMatch.transform };
            int listCapacity = list.Capacity;
            int setCapacity = set.EnsureCapacity(0);
            target.childrenList = list;
            target.childrenSet = set;

            target.AssignChildComponents();

            Assert.AreSame(list, target.childrenList);
            Assert.AreSame(set, target.childrenSet);
            CollectionAssert.IsEmpty(target.childrenArray);
            CollectionAssert.IsEmpty(target.childrenList);
            CollectionAssert.IsEmpty(target.childrenSet);
            Assert.AreEqual(listCapacity, list.Capacity);
            Assert.AreEqual(setCapacity, set.EnsureCapacity(0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(int.MaxValue)]
        public void ResultLimitsPreserveBreadthFirstOrderAndReuse(int limit)
        {
            GameObject root = Track(new GameObject("LimitRoot"));
            ChildCollectionCapacityTarget target =
                root.AddComponent<ChildCollectionCapacityTarget>();
            GameObject first = Track(new GameObject("FirstChild"));
            first.transform.SetParent(root.transform);
            GameObject grandchild = Track(new GameObject("Grandchild"));
            grandchild.transform.SetParent(first.transform);
            GameObject second = Track(new GameObject("SecondChild"));
            second.transform.SetParent(root.transform);
            List<Transform> expected = new()
            {
                first.transform,
                second.transform,
                grandchild.transform,
            };
            if (0 < limit && limit < expected.Count)
            {
                expected.RemoveRange(limit, expected.Count - limit);
            }
            RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute>[] fields =
                ChildComponentExtensions.GetOrCreateFields(typeof(ChildCollectionCapacityTarget));
            try
            {
                foreach (
                    RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute> field in fields
                )
                {
                    field.attribute.MaxCount = limit;
                }
                target.AssignChildComponents();
                List<Transform> assignedList = target.childrenList;
                HashSet<Transform> assignedSet = target.childrenSet;
                target.AssignChildComponents();
                CollectionAssert.AreEqual(expected, target.childrenArray);
                CollectionAssert.AreEqual(expected, target.childrenList);
                CollectionAssert.AreEquivalent(expected, target.childrenSet);
                Assert.AreSame(assignedList, target.childrenList);
                Assert.AreSame(assignedSet, target.childrenSet);
                Assert.That(assignedList.Capacity, Is.LessThanOrEqualTo(4));
                Assert.That(assignedSet.EnsureCapacity(0), Is.LessThanOrEqualTo(7));
            }
            finally
            {
                foreach (
                    RelationalComponentProcessor.FieldMetadata<ChildComponentAttribute> field in fields
                )
                {
                    field.attribute.MaxCount = 65536;
                }
            }
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(7, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(7, true)]
        public void LargeResultLimitAllocatesForActualMatches(int childCount, bool inspectSet)
        {
            const int smallHierarchyCapacityLimit = 64;
            GameObject root = Track(new GameObject("CapacityRoot"));
            ChildCollectionCapacityTarget target =
                root.AddComponent<ChildCollectionCapacityTarget>();
            List<Transform> expected = new();
            for (int index = 0; index < childCount; ++index)
            {
                GameObject child = Track(new GameObject("CapacityChild"));
                child.transform.SetParent(root.transform);
                expected.Add(child.transform);
            }

            target.AssignChildComponents();

            CollectionAssert.AreEqual(expected, target.childrenArray);
            CollectionAssert.AreEqual(expected, target.childrenList);
            CollectionAssert.AreEquivalent(expected, target.childrenSet);
            int capacity = inspectSet
                ? target.childrenSet.EnsureCapacity(0)
                : target.childrenList.Capacity;
            Assert.That(capacity, Is.LessThanOrEqualTo(smallHierarchyCapacityLimit));

            List<Transform> assignedList = target.childrenList;
            HashSet<Transform> assignedSet = target.childrenSet;
            target.AssignChildComponents();

            Assert.AreSame(assignedList, target.childrenList);
            Assert.AreSame(assignedSet, target.childrenSet);
            CollectionAssert.AreEqual(expected, target.childrenArray);
            CollectionAssert.AreEqual(expected, target.childrenList);
            CollectionAssert.AreEquivalent(expected, target.childrenSet);
        }
    }
}
