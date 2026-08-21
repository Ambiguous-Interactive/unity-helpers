// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class Xoshiro256StarStarTests : RandomTestBase
    {
        // Produced by an independent transcription of https://prng.di.unimi.it/xoshiro256starstar.c
        // driven from the same four seed words.
        private const ulong Seed0 = 0x0123456789ABCDEFUL;
        private const ulong Seed1 = 0xF0E1D2C3B4A59687UL;
        private const ulong Seed2 = 0x243F6A8885A308D3UL;
        private const ulong Seed3 = 0x13198A2E03707344UL;

        private static readonly ulong[] ReferenceStream =
        {
            0xD90633608DBAE0AAUL,
            0xD2C06E3B3BDBF046UL,
            0x2B173BC049420A23UL,
            0x1A776E56E675CFEAUL,
            0x11DAEEAFB571E0C4UL,
            0x3D575B04D244755EUL,
            0xA1B27FA1A6123592UL,
            0x5C48647BE83D9510UL,
            0x1A89957151DDF349UL,
            0xB34C0592DBDB83BDUL,
            0xA0E0A77B034F7A13UL,
            0x2F5F56CE048B769BUL,
        };

        protected override IRandom NewRandom() =>
            new Xoshiro256StarStar(Seed0, Seed1, Seed2, Seed3);

        [Test]
        public void NextUlongMatchesReferenceImplementation()
        {
            Xoshiro256StarStar random = new(Seed0, Seed1, Seed2, Seed3);

            for (int i = 0; i < ReferenceStream.Length; ++i)
            {
                Assert.AreEqual(
                    ReferenceStream[i],
                    random.NextUlong(),
                    $"Draw {i} diverged from the published xoshiro256** stream."
                );
            }
        }

        [Test]
        public void EveryDrawCostsExactlyOneStateAdvance()
        {
            // The inherited AbstractRandom.NextUlong composes two NextUint calls, so a 64-bit draw
            // would cost two advances. This generator overrides it. Comparing a NextUint against
            // NextUlong would only restate how NextUint is written, so both widths are measured
            // against the published stream instead: three 32-bit draws then a 64-bit one must land on
            // the fourth word, which holds only if each draw advanced the state exactly once.
            Xoshiro256StarStar random = new(Seed0, Seed1, Seed2, Seed3);

            for (int i = 0; i < 3; ++i)
            {
                Assert.AreEqual(
                    (uint)(ReferenceStream[i] >> 32),
                    random.NextUint(),
                    $"Draw {i} diverged from the published stream."
                );
            }

            Assert.AreEqual(
                ReferenceStream[3],
                random.NextUlong(),
                "The fourth draw skipped or repeated an advance."
            );
        }

        [Test]
        public void AllZeroSeedIsRepairedRatherThanLockedAtZero()
        {
            Xoshiro256StarStar random = new(0UL, 0UL, 0UL, 0UL);

            bool sawNonZero = false;
            for (int i = 0; i < 16 && !sawNonZero; ++i)
            {
                sawNonZero = random.NextUlong() != 0UL;
            }

            Assert.IsTrue(
                sawNonZero,
                "An all-zero state is a fixed point of the xoshiro recurrence and must be repaired."
            );
        }

        [Test]
        public void InternalStateRoundTripsThroughItsPayload()
        {
            Xoshiro256StarStar source = new(Seed0, Seed1, Seed2, Seed3);
            for (int i = 0; i < 7; ++i)
            {
                source.NextUlong();
            }

            RandomState state = source.InternalState;
            Xoshiro256StarStar restored = new(state);

            Assert.AreEqual(
                source,
                restored,
                "The two state words carried in the payload must survive the round trip."
            );
            for (int i = 0; i < 32; ++i)
            {
                Assert.AreEqual(source.NextUlong(), restored.NextUlong(), $"Draw {i} diverged.");
            }
        }
    }
}
