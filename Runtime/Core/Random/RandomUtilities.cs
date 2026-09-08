// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Buffers.Binary;
    using UnityEngine;

    public static class RandomUtilities
    {
        /// <summary>
        /// Selects an enum value excluding a runtime-length array of values.
        /// </summary>
        /// <param name="random">The generator, or null to return the default enum value.</param>
        /// <param name="exceptions">Excluded values; null or empty excludes nothing.</param>
        /// <remarks>
        /// Uses the generator's existing enum selection semantics, including its handling of
        /// duplicate, undefined and fully excluded values. Passing an array does not copy it.
        /// Fixed-arity instance calls retain precedence over this extension.
        /// </remarks>
        public static T NextEnumExcept<T>(this IRandom random, params T[] exceptions)
            where T : unmanaged, Enum
        {
            if (random == null)
            {
                return default;
            }

            switch (exceptions?.Length ?? 0)
            {
                case 0:
                    return random.NextEnum<T>();
                case 1:
                    return random.NextEnumExcept(exceptions[0]);
                case 2:
                    return random.NextEnumExcept(exceptions[0], exceptions[1]);
                case 3:
                    return random.NextEnumExcept(exceptions[0], exceptions[1], exceptions[2]);
                case 4:
                    return random.NextEnumExcept(
                        exceptions[0],
                        exceptions[1],
                        exceptions[2],
                        exceptions[3]
                    );
                default:
                    return random.NextEnumExcept(
                        exceptions[0],
                        exceptions[1],
                        exceptions[2],
                        exceptions[3],
                        exceptions
                    );
            }
        }

        public static (ulong First, ulong Second) GuidToUInt64Pair(Guid guid)
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes);
            ulong a = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            ulong b = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8));
            return (a, b);
        }

        public static (uint A, uint B, uint C, uint D) GuidToUInt32Quad(Guid guid)
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes);
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4));
            uint c = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8));
            uint d = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12));
            return (a, b, c, d);
        }

        public static int GuidToInt32(Guid guid)
        {
            Span<byte> bytes = stackalloc byte[16];
            guid.TryWriteBytes(bytes);
            return BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        public static float GetRandomVariance(this IRandom random, float baseValue, float variance)
        {
            if (variance < 0.0f)
            {
                Debug.LogError("Variance cannot be negative");
                return baseValue;
            }

            if (variance == 0.0f)
            {
                return baseValue;
            }

            float higher = variance / 2;
            float lower = -higher;

            return baseValue + random.NextFloat(lower, higher);
        }
    }
}
