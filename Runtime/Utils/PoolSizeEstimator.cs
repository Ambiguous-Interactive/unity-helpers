// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;

    /// <summary>
    /// Provides size estimation for pool item types to enable size-aware purging policies.
    /// Large objects (above the LOH threshold) are handled more aggressively by the purge system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The .NET Large Object Heap (LOH) threshold is 85,000 bytes. Objects larger than this
    /// are allocated on the LOH, which has different garbage collection characteristics:
    /// <list type="bullet">
    ///   <item><description>LOH is only collected during Gen2 collections (expensive)</description></item>
    ///   <item><description>LOH is not compacted by default (fragmentation risk)</description></item>
    ///   <item><description>Retaining large pooled objects wastes significant memory</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Size estimation is inherently approximate for managed objects because:
    /// <list type="bullet">
    ///   <item><description>Reference types have runtime overhead (vtable, sync block, etc.)</description></item>
    ///   <item><description>Fields may be padded for alignment</description></item>
    ///   <item><description>Generic types may have different layouts</description></item>
    ///   <item><description>Collections have capacity-based sizing</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Thread safety: All methods are thread-safe. Cached estimates use concurrent collections.
    /// </para>
    /// </remarks>
    public static class PoolSizeEstimator
    {
        /// <summary>
        /// The .NET Large Object Heap (LOH) threshold in bytes.
        /// Objects of this size or larger are allocated on the LOH.
        /// </summary>
        public const int LargeObjectHeapThreshold = 85000;

        /// <summary>
        /// Minimum object overhead for reference types (vtable pointer + sync block index).
        /// This is architecture-dependent; using 16 bytes as a conservative 64-bit estimate.
        /// </summary>
        private const int MinObjectOverhead = 16;

        /// <summary>
        /// Pointer size in bytes for reference calculations.
        /// Used as the size for reference type fields and as a fallback for unknown types.
        /// </summary>
        private static readonly int PointerSize = IntPtr.Size;

        /// <summary>
        /// Default typical capacity used for estimating collection sizes.
        /// Collections like List, Dictionary, HashSet, Queue, and Stack
        /// are estimated using this capacity multiplied by element size.
        /// </summary>
        private const int DefaultTypicalCollectionCapacity = 16;

        /// <summary>
        /// Minimum array overhead in bytes (header + length field).
        /// </summary>
        private const int MinArrayOverhead = MinObjectOverhead + 8;

        /// <summary>
        /// Cache for computed type size estimates to avoid repeated reflection.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, int> SizeCache =
            new ConcurrentDictionary<Type, int>();

        /// <summary>
        /// Cache for LOH classification to avoid repeated size checks.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, bool> LohCache =
            new ConcurrentDictionary<Type, bool>();

        /// <summary>
        /// Estimates the size in bytes of a single instance of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type to estimate size for.</typeparam>
        /// <returns>
        /// An estimate of the instance size in bytes. For value types, this is exact.
        /// For reference types, this is an approximation based on field analysis.
        /// </returns>
        /// <remarks>
        /// <para>
        /// For value types, uses <see cref="Unsafe.SizeOf{T}"/> for an exact size.
        /// </para>
        /// <para>
        /// For reference types, estimates based on:
        /// <list type="bullet">
        ///   <item><description>Object header overhead (~16 bytes on 64-bit)</description></item>
        ///   <item><description>Field sizes (value types by size, references by pointer size)</description></item>
        ///   <item><description>Array element sizes when applicable</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EstimateItemSizeBytes<T>()
        {
            return EstimateItemSizeBytes(typeof(T));
        }

        /// <summary>
        /// Estimates the size in bytes of a single instance of the specified type.
        /// </summary>
        /// <param name="type">The type to estimate size for.</param>
        /// <returns>
        /// An estimate of the instance size in bytes. For value types, this is exact.
        /// For reference types, this is an approximation based on field analysis.
        /// </returns>
        /// <remarks>
        /// If <paramref name="type"/> is <c>null</c>, returns <see cref="PointerSize"/> as a defensive default.
        /// </remarks>
        public static int EstimateItemSizeBytes(Type type)
        {
            if (type == null)
            {
                return PointerSize;
            }

            if (SizeCache.TryGetValue(type, out int cachedSize))
            {
                return cachedSize;
            }

            int estimatedSize = ComputeEstimatedSize(type);
            SizeCache.TryAdd(type, estimatedSize);
            return estimatedSize;
        }

        /// <summary>
        /// Determines whether instances of type <typeparamref name="T"/> would be allocated on the Large Object Heap.
        /// </summary>
        /// <typeparam name="T">The type to check.</typeparam>
        /// <returns>
        /// <c>true</c> if instances are estimated to be 85,000 bytes or larger; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLargeObject<T>()
        {
            return IsLargeObject(typeof(T));
        }

        /// <summary>
        /// Determines whether instances of the specified type would be allocated on the Large Object Heap.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>
        /// <c>true</c> if instances are estimated to be 85,000 bytes or larger; otherwise, <c>false</c>.
        /// Returns <c>false</c> if <paramref name="type"/> is <c>null</c>.
        /// </returns>
        public static bool IsLargeObject(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (LohCache.TryGetValue(type, out bool isLarge))
            {
                return isLarge;
            }

            int size = EstimateItemSizeBytes(type);
            isLarge = size >= LargeObjectHeapThreshold;
            LohCache.TryAdd(type, isLarge);
            return isLarge;
        }

        /// <summary>
        /// Estimates the size in bytes of an array with the specified element type and length.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="length">The number of elements in the array.</param>
        /// <returns>An estimate of the array size in bytes, including header overhead.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EstimateArraySizeBytes<T>(int length)
        {
            return EstimateArraySizeBytes(typeof(T), length);
        }

        /// <summary>
        /// Estimates the size in bytes of an array with the specified element type and length.
        /// </summary>
        /// <param name="elementType">The array element type.</param>
        /// <param name="length">The number of elements in the array.</param>
        /// <returns>
        /// An estimate of the array size in bytes, including header overhead.
        /// Returns <see cref="MinArrayOverhead"/> if <paramref name="elementType"/> is <c>null</c>
        /// or <paramref name="length"/> is negative.
        /// </returns>
        public static int EstimateArraySizeBytes(Type elementType, int length)
        {
            if (elementType == null)
            {
                return MinArrayOverhead;
            }

            if (length <= 0)
            {
                return MinArrayOverhead;
            }

            int elementSize = GetElementSize(elementType);
            return MinArrayOverhead + (elementSize * length);
        }

        /// <summary>
        /// Determines the array length at which the array would exceed the LOH threshold.
        /// </summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <returns>
        /// The minimum array length that would cause LOH allocation, or <see cref="int.MaxValue"/>
        /// if elements are so small that even maximum-length arrays would not reach LOH.
        /// </returns>
        public static int GetLohThresholdLength<T>()
        {
            return GetLohThresholdLength(typeof(T));
        }

        /// <summary>
        /// Determines the array length at which the array would exceed the LOH threshold.
        /// </summary>
        /// <param name="elementType">The array element type.</param>
        /// <returns>
        /// The minimum array length that would cause LOH allocation, or <see cref="int.MaxValue"/>
        /// if elements are so small that even maximum-length arrays would not reach LOH,
        /// or if <paramref name="elementType"/> is <c>null</c>.
        /// </returns>
        public static int GetLohThresholdLength(Type elementType)
        {
            if (elementType == null)
            {
                return int.MaxValue;
            }

            int elementSize = GetElementSize(elementType);
            if (elementSize <= 0)
            {
                return int.MaxValue;
            }

            int availableForElements = LargeObjectHeapThreshold - MinArrayOverhead;
            if (availableForElements <= 0)
            {
                return 0;
            }

            return availableForElements / elementSize;
        }

        /// <summary>
        /// Clears the internal caches. Primarily used for testing.
        /// </summary>
        internal static void ClearCaches()
        {
            SizeCache.Clear();
            LohCache.Clear();
        }

        private static int ComputeEstimatedSize(Type type)
        {
            // Value types: use Unsafe.SizeOf for exact measurement
            if (type.IsValueType)
            {
                return ComputeValueTypeSize(type);
            }

            // Arrays: estimate based on element type
            if (type.IsArray)
            {
                return EstimateArrayTypeSize(type);
            }

            // Reference types: estimate based on fields
            return EstimateReferenceTypeSize(type);
        }

        private static int ComputeValueTypeSize(Type type)
        {
            // Try to get the actual size using Marshal.SizeOf for blittable types
            try
            {
                return Marshal.SizeOf(type);
            }
            catch (Exception e)
            {
                // Non-blittable types - estimate based on fields
                _ = e;
                return EstimateFieldBasedSize(type);
            }
        }

        private static int EstimateArrayTypeSize(Type arrayType)
        {
            Type elementType = arrayType.GetElementType();
            if (elementType == null)
            {
                return MinObjectOverhead;
            }

            return EstimateArraySizeBytes(elementType, DefaultTypicalCollectionCapacity);
        }

        private static int EstimateReferenceTypeSize(Type type)
        {
            int size = MinObjectOverhead;

            // Check for common collection types and estimate based on typical capacity
            if (IsCollectionType(type, out int estimatedCollectionSize))
            {
                return estimatedCollectionSize;
            }

            // Add field sizes
            size += EstimateFieldBasedSize(type);

            return size;
        }

        private static int EstimateFieldBasedSize(Type type)
        {
            int size = 0;
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                FieldInfo[] fields = type.GetFields(flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    Type fieldType = field.FieldType;

                    if (fieldType.IsValueType)
                    {
                        size += GetElementSize(fieldType);
                    }
                    else
                    {
                        // Reference types are stored as pointers
                        size += PointerSize;
                    }
                }
            }
            catch (Exception e)
            {
                _ = e;
                // If reflection fails, use a conservative estimate
                size = PointerSize * 4;
            }

            return size;
        }

        private static bool IsCollectionType(Type type, out int estimatedSize)
        {
            estimatedSize = 0;

            // Check for generic collection types
            if (!type.IsGenericType)
            {
                return false;
            }

            Type genericDefinition = type.GetGenericTypeDefinition();
            Type[] genericArgs = type.GetGenericArguments();

            // List<T>
            if (genericDefinition == typeof(List<>) && genericArgs.Length == 1)
            {
                int elementSize = GetElementSize(genericArgs[0]);
                estimatedSize =
                    MinObjectOverhead
                    + (PointerSize * 3)
                    + (elementSize * DefaultTypicalCollectionCapacity);
                return true;
            }

            // Dictionary<TKey, TValue>
            if (genericDefinition == typeof(Dictionary<,>) && genericArgs.Length == 2)
            {
                int keySize = GetElementSize(genericArgs[0]);
                int valueSize = GetElementSize(genericArgs[1]);
                int entrySize = keySize + valueSize + 8; // Entry includes hash and next pointer
                estimatedSize =
                    MinObjectOverhead
                    + (PointerSize * 5)
                    + (entrySize * DefaultTypicalCollectionCapacity);
                return true;
            }

            // HashSet<T>
            if (genericDefinition == typeof(HashSet<>) && genericArgs.Length == 1)
            {
                int elementSize = GetElementSize(genericArgs[0]);
                int slotSize = elementSize + 8; // Slot includes hash and next
                estimatedSize =
                    MinObjectOverhead
                    + (PointerSize * 4)
                    + (slotSize * DefaultTypicalCollectionCapacity);
                return true;
            }

            // Queue<T>
            if (genericDefinition == typeof(Queue<>) && genericArgs.Length == 1)
            {
                int elementSize = GetElementSize(genericArgs[0]);
                estimatedSize =
                    MinObjectOverhead
                    + (PointerSize * 4)
                    + (elementSize * DefaultTypicalCollectionCapacity);
                return true;
            }

            // Stack<T>
            if (genericDefinition == typeof(Stack<>) && genericArgs.Length == 1)
            {
                int elementSize = GetElementSize(genericArgs[0]);
                estimatedSize =
                    MinObjectOverhead
                    + (PointerSize * 3)
                    + (elementSize * DefaultTypicalCollectionCapacity);
                return true;
            }

            return false;
        }

        private static int GetElementSize(Type type)
        {
            if (type == null)
            {
                return PointerSize;
            }

            if (!type.IsValueType)
            {
                return PointerSize;
            }

            // Primitive types - known sizes
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool))
            {
                return 1;
            }
            if (type == typeof(short) || type == typeof(ushort) || type == typeof(char))
            {
                return 2;
            }
            if (type == typeof(int) || type == typeof(uint) || type == typeof(float))
            {
                return 4;
            }
            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double))
            {
                return 8;
            }
            if (type == typeof(decimal))
            {
                return 16;
            }
            if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            {
                return PointerSize;
            }
            if (type == typeof(Guid))
            {
                return 16;
            }
            if (type == typeof(DateTime) || type == typeof(TimeSpan))
            {
                return 8;
            }

            // Enum types - size based on underlying type
            if (type.IsEnum)
            {
                return GetElementSize(Enum.GetUnderlyingType(type));
            }

            // Other value types - try Marshal.SizeOf
            try
            {
                return Marshal.SizeOf(type);
            }
            catch (Exception e)
            {
                _ = e;
                // Non-blittable struct - estimate based on fields
                return EstimateFieldBasedSize(type);
            }
        }
    }
}
