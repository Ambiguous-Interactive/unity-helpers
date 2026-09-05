// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class RandomProtoSerializationTests
    {
        private const int NumGenerations = 1000;

        private static T SerializeDeserialize<T>(T original)
            where T : IRandom
        {
            byte[] serialized = Serializer.ProtoSerialize(original);
            Assert.IsTrue(serialized != null, "Serialization should produce non-null bytes");
            Assert.Greater(serialized.Length, 0, "Serialization should produce non-empty bytes");

            T deserialized = Serializer.ProtoDeserialize<T>(serialized);
            Assert.IsTrue(deserialized != null, "Deserialization should produce non-null instance");

            return deserialized;
        }

        private static void VerifySerializationAndGeneration<T>(T original)
            where T : IRandom
        {
            RandomState initialState = original.InternalState;

            for (int i = 0; i < NumGenerations; ++i)
            {
                original.NextUint();
            }

            RandomState stateAfterGeneration = original.InternalState;

            Assert.AreNotEqual(
                initialState,
                stateAfterGeneration,
                "State should change after generation"
            );

            T deserialized = SerializeDeserialize(original);

            Assert.AreEqual(
                original.InternalState,
                deserialized.InternalState,
                "Internal states should match after deserialization"
            );

            for (int i = 0; i < NumGenerations; ++i)
            {
                uint originalValue = original.NextUint();
                uint deserializedValue = deserialized.NextUint();
                Assert.AreEqual(
                    originalValue,
                    deserializedValue,
                    $"Random value {i} should match after deserialization"
                );
            }

            Assert.AreEqual(
                original.InternalState,
                deserialized.InternalState,
                "Internal states should match after generating numbers"
            );
        }

        [Test]
        public void DotNetRandomSerializesAndDeserializes()
        {
            DotNetRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void DotNetRandomWithDifferentStatesSerializesCorrectly()
        {
            DotNetRandom random1 = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
            DotNetRandom random2 = new(Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"));

            DotNetRandom deserialized1 = SerializeDeserialize(random1);
            DotNetRandom deserialized2 = SerializeDeserialize(random2);

            Assert.AreEqual(random1.InternalState, deserialized1.InternalState);
            Assert.AreEqual(random2.InternalState, deserialized2.InternalState);
            Assert.AreNotEqual(deserialized1.InternalState, deserialized2.InternalState);
        }

        [Test]
        public void PcgRandomSerializesAndDeserializes()
        {
            PcgRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void PcgRandomWithCachedGaussianSerializesCorrectly()
        {
            PcgRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));

            random.NextGaussian();

            PcgRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
            Assert.AreEqual(random.InternalState.Gaussian, deserialized.InternalState.Gaussian);
        }

        [Test]
        public void XorShiftRandomSerializesAndDeserializes()
        {
            XorShiftRandom random = new(12345);
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void XorShiftRandomWithZeroStateHandledCorrectly()
        {
            XorShiftRandom random = new(0);
            XorShiftRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
            Assert.AreEqual(random.NextUint(), deserialized.NextUint());
        }

        [Test]
        public void WyRandomSerializesAndDeserializes()
        {
            WyRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void WyRandomWithExtremeStatesSerializesCorrectly()
        {
            WyRandom randomMin = new(ulong.MinValue);
            WyRandom randomMax = new(ulong.MaxValue);

            WyRandom deserializedMin = SerializeDeserialize(randomMin);
            WyRandom deserializedMax = SerializeDeserialize(randomMax);

            Assert.AreEqual(randomMin.InternalState, deserializedMin.InternalState);
            Assert.AreEqual(randomMax.InternalState, deserializedMax.InternalState);
        }

        [Test]
        public void XoroShiroRandomSerializesAndDeserializes()
        {
            XoroShiroRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void XoroShiroRandomWithBothStatesSerializesCorrectly()
        {
            XoroShiroRandom random = new(0x123456789ABCDEF0UL, 0xFEDCBA9876543210UL);

            XoroShiroRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
            Assert.AreEqual(random.InternalState.State1, deserialized.InternalState.State1);
            Assert.AreEqual(random.InternalState.State2, deserialized.InternalState.State2);
        }

        [Test]
        public void PhotonSpinRandomSerializesAndDeserializes()
        {
            PhotonSpinRandom random = new(Guid.Parse("0AF7CF4F-44F6-421E-B7DC-1ADEF9F27E19"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void StormDropRandomSerializesAndDeserializes()
        {
            StormDropRandom random = new(987654321u);
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void BlastCircuitRandomSerializesAndDeserializes()
        {
            BlastCircuitRandom random = new(Guid.Parse("89B35D54-4DD4-45F4-9B14-24A3A4595F6C"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void WaveSplatRandomSerializesAndDeserializes()
        {
            WaveSplatRandom random = new(0x1234_5678_9ABC_DEF0UL);
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void UnityRandomSerializesAndDeserializes()
        {
            UnityRandom random = new(42);
            UnityRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
        }

        /*
            Advance the engine between save and load to prove snapshots restore position rather than just a
            seed.
        */
#pragma warning disable WUH005
        [Test]
        public void UnityRandomProtoRoundTripResumesTheEnginePosition()
        {
            UnityRandom random = new(4242);
            for (int i = 0; i < 37; ++i)
            {
                random.NextUint();
            }

            byte[] saved = Serializer.ProtoSerialize(random);
            uint[] expected = new uint[64];
            for (int i = 0; i < expected.Length; ++i)
            {
                expected[i] = random.NextUint();
            }

            for (int i = 0; i < 500; ++i)
            {
                _ = UnityEngine.Random.value;
            }

            UnityRandom restored = Serializer.ProtoDeserialize<UnityRandom>(saved);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i], restored.NextUint(), $"draw {i}");
            }
        }
#pragma warning restore WUH005

        [Test]
        public void UnityRandomWithNullSeedSerializesCorrectly()
        {
            UnityRandom random = new(null);
            UnityRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
        }

        [Test]
        public void SystemRandomSerializesAndDeserializes()
        {
            SystemRandom random = new(12345);
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void SystemRandomWithNegativeSeedSerializesCorrectly()
        {
            SystemRandom random = new(-999);
            SystemRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(random.Next(), deserialized.Next());
            }
        }

        [Test]
        public void SystemRandomWithMinIntSeedSerializesCorrectly()
        {
            SystemRandom random = new(int.MinValue);
            SystemRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
        }

        [Test]
        public void LinearCongruentialGeneratorSerializesAndDeserializes()
        {
            LinearCongruentialGenerator random = new(12345);
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void LinearCongruentialGeneratorWithGuidSeedSerializesCorrectly()
        {
            LinearCongruentialGenerator random = new(
                Guid.Parse("12345678-1234-1234-1234-123456789012")
            );

            LinearCongruentialGenerator deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
        }

        [Test]
        public void SquirrelRandomSerializesAndDeserializes()
        {
            SquirrelRandom random = new(12345);
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void SquirrelRandomAfterNoiseGenerationSerializesCorrectly()
        {
            SquirrelRandom random = new(12345);

            _ = random.NextNoise(10, 20);

            for (int i = 0; i < 50; ++i)
            {
                random.NextUint();
            }

            SquirrelRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            Assert.AreEqual(random.NextNoise(10, 20), deserialized.NextNoise(10, 20));
        }

        [Test]
        public void RomuDuoSerializesAndDeserializes()
        {
            RomuDuo random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void RomuDuoWithSpecificSeedsSerializesCorrectly()
        {
            RomuDuo random = new(0x123456789ABCDEF0UL, 0xFEDCBA9876543210UL);

            RomuDuo deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
            Assert.AreEqual(random.InternalState.State1, deserialized.InternalState.State1);
            Assert.AreEqual(random.InternalState.State2, deserialized.InternalState.State2);
        }

        [Test]
        public void SplitMix64SerializesAndDeserializes()
        {
            SplitMix64 random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void SplitMix64WithUlongSeedSerializesCorrectly()
        {
            SplitMix64 random = new(0x123456789ABCDEF0UL);

            SplitMix64 deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);
            Assert.AreEqual(random.InternalState.State1, deserialized.InternalState.State1);
        }

        [Test]
        public void IllusionFlowSerializesAndDeserializes()
        {
            IllusionFlow random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));
            VerifySerializationAndGeneration(random);
        }

        [Test]
        public void IllusionFlowWithExtraSeedSerializesCorrectly()
        {
            IllusionFlow random = new(
                Guid.Parse("12345678-1234-1234-1234-123456789012"),
                0x12345678U
            );

            IllusionFlow deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(random.NextUint(), deserialized.NextUint());
            }
        }

        [Test]
        public void AllRandomImplementationsCanBeSerializedAsBatchTest()
        {
            IRandom[] randoms =
            {
                new DotNetRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new PcgRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new XorShiftRandom(12345),
                new WyRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new XoroShiroRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new UnityRandom(42),
                new SystemRandom(12345),
                new LinearCongruentialGenerator(12345),
                new SquirrelRandom(12345),
                new RomuDuo(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new SplitMix64(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new IllusionFlow(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new FlurryBurstRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new PhotonSpinRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new StormDropRandom(12345u),
                new BlastCircuitRandom(Guid.Parse("12345678-1234-1234-1234-123456789012")),
                new WaveSplatRandom(0xC0FFEEUL),
                new WDoomRandom(seedIndex: 7),
            };

            foreach (IRandom random in randoms)
            {
                for (int i = 0; i < 50; ++i)
                {
                    random.NextUint();
                }

                byte[] serialized = Serializer.ProtoSerialize(random);
                Assert.IsTrue(serialized != null, $"{random.GetType().Name} serialization failed");
                Assert.Greater(
                    serialized.Length,
                    0,
                    $"{random.GetType().Name} produced empty serialization"
                );

                IRandom deserialized = Serializer.ProtoDeserialize<IRandom>(serialized);
                Assert.IsTrue(
                    deserialized != null,
                    $"{random.GetType().Name} deserialization failed"
                );

                Assert.AreEqual(
                    random.InternalState,
                    deserialized.InternalState,
                    $"{random.GetType().Name} state mismatch"
                );
            }
        }

        [Test]
        public void WDoomRandomAtTableStartKeepsItsIndex()
        {
            // Protobuf omits index zero; constructor-created state must not replace the saved default.
            WDoomRandom random = new(seedIndex: 0);

            IRandom deserialized = Serializer.ProtoDeserialize<IRandom>(
                Serializer.ProtoSerialize<IRandom>(random)
            );

            for (int i = 0; i < 16; ++i)
            {
                Assert.AreEqual(random.NextUint(), deserialized.NextUint(), $"draw {i}");
            }
        }

        [Test]
        public void SerializationPreservesRandomSequenceForAllTypes()
        {
            Type[] randomTypes =
            {
                typeof(DotNetRandom),
                typeof(PcgRandom),
                typeof(XorShiftRandom),
                typeof(WyRandom),
                typeof(XoroShiroRandom),
                typeof(SystemRandom),
                typeof(LinearCongruentialGenerator),
                typeof(SquirrelRandom),
                typeof(RomuDuo),
                typeof(SplitMix64),
                typeof(IllusionFlow),
                typeof(FlurryBurstRandom),
                typeof(PhotonSpinRandom),
                typeof(StormDropRandom),
                typeof(BlastCircuitRandom),
                typeof(WaveSplatRandom),
                typeof(WDoomRandom),
                typeof(Xoshiro128StarStar),
                typeof(Xoshiro256StarStar),
                typeof(Sfc64Random),
            };

            foreach (Type randomType in randomTypes)
            {
                IRandom random = (IRandom)Activator.CreateInstance(randomType);

                byte[] serialized = Serializer.ProtoSerialize(random);
                IRandom deserialized = Serializer.ProtoDeserialize<IRandom>(serialized);

                for (int i = 0; i < 1_000; ++i)
                {
                    Assert.AreEqual(
                        random.NextUint(),
                        deserialized.NextUint(),
                        $"{randomType.Name} sequence mismatch at index {i}"
                    );
                }
            }
        }

        [Test]
        public void DotNetRandomAfterManyGenerationsSerializesCorrectly()
        {
            DotNetRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));

            for (int i = 0; i < 10000; ++i)
            {
                random.NextUint();
            }

            DotNetRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(random.NextUint(), deserialized.NextUint());
            }
        }

        [Test]
        public void PcgRandomCopyAndSerializeProduceSameResults()
        {
            PcgRandom original = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));

            for (int i = 0; i < 100; ++i)
            {
                original.NextUint();
            }

            PcgRandom copied = (PcgRandom)original.Copy();
            PcgRandom serialized = SerializeDeserialize(original);

            for (int i = 0; i < 100; ++i)
            {
                uint originalValue = original.NextUint();
                uint copiedValue = copied.NextUint();
                uint serializedValue = serialized.NextUint();

                Assert.AreEqual(originalValue, copiedValue, $"Copy mismatch at {i}");
                Assert.AreEqual(originalValue, serializedValue, $"Serialization mismatch at {i}");
            }
        }

        [Test]
        public void MultipleSerializationRoundtripsPreserveState()
        {
            PcgRandom random = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));

            PcgRandom deserialized1 = SerializeDeserialize(random);
            Assert.AreEqual(random.InternalState, deserialized1.InternalState);

            for (int i = 0; i < 50; ++i)
            {
                random.NextUint();
                deserialized1.NextUint();
            }

            PcgRandom deserialized2 = SerializeDeserialize(random);
            Assert.AreEqual(random.InternalState, deserialized2.InternalState);

            PcgRandom deserialized3 = SerializeDeserialize(deserialized2);
            Assert.AreEqual(deserialized2.InternalState, deserialized3.InternalState);
        }

        /// <summary>
        /// Every generator, built the ordinary way, so one case cannot quietly drop out.
        /// </summary>
        private static IEnumerable<TestCaseData> EveryGenerator()
        {
            Guid seed = Guid.Parse("12345678-1234-1234-1234-123456789012");
            yield return Named("Restored", "DotNetRandom", new DotNetRandom(seed));
            yield return Named("Restored", "PcgRandom", new PcgRandom(seed));
            yield return Named("Restored", "XorShiftRandom", new XorShiftRandom(12345));
            yield return Named("Restored", "WyRandom", new WyRandom(seed));
            yield return Named("Restored", "XoroShiroRandom", new XoroShiroRandom(seed));
            yield return Named("Restored", "SystemRandom", new SystemRandom(12345));
            yield return Named(
                "Restored",
                "LinearCongruentialGenerator",
                new LinearCongruentialGenerator(12345)
            );
            yield return Named("Restored", "SquirrelRandom", new SquirrelRandom(12345));
            yield return Named("Restored", "RomuDuo", new RomuDuo(seed));
            yield return Named("Restored", "SplitMix64", new SplitMix64(seed));
            yield return Named("Restored", "IllusionFlow", new IllusionFlow(seed));
            yield return Named("Restored", "FlurryBurstRandom", new FlurryBurstRandom(seed));
            yield return Named("Restored", "PhotonSpinRandom", new PhotonSpinRandom(seed));
            yield return Named("Restored", "StormDropRandom", new StormDropRandom(12345u));
            yield return Named("Restored", "BlastCircuitRandom", new BlastCircuitRandom(seed));
            yield return Named("Restored", "WaveSplatRandom", new WaveSplatRandom(0xC0FFEEUL));
            yield return Named("Restored", "WDoomRandom", new WDoomRandom(seedIndex: 7));
        }

        /// <summary>
        /// Every generator a caller can seed so that its ENTIRE serialized state is its type's
        /// default, which is the one state protobuf writes nothing about.
        /// </summary>
        /// <remarks>
        /// Seed zero is the most ordinary seed there is, so this is not a corner reachable only
        /// by a crafted payload -- <c>new SquirrelRandom(0)</c> is a line a game writes.
        /// </remarks>
        private static IEnumerable<TestCaseData> EveryAllDefaultGenerator()
        {
            yield return Named(
                "AllDefault",
                "BlastCircuitRandom",
                new BlastCircuitRandom(0UL, 0UL, 0UL, 0UL)
            );
            yield return Named(
                "AllDefault",
                "FlurryBurstRandom",
                new FlurryBurstRandom(default(RandomState))
            );
            yield return Named(
                "AllDefault",
                "LinearCongruentialGenerator",
                new LinearCongruentialGenerator(0)
            );
            yield return Named("AllDefault", "SplitMix64", new SplitMix64(0UL));
            yield return Named("AllDefault", "SquirrelRandom", new SquirrelRandom(0));
            yield return Named("AllDefault", "WaveSplatRandom", new WaveSplatRandom(0UL));
            yield return Named("AllDefault", "WDoomRandom", new WDoomRandom(seedIndex: 0));
            yield return Named("AllDefault", "WyRandom", new WyRandom(0UL));
        }

        /// <summary>Names one case, uniquely across both sources.</summary>
        /// <remarks>
        /// <c>SetName</c> replaces the WHOLE test name, so the suite has to be part of it or the
        /// two sources both produce a case called <c>WyRandom</c> and a failure report cannot say
        /// which one it came from. The overload that keeps the method name is absent from the
        /// NUnit that Unity 2021.3 ships.
        /// </remarks>
        private static TestCaseData Named(string suite, string name, IRandom random)
        {
            return new TestCaseData(random).SetName(suite + "-" + name);
        }

        private static IRandom ProtobufNetRoundTrip(IRandom random)
        {
            using MemoryStream written = new();
            ProtoBuf.Serializer.Serialize(written, (AbstractRandom)random);
            using MemoryStream read = new(written.ToArray());
            return ProtoBuf.Serializer.Deserialize<AbstractRandom>(read);
        }

        private static IEnumerable<TestCaseData> EveryGeneratorRepairedAfterDeserialization()
        {
            yield return EmptySubtype("DotNetRandom", 100, true);
            yield return EmptySubtype("PcgRandom", 101, true);
            yield return EmptySubtype("XorShiftRandom", 102, true);
            yield return EmptySubtype("XoroShiroRandom", 104, true);
            yield return EmptySubtype("SystemRandom", 106, true);
            yield return EmptySubtype("RomuDuo", 109, true);
            yield return EmptySubtype("PhotonSpinRandom", 113, true);
            yield return EmptySubtype("StormDropRandom", 114, true);
        }

        private static TestCaseData EmptySubtype(
            string name,
            int includeTag,
            bool expectVariedOutput
        )
        {
            byte[] payload = new byte[WProtoSizes.TagSize(includeTag) + 1];
            WProtoWriter writer = new(payload);
            if (
                !writer.TryWriteTag(includeTag, WProtoWireType.LengthDelimited)
                || !writer.TryWriteLengthPrefix(0)
            )
            {
                throw new InvalidOperationException($"Could not build the {name} test payload.");
            }

            return new TestCaseData(payload, expectVariedOutput).SetName(name);
        }

        [TestCaseSource(nameof(EveryGeneratorRepairedAfterDeserialization))]
        public void AGeneratorIsRepairedBeforeItsFirstDraw(byte[] payload, bool expectVariedOutput)
        {
            AbstractRandom wallstopProto = Serializer.ProtoDeserialize<AbstractRandom>(payload);
            using MemoryStream read = new(payload);
            AbstractRandom protobufNet = ProtoBuf.Serializer.Deserialize<AbstractRandom>(read);

            uint[] expected = Draw(protobufNet);
            CollectionAssert.AreEqual(expected, Draw(wallstopProto));
            if (expectVariedOutput)
            {
                Assert.Greater(
                    new HashSet<uint>(expected).Count,
                    1,
                    "a dead stream repeats one value"
                );
            }
        }

        [TestCaseSource(nameof(EveryGenerator))]
        public void ARestoredGeneratorCanStillProduceAGuid(IRandom random)
        {
            /*
                SkipConstructor leaves the unsaved GUID buffer null; exercise both readers to catch first-use
                failures.
            */
            foreach (IRandom restored in new[] { RoundTrip(random), ProtobufNetRoundTrip(random) })
            {
                Guid first = restored.NextGuid();
                Guid second = restored.NextGuid();
                Assert.AreNotEqual(Guid.Empty, first);
                Assert.AreNotEqual(first, second);
            }
        }

        [TestCase(-1, 5)]
        [TestCase(33, -1)]
        [TestCase(int.MaxValue, int.MinValue)]
        public void MalformedCommonReservoirsAreRepairedBeforeDrawing(int bitCount, int byteCount)
        {
            RandomState malformedState = new(
                state1: 1UL,
                bitBuffer: uint.MaxValue,
                bitCount: bitCount,
                byteBuffer: uint.MaxValue,
                byteCount: byteCount
            );
            XorShiftRandom constructed = new(malformedState);
            AssertCommonReservoirsWereRepaired(constructed);

            byte[] payload = BuildMalformedCommonReservoirPayload(bitCount, byteCount);
            AbstractRandom wallstopProto = Serializer.ProtoDeserialize<AbstractRandom>(payload);
            using MemoryStream read = new(payload);
            AbstractRandom protobufNet = ProtoBuf.Serializer.Deserialize<AbstractRandom>(read);

            AssertCommonReservoirsWereRepaired(wallstopProto);
            AssertCommonReservoirsWereRepaired(protobufNet);
            CollectionAssert.AreEqual(
                DrawCommonValues(protobufNet),
                DrawCommonValues(wallstopProto)
            );
        }

        [TestCaseSource(nameof(EveryAllDefaultGenerator))]
        public void AnAllDefaultStateRestoresTheStreamItSaved(IRandom random)
        {
            // Omitted default fields must not retain constructor-generated random state on reload.
            byte[] payload = Serializer.ProtoSerialize<IRandom>(random);
            Assert.LessOrEqual(payload.Length, 3, "expected a payload naming no member");

            /*
                Every restored generator is taken from the saved state, so they must be built before the draw
                below advances the original past it.
            */
            IRandom viaWallstopProto = Serializer.ProtoDeserialize<IRandom>(payload);
            IRandom viaWallstopProtoAgain = Serializer.ProtoDeserialize<IRandom>(payload);
            IRandom viaProtobufNet = ProtobufNetRoundTrip(random);

            uint[] expected = Draw(random);
            CollectionAssert.AreEqual(expected, Draw(viaWallstopProto));
            CollectionAssert.AreEqual(expected, Draw(viaWallstopProtoAgain));
            CollectionAssert.AreEqual(expected, Draw(viaProtobufNet));
        }

        [TestCase(-2, int.MaxValue)]
        [TestCase(5, 5)]
        public void MalformedSystemRandomIndicesAreRepairedBeforeDrawing(int inext, int inextp)
        {
            byte[] payload = BuildMalformedSystemRandomPayload(inext, inextp);
            int expectedInext = inext < 0 || 55 < inext ? 0 : inext;
            int expectedInextp = (expectedInext + 20) % 55 + 1;

            AssertReadersDrawMatchingSafeStreams(
                payload,
                unchecked((ulong)expectedInext),
                unchecked((ulong)expectedInextp),
                expectVariedOutput: true
            );
        }

        [Test]
        public void ExcessiveDotNetReplayCountIsBoundedBeforeDrawing()
        {
            byte[] payload = BuildExcessiveDotNetReplayPayload();

            InvalidOperationException wallstopProto = Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize(payload, out AbstractRandom _)
            );
            Assert.IsInstanceOf<System.Runtime.Serialization.SerializationException>(
                wallstopProto.InnerException
            );
            using MemoryStream read = new(payload);
            Assert.Catch<Exception>(() => ProtoBuf.Serializer.Deserialize<AbstractRandom>(read));
        }

        [Test]
        public void AmplifiedDotNetSnapshotLengthCannotAllocateFromTheHeader()
        {
            byte[] payload = BuildAmplifiedDotNetSnapshotPayload();

            AssertReadersDrawMatchingSafeStreams(payload);
        }

        [Test]
        public void SystemRandomStateConstructorRepairsMalformedIndices()
        {
            IReadOnlyList<byte> seedPayload = new SystemRandom(0).InternalState.PayloadBytes;
            SystemRandom random = new(
                new RandomState(state1: 5UL, state2: 5UL, payload: seedPayload)
            );

            Assert.AreEqual(5UL, random.InternalState.State1);
            Assert.AreEqual(26UL, random.InternalState.State2);
            Assert.Greater(
                new HashSet<uint>(Draw(random)).Count,
                1,
                "a dead stream repeats one value"
            );
        }

        private static byte[] BuildMalformedSystemRandomPayload(int inext, int inextp)
        {
            IReadOnlyList<byte> seedPayload = new SystemRandom(0).InternalState.PayloadBytes;
            byte[] nested = new byte[1024];
            WProtoWriter nestedWriter = new(nested);
            Assert.IsTrue(nestedWriter.TryWriteTag(6, WProtoWireType.Varint));
            Assert.IsTrue(nestedWriter.TryWriteInt32(inext));
            Assert.IsTrue(nestedWriter.TryWriteTag(7, WProtoWireType.Varint));
            Assert.IsTrue(nestedWriter.TryWriteInt32(inextp));
            Assert.IsTrue(
                nestedWriter.TryBeginLengthDelimited(8, false, out WProtoLengthToken token)
            );
            for (int offset = 0; offset < seedPayload.Count; offset += sizeof(int))
            {
                int seedValue = unchecked(
                    seedPayload[offset]
                    | (seedPayload[offset + 1] << 8)
                    | (seedPayload[offset + 2] << 16)
                    | (seedPayload[offset + 3] << 24)
                );
                Assert.IsTrue(nestedWriter.TryWriteInt32(seedValue));
            }
            Assert.IsTrue(nestedWriter.TryCloseLengthDelimited(token));

            return WrapSubtypePayload(106, nestedWriter.Written);
        }

        private static byte[] BuildMalformedCommonReservoirPayload(int bitCount, int byteCount)
        {
            byte[] payload = new byte[64];
            WProtoWriter writer = new(payload);
            /*
                protobuf-net must learn the concrete subtype before it can apply base members; if a base member
                arrives first, it correctly refuses to instantiate AbstractRandom.
            */
            Assert.IsTrue(writer.TryWriteTag(102, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteLengthPrefix(0));
            Assert.IsTrue(writer.TryWriteTag(2, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteVarint32(uint.MaxValue));
            Assert.IsTrue(writer.TryWriteTag(3, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(bitCount));
            Assert.IsTrue(writer.TryWriteTag(4, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteVarint32(uint.MaxValue));
            Assert.IsTrue(writer.TryWriteTag(5, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(byteCount));
            return writer.Written.ToArray();
        }

        private static byte[] BuildExcessiveDotNetReplayPayload()
        {
            byte[] nested = new byte[16];
            WProtoWriter nestedWriter = new(nested);
            Assert.IsTrue(nestedWriter.TryWriteTag(6, WProtoWireType.Varint));
            Assert.IsTrue(nestedWriter.TryWriteVarint64(ulong.MaxValue));

            return WrapSubtypePayload(100, nestedWriter.Written);
        }

        private static byte[] BuildAmplifiedDotNetSnapshotPayload()
        {
            byte[] snapshot = new byte[12];
            snapshot[0] = 0xFF;
            snapshot[1] = 0xFF;
            snapshot[2] = 0xFF;
            snapshot[3] = 0x7F;

            byte[] nested = new byte[32];
            WProtoWriter nestedWriter = new(nested);
            Assert.IsTrue(nestedWriter.TryWriteTag(8, WProtoWireType.LengthDelimited));
            Assert.IsTrue(nestedWriter.TryWriteLengthPrefix(snapshot.Length));
            Assert.IsTrue(nestedWriter.TryWriteRaw(snapshot));

            return WrapSubtypePayload(100, nestedWriter.Written);
        }

        private static byte[] WrapSubtypePayload(int includeTag, ReadOnlySpan<byte> nested)
        {
            byte[] payload = new byte[nested.Length + 16];
            WProtoWriter writer = new(payload);
            Assert.IsTrue(writer.TryWriteTag(includeTag, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteLengthPrefix(nested.Length));
            Assert.IsTrue(writer.TryWriteRaw(nested));
            return writer.Written.ToArray();
        }

        private static void AssertReadersDrawMatchingSafeStreams(
            byte[] payload,
            ulong? expectedState1 = null,
            ulong? expectedState2 = null,
            bool expectVariedOutput = false
        )
        {
            AbstractRandom wallstopProto = Serializer.ProtoDeserialize<AbstractRandom>(payload);
            using MemoryStream read = new(payload);
            AbstractRandom protobufNet = ProtoBuf.Serializer.Deserialize<AbstractRandom>(read);

            if (expectedState1.HasValue)
            {
                Assert.AreEqual(expectedState1.Value, wallstopProto.InternalState.State1);
                Assert.AreEqual(expectedState1.Value, protobufNet.InternalState.State1);
            }
            if (expectedState2.HasValue)
            {
                Assert.AreEqual(expectedState2.Value, wallstopProto.InternalState.State2);
                Assert.AreEqual(expectedState2.Value, protobufNet.InternalState.State2);
            }

            uint[] expected = Draw(protobufNet);
            CollectionAssert.AreEqual(expected, Draw(wallstopProto));
            if (expectVariedOutput)
            {
                Assert.Greater(
                    new HashSet<uint>(expected).Count,
                    1,
                    "a dead stream repeats one value"
                );
            }
        }

        private static IRandom RoundTrip(IRandom random)
        {
            return Serializer.ProtoDeserialize<IRandom>(Serializer.ProtoSerialize<IRandom>(random));
        }

        private static void AssertCommonReservoirsWereRepaired(IRandom random)
        {
            Assert.AreEqual(0, random.InternalState.BitCount);
            Assert.AreEqual(0U, random.InternalState.BitBuffer);
            Assert.AreEqual(0, random.InternalState.ByteCount);
            Assert.AreEqual(0U, random.InternalState.ByteBuffer);
        }

        private static byte[] DrawCommonValues(IRandom random)
        {
            byte[] drawn = new byte[32];
            for (int i = 0; i < drawn.Length; i += 2)
            {
                drawn[i] = random.NextBool() ? (byte)1 : (byte)0;
                drawn[i + 1] = random.NextByte();
            }

            Assert.Greater(new HashSet<byte>(drawn).Count, 2, "common draws are not varied");
            return drawn;
        }

        private static uint[] Draw(IRandom random)
        {
            uint[] drawn = new uint[32];
            for (int i = 0; i < drawn.Length; ++i)
            {
                drawn[i] = random.NextUint();
            }

            return drawn;
        }
    }
}
