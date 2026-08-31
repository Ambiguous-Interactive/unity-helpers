// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;
    using UnityEngine;

    /// <summary>
    /// Unified random number generator interface implemented by all PRNGs in this package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generator never throws because of its own internal state. Bounded sampling rejects biased
    /// draws, and a source that rejects every draw an internal cap allows is stuck rather than
    /// unlucky -- the method then returns an in-range but biased value instead of hanging or
    /// throwing. Callers that must distinguish an exact draw from that degraded answer use the
    /// <c>Try</c> counterparts on <see cref="AbstractRandom"/>, which report <c>false</c> instead.
    /// Invalid arguments still throw, because those are the caller's state, not the generator's.
    /// </para>
    /// Serialization guidance:
    /// - JSON: Works out of the box using runtime type information in the serializer entry points.
    /// - Protobuf: You can serialize an IRandom instance (the runtime type is used), but when
    ///   deserializing to IRandom you must either register a concrete root via
    ///   <c>Serializer.RegisterProtobufRoot&lt;IRandom, ConcreteRandom&gt;()</c> or declare your model
    ///   field as <see cref="AbstractRandom"/>, which is annotated with [ProtoInclude] for all
    ///   known implementations.
    ///   Alternatively, use the overload <c>Serializer.ProtoDeserialize&lt;T&gt;(byte[], Type)</c> and
    ///   pass the concrete type.
    /// </remarks>
    public interface IRandom
    {
        /// <summary>
        /// Gets the internal state of the random number generator for serialization and restoration.
        /// </summary>
        RandomState InternalState { get; }

        /// <summary>
        /// Generates a random 32-bit signed integer.
        /// </summary>
        /// <returns>A number within the range [0, int.MaxValue).</returns>
        int Next();

        /// <summary>
        /// Generates a random integer within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        int Next(int max);

        /// <summary>
        /// Generates a random integer within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        int Next(int min, int max);

        /// <summary>
        /// Generates a random 32-bit unsigned integer.
        /// </summary>
        /// <returns>A number within the range [0, uint.MaxValue].</returns>
        uint NextUint();

        /// <summary>
        /// Generates a random unsigned integer within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        uint NextUint(uint max);

        /// <summary>
        /// Generates a random unsigned integer within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        uint NextUint(uint min, uint max);

        /// <summary>
        /// Generates a random 16-bit signed integer.
        /// </summary>
        /// <returns>A number within the range [0, short.MaxValue).</returns>
        short NextShort();

        /// <summary>
        /// Generates a random short within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        short NextShort(short max);

        /// <summary>
        /// Generates a random short within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        short NextShort(short min, short max);

        /// <summary>
        /// Generates a random 8-bit unsigned integer.
        /// </summary>
        /// <returns>A number within the range [0, byte.MaxValue).</returns>
        byte NextByte();

        /// <summary>
        /// Generates a random byte within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        byte NextByte(byte max);

        /// <summary>
        /// Generates a random byte within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        byte NextByte(byte min, byte max);

        /// <summary>
        /// Generates a random 64-bit signed integer.
        /// </summary>
        /// <returns>A number within the range [0, long.MaxValue).</returns>
        long NextLong();

        /// <summary>
        /// Generates a random long within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        long NextLong(long max);

        /// <summary>
        /// Generates a random long within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        long NextLong(long min, long max);

        /// <summary>
        /// Generates a random 64-bit unsigned integer.
        /// </summary>
        /// <returns>A number within the range [0, ulong.MaxValue].</returns>
        ulong NextUlong();

        /// <summary>
        /// Generates a random ulong within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        ulong NextUlong(ulong max);

        /// <summary>
        /// Generates a random ulong within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        ulong NextUlong(ulong min, ulong max);

        /// <summary>
        /// Generates a random boolean with equal probability for true and false.
        /// </summary>
        /// <returns>A random boolean value with equal probability for true and false.</returns>
        bool NextBool();

        /// <summary>
        /// Fills the specified buffer with random bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill with random bytes.</param>
        void NextBytes(byte[] buffer);

        /// <summary>
        /// Generates a random single-precision floating-point number.
        /// </summary>
        /// <returns>A number within the range [0, 1).</returns>
        float NextFloat();

        /// <summary>
        /// Generates a random float within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        float NextFloat(float max);

        /// <summary>
        /// Generates a random float within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        float NextFloat(float min, float max);

        /// <summary>
        /// Generates a random double-precision floating-point number.
        /// </summary>
        /// <returns>A number within the range [0, 1).</returns>
        double NextDouble();

        /// <summary>
        /// Generates a random double within the specified exclusive upper bound.
        /// </summary>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [0, max).</returns>
        double NextDouble(double max);

        /// <summary>
        /// Generates a random double within the specified range.
        /// </summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <returns>A number within the range [min, max).</returns>
        double NextDouble(double min, double max);

        /// <summary>
        /// Generates a random number from a Gaussian (normal) distribution.
        /// </summary>
        /// <param name="mean">The mean of the distribution.</param>
        /// <param name="stdDev">The standard deviation of the distribution.</param>
        /// <returns>A random number from the specified Gaussian distribution.</returns>
        double NextGaussian(double mean = 0, double stdDev = 1);

        /// <summary>
        /// Generates a random globally unique identifier (GUID).
        /// </summary>
        /// <returns>A randomly generated GUID.</returns>
        Guid NextGuid();

        /// <summary>
        /// Generates a random serializable GUID wrapper.
        /// </summary>
        /// <returns>A randomly generated WGuid.</returns>
        WGuid NextWGuid();

        /// <summary>
        /// Selects a random element from the specified enumerable.
        /// </summary>
        /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
        /// <param name="enumerable">The enumerable to select from.</param>
        /// <returns>A randomly selected element.</returns>
        /// <remarks>
        /// This method automatically dispatches to more efficient overloads when the enumerable
        /// is actually an <see cref="IReadOnlyList{T}"/> or <see cref="IReadOnlyCollection{T}"/>,
        /// using indexed access to avoid enumerator allocations. For hot paths, prefer passing
        /// the concrete type (<c>List&lt;T&gt;</c>, <c>T[]</c>) or interface
        /// (<c>IReadOnlyList&lt;T&gt;</c>) directly to skip the runtime type check.
        /// </remarks>
        T NextOf<T>(IEnumerable<T> enumerable);

        /// <summary>
        /// Selects a random element from the specified collection.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="collection">The collection to select from.</param>
        /// <returns>A randomly selected element.</returns>
        T NextOf<T>(IReadOnlyCollection<T> collection);

        /// <summary>
        /// Selects a random element from the specified list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to select from.</param>
        /// <returns>A randomly selected element.</returns>
        T NextOf<T>(IReadOnlyList<T> list);

        /// <summary>
        /// Selects a random element from the specified parameters.
        /// </summary>
        /// <typeparam name="T">The type of elements.</typeparam>
        /// <param name="elements">The elements to select from.</param>
        /// <returns>A randomly selected element.</returns>
        T NextOfParams<T>(params T[] elements);

        /// <summary>
        /// Selects a random value from the specified enum type.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <returns>A randomly selected enum value.</returns>
        T NextEnum<T>()
            where T : unmanaged, Enum;

        /// <summary>
        /// Selects a random value from the specified enum type, excluding the specified value.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="exception1">The enum value to exclude.</param>
        /// <returns>A randomly selected enum value that is not the excluded value.</returns>
        T NextEnumExcept<T>(T exception1)
            where T : unmanaged, Enum;

        /// <summary>
        /// Selects a random value from the specified enum type, excluding the specified values.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="exception1">The first enum value to exclude.</param>
        /// <param name="exception2">The second enum value to exclude.</param>
        /// <returns>A randomly selected enum value that is not one of the excluded values.</returns>
        T NextEnumExcept<T>(T exception1, T exception2)
            where T : unmanaged, Enum;

        /// <summary>
        /// Selects a random value from the specified enum type, excluding the specified values.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="exception1">The first enum value to exclude.</param>
        /// <param name="exception2">The second enum value to exclude.</param>
        /// <param name="exception3">The third enum value to exclude.</param>
        /// <returns>A randomly selected enum value that is not one of the excluded values.</returns>
        T NextEnumExcept<T>(T exception1, T exception2, T exception3)
            where T : unmanaged, Enum;

        /// <summary>
        /// Selects a random value from the specified enum type, excluding the specified values.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="exception1">The first enum value to exclude.</param>
        /// <param name="exception2">The second enum value to exclude.</param>
        /// <param name="exception3">The third enum value to exclude.</param>
        /// <param name="exception4">The fourth enum value to exclude.</param>
        /// <returns>A randomly selected enum value that is not one of the excluded values.</returns>
        T NextEnumExcept<T>(T exception1, T exception2, T exception3, T exception4)
            where T : unmanaged, Enum;

        /// <summary>
        /// Selects a random value from the specified enum type, excluding the specified values.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="exception1">The first enum value to exclude.</param>
        /// <param name="exception2">The second enum value to exclude.</param>
        /// <param name="exception3">The third enum value to exclude.</param>
        /// <param name="exception4">The fourth enum value to exclude.</param>
        /// <param name="exceptions">Additional enum values to exclude.</param>
        /// <returns>A randomly selected enum value that is not one of the excluded values.</returns>
        T NextEnumExcept<T>(
            T exception1,
            T exception2,
            T exception3,
            T exception4,
            params T[] exceptions
        )
            where T : unmanaged, Enum;

        /// <summary>
        /// Generates a 2D Perlin noise map with the specified parameters.
        /// </summary>
        /// <param name="noiseMap">The array to fill with noise values.</param>
        /// <param name="noise">Optional PerlinNoise instance to use for generation.</param>
        /// <param name="scale">The scale of the noise pattern.</param>
        /// <param name="octaves">The number of noise octaves to combine.</param>
        /// <param name="persistence">The amplitude multiplier for each successive octave.</param>
        /// <param name="lacunarity">The frequency multiplier for each successive octave.</param>
        /// <param name="baseOffset">The base offset for sampling the noise.</param>
        /// <param name="octaveOffsetRange">The range for random octave offsets.</param>
        /// <param name="normalize">Whether to normalize the output values to [0, 1].</param>
        /// <returns>The filled noise map array.</returns>
        float[,] NextNoiseMap(
            float[,] noiseMap,
            PerlinNoise noise = null,
            float scale = 2.5f,
            int octaves = 8,
            float persistence = 0.5f,
            float lacunarity = 2f,
            Vector2 baseOffset = default,
            float octaveOffsetRange = 100000f,
            bool normalize = true
        );

        /// <summary>
        /// Creates a deep copy of this random number generator with the same internal state.
        /// </summary>
        /// <returns>A new IRandom instance with identical state.</returns>
        IRandom Copy();
    }
}
