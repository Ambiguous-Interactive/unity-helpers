// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.IO;
    using System.Text.Json;
    using NUnit.Framework;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableNullableTests
    {
        [Test]
        public void HasValueMatchesSystemNullable()
        {
            SerializableNullable<int> wrapped = new(5);
            Assert.IsTrue(wrapped.HasValue);
            Assert.AreEqual(5, wrapped.Value);
            Assert.AreEqual(5, wrapped.GetValueOrDefault());

            int? systemNullable = wrapped;
            Assert.IsTrue(systemNullable.HasValue);
            Assert.AreEqual(5, systemNullable.Value);
        }

        [Test]
        public void ValueAccessorThrowsWhenEmpty()
        {
            SerializableNullable<int> wrapped = default;
            Assert.Throws<InvalidOperationException>(() => _ = wrapped.Value);
        }

        [Test]
        public void ClearAndSetValueTogglePresence()
        {
            SerializableNullable<int> wrapped = default;
            Assert.IsFalse(wrapped.HasValue);

            wrapped.SetValue(9);
            Assert.IsTrue(wrapped.HasValue);
            Assert.AreEqual(9, wrapped.Value);

            wrapped.Clear();
            Assert.IsFalse(wrapped.HasValue);
            Assert.AreEqual(0, wrapped.GetValueOrDefault());
        }

        [Test]
        public void EqualityBehavesLikeNullable()
        {
            SerializableNullable<int> empty = default;
            SerializableNullable<int> five = new(5);
            int? systemFive = 5;

            Assert.IsTrue(empty == null);
            Assert.IsTrue(null == empty);
            Assert.IsTrue(five == systemFive);
            Assert.IsTrue(systemFive == five);
            Assert.IsTrue(five == 5);
            Assert.IsTrue(5 == five);
            Assert.IsTrue(five.Equals(systemFive));
            Assert.IsTrue(five.Equals(5));
            Assert.IsFalse(five == null);
            Assert.IsTrue(five != null);

            SerializableNullable<int> otherFive = new(5);
            Assert.IsTrue(five == otherFive);
            Assert.IsFalse(five != otherFive);
        }

        [Test]
        public void TryGetValueFollowsPresence()
        {
            SerializableNullable<int> empty = default;
            bool emptyResult = empty.TryGetValue(out int emptyValue);
            Assert.IsFalse(emptyResult);
            Assert.AreEqual(0, emptyValue);

            SerializableNullable<int> wrapped = new(12);
            bool hasValue = wrapped.TryGetValue(out int actualValue);
            Assert.IsTrue(hasValue);
            Assert.AreEqual(12, actualValue);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonSerializationRoundTrips()
        {
            SerializableNullable<int> wrapped = new(42);
            string jsonValue = JsonSerializer.Serialize(wrapped);
            Assert.AreEqual("42", jsonValue);

            SerializableNullable<int> deserialized = JsonSerializer.Deserialize<
                SerializableNullable<int>
            >("42");
            Assert.IsTrue(deserialized.HasValue);
            Assert.AreEqual(42, deserialized.Value);

            string jsonNull = JsonSerializer.Serialize(default(SerializableNullable<int>));
            Assert.AreEqual("null", jsonNull);

            SerializableNullable<int> deserializedNull = JsonSerializer.Deserialize<
                SerializableNullable<int>
            >("null");
            Assert.IsFalse(deserializedNull.HasValue);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationRoundTrips()
        {
            SerializableNullable<int> wrapped = new(1234);
            using MemoryStream stream = new();
            Serializer.Serialize(stream, wrapped);
            stream.Position = 0;
            SerializableNullable<int> roundTripped = Serializer.Deserialize<
                SerializableNullable<int>
            >(stream);

            Assert.IsTrue(roundTripped.HasValue);
            Assert.AreEqual(1234, roundTripped.Value);

            stream.SetLength(0);
            stream.Position = 0;
            SerializableNullable<int> empty = default;
            Serializer.Serialize(stream, empty);
            stream.Position = 0;
            SerializableNullable<int> roundTrippedEmpty = Serializer.Deserialize<
                SerializableNullable<int>
            >(stream);
            Assert.IsFalse(roundTrippedEmpty.HasValue);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationSnapshotsValueAcrossMutations()
        {
            SerializableNullable<int> wrapped = new(321);

            byte[] firstSnapshot;
            using (MemoryStream snapshotStream = new())
            {
                Serializer.Serialize(snapshotStream, wrapped);
                firstSnapshot = snapshotStream.ToArray();
            }

            wrapped.SetValue(654);

            byte[] secondSnapshot;
            using (MemoryStream snapshotStream = new())
            {
                Serializer.Serialize(snapshotStream, wrapped);
                secondSnapshot = snapshotStream.ToArray();
            }

            wrapped.Clear();

            byte[] thirdSnapshot;
            using (MemoryStream snapshotStream = new())
            {
                Serializer.Serialize(snapshotStream, wrapped);
                thirdSnapshot = snapshotStream.ToArray();
            }

            Assert.AreNotEqual(firstSnapshot, secondSnapshot);
            Assert.AreNotEqual(secondSnapshot, thirdSnapshot);
            Assert.AreNotEqual(firstSnapshot, thirdSnapshot);

            using MemoryStream firstStream = new(firstSnapshot);
            using MemoryStream secondStream = new(secondSnapshot);
            using MemoryStream thirdStream = new(thirdSnapshot);

            SerializableNullable<int> firstRoundTrip = Serializer.Deserialize<
                SerializableNullable<int>
            >(firstStream);
            SerializableNullable<int> secondRoundTrip = Serializer.Deserialize<
                SerializableNullable<int>
            >(secondStream);
            SerializableNullable<int> thirdRoundTrip = Serializer.Deserialize<
                SerializableNullable<int>
            >(thirdStream);

            Assert.IsTrue(firstRoundTrip.HasValue);
            Assert.AreEqual(321, firstRoundTrip.Value);
            Assert.IsTrue(secondRoundTrip.HasValue);
            Assert.AreEqual(654, secondRoundTrip.Value);
            Assert.IsFalse(thirdRoundTrip.HasValue);
        }

        [Test]
        public void ImplicitConversionsAreSymmetric()
        {
            int? systemNullable = 77;
            SerializableNullable<int> wrapped = systemNullable;
            Assert.IsTrue(wrapped.HasValue);
            Assert.AreEqual(77, wrapped.Value);

            int? convertedBack = wrapped;
            Assert.IsTrue(convertedBack.HasValue);
            Assert.AreEqual(77, convertedBack.Value);

            SerializableNullable<int> fromValue = 88;
            Assert.IsTrue(fromValue.HasValue);
            Assert.AreEqual(88, fromValue.Value);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonSerializationCapturesMutations()
        {
            SerializableNullable<int> wrapped = new(12);
            string firstSnapshot = JsonSerializer.Serialize(wrapped);

            wrapped.SetValue(34);
            string secondSnapshot = JsonSerializer.Serialize(wrapped);

            wrapped.Clear();
            string thirdSnapshot = JsonSerializer.Serialize(wrapped);

            Assert.AreEqual("12", firstSnapshot);
            Assert.AreEqual("34", secondSnapshot);
            Assert.AreEqual("null", thirdSnapshot);

            SerializableNullable<int> firstRoundTrip = JsonSerializer.Deserialize<
                SerializableNullable<int>
            >(firstSnapshot);
            SerializableNullable<int> secondRoundTrip = JsonSerializer.Deserialize<
                SerializableNullable<int>
            >(secondSnapshot);
            SerializableNullable<int> thirdRoundTrip = JsonSerializer.Deserialize<
                SerializableNullable<int>
            >(thirdSnapshot);

            Assert.IsTrue(firstRoundTrip.HasValue);
            Assert.AreEqual(12, firstRoundTrip.Value);
            Assert.IsTrue(secondRoundTrip.HasValue);
            Assert.AreEqual(34, secondRoundTrip.Value);
            Assert.IsFalse(thirdRoundTrip.HasValue);
        }

        [Test]
        public void OnBeforeSerializeClearsStaleValueWhenEmpty()
        {
            SerializableNullable<int> wrapped = default;
            wrapped.ForceStateForTesting(hasValue: false, rawValue: 99);

            ISerializationCallbackReceiver receiver = wrapped;
            receiver.OnBeforeSerialize();

            SerializableNullable<int> result = (SerializableNullable<int>)receiver;
            Assert.IsFalse(result.HasValue);
            Assert.AreEqual(0, result.GetValueOrDefault());
        }

        [Test]
        public void OnAfterDeserializeClearsStaleValueWhenEmpty()
        {
            SerializableNullable<int> wrapped = default;
            wrapped.ForceStateForTesting(hasValue: false, rawValue: 123);

            ISerializationCallbackReceiver receiver = wrapped;
            receiver.OnAfterDeserialize();

            SerializableNullable<int> result = (SerializableNullable<int>)receiver;
            Assert.IsFalse(result.HasValue);
            Assert.AreEqual(0, result.GetValueOrDefault());
        }

        [Test]
        public void ValueFieldIncludesWShowIfAttribute()
        {
            bool resolved = SerializableNullable<int>.TryGetValueFieldAttribute(
                out WShowIfAttribute attribute
            );
            Assert.IsTrue(resolved);
            Assert.AreEqual(
                SerializableNullable<int>.SerializedPropertyNames.HasValue,
                attribute.conditionField
            );
            Assert.IsFalse(attribute.inverse);
        }
    }
}
