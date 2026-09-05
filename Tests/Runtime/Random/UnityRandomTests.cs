// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

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

        protected override IRandom NewRandom() => new UnityRandom(DeterministicSeedInt);

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
