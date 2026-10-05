// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoOnlyModeTests
    {
#if WALLSTOP_PROTO_ONLY
        private static void AssertDeclaredRandomRootComparer<T>(
            T original,
            T equivalent,
            T different
        )
            where T : IRandom
        {
            byte[] payload = Serializer.ProtoSerialize(original);
            T restored = Serializer.ProtoDeserialize<T>(payload);
            ArrayBufferWriter<byte> destination = new();
            Assert.AreEqual(payload.Length, WProtoFacade.Serialize(original, destination));
            CollectionAssert.AreEqual(payload, destination.WrittenSpan.ToArray());
            IEqualityComparer<T> comparer = ProtoEqualityExtensions.GetProtoComparer<T>();
            Assert.IsTrue(original.ProtoEquals(equivalent));
            Assert.IsTrue(comparer.Equals(original, equivalent));
            Assert.IsTrue(comparer.Equals(original, restored));
            Assert.AreEqual(comparer.GetHashCode(original), comparer.GetHashCode(equivalent));
            Assert.AreEqual(comparer.GetHashCode(original), comparer.GetHashCode(restored));
            HashSet<T> retained = new(comparer) { original };
            Assert.IsTrue(retained.Contains(equivalent));
            Assert.IsTrue(retained.Contains(restored));
            different.NextUint();
            Assert.IsFalse(original.ProtoEquals(different));
            Assert.IsFalse(comparer.Equals(original, different));
            Assert.IsFalse(retained.Contains(different));
        }
#endif

        [SuppressMessage(
            "Performance",
            "WUH005",
            Justification = "Tests the legacy UnityRandom contract and restores its shared engine state."
        )]
        [TestCase(
            nameof(SystemRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(SystemRandom)
        )]
        [TestCase(
            nameof(SquirrelRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(SquirrelRandom)
        )]
        [TestCase(
            nameof(UnityRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(UnityRandom)
        )]
        [TestCase(
            nameof(DotNetRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(DotNetRandom)
        )]
        [TestCase(
            nameof(Xoshiro256StarStar),
            TestName = nameof(RandomContinuationSurvivesSaveReload)
                + "."
                + nameof(Xoshiro256StarStar)
        )]
        [TestCase(
            nameof(LinearCongruentialGenerator),
            TestName = nameof(RandomContinuationSurvivesSaveReload)
                + "."
                + nameof(LinearCongruentialGenerator)
        )]
        [TestCase(
            nameof(BlastCircuitRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload)
                + "."
                + nameof(BlastCircuitRandom)
        )]
        [TestCase(
            nameof(PhotonSpinRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(PhotonSpinRandom)
        )]
        [TestCase(
            nameof(Xoshiro128StarStar),
            TestName = nameof(RandomContinuationSurvivesSaveReload)
                + "."
                + nameof(Xoshiro128StarStar)
        )]
        [TestCase(
            nameof(XorShiftRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(XorShiftRandom)
        )]
        [TestCase(
            nameof(IllusionFlow),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(IllusionFlow)
        )]
        [TestCase(
            nameof(PcgRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(PcgRandom)
        )]
        [TestCase(
            nameof(XoroShiroRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(XoroShiroRandom)
        )]
        [TestCase(
            nameof(StormDropRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(StormDropRandom)
        )]
        [TestCase(
            nameof(WyRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(WyRandom)
        )]
        [TestCase(
            nameof(FlurryBurstRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload)
                + "."
                + nameof(FlurryBurstRandom)
        )]
        [TestCase(
            nameof(RomuDuo),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(RomuDuo)
        )]
        [TestCase(
            nameof(Sfc64Random),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(Sfc64Random)
        )]
        [TestCase(
            nameof(SplitMix64),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(SplitMix64)
        )]
        [TestCase(
            nameof(WaveSplatRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(WaveSplatRandom)
        )]
        [TestCase(
            nameof(WDoomRandom),
            TestName = nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(WDoomRandom)
        )]
        public void RandomContinuationSurvivesSaveReload(string generator)
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                Guid seed = new Guid(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
                IRandom original = generator switch
                {
                    nameof(SystemRandom) => new SystemRandom(3),
                    nameof(SquirrelRandom) => new SquirrelRandom(3),
                    nameof(UnityRandom) => new UnityRandom(3),
                    nameof(DotNetRandom) => new DotNetRandom(seed),
                    nameof(Xoshiro256StarStar) => new Xoshiro256StarStar(seed),
                    nameof(LinearCongruentialGenerator) => new LinearCongruentialGenerator(seed),
                    nameof(BlastCircuitRandom) => new BlastCircuitRandom(seed),
                    nameof(PhotonSpinRandom) => new PhotonSpinRandom(seed),
                    nameof(Xoshiro128StarStar) => new Xoshiro128StarStar(seed),
                    nameof(XorShiftRandom) => new XorShiftRandom(seed),
                    nameof(IllusionFlow) => new IllusionFlow(seed),
                    nameof(PcgRandom) => new PcgRandom(seed),
                    nameof(XoroShiroRandom) => new XoroShiroRandom(seed),
                    nameof(StormDropRandom) => new StormDropRandom(seed),
                    nameof(WyRandom) => new WyRandom(seed),
                    nameof(FlurryBurstRandom) => new FlurryBurstRandom(seed),
                    nameof(RomuDuo) => new RomuDuo(seed),
                    nameof(Sfc64Random) => new Sfc64Random(seed),
                    nameof(SplitMix64) => new SplitMix64(seed),
                    nameof(WaveSplatRandom) => new WaveSplatRandom(seed),
                    nameof(WDoomRandom) => new WDoomRandom(seed),
                    _ => throw new ArgumentOutOfRangeException(nameof(generator), generator, null),
                };
                for (int index = 0; index < 17; ++index)
                {
                    original.NextUint();
                }
                byte[] payload = Serializer.ProtoSerialize(original);
                uint[] expected = new uint[128];
                for (int index = 0; index < expected.Length; ++index)
                {
                    expected[index] = original.NextUint();
                }
                IRandom restored = Serializer.ProtoDeserialize<IRandom>(payload);
                Assert.AreEqual(original.GetType(), restored.GetType());
                foreach (uint expectedValue in expected)
                {
                    Assert.AreEqual(expectedValue, restored.NextUint());
                }
            }
            finally
            {
                UnityEngine.Random.state = previous;
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        [TestCase(10000)]
        public void PlayerDataCollectionsRetainExactKeysAndValues(int count)
        {
            SerializableDictionary<int, string> original =
                new SerializableDictionary<int, string>();
            for (int index = 0; index < count; ++index)
            {
                original.Add(index, index % 2 == 0 ? "   " : "retained");
            }
            byte[] payload = Serializer.ProtoSerialize(original);
            SerializableDictionary<int, string> restored = Serializer.ProtoDeserialize<
                SerializableDictionary<int, string>
            >(payload);
            Assert.AreEqual(original.Count, restored.Count);
            foreach (KeyValuePair<int, string> pair in original)
            {
                Assert.IsTrue(restored.TryGetValue(pair.Key, out string actual));
                Assert.AreEqual(pair.Value, actual);
            }
#if WALLSTOP_PROTO_ONLY
            Assert.IsTrue(original.ProtoEquals(restored));
            Assert.AreEqual(
                ProtoEqualityExtensions
                    .GetProtoComparer<SerializableDictionary<int, string>>()
                    .GetHashCode(original),
                ProtoEqualityExtensions
                    .GetProtoComparer<SerializableDictionary<int, string>>()
                    .GetHashCode(restored)
            );
#endif
        }

        [TestCase(0f)]
        [TestCase(1f)]
        [TestCase(-1f)]
        [TestCase(float.MaxValue)]
        public void UnityValuesRoundTripAcrossBothDeserializeShapes(float value)
        {
            Vector3 original = new Vector3(value, -value, value);
            byte[] payload = Serializer.ProtoSerialize(original);
            Vector3 restored = Serializer.ProtoDeserialize<Vector3>(payload);
            Vector3 concrete = Serializer.ProtoDeserialize<Vector3>(payload, typeof(Vector3));
            Assert.AreEqual(original, restored);
            Assert.AreEqual(original, concrete);
        }

#if WALLSTOP_PROTO_ONLY
        [TestCase(false)]
        [TestCase(true)]
        public void DeclaredRandomRootsRemainUsableByProtoComparers(bool abstractRoot)
        {
            SystemRandom original = new(3);
            SystemRandom equivalent = new(3);
            SystemRandom different = new(3);
            if (abstractRoot)
            {
                AssertDeclaredRandomRootComparer<AbstractRandom>(original, equivalent, different);
            }
            else
            {
                AssertDeclaredRandomRootComparer<IRandom>(original, equivalent, different);
            }
        }

        [Test]
        public void UndeclaredObjectRootIsRejectedBySerializationAndProtoComparers()
        {
            object original = new SystemRandom(3);
            object equivalent = new SystemRandom(3);
            Assert.Throws<SerializationTypeException>(() => Serializer.ProtoSerialize(original));
            ArrayBufferWriter<byte> destination = new();
            Assert.Throws<SerializationTypeException>(() =>
                WProtoFacade.Serialize(original, destination)
            );
            Assert.AreEqual(0, destination.WrittenCount);
            Assert.Throws<SerializationTypeException>(() => original.ProtoEquals(equivalent));
            IEqualityComparer<object> comparer = ProtoEqualityExtensions.GetProtoComparer<object>();
            Assert.Throws<SerializationTypeException>(() => comparer.Equals(original, equivalent));
            Assert.Throws<SerializationTypeException>(() => comparer.GetHashCode(original));
        }

        [Test]
        public void MissingFormatterCannotReachTheLegacyBackend()
        {
            byte[] destination = new byte[] { 3, 7, 9 };
            byte[] originalDestination = destination;
            Assert.Throws<SerializationTypeException>(() => Serializer.ProtoSerialize(typeof(int)));
            Assert.Throws<SerializationTypeException>(() =>
                Serializer.ProtoSerialize(typeof(int), ref destination)
            );
            Assert.AreSame(originalDestination, destination);
            CollectionAssert.AreEqual(new byte[] { 3, 7, 9 }, destination);
            Assert.Throws<SerializationTypeException>(() =>
                Serializer.ProtoDeserialize<Type>(Array.Empty<byte>())
            );
            Assert.Throws<SerializationTypeException>(() =>
                Serializer.ProtoDeserialize<Type>(Array.Empty<byte>(), typeof(Type))
            );
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.RegisterProtobufRoot(typeof(object), typeof(WProtoBufferWriterContract))
            );
        }
#endif
    }
}
