// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Tags.AttributeMetadataCache.ResolvedRelationalFieldMetadata)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.AttributeModification))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.EffectBehaviorContext))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.PeriodicEffectTickContext))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.EffectHandle))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.TagHandler.ActiveTagEnumerable))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.TagHandler.HandleEnumerable))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.TagHandler.HandleEnumerator))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Tags.TagHandler.ActiveTagEnumerator))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Utils.WaitInstructionCacheDiagnostics)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.PooledArray<int>))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.PooledResource<int>))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.DeferredDisposalResult<int>))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.DisposableScope))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Utils.DisposableScope<global::WallstopStudios.UnityHelpers.Utils.DisposableScope>)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.GlobalPoolStatistics))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.PoolPurgeEffectiveOptions))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.PoolStatistics))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.PoolFrequencyStatistics))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Utils.ScriptableObjectSingletonMetadata.Entry)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Utils.SpriteAlphaMask))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Visuals.AnimatedSpriteLayer))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.BitSet.BitEnumerator)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.BoundingBox3D))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.CacheBuilder<int, int>)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.CacheOptions<int, int>)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.CacheStatistics))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Circle))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.CyclicBuffer<int>.CyclicBufferEnumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Deque<int>.DequeEnumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Heap<int>.HeapEnumerator)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.ImmutableBitSet))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.ImmutableBitSet.BitEnumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.Enumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.KeyView)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.ValueView)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.KeyEnumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.ValueEnumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.KdTree2D<int>.Entry)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.OctTree3D<int>.Entry)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.OctTree3D<int>.BoundsQueryNodeTrace)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.QuadTree2D<int>.Entry)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.SparseSet.SparseSetEnumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.SparseSet<int>.SparseSetEnumerator)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Sphere))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Trie.Enumerator))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Trie<int>.Enumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueDictionaryEnumerable<
            int,
            int
        >)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueDictionaryEnumerator<
            int,
            int
        >)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueEnumerable<int>)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueEnumerator<int>)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Extension.AsyncOperationExtensions.AsyncOperationAwaiter)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Helper.RestorableGlobal<int>.Scope)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Math.Line2D))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Math.Line3D))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Math.Parabola))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Math.Range<int>))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<int, int, int>))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<int, int>))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<int, int, int, int>)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.OneOf.None))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Random.NativePcgRandom))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Random.RandomGeneratorMetadata))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Random.RandomState))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2Surrogate))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2IntSurrogate)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.ImmutableBitSetSurrogate)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate))]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Threading.SemaphoreLease))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.FastVector2Int)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.FastVector3Int)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableDictionaryBase<
            int,
            int,
            int
        >.Enumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableNullable<int>)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableSortedDictionaryBase<
            int,
            int,
            int
        >.Enumerator)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableType)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableTypeCatalog.SerializableTypeDescriptor)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableValueTuple<
            int,
            int
        >)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableValueTuple<
            int,
            int,
            int
        >)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.WGuid))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Extension.UnityExtensions.ConcaveHullOptions)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Extension.UnityExtensions.ConcaveHullOptions.Builder)
    )]
    [TestFixture(typeof(global::WallstopStudios.UnityHelpers.Core.Helper.Logging.DecorationEntry))]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoArrayBuilder<int>)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoConstruct)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoLengthToken)
    )]
    [TestFixture(
        typeof(global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoWriteResult)
    )]
    public sealed class PublicStructEqualityContractTests<T>
        where T : struct, IEquatable<T>
    {
        [Test]
        public void DefaultValueIsReflexiveAndSymmetric()
        {
            T first = default;
            T second = default;
            Assert.That(first.Equals(second), Is.True);
            Assert.That(second.Equals(first), Is.True);
            Assert.That(EqualityComparer<T>.Default.Equals(first, second), Is.True);
        }

        [Test]
        public void ObjectEqualityAgreesAndRejectsNullAndOtherTypes()
        {
            T value = default;
            object boxed = value;
            Assert.That(value.Equals(boxed), Is.True);
            Assert.That(value.Equals(null), Is.False);
            Assert.That(value.Equals(new object()), Is.False);
        }

        [Test]
        public void EqualDefaultValuesHaveMatchingHashes()
        {
            T first = default;
            T second = default;
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(
                EqualityComparer<T>.Default.GetHashCode(first),
                Is.EqualTo(first.GetHashCode())
            );
        }

        [Test]
        public void GenericHashCollectionsFindAnEqualValue()
        {
            T first = default;
            T second = default;
            HashSet<T> values = new() { first };
            Assert.That(values.Add(second), Is.False);
            Assert.That(values.Contains(second), Is.True);
            Dictionary<T, int> map = new() { [first] = 7 };
            Assert.That(map.TryGetValue(second, out int found), Is.True);
            Assert.That(found, Is.EqualTo(7));
        }
    }
}
