// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [Category("Serialization")]
    public sealed class NullableDictionaryWrapperTests
    {
        private static IEnumerable<TestCaseData> MalformedCases()
        {
            foreach (bool sorted in new[] { false, true })
            {
                yield return new TestCaseData(sorted, Payload(1, null, new byte[] { 1 })).SetName(
                    $"NullableWrapper.RejectsMissingDenseValue.Sorted{sorted}"
                );
                yield return new TestCaseData(sorted, Payload(1, 0, new byte[] { 0 })).SetName(
                    $"NullableWrapper.RejectsExtraDenseValue.Sorted{sorted}"
                );
                yield return new TestCaseData(sorted, Payload(9, null, new byte[] { 0 })).SetName(
                    $"NullableWrapper.RejectsShortBitmap.Sorted{sorted}"
                );
                yield return new TestCaseData(
                    sorted,
                    Payload(1, null, new byte[] { 0, 0 })
                ).SetName($"NullableWrapper.RejectsLongBitmap.Sorted{sorted}");
                yield return new TestCaseData(sorted, Payload(1, null, new byte[] { 128 })).SetName(
                    $"NullableWrapper.RejectsUnusedHighBit.Sorted{sorted}"
                );
            }
        }

        private static byte[] Payload(int keys, int? denseValue, byte[] presence)
        {
            List<byte> payload = new List<byte>();
            for (int key = 1; key <= keys; ++key)
            {
                payload.Add(8);
                payload.Add((byte)key);
            }
            if (denseValue.HasValue)
            {
                payload.Add(16);
                payload.Add((byte)denseValue.Value);
            }
            payload.Add(26);
            payload.Add((byte)presence.Length);
            payload.AddRange(presence);
            return payload.ToArray();
        }

        private static void AssertSerializationOverloads<T>(T value, byte[] expected)
        {
            CollectionAssert.AreEqual(expected, Serializer.ProtoSerialize(value));
            byte[] buffer = new byte[64];
            int length = Serializer.ProtoSerialize(value, ref buffer);
            Assert.AreEqual(expected.Length, length);
            CollectionAssert.AreEqual(expected, new ArraySegment<byte>(buffer, 0, length));
        }

        private static IEnumerable<TestCaseData> DuplicatePresenceCases()
        {
            foreach (bool sorted in new[] { false, true })
            {
                byte[] mixedInvalidFirst = { 8, 2, 8, 3, 8, 4, 16, 0, 16, 7, 26, 1, 128, 26, 1, 6 };
                byte[] mixedInvalidLast = { 8, 2, 8, 3, 8, 4, 16, 0, 16, 7, 26, 1, 6, 26, 1, 128 };
                yield return new TestCaseData(
                    sorted,
                    mixedInvalidFirst,
                    2,
                    new int?[] { null, 0, 7 }
                ).SetName($"NullableWrapper.LastValidBitmapOverridesInvalidFirst.Sorted{sorted}");
                yield return new TestCaseData(sorted, mixedInvalidLast, 2, null).SetName(
                    $"NullableWrapper.LastInvalidBitmapRejectsValidFirst.Sorted{sorted}"
                );
                yield return new TestCaseData(
                    sorted,
                    DuplicatePresencePayload(new byte[] { 0 }),
                    1,
                    null
                ).SetName($"NullableWrapper.DuplicateShortBitmapsDoNotConcatenate.Sorted{sorted}");
                yield return new TestCaseData(
                    sorted,
                    DuplicatePresencePayload(new byte[] { 0, 0 }),
                    1,
                    new int?[9]
                ).SetName($"NullableWrapper.LastCompleteBitmapOverridesShortFirst.Sorted{sorted}");
            }
        }

        private static byte[] DuplicatePresencePayload(byte[] lastPresence)
        {
            List<byte> payload = new List<byte>(Payload(9, null, new byte[] { 0 }));
            payload.Add(26);
            payload.Add((byte)lastPresence.Length);
            payload.AddRange(lastPresence);
            return payload.ToArray();
        }

        private static void AssertDuplicatePresenceOverloads<T>(
            byte[] payload,
            int firstKey,
            int?[] expected
        )
            where T : class, IDictionary<int, int?>
        {
            if (expected == null)
            {
                SerializationCorruptDataException typedFailure =
                    Assert.Throws<SerializationCorruptDataException>(() =>
                        Serializer.ProtoDeserialize<T>(payload)
                    );
                SerializationCorruptDataException suppliedTypeFailure =
                    Assert.Throws<SerializationCorruptDataException>(() =>
                        Serializer.ProtoDeserialize<T>(payload, typeof(T))
                    );
                foreach (
                    SerializationCorruptDataException failure in new[]
                    {
                        typedFailure,
                        suppliedTypeFailure,
                    }
                )
                {
                    Assert.AreEqual(SerializationFormat.Protobuf, failure.Format);
                    Assert.AreEqual(SerializationOperation.Deserialize, failure.Operation);
                    Assert.AreEqual(SerializationStage.PostProcess, failure.Stage);
                }
                Assert.IsFalse(Serializer.TryProtoDeserialize(payload, out T typedRefused));
                Assert.IsTrue(typedRefused == null);
                Assert.IsFalse(
                    Serializer.TryProtoDeserialize(payload, typeof(T), out T suppliedTypeRefused)
                );
                Assert.IsTrue(suppliedTypeRefused == null);
                return;
            }
            T typed = Serializer.ProtoDeserialize<T>(payload);
            T suppliedType = Serializer.ProtoDeserialize<T>(payload, typeof(T));
            Assert.IsTrue(Serializer.TryProtoDeserialize(payload, out T typedTry));
            Assert.IsTrue(
                Serializer.TryProtoDeserialize(payload, typeof(T), out T suppliedTypeTry)
            );
            foreach (T restored in new[] { typed, suppliedType, typedTry, suppliedTypeTry })
            {
                Assert.IsTrue(restored != null);
                Assert.AreEqual(expected.Length, restored.Count);
                for (int index = 0; index < expected.Length; ++index)
                {
                    Assert.IsTrue(restored.TryGetValue(firstKey + index, out int? slot));
                    Assert.AreEqual(expected[index], slot);
                }
            }
        }

        [Test]
        public void NullFreeValuesRetainOriginalArrayAndOmitPresence()
        {
            int?[] original = { 0, 7 };
            int?[] values = original;
            byte[] presence = null;
            NullableArrayPresence<int?>.Prepare(2, ref values, ref presence);
            Assert.AreSame(original, values);
            Assert.IsTrue(presence == null);
            Assert.IsTrue(NullableArrayPresence<int?>.TryRestore(2, ref values, presence));
            Assert.AreSame(original, values);
        }

        [Test]
        public void PresencePreservesNullDefaultAndOriginalSnapshot()
        {
            int?[] original = { null, 0, 7 };
            int?[] values = original;
            byte[] presence = null;
            NullableArrayPresence<int?>.Prepare(3, ref values, ref presence);
            CollectionAssert.AreEqual(new int?[] { null, 0, 7 }, original);
            CollectionAssert.AreEqual(new int?[] { 0, 7 }, values);
            CollectionAssert.AreEqual(new byte[] { 6 }, presence);
            Assert.IsTrue(NullableArrayPresence<int?>.TryRestore(3, ref values, presence));
            CollectionAssert.AreEqual(original, values);
        }

        [Test]
        public void AllNullValuesRestoreFromOmittedDenseArray()
        {
            int?[] values = null;
            Assert.IsTrue(NullableArrayPresence<int?>.TryRestore(3, ref values, new byte[] { 0 }));
            CollectionAssert.AreEqual(new int?[] { null, null, null }, values);
        }

        [Test]
        public void PresenceCrossesByteBoundaryWithoutCollapsingDefault()
        {
            int?[] values = { 0 };
            Assert.IsTrue(
                NullableArrayPresence<int?>.TryRestore(9, ref values, new byte[] { 0, 1 })
            );
            Assert.AreEqual(9, values.Length);
            for (int i = 0; i < 8; ++i)
            {
                Assert.IsTrue(values[i] == null);
            }
            Assert.IsTrue(values[8].HasValue);
            Assert.AreEqual(0, values[8].Value);
        }

        [Test]
        public void InvalidDenseNullDoesNotReplaceDeliveredArray()
        {
            int?[] original = { null };
            int?[] values = original;
            Assert.IsFalse(NullableArrayPresence<int?>.TryRestore(1, ref values, new byte[] { 1 }));
            Assert.AreSame(original, values);
        }

        [Test]
        public void ExtremeCountWithShortBitmapIsRejectedBeforeExpansion()
        {
            int?[] values = null;
            Assert.IsFalse(
                NullableArrayPresence<int?>.TryRestore(int.MaxValue, ref values, new byte[] { 0 })
            );
            Assert.IsTrue(values == null);
        }

        [Test]
        public void AbsentPresenceRetainsHistoricalArrayAlignmentPolicy()
        {
            int?[] original = { 0, 7 };
            int?[] values = original;
            Assert.IsTrue(NullableArrayPresence<int?>.TryRestore(4, ref values, null));
            Assert.AreSame(original, values);
        }

        [Test]
        public void EmptyPresenceAtZeroDeliveredKeysDoesNotAllocate()
        {
            int?[] values = null;
            Assert.IsTrue(
                NullableArrayPresence<int?>.TryRestore(0, ref values, Array.Empty<byte>())
            );
            Assert.IsTrue(values == null);
        }

        [Test]
        public void MiddleNullsAcrossBitmapBoundaryRestoreEverySlot()
        {
            int?[] original = { 0, null, 7, null, null, 0, null, 7, null, 0 };
            int?[] values = original;
            byte[] presence = null;
            NullableArrayPresence<int?>.Prepare(original.Length, ref values, ref presence);
            Assert.IsTrue(
                NullableArrayPresence<int?>.TryRestore(original.Length, ref values, presence)
            );
            CollectionAssert.AreEqual(original, values);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullableSlotsHaveExplicitRootWrapperBytes(bool sorted)
        {
            byte[] mixed = { 8, 2, 8, 3, 8, 4, 16, 0, 16, 7, 26, 1, 6 };
            byte[] allNull = { 8, 2, 8, 3, 26, 1, 0 };
            if (sorted)
            {
                SerializableSortedDictionary<int, int?> values = new()
                {
                    { 2, null },
                    { 3, 0 },
                    { 4, 7 },
                };
                AssertSerializationOverloads(values, mixed);
                SerializableSortedDictionary<int, int?> emptyValues = new()
                {
                    { 2, null },
                    { 3, null },
                };
                AssertSerializationOverloads(emptyValues, allNull);
            }
            else
            {
                SerializableDictionary<int, int?> values = new()
                {
                    { 2, null },
                    { 3, 0 },
                    { 4, 7 },
                };
                AssertSerializationOverloads(values, mixed);
                SerializableDictionary<int, int?> emptyValues = new() { { 2, null }, { 3, null } };
                AssertSerializationOverloads(emptyValues, allNull);
            }
        }

        [TestCaseSource(nameof(DuplicatePresenceCases))]
        public void DuplicateBitmapOccurrencesUseOnlyTheLastValue(
            bool sorted,
            byte[] payload,
            int firstKey,
            int?[] expected
        )
        {
            if (sorted)
            {
                AssertDuplicatePresenceOverloads<SerializableSortedDictionary<int, int?>>(
                    payload,
                    firstKey,
                    expected
                );
            }
            else
            {
                AssertDuplicatePresenceOverloads<SerializableDictionary<int, int?>>(
                    payload,
                    firstKey,
                    expected
                );
            }
        }

        [TestCaseSource(nameof(MalformedCases))]
        public void MalformedPresenceFailsThroughFacadeContract(bool sorted, byte[] payload)
        {
            SerializationCorruptDataException failure;
            if (sorted)
            {
                failure = Assert.Throws<SerializationCorruptDataException>(() =>
                    Serializer.ProtoDeserialize<SerializableSortedDictionary<int, int?>>(payload)
                );
                Assert.IsFalse(
                    Serializer.TryProtoDeserialize(
                        payload,
                        out SerializableSortedDictionary<int, int?> refused
                    )
                );
                Assert.IsTrue(refused == null);
            }
            else
            {
                failure = Assert.Throws<SerializationCorruptDataException>(() =>
                    Serializer.ProtoDeserialize<SerializableDictionary<int, int?>>(payload)
                );
                Assert.IsFalse(
                    Serializer.TryProtoDeserialize(
                        payload,
                        out SerializableDictionary<int, int?> refused
                    )
                );
                Assert.IsTrue(refused == null);
            }
            Assert.AreEqual(SerializationFormat.Protobuf, failure.Format);
            Assert.AreEqual(SerializationOperation.Deserialize, failure.Operation);
            Assert.AreEqual(SerializationStage.PostProcess, failure.Stage);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonnullableValuesRejectPresenceThroughFacadeContract(bool sorted)
        {
            byte[] payload = Payload(1, 7, new byte[] { 1 });
            SerializationCorruptDataException failure;
            if (sorted)
            {
                failure = Assert.Throws<SerializationCorruptDataException>(() =>
                    Serializer.ProtoDeserialize<SerializableSortedDictionary<int, int>>(payload)
                );
            }
            else
            {
                failure = Assert.Throws<SerializationCorruptDataException>(() =>
                    Serializer.ProtoDeserialize<SerializableDictionary<int, int>>(payload)
                );
            }
            Assert.AreEqual(SerializationFormat.Protobuf, failure.Format);
            Assert.AreEqual(SerializationOperation.Deserialize, failure.Operation);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NewAllNullPayloadRestoresThroughFacade(bool sorted)
        {
            byte[] payload = Payload(3, null, new byte[] { 0 });
            if (sorted)
            {
                SerializableSortedDictionary<int, int?> restored = Serializer.ProtoDeserialize<
                    SerializableSortedDictionary<int, int?>
                >(payload);
                Assert.AreEqual(3, restored.Count);
                for (int key = 1; key <= 3; ++key)
                {
                    Assert.IsTrue(restored.TryGetValue(key, out int? slot));
                    Assert.IsTrue(slot == null);
                }
            }
            else
            {
                SerializableDictionary<int, int?> restored = Serializer.ProtoDeserialize<
                    SerializableDictionary<int, int?>
                >(payload);
                Assert.AreEqual(3, restored.Count);
                for (int key = 1; key <= 3; ++key)
                {
                    Assert.IsTrue(restored.TryGetValue(key, out int? slot));
                    Assert.IsTrue(slot == null);
                }
            }
        }
    }
}
