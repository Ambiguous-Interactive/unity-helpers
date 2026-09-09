// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Buffers.Binary;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IllusionFlowTests : RandomTestBase
    {
        [Test]
        public void InternalStateEncodesExtraSeedInPayload()
        {
            const uint extraSeed = 0xBEEF1234u;
            IllusionFlow random = new(DeterministicGuid, extraSeed);

            RandomState state = random.InternalState;
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
        public void SerializationRoundtripPreservesSequence()
        {
            IllusionFlow original = new(DeterministicGuid, DeterministicSeed32);
            for (int i = 0; i < 256; ++i)
            {
                _ = original.NextUint();
            }

            RandomState snapshot = original.InternalState;
            IllusionFlow restored = new(snapshot);

            for (int i = 0; i < 512; ++i)
            {
                Assert.AreEqual(original.NextUint(), restored.NextUint(), $"Mismatch at {i}");
            }
        }

        [Test]
        public void LegacyGaussianPackedStateRestoresSequence()
        {
            const uint extraSeed = 0xCAFED00Du;
            IllusionFlow original = new(DeterministicGuid, extraSeed);
            for (int i = 0; i < 128; ++i)
            {
                _ = original.NextUint();
            }

            RandomState current = original.InternalState;
            Span<byte> payloadSpan = stackalloc byte[sizeof(uint)];
            for (int i = 0; i < sizeof(uint); ++i)
            {
                payloadSpan[i] = current.PayloadBytes[i];
            }
            uint encodedE = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan);
            double packed = BitConverter.Int64BitsToDouble(encodedE);
            RandomState legacy = new(
                current.State1,
                current.State2,
                gaussian: packed,
                payload: null,
                bitBuffer: current.BitBuffer,
                bitCount: current.BitCount,
                byteBuffer: current.ByteBuffer,
                byteCount: current.ByteCount
            );

            IllusionFlow restored = new(legacy);
            for (int i = 0; i < 256; ++i)
            {
                Assert.AreEqual(original.NextUint(), restored.NextUint(), $"Mismatch at {i}");
            }
        }

        protected override IRandom NewRandom()
        {
            return new IllusionFlow(DeterministicGuid, DeterministicSeed32);
        }
    }
}
