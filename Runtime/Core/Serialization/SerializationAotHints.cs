// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System.Collections.Generic;
    using ProtoBuf.Serializers;
    using UnityEngine;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;

    /// <summary>
    /// Roots, for the IL2CPP AOT compiler, the closed generic protobuf serializer instantiations the
    /// runtime serializer reaches only through reflection. The runtime path serializes the package's
    /// collection wrappers and surrogate-backed types via <c>ProtoBuf.Serializer.NonGeneric</c> plus
    /// <c>MakeGenericType</c>/<c>MakeGenericMethod</c>; protobuf-net itself then resolves its internal
    /// per-element serializers (<c>RepeatedSerializer.CreateVector&lt;T&gt;</c> for each <c>T[]</c> member,
    /// <c>CreateEnumerable&lt;TCollection,T&gt;</c> for directly-serialized collections) through reflected
    /// <c>MethodInfo.MakeGenericMethod(...).Invoke(...)</c> calls. Neither the reflective runtime path NOR a
    /// generic <c>Serializer.Serialize&lt;T&gt;</c> reference gives IL2CPP a static call site to those closed
    /// instantiations, so the AOT compiler never emits them and the player throws
    /// <c>ExecutionEngineException</c> ("no AOT code was generated") at runtime.
    ///
    /// The fix is to issue a DIRECT static call to each leaf generic protobuf-net exposes publicly
    /// (<see cref="RepeatedSerializer.CreateVector{T}"/>, <see cref="RepeatedSerializer.CreateEnumerable{TCollection, T}"/>)
    /// for every element/collection type the package round-trips, and to <c>new</c> every closed wrapper so
    /// IL2CPP emits its constructor. A static call site is exactly what IL2CPP follows to emit a closed
    /// generic. The guarded body never runs; it exists purely so the AOT compiler retains the IL.
    ///
    /// Coverage is the package's own proto-serializable types plus the primitive element types it round-trips.
    /// A consumer that protobuf-serializes a wrapper over a custom VALUE element type (e.g. <c>Deque&lt;MyStruct&gt;</c>)
    /// on IL2CPP must add an equivalent <c>CreateVector&lt;MyStruct&gt;()</c> hint; reference-type elements need no
    /// addition because IL2CPP shares one specialization across all of them.
    ///
    /// Limitation: protobuf-net's <c>ProtoBuf.Internal.StructValueChecker&lt;T&gt;</c> (built for surrogate-backed
    /// VALUE types) is <c>internal</c> and reached only via reflection, so it cannot be rooted by a static
    /// reference from this assembly. <see cref="ReferenceStructValueTypePaths"/> roots the value types' generic
    /// usage (equality comparers + a generic round-trip) as the best available influence on the AOT walk; if
    /// <c>StructValueChecker&lt;T&gt;</c> AOT errors persist for a value type on a standalone player, that type
    /// must move to a reference-type (class) surrogate or the model must be precompiled.
    /// </summary>
    [Preserve]
    internal static class SerializationAotHints
    {
        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void ForceReferenceSerializationTypes()
        {
            // Ensure the surrogate/model registration ran so the surrogate-backed paths resolve.
            ProtobufUnityModel.EnsureInitialized();

            // The guard reads a runtime field so the body is statically reachable IL (which IL2CPP
            // compiles, emitting the closed generics referenced below) yet never executes at runtime.
            if (NeverRun)
            {
                RootRepeatedElementSerializers();
                RootWrapperConstructors();
                RootDirectlySerializedCollections();
                ReferenceStructValueTypePaths();
            }
        }

        // Runtime-evaluated guard. Always false at runtime, but seeded from a value the compiler and the
        // IL2CPP whole-program stripper cannot prove (Environment.TickCount is never int.MinValue), so the
        // guarded branch is never const-folded away and IL2CPP emits the closed generics referenced below.
        private static bool NeverRun => RootCount < 0;

        private static readonly int RootCount =
            System.Environment.TickCount == int.MinValue ? -1 : 1;

        // Element types the package serializes inside T[] members of its proto wrappers (Deque, CyclicBuffer,
        // SparseSet, Serializable*Set, Serializable*Dictionary) and directly (BitSet/ImmutableBitSet ulong[]).
        // Each CreateVector<T> call gives IL2CPP the static site for protobuf-net's vector serializer over T.
        private static void RootRepeatedElementSerializers()
        {
            _ = RepeatedSerializer.CreateVector<int>();
            _ = RepeatedSerializer.CreateVector<uint>();
            _ = RepeatedSerializer.CreateVector<long>();
            _ = RepeatedSerializer.CreateVector<ulong>();
            _ = RepeatedSerializer.CreateVector<short>();
            _ = RepeatedSerializer.CreateVector<ushort>();
            _ = RepeatedSerializer.CreateVector<byte>();
            _ = RepeatedSerializer.CreateVector<sbyte>();
            _ = RepeatedSerializer.CreateVector<float>();
            _ = RepeatedSerializer.CreateVector<double>();
            _ = RepeatedSerializer.CreateVector<bool>();
            _ = RepeatedSerializer.CreateVector<string>();
        }

        // Rooting a closed wrapper's constructor lets the reflective Activator.CreateInstance /
        // MakeGenericType runtime path find AOT code for it. Reference-type element wrappers share a single
        // specialization, so the string arm covers every class element.
        private static void RootWrapperConstructors()
        {
            RootElementWrapperConstructors<int>();
            RootElementWrapperConstructors<long>();
            RootElementWrapperConstructors<ulong>();
            RootElementWrapperConstructors<float>();
            RootElementWrapperConstructors<double>();
            RootElementWrapperConstructors<bool>();
            RootElementWrapperConstructors<string>();

            RootDictionaryWrapperConstructors<int, int>();
            RootDictionaryWrapperConstructors<int, long>();
            RootDictionaryWrapperConstructors<int, float>();
            RootDictionaryWrapperConstructors<int, double>();
            RootDictionaryWrapperConstructors<int, bool>();
            RootDictionaryWrapperConstructors<int, string>();
            RootDictionaryWrapperConstructors<string, int>();
            RootDictionaryWrapperConstructors<string, long>();
            RootDictionaryWrapperConstructors<string, float>();
            RootDictionaryWrapperConstructors<string, bool>();
            RootDictionaryWrapperConstructors<string, string>();

            _ = new SparseSetProtoWrapper();
        }

        private static void RootElementWrapperConstructors<T>()
        {
            _ = new DequeProtoWrapper<T>();
            _ = new CyclicBufferProtoWrapper<T>();
            _ = new SerializableHashSetProtoWrapper<T>();
            _ = new SerializableSortedSetProtoWrapper<T>();
        }

        private static void RootDictionaryWrapperConstructors<TKey, TValue>()
        {
            _ = new SerializableDictionaryProtoWrapper<TKey, TValue>();
            _ = new SerializableSortedDictionaryProtoWrapper<TKey, TValue>();
        }

        // The Serializable*Set types implement IEnumerable and are sometimes handed straight to protobuf-net
        // (e.g. direct ProtoBuf.Serializer.Serialize of a SerializableHashSet), which resolves
        // CreateEnumerable<TCollection, TItem> reflectively. Root that closed generic for the set shapes used.
        private static void RootDirectlySerializedCollections()
        {
            _ = RepeatedSerializer.CreateEnumerable<SerializableHashSet<int>, int>();
            _ = RepeatedSerializer.CreateEnumerable<SerializableHashSet<string>, string>();
            _ = RepeatedSerializer.CreateEnumerable<SerializableSortedSet<int>, int>();
            _ = RepeatedSerializer.CreateEnumerable<SerializableSortedSet<string>, string>();
        }

        // Best-effort rooting for the surrogate-backed value types (Class B). StructValueChecker<T> is
        // internal and unreachable by direct reference; touching EqualityComparer<T>.Default plus a generic
        // protobuf round-trip realizes T's generic usage for the AOT walk. See the type-level remarks.
        private static void ReferenceStructValueTypePaths()
        {
            ReferenceValueType<FastVector2Int>();
            ReferenceValueType<FastVector3Int>();
            ReferenceValueType<Parabola>();
            ReferenceValueType<ImmutableBitSet>();
        }

        private static void ReferenceValueType<T>()
            where T : struct
        {
            _ = EqualityComparer<T>.Default;
            using System.IO.MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize<T>(stream, default);
            stream.Position = 0;
            _ = ProtoBuf.Serializer.Deserialize<T>(stream);
        }
    }
}
