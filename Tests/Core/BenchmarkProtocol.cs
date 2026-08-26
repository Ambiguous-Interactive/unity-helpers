// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;

    /// <summary>
    /// Compares two implementations by interleaving them, so a machine that drifts during the
    /// measurement cannot be mistaken for one of them being faster.
    /// </summary>
    /// <remarks>
    /// <para>Measuring A for a while and then B for a while attributes everything that changed in
    /// between -- another process starting, a thermal step, a JIT tier promotion -- to B. That is
    /// how a committed benchmark table and a fresh run come to disagree by up to 9x with inverted
    /// rankings, which is the state
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/285">#285</see>
    /// recorded and could not act on. The technique here is the one the sister DxMessaging
    /// repository settled on (issue
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/573">#573</see>).</para>
    /// <para>Each batch runs the fixed order <c>ABBABAAB</c>. Every subject slot is adjacent in
    /// time to the reference slot it is paired with, and both sides occupy the same mean position
    /// in the batch (3.5), so a drift that is linear over the batch cancels exactly rather than
    /// approximately. Four ratios come out of one batch, and the reported ratio is their geometric
    /// mean -- geometric because ratios compose by multiplication, so an arithmetic mean of
    /// <c>2.0</c> and <c>0.5</c> would report 1.25 for a pair that is exactly even.</para>
    /// <para>The four raw values per side are retained rather than averaged away, because their
    /// spread is the only evidence that the machine held still. A caller publishes a ratio only
    /// when <see cref="PairedMeasurement.IsStable"/> agrees.</para>
    /// <para>Every slot is preceded by <see cref="Settle"/>, so no slot pays for the garbage the
    /// previous one left (#431).</para>
    /// </remarks>
    public static class BenchmarkProtocol
    {
        /// <summary>
        /// Raw cycles each side contributes per batch. Fixed by the counterbalanced order.
        /// </summary>
        public const int CyclesPerBatch = 4;

        /// <summary>
        /// The spread above which a comparison is a reading of the machine. Predeclared rather
        /// than tuned after the fact, and the same 3% the sister repository settled on.
        /// </summary>
        public const double DefaultSpreadLimit = 0.03;

        // false = reference, true = subject. A B B A B A A B.
        private static readonly bool[] BatchSlots =
        {
            false,
            true,
            true,
            false,
            true,
            false,
            false,
            true,
        };

        /// <summary>
        /// The slot order one batch runs, as <c>'A'</c> (reference) and <c>'B'</c> (subject).
        /// Exposed so the order can be asserted rather than trusted.
        /// </summary>
        public static string BatchOrder()
        {
            char[] order = new char[BatchSlots.Length];
            for (int index = 0; index < BatchSlots.Length; index++)
            {
                order[index] = BatchSlots[index] ? 'B' : 'A';
            }

            return new string(order);
        }

        /// <summary>
        /// Runs both sides interleaved and reports how they compare. Each delegate is invoked once
        /// per slot and returns that slot's throughput in operations per second; higher is faster.
        /// </summary>
        /// <param name="reference">The side every ratio is expressed against.</param>
        /// <param name="subject">The side being compared.</param>
        /// <param name="batches">How many <c>ABBABAAB</c> batches to run. One is the default.</param>
        /// <returns>
        /// The comparison, or <see cref="PairedMeasurement.Unusable"/> when a delegate is missing,
        /// <paramref name="batches"/> is not positive, or any slot reported a throughput that is
        /// not a positive finite number.
        /// </returns>
        public static PairedMeasurement MeasurePaired(
            Func<double> reference,
            Func<double> subject,
            int batches = 1
        )
        {
            if (reference == null || subject == null || batches <= 0)
            {
                return PairedMeasurement.Unusable;
            }

            int cycles = batches * CyclesPerBatch;
            double[] referenceCycles = new double[cycles];
            double[] subjectCycles = new double[cycles];
            int referenceCount = 0;
            int subjectCount = 0;

            for (int batch = 0; batch < batches; batch++)
            {
                for (int slot = 0; slot < BatchSlots.Length; slot++)
                {
                    Settle();
                    if (BatchSlots[slot])
                    {
                        subjectCycles[subjectCount++] = subject();
                    }
                    else
                    {
                        referenceCycles[referenceCount++] = reference();
                    }
                }
            }

            return Combine(referenceCycles, subjectCycles);
        }

        /// <summary>
        /// Turns two equal-length series of per-cycle throughputs into a comparison. Separate from
        /// <see cref="MeasurePaired"/> so the arithmetic can be driven with known numbers.
        /// </summary>
        public static PairedMeasurement Combine(double[] referenceCycles, double[] subjectCycles)
        {
            if (
                referenceCycles == null
                || subjectCycles == null
                || referenceCycles.Length == 0
                || referenceCycles.Length != subjectCycles.Length
            )
            {
                return PairedMeasurement.Unusable;
            }

            double logSum = 0;
            for (int index = 0; index < referenceCycles.Length; index++)
            {
                double referenceValue = referenceCycles[index];
                double subjectValue = subjectCycles[index];
                if (!IsMeasurable(referenceValue) || !IsMeasurable(subjectValue))
                {
                    return PairedMeasurement.Unusable;
                }

                logSum += Math.Log(subjectValue / referenceValue);
            }

            double ratio = Math.Exp(logSum / referenceCycles.Length);
            return new PairedMeasurement(
                ratio,
                Spread(referenceCycles),
                Spread(subjectCycles),
                referenceCycles.Length
            );
        }

        /// <summary>
        /// Puts the heap in a known state so a slot does not pay for the garbage the previous slot
        /// left, and so a collection that was going to happen anyway happens before the clock
        /// starts rather than in the middle of the window.
        /// </summary>
        /// <remarks>
        /// Two collections with the finalizer queue drained between them, because the first one
        /// can only queue a finalizable object -- the memory it holds comes back on the second.
        /// This is the in-process half of what the sister repository got from a fresh player per
        /// roster (#431); it does not reset JIT tiering or native allocator state, and a
        /// comparison that needs those still needs a new process.
        /// </remarks>
        public static void Settle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Relative spread of a series: <c>(max - min) / min</c>. Zero for a series that never
        /// moved, and <see cref="double.PositiveInfinity"/> for one this cannot read -- returning
        /// zero there would report a series of garbage as perfectly stable, which is the one answer
        /// a stability check must never give.
        /// </summary>
        public static double Spread(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return double.PositiveInfinity;
            }

            double lowest = double.MaxValue;
            double highest = double.MinValue;
            foreach (double value in values)
            {
                if (!IsMeasurable(value))
                {
                    return double.PositiveInfinity;
                }

                if (value < lowest)
                {
                    lowest = value;
                }

                if (highest < value)
                {
                    highest = value;
                }
            }

            return (highest - lowest) / lowest;
        }

        private static bool IsMeasurable(double value)
        {
            return 0 < value && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
