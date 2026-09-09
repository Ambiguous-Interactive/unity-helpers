// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DotNetRandomTests : RandomTestBase
    {
        [Test]
        public void SnapshotPayloadRestoresExactSequence()
        {
            DotNetRandom original = new(DeterministicGuid);
            for (int i = 0; i < 4_096; ++i)
            {
                _ = original.Next();
            }

            RandomState snapshot = original.InternalState;
            Assert.IsTrue(
                snapshot.PayloadBytes != null,
                "PayloadBytes should not be null for DotNetRandom snapshot"
            );
            Assert.GreaterOrEqual(snapshot.PayloadBytes.Count, 12);

            DotNetRandom restored = new(snapshot);
            for (int i = 0; i < 512; ++i)
            {
                Assert.AreEqual(original.Next(), restored.Next(), $"Mismatch at {i}");
            }
        }

        [Test]
        public void LegacyStateWithoutPayloadStillRestores()
        {
            DotNetRandom original = new(DeterministicGuid);
            for (int i = 0; i < 2_048; ++i)
            {
                _ = original.Next();
            }

            RandomState snapshot = original.InternalState;
            RandomState legacy = new(
                snapshot.State1,
                snapshot.State2,
                gaussian: snapshot.Gaussian,
                payload: null,
                bitBuffer: snapshot.BitBuffer,
                bitCount: snapshot.BitCount,
                byteBuffer: snapshot.ByteBuffer,
                byteCount: snapshot.ByteCount
            );

            DotNetRandom restored = new(legacy);
            for (int i = 0; i < 256; ++i)
            {
                Assert.AreEqual(original.Next(), restored.Next(), $"Mismatch at {i}");
            }
        }

        protected override IRandom NewRandom() => new DotNetRandom(DeterministicGuid);
    }
}
