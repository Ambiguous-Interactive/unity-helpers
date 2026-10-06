// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class LegacyCollectionComparerTests
    {
        private static void AssertComparers<T>(T first, T equivalent, T different, int operation)
            where T : class
        {
            switch (operation)
            {
                case 0:
                    Assert.IsTrue(first.ProtoEquals(equivalent));
                    Assert.IsFalse(first.ProtoEquals(different));
                    Assert.IsTrue(ProtoEqualityExtensions.ProtoEquals<object>(first, equivalent));
                    Assert.IsFalse(ProtoEqualityExtensions.ProtoEquals<object>(first, different));
                    Assert.IsFalse(first.ProtoEquals(null));
                    Assert.IsTrue(ProtoEqualityExtensions.ProtoEquals<T>(null, null));
                    break;
                case 1:
                    IEqualityComparer<T> comparer = ProtoEqualityExtensions.GetProtoComparer<T>();
                    Assert.IsTrue(comparer.Equals(first, equivalent));
                    Assert.IsFalse(comparer.Equals(first, different));
                    Assert.IsFalse(comparer.Equals(first, null));
                    Assert.IsTrue(comparer.Equals(null, null));
                    IEqualityComparer<object> objectComparer =
                        ProtoEqualityExtensions.GetProtoComparer<object>();
                    Assert.IsTrue(objectComparer.Equals(first, equivalent));
                    Assert.IsFalse(objectComparer.Equals(first, different));
                    Assert.IsFalse(objectComparer.Equals(first, null));
                    Assert.IsTrue(objectComparer.Equals(null, null));
                    break;
                default:
                    IEqualityComparer<T> hashComparer =
                        ProtoEqualityExtensions.GetProtoComparer<T>();
                    int hash = hashComparer.GetHashCode(first);
                    Assert.AreEqual(hash, hashComparer.GetHashCode(equivalent));
                    Assert.AreNotEqual(hash, hashComparer.GetHashCode(different));
                    Assert.AreEqual(
                        hash,
                        ProtoEqualityExtensions.GetProtoComparer<object>().GetHashCode(first)
                    );
                    Assert.AreEqual(-2128831035, hashComparer.GetHashCode(null));
                    break;
            }
        }

        private static SparseSet CreateSparseSet(int[] items, int capacity)
        {
            SparseSet result = new(capacity);
            foreach (int item in items)
            {
                Assert.IsTrue(result.TryAdd(item));
            }
            return result;
        }

        [Test, Combinatorial]
        public void RootCollectionsPreserveValuesAcrossComparerEntryPoints(
            [Values(0, 1, 2, 3, 4)] int family,
            [Values(0, 1, 32, 10000)] int count,
            [Values(0, 1, 2)] int operation
        )
        {
            int[] items = new int[count];
            int[] changed = new int[count == 0 ? 1 : count];
            for (int index = 0; index < count; ++index)
            {
                items[index] = index;
                changed[index] = index;
            }
            changed[changed.Length - 1] = count;
            int capacity = count + 1;
            switch (family)
            {
                case 0:
                    AssertComparers(
                        new SerializableHashSet<int>(items),
                        new SerializableHashSet<int>(items),
                        new SerializableHashSet<int>(changed),
                        operation
                    );
                    break;
                case 1:
                    AssertComparers(
                        new SerializableSortedSet<int>(items),
                        new SerializableSortedSet<int>(items),
                        new SerializableSortedSet<int>(changed),
                        operation
                    );
                    break;
                case 2:
                    AssertComparers(
                        new Deque<int>(items),
                        new Deque<int>(items),
                        new Deque<int>(changed),
                        operation
                    );
                    break;
                case 3:
                    AssertComparers(
                        new CyclicBuffer<int>(capacity, items),
                        new CyclicBuffer<int>(capacity, items),
                        new CyclicBuffer<int>(capacity, changed),
                        operation
                    );
                    break;
                default:
                    AssertComparers(
                        CreateSparseSet(items, capacity),
                        CreateSparseSet(items, capacity),
                        CreateSparseSet(changed, capacity),
                        operation
                    );
                    break;
            }
        }
    }
}
