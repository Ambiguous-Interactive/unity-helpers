// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    [TestFixture]
    public sealed class PublicStructEqualityContractTests
    {
        private static IEnumerable<TestCaseData> ContractCases()
        {
            List<TestCaseData> contracts = new(400);
            AddCases<global::WallstopStudios.UnityHelpers.Tags.AttributeMetadataCache.ResolvedRelationalFieldMetadata>(
                contracts,
                "Tags.AttributeMetadataCache.ResolvedRelationalFieldMetadata"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.AttributeModification>(
                contracts,
                "Tags.AttributeModification"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.EffectBehaviorContext>(
                contracts,
                "Tags.EffectBehaviorContext"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.PeriodicEffectTickContext>(
                contracts,
                "Tags.PeriodicEffectTickContext"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.EffectHandle>(
                contracts,
                "Tags.EffectHandle"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.TagHandler.ActiveTagEnumerable>(
                contracts,
                "Tags.TagHandler.ActiveTagEnumerable"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.TagHandler.HandleEnumerable>(
                contracts,
                "Tags.TagHandler.HandleEnumerable"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.TagHandler.HandleEnumerator>(
                contracts,
                "Tags.TagHandler.HandleEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Tags.TagHandler.ActiveTagEnumerator>(
                contracts,
                "Tags.TagHandler.ActiveTagEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.WaitInstructionCacheDiagnostics>(
                contracts,
                "Utils.WaitInstructionCacheDiagnostics"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.PooledArray<int>>(
                contracts,
                "Utils.PooledArray<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.PooledResource<int>>(
                contracts,
                "Utils.PooledResource<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.DeferredDisposalResult<int>>(
                contracts,
                "Utils.DeferredDisposalResult<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.DisposableScope>(
                contracts,
                "Utils.DisposableScope"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.DisposableScope<global::WallstopStudios.UnityHelpers.Utils.DisposableScope>>(
                contracts,
                "Utils.DisposableScope<global::WallstopStudios.UnityHelpers.Utils.DisposableScope>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.GlobalPoolStatistics>(
                contracts,
                "Utils.GlobalPoolStatistics"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.PoolPurgeEffectiveOptions>(
                contracts,
                "Utils.PoolPurgeEffectiveOptions"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.PoolStatistics>(
                contracts,
                "Utils.PoolStatistics"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.PoolFrequencyStatistics>(
                contracts,
                "Utils.PoolFrequencyStatistics"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.ScriptableObjectSingletonMetadata.Entry>(
                contracts,
                "Utils.ScriptableObjectSingletonMetadata.Entry"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Utils.SpriteAlphaMask>(
                contracts,
                "Utils.SpriteAlphaMask"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Visuals.AnimatedSpriteLayer>(
                contracts,
                "Visuals.AnimatedSpriteLayer"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.BitSet.BitEnumerator>(
                contracts,
                "Core.DataStructure.BitSet.BitEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.BoundingBox3D>(
                contracts,
                "Core.DataStructure.BoundingBox3D"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.CacheBuilder<
                int,
                int
            >>(contracts, "Core.DataStructure.CacheBuilder<int,int>");
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.CacheOptions<
                int,
                int
            >>(contracts, "Core.DataStructure.CacheOptions<int,int>");
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.CacheStatistics>(
                contracts,
                "Core.DataStructure.CacheStatistics"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Circle>(
                contracts,
                "Core.DataStructure.Circle"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.CyclicBuffer<int>.CyclicBufferEnumerator>(
                contracts,
                "Core.DataStructure.CyclicBuffer<int>.CyclicBufferEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Deque<int>.DequeEnumerator>(
                contracts,
                "Core.DataStructure.Deque<int>.DequeEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Heap<int>.HeapEnumerator>(
                contracts,
                "Core.DataStructure.Heap<int>.HeapEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.ImmutableBitSet>(
                contracts,
                "Core.DataStructure.ImmutableBitSet"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.ImmutableBitSet.BitEnumerator>(
                contracts,
                "Core.DataStructure.ImmutableBitSet.BitEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.Enumerator>(
                contracts,
                "Core.DataStructure.IntMap<int>.Enumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.KeyView>(
                contracts,
                "Core.DataStructure.IntMap<int>.KeyView"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.ValueView>(
                contracts,
                "Core.DataStructure.IntMap<int>.ValueView"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.KeyEnumerator>(
                contracts,
                "Core.DataStructure.IntMap<int>.KeyEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.IntMap<int>.ValueEnumerator>(
                contracts,
                "Core.DataStructure.IntMap<int>.ValueEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.KdTree2D<int>.Entry>(
                contracts,
                "Core.DataStructure.KdTree2D<int>.Entry"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.OctTree3D<int>.Entry>(
                contracts,
                "Core.DataStructure.OctTree3D<int>.Entry"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.OctTree3D<int>.BoundsQueryNodeTrace>(
                contracts,
                "Core.DataStructure.OctTree3D<int>.BoundsQueryNodeTrace"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.QuadTree2D<int>.Entry>(
                contracts,
                "Core.DataStructure.QuadTree2D<int>.Entry"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.SparseSet.SparseSetEnumerator>(
                contracts,
                "Core.DataStructure.SparseSet.SparseSetEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.SparseSet<int>.SparseSetEnumerator>(
                contracts,
                "Core.DataStructure.SparseSet<int>.SparseSetEnumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Sphere>(
                contracts,
                "Core.DataStructure.Sphere"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Trie.Enumerator>(
                contracts,
                "Core.DataStructure.Trie.Enumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Trie<int>.Enumerator>(
                contracts,
                "Core.DataStructure.Trie<int>.Enumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueDictionaryEnumerable<
                int,
                int
            >>(contracts, "Core.DataStructure.ValueDictionaryEnumerable<int,int>");
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueDictionaryEnumerator<
                int,
                int
            >>(contracts, "Core.DataStructure.ValueDictionaryEnumerator<int,int>");
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueEnumerable<int>>(
                contracts,
                "Core.DataStructure.ValueEnumerable<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.ValueEnumerator<int>>(
                contracts,
                "Core.DataStructure.ValueEnumerator<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Extension.AsyncOperationExtensions.AsyncOperationAwaiter>(
                contracts,
                "Core.Extension.AsyncOperationExtensions.AsyncOperationAwaiter"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Helper.RestorableGlobal<int>.Scope>(
                contracts,
                "Core.Helper.RestorableGlobal<int>.Scope"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Math.Line2D>(
                contracts,
                "Core.Math.Line2D"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Math.Line3D>(
                contracts,
                "Core.Math.Line3D"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Math.Parabola>(
                contracts,
                "Core.Math.Parabola"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Math.Range<int>>(
                contracts,
                "Core.Math.Range<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<int, int, int>>(
                contracts,
                "Core.OneOf.FastOneOf<int,int,int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<int, int>>(
                contracts,
                "Core.OneOf.FastOneOf<int,int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<int, int, int, int>>(
                contracts,
                "Core.OneOf.FastOneOf<int,int,int,int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.OneOf.None>(
                contracts,
                "Core.OneOf.None"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Random.NativePcgRandom>(
                contracts,
                "Core.Random.NativePcgRandom"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Random.RandomGeneratorMetadata>(
                contracts,
                "Core.Random.RandomGeneratorMetadata"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Random.RandomState>(
                contracts,
                "Core.Random.RandomState"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate>(
                contracts,
                "Core.Serialization.BoneWeightSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate>(
                contracts,
                "Core.Serialization.KeyframeSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate>(
                contracts,
                "Core.Serialization.Matrix4x4Surrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2Surrogate>(
                contracts,
                "Core.Serialization.Vector2Surrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate>(
                contracts,
                "Core.Serialization.Vector3Surrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate>(
                contracts,
                "Core.Serialization.QuaternionSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate>(
                contracts,
                "Core.Serialization.ColorSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate>(
                contracts,
                "Core.Serialization.Color32Surrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate>(
                contracts,
                "Core.Serialization.RectSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate>(
                contracts,
                "Core.Serialization.RectIntSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate>(
                contracts,
                "Core.Serialization.BoundsSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate>(
                contracts,
                "Core.Serialization.BoundsIntSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2IntSurrogate>(
                contracts,
                "Core.Serialization.Vector2IntSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate>(
                contracts,
                "Core.Serialization.Vector3IntSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate>(
                contracts,
                "Core.Serialization.ResolutionSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate>(
                contracts,
                "Core.Serialization.ParabolaSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.ImmutableBitSetSurrogate>(
                contracts,
                "Core.Serialization.ImmutableBitSetSurrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate>(
                contracts,
                "Core.Serialization.Vector4Surrogate"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Threading.SemaphoreLease>(
                contracts,
                "Core.Threading.SemaphoreLease"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.FastVector2Int>(
                contracts,
                "Core.DataStructure.Adapters.FastVector2Int"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.FastVector3Int>(
                contracts,
                "Core.DataStructure.Adapters.FastVector3Int"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableDictionaryBase<
                int,
                int,
                int
            >.Enumerator>(
                contracts,
                "Core.DataStructure.Adapters.SerializableDictionaryBase<int,int,int>.Enumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableNullable<int>>(
                contracts,
                "Core.DataStructure.Adapters.SerializableNullable<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableSortedDictionaryBase<
                int,
                int,
                int
            >.Enumerator>(
                contracts,
                "Core.DataStructure.Adapters.SerializableSortedDictionaryBase<int,int,int>.Enumerator"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableType>(
                contracts,
                "Core.DataStructure.Adapters.SerializableType"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableTypeCatalog.SerializableTypeDescriptor>(
                contracts,
                "Core.DataStructure.Adapters.SerializableTypeCatalog.SerializableTypeDescriptor"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableValueTuple<
                int,
                int
            >>(contracts, "Core.DataStructure.Adapters.SerializableValueTuple<int,int>");
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableValueTuple<
                int,
                int,
                int
            >>(contracts, "Core.DataStructure.Adapters.SerializableValueTuple<int,int,int>");
            AddCases<global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.WGuid>(
                contracts,
                "Core.DataStructure.Adapters.WGuid"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Extension.UnityExtensions.ConcaveHullOptions>(
                contracts,
                "Core.Extension.UnityExtensions.ConcaveHullOptions"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Extension.UnityExtensions.ConcaveHullOptions.Builder>(
                contracts,
                "Core.Extension.UnityExtensions.ConcaveHullOptions.Builder"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Helper.Logging.DecorationEntry>(
                contracts,
                "Core.Helper.Logging.DecorationEntry"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoArrayBuilder<int>>(
                contracts,
                "Core.Serialization.WallstopProto.WProtoArrayBuilder<int>"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoConstruct>(
                contracts,
                "Core.Serialization.WallstopProto.WProtoConstruct"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoLengthToken>(
                contracts,
                "Core.Serialization.WallstopProto.WProtoLengthToken"
            );
            AddCases<global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoWriteResult>(
                contracts,
                "Core.Serialization.WallstopProto.WProtoWriteResult"
            );
            return contracts;
        }

        private static void AddCases<T>(List<TestCaseData> contracts, string typeName)
            where T : struct, IEquatable<T>
        {
            contracts.Add(
                new TestCaseData((Action)DefaultValueIsReflexiveAndSymmetric<T>).SetName(
                    $"{typeName}.{nameof(DefaultValueIsReflexiveAndSymmetric)}"
                )
            );
            contracts.Add(
                new TestCaseData(
                    (Action)ObjectEqualityAgreesAndRejectsNullAndOtherTypes<T>
                ).SetName($"{typeName}.{nameof(ObjectEqualityAgreesAndRejectsNullAndOtherTypes)}")
            );
            contracts.Add(
                new TestCaseData((Action)EqualDefaultValuesHaveMatchingHashes<T>).SetName(
                    $"{typeName}.{nameof(EqualDefaultValuesHaveMatchingHashes)}"
                )
            );
            contracts.Add(
                new TestCaseData((Action)GenericHashCollectionsFindAnEqualValue<T>).SetName(
                    $"{typeName}.{nameof(GenericHashCollectionsFindAnEqualValue)}"
                )
            );
        }

        private static void DefaultValueIsReflexiveAndSymmetric<T>()
            where T : struct, IEquatable<T>
        {
            T first = default;
            T second = default;
            Assert.That(first.Equals(second), Is.True);
            Assert.That(second.Equals(first), Is.True);
            Assert.That(EqualityComparer<T>.Default.Equals(first, second), Is.True);
        }

        private static void ObjectEqualityAgreesAndRejectsNullAndOtherTypes<T>()
            where T : struct, IEquatable<T>
        {
            T value = default;
            object boxed = value;
            Assert.That(value.Equals(boxed), Is.True);
            Assert.That(value.Equals(null), Is.False);
            Assert.That(value.Equals(new object()), Is.False);
        }

        private static void EqualDefaultValuesHaveMatchingHashes<T>()
            where T : struct, IEquatable<T>
        {
            T first = default;
            T second = default;
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(
                EqualityComparer<T>.Default.GetHashCode(first),
                Is.EqualTo(first.GetHashCode())
            );
        }

        private static void GenericHashCollectionsFindAnEqualValue<T>()
            where T : struct, IEquatable<T>
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

        [TestCaseSource(nameof(ContractCases))]
        public void EveryPublicStructSatisfiesEqualityContract(Action assertion)
        {
            assertion();
        }
    }
}
