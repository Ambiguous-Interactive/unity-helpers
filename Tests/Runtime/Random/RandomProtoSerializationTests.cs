// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
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
            // Capture initial state
            RandomState initialState = original.InternalState;

            // Generate some numbers to change state
            for (int i = 0; i < NumGenerations; ++i)
            {
                original.NextUint();
            }

            RandomState stateAfterGeneration = original.InternalState;
            // Generating values should advance the RNG state
            Assert.AreNotEqual(
                initialState,
                stateAfterGeneration,
                "State should change after generation"
            );

            // Serialize and deserialize
            T deserialized = SerializeDeserialize(original);

            // Verify internal states match
            Assert.AreEqual(
                original.InternalState,
                deserialized.InternalState,
                "Internal states should match after deserialization"
            );

            // Verify subsequent random number generation produces identical results
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

            // Verify states still match after generation
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
            // Test with different initial states
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
            // Generate a Gaussian to populate the cached value
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
            // XorShiftRandom should handle zero state by using default value
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

            // Generate some noise (doesn't advance RNG)
            _ = random.NextNoise(10, 20);

            // Generate some actual random numbers
            for (int i = 0; i < 50; ++i)
            {
                random.NextUint();
            }

            SquirrelRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            // Verify noise generation still works the same
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
            // Create instances of all implementations
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
                // Generate some numbers
                for (int i = 0; i < 50; ++i)
                {
                    random.NextUint();
                }

                // Serialize
                byte[] serialized = Serializer.ProtoSerialize(random);
                Assert.IsTrue(serialized != null, $"{random.GetType().Name} serialization failed");
                Assert.Greater(
                    serialized.Length,
                    0,
                    $"{random.GetType().Name} produced empty serialization"
                );

                // Deserialize
                IRandom deserialized = Serializer.ProtoDeserialize<IRandom>(serialized);
                Assert.IsTrue(
                    deserialized != null,
                    $"{random.GetType().Name} deserialization failed"
                );

                // Verify state
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
            // Index 0 is the one state the wire never carries: proto omits a member equal to its
            // type's default. A contract whose parameterless constructor invents state then keeps
            // the invented value, which for this generator is a different stream on reload -- one
            // run in 256, which is why it surfaced as a flake rather than a failure.
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
            };

            foreach (Type randomType in randomTypes)
            {
                // Create instance
                IRandom random = (IRandom)Activator.CreateInstance(randomType);

                // Serialize and deserialize
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

            // Generate many numbers
            for (int i = 0; i < 10000; ++i)
            {
                random.NextUint();
            }

            DotNetRandom deserialized = SerializeDeserialize(random);

            Assert.AreEqual(random.InternalState, deserialized.InternalState);

            // Verify continued generation
            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(random.NextUint(), deserialized.NextUint());
            }
        }

        [Test]
        public void PcgRandomCopyAndSerializeProduceSameResults()
        {
            PcgRandom original = new(Guid.Parse("12345678-1234-1234-1234-123456789012"));

            // Generate some numbers
            for (int i = 0; i < 100; ++i)
            {
                original.NextUint();
            }

            // Create copy and serialized version
            PcgRandom copied = (PcgRandom)original.Copy();
            PcgRandom serialized = SerializeDeserialize(original);

            // All three should produce same sequence
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

            // First roundtrip
            PcgRandom deserialized1 = SerializeDeserialize(random);
            Assert.AreEqual(random.InternalState, deserialized1.InternalState);

            // Generate more numbers
            for (int i = 0; i < 50; ++i)
            {
                random.NextUint();
                deserialized1.NextUint();
            }

            // Second roundtrip
            PcgRandom deserialized2 = SerializeDeserialize(random);
            Assert.AreEqual(random.InternalState, deserialized2.InternalState);

            // Third roundtrip
            PcgRandom deserialized3 = SerializeDeserialize(deserialized2);
            Assert.AreEqual(deserialized2.InternalState, deserialized3.InternalState);
        }

        /// <summary>
        /// Every generator, built the ordinary way, so one case cannot quietly drop out.
        /// </summary>
        private static IEnumerable<TestCaseData> EveryGenerator()
        {
            Guid seed = Guid.Parse("12345678-1234-1234-1234-123456789012");
            yield return Named("DotNetRandom", new DotNetRandom(seed));
            yield return Named("PcgRandom", new PcgRandom(seed));
            yield return Named("XorShiftRandom", new XorShiftRandom(12345));
            yield return Named("WyRandom", new WyRandom(seed));
            yield return Named("XoroShiroRandom", new XoroShiroRandom(seed));
            yield return Named("SystemRandom", new SystemRandom(12345));
            yield return Named(
                "LinearCongruentialGenerator",
                new LinearCongruentialGenerator(12345)
            );
            yield return Named("SquirrelRandom", new SquirrelRandom(12345));
            yield return Named("RomuDuo", new RomuDuo(seed));
            yield return Named("SplitMix64", new SplitMix64(seed));
            yield return Named("IllusionFlow", new IllusionFlow(seed));
            yield return Named("FlurryBurstRandom", new FlurryBurstRandom(seed));
            yield return Named("PhotonSpinRandom", new PhotonSpinRandom(seed));
            yield return Named("StormDropRandom", new StormDropRandom(12345u));
            yield return Named("BlastCircuitRandom", new BlastCircuitRandom(seed));
            yield return Named("WaveSplatRandom", new WaveSplatRandom(0xC0FFEEUL));
            yield return Named("WDoomRandom", new WDoomRandom(seedIndex: 7));
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
            yield return Named("BlastCircuitRandom", new BlastCircuitRandom(0UL, 0UL, 0UL, 0UL));
            yield return Named("FlurryBurstRandom", new FlurryBurstRandom(default(RandomState)));
            yield return Named("LinearCongruentialGenerator", new LinearCongruentialGenerator(0));
            yield return Named("SplitMix64", new SplitMix64(0UL));
            yield return Named("SquirrelRandom", new SquirrelRandom(0));
            yield return Named("WaveSplatRandom", new WaveSplatRandom(0UL));
            yield return Named("WDoomRandom", new WDoomRandom(seedIndex: 0));
            yield return Named("WyRandom", new WyRandom(0UL));
        }

        private static TestCaseData Named(string name, IRandom random)
        {
            return new TestCaseData(random).SetName(name);
        }

        private static IRandom ProtobufNetRoundTrip(IRandom random)
        {
            using MemoryStream written = new();
            ProtoBuf.Serializer.Serialize(written, (AbstractRandom)random);
            using MemoryStream read = new(written.ToArray());
            return ProtoBuf.Serializer.Deserialize<AbstractRandom>(read);
        }

        [TestCaseSource(nameof(EveryGenerator))]
        public void ARestoredGeneratorCanStillProduceAGuid(IRandom random)
        {
            // The guid buffer is not on the wire, so a reader that allocates the instance without
            // running a constructor -- which is what SkipConstructor asks protobuf-net for -- left
            // it null, and the first NextGuid on a loaded save threw. Both readers are asked,
            // because only one of them allocates that way.
            foreach (IRandom restored in new[] { RoundTrip(random), ProtobufNetRoundTrip(random) })
            {
                Guid first = restored.NextGuid();
                Guid second = restored.NextGuid();
                Assert.AreNotEqual(Guid.Empty, first);
                Assert.AreNotEqual(first, second);
            }
        }

        [TestCaseSource(nameof(EveryAllDefaultGenerator))]
        public void AnAllDefaultStateRestoresTheStreamItSaved(IRandom random)
        {
            // protobuf omits a member equal to its type's default, so this generator's payload is
            // the include wrapper and nothing else. A constructor that seeds from Guid.NewGuid()
            // keeps the invented value, which is a different stream on every load of the same
            // bytes -- identical saves, a different game.
            byte[] payload = Serializer.ProtoSerialize<IRandom>(random);
            Assert.LessOrEqual(payload.Length, 3, "expected a payload naming no member");

            uint[] expected = Draw(random);
            CollectionAssert.AreEqual(
                expected,
                Draw(Serializer.ProtoDeserialize<IRandom>(payload))
            );
            CollectionAssert.AreEqual(
                expected,
                Draw(Serializer.ProtoDeserialize<IRandom>(payload))
            );
            CollectionAssert.AreEqual(expected, Draw(ProtobufNetRoundTrip(random)));
        }

        [Test]
        public void APcgIncrementRestoredAsZeroStillAdvances()
        {
            // PCG multiplies and then adds its increment, so a zero increment leaves the state
            // where it was and every draw is the same value. Every constructor stores an odd one,
            // so only a payload can deliver zero -- and it must not produce a dead generator.
            PcgRandom zeroed = new(0UL, 0UL);
            IRandom restored = Serializer.ProtoDeserialize<IRandom>(
                Serializer.ProtoSerialize<IRandom>(zeroed)
            );

            uint[] drawn = Draw(restored);
            CollectionAssert.AreEqual(Draw(zeroed), drawn);
            Assert.Greater(new HashSet<uint>(drawn).Count, 1, "a dead stream repeats one value");
        }

        private static IRandom RoundTrip(IRandom random)
        {
            return Serializer.ProtoDeserialize<IRandom>(Serializer.ProtoSerialize<IRandom>(random));
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
