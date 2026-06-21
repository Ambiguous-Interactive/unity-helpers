// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;

    /// <summary>
    /// Force-references the closed generic serializer instantiations and surrogate/wrapper types that
    /// our protobuf and System.Text.Json paths reach only through reflection (MakeGenericMethod, the
    /// protobuf runtime model, and STJ metadata). Without these explicit references IL2CPP strips the
    /// code, and the AOT compiler never emits the generic specializations, producing runtime
    /// "no AOT code" / GetTypeModifiers failures. The guarded body never runs; it exists purely so the
    /// linker and AOT compiler retain the required IL.
    /// </summary>
    [Preserve]
    internal static class SerializationAotHints
    {
        // Surrogate types routed through protobuf-net's surrogate path (Class B). Preserving them keeps
        // their conversion operators and contracts available for the runtime model build under AOT.
        [Preserve]
        private static readonly Type[] PreservedSurrogateTypes =
        {
            typeof(FastVector2IntSurrogate),
            typeof(FastVector3IntSurrogate),
            typeof(ParabolaSurrogate),
            typeof(ImmutableBitSetSurrogate),
        };

        // Wrapper POCOs used to round-trip Deque/CyclicBuffer/SparseSet (Class A). The closed generics
        // listed here ensure DequeProtoWrapper<T>/CyclicBufferProtoWrapper<T> (and their T[] payloads,
        // which protobuf-net's runtime model walks) are emitted for the common value-type element types
        // the package round-trips, plus the List<int>/int[] element paths System.Text.Json walks.
        // Reference-type element types share one generic specialization, so a single reference anchor
        // covers every class element.
        [Preserve]
        private static readonly Type[] PreservedWrapperTypes =
        {
            typeof(DequeProtoWrapper<int>),
            typeof(DequeProtoWrapper<long>),
            typeof(DequeProtoWrapper<float>),
            typeof(DequeProtoWrapper<double>),
            typeof(DequeProtoWrapper<bool>),
            typeof(DequeProtoWrapper<string>),
            typeof(DequeProtoWrapper<Vector2>),
            typeof(DequeProtoWrapper<Vector3>),
            typeof(DequeProtoWrapper<Vector2Int>),
            typeof(DequeProtoWrapper<Vector3Int>),
            typeof(DequeProtoWrapper<FastVector2Int>),
            typeof(DequeProtoWrapper<FastVector3Int>),
            typeof(CyclicBufferProtoWrapper<int>),
            typeof(CyclicBufferProtoWrapper<long>),
            typeof(CyclicBufferProtoWrapper<float>),
            typeof(CyclicBufferProtoWrapper<double>),
            typeof(CyclicBufferProtoWrapper<bool>),
            typeof(CyclicBufferProtoWrapper<string>),
            typeof(CyclicBufferProtoWrapper<Vector2>),
            typeof(CyclicBufferProtoWrapper<Vector3>),
            typeof(CyclicBufferProtoWrapper<Vector2Int>),
            typeof(CyclicBufferProtoWrapper<Vector3Int>),
            typeof(CyclicBufferProtoWrapper<FastVector2Int>),
            typeof(CyclicBufferProtoWrapper<FastVector3Int>),
            typeof(SparseSetProtoWrapper),
            typeof(List<int>),
            typeof(int[]),
            typeof(long[]),
            typeof(float[]),
            typeof(double[]),
            typeof(bool[]),
            typeof(string[]),
            typeof(Vector2[]),
            typeof(Vector3[]),
            typeof(Vector2Int[]),
            typeof(Vector3Int[]),
            typeof(FastVector2Int[]),
            typeof(FastVector3Int[]),
        };

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void ForceReferenceSerializationTypes()
        {
            // Touch the preserved type tables so the arrays (and their referenced types) are retained.
            if (PreservedSurrogateTypes.Length == 0 || PreservedWrapperTypes.Length == 0)
            {
                return;
            }

            // Statically reference the closed generic wrapper helpers so the AOT compiler emits
            // SerializeDequeWrapper<int>, DeserializeDequeWrapper<int>, etc. -- the exact
            // specializations the reflection dispatch in Serializer invokes at runtime. The guard is
            // always false at runtime (so nothing executes), but IL2CPP still compiles the IL inside.
            if (!ShouldNeverRun)
            {
                return;
            }

            // Deque<T>/CyclicBuffer<T> dispatch their protobuf wrappers via MakeGenericMethod(elementType),
            // so the AOT compiler only emits a specialization if it is statically referenced here. Cover
            // the common value-type element types the package realistically round-trips. Reference-type
            // element types (string and any class) all share one shared generic specialization, so a
            // single reference-type anchor (string) keeps that path alive for every class element.
            ReferenceDequeHelpers<int>();
            ReferenceDequeHelpers<long>();
            ReferenceDequeHelpers<float>();
            ReferenceDequeHelpers<double>();
            ReferenceDequeHelpers<bool>();
            ReferenceDequeHelpers<string>();
            ReferenceDequeHelpers<Vector2>();
            ReferenceDequeHelpers<Vector3>();
            ReferenceDequeHelpers<Vector2Int>();
            ReferenceDequeHelpers<Vector3Int>();
            ReferenceDequeHelpers<FastVector2Int>();
            ReferenceDequeHelpers<FastVector3Int>();

            ReferenceCyclicBufferHelpers<int>();
            ReferenceCyclicBufferHelpers<long>();
            ReferenceCyclicBufferHelpers<float>();
            ReferenceCyclicBufferHelpers<double>();
            ReferenceCyclicBufferHelpers<bool>();
            ReferenceCyclicBufferHelpers<string>();
            ReferenceCyclicBufferHelpers<Vector2>();
            ReferenceCyclicBufferHelpers<Vector3>();
            ReferenceCyclicBufferHelpers<Vector2Int>();
            ReferenceCyclicBufferHelpers<Vector3Int>();
            ReferenceCyclicBufferHelpers<FastVector2Int>();
            ReferenceCyclicBufferHelpers<FastVector3Int>();

            ReferenceSparseSetHelpers();
            ReferenceSurrogateConversions();
            ReferenceJsonElementPaths();
        }

        // A runtime-false guard the compiler cannot trivially fold away into removing the references.
        private static bool ShouldNeverRun => PreservedWrapperTypes.Length < 0;

        private static void ReferenceDequeHelpers<T>()
        {
            Deque<T> deque = new(Deque<T>.DefaultCapacity);
            byte[] bytes = Serializer.SerializeDequeWrapper(deque);
            _ = Serializer.DeserializeDequeWrapper<T>(bytes);
        }

        private static void ReferenceCyclicBufferHelpers<T>()
        {
            CyclicBuffer<T> buffer = new(1);
            byte[] bytes = Serializer.SerializeCyclicBufferWrapper(buffer);
            _ = Serializer.DeserializeCyclicBufferWrapper<T>(bytes);
        }

        private static void ReferenceSparseSetHelpers()
        {
            SparseSet set = new(1);
            byte[] bytes = Serializer.SerializeSparseSetWrapper(set);
            _ = Serializer.DeserializeSparseSetWrapper(bytes);
        }

        private static void ReferenceSurrogateConversions()
        {
            // Exercise both directions of each surrogate's conversion operators so IL2CPP keeps them.
            FastVector2IntSurrogate v2 = new FastVector2Int(0, 0);
            FastVector2Int unusedV2 = v2;

            FastVector3IntSurrogate v3 = new FastVector3Int(0, 0, 0);
            FastVector3Int unusedV3 = v3;

            ParabolaSurrogate parabola = Parabola.FromCoefficients(-1f, 1f, 1f);
            Parabola unusedParabola = parabola;

            ImmutableBitSetSurrogate bitSet = new BitSet(1).ToImmutable();
            ImmutableBitSet unusedBitSet = bitSet;

            GC.KeepAlive(unusedV2);
            GC.KeepAlive(unusedV3);
            GC.KeepAlive(unusedParabola);
            GC.KeepAlive(unusedBitSet);
        }

        private static void ReferenceJsonElementPaths()
        {
            // STJ array/list element paths used by the wrapper payloads and the reflection-light
            // object writer; reference the closed generics so the converters are emitted.
            List<int> list = new() { 0 };
            int[] array = list.ToArray();
            _ = Serializer.JsonStringify(array);
            _ = Serializer.JsonStringify(list);
        }
    }
}
