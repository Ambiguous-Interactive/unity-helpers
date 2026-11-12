// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using WallstopStudios.UnityHelpers.Utils;

    public static partial class WallMath
    {
        /// <summary>Computes the logarithm of the sum of exponentials without intermediate overflow.</summary>
        /// <remarks>Empty input returns negative infinity. NaN propagates; positive infinity dominates finite values.</remarks>
        public static double LogSumExp(ReadOnlySpan<double> values)
        {
            if (!TryGetLogitMaximum(values, out double maximum, out _))
            {
                return double.NaN;
            }
            if (double.IsInfinity(maximum))
            {
                return maximum;
            }
            double sum = 0d;
            foreach (double value in values)
            {
                sum += Math.Exp(value - maximum);
            }
            return maximum + Math.Log(sum);
        }

        /// <summary>Replaces logits with normalized softmax probabilities using a positive finite temperature.</summary>
        /// <remarks>Invalid input and all-negative-infinity input return false without writes. Positive infinities share all probability equally.</remarks>
        public static bool SoftmaxInPlace(Span<double> logits, double temperature = 1d)
        {
            if (
                !double.IsFinite(temperature)
                || temperature <= 0d
                || logits.IsEmpty
                || !TryGetLogitMaximum(logits, out double maximum, out int infiniteCount)
                || double.IsNegativeInfinity(maximum)
            )
            {
                return false;
            }
            int count = logits.Length;
            if (0 < infiniteCount)
            {
                double probability = 1d / infiniteCount;
                for (int index = 0; index < count; ++index)
                {
                    logits[index] = double.IsPositiveInfinity(logits[index]) ? probability : 0d;
                }
                return true;
            }
            double sum = 0d;
            for (int index = 0; index < count; ++index)
            {
                if (double.IsNegativeInfinity(logits[index]))
                {
                    logits[index] = 0d;
                    continue;
                }
                double difference = logits[index] - maximum;
                double scaled = double.IsNegativeInfinity(difference)
                    ? logits[index] / temperature - maximum / temperature
                    : difference / temperature;
                double probability = Math.Exp(scaled);
                logits[index] = probability;
                sum += probability;
            }
            for (int index = 0; index < count; ++index)
            {
                logits[index] /= sum;
            }
            return true;
        }

        /// <summary>Computes the logistic sigmoid without overflowing the exponential.</summary>
        public static double Sigmoid(double value)
        {
            if (0d <= value)
            {
                return 1d / (1d + Math.Exp(-value));
            }
            double exponential = Math.Exp(value);
            return exponential / (1d + exponential);
        }

        /// <summary>Computes log(1 + exp(value)) without overflow or loss of the negative tail.</summary>
        public static double Softplus(double value)
        {
            double exponential = Math.Exp(-Math.Abs(value));
            double sum = 1d + exponential;
            double logarithm = sum == 1d ? exponential : Math.Log(sum) * exponential / (sum - 1d);
            return Math.Max(0d, value) + logarithm;
        }

        /// <summary>Reads an interpolated percentile from ascending data without copying or sorting.</summary>
        /// <remarks>Returns NaN for empty, unsorted or NaN-containing data or a percentile outside [0, 1]. Validation is linear in the input length.</remarks>
        public static double PercentileOfSorted(ReadOnlySpan<double> sorted, double percentile)
        {
            if (
                !IsSortedStatisticInput(sorted)
                || !double.IsFinite(percentile)
                || percentile < 0d
                || 1d < percentile
            )
            {
                return double.NaN;
            }
            double rank = percentile * (sorted.Length - 1);
            int lower = (int)rank;
            double fraction = rank - lower;
            return fraction == 0d
                ? sorted[lower]
                : InterpolateOrdered(sorted[lower], sorted[lower + 1], fraction);
        }

        /// <summary>Reads the nearest-rank percentile from ascending data without copying or sorting.</summary>
        /// <remarks>Uses ceil(p * count), with p = 0 selecting the minimum. Invalid inputs return NaN.</remarks>
        public static double PercentileNearestRank(ReadOnlySpan<double> sorted, double percentile)
        {
            if (
                !IsSortedStatisticInput(sorted)
                || !double.IsFinite(percentile)
                || percentile < 0d
                || 1d < percentile
            )
            {
                return double.NaN;
            }
            int index = percentile == 0d ? 0 : (int)Math.Ceiling(percentile * sorted.Length) - 1;
            return sorted[index];
        }

        /// <summary>Reads the median from ascending data without copying or sorting; invalid inputs return false and NaN.</summary>
        public static bool TryMedian(ReadOnlySpan<double> sorted, out double median)
        {
            double result = PercentileOfSorted(sorted, 0.5d);
            median = result;
            return !double.IsNaN(result);
        }

        /// <summary>Writes one-based tie-averaged ranks in original input order.</summary>
        /// <remarks>Uses pooled sorting storage. A short destination or NaN returns false without writes; overlapping inputs are supported. Empty input succeeds.</remarks>
        public static bool AverageRanks(ReadOnlySpan<double> values, Span<double> ranks)
        {
            int count = values.Length;
            if (ranks.Length < count || ContainsNaN(values))
            {
                return false;
            }
            if (count == 0)
            {
                return true;
            }
            using PooledArray<double> valuesLease = SystemArrayPool<double>.Get(
                count,
                out double[] sorted
            );
            using PooledArray<int> positionsLease = SystemArrayPool<int>.Get(
                count,
                out int[] positions
            );
            values.CopyTo(sorted);
            for (int index = 0; index < count; ++index)
            {
                positions[index] = index;
            }
            Array.Sort(sorted, positions, 0, count);
            int start = 0;
            while (start < count)
            {
                int end = start + 1;
                while (end < count && sorted[start] == sorted[end])
                {
                    ++end;
                }
                double rank = ((double)start + end + 1d) * 0.5d;
                for (int index = start; index < end; ++index)
                {
                    ranks[positions[index]] = rank;
                }
                start = end;
            }
            return true;
        }

        /// <summary>Computes Spearman's correlation with averaged ties using pooled scratch storage.</summary>
        /// <remarks>Returns NaN for mismatched lengths, fewer than two pairs, NaN values, or constant input.</remarks>
        public static double Spearman(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
        {
            int count = x.Length;
            if (count < 2 || y.Length != count || ContainsNaN(x) || ContainsNaN(y))
            {
                return double.NaN;
            }
            using PooledArray<double> xLease = SystemArrayPool<double>.Get(
                count,
                out double[] xRanks
            );
            using PooledArray<double> yLease = SystemArrayPool<double>.Get(
                count,
                out double[] yRanks
            );
            AverageRanks(x, xRanks.AsSpan(0, count));
            AverageRanks(y, yRanks.AsSpan(0, count));
            double center = (count + 1d) * 0.5d;
            double product = 0d;
            double xSquares = 0d;
            double ySquares = 0d;
            for (int index = 0; index < count; ++index)
            {
                double left = xRanks[index] - center;
                double right = yRanks[index] - center;
                product += left * right;
                xSquares += left * left;
                ySquares += right * right;
            }
            return product / Math.Sqrt(xSquares * ySquares);
        }

        /// <summary>Computes Kendall's tau-b with tie correction in O(n log n) time using pooled storage.</summary>
        /// <remarks>Returns NaN for mismatched lengths, fewer than two pairs, NaN values, or constant input.</remarks>
        public static double KendallTauB(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
        {
            int count = x.Length;
            if (count < 2 || y.Length != count || ContainsNaN(x) || ContainsNaN(y))
            {
                return double.NaN;
            }
            using PooledArray<double> xLease = SystemArrayPool<double>.Get(
                count,
                out double[] sortedX
            );
            using PooledArray<double> yLease = SystemArrayPool<double>.Get(
                count,
                out double[] sortedY
            );
            using PooledArray<double> scratchLease = SystemArrayPool<double>.Get(
                count,
                out double[] scratch
            );
            x.CopyTo(sortedX);
            y.CopyTo(sortedY);
            Array.Sort(sortedX, sortedY, 0, count);
            long xTies = 0;
            long jointTies = 0;
            int start = 0;
            while (start < count)
            {
                int end = start + 1;
                while (end < count && sortedX[start] == sortedX[end])
                {
                    ++end;
                }
                xTies += PairCount(end - start);
                Array.Sort(sortedY, start, end - start);
                jointTies += CountTies(sortedY, start, end);
                start = end;
            }
            long inversions = SortAndCountInversions(sortedY, scratch, 0, count);
            long yTies = CountTies(sortedY, 0, count);
            long pairs = PairCount(count);
            double numerator = pairs - xTies - yTies + jointTies - 2L * inversions;
            return numerator / Math.Sqrt((double)(pairs - xTies) * (pairs - yTies));
        }

        private static bool TryGetLogitMaximum(
            ReadOnlySpan<double> values,
            out double maximum,
            out int infiniteCount
        )
        {
            double result = double.NegativeInfinity;
            int positives = 0;
            foreach (double value in values)
            {
                if (double.IsNaN(value))
                {
                    maximum = double.NaN;
                    infiniteCount = 0;
                    return false;
                }
                if (result < value)
                {
                    result = value;
                }
                if (double.IsPositiveInfinity(value))
                {
                    ++positives;
                }
            }
            maximum = result;
            infiniteCount = positives;
            return true;
        }

        private static bool IsSortedStatisticInput(ReadOnlySpan<double> values)
        {
            if (values.IsEmpty || double.IsNaN(values[0]))
            {
                return false;
            }
            int count = values.Length;
            for (int index = 1; index < count; ++index)
            {
                if (double.IsNaN(values[index]) || values[index] < values[index - 1])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsNaN(ReadOnlySpan<double> values)
        {
            foreach (double value in values)
            {
                if (double.IsNaN(value))
                {
                    return true;
                }
            }
            return false;
        }

        private static long PairCount(int count)
        {
            return (long)count * (count - 1) / 2;
        }

        private static long CountTies(double[] values, int start, int end)
        {
            long ties = 0;
            while (start < end)
            {
                int next = start + 1;
                while (next < end && values[next] == values[start])
                {
                    ++next;
                }
                ties += PairCount(next - start);
                start = next;
            }
            return ties;
        }

        private static long SortAndCountInversions(
            double[] values,
            double[] scratch,
            int start,
            int end
        )
        {
            if (end - start < 2)
            {
                return 0;
            }
            int middle = start + (end - start) / 2;
            long inversions =
                SortAndCountInversions(values, scratch, start, middle)
                + SortAndCountInversions(values, scratch, middle, end);
            int left = start;
            int right = middle;
            for (int target = start; target < end; ++target)
            {
                if (left < middle && (end <= right || values[left] <= values[right]))
                {
                    scratch[target] = values[left++];
                }
                else
                {
                    scratch[target] = values[right++];
                    inversions += middle - left;
                }
            }
            Array.Copy(scratch, start, values, start, end - start);
            return inversions;
        }
    }
}
