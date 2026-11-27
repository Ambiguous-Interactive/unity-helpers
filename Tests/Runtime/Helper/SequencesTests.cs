// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SequencesTests
    {
        private static readonly long[] FirstFifteenLubyValues =
        {
            1L,
            1L,
            2L,
            1L,
            1L,
            2L,
            4L,
            1L,
            1L,
            2L,
            1L,
            1L,
            2L,
            4L,
            8L,
        };

        [Test]
        public void LubyReturnsThePublishedSequence()
        {
            for (int index = 1; index <= FirstFifteenLubyValues.Length; ++index)
            {
                Assert.AreEqual(
                    FirstFifteenLubyValues[index - 1],
                    Sequences.Luby(index),
                    $"index={index}"
                );
            }
        }

        [Test]
        public void LubyReturnsAPowerOfTwoAtEveryIndex()
        {
            for (int index = 1; index <= 4_096; ++index)
            {
                long value = Sequences.Luby(index);
                Assert.IsTrue(BitOps.IsPowerOfTwo(value), $"index={index}, value={value}");
            }
        }

        [Test]
        public void LubyReachesEachRunMaximum()
        {
            for (int run = 1; run <= 31; ++run)
            {
                int index = (int)((1L << run) - 1L);
                Assert.AreEqual(1L << (run - 1), Sequences.Luby(index), $"run={run}");
            }
        }

        [Test]
        public void LubyHandlesTheLargestIndex()
        {
            Assert.AreEqual(1L << 30, Sequences.Luby(int.MaxValue));
            Assert.AreEqual(1L << 29, Sequences.Luby(int.MaxValue - 1));
        }

        [Test]
        public void LubyReturnsZeroForNonPositiveIndexes()
        {
            Assert.AreEqual(0L, Sequences.Luby(0));
            Assert.AreEqual(0L, Sequences.Luby(-1));
            Assert.AreEqual(0L, Sequences.Luby(int.MinValue));
        }

        [Test]
        public void TryLubyRejectsNonPositiveIndexesWithoutThrowing()
        {
            Assert.IsFalse(Sequences.TryLuby(0, out long zeroValue));
            Assert.AreEqual(0L, zeroValue);
            Assert.IsFalse(Sequences.TryLuby(-1, out long negativeValue));
            Assert.AreEqual(0L, negativeValue);
            Assert.IsFalse(Sequences.TryLuby(int.MinValue, out long minimumValue));
            Assert.AreEqual(0L, minimumValue);
        }

        [Test]
        public void TryLubyMatchesLubyForPositiveIndexes()
        {
            for (int index = 1; index <= FirstFifteenLubyValues.Length; ++index)
            {
                Assert.IsTrue(Sequences.TryLuby(index, out long value), $"index={index}");
                Assert.AreEqual(Sequences.Luby(index), value, $"index={index}");
            }
        }

        [Test]
        public void LubySpanMatchesScalarValues()
        {
            long[] values = new long[FirstFifteenLubyValues.Length];
            Sequences.Luby(values);
            CollectionAssert.AreEqual(FirstFifteenLubyValues, values);
        }

        [Test]
        public void LubyEmptySpanIsANoOp()
        {
            Assert.DoesNotThrow(() => Sequences.Luby(Span<long>.Empty));
        }
    }
}
