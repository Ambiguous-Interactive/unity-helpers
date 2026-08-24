// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AbstractRandomEdgeCaseTests
    {
        private static readonly Guid DeterministicGuid = Guid.Parse(
            "F2E9A25A-9D23-4C9E-AD4F-5FF9C0E8ABCD"
        );

        private enum TinyEnum
        {
            Alpha,
            Beta,
            Gamma,
            Delta,
        }

        [Test]
        public void NextUlongUsesBitMaskForPowerOfTwoBounds()
        {
            DeterministicRandom random = new();
            const ulong sample = 0x123456789ABCDEF0UL;
            random.EnqueueUlong(sample);

            ulong result = random.NextUlong(8);

            Assert.AreEqual(sample & 7UL, result);
            Assert.AreEqual(2, random.UintCalls);
        }

        [Test]
        public void NextUlongResamplesWhenProductFallsBelowThreshold()
        {
            DeterministicRandom random = new();
            random.EnqueueUlong(0UL); // Forces rejection because productLow == 0 < threshold
            const ulong acceptedSample = 0x1111_2222_3333_4444UL;
            random.EnqueueUlong(acceptedSample);

            ulong result = random.NextUlong(3);

            Assert.AreEqual(MultiplyHigh(acceptedSample, 3UL), result);
            Assert.AreEqual(4, random.UintCalls);
        }

        [Test]
        public void NextDoubleSupportsPositiveInfinityUpperBound()
        {
            PcgRandom random = new(DeterministicGuid);
            for (int i = 0; i < 1024; ++i)
            {
                double value = random.NextDouble(42.5, double.PositiveInfinity);
                Assert.IsFalse(double.IsNaN(value), "Encountered NaN at iteration {0}", i);
                Assert.IsFalse(
                    double.IsInfinity(value),
                    "Encountered Infinity at iteration {0}",
                    i
                );
                Assert.GreaterOrEqual(value, 42.5);
            }
        }

        [Test]
        public void NextDoubleSupportsNegativeInfinityLowerBound()
        {
            PcgRandom random = new(DeterministicGuid);
            for (int i = 0; i < 1024; ++i)
            {
                double value = random.NextDouble(double.NegativeInfinity, -3.25);
                Assert.IsFalse(double.IsNaN(value), "Encountered NaN at iteration {0}", i);
                Assert.IsFalse(
                    double.IsInfinity(value),
                    "Encountered Infinity at iteration {0}",
                    i
                );
                Assert.Less(value, -3.25);
            }
        }

        [Test]
        public void NextDoubleSupportsFullyUnboundedRange()
        {
            PcgRandom random = new(DeterministicGuid);
            bool sawPositive = false;
            bool sawNegative = false;
            for (int i = 0; i < 4096; ++i)
            {
                double value = random.NextDouble(double.NegativeInfinity, double.PositiveInfinity);
                Assert.IsFalse(double.IsNaN(value), "Encountered NaN at iteration {0}", i);
                Assert.IsFalse(
                    double.IsInfinity(value),
                    "Encountered Infinity at iteration {0}",
                    i
                );
                if (0 <= value)
                {
                    sawPositive = true;
                }
                else
                {
                    sawNegative = true;
                }
            }

            Assert.IsTrue(sawPositive, "Expected to sample non-negative values.");
            Assert.IsTrue(sawNegative, "Expected to sample negative values.");
        }

        [Test]
        public void NextEnumExceptThrowsWhenAllValuesExcluded()
        {
            PcgRandom random = new(DeterministicGuid);
            Assert.Throws<InvalidOperationException>(() =>
                random.NextEnumExcept(TinyEnum.Alpha, TinyEnum.Beta, TinyEnum.Gamma, TinyEnum.Delta)
            );
        }

        [Test]
        public void SetUuidV4BitsEnforcesVersionAndVariant()
        {
            PcgRandom random = new(DeterministicGuid);
            byte[] buffer = new byte[16];
            random.NextBytes(buffer);

            AbstractRandom.SetUuidV4Bits(buffer);

            int version = (buffer[7] >> 4) & 0x0F;
            int variant = (buffer[8] >> 6) & 0x03;
            Assert.AreEqual(4, version);
            Assert.AreEqual(0b10, variant);
        }

        [Test]
        public void IllusionFlowInternalStateStoresExtraSeedInPayload()
        {
            const uint extraSeed = 0xDEADBEEFu;
            IllusionFlow flow = new(DeterministicGuid, extraSeed);

            RandomState state = flow.InternalState;
            Assert.IsTrue(
                state.PayloadBytes != null,
                "PayloadBytes should not be null for IllusionFlow state"
            );
            Assert.GreaterOrEqual(state.PayloadBytes.Count, sizeof(uint));

            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            for (int i = 0; i < sizeof(uint); ++i)
            {
                buffer[i] = state.PayloadBytes[i];
            }

            uint recovered = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            Assert.AreEqual(extraSeed, recovered);
        }

        [Test]
        public void IllusionFlowRestoresFromLegacyGaussianState()
        {
            const uint extraSeed = 0xCAFEBABEu;
            IllusionFlow original = new(DeterministicGuid, extraSeed);
            RandomState modernState = original.InternalState;

            RandomState legacyState = new(
                modernState.State1,
                modernState.State2,
                gaussian: BitConverter.Int64BitsToDouble(extraSeed),
                payload: null,
                bitBuffer: modernState.BitBuffer,
                bitCount: modernState.BitCount,
                byteBuffer: modernState.ByteBuffer,
                byteCount: modernState.ByteCount
            );

            IllusionFlow restoredLegacy = new(legacyState);
            IllusionFlow restoredModern = new(modernState);

            for (int i = 0; i < 64; ++i)
            {
                uint expected = restoredModern.NextUint();
                uint actual = restoredLegacy.NextUint();
                Assert.AreEqual(expected, actual, $"Mismatch at iteration {i}");
            }
        }

        [Test]
        public void DotNetRandomRestoresFromSnapshotState()
        {
            DotNetRandom original = new(DeterministicGuid);
            for (int i = 0; i < 5_000; ++i)
            {
                _ = original.Next();
            }

            RandomState snapshot = original.InternalState;
            DotNetRandom restored = new(snapshot);

            for (int i = 0; i < 256; ++i)
            {
                Assert.AreEqual(original.Next(), restored.Next(), $"Mismatch at {i}");
            }

            if (snapshot.PayloadBytes != null && 0 < snapshot.PayloadBytes.Count)
            {
                Assert.GreaterOrEqual(snapshot.PayloadBytes.Count, 12);
            }
        }

        [Test]
        public void DotNetRandomRemainsBackwardCompatibleWithoutPayload()
        {
            DotNetRandom original = new(DeterministicGuid);
            for (int i = 0; i < 2_000; ++i)
            {
                _ = original.Next();
            }

            RandomState snapshot = original.InternalState;
            RandomState legacyState = WithoutPayload(snapshot);
            DotNetRandom restored = new(legacyState);

            for (int i = 0; i < 256; ++i)
            {
                Assert.AreEqual(original.Next(), restored.Next(), $"Mismatch at {i}");
            }
        }

        [Test]
        public void DotNetRandomRefusesAnUnboundedLegacyReplayInsteadOfResettingIt()
        {
            RandomState excessiveReplay = new RandomState(state1: 1234UL, state2: 1_000_001UL);

            Assert.Catch<Exception>(() => new DotNetRandom(excessiveReplay));
        }

        private static ulong MultiplyHigh(ulong x, ulong y)
        {
            unchecked
            {
                ulong x0 = (uint)x;
                ulong x1 = x >> 32;
                ulong y0 = (uint)y;
                ulong y1 = y >> 32;

                ulong p11 = x1 * y1;
                ulong p01 = x0 * y1;
                ulong p10 = x1 * y0;
                ulong p00 = x0 * y0;

                ulong middle = p10 + (p00 >> 32) + (uint)p01;
                ulong hi = p11 + (middle >> 32) + (p01 >> 32);
                return hi;
            }
        }

        private static RandomState WithoutPayload(RandomState state)
        {
            return new RandomState(
                state.State1,
                state.State2,
                gaussian: state.Gaussian,
                payload: null,
                bitBuffer: state.BitBuffer,
                bitCount: state.BitCount,
                byteBuffer: state.ByteBuffer,
                byteCount: state.ByteCount
            );
        }

        private sealed class DeterministicRandom : AbstractRandom
        {
            private readonly Queue<uint> _values = new();

            public int UintCalls { get; private set; }

            public override RandomState InternalState => new(0UL);

            public void EnqueueUlong(ulong value)
            {
                uint upper = (uint)(value >> 32);
                uint lower = (uint)value;
                _values.Enqueue(upper);
                _values.Enqueue(lower);
            }

            public override uint NextUint()
            {
                if (_values.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No values enqueued for DeterministicRandom."
                    );
                }

                ++UintCalls;
                return _values.Dequeue();
            }

            public override IRandom Copy()
            {
                throw new NotSupportedException("DeterministicRandom does not support cloning.");
            }
        }
    }
}
