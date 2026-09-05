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
        private const int Repetitions = 32;

        /*
            Allow bounded process-wide counter noise from other threads while rejecting the eight leaked lease
            slots per accumulation.
        */
        private const int SlotBudget = 8;

        [Test]
        public void AccumulatingDoesNotLeakDisposalSlots()
        {
            // Warm every growth step before requiring lease-slot reuse.
            Accumulate(256);
            Accumulate(256);

            int before = DisposalLeases.SlotsCreated;
            for (int repetition = 0; repetition < Repetitions; ++repetition)
            {
                Accumulate(256);
            }

            int created = DisposalLeases.SlotsCreated - before;
            Assert.LessOrEqual(
                created,
                SlotBudget,
                $"{Repetitions} accumulations created {created} disposal slots. Each abandons one "
                    + "lease per rent and one per growth step, so a leak here is hundreds, not a "
                    + "handful; anything within the budget is another thread's lease, not this."
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
