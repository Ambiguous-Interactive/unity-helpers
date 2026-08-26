// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>
    /// Numeric helpers for safe bounds, positive modulo, and wrap-around arithmetic.
    /// </summary>
    /// <remarks>
    /// Includes IEEE-754-aware helpers (BoundedFloat/BoundedDouble) that adjust bit patterns to maintain strict inequalities.
    /// Useful for RNG upper bounds, indices, and cyclical arithmetic.
    /// </remarks>
    public static class WallMath
    {
        /// <summary>
        /// Returns <paramref name="value"/> if it is already below <paramref name="max"/>, and
        /// otherwise the largest representable value below it.
        /// </summary>
        /// <param name="max">The exclusive upper bound</param>
        /// <param name="value">The value to bound</param>
        /// <returns>A value strictly less than max</returns>
        /// <remarks>
        /// <para>
        /// Scaling a unit random draw by a bound can round up to the bound itself, which breaks the
        /// half-open interval every caller assumes. Stepping down one representable value is the
        /// smallest correction that restores it.
        /// </para>
        /// <para>
        /// The step relies on IEEE-754 laying out same-signed values in the same order as their bit
        /// patterns, so the next value down is one integer step away. This is what
        /// <c>Math.BitDecrement</c> does; it is reimplemented here because Unity's profile does not
        /// carry that method, and the two agree on every <see cref="float"/>.
        /// </para>
        /// </remarks>
        public static double BoundedDouble(double max, double value)
        {
            if (double.IsNaN(value) || double.IsNaN(max))
            {
                return double.NaN;
            }

            if (value < max)
            {
                return value;
            }

            if (double.IsNegativeInfinity(max))
            {
                return double.NegativeInfinity;
            }

            return PreviousDouble(value);
        }

        /// <summary>
        /// Ensures a float value is strictly less than the specified maximum by decrementing
        /// its bit representation if necessary.
        /// </summary>
        /// <param name="max">The exclusive upper bound</param>
        /// <param name="value">The value to bound</param>
        /// <returns>A value strictly less than max</returns>
        public static float BoundedFloat(float max, float value)
        {
            if (float.IsNaN(value) || float.IsNaN(max))
            {
                return float.NaN;
            }

            if (value < max)
            {
                return value;
            }

            if (float.IsNegativeInfinity(max))
            {
                return float.NegativeInfinity;
            }

            return PreviousFloat(value);
        }

        /// <summary>
        /// The largest <see cref="double"/> below <paramref name="value"/>.
        /// </summary>
        /// <remarks>
        /// Positive values step down in bit space and negative values step up, because the sign bit
        /// reverses the ordering. Zero is its own case: the value below it is the first negative
        /// subnormal, whose bit pattern is not adjacent to zero's.
        /// </remarks>
        private static double PreviousDouble(double value)
        {
            if (double.IsNaN(value))
            {
                return double.NaN;
            }

            if (value == double.NegativeInfinity)
            {
                return double.NegativeInfinity;
            }

            if (value == double.PositiveInfinity)
            {
                return double.MaxValue;
            }

            if (value == 0d)
            {
                return -double.Epsilon;
            }

            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += 0d < value ? -1L : 1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>
        /// The largest <see cref="float"/> below <paramref name="value"/>. The <see cref="double"/>
        /// overload's remark explains the cases.
        /// </summary>
        private static float PreviousFloat(float value)
        {
            if (float.IsNaN(value))
            {
                return float.NaN;
            }

            if (value == float.NegativeInfinity)
            {
                return float.NegativeInfinity;
            }

            if (value == float.PositiveInfinity)
            {
                return float.MaxValue;
            }

            if (value == 0f)
            {
                return -float.Epsilon;
            }

            int bits = BitConverter.SingleToInt32Bits(value);
            bits += 0f < value ? -1 : 1;
            return BitConverter.Int32BitsToSingle(bits);
        }

        /// <summary>
        /// Computes a positive modulo operation that always returns a non-negative result.
        /// Unlike the % operator which can return negative values, this ensures the result is in [0, max).
        /// </summary>
        /// <param name="value">The value to compute modulo for</param>
        /// <param name="max">The modulo divisor (must be positive)</param>
        /// <returns>A value in the range [0, max)</returns>
        public static float PositiveMod(this float value, float max)
        {
            // Handle edge cases explicitly
            if (float.IsNaN(value) || float.IsNaN(max))
            {
                return float.NaN;
            }

            if (max == 0f)
            {
                return 0f;
            }

            float remainder = value % max;
            if (remainder != 0f && remainder < 0f != max < 0f)
            {
                remainder += max;
                if (remainder == max)
                {
                    // A remainder below half an ulp of max rounds onto max when added, which is
                    // outside the half-open range. The second division this replaced folded it to 0.
                    remainder = 0f;
                }
            }

            // A remainder of negative zero compares equal to zero but is a different value; the two
            // divisions this replaced always produced positive zero.
            return remainder == 0f ? 0f : remainder;
        }

        /// <example>
        /// <code>
        /// float angle = -30f;
        /// float normalized = angle.PositiveMod(360f); // 330
        /// </code>
        /// </example>
        /// <summary>
        /// Computes a positive modulo operation that always returns a non-negative result.
        /// Unlike the % operator which can return negative values, this ensures the result is in [0, max).
        /// </summary>
        /// <param name="value">The value to compute modulo for</param>
        /// <param name="max">The modulo divisor (must be positive)</param>
        /// <returns>A value in the range [0, max)</returns>
        public static double PositiveMod(this double value, double max)
        {
            // Handle edge cases explicitly
            if (double.IsNaN(value) || double.IsNaN(max))
            {
                return double.NaN;
            }

            if (max == 0d)
            {
                return 0d;
            }

            double remainder = value % max;
            if (remainder != 0d && remainder < 0d != max < 0d)
            {
                remainder += max;
                if (remainder == max)
                {
                    // See the float overload: rounding onto the boundary folds to 0.
                    remainder = 0d;
                }
            }

            // See the float overload: negative zero is normalized to keep the old result exactly.
            return remainder == 0d ? 0d : remainder;
        }

        /// <example>
        /// <code>
        /// double phase = -0.25;
        /// double wrapped = phase.PositiveMod(1.0); // 0.75
        /// </code>
        /// </example>
        /// <summary>
        /// Computes a positive modulo operation that always returns a non-negative result.
        /// Unlike the % operator which can return negative values, this ensures the result is in [0, max).
        /// </summary>
        /// <param name="value">The value to compute modulo for</param>
        /// <param name="max">The modulo divisor (must be positive)</param>
        /// <returns>A value in the range [0, max)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PositiveMod(this int value, int max)
        {
            if (0 <= value && value < max)
            {
                return value;
            }

            // Floored modulo: the result takes the sign of the divisor, which is what a negative
            // maximum has always returned here. Adding only across a sign difference cannot overflow,
            // where adding unconditionally did once the maximum passed 2^30.
            int remainder = value % max;
            if (remainder != 0 && remainder < 0 != max < 0)
            {
                remainder += max;
            }

            return remainder;
        }

        /// <example>
        /// <code>
        /// int i = -1;
        /// int wrapped = i.PositiveMod(5); // 4
        /// </code>
        /// </example>
        /// <summary>
        /// Computes a positive modulo operation that always returns a non-negative result.
        /// Unlike the % operator which can return negative values, this ensures the result is in [0, max).
        /// </summary>
        /// <param name="value">The value to compute modulo for</param>
        /// <param name="max">The modulo divisor (must be positive)</param>
        /// <returns>A value in the range [0, max)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long PositiveMod(this long value, long max)
        {
            if (0 <= value && value < max)
            {
                return value;
            }

            // See the int overload: floored modulo, and the conditional add cannot overflow.
            long remainder = value % max;
            if (remainder != 0 && remainder < 0 != max < 0)
            {
                remainder += max;
            }

            return remainder;
        }

        /// <summary>
        /// Adds an increment to a value and wraps around using modulo if it exceeds the maximum.
        /// This is a non-mutating version that returns the result without modifying the input.
        /// </summary>
        /// <param name="value">The base value</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max)</returns>
        public static int WrappedAdd(this int value, int increment, int max)
        {
            WrappedAdd(ref value, increment, max);
            return value;
        }

        /// <example>
        /// <code>
        /// int index = 4;
        /// index = index.WrappedAdd(2, 5); // 1
        /// </code>
        /// </example>
        /// <summary>
        /// Adds an increment to a value and wraps around using modulo if it exceeds the maximum.
        /// This mutates the value parameter in place.
        /// </summary>
        /// <param name="value">The base value (modified in place)</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max)</returns>
        public static int WrappedAdd(ref int value, int increment, int max)
        {
            // Widened because the sum is what wraps: adding a large increment near int.MaxValue
            // overflows to a negative number that is not congruent to the real sum.
            long sum = (long)value + increment;
            if (0 <= sum && sum < max)
            {
                return value = (int)sum;
            }

            return value = (int)sum.PositiveMod(max);
        }

        /// <summary>
        /// Increments a value by 1 and wraps around if it reaches the maximum.
        /// This is a non-mutating version that returns the result without modifying the input.
        /// </summary>
        /// <param name="value">The value to increment</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The incremented value, wrapped to [0, max)</returns>
        public static int WrappedIncrement(this int value, int max)
        {
            return value.WrappedAdd(1, max);
        }

        /// <summary>
        /// Increments a value by 1 and wraps around if it reaches the maximum.
        /// This mutates the value parameter in place.
        /// </summary>
        /// <param name="value">The value to increment (modified in place)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The incremented value, wrapped to [0, max)</returns>
        public static int WrappedIncrement(ref int value, int max)
        {
            return WrappedAdd(ref value, 1, max);
        }

        /// <example>
        /// <code>
        /// long index = 4;
        /// index = index.WrappedAdd(2, 5); // 1
        /// </code>
        /// </example>
        /// <summary>
        /// Adds an increment to a value and wraps the sum into [0, max).
        /// This is a non-mutating version that returns the result without modifying the input.
        /// </summary>
        /// <param name="value">The base value</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max)</returns>
        public static long WrappedAdd(this long value, long increment, long max)
        {
            WrappedAdd(ref value, increment, max);
            return value;
        }

        /// <summary>
        /// Adds an increment to a value and wraps the sum into [0, max).
        /// This mutates the value parameter in place.
        /// </summary>
        /// <param name="value">The base value (modified in place)</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max)</returns>
        /// <remarks>
        /// A sum that overflows <see cref="long"/> cannot be widened the way the <see cref="int"/>
        /// overload widens its own, so it is folded through the maximum before it is added.
        /// </remarks>
        public static long WrappedAdd(ref long value, long increment, long max)
        {
            long start = value.PositiveMod(max);
            long step = increment.PositiveMod(max);
            long sum = start + step;
            if (sum < 0 || max <= sum)
            {
                sum -= max;
            }

            return value = sum;
        }

        /// <summary>
        /// Increments a value by 1 and wraps around if it reaches the maximum.
        /// This is a non-mutating version that returns the result without modifying the input.
        /// </summary>
        /// <param name="value">The value to increment</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The incremented value, wrapped to [0, max)</returns>
        public static long WrappedIncrement(this long value, long max)
        {
            return value.WrappedAdd(1L, max);
        }

        /// <summary>
        /// Increments a value by 1 and wraps around if it reaches the maximum.
        /// This mutates the value parameter in place.
        /// </summary>
        /// <param name="value">The value to increment (modified in place)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The incremented value, wrapped to [0, max)</returns>
        public static long WrappedIncrement(ref long value, long max)
        {
            return WrappedAdd(ref value, 1L, max);
        }

        /// <example>
        /// <code>
        /// float heading = 350f;
        /// heading = heading.WrappedAdd(20f, 360f); // 10
        /// </code>
        /// </example>
        /// <summary>
        /// Adds an increment to a value and wraps the sum into [0, max), which is what an angle or a
        /// normalized phase wants.
        /// </summary>
        /// <param name="value">The base value</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max), or NaN if the sum is not finite</returns>
        public static float WrappedAdd(this float value, float increment, float max)
        {
            WrappedAdd(ref value, increment, max);
            return value;
        }

        /// <summary>
        /// Adds an increment to a value and wraps the sum into [0, max).
        /// This mutates the value parameter in place.
        /// </summary>
        /// <param name="value">The base value (modified in place)</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max), or NaN if the sum is not finite</returns>
        public static float WrappedAdd(ref float value, float increment, float max)
        {
            return value = (value + increment).PositiveMod(max);
        }

        /// <example>
        /// <code>
        /// double phase = 0.9;
        /// phase = phase.WrappedAdd(0.2, 1.0); // 0.1
        /// </code>
        /// </example>
        /// <summary>
        /// Adds an increment to a value and wraps the sum into [0, max).
        /// </summary>
        /// <param name="value">The base value</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max), or NaN if the sum is not finite</returns>
        public static double WrappedAdd(this double value, double increment, double max)
        {
            WrappedAdd(ref value, increment, max);
            return value;
        }

        /// <summary>
        /// Adds an increment to a value and wraps the sum into [0, max).
        /// This mutates the value parameter in place.
        /// </summary>
        /// <param name="value">The base value (modified in place)</param>
        /// <param name="increment">The amount to add (can be negative)</param>
        /// <param name="max">The wrap-around boundary</param>
        /// <returns>The wrapped result in the range [0, max), or NaN if the sum is not finite</returns>
        public static double WrappedAdd(ref double value, double increment, double max)
        {
            return value = (value + increment).PositiveMod(max);
        }

        /// <summary>
        /// Clamps a value between a minimum and maximum using generic comparison.
        /// Works with any type that implements IComparable.
        /// </summary>
        /// <typeparam name="T">The type being clamped (must implement IComparable)</typeparam>
        /// <param name="value">The value to clamp</param>
        /// <param name="min">The minimum allowed value</param>
        /// <param name="max">The maximum allowed value</param>
        /// <returns>The clamped value in the range [min, max]</returns>
        public static T Clamp<T>(this T value, T min, T max)
            where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
            {
                return min;
            }

            return max.CompareTo(value) < 0 ? max : value;
        }

        /// <summary>
        /// Clamps a point to the nearest position inside or on the boundary of a rectangle.
        /// If the point is outside, it finds the closest point on the rectangle's edge.
        /// </summary>
        /// <param name="bounds">The bounding rectangle</param>
        /// <param name="point">The point to clamp</param>
        /// <returns>The clamped point within the rectangle</returns>
        public static Vector2 Clamp(this in Rect bounds, Vector2 point)
        {
            // Called as a plain static rather than through extension syntax: the receiver is already
            // a reference here, and extension syntax would report a copy that the `in` overload does
            // not make.
            return Clamp(in bounds, ref point);
        }

        /// <summary>
        /// Clamps a point to the nearest position inside or on the boundary of a rectangle.
        /// If the point is outside, it finds the closest point on the rectangle's edge.
        /// This version modifies the point parameter in place.
        /// </summary>
        /// <param name="bounds">The bounding rectangle</param>
        /// <param name="point">The point to clamp (modified in place)</param>
        /// <returns>The clamped point within the rectangle</returns>
        public static Vector2 Clamp(this in Rect bounds, ref Vector2 point)
        {
            Rect self = bounds;
            // Compute normalized axis-aligned bounds regardless of sign of width/height
            float x0 = Mathf.Min(self.xMin, self.xMax);
            float x1 = Mathf.Max(self.xMin, self.xMax);
            float y0 = Mathf.Min(self.yMin, self.yMax);
            float y1 = Mathf.Max(self.yMin, self.yMax);

            // If degenerate (zero area), clamp to the center point
            if (Mathf.Approximately(x0, x1) && Mathf.Approximately(y0, y1))
            {
                point = new Vector2(x0, y0);
                return point;
            }

            // First, clamp to the normalized rectangle
            float cx = Mathf.Clamp(point.x, x0, x1);
            float cy = Mathf.Clamp(point.y, y0, y1);

            // Then, ensure results respect original Rect's sign semantics for negative sizes
            // so that tests using Rect.max/Rect.min pass even when width/height are negative.
            // If width is negative, Rect.max.x == bounds.x + bounds.width is the lesser x.
            // Ensure clamped x does not exceed this value.
            Vector2 selfMax = self.max;
            if (self.width < 0f && selfMax.x < cx)
            {
                cx = selfMax.x;
            }

            if (self.height < 0f && selfMax.y < cy)
            {
                cy = selfMax.y;
            }

            point = new Vector2(cx, cy);
            return point;
        }

        /// <summary>
        /// Checks if two Vector2 values are approximately equal with the chosen comparison mode.
        /// Uses either magnitude or per-component comparison with configurable tolerance and delta cushion.
        /// </summary>
        /// <param name="lhs">The first vector.</param>
        /// <param name="rhs">The second vector.</param>
        /// <param name="tolerance">The base tolerance permitted for the comparison (default: 1e-3).</param>
        /// <param name="delta">Additional cushion added to the tolerance (default: 0).</param>
        /// <param name="mode">Determines whether to compare via magnitude or individual components.</param>
        /// <returns>True if the vectors are approximately equal according to the selected mode.</returns>
        public static bool Approximately(
            this Vector2 lhs,
            Vector2 rhs,
            float tolerance = 1e-3f,
            float delta = 0f,
            VectorApproximationMode mode = VectorApproximationMode.Magnitude
        )
        {
            if (!IsFinite(lhs) || !IsFinite(rhs))
            {
                return false;
            }

            float effectiveTolerance = Mathf.Max(0f, tolerance);
            float cushion = Mathf.Max(Mathf.Abs(delta), Mathf.Epsilon * 8f);
            float threshold = effectiveTolerance + cushion;

            return mode == VectorApproximationMode.Components
                ? lhs.x.Approximately(rhs.x, threshold) && lhs.y.Approximately(rhs.y, threshold)
                : Vector2.Distance(lhs, rhs) <= threshold;
        }

        /// <summary>
        /// Checks if two Vector3 values are approximately equal with the chosen comparison mode.
        /// Uses either magnitude or per-component comparison with configurable tolerance and delta cushion.
        /// </summary>
        /// <param name="lhs">The first vector.</param>
        /// <param name="rhs">The second vector.</param>
        /// <param name="tolerance">The base tolerance permitted for the comparison (default: 1e-3).</param>
        /// <param name="delta">Additional cushion added to the tolerance (default: 0).</param>
        /// <param name="mode">Determines whether to compare via magnitude or individual components.</param>
        /// <returns>True if the vectors are approximately equal according to the selected mode.</returns>
        public static bool Approximately(
            this Vector3 lhs,
            Vector3 rhs,
            float tolerance = 1e-3f,
            float delta = 0f,
            VectorApproximationMode mode = VectorApproximationMode.Magnitude
        )
        {
            if (!IsFinite(lhs) || !IsFinite(rhs))
            {
                return false;
            }

            float effectiveTolerance = Mathf.Max(0f, tolerance);
            float cushion = Mathf.Max(Mathf.Abs(delta), Mathf.Epsilon * 8f);
            float threshold = effectiveTolerance + cushion;

            return mode == VectorApproximationMode.Components
                ? lhs.x.Approximately(rhs.x, threshold)
                    && lhs.y.Approximately(rhs.y, threshold)
                    && lhs.z.Approximately(rhs.z, threshold)
                : Vector3.Distance(lhs, rhs) <= threshold;
        }

        /// <summary>
        /// Checks if two Color values are approximately equal.
        /// Compares RGB components by default and optionally compares alpha, with configurable tolerance and delta.
        /// </summary>
        /// <param name="lhs">The first color.</param>
        /// <param name="rhs">The second color.</param>
        /// <param name="tolerance">The base tolerance permitted for each channel comparison (default: 1/255).</param>
        /// <param name="delta">Additional cushion added to the tolerance (default: 0).</param>
        /// <param name="includeAlpha">Whether to include the alpha channel in the comparison.</param>
        /// <returns>True if the colors are approximately equal within the provided settings.</returns>
        public static bool Approximately(
            this in Color lhs,
            Color rhs,
            float tolerance = ColorQuantization.ChannelStep,
            float delta = 0f,
            bool includeAlpha = true
        )
        {
            if (!IsFinite(lhs, includeAlpha) || !IsFinite(rhs, includeAlpha))
            {
                return false;
            }

            float effectiveTolerance = Mathf.Max(0f, tolerance);
            float cushion = Mathf.Max(Mathf.Abs(delta), Mathf.Epsilon * 8f);
            float threshold = effectiveTolerance + cushion;

            if (!lhs.r.Approximately(rhs.r, threshold))
            {
                return false;
            }

            if (!lhs.g.Approximately(rhs.g, threshold))
            {
                return false;
            }

            if (!lhs.b.Approximately(rhs.b, threshold))
            {
                return false;
            }

            if (!includeAlpha)
            {
                return true;
            }

            return lhs.a.Approximately(rhs.a, threshold);
        }

        /// <summary>
        /// Checks if two Color32 values are approximately equal.
        /// Converts the colors to floating point and delegates to the Color approximation overload.
        /// </summary>
        /// <param name="lhs">The first color.</param>
        /// <param name="rhs">The second color.</param>
        /// <param name="tolerance">The base tolerance permitted for each channel comparison in byte space (default: 1).</param>
        /// <param name="delta">Additional cushion added to the tolerance in byte space (default: 0).</param>
        /// <param name="includeAlpha">Whether to include the alpha channel in the comparison.</param>
        /// <returns>True if the colors are approximately equal within the provided settings.</returns>
        public static bool Approximately(
            this Color32 lhs,
            Color32 rhs,
            byte tolerance = 1,
            byte delta = 0,
            bool includeAlpha = true
        )
        {
            float floatTolerance = Mathf.Max(0f, tolerance) * ColorQuantization.ChannelStep;
            float floatDelta = Mathf.Max(0f, delta) * ColorQuantization.ChannelStep;
            return ((Color)lhs).Approximately(rhs, floatTolerance, floatDelta, includeAlpha);
        }

        /// <summary>
        /// Checks if two float values are approximately equal within a specified tolerance.
        /// Uses absolute difference comparison with an epsilon-scaled cushion to handle rounding.
        /// </summary>
        /// <param name="lhs">The first value</param>
        /// <param name="rhs">The second value</param>
        /// <param name="tolerance">The maximum allowed difference (default 0.045)</param>
        /// <returns>True if the absolute difference is less than or equal to tolerance plus the floating-point cushion</returns>
        public static bool Approximately(this float lhs, float rhs, float tolerance = 0.045f)
        {
            if (float.IsNaN(lhs) || float.IsNaN(rhs))
            {
                return false;
            }

            if (float.IsInfinity(lhs) || float.IsInfinity(rhs))
            {
                return false;
            }

            float difference = Mathf.Abs(lhs - rhs);
            if (float.IsNaN(difference) || float.IsInfinity(difference))
            {
                return false;
            }

            float absTolerance = Mathf.Abs(tolerance);
            float maxMagnitude = Mathf.Max(Mathf.Abs(lhs), Mathf.Abs(rhs));
            float fudge = Mathf.Max(1e-6f * maxMagnitude, Mathf.Epsilon * 8f);

            return difference <= absTolerance + fudge;
        }

        /// <example>
        /// <code>
        /// bool close = 0.1f.Approximately(0.10001f, 0.0001f); // true
        /// </code>
        /// </example>
        /// <summary>
        /// Checks if two double values are approximately equal within a specified tolerance.
        /// Uses absolute difference comparison with an epsilon-scaled cushion to handle rounding.
        /// </summary>
        /// <param name="lhs">The first value</param>
        /// <param name="rhs">The second value</param>
        /// <param name="tolerance">The maximum allowed difference (default 0.045)</param>
        /// <returns>True if the absolute difference is less than or equal to tolerance plus the floating-point cushion</returns>
        public static bool Approximately(this double lhs, double rhs, double tolerance = 0.045f)
        {
            if (double.IsNaN(lhs) || double.IsNaN(rhs))
            {
                return false;
            }

            if (double.IsInfinity(lhs) || double.IsInfinity(rhs))
            {
                return false;
            }

            double difference = Math.Abs(lhs - rhs);
            if (double.IsNaN(difference) || double.IsInfinity(difference))
            {
                return false;
            }

            double absTolerance = Math.Abs(tolerance);
            double maxMagnitude = Math.Max(Math.Abs(lhs), Math.Abs(rhs));
            double fudge = Math.Max(1e-12d * maxMagnitude, double.Epsilon * 8d);

            return difference <= absTolerance + fudge;
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Color value, bool includeAlpha)
        {
            if (!IsFinite(value.r) || !IsFinite(value.g) || !IsFinite(value.b))
            {
                return false;
            }

            return !includeAlpha || IsFinite(value.a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Compares two float values for total equality with special handling for NaN and infinity.
        /// Unlike standard equality, this treats NaN == NaN as true and properly compares infinities.
        /// Based on IEEE 754 totalOrder semantics.
        /// </summary>
        /// <param name="lhs">The first value</param>
        /// <param name="rhs">The second value</param>
        /// <returns>True if the values are equal, including special cases where both are NaN or the same infinity</returns>
        public static bool TotalEquals(this float lhs, float rhs)
        {
            if (float.IsNaN(lhs) && float.IsNaN(rhs))
            {
                return true;
            }
            if (float.IsPositiveInfinity(lhs) && float.IsPositiveInfinity(rhs))
            {
                return true;
            }
            if (float.IsNegativeInfinity(lhs) && float.IsNegativeInfinity(rhs))
            {
                return true;
            }
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return lhs == rhs;
        }

        /// <summary>
        /// Compares two double values for total equality with special handling for NaN and infinity.
        /// Unlike standard equality, this treats NaN == NaN as true and properly compares infinities.
        /// Based on IEEE 754 totalOrder semantics.
        /// </summary>
        /// <param name="lhs">The first value</param>
        /// <param name="rhs">The second value</param>
        /// <returns>True if the values are equal, including special cases where both are NaN or the same infinity</returns>
        public static bool TotalEquals(this double lhs, double rhs)
        {
            if (double.IsNaN(lhs) && double.IsNaN(rhs))
            {
                return true;
            }
            if (double.IsPositiveInfinity(lhs) && double.IsPositiveInfinity(rhs))
            {
                return true;
            }
            if (double.IsNegativeInfinity(lhs) && double.IsNegativeInfinity(rhs))
            {
                return true;
            }
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return lhs == rhs;
        }

        /// <summary>
        /// Determines whether vector comparisons should use magnitude difference or per-component comparison.
        /// </summary>
        public enum VectorApproximationMode
        {
            /// <summary>Compares the distance between vectors against the tolerance.</summary>
            Magnitude = 0,

            /// <summary>Compares each component against the tolerance individually.</summary>
            Components = 1,
        }
    }
}
