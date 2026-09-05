// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class Xoshiro128StarStarTests : RandomTestBase
    {
        /*
            Produced by an independent transcription of https://prng.di.unimi.it/xoshiro128starstar.c driven
            from the same four seed words. A generator whose recurrence drifts from the algorithm it cites is
            the exact defect this package shipped in XoroShiroRandom, so the citation is pinned by output rather
            than by comment.
        */
        private const uint Seed0 = 0x12345678U;
        private const uint Seed1 = 0x9ABCDEF0U;
        private const uint Seed2 = 0x0FEDCBA9U;
        private const uint Seed3 = 0x87654321U;

        private static readonly uint[] ReferenceStream =
        {
            0x99981812U,
            0x66666962U,
            0xD3905550U,
            0x309CBE4FU,
            0x06991CB1U,
            0x4EF39F2DU,
            0x1F6BC67BU,
            0x8D5D51C5U,
            0xA6091973U,
            0xF2E9A317U,
            0x270CB834U,
            0x3F5A171FU,
        };

        protected override IRandom NewRandom() =>
            new Xoshiro128StarStar(Seed0, Seed1, Seed2, Seed3);

        [Test]
        public void NextUintMatchesReferenceImplementation()
        {
            Xoshiro128StarStar random = new(Seed0, Seed1, Seed2, Seed3);

            for (int i = 0; i < ReferenceStream.Length; ++i)
            {
                Assert.AreEqual(
                    ReferenceStream[i],
                    random.NextUint(),
                    $"Draw {i} diverged from the published xoshiro128** stream."
                );
            }
        }

        [Test]
        public void AllZeroSeedIsRepairedRatherThanLockedAtZero()
        {
            Xoshiro128StarStar random = new(0U, 0U, 0U, 0U);

            bool sawNonZero = false;
            for (int i = 0; i < 16 && !sawNonZero; ++i)
            {
                sawNonZero = random.NextUint() != 0U;
            }

            Assert.IsTrue(
                sawNonZero,
                "An all-zero state is a fixed point of the xoshiro recurrence and must be repaired."
            );
        }

        [Test]
        public void InternalStateRoundTripsWithoutAPayload()
        {
            Xoshiro128StarStar source = new(Seed0, Seed1, Seed2, Seed3);
            for (int i = 0; i < 7; ++i)
            {
                source.NextUint();
            }

            RandomState state = source.InternalState;
            Xoshiro128StarStar restored = new(state);

            Assert.AreEqual(source, restored);
            for (int i = 0; i < 32; ++i)
            {
                Assert.AreEqual(source.NextUint(), restored.NextUint(), $"Draw {i} diverged.");
            }
        }
    }
}
