// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;

    /// <summary>
    /// The outcome of one counterbalanced comparison: how much faster the subject is than the
    /// reference, and how much each side's own throughput moved between raw cycles.
    /// </summary>
    /// <remarks>
    /// The spreads are the part that decides whether the ratio may be published. A ratio measured
    /// on a machine whose own throughput moved 30% between adjacent cycles is a reading of the
    /// machine, not of the code.
    /// </remarks>
    public readonly struct PairedMeasurement : IEquatable<PairedMeasurement>
    {
        /// <summary>
        /// A measurement that could not be taken. Every field is zero and
        /// <see cref="IsUsable"/> is false.
        /// </summary>
        public static readonly PairedMeasurement Unusable = default;

        /// <summary>
        /// Subject throughput divided by reference throughput, as the geometric mean over the
        /// retained cycles. Greater than one means the subject is faster.
        /// </summary>
        public double Ratio { get; }

        /// <summary>Relative spread of the reference's own raw cycles: <c>(max - min) / min</c>.</summary>
        public double ReferenceSpread { get; }

        /// <summary>Relative spread of the subject's own raw cycles: <c>(max - min) / min</c>.</summary>
        public double SubjectSpread { get; }

        /// <summary>How many raw cycles each side contributed.</summary>
        public int Cycles { get; }

        public PairedMeasurement(
            double ratio,
            double referenceSpread,
            double subjectSpread,
            int cycles
        )
        {
            Ratio = ratio;
            ReferenceSpread = referenceSpread;
            SubjectSpread = subjectSpread;
            Cycles = cycles;
        }

        /// <summary>Whether a comparison was actually taken.</summary>
        public bool IsUsable => 0 < Cycles && 0 < Ratio;

        /// <summary>The larger of the two sides' spreads, which is the one that bounds the result.</summary>
        public double WorstSpread =>
            ReferenceSpread < SubjectSpread ? SubjectSpread : ReferenceSpread;

        /// <summary>
        /// Whether both sides held still enough for the ratio to mean anything. An unusable
        /// measurement is never stable, so a caller that checks only this cannot publish a zero.
        /// </summary>
        public bool IsStable(double spreadLimit)
        {
            return IsUsable && WorstSpread <= spreadLimit;
        }

        public override string ToString()
        {
            return IsUsable
                ? $"{Ratio:F4}x over {Cycles} cycles (reference spread {ReferenceSpread:P2}, subject spread {SubjectSpread:P2})"
                : "unusable";
        }

        public bool Equals(PairedMeasurement other)
        {
            return Ratio.Equals(other.Ratio)
                && ReferenceSpread.Equals(other.ReferenceSpread)
                && SubjectSpread.Equals(other.SubjectSpread)
                && Cycles == other.Cycles;
        }

        public override bool Equals(object obj)
        {
            return obj is PairedMeasurement other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Ratio.GetHashCode();
                hash = (hash * 397) ^ ReferenceSpread.GetHashCode();
                hash = (hash * 397) ^ SubjectSpread.GetHashCode();
                hash = (hash * 397) ^ Cycles;
                return hash;
            }
        }

        public static bool operator ==(PairedMeasurement left, PairedMeasurement right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PairedMeasurement left, PairedMeasurement right)
        {
            return !left.Equals(right);
        }
    }
}
