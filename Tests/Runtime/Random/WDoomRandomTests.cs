// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// WDoomRandom deliberately has a period of 1024, so it is the one generator that does not run
    /// the statistical suite in <c>RandomTestBase</c> -- it would fail it, by design. What is worth
    /// asserting is the table, the wrap, and that a saved index restores the exact sequence.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WDoomRandomTests
    {
        [Test]
        public void LookupTableEntriesAreAllDistinct()
        {
            ReadOnlySpan<uint> table = WDoomRandom.LookupTable;
            Assert.That(table.Length, Is.EqualTo(1024));

            HashSet<uint> seen = new(table.Length);
            foreach (uint value in table)
            {
                Assert.That(seen.Add(value), Is.True, $"entry {value} appears twice");
            }
        }

        [Test]
        public void ACycleOfDrawsReturnsEveryTableEntryExactlyOnce()
        {
            WDoomRandom random = new(seedIndex: 0);
            Dictionary<uint, int> counts = new(1024);
            for (int i = 0; i < 1024; ++i)
            {
                uint drawn = random.NextUint();
                counts[drawn] = counts.TryGetValue(drawn, out int drawnSoFar) ? drawnSoFar + 1 : 1;
            }

            Assert.That(counts.Count, Is.EqualTo(1024));
            foreach (uint entry in WDoomRandom.LookupTable)
            {
                Assert.That(counts.TryGetValue(entry, out int count), Is.True, $"entry {entry}");
                Assert.That(count, Is.EqualTo(1), $"entry {entry} came out {count} times");
            }
        }

        [Test]
        public void TheSequenceRepeatsEveryThousandAndTwentyFourDraws()
        {
            WDoomRandom random = new(seedIndex: 17);
            List<uint> first = new(1024);
            for (int i = 0; i < 1024; ++i)
            {
                first.Add(random.NextUint());
            }

            for (int i = 0; i < 1024; ++i)
            {
                Assert.That(
                    random.NextUint(),
                    Is.EqualTo(first[i]),
                    $"draw {i} of the second cycle"
                );
            }
        }

        [Test]
        public void AnyStartingIndexIsAcceptedAndWrapped()
        {
            foreach (int seed in new[] { int.MinValue, -1, 0, 1023, 1024, 1_000_000, int.MaxValue })
            {
                WDoomRandom random = new(seed);
                Assert.DoesNotThrow(() => random.NextUint());
                Assert.DoesNotThrow(() => random.NextUlong());
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
        public void ADrawIsTheNextTableEntryAndAdvancesTheIndexByOne()
        {
            // Whole-word table entries preserve the visible period instead of consuming four bytes per draw.
            ReadOnlySpan<uint> table = WDoomRandom.LookupTable;
            WDoomRandom random = new(seedIndex: 0);

            for (int i = 1; i <= 8; ++i)
            {
                Assert.That(random.NextUint(), Is.EqualTo(table[i]), $"draw {i}");
            }
        }
    }
}
