// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the seven collections <c>Serializer</c> marshals through a wrapper against the bytes
    /// that shipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These types are never handed to protobuf-net as themselves: each is copied into a wrapper
    /// POCO -- items plus capacity, or parallel key/value arrays -- because protobuf-net's repeated
    /// provider ignores <c>IgnoreListHandling</c>. Every save file a consumer has written holds the
    /// wrapper's bytes, so a root marshal that wrote anything else would be a silent data change.
    /// </para>
    /// <para>
    /// The claim is <b>interoperability, not byte-identity</b>, which is this package's stated
    /// encoding policy: a packable repeated field is written packed here and unpacked by
    /// protobuf-net, roughly halving it, and each side decodes the other's form exactly. So both
    /// directions are asserted rather than a hash of the output.
    /// </para>
    /// <para>
    /// The shipped path is called directly rather than through <c>Serializer.ProtoSerialize</c>,
    /// which routes to the facade once <c>WALLSTOP_PROTO</c> is defined -- comparing the marshal
    /// against itself would pass whatever it wrote. Only those two tests need the oracle, so only
    /// they are skipped under IL2CPP; the round-trips run on every leg.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoCollectionMarshalTests
    {
        [Test]
        public void EveryMarshalledCollectionIsServedAtTheRoot()
        {
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<SerializableHashSet<int>>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<SerializableSortedSet<int>>());
            Assert.IsTrue(
                WProtoRootMarshalProvider.IsRegistered<SerializableDictionary<string, int>>()
            );
            Assert.IsTrue(
                WProtoRootMarshalProvider.IsRegistered<SerializableSortedDictionary<string, int>>()
            );
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<Deque<int>>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<CyclicBuffer<int>>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<SparseSet>());
        }

        /// <summary>
        /// A marshal is reachable only at the root, never from a member position.
        /// </summary>
        /// <remarks>
        /// <see cref="WProtoGeneric{T}"/> resolves a member whose type a closure decides through
        /// <see cref="WProtoFormatterProvider"/>. A marshal registered there would be found from a
        /// member and would write the wrapper where protobuf-net writes a repeated field -- bytes no
        /// reader on either side ever produced.
        /// </remarks>
        [Test]
        public void AMarshalIsNotVisibleToTheContractFormatterProvider()
        {
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<SerializableHashSet<int>>());
            Assert.IsFalse(
                WProtoFormatterProvider.IsRegistered<SerializableDictionary<string, int>>()
            );
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<Deque<int>>());
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<SparseSet>());
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void TheShippedWrapperPathReadsWhatTheMarshalWrites()
        {
            foreach (Sample sample in Samples())
            {
                sample.AssertShippedReadsOurs();
            }
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void TheMarshalReadsWhatTheShippedWrapperPathWrote()
        {
            foreach (Sample sample in Samples())
            {
                sample.AssertOursReadsShipped();
            }
        }

        [Test]
        public void EveryMarshalledCollectionRoundTripsThroughTheSeam()
        {
            foreach (Sample sample in Samples())
            {
                sample.AssertRoundTrips();
            }
        }

        /// <summary>
        /// An empty marshalled collection encodes to nothing and still reads back as itself.
        /// </summary>
        /// <remarks>
        /// A wrapper of nothing but repeated fields writes zero bytes when it is empty, which is why
        /// <c>Serializer</c>'s empty-payload guard runs after the interception rather than before it.
        /// Reading that back as <c>null</c> is the failure to watch for.
        /// </remarks>
        [Test]
        public void AnEmptyMarshalledCollectionRoundTrips()
        {
            AssertRoundTrip(new SerializableHashSet<int>(), set => Assert.AreEqual(0, set.Count));
            AssertRoundTrip(new SerializableSortedSet<int>(), set => Assert.AreEqual(0, set.Count));
            AssertRoundTrip(
                new SerializableDictionary<string, int>(),
                map => Assert.AreEqual(0, map.Count)
            );
            AssertRoundTrip(
                new SerializableSortedDictionary<string, int>(),
                map => Assert.AreEqual(0, map.Count)
            );
            AssertRoundTrip(new Deque<int>(4), deque => Assert.AreEqual(0, deque.Count));
            AssertRoundTrip(new CyclicBuffer<int>(4), buffer => Assert.AreEqual(0, buffer.Count));
            AssertRoundTrip(new SparseSet(8), set => Assert.AreEqual(0, set.Count));
        }

        /// <summary>
        /// A deque keeps the capacity it was saved with, which only the wrapper carries.
        /// </summary>
        [Test]
        public void AMarshalledDequeKeepsItsCapacity()
        {
            Deque<int> deque = new Deque<int>(32);
            deque.PushBack(1);

            Assert.IsTrue(WProtoFacade.TrySerialize(deque, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out Deque<int> restored));
            Assert.AreEqual(deque.Capacity, restored.Capacity);
            Assert.AreEqual(1, restored.Count);
        }

        private static void AssertRoundTrip<T>(T value, Action<T> verify)
        {
            Assert.IsTrue(
                WProtoFacade.TrySerialize(value, out byte[] bytes),
                typeof(T).Name + " is not served at the root."
            );
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(bytes, out T restored),
                typeof(T).Name + " could not be read back."
            );
            Assert.IsTrue(restored != null);
            verify(restored);
        }

        private static IEnumerable<Sample> Samples()
        {
            SerializableHashSet<int> hashSet = new SerializableHashSet<int> { 1, 300, -7 };
            yield return Sample.Wrapped(
                hashSet,
                restored => CollectionAssert.AreEquivalent(hashSet.ToList(), restored.ToList())
            );

            SerializableSortedSet<int> sortedSet = new SerializableSortedSet<int> { 5, 1, 9 };
            yield return Sample.Wrapped(
                sortedSet,
                restored => CollectionAssert.AreEqual(sortedSet.ToList(), restored.ToList())
            );

            SerializableDictionary<string, int> dictionary = new SerializableDictionary<string, int>
            {
                { "a", 1 },
                { "b", 300 },
            };
            yield return Sample.Wrapped(
                dictionary,
                restored =>
                {
                    Assert.AreEqual(dictionary.Count, restored.Count);
                    foreach (KeyValuePair<string, int> entry in dictionary)
                    {
                        Assert.AreEqual(entry.Value, restored[entry.Key]);
                    }
                }
            );

            SerializableSortedDictionary<string, int> sortedDictionary =
                new SerializableSortedDictionary<string, int> { { "b", 2 }, { "a", 1 } };
            yield return Sample.Wrapped(
                sortedDictionary,
                restored =>
                {
                    Assert.AreEqual(sortedDictionary.Count, restored.Count);
                    CollectionAssert.AreEqual(
                        sortedDictionary.Keys.ToList(),
                        restored.Keys.ToList()
                    );
                }
            );

            Deque<int> deque = new Deque<int>(16);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushFront(0);
            yield return Sample.Special(
                deque,
                restored =>
                {
                    CollectionAssert.AreEqual(deque.ToArray(), restored.ToArray());
                    Assert.AreEqual(deque.Capacity, restored.Capacity);
                }
            );

            CyclicBuffer<int> buffer = new CyclicBuffer<int>(3);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);
            yield return Sample.Special(
                buffer,
                restored =>
                {
                    Assert.AreEqual(buffer.Count, restored.Count);
                    Assert.AreEqual(buffer.Capacity, restored.Capacity);
                    for (int index = 0; index < buffer.Count; index++)
                    {
                        Assert.AreEqual(buffer[index], restored[index]);
                    }
                }
            );

            SparseSet sparse = new SparseSet(64);
            sparse.TryAdd(3);
            sparse.TryAdd(40);
            yield return Sample.Special(
                sparse,
                restored =>
                {
                    CollectionAssert.AreEquivalent(sparse.ToArray(), restored.ToArray());
                    Assert.AreEqual(sparse.Capacity, restored.Capacity);
                }
            );
        }

        private abstract class Sample
        {
            internal static Sample Wrapped<T>(T value, Action<T> verify)
            {
                return new Sample<T>(
                    value,
                    verify,
                    Serializer.SerializeCollectionWithWrapper,
                    Serializer.DeserializeCollectionFromWrapper<T>
                );
            }

            internal static Sample Special<T>(T value, Action<T> verify)
            {
                return new Sample<T>(
                    value,
                    verify,
                    Serializer.SerializeSpecialCollection,
                    Serializer.DeserializeSpecialCollection<T>
                );
            }

            internal abstract void AssertShippedReadsOurs();

            internal abstract void AssertOursReadsShipped();

            internal abstract void AssertRoundTrips();
        }

        private sealed class Sample<T> : Sample
        {
            private readonly T _value;
            private readonly Action<T> _verify;
            private readonly Func<T, byte[]> _shippedWrite;
            private readonly Func<byte[], T> _shippedRead;

            internal Sample(
                T value,
                Action<T> verify,
                Func<T, byte[]> shippedWrite,
                Func<byte[], T> shippedRead
            )
            {
                _value = value;
                _verify = verify;
                _shippedWrite = shippedWrite;
                _shippedRead = shippedRead;
            }

            internal override void AssertShippedReadsOurs()
            {
                Assert.IsTrue(
                    WProtoFacade.TrySerialize(_value, out byte[] ours),
                    typeof(T).Name + " is not served at the root."
                );
                _verify(_shippedRead(ours));
            }

            internal override void AssertOursReadsShipped()
            {
                byte[] theirs = _shippedWrite(_value);
                Assert.IsTrue(
                    WProtoFacade.TryDeserialize(theirs, out T restored),
                    typeof(T).Name + " refused the payload the shipped path wrote."
                );
                _verify(restored);
            }

            internal override void AssertRoundTrips()
            {
                AssertRoundTrip(_value, _verify);
            }
        }
    }
}
