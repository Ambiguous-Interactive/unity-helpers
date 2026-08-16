// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Helper;
    using Random;
    using UnityEngine;
    using UnityEngine.Pool;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Provides extension methods for generating random Unity types (vectors, quaternions, colors) using the IRandom interface.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe if the IRandom implementation provided is thread-safe.
    /// Performance: Most methods are O(1). NextSubset is O(n) where n is the collection size.
    /// </remarks>
    public static class RandomExtensions
    {
        /// <summary>
        /// Returns an index sampled from the provided weights (unnormalized). Negative weights are treated as zero.
        /// </summary>
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
                if (weights[i] > 0)
                {
                    total += weights[i];
                }
            }
            if (total <= 0)
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
        /// Generates a random 2D vector with components in the range [-amplitude, amplitude].
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="amplitude">The maximum absolute value for each component (must be positive).</param>
        /// <returns>A Vector2 with x and y components each in [-amplitude, amplitude].</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - two random number generations.
        /// Allocations: No heap allocations (Vector2 is a value type).
        /// Edge Cases: Negative amplitude is normalized via absolute value. Zero amplitude returns Vector2.zero.
        /// </remarks>
        public static Vector2 NextVector2(this IRandom random, float amplitude)
        {
            float range = Mathf.Abs(amplitude);
            if (range <= 0f)
            {
                return Vector2.zero;
            }

            return random.NextVector2(-range, range);
        }

        /// <summary>
        /// Randomly selects an element from a collection with no exclusions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <returns>A randomly selected element from values.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(1) for lists/arrays, O(n) for general enumerables.
        /// Allocations: Zero allocation for this overload. Materializes non-list/collection enumerables to pooled list.
        /// Edge Cases: Empty values collection will cause NextOf to fail.
        /// </remarks>
        public static T NextOfExcept<T>(this IRandom random, IEnumerable<T> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return random.NextOf(values);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding one specified value.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exception1">The value to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not the excluded value.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n) where n is collection size.
        /// Allocations: Zero allocation - uses pooled collections internally.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(this IRandom random, IEnumerable<T> values, T exception1)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exception1);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exception1);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding two specified values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exception1">The first value to exclude from selection.</param>
        /// <param name="exception2">The second value to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not one of the excluded values.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n) where n is collection size.
        /// Allocations: Zero allocation - uses pooled collections internally.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            T exception1,
            T exception2
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exception1, exception2);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exception1, exception2);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding three specified values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exception1">The first value to exclude from selection.</param>
        /// <param name="exception2">The second value to exclude from selection.</param>
        /// <param name="exception3">The third value to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not one of the excluded values.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n) where n is collection size.
        /// Allocations: Zero allocation - uses pooled collections internally.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            T exception1,
            T exception2,
            T exception3
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exception1, exception2, exception3);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exception1, exception2, exception3);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding specified exception values via IEnumerable.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exceptions">An enumerable of values to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not in exceptions.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null. Null exceptions treated as empty.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n*k) where n is collection size and k is exceptions count.
        /// Allocations: Uses pooled collections internally. Does not allocate params array.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            IEnumerable<T> exceptions
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exceptions);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exceptions);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding specified exception values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exceptions">Values to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not in exceptions.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(k*n) worst case where k is number of exceptions and n is selection attempts.
        /// Allocations: This params overload allocates an array on each call. Prefer the specific 0-3 arg overloads
        /// or the IEnumerable overload for zero-allocation hot paths.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will cause NextOf to fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            params T[] exceptions
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exceptions);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exceptions);
        }

        private static T NextOfExceptCore<T>(IRandom random, IReadOnlyList<T> source, T exception1)
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!comparer.Equals(v, exception1))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exception1));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            T exception1,
            T exception2
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!comparer.Equals(v, exception1) && !comparer.Equals(v, exception2))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exception1));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            T exception1,
            T exception2,
            T exception3
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (
                    !comparer.Equals(v, exception1)
                    && !comparer.Equals(v, exception2)
                    && !comparer.Equals(v, exception3)
                )
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exception1));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            IEnumerable<T> exceptions
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            if (exceptions == null)
            {
                return random.NextOf(source);
            }

            using PooledResource<HashSet<T>> excludeLease = Buffers<T>.HashSet.Get(
                out HashSet<T> exclude
            );

            if (exceptions is IReadOnlyList<T> exceptionList)
            {
                for (int i = 0; i < exceptionList.Count; ++i)
                {
                    exclude.Add(exceptionList[i]);
                }
            }
            else
            {
                foreach (T exception in exceptions)
                {
                    exclude.Add(exception);
                }
            }

            if (exclude.Count == 0)
            {
                return random.NextOf(source);
            }

            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!exclude.Contains(v))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exceptions));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            T[] exceptions
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            if (exceptions == null || exceptions.Length == 0)
            {
                return random.NextOf(source);
            }

            using PooledResource<HashSet<T>> excludeLease = Buffers<T>.HashSet.Get(
                out HashSet<T> exclude
            );
            for (int i = 0; i < exceptions.Length; ++i)
            {
                exclude.Add(exceptions[i]);
            }

            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!exclude.Contains(v))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exceptions));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        /// <summary>
        /// Generates a random 2D vector with components in the specified range.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="minAmplitude">The minimum value for each component (inclusive).</param>
        /// <param name="maxAmplitude">The maximum value for each component (exclusive).</param>
        /// <returns>A Vector2 with x and y components each in [minAmplitude, maxAmplitude).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - two random number generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: If minAmplitude >= maxAmplitude, behavior depends on IRandom.NextFloat implementation.
        /// </remarks>
        public static Vector2 NextVector2(
            this IRandom random,
            float minAmplitude,
            float maxAmplitude
        )
        {
            float x = random.NextFloat(minAmplitude, maxAmplitude);
            float y = random.NextFloat(minAmplitude, maxAmplitude);
            return new Vector2(x, y);
        }

        /// <summary>
        /// Generates a random 2D point uniformly distributed within a circular area.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="range">The radius of the circle.</param>
        /// <param name="origin">The center of the circle (default: Vector2.zero).</param>
        /// <returns>A Vector2 uniformly distributed within the circle defined by origin and range.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null. Null origin defaults to Vector2.zero.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - uses square root for uniform distribution.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative range is normalized via absolute value. Zero range returns origin.
        /// </remarks>
        public static Vector2 NextVector2InRange(
            this IRandom random,
            float range,
            Vector2? origin = null
        )
        {
            float radius = Mathf.Abs(range);
            if (radius <= 0f)
            {
                return origin ?? Vector2.zero;
            }

            return Helpers.GetRandomPointInCircle(origin ?? Vector2.zero, radius, random);
        }

        /// <summary>
        /// Generates a random 3D vector with components in the range [-amplitude, amplitude].
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="amplitude">The maximum absolute value for each component.</param>
        /// <returns>A Vector3 with x, y, and z components each in [-amplitude, amplitude].</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three random number generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative amplitude is normalized via absolute value. Zero amplitude returns Vector3.zero.
        /// </remarks>
        public static Vector3 NextVector3(this IRandom random, float amplitude)
        {
            float range = Mathf.Abs(amplitude);
            if (range <= 0f)
            {
                return Vector3.zero;
            }

            return random.NextVector3(-range, range);
        }

        /// <summary>
        /// Generates a random 3D vector with components in the specified range.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="minAmplitude">The minimum value for each component (inclusive).</param>
        /// <param name="maxAmplitude">The maximum value for each component (exclusive).</param>
        /// <returns>A Vector3 with x, y, and z components each in [minAmplitude, maxAmplitude).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three random number generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: If minAmplitude >= maxAmplitude, behavior depends on IRandom.NextFloat implementation.
        /// </remarks>
        public static Vector3 NextVector3(
            this IRandom random,
            float minAmplitude,
            float maxAmplitude
        )
        {
            float z = random.NextFloat(minAmplitude, maxAmplitude);
            Vector3 result = random.NextVector2(minAmplitude, maxAmplitude);
            result.z = z;
            return result;
        }

        /// <summary>
        /// Generates a random 3D point uniformly distributed within a spherical volume.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="range">The radius of the sphere.</param>
        /// <param name="origin">The center of the sphere (default: Vector3.zero).</param>
        /// <returns>A Vector3 uniformly distributed within the sphere defined by origin and range.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null. Null origin defaults to Vector3.zero.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - uses cube root for uniform distribution.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative range is normalized via absolute value. Zero range returns origin.
        /// </remarks>
        public static Vector3 NextVector3InRange(
            this IRandom random,
            float range,
            Vector3? origin = null
        )
        {
            float radius = Mathf.Abs(range);
            if (radius <= 0f)
            {
                return origin ?? Vector3.zero;
            }

            return Helpers.GetRandomPointInSphere(origin ?? Vector3.zero, radius, random);
        }

        /// <summary>
        /// Generates a random 3D point uniformly distributed on the surface of a sphere using Marsaglia's method.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="center">The center of the sphere (default: Vector3.zero).</param>
        /// <returns>A Vector3 on the surface of the sphere with exact distance 'radius' from center.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null. Null center defaults to Vector3.zero.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) average case - Marsaglia rejection sampling averages ~1.3 iterations. Uses square root.
        /// Allocations: No heap allocations.
        /// Edge Cases: Very small radius (near zero) works correctly. Negative radius is treated as its absolute value.
        /// </remarks>
        public static Vector3 NextVector3OnSphere(
            this IRandom random,
            float radius,
            Vector3? center = null
        )
        {
            const int MaxAttempts = 128;
            const float MinLengthSquared = 0.0001f;
            float radiusMagnitude = Mathf.Abs(radius);
            if (radiusMagnitude <= 0f)
            {
                return center ?? Vector3.zero;
            }

            for (int attempt = 0; attempt < MaxAttempts; ++attempt)
            {
                float x = random.NextFloat(-1f, 1f);
                float y = random.NextFloat(-1f, 1f);
                float z = random.NextFloat(-1f, 1f);
                float lengthSquared = x * x + y * y + z * z;
                if (
                    !float.IsFinite(lengthSquared)
                    || lengthSquared > 1f
                    || lengthSquared < MinLengthSquared
                )
                {
                    continue;
                }

                float invLength = radiusMagnitude / Mathf.Sqrt(lengthSquared);
                Vector3 sampled = new(x * invLength, y * invLength, z * invLength);
                return center.HasValue ? sampled + center.Value : sampled;
            }

            Vector3 fallback = new(radiusMagnitude, 0f, 0f);
            return center.HasValue ? fallback + center.Value : fallback;
        }

        /// <summary>
        /// Generates a random 3D point uniformly distributed within a spherical volume.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="radius">The radius of the sphere.</param>
        /// <param name="center">The center of the sphere (default: Vector3.zero).</param>
        /// <returns>A Vector3 uniformly distributed within the sphere defined by center and radius.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null. Null center defaults to Vector3.zero.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - uses cube root for uniform volumetric distribution.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative radius is treated as its absolute value. Zero radius returns center.
        /// </remarks>
        public static Vector3 NextVector3InSphere(
            this IRandom random,
            float radius,
            Vector3? center = null
        )
        {
            return Helpers.GetRandomPointInSphere(center ?? Vector3.zero, radius, random);
        }

        /// <summary>
        /// Generates a uniformly distributed random rotation quaternion using Shoemake's algorithm.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <returns>A uniformly distributed rotation quaternion (all orientations equally likely).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - involves square roots and trigonometric functions.
        /// Allocations: No heap allocations.
        /// Edge Cases: Produces normalized quaternions. Algorithm based on Shoemake, "Uniform Random Rotations", Graphics Gems III.
        /// </remarks>
        public static Quaternion NextQuaternion(this IRandom random)
        {
            // Uniform random rotation using Shoemake's algorithm
            float u1 = Helpers.ClampUnitInterval(random.NextFloat());
            float u2 = Helpers.ClampUnitInterval(random.NextFloat());
            float u3 = Helpers.ClampUnitInterval(random.NextFloat());

            float sqrt1MinusU1 = Mathf.Sqrt(1f - u1);
            float sqrtU1 = Mathf.Sqrt(u1);

            float twoPiU2 = 2f * Mathf.PI * u2;
            float twoPiU3 = 2f * Mathf.PI * u3;

            return new Quaternion(
                sqrt1MinusU1 * Mathf.Sin(twoPiU2),
                sqrt1MinusU1 * Mathf.Cos(twoPiU2),
                sqrtU1 * Mathf.Sin(twoPiU3),
                sqrtU1 * Mathf.Cos(twoPiU3)
            );
        }

        /// <summary>
        /// Generates a random rotation around a specified axis within an angle range.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="axis">The axis to rotate around (will be normalized).</param>
        /// <param name="minAngle">The minimum rotation angle in degrees (inclusive).</param>
        /// <param name="maxAngle">The maximum rotation angle in degrees (exclusive).</param>
        /// <returns>A quaternion representing a rotation around axis by a random angle in [minAngle, maxAngle).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - involves vector normalization and quaternion construction.
        /// Allocations: No heap allocations.
        /// Edge Cases: Zero-length axis will produce undefined results from normalization.
        /// </remarks>
        public static Quaternion NextQuaternionAxisAngle(
            this IRandom random,
            Vector3 axis,
            float minAngle,
            float maxAngle
        )
        {
            float angle = random.NextFloat(minAngle, maxAngle);
            return Quaternion.AngleAxis(angle, axis.normalized);
        }

        /// <summary>
        /// Generates a random rotation that would make an object "look" in a random direction.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <returns>A quaternion representing a look rotation toward a random 3D direction.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - involves sphere sampling and look rotation calculation.
        /// Allocations: No heap allocations.
        /// Edge Cases: The "up" direction for LookRotation is always Vector3.up, which may cause issues near poles.
        /// </remarks>
        public static Quaternion NextQuaternionLookRotation(this IRandom random)
        {
            Vector3 direction = random.NextDirection3D();
            return Quaternion.LookRotation(direction);
        }

        /// <summary>
        /// Generates a random color with RGB components uniformly distributed in [0, 1].
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="randomAlpha">If true, alpha is random [0, 1); if false, alpha is 1.0 (opaque).</param>
        /// <returns>A random Color with all randomized components in [0, 1).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three or four random float generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: The randomized range is half-open. Each channel is drawn from
        /// <see cref="IRandom.NextFloat()"/>, which yields one of the 2^24 values
        /// <c>k / 2^24</c>, so 0 is reachable and exactly 1 is not.
        /// </remarks>
        public static Color NextColor(this IRandom random, bool randomAlpha = false)
        {
            float r = random.NextFloat();
            float g = random.NextFloat();
            float b = random.NextFloat();
            float a = randomAlpha ? random.NextFloat() : 1f;
            return new Color(r, g, b, a);
        }

        /// <summary>
        /// Generates a random color within a specified variance range from a base color in HSV space.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="baseColor">The base color to vary from.</param>
        /// <param name="hueVariance">The maximum hue deviation (0-1 scale, wraps around).</param>
        /// <param name="saturationVariance">The maximum saturation deviation (clamped to [0, 1]).</param>
        /// <param name="valueVariance">The maximum value/brightness deviation (clamped to [0, 1]).</param>
        /// <returns>A color randomly varied from baseColor within the specified HSV ranges, carrying the alpha of <paramref name="baseColor"/>.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - involves RGB-to-HSV conversion, random generation, and HSV-to-RGB conversion.
        /// Allocations: No heap allocations.
        /// Edge Cases: Hue wraps around at boundaries (0 and 1 are adjacent). Saturation and value are clamped
        /// to [0, 1], so an HDR base color returns with its intensity clamped. A variance of zero pins that
        /// channel to the base color rather than throwing, a negative variance is read as its magnitude, and a
        /// variance that is not a finite number is read as zero.
        /// </remarks>
        public static Color NextColorInRange(
            this IRandom random,
            Color baseColor,
            float hueVariance,
            float saturationVariance,
            float valueVariance
        )
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);

            h = Mathf.Repeat(h + NextSymmetricVariance(random, hueVariance), 1f);
            s = Mathf.Clamp01(s + NextSymmetricVariance(random, saturationVariance));
            v = Mathf.Clamp01(v + NextSymmetricVariance(random, valueVariance));

            Color varied = Color.HSVToRGB(h, s, v, true);
            // HSVToRGB builds from Color.white, so the base color's alpha would otherwise be lost.
            varied.a = baseColor.a;
            return varied;
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

        /// <summary>
        /// Generates a random 32-bit color with RGB components uniformly distributed in [0, 255].
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="randomAlpha">If true, alpha is random [0, 255]; if false, alpha is 255 (opaque).</param>
        /// <returns>A random Color32 with byte-precision components.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three or four random byte generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: None - all byte values are valid.
        /// </remarks>
        public static Color32 NextColor32(this IRandom random, bool randomAlpha = false)
        {
            byte r = random.NextByte();
            byte g = random.NextByte();
            byte b = random.NextByte();
            byte a = randomAlpha ? random.NextByte() : (byte)255;
            return new Color32(r, g, b, a);
        }

        /// <summary>
        /// Generates a random 2D integer vector with components in the range [-amplitude, amplitude).
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="amplitude">The maximum absolute value for each component.</param>
        /// <returns>A Vector2Int with x and y components each in [-amplitude, amplitude).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - two random integer generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative amplitude is normalized via absolute value. Zero amplitude returns Vector2Int.zero.
        /// </remarks>
        public static Vector2Int NextVector2Int(this IRandom random, int amplitude)
        {
            int range = Mathf.Abs(amplitude);
            if (range == 0)
            {
                return Vector2Int.zero;
            }

            return random.NextVector2Int(-range, range);
        }

        /// <summary>
        /// Generates a random 2D integer vector with components in the specified range.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="minAmplitude">The minimum value for each component (inclusive).</param>
        /// <param name="maxAmplitude">The maximum value for each component (exclusive).</param>
        /// <returns>A Vector2Int with x and y components each in [minAmplitude, maxAmplitude).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - two random integer generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: If minAmplitude >= maxAmplitude, behavior depends on IRandom.Next implementation.
        /// </remarks>
        public static Vector2Int NextVector2Int(
            this IRandom random,
            int minAmplitude,
            int maxAmplitude
        )
        {
            int x = random.Next(minAmplitude, maxAmplitude);
            int y = random.Next(minAmplitude, maxAmplitude);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Generates a random 2D integer vector with components independently bounded by min and max vectors.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="min">The minimum bounds (inclusive) for each component.</param>
        /// <param name="max">The maximum bounds (exclusive) for each component.</param>
        /// <returns>A Vector2Int with x in [min.x, max.x) and y in [min.y, max.y).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - two random integer generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: If any min component >= corresponding max component, behavior depends on IRandom.Next.
        /// </remarks>
        public static Vector2Int NextVector2Int(this IRandom random, Vector2Int min, Vector2Int max)
        {
            int x = random.Next(min.x, max.x);
            int y = random.Next(min.y, max.y);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Generates a random 3D integer vector with components in the range [-amplitude, amplitude).
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="amplitude">The maximum absolute value for each component.</param>
        /// <returns>A Vector3Int with x, y, and z components each in [-amplitude, amplitude).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three random integer generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: Negative amplitude is normalized via absolute value. Zero amplitude returns Vector3Int.zero.
        /// </remarks>
        public static Vector3Int NextVector3Int(this IRandom random, int amplitude)
        {
            int range = Mathf.Abs(amplitude);
            if (range == 0)
            {
                return Vector3Int.zero;
            }

            return random.NextVector3Int(-range, range);
        }

        /// <summary>
        /// Generates a random 3D integer vector with components in the specified range.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="minAmplitude">The minimum value for each component (inclusive).</param>
        /// <param name="maxAmplitude">The maximum value for each component (exclusive).</param>
        /// <returns>A Vector3Int with x, y, and z components each in [minAmplitude, maxAmplitude).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three random integer generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: If minAmplitude >= maxAmplitude, behavior depends on IRandom.Next implementation.
        /// </remarks>
        public static Vector3Int NextVector3Int(
            this IRandom random,
            int minAmplitude,
            int maxAmplitude
        )
        {
            int x = random.Next(minAmplitude, maxAmplitude);
            int y = random.Next(minAmplitude, maxAmplitude);
            int z = random.Next(minAmplitude, maxAmplitude);
            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Generates a random 3D integer vector with components independently bounded by min and max vectors.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="min">The minimum bounds (inclusive) for each component.</param>
        /// <param name="max">The maximum bounds (exclusive) for each component.</param>
        /// <returns>A Vector3Int with components independently ranged: x in [min.x, max.x), y in [min.y, max.y), z in [min.z, max.z).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three random integer generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: If any min component >= corresponding max component, behavior depends on IRandom.Next.
        /// </remarks>
        public static Vector3Int NextVector3Int(this IRandom random, Vector3Int min, Vector3Int max)
        {
            int x = random.Next(min.x, max.x);
            int y = random.Next(min.y, max.y);
            int z = random.Next(min.z, max.z);
            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Generates a uniformly distributed random 2D unit direction vector.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <returns>A normalized Vector2 pointing in a random direction (magnitude 1.0).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - one random generation and two trigonometric functions.
        /// Allocations: No heap allocations.
        /// Edge Cases: Always returns a normalized vector with magnitude 1.0.
        /// </remarks>
        public static Vector2 NextDirection2D(this IRandom random)
        {
            float angle = random.NextFloat(0f, 2f * Mathf.PI);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>
        /// Generates a uniformly distributed random 3D unit direction vector.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <returns>A normalized Vector3 pointing in a random direction (magnitude 1.0).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) average - uses Marsaglia sphere sampling.
        /// Allocations: No heap allocations.
        /// Edge Cases: Always returns a normalized vector with magnitude 1.0.
        /// </remarks>
        public static Vector3 NextDirection3D(this IRandom random)
        {
            return random.NextVector3OnSphere(1f, Vector3.zero);
        }

        /// <summary>
        /// Generates a random angle in degrees within the specified range.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="min">The minimum angle in degrees (inclusive, default: 0).</param>
        /// <param name="max">The maximum angle in degrees (exclusive, default: 360).</param>
        /// <returns>A random angle in degrees within [min, max).</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - single random float generation.
        /// Allocations: No heap allocations.
        /// Edge Cases: Does not normalize angle to [0, 360) - can return negative or >360 values if range allows.
        /// </remarks>
        public static float NextAngle(this IRandom random, float min = 0f, float max = 360f)
        {
            return random.NextFloat(min, max);
        }

        /// <summary>
        /// Generates a random 2D point uniformly distributed within a rectangle.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="rect">The bounding rectangle to generate points within.</param>
        /// <returns>A Vector2 uniformly distributed within the rect bounds.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - two random float generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: Works with negative or inverted rectangles. Zero-area rectangles return the min corner.
        /// </remarks>
        public static Vector2 NextVector2InRect(this IRandom random, Rect rect)
        {
            float xMin = Mathf.Min(rect.xMin, rect.xMax);
            float xMax = Mathf.Max(rect.xMin, rect.xMax);
            float yMin = Mathf.Min(rect.yMin, rect.yMax);
            float yMax = Mathf.Max(rect.yMin, rect.yMax);

            float x = xMax - xMin <= 0f ? xMin : random.NextFloat(xMin, xMax);
            float y = yMax - yMin <= 0f ? yMin : random.NextFloat(yMin, yMax);
            return new Vector2(x, y);
        }

        /// <summary>
        /// Generates a random 3D point uniformly distributed within an axis-aligned bounding box.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="bounds">The bounding box to generate points within.</param>
        /// <returns>A Vector3 uniformly distributed within the bounds.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - three random float generations.
        /// Allocations: No heap allocations.
        /// Edge Cases: Degenerate (zero-volume) bounds return the center point.
        /// </remarks>
        public static Vector3 NextVector3InBounds(this IRandom random, Bounds bounds)
        {
            Vector3 size = bounds.size;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                return bounds.center;
            }

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            float x = random.NextFloat(min.x, max.x);
            float y = random.NextFloat(min.y, max.y);
            float z = random.NextFloat(min.z, max.z);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Selects a random item from a weighted collection where each item has an associated probability weight.
        /// </summary>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="weighted">A collection of (item, weight) tuples where weight determines selection probability.</param>
        /// <returns>A randomly selected item, with probability proportional to its weight relative to total weight.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if collection is empty, any weight is negative, or total weight is zero or negative.
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

            // Materialize enumerable to pooled list - AddRange is preferred for performance:
            // it checks for ICollection<T> and pre-allocates, and uses Array.Copy for arrays/lists
            using PooledResource<List<(T, float)>> lease = Buffers<(T, float)>.List.Get(
                out List<(T, float)> materializedList
            );
            materializedList.AddRange(weighted);

            return NextWeightedCore(random, materializedList);
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
                if (weight < 0f)
                {
                    throw new ArgumentException("Weights cannot be negative", nameof(items));
                }

                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                throw new ArgumentException(
                    "Total weight must be greater than zero",
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

        /// <summary>
        /// Selects a random index from an array of weights, where each weight determines selection probability.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="weights">An array of weights where each element determines the probability of selecting that index.</param>
        /// <returns>A random index in [0, weights.Length), with probability proportional to weights[i] / totalWeight.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if weights is null/empty, any weight is negative, or total weight is zero or negative.
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
            for (int i = 0; i < weights.Length; ++i)
            {
                float weight = weights[i];
                if (weight < 0f)
                {
                    throw new ArgumentException("Weights cannot be negative", nameof(weights));
                }

                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                throw new ArgumentException(
                    "Total weight must be greater than zero",
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

        /// <summary>
        /// Generates a random boolean with a specified probability of being true.
        /// </summary>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="probability">The probability [0, 1] that the result will be true.</param>
        /// <returns>True with probability 'probability', false otherwise.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if probability is not in [0, 1].</exception>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe.
        /// Performance: O(1) - single random float generation and comparison.
        /// Allocations: No heap allocations.
        /// Edge Cases: probability=0 always returns false, probability=1 always returns true.
        /// </remarks>
        public static bool NextBool(this IRandom random, float probability)
        {
            if (probability < 0f || probability > 1f)
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

        /// <summary>
        /// Selects a random subset of items from a collection using reservoir sampling for uniform distribution.
        /// </summary>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="items">The collection to select from.</param>
        /// <param name="count">The number of items to select.</param>
        /// <returns>An array containing 'count' randomly selected items from the collection, with uniform probability.</returns>
        /// <exception cref="ArgumentNullException">Thrown if items is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative.</exception>
        /// <exception cref="ArgumentException">Thrown if count exceeds the number of items.</exception>
        /// <remarks>
        /// Null Handling: Throws ArgumentNullException if items is null. Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe and items is not modified during execution.
        /// Performance: O(n) where n is items.Count - uses reservoir sampling algorithm. Materializes non-list collections.
        /// Allocations: Uses pooled array for result (returned to pool when disposed). Materializes IEnumerable to array/list.
        /// Edge Cases: count=0 returns empty enumerable. Uses Algorithm R (reservoir sampling) for uniform selection probability.
        /// The returned array is pooled and will be returned to the pool - caller should not hold reference long-term.
        /// </remarks>
        public static IEnumerable<T> NextSubset<T>(
            this IRandom random,
            IEnumerable<T> items,
            int count
        )
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");
            }

            if (items is IReadOnlyList<T> itemsList)
            {
                if (count > itemsList.Count)
                {
                    throw new ArgumentException(
                        "Count cannot exceed the number of items",
                        nameof(count)
                    );
                }

                if (count == 0)
                {
                    return Array.Empty<T>();
                }

                return NextSubsetIterator(random, itemsList, count);
            }

            // Materialize enumerable to pooled list - AddRange is preferred for performance:
            // it checks for ICollection<T> and pre-allocates, and uses Array.Copy for arrays/lists
            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(items);

            if (count > materializedList.Count)
            {
                throw new ArgumentException(
                    "Count cannot exceed the number of items",
                    nameof(count)
                );
            }

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            return NextSubsetIterator(random, materializedList, count);
        }

        private static IEnumerable<T> NextSubsetIterator<T>(
            IRandom random,
            IReadOnlyList<T> items,
            int count
        )
        {
            using PooledArray<T> arrayBuffer = SystemArrayPool<T>.Get(count, out T[] result);

            for (int i = 0; i < count; ++i)
            {
                result[i] = items[i];
            }

            for (int i = count; i < items.Count; ++i)
            {
                int j = random.Next(0, i + 1);
                if (j < count)
                {
                    result[j] = items[i];
                }
            }

            for (int i = 0; i < count; ++i)
            {
                yield return result[i];
            }
        }
    }
}
