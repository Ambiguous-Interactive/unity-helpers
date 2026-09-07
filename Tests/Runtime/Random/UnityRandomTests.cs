// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /*
        UnityRandom adapts engine state; engine draws are necessary to prove restoration rather than fixture
        randomness.
    */
#pragma warning disable WUH005
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityRandomTests : RandomTestBase
    {
        private const int DrawsBeforeSnapshot = 37;
        private const int ComparedDraws = 64;
        private static readonly RestorableGlobal<UnityEngine.Random.State> EngineState = new(
            () => UnityEngine.Random.state,
            state => UnityEngine.Random.state = state
        );

        protected override IRandom NewRandom() => new UnityRandom(DeterministicSeedInt);

        [TestCase(0, 32)]
        [TestCase(-1, 32)]
        [TestCase(int.MinValue, 32)]
        [TestCase(int.MaxValue, 32)]
        [TestCase(4242, 32)]
        [TestCase(0, 64)]
        [TestCase(-1, 64)]
        [TestCase(int.MinValue, 64)]
        [TestCase(int.MaxValue, 64)]
        [TestCase(4242, 64)]
        [Parallelizable(ParallelScope.None)]
        public void RawStreamPreservesEngineAdapterDrawOrder(int seed, int width)
        {
            using RestorableGlobal<UnityEngine.Random.State>.Scope scope = EngineState.Borrow(
                UnityEngine.Random.state
            );
            UnityEngine.Random.InitState(seed);
            ulong[] expected = new ulong[1024];
            for (int i = 0; i < expected.Length; ++i)
            {
                uint upper = unchecked((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
                expected[i] =
                    width == 32
                        ? upper
                        : ((ulong)upper << 32)
                            | unchecked((uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            }
            UnityEngine.Random.State expectedPosition = UnityEngine.Random.state;

            UnityRandom random = new(seed);
            foreach (ulong word in expected)
            {
                Assert.AreEqual(word, width == 32 ? random.NextUint() : random.NextUlong());
            }
            Assert.AreEqual(expectedPosition, UnityEngine.Random.state);
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [Parallelizable(ParallelScope.None)]
        public void SnapshotResumesMixedDraws(bool seeded, int gaussianDraws)
        {
            AssertSnapshotContinuation(seeded, gaussianDraws, state => state);
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
#if !WALLSTOP_PROTO
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
#endif
        [Parallelizable(ParallelScope.None)]
        public void ProtobufSnapshotResumesMixedDraws(bool seeded, int gaussianDraws)
        {
            AssertSnapshotContinuation(
                seeded,
                gaussianDraws,
                state => Serializer.ProtoDeserialize<RandomState>(Serializer.ProtoSerialize(state))
            );
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        [Parallelizable(ParallelScope.None)]
        public void JsonSnapshotResumesMixedDraws(bool seeded, int gaussianDraws)
        {
            AssertSnapshotContinuation(
                seeded,
                gaussianDraws,
                state => Serializer.JsonDeserialize<RandomState>(Serializer.JsonStringify(state))
            );
        }

        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [Parallelizable(ParallelScope.None)]
        public void CopyPreservesMixedDrawReservoirs(bool seeded, int gaussianDraws)
        {
            using RestorableGlobal<UnityEngine.Random.State>.Scope scope = EngineState.Borrow(
                UnityEngine.Random.state
            );
            UnityRandom random = CreatePartiallyConsumedRandom(seeded, gaussianDraws);
            UnityEngine.Random.State position = UnityEngine.Random.state;
            IRandom copy = random.Copy();
            object[] expected = ReadMixedSequence(random);

            // Both objects draw from the same engine; replay its position while keeping each object's reservoirs.
            UnityEngine.Random.state = position;
            CollectionAssert.AreEqual(expected, ReadMixedSequence(copy));
        }

        [TestCase(true)]
        [TestCase(false)]
        [Parallelizable(ParallelScope.None)]
        public void SnapshotPreservesALegitimateZeroGaussian(bool seeded)
        {
            using RestorableGlobal<UnityEngine.Random.State>.Scope scope = EngineState.Borrow(
                UnityEngine.Random.state
            );
            UnityRandom random = CreatePartiallyConsumedRandom(seeded, 0);
            RandomState snapshot = random.InternalState;
            RandomState withZeroGaussian = new(
                snapshot.State1,
                snapshot.State2,
                gaussian: 0,
                payload: snapshot.PayloadBytes
            );
            uint expectedNextUint = random.NextUint();

            UnityRandom restored = new(withZeroGaussian);
            Assert.AreEqual(0d, restored.NextGaussian());
            Assert.AreEqual(expectedNextUint, restored.NextUint());
            Assert.IsFalse(restored.InternalState.Gaussian.HasValue);
        }

        [TestCase(4242, true)]
        [TestCase(4242, false)]
        [TestCase(0, true)]
        [TestCase(0, false)]
        [TestCase(-1, true)]
        [TestCase(-1, false)]
        [TestCase(null, true)]
        [TestCase(null, false)]
        [Parallelizable(ParallelScope.None)]
        public void LegacySnapshotSeedMarkerIsNotAGaussian(int? seed, bool hasPayload)
        {
            using RestorableGlobal<UnityEngine.Random.State>.Scope scope = EngineState.Borrow(
                UnityEngine.Random.state
            );
            UnityEngine.Random.InitState(4242);
            UnityRandom random = new(seed);
            RandomState current = random.InternalState;
            RandomState legacy = new(
                current.State1,
                gaussian: seed.HasValue ? 0d : null,
                payload: hasPayload ? current.PayloadBytes : null
            );
            UnityEngine.Random.State position = UnityEngine.Random.state;
            object[] expected = ReadMixedSequence(new UnityRandom());
            UnityEngine.Random.state = position;

            UnityRandom restored = new(legacy);
            RandomState migrated = restored.InternalState;
            Assert.AreEqual(current.State1, migrated.State1);
            Assert.AreEqual(current.State2, migrated.State2);
            Assert.IsFalse(migrated.Gaussian.HasValue);
            CollectionAssert.AreEqual(expected, ReadMixedSequence(restored));
        }

        private static void AssertSnapshotContinuation(
            bool seeded,
            int gaussianDraws,
            Func<RandomState, RandomState> roundTrip
        )
        {
            using RestorableGlobal<UnityEngine.Random.State>.Scope scope = EngineState.Borrow(
                UnityEngine.Random.state
            );
            UnityRandom random = CreatePartiallyConsumedRandom(seeded, gaussianDraws);
            RandomState snapshot = random.InternalState;
            object[] expected = ReadMixedSequence(random);
            UnityEngine.Random.InitState(-1);

            UnityRandom restored = new(roundTrip(snapshot));
            Assert.AreEqual(snapshot, restored.InternalState);
            CollectionAssert.AreEqual(expected, ReadMixedSequence(restored));
        }

        private static UnityRandom CreatePartiallyConsumedRandom(bool seeded, int gaussianDraws)
        {
            UnityEngine.Random.InitState(4242);
            UnityRandom random = new(seeded ? 4242 : null);
            for (int i = 0; i < gaussianDraws; ++i)
            {
                random.NextGaussian();
            }

            random.NextBool();
            random.NextByte();
            return random;
        }

        private static object[] ReadMixedSequence(IRandom random)
        {
            object[] values = new object[6 * ComparedDraws];
            for (int index = 0; index < values.Length; index += 6)
            {
                values[index] = random.NextGaussian();
                values[index + 1] = random.NextBool();
                values[index + 2] = random.NextByte();
                values[index + 3] = random.NextUint();
                values[index + 4] = random.NextUlong();
                values[index + 5] = random.NextDouble();
            }
            return values;
        }

        [Test]
        public void ASnapshotResumesTheStreamAfterOtherCodeHasMovedTheEngine()
        {
            UnityRandom random = new(seed: 4242);
            for (int i = 0; i < DrawsBeforeSnapshot; ++i)
            {
                random.NextUint();
            }

            RandomState snapshot = random.InternalState;
            uint[] expected = new uint[ComparedDraws];
            for (int i = 0; i < expected.Length; ++i)
            {
                expected[i] = random.NextUint();
            }

            // Unrelated engine draws between save and load expose seed-only snapshots.
            for (int i = 0; i < 500; ++i)
            {
                _ = UnityEngine.Random.value;
            }

            UnityRandom restored = new(snapshot);
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.AreEqual(expected[i], restored.NextUint(), $"draw {i}");
            }
        }

        [Test]
        public void AnUnseededSnapshotResumesTheStreamToo()
        {
            // The parameterless constructor does not seed the engine, so only a position payload can restore it.
            UnityRandom random = new();
            for (int i = 0; i < DrawsBeforeSnapshot; ++i)
            {
                random.NextUint();
            }

            RandomState snapshot = random.InternalState;
            uint first = random.NextUint();

            _ = UnityEngine.Random.value;

            UnityRandom restored = new(snapshot);
            Assert.AreEqual(first, restored.NextUint());
        }

        [Test]
        public void APayloadThatIsNotAnEnginePositionLeavesTheEngineAlone()
        {
            // JsonUtility accepts foreign JSON as zeroed state; applying it would freeze the generator.
            string[] foreign =
            {
                "{\"foo\":1}",
                "{}",
                "{\"s0\":0,\"s1\":0,\"s2\":0,\"s3\":0}",
                "not json at all",
            };

            foreach (string payload in foreign)
            {
                UnityEngine.Random.InitState(1234);
                uint expected = new UnityRandom().NextUint();

                UnityEngine.Random.InitState(1234);
                UnityRandom restored = new(
                    new RandomState(0UL, payload: Encoding.UTF8.GetBytes(payload))
                );

                Assert.AreEqual(expected, restored.NextUint(), payload);
            }
        }

        [Test]
        public void ASnapshotWithoutAnEnginePositionLeavesTheEngineAlone()
        {
            /*
                What a save file written by 3.5.1 looks like: a seed, and no payload. Restoring it must not
                throw and must not move a stream it knows nothing about.
            */
            UnityEngine.Random.InitState(99);
            uint expected = new UnityRandom().NextUint();

            UnityEngine.Random.InitState(99);
            UnityRandom legacy = new(new RandomState(7UL, gaussian: 0.0));

            Assert.AreEqual(expected, legacy.NextUint());
        }
    }
#pragma warning restore WUH005
}
