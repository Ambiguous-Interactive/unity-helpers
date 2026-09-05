// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Pins that a generic type's JSON converter exists without being built reflectively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> marked <c>[SkipUnderIL2CPP]</c>, unlike every other JSON fixture
    /// here. Those are skipped because System.Text.Json resolves a converter for a type it has never
    /// seen through <c>MakeGenericType</c>, which IL2CPP cannot compile; this fixture exists to
    /// prove the closures below no longer take that path, so skipping it on the one backend that
    /// can fail would leave the claim untested.
    /// </para>
    /// <para>
    /// The closures are all over <b>value-type</b> arguments on purpose. IL2CPP shares compiled code
    /// between reference-type instantiations, so a converter closed over a class is generated
    /// whether or not anything names it; a value type gets its own copy or none at all, which is why
    /// the failure was first seen on <c>(int, float)</c>.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class JsonAotConverterTests
    {
        private static readonly Type[] ClosuresThisAssemblyWrites =
        {
            typeof(Deque<sbyte>),
            typeof(CyclicBuffer<ushort>),
            typeof(SerializableList<ulong>),
            typeof(SerializableHashSet<short>),
            typeof(SerializableDictionary<byte, decimal>),
            typeof(SerializableNullable<sbyte>),
            typeof(Range<double>),
        };

        [Test]
        public void TheGeneratorRegisteredAConverterForEveryClosureWritten()
        {
            Assert.Greater(
                WJsonConverterRegistry.Count,
                0,
                "No JSON converter was registered at all, so the generated registrar never ran"
            );

            foreach (Type closure in ClosuresThisAssemblyWrites)
            {
                Assert.IsTrue(
                    WJsonConverterRegistry.TryGet(closure, out JsonConverter converter),
                    $"No converter registered for {closure}"
                );
                Assert.IsTrue(converter.CanConvert(closure), $"{converter} cannot serve {closure}");
            }
        }

        [Test]
        public void AGenericContainerRoundTripsThroughTheRegisteredConverter()
        {
            // An unregistered generic closure fails only on first save in an AOT player.
            Deque<sbyte> deque = new(4);
            deque.PushBack(1);
            deque.PushBack(-2);
            Deque<sbyte> readDeque = Serializer.JsonDeserialize<Deque<sbyte>>(
                Serializer.JsonStringify(deque)
            );
            Assert.AreEqual(2, readDeque.Count);
            Assert.AreEqual((sbyte)1, readDeque[0]);
            Assert.AreEqual((sbyte)(-2), readDeque[1]);

            SerializableDictionary<byte, decimal> dictionary = new() { { 3, 4.5m } };
            SerializableDictionary<byte, decimal> readDictionary = Serializer.JsonDeserialize<
                SerializableDictionary<byte, decimal>
            >(Serializer.JsonStringify(dictionary));
            Assert.AreEqual(1, readDictionary.Count);
            Assert.AreEqual(4.5m, readDictionary.ValueFor<byte, decimal>(3));

            SerializableNullable<sbyte> nullable = new(7);
            SerializableNullable<sbyte> readNullable = Serializer.JsonDeserialize<
                SerializableNullable<sbyte>
            >(Serializer.JsonStringify(nullable));
            Assert.IsTrue(readNullable.HasValue);
            Assert.AreEqual((sbyte)7, readNullable.Value);

            Range<double> range = new(1.5, 2.5);
            Range<double> readRange = Serializer.JsonDeserialize<Range<double>>(
                Serializer.JsonStringify(range)
            );
            Assert.AreEqual(1.5, readRange.min);
            Assert.AreEqual(2.5, readRange.max);

            /*
                Container registration alone is insufficient if its converter reflectively requests another
                collection converter.
            */
            SerializableList<ulong> list = new() { 1UL, 2UL };
            SerializableList<ulong> readList = Serializer.JsonDeserialize<SerializableList<ulong>>(
                Serializer.JsonStringify(list)
            );
            CollectionAssert.AreEqual(new ulong[] { 1UL, 2UL }, readList);

            SerializableHashSet<short> set = new() { 3, 4 };
            SerializableHashSet<short> readSet = Serializer.JsonDeserialize<
                SerializableHashSet<short>
            >(Serializer.JsonStringify(set));
            CollectionAssert.AreEquivalent(new short[] { 3, 4 }, readSet);

            CyclicBuffer<ushort> buffer = new(4) { 5, 6 };
            CyclicBuffer<ushort> readBuffer = Serializer.JsonDeserialize<CyclicBuffer<ushort>>(
                Serializer.JsonStringify(buffer)
            );
            CollectionAssert.AreEqual(new ushort[] { 5, 6 }, readBuffer);
        }

        [Test]
        public void AClosureNothingWritesIsAbsentRatherThanGuessedAt()
        {
            /*
                Construct this unwritten closure reflectively so source discovery cannot register the negative
                control.
            */
            Assert.IsFalse(
                WJsonConverterRegistry.TryGet(
                    typeof(Deque<>).MakeGenericType(typeof(DateTimeOffset)),
                    out _
                )
            );
        }
    }
}
