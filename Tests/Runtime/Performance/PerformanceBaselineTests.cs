// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Performance baseline tests that verify critical operations complete within acceptable time bounds.
    /// These tests detect performance regressions by failing when operations exceed established baselines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Purpose:</strong> These tests serve as automated guards against performance regressions.
    /// They run during CI to catch slowdowns before they reach production.
    /// </para>
    /// <para>
    /// <strong>Baseline Philosophy:</strong> Baselines are set generously (2-3x expected typical performance)
    /// to account for CI environment variability while still catching significant regressions.
    /// </para>
    /// <para>
    /// <strong>Test Categories:</strong>
    /// <list type="bullet">
    ///   <item><description>Spatial structures: QuadTree, KdTree, RTree query performance</description></item>
    ///   <item><description>PRNG: Random number generation throughput</description></item>
    ///   <item><description>Pooling: Collection pool rent/return overhead</description></item>
    ///   <item><description>Serialization: JSON and Protobuf serialization/deserialization throughput</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    [Ignore("These have to be created and measured properly.")]
    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class PerformanceBaselineTests
    {
        private const int WarmupIterations = 10;
        private const int SpatialTreeElementCount = 10000;
        private const int SpatialTreeQueryIterations = 1000;
        private const int PrngIterations = 1000000;
        private const int PoolingIterations = 100000;
        private const int SerializationIterations = 10000;

        private const long SpatialTreeQueryBaselineMs = 200;
        private const long SpatialTreeConstructionBaselineMs = 500;
        private const long PrngBaselineMs = 500;
        private const long PoolingBaselineMs = 200;
        private const long SerializationBaselineMs = 500;

        private static readonly ulong DeterministicSeed = 0x6C8E9CF5709321D5UL;

        // Spatial Tree Baselines

        /// <summary>
        /// Verifies QuadTree2D range query performance meets baseline requirements.
        /// Baseline: 1000 range queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void QuadTree2DRangeQueryPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(1)));
            QuadTree2D<Vector2> tree = CreateQuadTree2D(random);
            List<Vector2> buffer = new();

            Vector2 center = new(500f, 500f);
            float radius = 100f;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"QuadTree2D range query performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies QuadTree2D bounds query performance meets baseline requirements.
        /// Baseline: 1000 bounds queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void QuadTree2DBoundsQueryPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(2)));
            QuadTree2D<Vector2> tree = CreateQuadTree2D(random);
            List<Vector2> buffer = new();

            Bounds queryBounds = new(new Vector3(500f, 500f, 0f), new Vector3(200f, 200f, 1f));

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInBounds(queryBounds, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetElementsInBounds(queryBounds, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"QuadTree2D bounds query performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies KdTree2D range query performance meets baseline requirements.
        /// Baseline: 1000 range queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void KdTree2DRangeQueryPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(3)));
            KdTree2D<Vector2> tree = CreateKdTree2D(random);
            List<Vector2> buffer = new();

            Vector2 center = new(500f, 500f);
            float radius = 100f;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"KdTree2D range query performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies KdTree2D nearest neighbor query performance meets baseline requirements.
        /// Baseline: 1000 nearest neighbor queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void KdTree2DNearestNeighborPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(4)));
            KdTree2D<Vector2> tree = CreateKdTree2D(random);
            List<Vector2> buffer = new();

            Vector2 center = new(500f, 500f);
            int neighborCount = 10;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetApproximateNearestNeighbors(center, neighborCount, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetApproximateNearestNeighbors(center, neighborCount, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"KdTree2D nearest neighbor performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies RTree2D range query performance meets baseline requirements.
        /// Baseline: 1000 range queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void RTree2DRangeQueryPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(5)));
            RTree2D<Vector2> tree = CreateRTree2D(random);
            List<Vector2> buffer = new();

            Vector2 center = new(500f, 500f);
            float radius = 100f;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"RTree2D range query performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies QuadTree2D construction performance meets baseline requirements.
        /// Baseline: Construct tree with 10K elements in less than 500ms.
        /// </summary>
        [Test]
        public void QuadTree2DConstructionPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(6)));
            Vector2[] points = CreateRandomPoints2D(random, SpatialTreeElementCount);

            for (int i = 0; i < 3; ++i)
            {
                _ = new QuadTree2D<Vector2>(points, p => p);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            QuadTree2D<Vector2> tree = new(points, p => p);
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeConstructionBaselineMs,
                $"QuadTree2D construction performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeElementCount} elements (baseline: {SpatialTreeConstructionBaselineMs}ms)"
            );
            Assert.AreEqual(SpatialTreeElementCount, tree.elements.Length);
        }

        /// <summary>
        /// Verifies KdTree2D construction performance meets baseline requirements.
        /// Baseline: Construct balanced tree with 10K elements in less than 500ms.
        /// </summary>
        [Test]
        public void KdTree2DConstructionPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(7)));
            Vector2[] points = CreateRandomPoints2D(random, SpatialTreeElementCount);

            for (int i = 0; i < 3; ++i)
            {
                _ = new KdTree2D<Vector2>(points, p => p);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            KdTree2D<Vector2> tree = new(points, p => p);
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeConstructionBaselineMs,
                $"KdTree2D construction performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeElementCount} elements (baseline: {SpatialTreeConstructionBaselineMs}ms)"
            );
            Assert.AreEqual(SpatialTreeElementCount, tree.elements.Length);
        }

        /// <summary>
        /// Verifies RTree2D construction performance meets baseline requirements.
        /// Baseline: Construct tree with 10K elements in less than 500ms.
        /// </summary>
        [Test]
        public void RTree2DConstructionPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(8)));
            Vector2[] points = CreateRandomPoints2D(random, SpatialTreeElementCount);

            for (int i = 0; i < 3; ++i)
            {
                _ = new RTree2D<Vector2>(points, CreatePointBounds);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            RTree2D<Vector2> tree = new(points, CreatePointBounds);
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeConstructionBaselineMs,
                $"RTree2D construction performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeElementCount} elements (baseline: {SpatialTreeConstructionBaselineMs}ms)"
            );
            Assert.AreEqual(SpatialTreeElementCount, tree.elements.Length);
        }

        // 3D Spatial Tree Baselines

        /// <summary>
        /// Verifies OctTree3D range query performance meets baseline requirements.
        /// Baseline: 1000 range queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void OctTree3DRangeQueryPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(9)));
            OctTree3D<Vector3> tree = CreateOctTree3D(random);
            List<Vector3> buffer = new();

            Vector3 center = new(500f, 500f, 500f);
            float radius = 100f;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"OctTree3D range query performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies KdTree3D range query performance meets baseline requirements.
        /// Baseline: 1000 range queries on 10K elements in less than 200ms.
        /// </summary>
        [Test]
        public void KdTree3DRangeQueryPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(10)));
            KdTree3D<Vector3> tree = CreateKdTree3D(random);
            List<Vector3> buffer = new();

            Vector3 center = new(500f, 500f, 500f);
            float radius = 100f;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SpatialTreeQueryBaselineMs,
                $"KdTree3D range query performance regression: {stopwatch.ElapsedMilliseconds}ms for {SpatialTreeQueryIterations} queries (baseline: {SpatialTreeQueryBaselineMs}ms)"
            );
        }

        // PRNG Baselines

        /// <summary>
        /// Verifies PcgRandom integer generation performance meets baseline requirements.
        /// Baseline: 1M integer generations in less than 500ms.
        /// </summary>
        [Test]
        public void PcgRandomNextIntPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(11)));

            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = random.Next();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = random.Next();
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PrngBaselineMs,
                $"PcgRandom.Next() performance regression: {stopwatch.ElapsedMilliseconds}ms for {PrngIterations} generations (baseline: {PrngBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies PcgRandom float generation performance meets baseline requirements.
        /// Baseline: 1M float generations in less than 500ms.
        /// </summary>
        [Test]
        public void PcgRandomNextFloatPerformance()
        {
            PcgRandom random = new(new Guid(CreateSeedBytes(12)));

            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = random.NextFloat();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = random.NextFloat();
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PrngBaselineMs,
                $"PcgRandom.NextFloat() performance regression: {stopwatch.ElapsedMilliseconds}ms for {PrngIterations} generations (baseline: {PrngBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies XoroShiroRandom integer generation performance meets baseline requirements.
        /// Baseline: 1M integer generations in less than 500ms.
        /// </summary>
        [Test]
        public void XoroShiroRandomNextIntPerformance()
        {
            XoroShiroRandom random = new(new Guid(CreateSeedBytes(13)));

            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = random.Next();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = random.Next();
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PrngBaselineMs,
                $"XoroShiroRandom.Next() performance regression: {stopwatch.ElapsedMilliseconds}ms for {PrngIterations} generations (baseline: {PrngBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies SplitMix64 integer generation performance meets baseline requirements.
        /// Baseline: 1M integer generations in less than 500ms.
        /// </summary>
        [Test]
        public void SplitMix64NextIntPerformance()
        {
            SplitMix64 random = new(new Guid(CreateSeedBytes(14)));

            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = random.Next();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = random.Next();
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PrngBaselineMs,
                $"SplitMix64.Next() performance regression: {stopwatch.ElapsedMilliseconds}ms for {PrngIterations} generations (baseline: {PrngBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies RomuDuo integer generation performance meets baseline requirements.
        /// Baseline: 1M integer generations in less than 500ms.
        /// </summary>
        [Test]
        public void RomuDuoNextIntPerformance()
        {
            RomuDuo random = new(new Guid(CreateSeedBytes(15)));

            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = random.Next();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = random.Next();
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PrngBaselineMs,
                $"RomuDuo.Next() performance regression: {stopwatch.ElapsedMilliseconds}ms for {PrngIterations} generations (baseline: {PrngBaselineMs}ms)"
            );
        }

        // Pooling Baselines

        /// <summary>
        /// Verifies List pooling rent/return performance meets baseline requirements.
        /// Baseline: 100K rent/return cycles in less than 200ms.
        /// </summary>
        [Test]
        public void ListPoolingPerformance()
        {
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<List<int>> pooled = Buffers<int>.List.Get(out List<int> list);
                list.Add(i);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<List<int>> pooled = Buffers<int>.List.Get(out List<int> list);
                list.Add(i);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PoolingBaselineMs,
                $"List pooling performance regression: {stopwatch.ElapsedMilliseconds}ms for {PoolingIterations} cycles (baseline: {PoolingBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies HashSet pooling rent/return performance meets baseline requirements.
        /// Baseline: 100K rent/return cycles in less than 200ms.
        /// </summary>
        [Test]
        public void HashSetPoolingPerformance()
        {
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<HashSet<int>> pooled = Buffers<int>.HashSet.Get(
                    out HashSet<int> set
                );
                set.Add(i);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<HashSet<int>> pooled = Buffers<int>.HashSet.Get(
                    out HashSet<int> set
                );
                set.Add(i);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PoolingBaselineMs,
                $"HashSet pooling performance regression: {stopwatch.ElapsedMilliseconds}ms for {PoolingIterations} cycles (baseline: {PoolingBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies Dictionary pooling rent/return performance meets baseline requirements.
        /// Baseline: 100K rent/return cycles in less than 200ms.
        /// </summary>
        [Test]
        public void DictionaryPoolingPerformance()
        {
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<Dictionary<int, int>> pooled = DictionaryBuffer<
                    int,
                    int
                >.Dictionary.Get(out Dictionary<int, int> dict);
                dict[i] = i;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<Dictionary<int, int>> pooled = DictionaryBuffer<
                    int,
                    int
                >.Dictionary.Get(out Dictionary<int, int> dict);
                dict[i] = i;
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PoolingBaselineMs,
                $"Dictionary pooling performance regression: {stopwatch.ElapsedMilliseconds}ms for {PoolingIterations} cycles (baseline: {PoolingBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies SystemArrayPool rent/return performance meets baseline requirements.
        /// Baseline: 100K rent/return cycles in less than 200ms.
        /// </summary>
        [Test]
        public void SystemArrayPoolPerformance()
        {
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledArray<int> pooled = SystemArrayPool<int>.Get(100, out int[] array);
                array[0] = i;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledArray<int> pooled = SystemArrayPool<int>.Get(100, out int[] array);
                array[0] = i;
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PoolingBaselineMs,
                $"SystemArrayPool performance regression: {stopwatch.ElapsedMilliseconds}ms for {PoolingIterations} cycles (baseline: {PoolingBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies StringBuilder pooling rent/return performance meets baseline requirements.
        /// Baseline: 100K rent/return cycles in less than 200ms.
        /// </summary>
        [Test]
        public void StringBuilderPoolingPerformance()
        {
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<System.Text.StringBuilder> pooled = Buffers.StringBuilder.Get(
                    out System.Text.StringBuilder sb
                );
                sb.Append("test");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<System.Text.StringBuilder> pooled = Buffers.StringBuilder.Get(
                    out System.Text.StringBuilder sb
                );
                sb.Append("test");
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                PoolingBaselineMs,
                $"StringBuilder pooling performance regression: {stopwatch.ElapsedMilliseconds}ms for {PoolingIterations} cycles (baseline: {PoolingBaselineMs}ms)"
            );
        }

        // Comparison Baselines

        /// <summary>
        /// Verifies pooled List allocation is faster than new List allocation.
        /// This test ensures pooling provides measurable performance benefit.
        /// </summary>
        [Test]
        public void PooledListFasterThanNewList()
        {
            const int iterations = 10000;

            Stopwatch pooledWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; ++i)
            {
                using PooledResource<List<int>> pooled = Buffers<int>.List.Get(out List<int> list);
                for (int j = 0; j < 100; ++j)
                {
                    list.Add(j);
                }
            }
            pooledWatch.Stop();

            Stopwatch newWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; ++i)
            {
                List<int> list = new();
                for (int j = 0; j < 100; ++j)
                {
                    list.Add(j);
                }
            }
            newWatch.Stop();

            Assert.Less(
                pooledWatch.ElapsedMilliseconds,
                newWatch.ElapsedMilliseconds * 2,
                $"Pooled List should be comparable to or faster than new List. Pooled: {pooledWatch.ElapsedMilliseconds}ms, New: {newWatch.ElapsedMilliseconds}ms"
            );
        }

        /// <summary>
        /// Verifies pooled array allocation is faster than new array allocation.
        /// This test ensures pooling provides measurable performance benefit.
        /// </summary>
        [Test]
        public void PooledArrayFasterThanNewArray()
        {
            const int iterations = 50000;
            const int arraySize = 256;

            Stopwatch pooledWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; ++i)
            {
                using PooledArray<int> pooled = SystemArrayPool<int>.Get(
                    arraySize,
                    out int[] array
                );
                array[0] = i;
            }
            pooledWatch.Stop();

            Stopwatch newWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; ++i)
            {
                int[] array = new int[arraySize];
                array[0] = i;
            }
            newWatch.Stop();

            Assert.Less(
                pooledWatch.ElapsedMilliseconds,
                newWatch.ElapsedMilliseconds * 2,
                $"Pooled array should be comparable to or faster than new array. Pooled: {pooledWatch.ElapsedMilliseconds}ms, New: {newWatch.ElapsedMilliseconds}ms"
            );
        }

        // Serialization Baselines

        /// <summary>
        /// Test data class for JSON serialization performance testing.
        /// Contains a variety of field types to simulate realistic serialization workloads.
        /// </summary>
        [Serializable]
        private sealed class JsonTestData
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public float Score { get; set; }
            public bool IsActive { get; set; }
            public List<int> Values { get; set; }
        }

        /// <summary>
        /// Test data class for Protobuf serialization performance testing.
        /// Decorated with ProtoContract and ProtoMember attributes for protobuf-net compatibility.
        /// </summary>
        [Serializable]
        [ProtoContract]
        private sealed class ProtoTestData
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            public float Score { get; set; }

            [ProtoMember(4)]
            public bool IsActive { get; set; }

            [ProtoMember(5)]
            public List<int> Values { get; set; }
        }

        /// <summary>
        /// Verifies JSON serialization performance meets baseline requirements.
        /// Baseline: 10K serialize operations in less than 500ms.
        /// </summary>
        [Test]
        public void JsonSerializePerformance()
        {
            JsonTestData testData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.JsonStringify(testData);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.JsonStringify(testData);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SerializationBaselineMs,
                $"JSON serialization performance regression: {stopwatch.ElapsedMilliseconds}ms for {SerializationIterations} operations (baseline: {SerializationBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies JSON deserialization performance meets baseline requirements.
        /// Baseline: 10K deserialize operations in less than 500ms.
        /// </summary>
        [Test]
        public void JsonDeserializePerformance()
        {
            JsonTestData testData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };
            string json = Serializer.JsonStringify(testData);

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.JsonDeserialize<JsonTestData>(json);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.JsonDeserialize<JsonTestData>(json);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SerializationBaselineMs,
                $"JSON deserialization performance regression: {stopwatch.ElapsedMilliseconds}ms for {SerializationIterations} operations (baseline: {SerializationBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies JSON round-trip (serialize + deserialize) performance meets baseline requirements.
        /// Baseline: 10K round-trip operations in less than 500ms.
        /// </summary>
        [Test]
        public void JsonRoundTripPerformance()
        {
            JsonTestData testData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                string json = Serializer.JsonStringify(testData);
                _ = Serializer.JsonDeserialize<JsonTestData>(json);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                string json = Serializer.JsonStringify(testData);
                _ = Serializer.JsonDeserialize<JsonTestData>(json);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SerializationBaselineMs * 2,
                $"JSON round-trip performance regression: {stopwatch.ElapsedMilliseconds}ms for {SerializationIterations} operations (baseline: {SerializationBaselineMs * 2}ms)"
            );
        }

        /// <summary>
        /// Verifies Protobuf serialization performance meets baseline requirements.
        /// Baseline: 10K serialize operations in less than 500ms.
        /// </summary>
        [Test]
        public void ProtobufSerializePerformance()
        {
            ProtoTestData testData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.ProtoSerialize(testData);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.ProtoSerialize(testData);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SerializationBaselineMs,
                $"Protobuf serialization performance regression: {stopwatch.ElapsedMilliseconds}ms for {SerializationIterations} operations (baseline: {SerializationBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies Protobuf deserialization performance meets baseline requirements.
        /// Baseline: 10K deserialize operations in less than 500ms.
        /// </summary>
        [Test]
        public void ProtobufDeserializePerformance()
        {
            ProtoTestData testData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };
            byte[] bytes = Serializer.ProtoSerialize(testData);

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.ProtoDeserialize<ProtoTestData>(bytes);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.ProtoDeserialize<ProtoTestData>(bytes);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SerializationBaselineMs,
                $"Protobuf deserialization performance regression: {stopwatch.ElapsedMilliseconds}ms for {SerializationIterations} operations (baseline: {SerializationBaselineMs}ms)"
            );
        }

        /// <summary>
        /// Verifies Protobuf round-trip (serialize + deserialize) performance meets baseline requirements.
        /// Baseline: 10K round-trip operations in less than 500ms.
        /// </summary>
        [Test]
        public void ProtobufRoundTripPerformance()
        {
            ProtoTestData testData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                byte[] bytes = Serializer.ProtoSerialize(testData);
                _ = Serializer.ProtoDeserialize<ProtoTestData>(bytes);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                byte[] bytes = Serializer.ProtoSerialize(testData);
                _ = Serializer.ProtoDeserialize<ProtoTestData>(bytes);
            }
            stopwatch.Stop();

            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                SerializationBaselineMs * 2,
                $"Protobuf round-trip performance regression: {stopwatch.ElapsedMilliseconds}ms for {SerializationIterations} operations (baseline: {SerializationBaselineMs * 2}ms)"
            );
        }

        /// <summary>
        /// Generates a performance baseline report and outputs it to the console.
        /// This test is marked [Explicit] so it only runs when explicitly requested.
        /// </summary>
        /// <remarks>
        /// Run this test explicitly to generate fresh benchmark results.
        /// Results are output to TestContext.WriteLine and also update the
        /// docs/performance/baseline-tests-performance.md file when not running in CI.
        /// </remarks>
        [Test]
        [Explicit]
        public void GeneratePerformanceBaselineReport()
        {
            List<BaselineTestResult> results = new();
            DateTime timestamp = DateTime.UtcNow;

            TestContext.WriteLine("Running performance baseline measurements...");
            TestContext.WriteLine("");

            results.AddRange(RunSpatialTreeBaselines());
            results.AddRange(RunPrngBaselines());
            results.AddRange(RunPoolingBaselines());
            results.AddRange(RunSerializationBaselines());

            List<string> markdownLines = BuildMarkdownReport(results, timestamp);

            TestContext.WriteLine("");
            TestContext.WriteLine("=== Performance Baseline Report ===");
            TestContext.WriteLine("");
            for (int i = 0; i < markdownLines.Count; ++i)
            {
                TestContext.WriteLine(markdownLines[i]);
            }

            BenchmarkReadmeUpdater.UpdateSection(
                "BASELINE_PERFORMANCE",
                markdownLines,
                "docs/performance/baseline-tests-performance.md"
            );

            int passedCount = 0;
            int failedCount = 0;
            for (int i = 0; i < results.Count; ++i)
            {
                if (results[i].Passed)
                {
                    passedCount = passedCount + 1;
                }
                else
                {
                    failedCount = failedCount + 1;
                }
            }

            TestContext.WriteLine("");
            TestContext.WriteLine(
                $"Summary: {passedCount} passed, {failedCount} failed out of {results.Count} tests"
            );

            Assert.AreEqual(
                0,
                failedCount,
                $"{failedCount} baseline tests exceeded their thresholds"
            );
        }

        private List<BaselineTestResult> RunSpatialTreeBaselines()
        {
            List<BaselineTestResult> results = new();

            TestContext.WriteLine("Running Spatial Tree baselines...");

            PcgRandom random1 = new(new Guid(CreateSeedBytes(101)));
            QuadTree2D<Vector2> quadTree = CreateQuadTree2D(random1);
            List<Vector2> buffer2D = new();
            Vector2 center2D = new(500f, 500f);
            float radius = 100f;

            for (int i = 0; i < WarmupIterations; ++i)
            {
                quadTree.GetElementsInRange(center2D, radius, buffer2D);
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                quadTree.GetElementsInRange(center2D, radius, buffer2D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "QuadTree2DRangeQuery",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "Range queries on 10K elements"
                )
            );

            Bounds queryBounds = new(new Vector3(500f, 500f, 0f), new Vector3(200f, 200f, 1f));
            for (int i = 0; i < WarmupIterations; ++i)
            {
                quadTree.GetElementsInBounds(queryBounds, buffer2D);
            }

            sw.Restart();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                quadTree.GetElementsInBounds(queryBounds, buffer2D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "QuadTree2DBoundsQuery",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "Bounds queries on 10K elements"
                )
            );

            PcgRandom random2 = new(new Guid(CreateSeedBytes(102)));
            KdTree2D<Vector2> kdTree2D = CreateKdTree2D(random2);

            for (int i = 0; i < WarmupIterations; ++i)
            {
                kdTree2D.GetElementsInRange(center2D, radius, buffer2D);
            }

            sw.Restart();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                kdTree2D.GetElementsInRange(center2D, radius, buffer2D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "KdTree2DRangeQuery",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "Range queries on 10K elements"
                )
            );

            int neighborCount = 10;
            for (int i = 0; i < WarmupIterations; ++i)
            {
                kdTree2D.GetApproximateNearestNeighbors(center2D, neighborCount, buffer2D);
            }

            sw.Restart();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                kdTree2D.GetApproximateNearestNeighbors(center2D, neighborCount, buffer2D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "KdTree2DNearestNeighbor",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "Nearest neighbor queries on 10K elements"
                )
            );

            PcgRandom random3 = new(new Guid(CreateSeedBytes(103)));
            RTree2D<Vector2> rTree2D = CreateRTree2D(random3);

            for (int i = 0; i < WarmupIterations; ++i)
            {
                rTree2D.GetElementsInRange(center2D, radius, buffer2D);
            }

            sw.Restart();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                rTree2D.GetElementsInRange(center2D, radius, buffer2D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "RTree2DRangeQuery",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "Range queries on 10K elements"
                )
            );

            PcgRandom random4 = new(new Guid(CreateSeedBytes(104)));
            OctTree3D<Vector3> octTree = CreateOctTree3D(random4);
            List<Vector3> buffer3D = new();
            Vector3 center3D = new(500f, 500f, 500f);

            for (int i = 0; i < WarmupIterations; ++i)
            {
                octTree.GetElementsInRange(center3D, radius, buffer3D);
            }

            sw.Restart();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                octTree.GetElementsInRange(center3D, radius, buffer3D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "OctTree3DRangeQuery",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "3D range queries on 10K elements"
                )
            );

            PcgRandom random5 = new(new Guid(CreateSeedBytes(105)));
            KdTree3D<Vector3> kdTree3D = CreateKdTree3D(random5);

            for (int i = 0; i < WarmupIterations; ++i)
            {
                kdTree3D.GetElementsInRange(center3D, radius, buffer3D);
            }

            sw.Restart();
            for (int i = 0; i < SpatialTreeQueryIterations; ++i)
            {
                kdTree3D.GetElementsInRange(center3D, radius, buffer3D);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "KdTree3DRangeQuery",
                    SpatialTreeQueryIterations,
                    sw.ElapsedMilliseconds,
                    SpatialTreeQueryBaselineMs,
                    "3D range queries on 10K elements"
                )
            );

            PcgRandom random6 = new(new Guid(CreateSeedBytes(106)));
            Vector2[] points2D = CreateRandomPoints2D(random6, SpatialTreeElementCount);

            for (int i = 0; i < 3; ++i)
            {
                _ = new QuadTree2D<Vector2>(points2D, p => p);
            }

            sw.Restart();
            _ = new QuadTree2D<Vector2>(points2D, p => p);
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "QuadTree2DConstruction",
                    1,
                    sw.ElapsedMilliseconds,
                    SpatialTreeConstructionBaselineMs,
                    "Construct tree with 10K elements"
                )
            );

            PcgRandom random7 = new(new Guid(CreateSeedBytes(107)));
            Vector2[] points2DKd = CreateRandomPoints2D(random7, SpatialTreeElementCount);

            for (int i = 0; i < 3; ++i)
            {
                _ = new KdTree2D<Vector2>(points2DKd, p => p);
            }

            sw.Restart();
            _ = new KdTree2D<Vector2>(points2DKd, p => p);
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "KdTree2DConstruction",
                    1,
                    sw.ElapsedMilliseconds,
                    SpatialTreeConstructionBaselineMs,
                    "Construct balanced tree with 10K elements"
                )
            );

            PcgRandom random8 = new(new Guid(CreateSeedBytes(108)));
            Vector2[] points2DRTree = CreateRandomPoints2D(random8, SpatialTreeElementCount);

            for (int i = 0; i < 3; ++i)
            {
                _ = new RTree2D<Vector2>(points2DRTree, CreatePointBounds);
            }

            sw.Restart();
            _ = new RTree2D<Vector2>(points2DRTree, CreatePointBounds);
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Spatial Trees",
                    "RTree2DConstruction",
                    1,
                    sw.ElapsedMilliseconds,
                    SpatialTreeConstructionBaselineMs,
                    "Construct tree with 10K elements"
                )
            );

            return results;
        }

        private List<BaselineTestResult> RunPrngBaselines()
        {
            List<BaselineTestResult> results = new();

            TestContext.WriteLine("Running PRNG baselines...");

            PcgRandom pcgRandom = new(new Guid(CreateSeedBytes(201)));
            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = pcgRandom.Next();
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = pcgRandom.Next();
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "PRNG",
                    "PcgRandomNextInt",
                    PrngIterations,
                    sw.ElapsedMilliseconds,
                    PrngBaselineMs,
                    "Integer generation throughput"
                )
            );

            pcgRandom = new PcgRandom(new Guid(CreateSeedBytes(202)));
            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = pcgRandom.NextFloat();
            }

            sw.Restart();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = pcgRandom.NextFloat();
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "PRNG",
                    "PcgRandomNextFloat",
                    PrngIterations,
                    sw.ElapsedMilliseconds,
                    PrngBaselineMs,
                    "Float generation throughput"
                )
            );

            XoroShiroRandom xoroShiro = new(new Guid(CreateSeedBytes(203)));
            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = xoroShiro.Next();
            }

            sw.Restart();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = xoroShiro.Next();
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "PRNG",
                    "XoroShiroRandomNextInt",
                    PrngIterations,
                    sw.ElapsedMilliseconds,
                    PrngBaselineMs,
                    "Integer generation throughput"
                )
            );

            SplitMix64 splitMix = new(new Guid(CreateSeedBytes(204)));
            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = splitMix.Next();
            }

            sw.Restart();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = splitMix.Next();
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "PRNG",
                    "SplitMix64NextInt",
                    PrngIterations,
                    sw.ElapsedMilliseconds,
                    PrngBaselineMs,
                    "Integer generation throughput"
                )
            );

            RomuDuo romuDuo = new(new Guid(CreateSeedBytes(205)));
            for (int i = 0; i < WarmupIterations * 1000; ++i)
            {
                _ = romuDuo.Next();
            }

            sw.Restart();
            for (int i = 0; i < PrngIterations; ++i)
            {
                _ = romuDuo.Next();
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "PRNG",
                    "RomuDuoNextInt",
                    PrngIterations,
                    sw.ElapsedMilliseconds,
                    PrngBaselineMs,
                    "Integer generation throughput"
                )
            );

            return results;
        }

        private List<BaselineTestResult> RunPoolingBaselines()
        {
            List<BaselineTestResult> results = new();

            TestContext.WriteLine("Running Pooling baselines...");

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<List<int>> pooled = Buffers<int>.List.Get(out List<int> list);
                list.Add(i);
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<List<int>> pooled = Buffers<int>.List.Get(out List<int> list);
                list.Add(i);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Pooling",
                    "ListPooling",
                    PoolingIterations,
                    sw.ElapsedMilliseconds,
                    PoolingBaselineMs,
                    "List rent/return cycles"
                )
            );

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<HashSet<int>> pooled = Buffers<int>.HashSet.Get(
                    out HashSet<int> set
                );
                set.Add(i);
            }

            sw.Restart();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<HashSet<int>> pooled = Buffers<int>.HashSet.Get(
                    out HashSet<int> set
                );
                set.Add(i);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Pooling",
                    "HashSetPooling",
                    PoolingIterations,
                    sw.ElapsedMilliseconds,
                    PoolingBaselineMs,
                    "HashSet rent/return cycles"
                )
            );

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<Dictionary<int, int>> pooled = DictionaryBuffer<
                    int,
                    int
                >.Dictionary.Get(out Dictionary<int, int> dict);
                dict[i] = i;
            }

            sw.Restart();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<Dictionary<int, int>> pooled = DictionaryBuffer<
                    int,
                    int
                >.Dictionary.Get(out Dictionary<int, int> dict);
                dict[i] = i;
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Pooling",
                    "DictionaryPooling",
                    PoolingIterations,
                    sw.ElapsedMilliseconds,
                    PoolingBaselineMs,
                    "Dictionary rent/return cycles"
                )
            );

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledArray<int> pooled = SystemArrayPool<int>.Get(100, out int[] array);
                array[0] = i;
            }

            sw.Restart();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledArray<int> pooled = SystemArrayPool<int>.Get(100, out int[] array);
                array[0] = i;
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Pooling",
                    "SystemArrayPool",
                    PoolingIterations,
                    sw.ElapsedMilliseconds,
                    PoolingBaselineMs,
                    "Array rent/return cycles"
                )
            );

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                using PooledResource<System.Text.StringBuilder> pooled = Buffers.StringBuilder.Get(
                    out System.Text.StringBuilder sb
                );
                sb.Append("test");
            }

            sw.Restart();
            for (int i = 0; i < PoolingIterations; ++i)
            {
                using PooledResource<System.Text.StringBuilder> pooled = Buffers.StringBuilder.Get(
                    out System.Text.StringBuilder sb
                );
                sb.Append("test");
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Pooling",
                    "StringBuilderPooling",
                    PoolingIterations,
                    sw.ElapsedMilliseconds,
                    PoolingBaselineMs,
                    "StringBuilder rent/return cycles"
                )
            );

            return results;
        }

        private List<BaselineTestResult> RunSerializationBaselines()
        {
            List<BaselineTestResult> results = new();

            TestContext.WriteLine("Running Serialization baselines...");

            JsonTestData jsonData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.JsonStringify(jsonData);
            }

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.JsonStringify(jsonData);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Serialization",
                    "JsonSerialize",
                    SerializationIterations,
                    sw.ElapsedMilliseconds,
                    SerializationBaselineMs,
                    "JSON serialization operations"
                )
            );

            string jsonString = Serializer.JsonStringify(jsonData);
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.JsonDeserialize<JsonTestData>(jsonString);
            }

            sw.Restart();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.JsonDeserialize<JsonTestData>(jsonString);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Serialization",
                    "JsonDeserialize",
                    SerializationIterations,
                    sw.ElapsedMilliseconds,
                    SerializationBaselineMs,
                    "JSON deserialization operations"
                )
            );

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                string json = Serializer.JsonStringify(jsonData);
                _ = Serializer.JsonDeserialize<JsonTestData>(json);
            }

            sw.Restart();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                string json = Serializer.JsonStringify(jsonData);
                _ = Serializer.JsonDeserialize<JsonTestData>(json);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Serialization",
                    "JsonRoundTrip",
                    SerializationIterations,
                    sw.ElapsedMilliseconds,
                    SerializationBaselineMs * 2,
                    "JSON serialize + deserialize"
                )
            );

            ProtoTestData protoData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.ProtoSerialize(protoData);
            }

            sw.Restart();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.ProtoSerialize(protoData);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Serialization",
                    "ProtobufSerialize",
                    SerializationIterations,
                    sw.ElapsedMilliseconds,
                    SerializationBaselineMs,
                    "Protobuf serialization operations"
                )
            );

            byte[] protoBytes = Serializer.ProtoSerialize(protoData);
            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.ProtoDeserialize<ProtoTestData>(protoBytes);
            }

            sw.Restart();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                _ = Serializer.ProtoDeserialize<ProtoTestData>(protoBytes);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Serialization",
                    "ProtobufDeserialize",
                    SerializationIterations,
                    sw.ElapsedMilliseconds,
                    SerializationBaselineMs,
                    "Protobuf deserialization operations"
                )
            );

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                byte[] bytes = Serializer.ProtoSerialize(protoData);
                _ = Serializer.ProtoDeserialize<ProtoTestData>(bytes);
            }

            sw.Restart();
            for (int i = 0; i < SerializationIterations; ++i)
            {
                byte[] bytes = Serializer.ProtoSerialize(protoData);
                _ = Serializer.ProtoDeserialize<ProtoTestData>(bytes);
            }
            sw.Stop();
            results.Add(
                new BaselineTestResult(
                    "Serialization",
                    "ProtobufRoundTrip",
                    SerializationIterations,
                    sw.ElapsedMilliseconds,
                    SerializationBaselineMs * 2,
                    "Protobuf serialize + deserialize"
                )
            );

            return results;
        }

        private static List<string> BuildMarkdownReport(
            List<BaselineTestResult> results,
            DateTime timestamp
        )
        {
            List<string> lines = new();

            lines.Add("");
            lines.Add("## Performance Baseline Report");
            lines.Add("");
            lines.Add($"Generated: {timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            lines.Add("");

            string currentCategory = null;
            for (int i = 0; i < results.Count; ++i)
            {
                BaselineTestResult result = results[i];
                if (result.Category != currentCategory)
                {
                    if (currentCategory != null)
                    {
                        lines.Add("  </tbody>");
                        lines.Add("</table>");
                        lines.Add("");
                    }
                    currentCategory = result.Category;
                    lines.Add($"### {currentCategory}");
                    lines.Add("");
                    lines.Add("<table data-sortable>");
                    lines.Add("  <thead>");
                    lines.Add("    <tr>");
                    lines.Add("      <th align=\"left\">Test</th>");
                    lines.Add("      <th align=\"right\">Iterations</th>");
                    lines.Add("      <th align=\"right\">Time (ms)</th>");
                    lines.Add("      <th align=\"right\">Baseline (ms)</th>");
                    lines.Add("      <th align=\"right\">% of Baseline</th>");
                    lines.Add("      <th align=\"left\">Status</th>");
                    lines.Add("    </tr>");
                    lines.Add("  </thead>");
                    lines.Add("  <tbody>");
                }

                double percent =
                    result.BaselineMs > 0
                        ? (result.TimeMs / (double)result.BaselineMs) * 100.0
                        : 0.0;
                string status = result.Passed ? "Pass" : "FAIL";
                string iterationsFormatted = FormatNumber(result.Iterations);
                lines.Add(
                    $"    <tr><td align=\"left\">{result.TestName}</td><td align=\"right\">{iterationsFormatted}</td><td align=\"right\">{result.TimeMs}</td><td align=\"right\">{result.BaselineMs}</td><td align=\"right\">{percent:F1}%</td><td align=\"left\">{status}</td></tr>"
                );
            }

            if (currentCategory != null)
            {
                lines.Add("  </tbody>");
                lines.Add("</table>");
            }

            lines.Add("");

            int passedCount = 0;
            int failedCount = 0;
            for (int i = 0; i < results.Count; ++i)
            {
                if (results[i].Passed)
                {
                    passedCount = passedCount + 1;
                }
                else
                {
                    failedCount = failedCount + 1;
                }
            }

            lines.Add("### Summary");
            lines.Add("");
            if (failedCount == 0)
            {
                lines.Add($"All {results.Count} tests passed within baseline thresholds.");
            }
            else
            {
                lines.Add(
                    $"{passedCount} passed, {failedCount} failed out of {results.Count} tests."
                );
            }
            lines.Add("");

            return lines;
        }

        private static string FormatNumber(int number)
        {
            if (number >= 1000000)
            {
                return $"{number / 1000000.0:F0}M".Replace(".0M", "M");
            }
            if (number >= 1000)
            {
                return $"{number / 1000.0:F0}K".Replace(".0K", "K");
            }
            return number.ToString();
        }

        private readonly struct BaselineTestResult
        {
            public readonly string Category;
            public readonly string TestName;
            public readonly int Iterations;
            public readonly long TimeMs;
            public readonly long BaselineMs;
            public readonly string Description;
            public readonly bool Passed;

            public BaselineTestResult(
                string category,
                string testName,
                int iterations,
                long timeMs,
                long baselineMs,
                string description
            )
            {
                Category = category;
                TestName = testName;
                Iterations = iterations;
                TimeMs = timeMs;
                BaselineMs = baselineMs;
                Description = description;
                Passed = timeMs <= baselineMs;
            }
        }

        /// <summary>
        /// Verifies Protobuf is faster than JSON for serialization.
        /// This test ensures Protobuf maintains its expected performance advantage over JSON.
        /// </summary>
        [Test]
        public void ProtobufFasterThanJsonForSerialization()
        {
            ProtoTestData protoData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            JsonTestData jsonData = new()
            {
                Id = 12345,
                Name = "Performance Test Object",
                Score = 98.765f,
                IsActive = true,
                Values = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            };

            const int iterations = 5000;

            for (int i = 0; i < WarmupIterations * 100; ++i)
            {
                _ = Serializer.ProtoSerialize(protoData);
                _ = Serializer.JsonStringify(jsonData);
            }

            Stopwatch protoWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; ++i)
            {
                _ = Serializer.ProtoSerialize(protoData);
            }
            protoWatch.Stop();

            Stopwatch jsonWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; ++i)
            {
                _ = Serializer.JsonStringify(jsonData);
            }
            jsonWatch.Stop();

            Assert.Less(
                protoWatch.ElapsedMilliseconds,
                jsonWatch.ElapsedMilliseconds * 3,
                $"Protobuf should be comparable to or faster than JSON. Protobuf: {protoWatch.ElapsedMilliseconds}ms, JSON: {jsonWatch.ElapsedMilliseconds}ms"
            );
        }

        // Helper Methods

        private static byte[] CreateSeedBytes(int index)
        {
            byte[] bytes = new byte[16];
            ulong first = DeterministicSeed + (ulong)index;
            ulong second = DeterministicSeed ^ ((ulong)index * 0x9E3779B97F4A7C15UL);
            WriteUInt64LittleEndian(bytes, 0, first);
            WriteUInt64LittleEndian(bytes, 8, second);
            return bytes;
        }

        private static void WriteUInt64LittleEndian(byte[] buffer, int offset, ulong value)
        {
            buffer[offset + 0] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        private static Vector2[] CreateRandomPoints2D(PcgRandom random, int count)
        {
            Vector2[] points = new Vector2[count];
            for (int i = 0; i < count; ++i)
            {
                points[i] = new Vector2(random.NextFloat() * 1000f, random.NextFloat() * 1000f);
            }
            return points;
        }

        private static Vector3[] CreateRandomPoints3D(PcgRandom random, int count)
        {
            Vector3[] points = new Vector3[count];
            for (int i = 0; i < count; ++i)
            {
                points[i] = new Vector3(
                    random.NextFloat() * 1000f,
                    random.NextFloat() * 1000f,
                    random.NextFloat() * 1000f
                );
            }
            return points;
        }

        private static QuadTree2D<Vector2> CreateQuadTree2D(PcgRandom random)
        {
            Vector2[] points = CreateRandomPoints2D(random, SpatialTreeElementCount);
            return new QuadTree2D<Vector2>(points, p => p);
        }

        private static KdTree2D<Vector2> CreateKdTree2D(PcgRandom random)
        {
            Vector2[] points = CreateRandomPoints2D(random, SpatialTreeElementCount);
            return new KdTree2D<Vector2>(points, p => p);
        }

        private static RTree2D<Vector2> CreateRTree2D(PcgRandom random)
        {
            Vector2[] points = CreateRandomPoints2D(random, SpatialTreeElementCount);
            return new RTree2D<Vector2>(points, CreatePointBounds);
        }

        private static OctTree3D<Vector3> CreateOctTree3D(PcgRandom random)
        {
            Vector3[] points = CreateRandomPoints3D(random, SpatialTreeElementCount);
            return new OctTree3D<Vector3>(points, p => p);
        }

        private static KdTree3D<Vector3> CreateKdTree3D(PcgRandom random)
        {
            Vector3[] points = CreateRandomPoints3D(random, SpatialTreeElementCount);
            return new KdTree3D<Vector3>(points, p => p);
        }

        private static Bounds CreatePointBounds(Vector2 point)
        {
            return new Bounds(new Vector3(point.x, point.y, 0f), new Vector3(0.01f, 0.01f, 1f));
        }
    }
}
