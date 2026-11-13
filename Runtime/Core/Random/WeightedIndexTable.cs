// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Immutable weights validated once for repeated index sampling.</summary>
    public sealed class WeightedIndexTable
    {
        private const int LinearSearchLimit = 16;

        /// <summary>Gets the original number of slots, including disabled weights.</summary>
        public int Count => _logWeights.Length;

        private readonly double[] _cumulative;
        private readonly double[] _logWeights;
        private readonly double _total;
        private readonly int _lastPositiveIndex;

        private WeightedIndexTable(
            double[] cumulative,
            double[] logWeights,
            double total,
            int lastPositiveIndex
        )
        {
            _cumulative = cumulative;
            _logWeights = logWeights;
            _total = total;
            _lastPositiveIndex = lastPositiveIndex;
        }

        /// <summary>Copies finite float weights, treating nonpositive slots as disabled.</summary>
        /// <remarks>Returns false for empty, nonfinite, or entirely disabled weights. Temporary conversion storage is pooled.</remarks>
        public static bool TryCreate(ReadOnlySpan<float> weights, out WeightedIndexTable table)
        {
            int length = weights.Length;
            if (length == 0)
            {
                table = null;
                return false;
            }
            using PooledArray<double> conversionLease = SystemArrayPool<double>.Get(
                length,
                out double[] converted
            );
            for (int index = 0; index < length; ++index)
            {
                converted[index] = weights[index];
            }
            ReadOnlySpan<double> convertedWeights = converted.AsSpan(0, length);
            return TryCreate(convertedWeights, out table);
        }

        /// <summary>Copies finite weights, treating nonpositive slots as disabled.</summary>
        /// <remarks>Returns false for empty, nonfinite, or entirely disabled weights. Later source changes do not affect the table.</remarks>
        public static bool TryCreate(ReadOnlySpan<double> weights, out WeightedIndexTable table)
        {
            table = null;
            double scale = 0d;
            foreach (double weight in weights)
            {
                if (!double.IsFinite(weight))
                {
                    return false;
                }
                if (scale < weight)
                {
                    scale = weight;
                }
            }
            if (!(0d < scale))
            {
                return false;
            }
            int length = weights.Length;
            double[] cumulative = new double[length];
            double[] logWeights = new double[length];
            double total = 0d;
            int lastPositiveIndex = 0;
            for (int index = 0; index < length; ++index)
            {
                double weight = weights[index];
                if (0d < weight)
                {
                    total += weight / scale;
                    logWeights[index] = Math.Log(weight);
                    lastPositiveIndex = index;
                }
                else
                {
                    logWeights[index] = double.NegativeInfinity;
                }
                cumulative[index] = total;
            }
            table = new WeightedIndexTable(cumulative, logWeights, total, lastPositiveIndex);
            return true;
        }

        /// <summary>Draws one index, or returns -1 for a null generator or invalid uniform sample.</summary>
        /// <remarks>Consumes one double. Scans up to 16 slots and uses binary search for larger tables.</remarks>
        public int Next(IRandom random)
        {
            if (random == null)
            {
                return -1;
            }
            double uniform = random.NextDouble();
            if (!(0d <= uniform && uniform < 1d))
            {
                return -1;
            }
            double sample = uniform * _total;
            int length = _cumulative.Length;
            if (length <= LinearSearchLimit)
            {
                for (int index = 0; index < length; ++index)
                {
                    if (sample < _cumulative[index])
                    {
                        return index;
                    }
                }
            }
            else
            {
                int low = 0;
                int high = length;
                while (low < high)
                {
                    int middle = low + ((high - low) >> 1);
                    if (sample < _cumulative[middle])
                    {
                        high = middle;
                    }
                    else
                    {
                        low = middle + 1;
                    }
                }
                if (low < length)
                {
                    return low;
                }
            }
            return _lastPositiveIndex;
        }

        /// <summary>Draws one index by exponential race, or returns -1 for a null generator or invalid sample.</summary>
        /// <remarks>Consumes one double per original slot, including disabled slots. Ties select the first positive slot.</remarks>
        public int NextByRace(IRandom random)
        {
            if (random == null)
            {
                return -1;
            }
            double bestScore = double.PositiveInfinity;
            int selected = -1;
            int length = _logWeights.Length;
            for (int index = 0; index < length; ++index)
            {
                double uniform = random.NextDouble();
                if (!(0d <= uniform && uniform < 1d))
                {
                    return -1;
                }
                double logWeight = _logWeights[index];
                if (double.IsNegativeInfinity(logWeight))
                {
                    continue;
                }
                double exponential = -Math.Log(1d - uniform);
                double score = Math.Log(exponential) - logWeight;
                if (selected < 0 || score < bestScore)
                {
                    bestScore = score;
                    selected = index;
                }
            }
            return selected;
        }
    }
}
