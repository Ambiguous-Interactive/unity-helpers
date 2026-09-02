// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class JsonArrayAccumulatorTests
    {
        /// <summary>
        /// Every rent must be released through the lease the pool handed back. Wrapping the same
        /// array in a second <see cref="PooledArray{T}"/> acquires a second slot and abandons the
        /// first, and an abandoned slot never reaches the free list -- so the count of slots ever
        /// created grows once per accumulator and once per growth step, for the life of the
        /// process, on the path every JSON array deserialization takes.
        /// </summary>
        [Test]
        public void AccumulatingDoesNotLeakDisposalSlots()
        {
            /*
                Warm up first: the very first accumulator legitimately creates the slots it uses,
                and the growth steps create theirs. After that every rent must reuse a freed slot,
                so the honest assertion is zero growth rather than a threshold.
            */
            Accumulate(256);
            Accumulate(256);

            int before = DisposalLeases.SlotsCreated;
            for (int repetition = 0; repetition < 32; ++repetition)
            {
                Accumulate(256);
            }

            Assert.AreEqual(
                before,
                DisposalLeases.SlotsCreated,
                "32 accumulations created new disposal slots, so a lease is being abandoned rather "
                    + "than released"
            );
        }

        [Test]
        public void AccumulatingReturnsEveryItemInOrderAcrossGrowth()
        {
            JsonArrayAccumulator<int> accumulator = default;
            try
            {
                for (int index = 0; index < 300; ++index)
                {
                    accumulator.Add(index);
                }

                int[] produced = accumulator.Finish();
                Assert.AreEqual(300, produced.Length);
                for (int index = 0; index < produced.Length; ++index)
                {
                    Assert.AreEqual(index, produced[index], $"element {index} survived the growth");
                }
            }
            finally
            {
                accumulator.Dispose();
            }
        }

        private static void Accumulate(int count)
        {
            JsonArrayAccumulator<int> accumulator = default;
            try
            {
                for (int index = 0; index < count; ++index)
                {
                    accumulator.Add(index);
                }
            }
            finally
            {
                accumulator.Dispose();
            }
        }
    }
}
