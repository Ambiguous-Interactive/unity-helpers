// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class RandomSnapshotOwnershipTests
    {
        private static IEnumerable<TestCaseData> ReplayCases()
        {
            for (int shape = 0; shape < 3; ++shape)
            {
                foreach (uint seed in new[] { 0U, 123U, uint.MaxValue })
                {
                    for (int cache = 0; cache < 4; ++cache)
                    {
                        yield return new TestCaseData(shape, seed, cache).SetName(
                            $"Snapshot.Replay.Shape{shape}.Seed{seed}.Cache{cache}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> PayloadCases()
        {
            for (int shape = 0; shape < 3; ++shape)
            {
                int length = PayloadLength(shape);
                foreach (
                    int supplied in new[] { -1, 0, length - 1, length, length + 1, length + 1024 }
                )
                {
                    yield return new TestCaseData(shape, supplied).SetName(
                        $"Snapshot.Payload.Shape{shape}.Length{supplied}"
                    );
                }
            }
        }

        private static int PayloadLength(int shape)
        {
            return shape == 0 ? 8
                : shape == 1 ? 80
                : 4096;
        }

        private static AbstractRandom Create(int shape, RandomState state)
        {
            switch (shape)
            {
                case 0:
                    return new FlurryBurstRandom(state);
                case 1:
                    return new PhotonSpinRandom(state);
                default:
                    return new StormDropRandom(state);
            }
        }

        [TestCaseSource(nameof(ReplayCases))]
        public void SnapshotPreservesCachedStateReplayAndIndependentBacking(
            int shape,
            uint seed,
            int cache
        )
        {
            AbstractRandom random = Create(shape, new RandomState(seed + 1UL, 0x80000000UL));
            if (cache != 0)
            {
                random.NextBool();
                random.NextByte();
            }
            for (int index = 0; index < cache; ++index)
            {
                random.NextGaussian();
            }
            RandomState snapshot = random.InternalState;
            RandomState second = random.InternalState;
            byte[] original = new byte[snapshot.Payload.Count];
            for (int index = 0; index < original.Length; ++index)
            {
                original[index] = snapshot.Payload[index];
            }
            Assert.AreEqual(snapshot, second);
            Assert.AreNotSame(snapshot.Payload, second.Payload);
            Assert.AreEqual(PayloadLength(shape), snapshot.Payload.Count);
            AbstractRandom restored = Create(shape, snapshot);
            Assert.AreEqual(snapshot, restored.InternalState);
            for (int index = 0; index < 128; ++index)
            {
                Assert.AreEqual(random.NextUlong(), restored.NextUlong());
                Assert.AreEqual(random.NextBool(), restored.NextBool());
                Assert.AreEqual(random.NextByte(), restored.NextByte());
                Assert.AreEqual(random.NextGaussian(), restored.NextGaussian());
            }
            CollectionAssert.AreEqual(original, snapshot.Payload);
            ((byte[])second.Payload)[0] ^= byte.MaxValue;
            CollectionAssert.AreEqual(original, snapshot.Payload);
        }

        [TestCaseSource(nameof(PayloadCases))]
        public void SnapshotPreservesLogicalPayloadAndInputIndependence(int shape, int supplied)
        {
            int expectedLength = PayloadLength(shape);
            byte[] payload = supplied < 0 ? null : new byte[supplied];
            if (payload != null)
            {
                for (int index = 0; index < payload.Length; ++index)
                {
                    payload[index] = unchecked((byte)(index * 37 + 11));
                }
            }
            RandomState input = new RandomState(
                0x000000070000000BUL,
                0x0000001F80000000UL,
                payload: payload
            );
            AbstractRandom random = Create(shape, input);
            if (payload != null)
            {
                Array.Clear(payload, 0, payload.Length);
            }
            RandomState snapshot = random.InternalState;
            Assert.AreEqual(expectedLength, snapshot.Payload.Count);
            for (int index = 0; index < expectedLength; ++index)
            {
                byte expected =
                    expectedLength <= supplied ? unchecked((byte)(index * 37 + 11)) : (byte)0;
                Assert.AreEqual(
                    expected,
                    snapshot.Payload[index],
                    $"Payload byte {index} differs."
                );
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SnapshotDoesNotAcquireADisposalLease(int shape)
        {
            AbstractRandom random = Create(shape, new RandomState(123, 0x80000000UL));
            RandomState warm = random.InternalState;
            DisposalLease probe = DisposalLeases.Acquire();
            try
            {
                int slot = probe._slot;
                bool released = probe.TryClaim();
                long before = DisposalLeases.CurrentGeneration(slot);
                RandomState actual = random.InternalState;
                long after = DisposalLeases.CurrentGeneration(slot);
                Assert.IsTrue(released);
                Assert.AreEqual(warm, actual);
                Assert.AreEqual(before, after, "A snapshot acquired and claimed a disposal lease.");
            }
            finally
            {
                probe.TryClaim();
            }
        }

        [Test]
        public void SnapshotLeaseObservationDetectsBalancedLeaseWork()
        {
            DisposalLease probe = DisposalLeases.Acquire();
            DisposalLease balanced = default;
            try
            {
                int slot = probe._slot;
                bool released = probe.TryClaim();
                long before = DisposalLeases.CurrentGeneration(slot);
                balanced = DisposalLeases.Acquire();
                int reusedSlot = balanced._slot;
                bool balancedReleased = balanced.TryClaim();
                long after = DisposalLeases.CurrentGeneration(slot);
                Assert.IsTrue(released);
                Assert.IsTrue(balancedReleased);
                Assert.AreEqual(slot, reusedSlot);
                Assert.AreEqual(before + 2, after);
            }
            finally
            {
                balanced.TryClaim();
                probe.TryClaim();
            }
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(16, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(16, true)]
        public void LegacySnapshotFactoryClonesCallerPayload(int length, bool useList)
        {
            byte[] payload = new byte[length];
            IReadOnlyList<byte> source = useList
                ? (IReadOnlyList<byte>)new List<byte>(payload)
                : payload;
            SnapshotFactoryRandom random = new SnapshotFactoryRandom();
            random.NextBool();
            random.NextByte();
            RandomState before = random.InternalState;
            RandomState state = random.CreateWithPayload(source);
            if (length != 0)
            {
                if (useList)
                {
                    ((List<byte>)source)[0] = 123;
                }
                else
                {
                    payload[0] = 123;
                }
            }
            Assert.AreNotSame(payload, state.Payload);
            Assert.AreEqual(length, state.Payload.Count);
            if (length != 0)
            {
                Assert.AreEqual(0, state.Payload[0]);
            }
            Assert.AreEqual(before.BitBuffer, state.BitBuffer);
            Assert.AreEqual(before.BitCount, state.BitCount);
            Assert.AreEqual(before.ByteBuffer, state.ByteBuffer);
            Assert.AreEqual(before.ByteCount, state.ByteCount);
        }

        [Test]
        public void LegacySnapshotFactoryPreservesNullPayload()
        {
            SnapshotFactoryRandom random = new SnapshotFactoryRandom();
            RandomState state = random.CreateWithPayload(null);
            Assert.IsTrue(state.Payload == null);
        }

        [Test]
        public void SpanSnapshotFactoryClonesOnlyLogicalSliceAndPreservesCommonState(
            [Values(0, 1, 8, 80, 4096, 10240)] int length,
            [Values(0, 1, 16)] int offset
        )
        {
            byte[] payload = new byte[offset + length + 7];
            for (int index = 0; index < payload.Length; ++index)
            {
                payload[index] = unchecked((byte)(index * 37 + 11));
            }
            AbstractRandom random = Create(0, new RandomState(123, 456));
            random.NextBool();
            random.NextByte();
            random.NextGaussian();
            RandomState before = random.InternalState;
            RandomState state = random.BuildStateFromSpan(
                ulong.MaxValue,
                23,
                payload.AsSpan(offset, length)
            );
            Array.Clear(payload, 0, payload.Length);
            Assert.AreNotSame(payload, state.Payload);
            Assert.AreEqual(length, state.Payload.Count);
            Assert.AreEqual(ulong.MaxValue, state.State1);
            Assert.AreEqual(23, state.State2);
            Assert.AreEqual(before.Gaussian, state.Gaussian);
            Assert.AreEqual(before.BitBuffer, state.BitBuffer);
            Assert.AreEqual(before.BitCount, state.BitCount);
            Assert.AreEqual(before.ByteBuffer, state.ByteBuffer);
            Assert.AreEqual(before.ByteCount, state.ByteCount);
            for (int index = 0; index < length; ++index)
            {
                Assert.AreEqual(
                    unchecked((byte)((offset + index) * 37 + 11)),
                    state.Payload[index]
                );
            }
        }
    }
}
