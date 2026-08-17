// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// WDoomRandom deliberately has a period of 256, so it is the one generator that does not run the
    /// statistical suite in <c>RandomTestBase</c> -- it would fail it, by design. What is worth
    /// asserting is the table, the wrap, and that a saved index restores the exact sequence.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WDoomRandomTests
    {
        [Test]
        public void LookupTableIsAPermutationOfEveryByte()
        {
            ReadOnlySpan<byte> table = WDoomRandom.LookupTable;
            Assert.That(table.Length, Is.EqualTo(256));

            bool[] seen = new bool[256];
            foreach (byte value in table)
            {
                Assert.That(seen[value], Is.False, $"byte {value} appears twice");
                seen[value] = true;
            }
        }

        [Test]
        public void ACycleOfDrawsReturnsEveryByteExactlyOnce()
        {
            WDoomRandom random = new(seedIndex: 0);
            int[] counts = new int[256];
            for (int i = 0; i < 256; ++i)
            {
                counts[random.NextTableByte()]++;
            }

            for (int value = 0; value < counts.Length; ++value)
            {
                Assert.That(
                    counts[value],
                    Is.EqualTo(1),
                    $"byte {value} came out {counts[value]} times"
                );
            }
        }

        [Test]
        public void TheSequenceRepeatsEveryTwoHundredAndFiftySixDraws()
        {
            WDoomRandom random = new(seedIndex: 17);
            List<byte> first = new(256);
            for (int i = 0; i < 256; ++i)
            {
                first.Add(random.NextTableByte());
            }

            for (int i = 0; i < 256; ++i)
            {
                Assert.That(
                    random.NextTableByte(),
                    Is.EqualTo(first[i]),
                    $"draw {i} of the second cycle"
                );
            }
        }

        [Test]
        public void AnyStartingIndexIsAcceptedAndWrapped()
        {
            foreach (int seed in new[] { int.MinValue, -1, 0, 255, 256, 1_000_000, int.MaxValue })
            {
                WDoomRandom random = new(seed);
                Assert.DoesNotThrow(() => random.NextTableByte());
                Assert.DoesNotThrow(() => random.NextUint());
            }
        }

        [Test]
        public void TheSavedIndexRestoresTheExactSequence()
        {
            WDoomRandom original = new(seedIndex: 3);
            for (int i = 0; i < 37; ++i)
            {
                original.NextUint();
            }

            WDoomRandom restored = new(original.InternalState);
            for (int i = 0; i < 512; ++i)
            {
                Assert.That(restored.NextUint(), Is.EqualTo(original.NextUint()), $"draw {i}");
            }
        }

        [Test]
        public void ACopyIsIndependentOfItsSource()
        {
            WDoomRandom original = new(seedIndex: 91);
            original.NextUint();
            IRandom copy = original.Copy();

            Assert.That(copy.NextUint(), Is.EqualTo(original.NextUint()));

            copy.NextUint();
            Assert.That(copy.InternalState, Is.Not.EqualTo(original.InternalState));
        }

        [Test]
        public void AUintDrawAdvancesTheIndexByFourBytes()
        {
            WDoomRandom viaUint = new(seedIndex: 0);
            WDoomRandom viaBytes = new(seedIndex: 0);

            uint packed = viaUint.NextUint();
            uint rebuilt = 0;
            for (int i = 0; i < 4; ++i)
            {
                rebuilt = (rebuilt << 8) | viaBytes.NextTableByte();
            }

            Assert.That(packed, Is.EqualTo(rebuilt));
            Assert.That(viaUint.InternalState, Is.EqualTo(viaBytes.InternalState));
        }
    }
}
