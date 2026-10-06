// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
#if !WALLSTOP_PROTO_ONLY
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class LegacyColdCollectionProviderTests
    {
        private static void VerifyColdRoot<TCollection, TItem>(
            byte[] wire,
            bool metadataFirst,
            Func<TItem, int> number
        )
            where TCollection : class, IReadOnlyCollection<TItem>
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            if (metadataFirst)
            {
                model.Add(typeof(TCollection), true);
#if !ENABLE_IL2CPP
                model.CompileInPlace();
#endif
            }
            using MemoryStream source = new(wire);
            TCollection restored = (TCollection)
                model.Deserialize(source, null, typeof(TCollection));
            AssertItemMetadata<TItem>(model);
            AssertSingle(restored, number);
            using MemoryStream destination = new();
            model.Serialize(destination, restored);
            CollectionAssert.AreEqual(wire, destination.ToArray());
            VerifyIndependentItemModel(model, restored, wire, number);
        }

        private static void VerifyNullParent<TCollection, TItem>(
            byte[] wire,
            Func<TItem, int> number
        )
            where TCollection : class, IReadOnlyCollection<TItem>
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            model.Add(typeof(Parent<TCollection>), true);
#if !ENABLE_IL2CPP
            model.CompileInPlace();
#endif
            using MemoryStream empty = new();
            model.Serialize(empty, new Parent<TCollection>());
            Assert.AreEqual(0, empty.Length);
            using MemoryStream source = new(wire);
            Parent<TCollection> restored =
                (Parent<TCollection>)model.Deserialize(source, null, typeof(Parent<TCollection>));
            Assert.IsTrue(restored != null);
            AssertItemMetadata<TItem>(model);
            AssertSingle(restored.Values, number);
            using MemoryStream destination = new();
            model.Serialize(destination, restored);
            CollectionAssert.AreEqual(wire, destination.ToArray());
            VerifyIndependentItemModel(model, restored, wire, number);
        }

        private static void VerifyIndependentItemModel<TCollection, TItem>(
            RuntimeTypeModel original,
            TCollection restored,
            byte[] wire,
            Func<TItem, int> number
        )
        {
            RuntimeTypeModel independent = RuntimeTypeModel.Create();
            independent.AutoCompile = false;
            independent.Add(typeof(TItem), false).Add(2, nameof(ColdHashItem.Number));
            using MemoryStream source = new(new byte[] { 16, 9 });
            TItem item = (TItem)independent.Deserialize(source, null, typeof(TItem));
            Assert.AreEqual(9, number(item));
            using MemoryStream retained = new();
            original.Serialize(retained, restored);
            CollectionAssert.AreEqual(wire, retained.ToArray());
        }

        private static void AssertItemMetadata<TItem>(RuntimeTypeModel model)
        {
            ValueMember[] fields = model[typeof(TItem)].GetFields();
            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual(1, fields[0].FieldNumber);
            Assert.AreEqual(typeof(int), fields[0].MemberType);
        }

        private static void AssertSingle<TItem>(
            IReadOnlyCollection<TItem> restored,
            Func<TItem, int> number
        )
        {
            Assert.IsTrue(restored != null);
            Assert.AreEqual(1, restored.Count);
            using IEnumerator<TItem> items = restored.GetEnumerator();
            Assert.IsTrue(items.MoveNext());
            Assert.AreEqual(7, number(items.Current));
            Assert.IsFalse(items.MoveNext());
        }

        [Test]
        public void FirstDeserializationInitializesHashProvider()
        {
            VerifyColdRoot<SerializableHashSet<ColdHashItem>, ColdHashItem>(
                new byte[] { 10, 2, 8, 7 },
                false,
                item => item.Number
            );
        }

        [Test]
        public void NullParentMetadataInitializesHashProvider()
        {
            VerifyNullParent<SerializableHashSet<ParentHashItem>, ParentHashItem>(
                new byte[] { 58, 2, 8, 7 },
                item => item.Number
            );
        }

        [Test]
        public void FirstDeserializationInitializesSortedProvider()
        {
            VerifyColdRoot<SerializableSortedSet<ColdSortedItem>, ColdSortedItem>(
                new byte[] { 10, 2, 8, 7 },
                false,
                item => item.Number
            );
        }

        [Test]
        public void NullParentMetadataInitializesSortedProvider()
        {
            VerifyNullParent<SerializableSortedSet<ParentSortedItem>, ParentSortedItem>(
                new byte[] { 58, 2, 8, 7 },
                item => item.Number
            );
        }

        [Test]
        public void FirstDeserializationInitializesDequeProvider()
        {
            VerifyColdRoot<Deque<ColdDequeItem>, ColdDequeItem>(
                new byte[] { 10, 2, 8, 7, 24, 1, 32, 1, 40, 16 },
                false,
                item => item.Number
            );
        }

        [Test]
        public void NullParentMetadataInitializesDequeProvider()
        {
            VerifyNullParent<Deque<ParentDequeItem>, ParentDequeItem>(
                new byte[] { 58, 10, 10, 2, 8, 7, 24, 1, 32, 1, 40, 16 },
                item => item.Number
            );
        }

        [Test]
        public void ExplicitMetadataInitializesDequeProvider()
        {
            VerifyColdRoot<Deque<ExplicitDequeItem>, ExplicitDequeItem>(
                new byte[] { 10, 2, 8, 7, 24, 1, 32, 1, 40, 16 },
                true,
                item => item.Number
            );
        }

        [Test]
        public void FirstDeserializationInitializesCyclicProvider()
        {
            VerifyColdRoot<CyclicBuffer<ColdCyclicItem>, ColdCyclicItem>(
                new byte[] { 8, 2, 16, 1, 26, 2, 8, 7, 32, 1 },
                false,
                item => item.Number
            );
        }

        [Test]
        public void NullParentMetadataInitializesCyclicProvider()
        {
            VerifyNullParent<CyclicBuffer<ParentCyclicItem>, ParentCyclicItem>(
                new byte[] { 58, 10, 8, 2, 16, 1, 26, 2, 8, 7, 32, 1 },
                item => item.Number
            );
        }

        [Test]
        public void ExplicitMetadataInitializesCyclicProvider()
        {
            VerifyColdRoot<CyclicBuffer<ExplicitCyclicItem>, ExplicitCyclicItem>(
                new byte[] { 8, 2, 16, 1, 26, 2, 8, 7, 32, 1 },
                true,
                item => item.Number
            );
        }

        [ProtoContract]
        public sealed class Parent<TCollection>
        {
            [ProtoMember(7)]
            public TCollection Values;
        }

        [ProtoContract]
        public struct ColdHashItem : IComparable<ColdHashItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ColdHashItem other)
            {
                return Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public sealed class ParentHashItem : IComparable<ParentHashItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ParentHashItem other)
            {
                return other == null ? 1 : Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public struct ColdSortedItem : IComparable<ColdSortedItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ColdSortedItem other)
            {
                return Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public sealed class ParentSortedItem : IComparable<ParentSortedItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ParentSortedItem other)
            {
                return other == null ? 1 : Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public struct ColdDequeItem : IComparable<ColdDequeItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ColdDequeItem other)
            {
                return Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public sealed class ParentDequeItem : IComparable<ParentDequeItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ParentDequeItem other)
            {
                return other == null ? 1 : Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public sealed class ExplicitDequeItem : IComparable<ExplicitDequeItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ExplicitDequeItem other)
            {
                return other == null ? 1 : Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public struct ColdCyclicItem : IComparable<ColdCyclicItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ColdCyclicItem other)
            {
                return Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public sealed class ParentCyclicItem : IComparable<ParentCyclicItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ParentCyclicItem other)
            {
                return other == null ? 1 : Number.CompareTo(other.Number);
            }
        }

        [ProtoContract]
        public sealed class ExplicitCyclicItem : IComparable<ExplicitCyclicItem>
        {
            [ProtoMember(1)]
            public int Number;

            public int CompareTo(ExplicitCyclicItem other)
            {
                return other == null ? 1 : Number.CompareTo(other.Number);
            }
        }
    }
#endif
}
