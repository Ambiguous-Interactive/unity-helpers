// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Utilities for null checks (including UnityEngine.Object overloads) and hash code composition.
    /// </summary>
    /// <remarks>
    /// The <c>HashCode</c> family mixes its arguments with a fixed FNV-1a step, but each argument
    /// contributes its ordinary <see cref="object.GetHashCode"/> value -- which is randomized per
    /// process for <see cref="string"/>, is a session-local instance id for a
    /// <see cref="UnityEngine.Object"/>, and is whatever the author wrote for any other type. The
    /// composed result is therefore a hash code: valid for dictionaries and sets inside one process,
    /// and not a value to persist, compare across processes or put on a wire. For a value that
    /// survives all three, hash bytes you control with <see cref="StableHash32V1"/>.
    /// </remarks>
    public static class Objects
    {
        /// <summary>
        /// The standard 32-bit FNV-1a offset basis, the conventional seed for
        /// <see cref="StableHash32V1"/>.
        /// </summary>
        public const uint Fnv32OffsetBasis = 2166136261u;

        private const uint Fnv32Prime = 16777619u;

        /// <summary>
        /// Unity-aware null check for UnityEngine.Object types (handles destroyed objects returning true for == null).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Null<T>(T instance)
            where T : UnityEngine.Object
        {
            return instance == null;
        }

        /// <summary>
        /// Hybrid null check for boxed or unknown objects (handles UnityEngine.Object special null semantics).
        /// </summary>
        public static bool Null(object instance)
        {
            if (instance is null)
            {
                return true;
            }

            if (instance is UnityEngine.Object unityObject)
            {
                return unityObject == null;
            }

            return false;
        }

        /// <summary>
        /// Unity-aware not-null check for UnityEngine.Object types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NotNull<T>(T instance)
            where T : UnityEngine.Object
        {
            return instance != null;
        }

        /// <summary>
        /// Hybrid not-null check for boxed or unknown objects.
        /// </summary>
        public static bool NotNull(object instance)
        {
            return !Null(instance);
        }

        /// <summary>
        /// Combines hash codes for a span of values into one composite hash code. Process-local, like
        /// every value it mixes; see the remarks on <see cref="Objects"/>.
        /// </summary>
        public static int SpanHashCode<T>(ReadOnlySpan<T> values)
        {
            if (values.IsEmpty)
            {
                return 0;
            }

            HashCodeBuilder hash = default;
            foreach (ref readonly T value in values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Combines one value into a composite hash code. Process-local, like every value it mixes;
        /// see the remarks on <see cref="Objects"/>.
        /// </summary>
        public static int HashCode<T1>(T1 param1)
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Combines two values into a composite hash code. Process-local, like every value it mixes;
        /// see the remarks on <see cref="Objects"/>.
        /// </summary>
        public static int HashCode<T1, T2>(T1 param1, T2 param2)
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3>(T1 param1, T2 param2, T3 param3)
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4>(T1 param1, T2 param2, T3 param3, T4 param4)
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            return hash.ToHashCode();
        }

        public static int HashCode<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            return hash.ToHashCode();
        }

        public static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15
        >(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14,
            T15 param15
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            hash.Add(param15);
            return hash.ToHashCode();
        }

        public static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15,
            T16
        >(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14,
            T15 param15,
            T16 param16
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            hash.Add(param15);
            hash.Add(param16);
            return hash.ToHashCode();
        }

        public static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15,
            T16,
            T17
        >(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14,
            T15 param15,
            T16 param16,
            T17 param17
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            hash.Add(param15);
            hash.Add(param16);
            hash.Add(param17);
            return hash.ToHashCode();
        }

        public static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15,
            T16,
            T17,
            T18
        >(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14,
            T15 param15,
            T16 param16,
            T17 param17,
            T18 param18
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            hash.Add(param15);
            hash.Add(param16);
            hash.Add(param17);
            hash.Add(param18);
            return hash.ToHashCode();
        }

        public static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15,
            T16,
            T17,
            T18,
            T19
        >(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14,
            T15 param15,
            T16 param16,
            T17 param17,
            T18 param18,
            T19 param19
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            hash.Add(param15);
            hash.Add(param16);
            hash.Add(param17);
            hash.Add(param18);
            hash.Add(param19);
            return hash.ToHashCode();
        }

        public static int HashCode<
            T1,
            T2,
            T3,
            T4,
            T5,
            T6,
            T7,
            T8,
            T9,
            T10,
            T11,
            T12,
            T13,
            T14,
            T15,
            T16,
            T17,
            T18,
            T19,
            T20
        >(
            T1 param1,
            T2 param2,
            T3 param3,
            T4 param4,
            T5 param5,
            T6 param6,
            T7 param7,
            T8 param8,
            T9 param9,
            T10 param10,
            T11 param11,
            T12 param12,
            T13 param13,
            T14 param14,
            T15 param15,
            T16 param16,
            T17 param17,
            T18 param18,
            T19 param19,
            T20 param20
        )
        {
            HashCodeBuilder hash = default;
            hash.Add(param1);
            hash.Add(param2);
            hash.Add(param3);
            hash.Add(param4);
            hash.Add(param5);
            hash.Add(param6);
            hash.Add(param7);
            hash.Add(param8);
            hash.Add(param9);
            hash.Add(param10);
            hash.Add(param11);
            hash.Add(param12);
            hash.Add(param13);
            hash.Add(param14);
            hash.Add(param15);
            hash.Add(param16);
            hash.Add(param17);
            hash.Add(param18);
            hash.Add(param19);
            hash.Add(param20);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Combines hash codes for all elements in an enumerable (with optimized paths for common
        /// collection types). Process-local, like every value it mixes; see the remarks on
        /// <see cref="Objects"/>.
        /// </summary>
        public static int EnumerableHashCode<T>(IEnumerable<T> enumerable)
        {
            if (ReferenceEquals(enumerable, null))
            {
                return 0;
            }

            HashCodeBuilder hash = default;
            switch (enumerable)
            {
                case IReadOnlyList<T> list:
                {
                    for (int i = 0; i < list.Count; ++i)
                    {
                        hash.Add(list[i]);
                    }

                    break;
                }
                case HashSet<T> hashSet:
                {
                    foreach (T item in hashSet)
                    {
                        hash.Add(item);
                    }

                    break;
                }
                case Queue<T> queue:
                {
                    foreach (T item in queue)
                    {
                        hash.Add(item);
                    }

                    break;
                }
                case Stack<T> stack:
                {
                    foreach (T item in stack)
                    {
                        hash.Add(item);
                    }

                    break;
                }
                case SortedSet<T> sortedSet:
                {
                    foreach (T item in sortedSet)
                    {
                        hash.Add(item);
                    }

                    break;
                }
                default:
                {
                    foreach (T item in enumerable)
                    {
                        hash.Add(item);
                    }

                    break;
                }
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// Computes a 32-bit FNV-1a hash over exactly the supplied bytes, using
        /// <paramref name="seed"/> as the initial state.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike the <c>HashCode</c> family, this depends on nothing but its arguments. The same
        /// bytes and the same seed produce the same value in every process, on every platform, and
        /// in every later version of this package: the algorithm is frozen, which is what the
        /// <c>V1</c> names. A different algorithm would arrive as a differently named method, never
        /// as a new answer from this one. That makes it safe for save-file digests, content
        /// addressing and cross-machine agreement.
        /// </para>
        /// <para>
        /// It hashes bytes and only bytes. Encode text yourself -- <c>Encoding.UTF8</c> is the usual
        /// choice -- so the encoding is part of your format rather than an assumption of this one.
        /// </para>
        /// </remarks>
        /// <param name="bytes">The bytes to hash. An empty span returns <paramref name="seed"/> unchanged.</param>
        /// <param name="seed">The initial state. Pass <see cref="Fnv32OffsetBasis"/> for standard FNV-1a.</param>
        /// <returns>The FNV-1a hash of <paramref name="bytes"/> starting from <paramref name="seed"/>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// byte[] payload = Encoding.UTF8.GetBytes(saveSlotName);
        /// uint digest = Objects.StableHash32V1(payload, Objects.Fnv32OffsetBasis);
        /// ]]></code>
        /// </example>
        public static uint StableHash32V1(ReadOnlySpan<byte> bytes, uint seed)
        {
            uint hash = seed;
            for (int i = 0; i < bytes.Length; ++i)
            {
                hash ^= bytes[i];
                hash *= Fnv32Prime;
            }

            return hash;
        }

        /*
            Lightweight hash accumulator using FNV-1a mixing. The mixing step is fixed, but each
            contribution is an ordinary GetHashCode value, so the composed result is a hash code and
            not a digest -- see the remarks on Objects.
        */
        private struct HashCodeBuilder
        {
            private const uint Seed = Fnv32OffsetBasis;
            private const uint Prime = Fnv32Prime;

            private uint _hash;
            private bool _hasContribution;
            private bool _hasNonNullContribution;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add<T>(T value)
            {
                uint valueHash = TypeTraits<T>.GetValueHash(value, out bool hasNonNullValue);

                if (!_hasContribution)
                {
                    // Defer seeding until the first value is observed so empty hashes stay at 0.
                    _hash = Seed;
                    _hasContribution = true;
                }

                _hash ^= valueHash;
                _hash *= Prime;

                if (hasNonNullValue)
                {
                    _hasNonNullContribution = true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ToHashCode()
            {
                if (!_hasContribution || !_hasNonNullContribution)
                {
                    return 0;
                }

                return unchecked((int)_hash);
            }
        }

        private static class TypeTraits<T>
        {
            private const uint NullSentinel = 0x9E3779B9u;

            private static readonly bool IsReferenceType = !typeof(T).IsValueType;
            private static readonly bool IsObjectType = typeof(T) == typeof(object);
            private static readonly bool IsUnityObject =
                typeof(UnityEngine.Object).IsAssignableFrom(typeof(T));
            private static readonly EqualityComparer<T> EqualityComparer =
                EqualityComparer<T>.Default;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint GetValueHash(T value, out bool hasNonNullValue)
            {
                if (!IsReferenceType)
                {
                    hasNonNullValue = true;
                    return unchecked((uint)EqualityComparer.GetHashCode(value));
                }

                if (IsObjectType)
                {
                    return GetBoxedObjectHash(value, out hasNonNullValue);
                }

                if (IsUnityObject)
                {
                    return GetUnityObjectHash(value, out hasNonNullValue);
                }

                if (ReferenceEquals(value, null))
                {
                    hasNonNullValue = false;
                    return NullSentinel;
                }

                hasNonNullValue = true;
                return unchecked((uint)EqualityComparer.GetHashCode(value));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint GetUnityObjectHash(T value, out bool hasNonNullValue)
            {
                T local = value;
                UnityEngine.Object unityObject = Unsafe.As<T, UnityEngine.Object>(ref local);

                if (unityObject == null)
                {
                    hasNonNullValue = false;
                    return NullSentinel;
                }

                hasNonNullValue = true;
                return unchecked((uint)unityObject.GetHashCode());
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint GetBoxedObjectHash(T value, out bool hasNonNullValue)
            {
                object boxed = value;

                if (boxed is UnityEngine.Object unityObject)
                {
                    if (unityObject == null)
                    {
                        hasNonNullValue = false;
                        return NullSentinel;
                    }

                    hasNonNullValue = true;
                    return unchecked((uint)unityObject.GetHashCode());
                }

                if (boxed is null)
                {
                    hasNonNullValue = false;
                    return NullSentinel;
                }

                hasNonNullValue = true;
                return unchecked((uint)EqualityComparer<object>.Default.GetHashCode(boxed));
            }
        }
    }
}
