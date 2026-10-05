// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoOnlyModeTests
    {
        private static IEnumerable<TestCaseData> Generators()
        {
            Guid seed = new Guid(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
            yield return new TestCaseData((Func<IRandom>)(() => new SystemRandom(3))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(SystemRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new SquirrelRandom(3))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(SquirrelRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new UnityRandom(3))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(UnityRandom)
            );

            yield return new TestCaseData((Func<IRandom>)(() => new DotNetRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(DotNetRandom)
            );
            yield return new TestCaseData(
                (Func<IRandom>)(() => new Xoshiro256StarStar(seed))
            ).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(Xoshiro256StarStar)
            );
            yield return new TestCaseData(
                (Func<IRandom>)(() => new LinearCongruentialGenerator(seed))
            ).SetName(
                nameof(RandomContinuationSurvivesSaveReload)
                    + "."
                    + nameof(LinearCongruentialGenerator)
            );
            yield return new TestCaseData(
                (Func<IRandom>)(() => new BlastCircuitRandom(seed))
            ).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(BlastCircuitRandom)
            );
            yield return new TestCaseData(
                (Func<IRandom>)(() => new PhotonSpinRandom(seed))
            ).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(PhotonSpinRandom)
            );
            yield return new TestCaseData(
                (Func<IRandom>)(() => new Xoshiro128StarStar(seed))
            ).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(Xoshiro128StarStar)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new XorShiftRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(XorShiftRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new IllusionFlow(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(IllusionFlow)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new PcgRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(PcgRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new XoroShiroRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(XoroShiroRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new StormDropRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(StormDropRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new WyRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(WyRandom)
            );
            yield return new TestCaseData(
                (Func<IRandom>)(() => new FlurryBurstRandom(seed))
            ).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(FlurryBurstRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new RomuDuo(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(RomuDuo)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new Sfc64Random(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(Sfc64Random)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new SplitMix64(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(SplitMix64)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new WaveSplatRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(WaveSplatRandom)
            );
            yield return new TestCaseData((Func<IRandom>)(() => new WDoomRandom(seed))).SetName(
                nameof(RandomContinuationSurvivesSaveReload) + "." + nameof(WDoomRandom)
            );
        }

        [SuppressMessage(
            "Performance",
            "WUH005",
            Justification = "Tests the legacy UnityRandom contract and restores its shared engine state."
        )]
        [TestCaseSource(nameof(Generators))]
        public void RandomContinuationSurvivesSaveReload(Func<IRandom> create)
        {
            UnityEngine.Random.State previous = UnityEngine.Random.state;
            try
            {
                IRandom original = create();
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
            Assert.IsTrue(original.ProtoEquals(restored));
            Assert.AreEqual(
                ProtoEqualityExtensions
                    .GetProtoComparer<SerializableDictionary<int, string>>()
                    .GetHashCode(original),
                ProtoEqualityExtensions
                    .GetProtoComparer<SerializableDictionary<int, string>>()
                    .GetHashCode(restored)
            );
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
