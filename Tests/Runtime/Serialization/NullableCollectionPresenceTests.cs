// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [Category("Serialization")]
    public sealed class NullableCollectionPresenceTests
    {
        private static IReadOnlyList<T> Collection<T>(T[] values, bool readOnly)
        {
            List<T> list = new(values);
            if (readOnly)
            {
                return list.AsReadOnly();
            }
            return list;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedCollectionsPreserveNullDefaultAndNondefault(bool readOnly)
        {
            int?[] expected = { null, 0, 7 };
            IReadOnlyList<int?> source = Collection(expected, readOnly);
            NullableCollectionPresence<int?>.Prepare(
                source.Count,
                source,
                out int?[] compact,
                out byte[] presence
            );
            CollectionAssert.AreEqual(expected, source);
            CollectionAssert.AreEqual(new int?[] { 0, 7 }, compact);
            CollectionAssert.AreEqual(new byte[] { 6 }, presence);
            IReadOnlyList<int?> dense = Collection(compact, readOnly);
            Assert.IsTrue(
                NullableCollectionPresence<int?>.TryRestore(3, dense, presence, out int?[] restored)
            );
            CollectionAssert.AreEqual(expected, restored);
            CollectionAssert.AreEqual(compact, dense);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedNullFreeCollectionDoesNotCreateReplacement(bool readOnly)
        {
            IReadOnlyList<int?> source = Collection(new int?[] { 0, 7 }, readOnly);
            NullableCollectionPresence<int?>.Prepare(
                2,
                source,
                out int?[] compact,
                out byte[] presence
            );
            Assert.IsTrue(compact == null);
            Assert.IsTrue(presence == null);
            Assert.IsTrue(
                NullableCollectionPresence<int?>.TryRestore(
                    2,
                    source,
                    new byte[] { 3 },
                    out int?[] restored
                )
            );
            Assert.IsTrue(restored == null);
            CollectionAssert.AreEqual(new int?[] { 0, 7 }, source);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedCollectionRestoresAcrossBitmapBoundary(bool readOnly)
        {
            int?[] expected = { 0, null, 7, null, null, 0, null, 7, null, 0 };
            IReadOnlyList<int?> source = Collection(expected, readOnly);
            NullableCollectionPresence<int?>.Prepare(
                source.Count,
                source,
                out int?[] compact,
                out byte[] presence
            );
            CollectionAssert.AreEqual(new byte[] { 165, 2 }, presence);
            Assert.IsTrue(
                NullableCollectionPresence<int?>.TryRestore(
                    source.Count,
                    Collection(compact, readOnly),
                    presence,
                    out int?[] restored
                )
            );
            CollectionAssert.AreEqual(expected, restored);
            CollectionAssert.AreEqual(expected, source);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedAbsentPresenceRetainsHistoricalAlignment(bool readOnly)
        {
            IReadOnlyList<int?> source = Collection(new int?[] { 0, 7 }, readOnly);
            Assert.IsTrue(
                NullableCollectionPresence<int?>.TryRestore(4, source, null, out int?[] restored)
            );
            Assert.IsTrue(restored == null);
            CollectionAssert.AreEqual(new int?[] { 0, 7 }, source);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedAllNullCollectionRetainsSlotCount(bool readOnly)
        {
            IReadOnlyList<int?> source = Collection(new int?[] { null, null, null }, readOnly);
            NullableCollectionPresence<int?>.Prepare(
                3,
                source,
                out int?[] compact,
                out byte[] presence
            );
            Assert.AreEqual(0, compact.Length);
            CollectionAssert.AreEqual(new byte[] { 0 }, presence);
            Assert.IsTrue(
                NullableCollectionPresence<int?>.TryRestore(
                    3,
                    Collection(compact, readOnly),
                    presence,
                    out int?[] restored
                )
            );
            CollectionAssert.AreEqual(new int?[] { null, null, null }, restored);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MismatchedIndexedSlotCountDoesNotMutateSource(bool readOnly)
        {
            IReadOnlyList<int?> source = Collection(new int?[] { null, 0 }, readOnly);
            int?[] originalCompact = { 7 };
            byte[] originalPresence = { 1 };
            int?[] compact = originalCompact;
            byte[] presence = originalPresence;
            Assert.Throws<InvalidOperationException>(() =>
                NullableCollectionPresence<int?>.Prepare(3, source, out compact, out presence)
            );
            Assert.AreSame(originalCompact, compact);
            Assert.AreSame(originalPresence, presence);
            CollectionAssert.AreEqual(new int?[] { null, 0 }, source);
        }

        [TestCase(false, 1, 0, 1)]
        [TestCase(true, 1, 0, 1)]
        [TestCase(false, 1, 1, 1)]
        [TestCase(true, 1, 1, 1)]
        [TestCase(false, 1, 2, 0)]
        [TestCase(true, 1, 2, 0)]
        [TestCase(false, 1, 2, 128)]
        [TestCase(true, 1, 2, 128)]
        [TestCase(false, 9, 2, 1)]
        [TestCase(true, 9, 2, 1)]
        [TestCase(false, -1, 0, 0)]
        [TestCase(true, -1, 0, 0)]
        [TestCase(false, int.MaxValue, 0, 0)]
        [TestCase(true, int.MaxValue, 0, 0)]
        public void MalformedIndexedPresenceProducesNoPartialReplacement(
            bool readOnly,
            int slots,
            int valueKind,
            byte bitmap
        )
        {
            int?[] expected =
                valueKind == 0 ? Array.Empty<int?>() : new int?[] { valueKind == 1 ? null : 0 };
            IReadOnlyList<int?> source = Collection(expected, readOnly);
            Assert.IsFalse(
                NullableCollectionPresence<int?>.TryRestore(
                    slots,
                    source,
                    new[] { bitmap },
                    out int?[] restored
                )
            );
            Assert.IsTrue(restored == null);
            CollectionAssert.AreEqual(expected, source);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyIndexedCollectionDoesNotCreateReplacement(bool readOnly)
        {
            IReadOnlyList<int?> source = Collection(Array.Empty<int?>(), readOnly);
            NullableCollectionPresence<int?>.Prepare(
                0,
                source,
                out int?[] compact,
                out byte[] presence
            );
            Assert.IsTrue(compact == null);
            Assert.IsTrue(presence == null);
            Assert.IsTrue(
                NullableCollectionPresence<int?>.TryRestore(
                    0,
                    source,
                    Array.Empty<byte>(),
                    out int?[] restored
                )
            );
            Assert.IsTrue(restored == null);
            Assert.AreEqual(0, source.Count);
        }

        [Test]
        public void NullIndexedCollectionDoesNotCreatePreparedReplacement()
        {
            NullableCollectionPresence<int?>.Prepare(
                0,
                null,
                out int?[] compact,
                out byte[] presence
            );
            Assert.IsTrue(compact == null);
            Assert.IsTrue(presence == null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OversizedBitmapDoesNotReplaceIndexedCollection(bool readOnly)
        {
            IReadOnlyList<int?> source = Collection(new int?[] { 0 }, readOnly);
            Assert.IsFalse(
                NullableCollectionPresence<int?>.TryRestore(
                    1,
                    source,
                    new byte[] { 1, 0 },
                    out int?[] restored
                )
            );
            Assert.IsTrue(restored == null);
            CollectionAssert.AreEqual(new int?[] { 0 }, source);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonnullableIndexedCollectionDoesNotUsePresence(bool readOnly)
        {
            IReadOnlyList<int> source = Collection(new[] { 0, 7 }, readOnly);
            NullableCollectionPresence<int>.Prepare(
                2,
                source,
                out int[] compact,
                out byte[] presence
            );
            Assert.IsTrue(compact == null);
            Assert.IsTrue(presence == null);
            Assert.IsFalse(
                NullableCollectionPresence<int>.TryRestore(
                    2,
                    source,
                    new byte[] { 3 },
                    out int[] restored
                )
            );
            Assert.IsTrue(restored == null);
            CollectionAssert.AreEqual(new[] { 0, 7 }, source);
        }
    }
}
