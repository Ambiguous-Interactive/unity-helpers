// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /*
        UnityRandom is the package's IRandom adapter over the engine generator, so the engine
        generator is this fixture's subject rather than its tool. Every draw and every InitState
        below exists to move UnityEngine.Random out from under a snapshot and prove the adapter
        still resumes the stream; routing them through a seedable generator would delete what is
        being tested.
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
        public void APayloadThatIsNotAnEnginePositionLeavesTheEngineAlone()
        {
            // JsonUtility throws only on text that is not JSON at all. Well-formed JSON that is not
            // an engine state parses to a ZEROED state, and an all-zero xorshift state emits zeros
            // forever -- so a foreign payload must be refused rather than applied.
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
            // What a save file written by 3.5.1 looks like: a seed, and no payload. Restoring it
            // must not throw and must not move a stream it knows nothing about.
            UnityEngine.Random.InitState(99);
            uint expected = new UnityRandom().NextUint();

            UnityEngine.Random.InitState(99);
            UnityRandom legacy = new(new RandomState(7UL, gaussian: 0.0));

            Assert.AreEqual(expected, legacy.NextUint());
        }
    }
#pragma warning restore WUH005
}
