// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableListTests : CommonTestBase
    {
        [Test]
        public void ListSemanticsMatchTheWrappedList()
        {
            SerializableList<int> list = new() { 1, 2, 3 };

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(2, list[1]);
            Assert.IsTrue(list.Contains(3));
            Assert.AreEqual(2, list.IndexOf(3));
            Assert.IsFalse(list.IsReadOnly);

            list[1] = 20;
            Assert.AreEqual(20, list[1]);

            list.Insert(1, 15);
            CollectionAssert.AreEqual(new[] { 1, 15, 20, 3 }, list.ToArray());

            Assert.IsTrue(list.Remove(15));
            Assert.IsFalse(list.Remove(15));

            list.RemoveAt(0);
            CollectionAssert.AreEqual(new[] { 20, 3 }, list.ToArray());

            list.AddRange(new[] { 7, 8 });
            CollectionAssert.AreEqual(new[] { 20, 3, 7, 8 }, list.ToArray());

            int[] destination = new int[4];
            list.CopyTo(destination, 0);
            CollectionAssert.AreEqual(new[] { 20, 3, 7, 8 }, destination);

            list.Clear();
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void ConstructorsCopyOrReserveWithoutThrowing()
        {
            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                new SerializableList<int>(new[] { 1, 2 }).ToArray()
            );
            Assert.AreEqual(0, new SerializableList<int>((IEnumerable<int>)null).Count);
            Assert.AreEqual(0, new SerializableList<int>(-5).Count);

            List<int> source = new() { 1, 2 };
            SerializableList<int> copied = new(source);
            source.Add(3);
            Assert.AreEqual(2, copied.Count, "The enumerable constructor must copy, not adopt.");
        }

        // Every one of these throws on List<T>. The wrapper is authored data, so a bad index in a
        // consumer's loop must not take a frame down with it.
        [Test]
        public void OutOfRangeAndNullInputsAreInert()
        {
            SerializableList<int> list = new() { 1 };

            list.RemoveAt(-1);
            list.RemoveAt(5);
            Assert.AreEqual(1, list.Count);

            list.Insert(-10, 0);
            list.Insert(100, 2);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, list.ToArray());

            list.AddRange(null);
            list.CopyTo(null, 0);
            list.CopyTo(new int[1], 0);
            list.CopyTo(new int[3], -1);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, list.ToArray());

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => list[-1] = 0);
        }

        [Test]
        public void ConversionsShareStorageWithTheWrappedList()
        {
            List<int> source = new() { 1, 2 };
            SerializableList<int> wrapped = source;
            wrapped.Add(3);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, source);

            List<int> unwrapped = wrapped;
            Assert.AreSame(source, unwrapped);

            SerializableList<int> fromNullList = (List<int>)null;
            Assert.AreEqual(0, fromNullList.Count);
            Assert.IsTrue((List<int>)(SerializableList<int>)null == null);

            wrapped.AsList().Sort((left, right) => right.CompareTo(left));
            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, wrapped.ToArray());
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonRoundTripsAsAPlainArray()
        {
            SerializableList<int> original = new() { 1, 2, 3 };

            string json = Serializer.JsonStringify(original);
            Assert.AreEqual(
                "[1,2,3]",
                json,
                "Wrapping a list for Unity must not change its JSON shape."
            );

            SerializableList<int> deserialized = Serializer.JsonDeserialize<SerializableList<int>>(
                json
            );
            CollectionAssert.AreEqual(original.ToArray(), deserialized.ToArray());
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void JsonRoundTripsInsideADictionaryValue()
        {
            SerializableDictionary<string, SerializableList<int>> original = new()
            {
                {
                    "alpha",
                    new SerializableList<int> { 1, 2 }
                },
                { "beta", new SerializableList<int>() },
            };

            string json = Serializer.JsonStringify(original);
            SerializableDictionary<string, SerializableList<int>> deserialized =
                Serializer.JsonDeserialize<SerializableDictionary<string, SerializableList<int>>>(
                    json
                );

            Assert.AreEqual(original.Count, deserialized.Count);
            foreach (KeyValuePair<string, SerializableList<int>> pair in original)
            {
                Assert.IsTrue(deserialized.TryGetValue(pair.Key, out SerializableList<int> value));
                CollectionAssert.AreEqual(pair.Value.ToArray(), value.ToArray());
            }
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoRoundTripsElements()
        {
            SerializableList<int> original = new() { 4, 5, 6 };

            byte[] bytes = Serializer.ProtoSerialize(original);
            SerializableList<int> deserialized = Serializer.ProtoDeserialize<SerializableList<int>>(
                bytes
            );

            CollectionAssert.AreEqual(original.ToArray(), deserialized.ToArray());
        }
    }
}
