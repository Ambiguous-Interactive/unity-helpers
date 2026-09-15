// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Portions of this file are independent implementations of published algorithms: uniform random
// rotations from Shoemake, "Uniform Random Rotations", Graphics Gems III; and uniform points on a
// sphere from Marsaglia's method. The designs are the original authors'.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using Helper;
    using Random;
    using UnityEngine;

    public static partial class RandomExtensions
    {
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
        /// Zero or nonfinite radius returns center without drawing.
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
            if (!(0f < radiusMagnitude && radiusMagnitude <= float.MaxValue))
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
                    || 1f < lengthSquared
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
        /// Edge Cases: A collapsed range (maxAmplitude at or below minAmplitude) throws, because this
        /// composes from <see cref="IRandom.Next(int, int)"/>, which refuses one. Use
        /// <see cref="RandomExtensions.NextIntInRange(IRandom, int, int)"/> per component to get the
        /// low bound back instead.
        /// </remarks>
        /// <exception cref="System.ArgumentException">maxAmplitude is at or below minAmplitude.</exception>
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
        /// Edge Cases: A component whose max is at or below its min throws, because this composes from
        /// <see cref="IRandom.Next(int, int)"/>, which refuses a collapsed range. Flattening a spawn box
        /// to a line is an authored case, so build it from
        /// <see cref="RandomExtensions.NextIntInRange(IRandom, int, int)"/> per component, which answers
        /// the low bound instead of raising.
        /// </remarks>
        /// <exception cref="System.ArgumentException">A component's max is at or below its min.</exception>
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
        /// Edge Cases: A collapsed range (maxAmplitude at or below minAmplitude) throws, because this
        /// composes from <see cref="IRandom.Next(int, int)"/>, which refuses one. Use
        /// <see cref="RandomExtensions.NextIntInRange(IRandom, int, int)"/> per component to get the
        /// low bound back instead.
        /// </remarks>
        /// <exception cref="System.ArgumentException">maxAmplitude is at or below minAmplitude.</exception>
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
        /// Edge Cases: A component whose max is at or below its min throws, because this composes from
        /// <see cref="IRandom.Next(int, int)"/>, which refuses a collapsed range. Flattening a spawn box
        /// to a line is an authored case, so build it from
        /// <see cref="RandomExtensions.NextIntInRange(IRandom, int, int)"/> per component, which answers
        /// the low bound instead of raising.
        /// </remarks>
        /// <exception cref="System.ArgumentException">A component's max is at or below its min.</exception>
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
        /// Edge Cases: Does not normalize the angle to [0, 360) - can return a negative value, or one
        /// above 360, if the range allows. A collapsed range (max at or below min) throws, because this
        /// composes from <see cref="IRandom.NextFloat(float, float)"/>, which refuses one. Pinning an
        /// angle to a fixed value is an authored case, so use
        /// <see cref="RandomExtensions.NextFloatInRange(IRandom, float, float)"/>, which answers the low
        /// bound instead of raising.
        /// </remarks>
        /// <exception cref="System.ArgumentException">max is at or below min.</exception>
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
        /// Edge Cases: Works with negative or inverted rectangles. A zero-width or zero-height rect
        /// returns that axis's min rather than raising - unlike the composed integer draws, this one
        /// guards each axis before it reaches <see cref="IRandom.NextFloat(float, float)"/>.
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
        /// Edge Cases: Degenerate (zero-volume) bounds return the center point rather than raising -
        /// unlike the composed integer draws, this one guards before it reaches
        /// <see cref="IRandom.NextFloat(float, float)"/>.
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
    }
}
