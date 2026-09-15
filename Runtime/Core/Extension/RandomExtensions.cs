// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using Random;
    using UnityEngine;

    /// <summary>
    /// Provides extension methods for generating random Unity types (vectors, quaternions, colors) using the IRandom interface.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe if the IRandom implementation provided is thread-safe.
    /// Performance: Most methods are O(1). NextSubset is O(n) where n is the collection size.
    /// </remarks>
    public static partial class RandomExtensions
    {
        /// <summary>The largest supported uniform sample count for <see cref="NextIntSkewed"/>.</summary>
        public const int MaxSkewedIterations = 1024;

        private const int SkewedTargetWeight = 2;

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty.
        /// </summary>
        /// <remarks>
        /// This is for an <b>authored</b> range -- two serialized fields a designer fills in -- not a
        /// computed one. Collapsing both ends onto the same value is how an inspector asks for "no
        /// spread", so the natural way to turn a spread off is exactly the input the strict overload
        /// rejects. That overload stays the default, because a computed range that inverts is a bug
        /// worth surfacing; these draws usually sit inside a coroutine or a periodic tick, where an
        /// exception ends the loop permanently and the system it drove simply stops existing.
        /// <para>
        /// The answer is the <b>low</b> bound rather than zero: a collapsed range is still a range,
        /// so <c>3 .. 3</c> means three, not nothing. A symmetric scatter whose collapse genuinely
        /// is zero is a different shape and keeps its own guard.
        /// </para>
        /// </remarks>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static int NextIntInRange(this IRandom random, int low, int high)
        {
            if (random == null || high <= low)
            {
                return low;
            }

            return random.Next(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static uint NextUintInRange(this IRandom random, uint low, uint high)
        {
            if (random == null || high <= low)
            {
                return low;
            }

            return random.NextUint(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static short NextShortInRange(this IRandom random, short low, short high)
        {
            if (random == null || high <= low)
            {
                return low;
            }

            return random.NextShort(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static byte NextByteInRange(this IRandom random, byte low, byte high)
        {
            if (random == null || high <= low)
            {
                return low;
            }

            return random.NextByte(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static long NextLongInRange(this IRandom random, long low, long high)
        {
            if (random == null || high <= low)
            {
                return low;
            }

            return random.NextLong(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static ulong NextUlongInRange(this IRandom random, ulong low, ulong high)
        {
            if (random == null || high <= low)
            {
                return low;
            }

            return random.NextUlong(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty or either bound is not a number. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static float NextFloatInRange(this IRandom random, float low, float high)
        {
            // A negated strict comparison also rejects NaN bounds.
            if (random == null || !(low < high))
            {
                return low;
            }

            return random.NextFloat(low, high);
        }

        /// <summary>
        /// Draws a value in <c>[low, high)</c>, returning <paramref name="low"/> instead of throwing
        /// when the range is empty or either bound is not a number. See <see cref="NextIntInRange"/> for why this is not the default.
        /// </summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="low"/>.</param>
        /// <param name="low">Inclusive lower bound, and the answer whenever the range is empty.</param>
        /// <param name="high">Exclusive upper bound.</param>
        public static double NextDoubleInRange(this IRandom random, double low, double high)
        {
            // A negated strict comparison also rejects NaN bounds.
            if (random == null || !(low < high))
            {
                return low;
            }

            return random.NextDouble(low, high);
        }

        /// <summary>Draws an integer biased toward a target within the requested bounds.</summary>
        /// <param name="random">Generator to draw from. A null generator yields <paramref name="min"/>.</param>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Inclusive upper clamp.</param>
        /// <param name="target">The value that contributes the weight of two uniform draws.</param>
        /// <param name="iterations">
        /// The number of uniform draws to average with the target, from zero through
        /// <see cref="MaxSkewedIterations"/>.
        /// </param>
        /// <returns>
        /// The truncated weighted mean in <c>[min, max]</c>. Invalid or float-indistinguishable
        /// bounds, an iteration count outside the supported range, or a target that is not a
        /// number yield <paramref name="min"/>.
        /// </returns>
        public static int NextIntSkewed(
            this IRandom random,
            int min,
            int max,
            float target,
            int iterations = 3
        )
        {
            if (
                random == null
                || max <= min
                || iterations < 0
                || MaxSkewedIterations < iterations
                || float.IsNaN(target)
            )
            {
                return min;
            }

            if (!((float)min < (float)max))
            {
                return min;
            }

            if (iterations == 0)
            {
                return ClampSkewedInteger(target, min, max);
            }

            float sum = 0f;
            for (int index = 0; index < iterations; index++)
            {
                sum += random.NextFloat(min, max);
            }

            sum += target * SkewedTargetWeight;
            float result = sum / (iterations + (float)SkewedTargetWeight);
            return ClampSkewedInteger(result, min, max);
        }

        /// <summary>
        /// Generates a random boolean with a specified probability of being true.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="probability">The probability [0, 1] that the result will be true.</param>
        /// <returns>True with probability 'probability', false otherwise.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if probability is NaN or not in [0, 1].</exception>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - single random float generation and comparison.
        /// Allocations: No heap allocations.
        /// Edge Cases: probability=0 always returns false, probability=1 always returns true.
        /// </remarks>
        public static bool NextBool(this IRandom random, float probability)
        {
            if (!(0f <= probability && probability <= 1f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(probability),
                    "Probability must be between 0 and 1"
                );
            }

            return random.NextFloat() < probability;
        }

        /// <summary>
        /// Generates a random sign (1 or -1) with equal probability.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <returns>Either 1 or -1, each with 50% probability.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - single random boolean generation.
        /// Allocations: No heap allocations.
        /// Edge Cases: None - always returns exactly 1 or -1.
        /// </remarks>
        public static int NextSign(this IRandom random)
        {
            return random.NextBool() ? 1 : -1;
        }

        /// <summary>
        /// Generates a random float centered around a value with specified variance.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="center">The center value of the range.</param>
        /// <param name="variance">The maximum deviation from center (can be positive or negative).</param>
        /// <returns>A random float in [center - variance, center + variance).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - arithmetic and single random generation.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative variance inverts the range. Zero variance returns center.
        /// </remarks>
        public static float NextFloatAround(this IRandom random, float center, float variance)
        {
            if (variance <= 0f)
            {
                return center;
            }

            return random.NextFloat(center - variance, center + variance);
        }

        /// <summary>
        /// Generates a random integer centered around a value with specified variance.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="center">The center value of the range.</param>
        /// <param name="variance">The maximum deviation from center (inclusive).</param>
        /// <returns>A random integer in [center - variance, center + variance].</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - arithmetic and single random generation.
        /// Allocations: No heap allocations.
        /// Edge Cases: Note the +1 adjustment makes upper bound inclusive. Negative variance inverts range.
        /// </remarks>
        public static int NextIntAround(this IRandom random, int center, int variance)
        {
            if (variance <= 0)
            {
                return center;
            }

            return random.Next(center - variance, center + variance + 1);
        }

        private static int ClampSkewedInteger(float value, int min, int max)
        {
            if (float.IsNaN(value) || value <= min)
            {
                return min;
            }

            if (max <= value)
            {
                return max;
            }

            return (int)value;
        }

        private static float NextSymmetricVariance(IRandom random, float variance)
        {
            if (float.IsNaN(variance) || float.IsInfinity(variance))
            {
                return 0f;
            }

            float magnitude = Mathf.Abs(variance);
            if (magnitude <= 0f)
            {
                return 0f;
            }

            return random.NextFloat(-magnitude, magnitude);
        }
    }
}
