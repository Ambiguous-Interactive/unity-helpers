// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using DataStructure.Adapters;
    using ProtoBuf;
    using UnityEngine;
    using Utils;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#else
    using Extension;
#endif

    /// <summary>
    /// Common abstract base for all <see cref="IRandom"/> implementations with protobuf support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is annotated with [ProtoContract] and explicitly lists all known concrete
    /// implementations via [ProtoInclude]. This enables polymorphic protobuf serialization when
    /// the declared type is AbstractRandom (or another abstract base that carries the
    /// [ProtoInclude] annotations).
    /// </para>
    /// <para>
    /// Adding a new PRNG: implement <see cref="IRandom"/>, derive from <see cref="AbstractRandom"/>,
    /// and add a new [ProtoInclude(tag, typeof(YourRandom))] entry here with a unique, stable
    /// field number. Never renumber existing tags once published.
    /// </para>
    /// <example>
    /// <code>
    /// // 1) Implement your generator
    /// [ProtoContract]
    /// public sealed class MyCustomRandom : AbstractRandom { /* state + [ProtoMember]s... */ }
    ///
    /// // 2) Add a ProtoInclude tag below, e.g.
    /// // [ProtoInclude(112, typeof(MyCustomRandom))]
    ///
    /// // 3) Use AbstractRandom as your declared type in protobuf models for seamless polymorphism
    /// [ProtoContract]
    /// class RNGHolder { [ProtoMember(1)] public AbstractRandom rng; }
    /// </code>
    /// </example>
    /// <para>
    /// Interfaces: protobuf-net cannot infer a concrete type when deserializing to an interface such as
    /// <see cref="IRandom"/>. You can still serialize an IRandom value (we use the runtime type), but for
    /// deserialization you must either:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Declare the field/property as AbstractRandom (recommended), or</description></item>
    /// <item><description>Call Serializer.RegisterProtobufRoot&lt;IRandom, SomeConcreteRandom&gt;() at startup and
    /// then use Serializer.ProtoDeserialize&lt;IRandom&gt;.</description></item>
    /// </list>
    /// </remarks>
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    [ProtoInclude(100, typeof(DotNetRandom))]
    [WProtoInclude(100, typeof(DotNetRandom))]
    [ProtoInclude(101, typeof(PcgRandom))]
    [WProtoInclude(101, typeof(PcgRandom))]
    [ProtoInclude(102, typeof(XorShiftRandom))]
    [WProtoInclude(102, typeof(XorShiftRandom))]
    [ProtoInclude(103, typeof(WyRandom))]
    [WProtoInclude(103, typeof(WyRandom))]
    [ProtoInclude(104, typeof(XoroShiroRandom))]
    [WProtoInclude(104, typeof(XoroShiroRandom))]
    [ProtoInclude(105, typeof(UnityRandom))]
    [WProtoInclude(105, typeof(UnityRandom))]
    [ProtoInclude(106, typeof(SystemRandom))]
    [WProtoInclude(106, typeof(SystemRandom))]
    [ProtoInclude(107, typeof(LinearCongruentialGenerator))]
    [WProtoInclude(107, typeof(LinearCongruentialGenerator))]
    [ProtoInclude(108, typeof(SquirrelRandom))]
    [WProtoInclude(108, typeof(SquirrelRandom))]
    [ProtoInclude(109, typeof(RomuDuo))]
    [WProtoInclude(109, typeof(RomuDuo))]
    [ProtoInclude(110, typeof(SplitMix64))]
    [WProtoInclude(110, typeof(SplitMix64))]
    [ProtoInclude(111, typeof(IllusionFlow))]
    [WProtoInclude(111, typeof(IllusionFlow))]
    [ProtoInclude(112, typeof(FlurryBurstRandom))]
    [WProtoInclude(112, typeof(FlurryBurstRandom))]
    [ProtoInclude(113, typeof(PhotonSpinRandom))]
    [WProtoInclude(113, typeof(PhotonSpinRandom))]
    [ProtoInclude(114, typeof(StormDropRandom))]
    [WProtoInclude(114, typeof(StormDropRandom))]
    [ProtoInclude(115, typeof(BlastCircuitRandom))]
    [WProtoInclude(115, typeof(BlastCircuitRandom))]
    [ProtoInclude(116, typeof(WaveSplatRandom))]
    [WProtoInclude(116, typeof(WaveSplatRandom))]
    [ProtoInclude(117, typeof(WDoomRandom))]
    [WProtoInclude(117, typeof(WDoomRandom))]
    public abstract partial class AbstractRandom : IRandom
    {
        /// <summary>
        /// The values of one enum type, materialized once by its own static initializer.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <remarks>
        /// A closed generic type gets its own copy of this field, so the lookup that a dictionary
        /// keyed by <see cref="Type"/> would perform on every roll costs nothing at all.
        /// </remarks>
        private static class EnumValues<T>
            where T : unmanaged, Enum
        {
            internal static readonly T[] Values = (T[])Enum.GetValues(typeof(T));
        }

        protected const float MagicFloat = 5.960465E-008F;
        private const ulong LongBias = 1UL << 63;
        private const int MaxRejectionAttempts32 = 1 << 16;
        private const int MaxRejectionAttempts64 = 1 << 20;
        private const int MaxGaussianAttempts = 1 << 20;
        private const int GuidByteCount = 16;
        private const int MaxDoubleBitAttempts = 1 << 20;

        [ProtoMember(1)]
        [WProtoMember(1)]
        protected double? _cachedGaussian;

        public abstract RandomState InternalState { get; }

        // Allocated on first use rather than by an initializer: a formatter that allocates an
        // uninitialized instance -- which is what SkipConstructor asks protobuf-net for -- runs no
        // initializer at all, and a buffer that only NextGuid touches would arrive null.
        private byte[] _guidBytes;

        // Bit/byte reservoirs to accelerate small requests
        // Note: included in protobuf to preserve exact generator state across round-trips
        [ProtoMember(2)]
        [WProtoMember(2)]
        protected uint _bitBuffer;

        [ProtoMember(3)]
        [WProtoMember(3)]
        protected int _bitCount;

        [ProtoMember(4)]
        [WProtoMember(4)]
        protected uint _byteBuffer;

        [ProtoMember(5)]
        [WProtoMember(5)]
        protected int _byteCount;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected RandomState BuildState(
            ulong state1,
            ulong state2 = 0,
            IReadOnlyList<byte> payload = null,
            uint? auxiliaryUintA = null,
            uint? auxiliaryUintB = null
        )
        {
            byte[] payloadCopy = null;
            if (payload != null)
            {
                if (payload is byte[] payloadArray)
                {
                    int length = payloadArray.Length;
                    payloadCopy = new byte[length];
                    Buffer.BlockCopy(payloadArray, 0, payloadCopy, 0, length);
                }
                else
                {
                    int count = payload.Count;
                    payloadCopy = new byte[count];
                    for (int i = 0; i < count; ++i)
                    {
                        payloadCopy[i] = payload[i];
                    }
                }
            }

            return new RandomState(
                state1,
                state2,
                _cachedGaussian,
                payload: payloadCopy,
                bitBuffer: _bitBuffer,
                bitCount: _bitCount,
                byteBuffer: _byteBuffer,
                byteCount: _byteCount
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void RestoreCommonState(RandomState state)
        {
            _cachedGaussian = state.Gaussian;
            _bitBuffer = state.BitBuffer;
            _bitCount = state.BitCount;
            _byteBuffer = state.ByteBuffer;
            _byteCount = state.ByteCount;
        }

        public virtual int Next()
        {
            // Mask out the MSB to ensure the value is within [0, int.MaxValue]
            return unchecked((int)NextUint() & 0x7FFFFFFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Next(int max)
        {
            if (max <= 0)
            {
                throw new ArgumentException($"Max {max} cannot be less-than or equal-to 0");
            }

            return unchecked((int)NextUint(unchecked((uint)max)));
        }

        public int Next(int min, int max)
        {
            if (max <= min)
            {
                throw new ArgumentException(
                    $"Min {min} cannot be larger-than or equal-to max {max}"
                );
            }

            uint range = (uint)(max - min);
            if (range == 0)
            {
                return unchecked((int)NextUint());
            }

            return unchecked((int)(min + NextUint(range)));
        }

        // Internal sampler
        public abstract uint NextUint();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint NextUint(uint max)
        {
            if (max == 0)
            {
                throw new ArgumentException("Max cannot be zero");
            }

            // Power-of-two fast path
            if ((max & (max - 1)) == 0)
            {
                return NextUint() & (max - 1);
            }

            // Lemire's method (32-bit): take high 32 bits of r*max
            uint r = NextUint();
            ulong m = (ulong)r * max;
            uint lo = (uint)m;
            if (lo < max)
            {
                uint t = unchecked((0u - max) % max);
                int attempts = 0;
                while (lo < t)
                {
                    if (++attempts > MaxRejectionAttempts32)
                    {
                        // Prevent infinite loop: fall back to modulo (small bias) rather than hang
                        return r % max;
                    }
                    r = NextUint();
                    m = (ulong)r * max;
                    lo = (uint)m;
                }
            }
            return (uint)(m >> 32);
        }

        public uint NextUint(uint min, uint max)
        {
            if (max <= min)
            {
                throw new ArgumentException(
                    $"Min {min} cannot be larger-than or equal-to max {max}"
                );
            }

            return min + NextUint(max - min);
        }

        public short NextShort()
        {
            return NextShort(short.MaxValue);
        }

        public short NextShort(short max)
        {
            return NextShort(0, max);
        }

        public short NextShort(short min, short max)
        {
            return unchecked((short)Next(min, max));
        }

        public byte NextByte()
        {
            if (_byteCount == 0)
            {
                _byteBuffer = NextUint();
                _byteCount = 4;
            }
            byte b = (byte)_byteBuffer;
            _byteBuffer >>= 8;
            _byteCount--;
            return b;
        }

        public byte NextByte(byte max)
        {
            return NextByte(0, max);
        }

        public byte NextByte(byte min, byte max)
        {
            return unchecked((byte)Next(min, max));
        }

        public long NextLong()
        {
            uint upper = NextUint();
            uint lower = NextUint();
            unchecked
            {
                return (long)((((ulong)upper << 32) | lower) & 0x7FFFFFFFFFFFFFFF);
            }
        }

        public long NextLong(long max)
        {
            if (max <= 0)
            {
                throw new ArgumentException($"Max {max} cannot be less-than or equal-to 0");
            }

            return unchecked((long)NextUlong(unchecked((ulong)max)));
        }

        public long NextLong(long min, long max)
        {
            if (max <= min)
            {
                throw new ArgumentException(
                    $"Min {min} cannot be larger-than or equal-to Max {max}"
                );
            }

            ulong biasedMin = BiasLong(min);
            ulong biasedMax = BiasLong(max);
            ulong range = biasedMax - biasedMin;

            ulong sample = NextUlong(range);
            ulong biasedResult = biasedMin + sample;
            return UnbiasLong(biasedResult);
        }

        public virtual ulong NextUlong()
        {
            uint upper = NextUint();
            uint lower = NextUint();
            return ((ulong)upper << 32) | lower;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextUlong(ulong max)
        {
            if (max == 0)
            {
                throw new ArgumentException("Max cannot be zero");
            }

            // Power-of-two fast path (no rejection required)
            if ((max & (max - 1)) == 0)
            {
                return NextUlong() & (max - 1);
            }

            // Lemire's method with rejection: use high 64 bits of the 128-bit product,
            // retrying when the low bits fall within the threshold region to avoid bias.
            ulong threshold = unchecked(0UL - max) % max;
            int attempts = 0;
            while (true)
            {
                ulong sample = NextUlong();
                ulong productLow = unchecked(sample * max);
                if (productLow >= threshold)
                {
                    return MulHi64(sample, max);
                }

                if (++attempts > MaxRejectionAttempts64)
                {
                    // Failsafe: fall back to modulo to avoid hanging indefinitely.
                    return sample % max;
                }
            }
        }

        public ulong NextUlong(ulong min, ulong max)
        {
            if (max <= min)
            {
                throw new ArgumentException(
                    $"Min {min} cannot be larger-than or equal-to max {max}"
                );
            }

            return NextUlong(max - min) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual bool NextBool()
        {
            if (_bitCount == 0)
            {
                _bitBuffer = NextUint();
                _bitCount = 32;
            }
            bool bit = (_bitBuffer & 1u) == 0;
            _bitBuffer >>= 1;
            _bitCount--;
            return bit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void NextBytes(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            NextBytes(buffer.AsSpan());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void NextBytes(Span<byte> buffer)
        {
            int i = 0;
            int len = buffer.Length;

            // 16-byte unrolled blocks
            for (; i <= len - 16; i += 16)
            {
                uint r0 = NextUint();
                uint r1 = NextUint();
                uint r2 = NextUint();
                uint r3 = NextUint();

                buffer[i + 0] = (byte)r0;
                buffer[i + 1] = (byte)(r0 >> 8);
                buffer[i + 2] = (byte)(r0 >> 16);
                buffer[i + 3] = (byte)(r0 >> 24);

                buffer[i + 4] = (byte)r1;
                buffer[i + 5] = (byte)(r1 >> 8);
                buffer[i + 6] = (byte)(r1 >> 16);
                buffer[i + 7] = (byte)(r1 >> 24);

                buffer[i + 8] = (byte)r2;
                buffer[i + 9] = (byte)(r2 >> 8);
                buffer[i + 10] = (byte)(r2 >> 16);
                buffer[i + 11] = (byte)(r2 >> 24);

                buffer[i + 12] = (byte)r3;
                buffer[i + 13] = (byte)(r3 >> 8);
                buffer[i + 14] = (byte)(r3 >> 16);
                buffer[i + 15] = (byte)(r3 >> 24);
            }

            // 4-byte chunks
            for (; i <= len - 4; i += 4)
            {
                uint r = NextUint();
                buffer[i + 0] = (byte)r;
                buffer[i + 1] = (byte)(r >> 8);
                buffer[i + 2] = (byte)(r >> 16);
                buffer[i + 3] = (byte)(r >> 24);
            }

            // Tail
            if (i < len)
            {
                uint r = NextUint();
                int j = 0;
                for (; i < len; ++i, ++j)
                {
                    buffer[i] = (byte)(r >> (j * 8));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual double NextDouble()
        {
            // 53 random bits from a 64-bit sample
            const double scale = 1.0 / 9007199254740992.0; // 2^53
            ulong combined = NextUlong() >> 11;
            return combined * scale;
        }

        public double NextDouble(double max)
        {
            if (max <= 0)
            {
                throw new ArgumentException($"Max {max} cannot be less-than or equal-to 0");
            }

            return NextDouble() * max;
        }

        public double NextDouble(double min, double max)
        {
            if (max <= min)
            {
                throw new ArgumentException(
                    $"Min {min} cannot be larger-than or equal-to max {max}"
                );
            }

            double range = max - min;
            if (double.IsInfinity(range))
            {
                return NextDoubleWithInfiniteRange(min, max);
            }

            return min + NextDouble() * range;
        }

        protected double NextDoubleWithInfiniteRange(double min, double max)
        {
            ulong orderedMin = ToOrderedDouble(min);
            ulong orderedMax = ToOrderedDouble(max);

            if (orderedMax <= orderedMin)
            {
                throw new ArgumentException(
                    $"Invalid range [{min}, {max}) for infinite-range sampling."
                );
            }

            ulong range = orderedMax - orderedMin;
            int attempts = 0;
            while (true)
            {
                ulong sample = orderedMin + NextUlong(range);
                double value = FromOrderedDouble(sample);

                if (!double.IsNaN(value) && !double.IsInfinity(value))
                {
                    return value;
                }
                if (++attempts > MaxRejectionAttempts64)
                {
                    // Transparent fallback: pick a finite value inside [min, max)
                    if (double.IsPositiveInfinity(max))
                    {
                        return FromOrderedDouble(orderedMax - 1);
                    }
                    if (double.IsNegativeInfinity(min))
                    {
                        return FromOrderedDouble(orderedMin + 1);
                    }

                    ulong midpoint = orderedMin + (range >> 1);
                    double midValue = FromOrderedDouble(midpoint);
                    if (!double.IsNaN(midValue) && !double.IsInfinity(midValue))
                    {
                        return midValue;
                    }

                    // Final safeguard: nudge just above min in ordered space
                    return FromOrderedDouble(orderedMin + 1);
                }
            }
        }

        protected double NextDoubleFullRange()
        {
            const ulong exponentMask = 0x7FF0000000000000;
            ulong randomBits;
            int attempts = 0;
            do
            {
                randomBits = NextUlong();
                if (++attempts > MaxDoubleBitAttempts)
                {
                    // Force a finite value by clearing exponent bits
                    randomBits &= ~exponentMask;
                    break;
                }
            } while ((randomBits & exponentMask) == exponentMask);

            return BitConverter.Int64BitsToDouble(unchecked((long)randomBits));
        }

        public double NextGaussian(double mean = 0, double stdDev = 1)
        {
            return mean + NextGaussianInternal() * stdDev;
        }

        private double NextGaussianInternal()
        {
            if (_cachedGaussian != null)
            {
                double gaussian = _cachedGaussian.Value;
                _cachedGaussian = null;
                return gaussian;
            }

            // https://stackoverflow.com/q/7183229/1917135
            double x;
            double y;
            double square;
            int attempts = 0;
            do
            {
                x = 2 * NextDouble() - 1;
                y = 2 * NextDouble() - 1;
                square = x * x + y * y;
                if (++attempts > MaxGaussianAttempts)
                {
                    // Fallback to Box-Muller without rejection to avoid infinite loop
                    double u1 = NextDouble();
                    if (u1 <= double.Epsilon)
                    {
                        u1 = double.Epsilon;
                    }
                    double u2 = NextDouble();
                    double mag = Math.Sqrt(-2.0 * Math.Log(u1));
                    double z0 = mag * Math.Cos(2.0 * Math.PI * u2);
                    double z1 = mag * Math.Sin(2.0 * Math.PI * u2);
                    _cachedGaussian = z1;
                    return z0;
                }
            } while (square is 0 or > 1);

            double fac = Math.Sqrt(-2 * Math.Log(square) / square);
            _cachedGaussian = x * fac;
            return y * fac;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual float NextFloat()
        {
            // Use 24 random bits for float mantissa
            return (NextUint() >> 8) * (1f / (1 << 24));
        }

        public float NextFloat(float max)
        {
            if (max <= 0)
            {
                throw new ArgumentException($"{max} cannot be less-than or equal-to 0");
            }

            return NextFloat() * max;
        }

        public float NextFloat(float min, float max)
        {
            if (max <= min)
            {
                throw new ArgumentException(
                    $"Min {min} cannot be larger-than or equal-to max {max}"
                );
            }

            float range = max - min;
            if (float.IsInfinity(range))
            {
                return (float)NextDouble(min, max);
            }

            return min + NextFloat(range);
        }

        public T NextOf<T>(IEnumerable<T> enumerable)
        {
            if (enumerable is null)
            {
                throw new ArgumentNullException(nameof(enumerable));
            }

            return enumerable switch
            {
                IReadOnlyList<T> list => NextOf(list),
                IReadOnlyCollection<T> collection => NextOf(collection),
                _ => NextFromEnumerable(enumerable),
            };
        }

        public T NextOf<T>(IReadOnlyCollection<T> collection)
        {
            if (collection is not { Count: > 0 })
            {
                throw new ArgumentException("Collection cannot be empty");
            }

            if (collection is IReadOnlyList<T> list)
            {
                return RandomOf(list);
            }
            int index = Next(collection.Count);

            switch (collection)
            {
                case HashSet<T> hashSet:
                {
                    int i = 0;
                    foreach (T element in hashSet)
                    {
                        if (index == i++)
                        {
                            return element;
                        }
                    }
                    throw new ArgumentException(nameof(collection));
                }
                case SortedSet<T> sortedSet:
                {
                    int i = 0;
                    foreach (T element in sortedSet)
                    {
                        if (index == i++)
                        {
                            return element;
                        }
                    }

                    throw new ArgumentException(nameof(collection));
                }
                case LinkedList<T> linkedList:
                {
                    int i = 0;
                    foreach (T element in linkedList)
                    {
                        if (index == i++)
                        {
                            return element;
                        }
                    }

                    throw new ArgumentException(nameof(collection));
                }
                case Queue<T> queue:
                {
                    int i = 0;
                    foreach (T element in queue)
                    {
                        if (index == i++)
                        {
                            return element;
                        }
                    }

                    throw new ArgumentException(nameof(collection));
                }
                case Stack<T> stack:
                {
                    int i = 0;
                    foreach (T element in stack)
                    {
                        if (index == i++)
                        {
                            return element;
                        }
                    }

                    throw new ArgumentException(nameof(collection));
                }
                default:
                {
                    int i = 0;
                    foreach (T element in collection)
                    {
                        if (index == i++)
                        {
                            return element;
                        }
                    }
                    throw new ArgumentException(nameof(collection));
                }
            }
        }

        public T NextOf<T>(IReadOnlyList<T> list)
        {
            if (list is not { Count: > 0 })
            {
                throw new ArgumentException("Collection cannot be empty", nameof(list));
            }

            /*
                For small lists, it's much more efficient to simply return one of their elements
                instead of trying to generate a random number within bounds (which is implemented as a while(true) loop)
             */
            return RandomOf(list);
        }

        public T NextOfParams<T>(params T[] elements)
        {
            if (elements.Length == 0)
            {
                throw new ArgumentException(nameof(elements));
            }

            return RandomOf(elements);
        }

        private T NextFromEnumerable<T>(IEnumerable<T> enumerable)
        {
            using IEnumerator<T> enumerator = enumerable.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                throw new ArgumentException("Collection cannot be empty", nameof(enumerable));
            }

            T selection = enumerator.Current;
            ulong seen = 1;

            while (enumerator.MoveNext())
            {
                seen++;
                if (NextUlong(seen) == 0)
                {
                    selection = enumerator.Current;
                }
            }

            return selection;
        }

        private static T[] GetEnumValues<T>()
            where T : unmanaged, Enum
        {
            return EnumValues<T>.Values;
        }

        private static void EnsureEnumHasAvailableValues<T>(
            T[] enumValues,
            ReadOnlySpan<T> exclusions
        )
            where T : unmanaged, Enum
        {
            if (enumValues.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Enum {typeof(T).Name} does not define any values."
                );
            }

            if (exclusions.IsEmpty)
            {
                return;
            }

            int enumCount = enumValues.Length;
            const int StackThreshold = 8;

            if (exclusions.Length <= StackThreshold)
            {
                Span<T> unique = stackalloc T[StackThreshold];
                int uniqueCount = 0;

                foreach (T exclusion in exclusions)
                {
                    if (Array.IndexOf(enumValues, exclusion) < 0)
                    {
                        continue;
                    }

                    bool seen = false;
                    for (int i = 0; i < uniqueCount; ++i)
                    {
                        if (EqualityComparer<T>.Default.Equals(unique[i], exclusion))
                        {
                            seen = true;
                            break;
                        }
                    }

                    if (seen)
                    {
                        continue;
                    }

                    unique[uniqueCount++] = exclusion;
                    if (uniqueCount >= enumCount)
                    {
                        ThrowAllEnumValuesExcluded<T>(enumCount);
                    }
                }

                return;
            }

            using PooledResource<HashSet<T>> pooledSet = Buffers<T>.HashSet.Get(out HashSet<T> set);
            foreach (T exclusion in exclusions)
            {
                if (Array.IndexOf(enumValues, exclusion) < 0)
                {
                    continue;
                }

                set.Add(exclusion);
                if (set.Count >= enumCount)
                {
                    ThrowAllEnumValuesExcluded<T>(enumCount);
                }
            }
        }

        private T NextEnumExceptInternal<T>(ReadOnlySpan<T> exclusions)
            where T : unmanaged, Enum
        {
            T[] enumValues = GetEnumValues<T>();
            EnsureEnumHasAvailableValues(enumValues, exclusions);
            return SelectEnumValue(enumValues, exclusions);
        }

        private T SelectEnumValue<T>(T[] enumValues, ReadOnlySpan<T> exclusions)
            where T : unmanaged, Enum
        {
            if (exclusions.IsEmpty)
            {
                return RandomOf(enumValues);
            }

            const int StackThreshold = 128;
            int enumCount = enumValues.Length;
            Span<T> buffer = enumCount <= StackThreshold ? stackalloc T[enumCount] : default;

            if (!buffer.IsEmpty)
            {
                int count = PopulateAllowedValues(enumValues, exclusions, buffer);
                if (count == 0)
                {
                    ThrowAllEnumValuesExcluded<T>(enumCount);
                }

                return count == 1 ? buffer[0] : buffer[Next(count)];
            }

            using PooledArray<T> pooled = SystemArrayPool<T>.Get(enumCount, out T[] temp);
            Span<T> tempSpan = temp.AsSpan(0, enumCount);
            int index = PopulateAllowedValues(enumValues, exclusions, tempSpan);
            if (index == 0)
            {
                ThrowAllEnumValuesExcluded<T>(enumCount);
            }

            return index == 1 ? temp[0] : temp[Next(index)];
        }

        private T SelectEnumValue<T>(T[] enumValues, HashSet<T> exclusions)
            where T : unmanaged, Enum
        {
            if (exclusions == null || exclusions.Count == 0)
            {
                return RandomOf(enumValues);
            }

            const int StackThreshold = 128;
            int enumCount = enumValues.Length;
            Span<T> buffer = enumCount <= StackThreshold ? stackalloc T[enumCount] : default;

            if (!buffer.IsEmpty)
            {
                int count = PopulateAllowedValues(enumValues, exclusions, buffer);
                if (count == 0)
                {
                    ThrowAllEnumValuesExcluded<T>(enumCount);
                }

                return count == 1 ? buffer[0] : buffer[Next(count)];
            }

            using PooledArray<T> pooled = SystemArrayPool<T>.Get(enumCount, out T[] temp);
            Span<T> tempSpan = temp.AsSpan(0, enumCount);
            int index = PopulateAllowedValues(enumValues, exclusions, tempSpan);
            if (index == 0)
            {
                ThrowAllEnumValuesExcluded<T>(enumCount);
            }

            return index == 1 ? temp[0] : temp[Next(index)];
        }

        private static void EnsureEnumHasAvailableValues<T>(T[] enumValues, HashSet<T> exclusions)
            where T : struct, Enum
        {
            if (enumValues.Length == 0)
            {
                exclusions.Clear();
                throw new InvalidOperationException(
                    $"Enum {typeof(T).Name} does not define any values."
                );
            }

            if (exclusions.Count == 0)
            {
                return;
            }

            int excludedCount = 0;
            foreach (T value in enumValues)
            {
                if (exclusions.Contains(value))
                {
                    excludedCount++;
                }
            }

            if (excludedCount >= enumValues.Length)
            {
                exclusions.Clear();
                ThrowAllEnumValuesExcluded<T>(enumValues.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SpanContains<T>(ReadOnlySpan<T> span, T value)
        {
            for (int i = 0; i < span.Length; ++i)
            {
                if (EqualityComparer<T>.Default.Equals(span[i], value))
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopulateAllowedValues<T>(
            T[] enumValues,
            ReadOnlySpan<T> exclusions,
            Span<T> destination
        )
            where T : unmanaged, Enum
        {
            int count = 0;
            foreach (T value in enumValues)
            {
                if (!SpanContains(exclusions, value))
                {
                    destination[count++] = value;
                }
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PopulateAllowedValues<T>(
            T[] enumValues,
            HashSet<T> exclusions,
            Span<T> destination
        )
            where T : unmanaged, Enum
        {
            int count = 0;
            foreach (T value in enumValues)
            {
                if (!exclusions.Contains(value))
                {
                    destination[count++] = value;
                }
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowAllEnumValuesExcluded<T>(int enumCount)
            where T : struct, Enum
        {
            throw new InvalidOperationException(
                $"Cannot select a value from enum {typeof(T).Name} because all {enumCount} defined values are excluded."
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong BiasLong(long value)
        {
            return unchecked((ulong)value + LongBias);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long UnbiasLong(ulong value)
        {
            return unchecked((long)(value - LongBias));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MulHi64(ulong x, ulong y)
        {
#if NET7_0_OR_GREATER
            if (System.Runtime.Intrinsics.X86.Bmi2.X64.IsSupported)
            {
                unsafe
                {
                    ulong lo;
                    ulong hi = System.Runtime.Intrinsics.X86.Bmi2.X64.MultiplyNoFlags(x, y, &lo);
                    return hi;
                }
            }
#endif
            ulong x0 = (uint)x;
            ulong x1 = x >> 32;
            ulong y0 = (uint)y;
            ulong y1 = y >> 32;

            ulong p11 = x1 * y1;
            ulong p01 = x0 * y1;
            ulong p10 = x1 * y0;
            ulong p00 = x0 * y0;

            ulong middle = p10 + (p00 >> 32) + (uint)p01;
            ulong hi = p11 + (middle >> 32) + (p01 >> 32);
            return hi;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ToOrderedDouble(double value)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException(
                    "NaN is not a valid bound for random sampling.",
                    nameof(value)
                );
            }

            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            const ulong signBit = 1UL << 63;
            return (bits & signBit) != 0 ? ~bits : bits | signBit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double FromOrderedDouble(ulong value)
        {
            const ulong signBit = 1UL << 63;
            ulong bits = (value & signBit) != 0 ? value & ~signBit : ~value;
            return BitConverter.Int64BitsToDouble(unchecked((long)bits));
        }

        public T NextEnum<T>()
            where T : unmanaged, Enum
        {
            T[] enumValues = GetEnumValues<T>();
            if (enumValues.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Enum {typeof(T).Name} does not define any values."
                );
            }

            return RandomOf(enumValues);
        }

        public T NextEnumExcept<T>(T exception1)
            where T : unmanaged, Enum
        {
            Span<T> exclusions = stackalloc T[1];
            exclusions[0] = exception1;
            return NextEnumExceptInternal<T>(exclusions);
        }

        public T NextEnumExcept<T>(T exception1, T exception2)
            where T : unmanaged, Enum
        {
            Span<T> exclusions = stackalloc T[2];
            exclusions[0] = exception1;
            exclusions[1] = exception2;
            return NextEnumExceptInternal<T>(exclusions);
        }

        public T NextEnumExcept<T>(T exception1, T exception2, T exception3)
            where T : unmanaged, Enum
        {
            Span<T> exclusions = stackalloc T[3];
            exclusions[0] = exception1;
            exclusions[1] = exception2;
            exclusions[2] = exception3;
            return NextEnumExceptInternal<T>(exclusions);
        }

        public T NextEnumExcept<T>(T exception1, T exception2, T exception3, T exception4)
            where T : unmanaged, Enum
        {
            Span<T> exclusions = stackalloc T[4];
            exclusions[0] = exception1;
            exclusions[1] = exception2;
            exclusions[2] = exception3;
            exclusions[3] = exception4;
            return NextEnumExceptInternal<T>(exclusions);
        }

        public T NextEnumExcept<T>(
            T exception1,
            T exception2,
            T exception3,
            T exception4,
            params T[] exceptions
        )
            where T : unmanaged, Enum
        {
            T[] enumValues = GetEnumValues<T>();

            using PooledResource<HashSet<T>> bufferResource = Buffers<T>.HashSet.Get(
                out HashSet<T> set
            );
            set.Add(exception1);
            set.Add(exception2);
            set.Add(exception3);
            set.Add(exception4);
            foreach (T exception in exceptions)
            {
                set.Add(exception);
            }

            EnsureEnumHasAvailableValues(enumValues, set);

            return SelectEnumValue(enumValues, set);
        }

        public Guid NextGuid()
        {
            return new Guid(GenerateGuidBytes());
        }

        public WGuid NextWGuid()
        {
            return new WGuid(GenerateGuidBytes());
        }

        private byte[] GenerateGuidBytes()
        {
            byte[] guidBytes = _guidBytes;
            if (guidBytes == null)
            {
                guidBytes = new byte[GuidByteCount];
                _guidBytes = guidBytes;
            }

            NextBytes(guidBytes);
            SetUuidV4Bits(guidBytes);
            return guidBytes;
        }

        public static void SetUuidV4Bits(byte[] bytes)
        {
            if (bytes == null || bytes.Length < GuidByteCount)
            {
                throw new ArgumentException(
                    "UUID buffer must contain at least 16 bytes.",
                    nameof(bytes)
                );
            }

            // Version bits live in byte 7 (second byte of the time_hi_and_version field).
            byte versionByte = bytes[7];
            versionByte &= 0x0F;
            versionByte |= 0x40;
            bytes[7] = versionByte;

            // Variant bits live in byte 8 (clock_seq_hi_and_reserved).
            byte variantByte = bytes[8];
            variantByte &= 0x3F;
            variantByte |= 0x80;
            bytes[8] = variantByte;
        }

        public float[,] NextNoiseMap(
            float[,] noiseMap,
            PerlinNoise noise = null,
            float scale = 2.5f,
            int octaves = 8,
            float persistence = 0.5f,
            float lacunarity = 2f,
            Vector2 baseOffset = default,
            float octaveOffsetRange = 100000f,
            bool normalize = true
        )
        {
            if (noiseMap is null)
            {
                throw new ArgumentNullException(nameof(noiseMap));
            }

            if (scale <= 0)
            {
                throw new ArgumentException(nameof(scale));
            }

            if (octaves < 1)
            {
                throw new ArgumentException(nameof(octaves));
            }

            if (persistence <= 0)
            {
                throw new ArgumentException(nameof(persistence));
            }

            if (lacunarity <= 0)
            {
                throw new ArgumentException(nameof(lacunarity));
            }

            if (octaveOffsetRange <= 0)
            {
                throw new ArgumentException(nameof(octaveOffsetRange));
            }

            noise ??= PerlinNoise.Instance;

            int width = noiseMap.GetLength(0);
            int height = noiseMap.GetLength(1);
            using PooledArray<Vector2> octaveOffsetBuffer = SystemArrayPool<Vector2>.Get(
                octaves,
                out Vector2[] octaveOffsets
            );
            for (int i = 0; i < octaves; i++)
            {
                float offsetX = NextFloat(-octaveOffsetRange, octaveOffsetRange);
                float offsetY = NextFloat(-octaveOffsetRange, octaveOffsetRange);
                octaveOffsets[i] = new Vector2(offsetX, offsetY);
            }

            float maxNoiseHeight = float.MinValue;
            float minNoiseHeight = float.MaxValue;

            float halfWidth = width / 2f;
            float halfHeight = height / 2f;

            for (int x = 0; x < width; ++x)
            {
                for (int y = 0; y < height; ++y)
                {
                    float amplitude = 1;
                    float frequency = 1;
                    float noiseHeight = 0;
                    for (int i = 0; i < octaves; i++)
                    {
                        float sampleX =
                            (x - halfWidth) / scale * frequency + octaveOffsets[i].x + baseOffset.x;
                        float sampleY =
                            (y - halfHeight) / scale * frequency
                            + octaveOffsets[i].y
                            + baseOffset.y;

                        float perlinValue = noise.Noise(sampleX, sampleY) * 2 - 1;
                        noiseHeight += perlinValue * amplitude;
                        amplitude *= persistence;
                        frequency *= lacunarity;
                    }

                    if (noiseHeight > maxNoiseHeight)
                    {
                        maxNoiseHeight = noiseHeight;
                    }
                    else if (noiseHeight < minNoiseHeight)
                    {
                        minNoiseHeight = noiseHeight;
                    }

                    noiseMap[x, y] = noiseHeight;
                }
            }

            if (normalize)
            {
                for (int x = 0; x < width; ++x)
                {
                    for (int y = 0; y < height; ++y)
                    {
                        // Returns a value between 0f and 1f based on noiseMap value
                        // minNoiseHeight being 0f, and maxNoiseHeight being 1f
                        noiseMap[x, y] = Mathf.InverseLerp(
                            minNoiseHeight,
                            maxNoiseHeight,
                            noiseMap[x, y]
                        );
                    }
                }
            }
            return noiseMap;
        }

        protected T RandomOf<T>(IReadOnlyList<T> values)
        {
            int count = values.Count;
            return count switch
            {
                0 => default,
                1 => values[0],
                2 => NextBool() ? values[0] : values[1],
                _ => values[Next(count)],
            };
        }

        public abstract IRandom Copy();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected internal static uint RotateLeft(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected internal static uint Mix32(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (uint)z;
            }
        }
    }
}
