// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class Sfc64RandomTests : RandomTestBase
    {
        // Produced by an independent transcription of the sfc64 reference implementation, including
        // the canonical seeding: counter starts at 1 and twelve warm-up draws run before the first
        // observed draw.
        private const ulong Seed0 = 0x0123456789ABCDEFUL;
        private const ulong Seed1 = 0xF0E1D2C3B4A59687UL;
        private const ulong Seed2 = 0x243F6A8885A308D3UL;

        private static readonly ulong[] ReferenceStream =
        {
            0x0E49ACA4B3A80477UL,
            0x1B7DA9B76C2C00B1UL,
            0x806E56950F148948UL,
            0x25530ABBAB8BF7C4UL,
            0x3756924A87B25658UL,
            0x1DDD9292B852C619UL,
            0x3A440B61DBC06D75UL,
            0x0CF2C99C523B1956UL,
            0x7854A96C950B35F1UL,
            0x6C584EB8F0915868UL,
            0x71B62D054E611F56UL,
            0x5B36BD06958F78F2UL,
        };

        protected override IRandom NewRandom() => new Sfc64Random(Seed0, Seed1, Seed2);

        [Test]
        public void NextUlongMatchesReferenceImplementation()
        {
            Sfc64Random random = new(Seed0, Seed1, Seed2);

            for (int i = 0; i < ReferenceStream.Length; ++i)
            {
                Assert.AreEqual(
                    ReferenceStream[i],
                    random.NextUlong(),
                    $"Draw {i} diverged from the published sfc64 stream."
                );
            }
        }

        [Test]
        public void EveryDrawCostsExactlyOneStateAdvance()
        {
            // The inherited AbstractRandom.NextUlong composes two NextUint calls, so a 64-bit draw
            // would cost two advances. This generator overrides it. Comparing a NextUint against
            // NextUlong would only restate how NextUint is written, so both widths are measured
            // against the reference stream instead: three 32-bit draws then a 64-bit one must land
            // on the fourth word, which holds only if each draw advanced the state exactly once.
            Sfc64Random random = new(Seed0, Seed1, Seed2);

            for (int i = 0; i < 3; ++i)
            {
                Assert.AreEqual(
                    (uint)(ReferenceStream[i] >> 32),
                    random.NextUint(),
                    $"Draw {i} diverged from the reference stream."
                );
            }

            Assert.AreEqual(
                ReferenceStream[3],
                random.NextUlong(),
                "The fourth draw skipped or repeated an advance."
            );
        }

        [Test]
        public void AllZeroSeedEscapesThroughItsCounter()
        {
            // The all-zero corner is not a fixed point of sfc64 the way it is for a bare
            // xorshift-style recurrence: the counter contributes to the very first output and keeps
            // moving the state. It must still be producing non-zero words immediately.
            Sfc64Random random = new(0UL, 0UL, 0UL);

            bool sawNonZero = false;
            for (int i = 0; i < 16 && !sawNonZero; ++i)
            {
                sawNonZero = random.NextUlong() != 0UL;
            }

            Assert.IsTrue(sawNonZero, "The all-zero seed must escape through its draw counter.");
        }

        [Test]
        public void InternalStateRoundTripsThroughItsPayload()
        {
            Sfc64Random source = new(Seed0, Seed1, Seed2);
            for (int i = 0; i < 7; ++i)
            {
                source.NextUlong();
            }

            RandomState state = source.InternalState;
            Sfc64Random restored = new(state);

            Assert.AreEqual(
                source,
                restored,
                "The counter and the third state word carried in the payload must survive the round trip."
            );
            for (int i = 0; i < 32; ++i)
            {
                Assert.AreEqual(source.NextUlong(), restored.NextUlong(), $"Draw {i} diverged.");
            }
        }

        [Test]
        public void RestoreWithoutAPayloadWarmUpInsteadOfStreamingColdState()
        {
            // A state snapshot stripped of its payload cannot restore _c or the counter, so the
            // constructor derives them and re-runs the canonical warm-up. A cold, correlated
            // half-state is exactly the corner the warm-up exists for.
            Sfc64Random source = new(Seed0, Seed1, Seed2);
            for (int i = 0; i < 37; ++i)
            {
                source.NextUlong();
            }

            RandomState stripped = source.InternalState;
            Sfc64Random restored = new(
                new RandomState(
                    stripped.State1,
                    stripped.State2,
                    stripped.Gaussian,
                    payload: null,
                    bitBuffer: stripped.BitBuffer,
                    bitCount: stripped.BitCount,
                    byteBuffer: stripped.ByteBuffer,
                    byteCount: stripped.ByteCount
                )
            );

            byte[] strippedPayload = ToBytes(stripped.PayloadBytes);
            byte[] recoveredPayload = ToBytes(restored.InternalState.PayloadBytes);
            CollectionAssert.AreNotEqual(
                strippedPayload,
                recoveredPayload,
                "The recovery path must replace the lost counter, not stream on from the cold half-state."
            );

            Sfc64Random second = new(restored.InternalState);
            for (int i = 0; i < 32; ++i)
            {
                Assert.AreEqual(
                    restored.NextUlong(),
                    second.NextUlong(),
                    $"Draw {i} diverged from the recovered state."
                );
            }
        }

        private static byte[] ToBytes(IReadOnlyList<byte> payload)
        {
            byte[] bytes = new byte[payload.Count];
            for (int i = 0; i < bytes.Length; ++i)
            {
                bytes[i] = payload[i];
            }

            return bytes;
        }
    }
}
