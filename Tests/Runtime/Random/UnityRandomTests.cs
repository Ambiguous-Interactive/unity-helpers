// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

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

            // The ordinary case rather than an exotic one: something else in the project draws from
            // UnityEngine.Random between the save and the load. A seed-only snapshot resumed from
            // here, silently, with a different sequence.
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
            // The parameterless constructor never calls InitState, so before the position travelled
            // in the snapshot there was nothing at all to restore from.
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
        public void ASnapshotWithoutAnEnginePositionLeavesTheEngineAlone()
        {
            // What a save file written by 3.5.1 looks like: a seed, and no payload. Restoring it
            // must not throw and must not move a stream it knows nothing about.
            UnityEngine.Random.InitState(99);
            uint expected = new UnityRandom().NextUint();

            UnityEngine.Random.InitState(99);
            UnityRandom legacy = new(new RandomState(7UL, gaussian: 0.0));

            Assert.AreEqual(expected, legacy.NextUint());
        }
    }
}
