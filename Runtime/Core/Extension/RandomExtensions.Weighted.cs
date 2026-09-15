// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Random;
    using WallstopStudios.UnityHelpers.Utils;

    public static partial class RandomExtensions
    {
        private const int MaxStackWeightedScoreCount =
            StackAllocation.MaxByteBudget / sizeof(double);

        /// <summary>
        /// Returns an index sampled from unnormalized weights, treating finite negative weights as zero.
        /// </summary>
        /// <exception cref="ArgumentException">A weight is not finite, the list is empty, or no weight is positive.</exception>
        public static int NextWeightedIndex(this IRandom random, IReadOnlyList<float> weights)
        {
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }
            if (weights.Count == 0)
            {
                throw new ArgumentException("Weights cannot be empty", nameof(weights));
            }
            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                float weight = weights[i];
                if (!float.IsFinite(weight))
                {
                    throw new ArgumentException("Weights must be finite", nameof(weights));
                }
                if (0f < weight)
                {
                    total += weight;
                }
            }
            if (!(0d < total))
            {
                throw new ArgumentException("Sum of weights must be > 0", nameof(weights));
            }
            double r = random.NextDouble() * total;
            double acc = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                float w = weights[i];
                if (w <= 0)
                {
                    continue;
                }
                acc += w;
                if (r <= acc)
                {
                    return i;
                }
            }
            return weights.Count - 1;
        }

        /// <summary>
        /// Returns an element sampled according to the given weights list. Throws if lengths mismatch.
        /// </summary>
        public static T NextWeightedElement<T>(
            this IRandom random,
            IReadOnlyList<T> items,
            IReadOnlyList<float> weights
        )
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }
            if (items.Count != weights.Count)
            {
                throw new ArgumentException(
                    "Items and weights length must match.",
                    nameof(weights)
                );
            }
            int idx = random.NextWeightedIndex(weights);
            return items[idx];
        }

        /// <summary>
        /// Selects a random item from a weighted collection where each item has an associated probability weight.
        /// </summary>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="weighted">A collection of (item, weight) tuples where weight determines selection probability.</param>
        /// <returns>A randomly selected item, with probability proportional to its weight relative to total weight.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if collection is empty, any weight is negative or nonfinite, or total weight is not positive and finite.
        /// </exception>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or weighted is null.
        /// Thread Safety: Thread-safe if random is thread-safe and weighted is not modified during execution.
        /// Performance: O(n) where n is the number of items - iterates twice (sum weights, then select).
        /// Allocations: Materializes non-list collections to array on first pass.
        /// Edge Cases: Due to floating point precision, may return last item if randomValue equals totalWeight.
        /// </remarks>
        public static T NextWeighted<T>(
            this IRandom random,
            IEnumerable<(T item, float weight)> weighted
        )
        {
            if (weighted is IReadOnlyList<(T, float)> items)
            {
                return NextWeightedCore(random, items);
            }

            using PooledResource<List<(T, float)>> lease = Buffers<(T, float)>.List.Get(
                out List<(T, float)> materializedList
            );
            materializedList.AddRange(weighted);

            return NextWeightedCore(random, materializedList);
        }

        /// <summary>
        /// Selects a random index from an array of weights, where each weight determines selection probability.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="weights">An array of weights where each element determines the probability of selecting that index.</param>
        /// <returns>A random index in [0, weights.Length), with probability proportional to weights[i] / totalWeight.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if weights is null/empty, any weight is negative or nonfinite, or total weight is not positive and finite.
        /// </exception>
        /// <remarks>
        /// Null Handling: Throws ArgumentException if weights is null.
        /// Thread Safety: Thread-safe if random is thread-safe and weights array is not modified during execution.
        /// Performance: O(n) where n is weights.Length - iterates to sum weights, then to select index.
        /// Allocations: No heap allocations.
        /// Edge Cases: Due to floating point precision, may return last index if randomValue equals totalWeight.
        /// </remarks>
        public static int NextWeightedIndex(this IRandom random, float[] weights)
        {
            if (weights == null || weights.Length == 0)
            {
                throw new ArgumentException(
                    "Weights array cannot be null or empty",
                    nameof(weights)
                );
            }

            float totalWeight = 0f;
            foreach (float weight in weights)
            {
                if (!(0f <= weight && weight <= float.MaxValue))
                {
                    throw new ArgumentException(
                        "Weights must be finite and nonnegative",
                        nameof(weights)
                    );
                }

                totalWeight += weight;
            }

            if (!(0f < totalWeight && totalWeight <= float.MaxValue))
            {
                throw new ArgumentException(
                    "Total weight must be finite and greater than zero",
                    nameof(weights)
                );
            }

            float randomValue = random.NextFloat(0f, totalWeight);
            float cumulative = 0f;

            for (int i = 0; i < weights.Length; ++i)
            {
                cumulative += weights[i];
                if (randomValue < cumulative)
                {
                    return i;
                }
            }

            // Fallback due to floating point precision
            return weights.Length - 1;
        }

        /// <summary>Tries to select an index with probability proportional to its double weight.</summary>
        /// <param name="random">The generator that supplies the draw.</param>
        /// <param name="weights">Finite weights. Non-positive weights cannot win.</param>
        /// <param name="index">The selected index, or zero on failure.</param>
        /// <returns>
        /// <see langword="true" /> when the generator is non-null and the weights are finite with
        /// at least one positive value; otherwise <see langword="false" />.
        /// </returns>
        /// <example>
        /// <code>
        /// ReadOnlySpan&lt;double&gt; weights = stackalloc double[] { 1d, 3d };
        /// bool selected = random.TryNextWeightedIndex(weights, out int index);
        /// </code>
        /// </example>
        public static bool TryNextWeightedIndex(
            this IRandom random,
            ReadOnlySpan<double> weights,
            out int index
        )
        {
            if (!TryGetWeightScale(random, weights, out double scale))
            {
                index = default;
                return false;
            }

            double normalizedTotal = 0d;
            foreach (double weight in weights)
            {
                if (0d < weight)
                {
                    normalizedTotal += weight / scale;
                }
            }

            double sample = random.NextDouble() * normalizedTotal;
            double cumulative = 0d;
            int lastPositiveIndex = 0;
            for (int weightIndex = 0; weightIndex < weights.Length; weightIndex++)
            {
                double weight = weights[weightIndex];
                if (!(0d < weight))
                {
                    continue;
                }

                lastPositiveIndex = weightIndex;
                cumulative += weight / scale;
                if (sample < cumulative)
                {
                    index = weightIndex;
                    return true;
                }
            }

            index = lastPositiveIndex;
            return true;
        }

        /// <summary>Tries to select an index through an exponential race over float weights.</summary>
        /// <param name="random">The generator that supplies one draw per weight.</param>
        /// <param name="weights">Finite weights. Non-positive weights consume a draw but cannot win.</param>
        /// <param name="index">The selected index, or zero on failure.</param>
        /// <returns>
        /// <see langword="true" /> when the generator is non-null and the weights are finite with
        /// at least one positive value; otherwise <see langword="false" />.
        /// </returns>
        /// <remarks>
        /// A weight change moves only that slot's clock. Near-equal clocks can compare differently
        /// across runtimes because <see cref="Math.Log(double)" /> is platform-dependent.
        /// </remarks>
        /// <example>
        /// <code>
        /// ReadOnlySpan&lt;float&gt; weights = stackalloc float[] { 1f, 3f };
        /// bool selected = random.TryNextWeightedIndexByRace(weights, out int index);
        /// </code>
        /// </example>
        public static bool TryNextWeightedIndexByRace(
            this IRandom random,
            ReadOnlySpan<float> weights,
            out int index
        )
        {
            if (!TryValidateRaceWeights(random, weights, out _))
            {
                index = default;
                return false;
            }

            double bestScore = double.PositiveInfinity;
            int selectedIndex = 0;
            for (int weightIndex = 0; weightIndex < weights.Length; weightIndex++)
            {
                double score = RaceScore(random.NextDouble(), weights[weightIndex]);
                if (score < bestScore)
                {
                    bestScore = score;
                    selectedIndex = weightIndex;
                }
            }

            index = selectedIndex;
            return true;
        }

        /// <summary>Tries to select an index through an exponential race over double weights.</summary>
        /// <param name="random">The generator that supplies one draw per weight.</param>
        /// <param name="weights">Finite weights. Non-positive weights consume a draw but cannot win.</param>
        /// <param name="index">The selected index, or zero on failure.</param>
        /// <returns>
        /// <see langword="true" /> when the generator is non-null and the weights are finite with
        /// at least one positive value; otherwise <see langword="false" />.
        /// </returns>
        /// <remarks>
        /// A weight change moves only that slot's clock. Near-equal clocks can compare differently
        /// across runtimes because <see cref="Math.Log(double)" /> is platform-dependent.
        /// </remarks>
        /// <example>
        /// <code>
        /// ReadOnlySpan&lt;double&gt; weights = stackalloc double[] { 1d, 3d };
        /// bool selected = random.TryNextWeightedIndexByRace(weights, out int index);
        /// </code>
        /// </example>
        public static bool TryNextWeightedIndexByRace(
            this IRandom random,
            ReadOnlySpan<double> weights,
            out int index
        )
        {
            if (!TryValidateRaceWeights(random, weights, out _))
            {
                index = default;
                return false;
            }

            double bestScore = double.PositiveInfinity;
            int selectedIndex = 0;
            for (int weightIndex = 0; weightIndex < weights.Length; weightIndex++)
            {
                double score = RaceScore(random.NextDouble(), weights[weightIndex]);
                if (score < bestScore)
                {
                    bestScore = score;
                    selectedIndex = weightIndex;
                }
            }

            index = selectedIndex;
            return true;
        }

        /// <summary>Tries to select weighted indices without replacement through an exponential race.</summary>
        /// <param name="random">The generator that supplies one draw per weight.</param>
        /// <param name="weights">Finite weights. Non-positive weights consume a draw but cannot win.</param>
        /// <param name="destination">Receives winners in ascending clock order.</param>
        /// <returns>
        /// <see langword="true" /> when every destination slot receives a winner; otherwise
        /// <see langword="false" /> without changing <paramref name="destination" />.
        /// </returns>
        /// <remarks>
        /// <paramref name="weights" /> and <paramref name="destination" /> must not overlap,
        /// including when backed by differently typed views of the same bytes.
        /// Calls requesting at most 1024 winners use stack scratch. Larger calls rent scratch from
        /// <see cref="SystemArrayPool{T}" />. Use the scratch overload to guarantee no pool activity.
        /// Selection takes O(n log k) time for n weights and k winners.
        /// </remarks>
        /// <example>
        /// <code>
        /// ReadOnlySpan&lt;double&gt; weights = stackalloc double[] { 1d, 2d, 3d };
        /// Span&lt;int&gt; winners = stackalloc int[2];
        /// bool selected = random.TryNextWeightedSubsetByRace(weights, winners);
        /// </code>
        /// </example>
        public static bool TryNextWeightedSubsetByRace(
            this IRandom random,
            ReadOnlySpan<double> weights,
            Span<int> destination
        )
        {
            int winnerCount = destination.Length;
            Span<double> stackScores =
                winnerCount <= MaxStackWeightedScoreCount
                    ? stackalloc double[winnerCount]
                    : default;
            if (!stackScores.IsEmpty || winnerCount == 0)
            {
                return random.TryNextWeightedSubsetByRace(weights, destination, stackScores);
            }

            using PooledArray<double> scoreLease = SystemArrayPool<double>.Get(
                winnerCount,
                out double[] scoreBuffer
            );
            return random.TryNextWeightedSubsetByRace(
                weights,
                destination,
                scoreBuffer.AsSpan(0, winnerCount)
            );
        }

        /// <summary>Tries to select weighted indices without replacement using caller-owned scratch.</summary>
        /// <param name="random">The generator that supplies one draw per weight.</param>
        /// <param name="weights">Finite weights. Non-positive weights consume a draw but cannot win.</param>
        /// <param name="destination">Receives winners in ascending clock order.</param>
        /// <param name="scoreScratch">
        /// Scratch for at least <paramref name="destination" />.Length scores. The weights,
        /// destination, and scratch spans must be pairwise non-overlapping, including when backed by
        /// differently typed views of the same bytes.
        /// </param>
        /// <returns>
        /// <see langword="true" /> when every destination slot receives a winner; otherwise
        /// <see langword="false" /> without changing <paramref name="destination" />.
        /// </returns>
        /// <remarks>
        /// The method does not allocate or retain either span. Selection takes O(n log k) time for
        /// n weights and k winners. An empty destination succeeds without reading the weights.
        /// </remarks>
        /// <example>
        /// <code>
        /// ReadOnlySpan&lt;double&gt; weights = stackalloc double[] { 1d, 2d, 3d };
        /// Span&lt;int&gt; winners = stackalloc int[2];
        /// Span&lt;double&gt; scratch = stackalloc double[2];
        /// bool selected = random.TryNextWeightedSubsetByRace(weights, winners, scratch);
        /// </code>
        /// </example>
        public static bool TryNextWeightedSubsetByRace(
            this IRandom random,
            ReadOnlySpan<double> weights,
            Span<int> destination,
            Span<double> scoreScratch
        )
        {
            if (random == null)
            {
                return false;
            }

            if (destination.IsEmpty)
            {
                return true;
            }

            ReadOnlySpan<byte> weightBytes = MemoryMarshal.AsBytes(weights);
            Span<byte> destinationBytes = MemoryMarshal.AsBytes(destination);
            Span<byte> scoreBytes = MemoryMarshal.AsBytes(scoreScratch);
            if (
                scoreScratch.Length < destination.Length
                || weightBytes.Overlaps(destinationBytes)
                || weightBytes.Overlaps(scoreBytes)
                || destinationBytes.Overlaps(scoreBytes)
                || !TryValidateRaceWeights(random, weights, out int positiveCount)
                || positiveCount < destination.Length
            )
            {
                return false;
            }

            int selectedCount = 0;
            for (int weightIndex = 0; weightIndex < weights.Length; weightIndex++)
            {
                double score = RaceScore(random.NextDouble(), weights[weightIndex]);
                if (!(0d < weights[weightIndex]))
                {
                    continue;
                }

                if (selectedCount < destination.Length)
                {
                    scoreScratch[selectedCount] = score;
                    destination[selectedCount] = weightIndex;
                    SiftWeightedRaceUp(scoreScratch, destination, selectedCount);
                    selectedCount++;
                    continue;
                }

                if (!IsWeightedRaceEntryBefore(score, weightIndex, scoreScratch[0], destination[0]))
                {
                    continue;
                }

                scoreScratch[0] = score;
                destination[0] = weightIndex;
                SiftWeightedRaceDown(scoreScratch, destination, 0, selectedCount);
            }

            for (int endIndex = selectedCount - 1; 0 < endIndex; endIndex--)
            {
                SwapWeightedRaceEntries(scoreScratch, destination, 0, endIndex);
                SiftWeightedRaceDown(scoreScratch, destination, 0, endIndex);
            }

            return selectedCount == destination.Length;
        }

        private static double RaceScore(double uniform, double weight)
        {
            if (!(0d < weight))
            {
                return double.PositiveInfinity;
            }

            double exponential = -Math.Log(1d - uniform);
            return Math.Log(exponential) - Math.Log(weight);
        }

        private static bool IsWeightedRaceEntryBefore(
            double leftScore,
            int leftIndex,
            double rightScore,
            int rightIndex
        )
        {
            return leftScore < rightScore || (leftScore == rightScore && leftIndex < rightIndex);
        }

        private static void SiftWeightedRaceUp(
            Span<double> scores,
            Span<int> indices,
            int entryIndex
        )
        {
            while (0 < entryIndex)
            {
                int parentIndex = (entryIndex - 1) / 2;
                if (
                    !IsWeightedRaceEntryBefore(
                        scores[parentIndex],
                        indices[parentIndex],
                        scores[entryIndex],
                        indices[entryIndex]
                    )
                )
                {
                    return;
                }

                SwapWeightedRaceEntries(scores, indices, parentIndex, entryIndex);
                entryIndex = parentIndex;
            }
        }

        private static void SiftWeightedRaceDown(
            Span<double> scores,
            Span<int> indices,
            int entryIndex,
            int entryCount
        )
        {
            while (true)
            {
                int leftIndex = entryIndex * 2 + 1;
                if (entryCount <= leftIndex)
                {
                    return;
                }

                int worseIndex = leftIndex;
                int rightIndex = leftIndex + 1;
                if (
                    rightIndex < entryCount
                    && IsWeightedRaceEntryBefore(
                        scores[leftIndex],
                        indices[leftIndex],
                        scores[rightIndex],
                        indices[rightIndex]
                    )
                )
                {
                    worseIndex = rightIndex;
                }

                if (
                    !IsWeightedRaceEntryBefore(
                        scores[entryIndex],
                        indices[entryIndex],
                        scores[worseIndex],
                        indices[worseIndex]
                    )
                )
                {
                    return;
                }

                SwapWeightedRaceEntries(scores, indices, entryIndex, worseIndex);
                entryIndex = worseIndex;
            }
        }

        private static void SwapWeightedRaceEntries(
            Span<double> scores,
            Span<int> indices,
            int leftIndex,
            int rightIndex
        )
        {
            double score = scores[leftIndex];
            scores[leftIndex] = scores[rightIndex];
            scores[rightIndex] = score;

            int index = indices[leftIndex];
            indices[leftIndex] = indices[rightIndex];
            indices[rightIndex] = index;
        }

        private static bool TryGetWeightScale(
            IRandom random,
            ReadOnlySpan<double> weights,
            out double scale
        )
        {
            if (random == null || weights.IsEmpty)
            {
                scale = default;
                return false;
            }

            double maxWeight = 0d;
            foreach (double weight in weights)
            {
                if (!double.IsFinite(weight))
                {
                    scale = default;
                    return false;
                }

                if (maxWeight < weight)
                {
                    maxWeight = weight;
                }
            }

            scale = maxWeight;
            return 0d < scale;
        }

        private static bool TryValidateRaceWeights(
            IRandom random,
            ReadOnlySpan<double> weights,
            out int positiveCount
        )
        {
            if (random == null || weights.IsEmpty)
            {
                positiveCount = default;
                return false;
            }

            int count = 0;
            foreach (double weight in weights)
            {
                if (!double.IsFinite(weight))
                {
                    positiveCount = default;
                    return false;
                }

                if (0d < weight)
                {
                    count++;
                }
            }

            positiveCount = count;
            return 0 < positiveCount;
        }

        private static bool TryValidateRaceWeights(
            IRandom random,
            ReadOnlySpan<float> weights,
            out int positiveCount
        )
        {
            if (random == null || weights.IsEmpty)
            {
                positiveCount = default;
                return false;
            }

            int count = 0;
            foreach (float weight in weights)
            {
                if (!float.IsFinite(weight))
                {
                    positiveCount = default;
                    return false;
                }

                if (0f < weight)
                {
                    count++;
                }
            }

            positiveCount = count;
            return 0 < positiveCount;
        }

        private static T NextWeightedCore<T>(IRandom random, IReadOnlyList<(T, float)> items)
        {
            if (items.Count == 0)
            {
                throw new ArgumentException("Weighted collection cannot be empty", nameof(items));
            }

            float totalWeight = 0f;
            for (int i = 0; i < items.Count; ++i)
            {
                float weight = items[i].Item2;
                if (!(0f <= weight && weight <= float.MaxValue))
                {
                    throw new ArgumentException(
                        "Weights must be finite and nonnegative",
                        nameof(items)
                    );
                }

                totalWeight += weight;
            }

            if (!(0f < totalWeight && totalWeight <= float.MaxValue))
            {
                throw new ArgumentException(
                    "Total weight must be finite and greater than zero",
                    nameof(items)
                );
            }

            float randomValue = random.NextFloat(0f, totalWeight);
            float cumulative = 0f;

            for (int i = 0; i < items.Count; ++i)
            {
                (T item, float weight) = items[i];
                cumulative += weight;
                if (randomValue < cumulative)
                {
                    return item;
                }
            }

            // Fallback due to floating point precision
            return items[items.Count - 1].Item1;
        }
    }
}
