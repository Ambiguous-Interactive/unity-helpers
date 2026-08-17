// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;
    using PbSerializer = ProtoBuf.Serializer;

    /// <summary>
    /// Provides extensions and equality comparers that use protobuf-net serialization output
    /// to compare values for equality, avoiding intermediate byte[] allocations.
    /// </summary>
    /// <remarks>
    /// Thread Safety: Thread-safe for value types. Reference types are safe if not modified during comparison.
    /// Performance: Requires full serialization of both objects for comparison - can be expensive for large objects.
    /// Use for deep equality where standard equality is insufficient (e.g., comparing complex object graphs).
    /// </remarks>
    public static class ProtoEqualityExtensions
    {
        /// <summary>
        /// Compares two instances for equality by serializing them via protobuf and comparing the resulting bytes.
        /// This implementation writes to reusable MemoryStreams and compares their backing buffers without ToArray().
        /// </summary>
        /// <typeparam name="T">The type of objects to compare (must be protobuf-serializable).</typeparam>
        /// <param name="self">The first object to compare.</param>
        /// <param name="other">The second object to compare.</param>
        /// <returns>True if both objects serialize to identical byte sequences, false otherwise.</returns>
        /// <remarks>
        /// Null Handling: For reference types, null == null returns true, null vs non-null returns false.
        /// For value types, uses default equality for nullability.
        /// Thread Safety: Thread-safe if objects are not modified during comparison. Uses thread-safe pooled resources.
        /// Performance: O(n) where n is serialized size. Requires full serialization of both objects.
        /// Very expensive for large objects - consider caching or custom equality when possible.
        /// Allocations: Uses pooled PooledBufferStream instances - no heap allocations for buffers.
        /// Edge Cases: ReferenceEquals check short-circuits for identical reference types. Value types always serialize.
        /// Requires both types to be protobuf-serializable with consistent serialization.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ProtoEquals<T>(this T self, T other)
        {
            if (ProtoEqualityComparer<T>.IsReferenceType)
            {
                if (ReferenceEquals(self, other))
                {
                    return true;
                }

                if (self is null || other is null)
                {
                    return false;
                }
            }

            using PooledResource<PooledBufferStream> aLease = PooledBufferStream.Rent(
                out PooledBufferStream a
            );
            using PooledResource<PooledBufferStream> bLease = PooledBufferStream.Rent(
                out PooledBufferStream b
            );

            // Mirror Serializer's decision logic to prefer runtime type for interface/abstract/object
            Type declared = typeof(T);
            bool useRuntime = Serializer.ShouldUseRuntimeTypeForProtobuf(
                declared,
                self,
                forceRuntimeType: false
            );

            if (useRuntime)
            {
                PbSerializer.NonGeneric.Serialize(a, self);
                PbSerializer.NonGeneric.Serialize(b, other);
            }
            else
            {
                PbSerializer.Serialize(a, self);
                PbSerializer.Serialize(b, other);
            }

            return ProtoBufferComparer.StreamContentEquals(a, b);
        }

        /// <summary>
        /// Returns a singleton equality comparer that compares values by their protobuf serialization.
        /// </summary>
        /// <typeparam name="T">The type of objects to compare (must be protobuf-serializable).</typeparam>
        /// <returns>A cached singleton instance of ProtoEqualityComparer&lt;T&gt;.</returns>
        /// <remarks>
        /// Null Handling: The returned comparer handles nulls as per ProtoEquals.
        /// Thread Safety: Thread-safe - returns a singleton instance.
        /// Performance: O(1) - returns cached singleton.
        /// Allocations: No allocations - uses pre-initialized singleton.
        /// Edge Cases: Same comparer instance is returned for each type T across all calls.
        /// </remarks>
        public static IEqualityComparer<T> GetProtoComparer<T>()
        {
            return ProtoEqualityComparer<T>.Instance;
        }
    }

    /// <summary>
    /// Generic proto-based equality comparer using protobuf serialization output, with minimal allocations.
    /// Implements IEqualityComparer&lt;T&gt; for use in dictionaries, hash sets, and LINQ operations.
    /// </summary>
    /// <typeparam name="T">The type of objects to compare (must be protobuf-serializable).</typeparam>
    /// <remarks>
    /// Thread Safety: Thread-safe singleton pattern. Comparison operations are thread-safe if objects are not modified.
    /// Performance: Expensive - requires full serialization for both Equals and GetHashCode operations.
    /// Allocations: Uses pooled PooledBufferStream instances - minimal heap allocations.
    /// </remarks>
    public sealed class ProtoEqualityComparer<T> : IEqualityComparer<T>
    {
        /// <summary>
        /// Singleton instance of the comparer for type T.
        /// </summary>
        public static readonly ProtoEqualityComparer<T> Instance = new();

        // Reference-type closures share one canonical instantiation, so typeof(T) inside a generic
        // member is a per-call handle lookup rather than a constant. This resolves it once.
        internal static readonly bool IsReferenceType = !typeof(T).IsValueType;

        private ProtoEqualityComparer() { }

        /// <summary>
        /// Determines whether two objects of type T are equal by comparing their protobuf serialization.
        /// </summary>
        /// <param name="x">The first object to compare.</param>
        /// <param name="y">The second object to compare.</param>
        /// <returns>True if both objects serialize to identical byte sequences, false otherwise.</returns>
        /// <remarks>
        /// Null Handling: For reference types, null == null returns true, null vs non-null returns false.
        /// Thread Safety: Thread-safe if objects are not modified during comparison.
        /// Performance: O(n) where n is serialized size - requires full serialization of both objects.
        /// Allocations: Uses pooled buffers - no heap allocations for streams.
        /// Edge Cases: ReferenceEquals short-circuits for identical references.
        /// </remarks>
        public bool Equals(T x, T y)
        {
            if (IsReferenceType)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }
            }

            using PooledResource<PooledBufferStream> aLease = PooledBufferStream.Rent(
                out PooledBufferStream a
            );
            using PooledResource<PooledBufferStream> bLease = PooledBufferStream.Rent(
                out PooledBufferStream b
            );

            Type declared = typeof(T);
            bool useRuntime = Serializer.ShouldUseRuntimeTypeForProtobuf(
                declared,
                x,
                forceRuntimeType: false
            );
            if (useRuntime)
            {
                PbSerializer.NonGeneric.Serialize(a, x);
                PbSerializer.NonGeneric.Serialize(b, y);
            }
            else
            {
                PbSerializer.Serialize(a, x);
                PbSerializer.Serialize(b, y);
            }

            return ProtoBufferComparer.StreamContentEquals(a, b);
        }

        /// <summary>
        /// Returns a hash code for the object based on its protobuf serialization using FNV-1a algorithm.
        /// </summary>
        /// <param name="obj">The object to compute a hash code for.</param>
        /// <returns>A 32-bit hash code computed from the object's serialized bytes using FNV-1a.</returns>
        /// <remarks>
        /// Null Handling: Behavior for null depends on protobuf serialization handling.
        /// Thread Safety: Thread-safe if obj is not modified during hashing.
        /// Performance: O(n) where n is serialized size - requires full serialization.
        /// Allocations: Uses pooled buffer - no heap allocations for stream.
        /// Edge Cases: Hash collisions possible but minimized by FNV-1a algorithm.
        /// Objects that serialize to same bytes will have same hash code (required for equality contract).
        /// </remarks>
        public int GetHashCode(T obj)
        {
            using PooledResource<PooledBufferStream> sLease = PooledBufferStream.Rent(
                out PooledBufferStream s
            );

            Type declared = typeof(T);
            bool useRuntime = Serializer.ShouldUseRuntimeTypeForProtobuf(
                declared,
                obj,
                forceRuntimeType: false
            );
            if (useRuntime)
            {
                PbSerializer.NonGeneric.Serialize(s, obj);
            }
            else
            {
                PbSerializer.Serialize(s, obj);
            }

            return ProtoBufferComparer.Fnv1A32(s);
        }
    }

    /// <summary>
    /// Internal helper class for comparing protobuf-serialized stream contents and computing hash codes.
    /// </summary>
    internal static class ProtoBufferComparer
    {
        /// <summary>
        /// Compares the written content of two PooledBufferStream instances for byte-wise equality.
        /// </summary>
        /// <param name="a">The first stream to compare.</param>
        /// <param name="b">The second stream to compare.</param>
        /// <returns>True if both streams contain identical byte sequences, false otherwise.</returns>
        /// <remarks>
        /// Null Handling: null == null returns true, null vs non-null returns false.
        /// Thread Safety: Thread-safe if streams are not modified during comparison.
        /// Performance: O(n) where n is byte count - uses optimized Span.SequenceEqual for SIMD acceleration.
        /// Allocations: No heap allocations - operates on ArraySegments and Spans.
        /// Edge Cases: ReferenceEquals short-circuits for same stream instance.
        /// Different-length streams return false immediately without byte comparison.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StreamContentEquals(PooledBufferStream a, PooledBufferStream b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null)
            {
                return false;
            }

            ArraySegment<byte> segA = a.GetWrittenSegment();
            ArraySegment<byte> segB = b.GetWrittenSegment();

            if (segA.Count != segB.Count)
            {
                return false;
            }

            // Use Span<T>.SequenceEqual for efficient memory comparison
            return segA.AsSpan().SequenceEqual(segB.AsSpan());
        }

        /// <summary>
        /// Computes a 32-bit FNV-1a hash code from the written content of a PooledBufferStream.
        /// </summary>
        /// <param name="s">The stream whose content to hash.</param>
        /// <returns>A 32-bit FNV-1a hash code computed from the stream's byte content.</returns>
        /// <remarks>
        /// Null Handling: Assumes stream is non-null (internal use only).
        /// Thread Safety: Thread-safe if stream is not modified during hashing.
        /// Performance: O(n) where n is byte count - iterates once through all bytes.
        /// Allocations: No heap allocations - operates on ArraySegment.
        /// Edge Cases: Uses FNV-1a algorithm with standard constants (offset=2166136261, prime=16777619).
        /// Empty streams produce the FNV offset basis hash. Unchecked arithmetic prevents overflow exceptions.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Fnv1A32(PooledBufferStream s)
        {
            const uint fnvOffset = 2166136261;
            const uint fnvPrime = 16777619;
            uint hash = fnvOffset;

            ArraySegment<byte> seg = s.GetWrittenSegment();
            foreach (byte element in seg)
            {
                hash ^= element;
                hash *= fnvPrime;
            }
            return unchecked((int)hash);
        }
    }
}
