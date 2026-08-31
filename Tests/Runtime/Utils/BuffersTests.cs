// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BuffersTests
    {
        private IDisposable _waitInstructionScope;
        private readonly WallstopGenericPool<List<int>> _intPool = new(
            () => new List<int>(),
            onRelease: list => list.Clear()
        );

        [SetUp]
        public void SetUp()
        {
            PoolPurgeSettings.ResetToDefaults();
            _waitInstructionScope = Buffers.BeginWaitInstructionTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _waitInstructionScope?.Dispose();
            PoolPurgeSettings.ResetToDefaults();
        }

        [Test]
        public void PreWarmCount()
        {
            int retrieved = 0;
            int released = 0;
            using WallstopGenericPool<int> pool = new(
                () => 0,
                preWarmCount: 10,
                onGet: _ => ++retrieved,
                onRelease: _ => ++released
            );
            Assert.AreEqual(10, retrieved);
            Assert.AreEqual(10, released);
            Assert.AreEqual(10, pool.Count);
        }

        [TestCase(1, TestName = "PreWarmCallbacksInvokedInCorrectOrderCount1")]
        [TestCase(5, TestName = "PreWarmCallbacksInvokedInCorrectOrderCount5")]
        [TestCase(10, TestName = "PreWarmCallbacksInvokedInCorrectOrderCount10")]
        public void PreWarmCallbacksInvokedInCorrectOrder(int preWarmCount)
        {
            List<string> callOrder = new();
            using WallstopGenericPool<int> pool = new(
                () => 0,
                preWarmCount: preWarmCount,
                onGet: _ => callOrder.Add("onGet"),
                onRelease: _ => callOrder.Add("onRelease")
            );

            Assert.AreEqual(preWarmCount * 2, callOrder.Count);

            for (int i = 0; i < preWarmCount; i++)
            {
                int getIndex = i * 2;
                int releaseIndex = i * 2 + 1;
                Assert.AreEqual(
                    "onGet",
                    callOrder[getIndex],
                    $"Pre-warm item {i}: expected onGet at index {getIndex}, but found {callOrder[getIndex]}"
                );
                Assert.AreEqual(
                    "onRelease",
                    callOrder[releaseIndex],
                    $"Pre-warm item {i}: expected onRelease at index {releaseIndex}, but found {callOrder[releaseIndex]}"
                );
            }
        }

        [TestCase(1, TestName = "PreWarmCallbackSymmetryCount1")]
        [TestCase(5, TestName = "PreWarmCallbackSymmetryCount5")]
        [TestCase(10, TestName = "PreWarmCallbackSymmetryCount10")]
        public void PreWarmCallbackSymmetry(int preWarmCount)
        {
            HashSet<int> gotItems = new();
            HashSet<int> releasedItems = new();
            int itemId = 0;
            using WallstopGenericPool<int> pool = new(
                () => itemId++,
                preWarmCount: preWarmCount,
                onGet: item => gotItems.Add(item),
                onRelease: item => releasedItems.Add(item)
            );

            Assert.AreEqual(
                preWarmCount,
                gotItems.Count,
                "Each pre-warmed item should have onGet invoked exactly once"
            );
            Assert.AreEqual(
                preWarmCount,
                releasedItems.Count,
                "Each pre-warmed item should have onRelease invoked exactly once"
            );
            Assert.IsTrue(
                gotItems.SetEquals(releasedItems),
                "The same items should have both onGet and onRelease invoked"
            );
        }

        [TestCase(0, TestName = "PreWarmCallbackCountCorrectCount0")]
        [TestCase(1, TestName = "PreWarmCallbackCountCorrectCount1")]
        [TestCase(5, TestName = "PreWarmCallbackCountCorrectCount5")]
        [TestCase(10, TestName = "PreWarmCallbackCountCorrectCount10")]
        [TestCase(100, TestName = "PreWarmCallbackCountCorrectCount100")]
        public void PreWarmCallbackCountCorrect(int preWarmCount)
        {
            int getCount = 0;
            int releaseCount = 0;
            using WallstopGenericPool<int> pool = new(
                () => 0,
                preWarmCount: preWarmCount,
                onGet: _ => getCount++,
                onRelease: _ => releaseCount++
            );

            Assert.AreEqual(
                preWarmCount,
                getCount,
                $"onGet should be called exactly {preWarmCount} times during pre-warming"
            );
            Assert.AreEqual(
                preWarmCount,
                releaseCount,
                $"onRelease should be called exactly {preWarmCount} times during pre-warming"
            );
            Assert.AreEqual(
                preWarmCount,
                pool.Count,
                $"Pool should contain exactly {preWarmCount} items after pre-warming"
            );
        }

        [Test]
        public void GenericPoolListTests()
        {
            {
                using PooledResource<List<int>> firstList = _intPool.Get();
                using PooledResource<List<int>> secondList = _intPool.Get();
                Assert.AreNotEqual(firstList, secondList);
                firstList.resource.Add(1);
                Assert.AreEqual(1, firstList.resource.Count);
                Assert.AreEqual(0, secondList.resource.Count);
                secondList.resource.Add(2);
                Assert.AreEqual(1, firstList.resource.Count);
                Assert.AreEqual(1, secondList.resource.Count);
            }
            {
                // Ensure cleared
                using PooledResource<List<int>> firstList = _intPool.Get();
                Assert.AreEqual(0, firstList.resource.Count);
            }
        }

        [Test]
        public void ArrayPoolSizeTests()
        {
            for (int i = 0; i < 100; ++i)
            {
                using PooledArray<int> resource = WallstopArrayPool<int>.Get(i, out int[] buffer);
                Assert.AreEqual(i, buffer.Length);
                for (int j = 0; j < i; ++j)
                {
                    buffer[j] = PRNG.Instance.Next();
                }
            }

            for (int i = 0; i < 100; ++i)
            {
                using PooledArray<int> resource = WallstopArrayPool<int>.Get(i, out int[] buffer);
                Assert.AreEqual(i, buffer.Length);
                for (int j = 0; j < i; ++j)
                {
                    Assert.AreEqual(0, buffer[j]);
                }
            }
        }

        private static IEnumerable<TestCaseData> SteadyStateArrayPoolCases()
        {
            yield return new TestCaseData(
                (Action)(
                    () =>
                    {
                        using PooledArray<int> lease = WallstopArrayPool<int>.Get(
                            16,
                            out int[] buffer
                        );
                        buffer[0] = 1;
                    }
                )
            ).SetName(
                $"{nameof(ArrayPoolSteadyStateRentAndReturnDoesNotAllocate)}({nameof(WallstopArrayPool<int>)})"
            );

            yield return new TestCaseData(
                (Action)(
                    () =>
                    {
                        using PooledArray<int> lease = WallstopFastArrayPool<int>.Get(
                            16,
                            out int[] buffer
                        );
                        buffer[0] = 1;
                    }
                )
            ).SetName(
                $"{nameof(ArrayPoolSteadyStateRentAndReturnDoesNotAllocate)}({nameof(WallstopFastArrayPool<int>)})"
            );
        }

        // A pool that allocates per rent is not a pool. Both of these stored their idle arrays in a
        // ConcurrentStack, which holds every item in a freshly allocated node, so a rent-and-return
        // cycle cost 32 bytes forever (#367). The window is wide because the pool's own lazily
        // created structures have not settled after ten iterations.
        [TestCaseSource(nameof(SteadyStateArrayPoolCases))]
        public void ArrayPoolSteadyStateRentAndReturnDoesNotAllocate(Action rentAndReturn)
        {
            GCAssert.DoesNotAllocate(
                rentAndReturn,
                warmupIterations: 10_000,
                measuredIterations: 100_000
            );
        }

        [Test]
        public void WallstopFastArrayPoolGetNegativeSizeThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WallstopFastArrayPool<int>.Get(-1, out _)
            );
        }

        [Test]
        public void WallstopFastArrayPoolGetZeroSizeReturnsEmptyArrayWithNoOpDispose()
        {
            using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(0, out int[] buffer);
            Assert.NotNull(buffer);
            Assert.AreEqual(0, buffer.Length);
            Assert.AreSame(Array.Empty<int>(), buffer);
        }

        [Test]
        public void WallstopFastArrayPoolGetPositiveSizeReturnsArrayWithCorrectLength()
        {
            const int size = 10;
            using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] buffer);

            Assert.NotNull(buffer);
            Assert.AreEqual(size, buffer.Length);
        }

        [Test]
        public void WallstopFastArrayPoolGetSameSizeReusesArrayAfterDispose()
        {
            const int size = 5;
            int[] firstArray;

            using (PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] buffer))
            {
                firstArray = buffer;
                firstArray[0] = 42;
            }

            using PooledArray<int> pooledReused = WallstopFastArrayPool<int>.Get(
                size,
                out int[] reused
            );
            Assert.AreSame(firstArray, reused);
            Assert.AreEqual(42, reused[0]);
        }

        [Test]
        public void WallstopFastArrayPoolZeroLengthAlwaysSharedInstance()
        {
            using PooledArray<int> first = WallstopFastArrayPool<int>.Get(0, out int[] zeroA);
            using PooledArray<int> second = WallstopFastArrayPool<int>.Get(0, out int[] zeroB);

            Assert.AreSame(Array.Empty<int>(), zeroA);
            Assert.AreSame(zeroA, zeroB);

            for (int i = 0; i < 32; i++)
            {
                using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    0,
                    out int[] zeroBuffer
                );
                Assert.AreSame(
                    zeroA,
                    zeroBuffer,
                    $"Zero-length iteration {i} should reuse the shared empty array."
                );
            }
        }

        [Test]
        public void WallstopFastArrayPoolDoesNotCrossContaminateSizes()
        {
            const int smallSize = 16;
            const int largeSize = 64;
            const int iterations = 20;
            HashSet<int> smallHashes = new();
            HashSet<int> largeHashes = new();

            for (int i = 0; i < iterations; i++)
            {
                using PooledArray<int> small = WallstopFastArrayPool<int>.Get(
                    smallSize,
                    out int[] smallArray
                );
                using PooledArray<int> large = WallstopFastArrayPool<int>.Get(
                    largeSize,
                    out int[] largeArray
                );
                Assert.AreNotSame(smallArray, largeArray);

                smallHashes.Add(RuntimeHelpers.GetHashCode(smallArray));
                largeHashes.Add(RuntimeHelpers.GetHashCode(largeArray));
            }

            for (int i = 0; i < iterations; i++)
            {
                using PooledArray<int> small = WallstopFastArrayPool<int>.Get(
                    smallSize,
                    out int[] smallArray
                );
                int hash = RuntimeHelpers.GetHashCode(smallArray);
                Assert.IsTrue(
                    smallHashes.Contains(hash),
                    $"Small-size fetch {i} returned unexpected buffer hash {hash}."
                );
                Assert.IsFalse(
                    largeHashes.Contains(hash),
                    $"Small-size fetch {i} reused buffer previously issued for large size."
                );
            }

            for (int i = 0; i < iterations; i++)
            {
                using PooledArray<int> large = WallstopFastArrayPool<int>.Get(
                    largeSize,
                    out int[] largeArray
                );
                int hash = RuntimeHelpers.GetHashCode(largeArray);
                Assert.IsTrue(
                    largeHashes.Contains(hash),
                    $"Large-size fetch {i} returned unexpected buffer hash {hash}."
                );
                Assert.IsFalse(
                    smallHashes.Contains(hash),
                    $"Large-size fetch {i} reused buffer previously issued for small size."
                );
            }
        }

#if SINGLE_THREADED
        [Test]
        public void WallstopFastArrayPoolReleaseHandlesSparseBuckets()
        {
            const int maxSize = 128;

            using (
                PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    maxSize,
                    out int[] maxArray
                )
            )
            {
                Assert.AreEqual(maxSize, maxArray.Length);
            }

            Assert.DoesNotThrow(() =>
            {
                for (int size = 1; size <= maxSize; size += 5)
                {
                    using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                        size,
                        out int[] array
                    );
                    Assert.AreEqual(size, array.Length);
                }
            });

            Assert.DoesNotThrow(() =>
            {
                for (int size = maxSize - 1; 1 <= size; size -= 7)
                {
                    using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                        size,
                        out int[] array
                    );
                    Assert.AreEqual(size, array.Length);
                }
            });
        }
#endif

        [Test]
        public void WallstopFastArrayPoolGetDifferentSizesReturnsCorrectArrays()
        {
            int[] sizes = { 1, 5, 10, 100, 1000 };
            List<PooledArray<int>> pooledArrays = new();

            try
            {
                foreach (int size in sizes)
                {
                    PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] array);
                    pooledArrays.Add(pooled);
                    Assert.AreEqual(size, array.Length);
                }
            }
            finally
            {
                foreach (PooledArray<int> pooled in pooledArrays)
                {
                    pooled.Dispose();
                }
            }
        }

        [Test]
        public void WallstopFastArrayPoolArraysNotClearedOnRelease()
        {
            const int size = 10;

            using (PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] array))
            {
                for (int i = 0; i < size; i++)
                {
                    array[i] = i + 1;
                }
            }

            using PooledArray<int> pooledReused = WallstopFastArrayPool<int>.Get(
                size,
                out int[] reused
            );
            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual(i + 1, reused[i]);
            }
        }

        [Test]
        public void WallstopFastArrayPoolPoolGrowsAsNeeded()
        {
            const int size = 7;
            const int count = 5;
            List<PooledArray<int>> pooledArrays = new();

            try
            {
                for (int i = 0; i < count; i++)
                {
                    PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] array);
                    pooledArrays.Add(pooled);
                    Assert.AreEqual(size, array.Length);
                }

                HashSet<int[]> distinctArrays = pooledArrays.Select(p => p.array).ToHashSet();
                Assert.AreEqual(count, distinctArrays.Count);
            }
            finally
            {
                foreach (PooledArray<int> pooled in pooledArrays)
                {
                    pooled.Dispose();
                }
            }
        }

        [Test]
        public void WallstopFastArrayPoolDifferentTypesUseDifferentPools()
        {
            const int size = 10;

            using PooledArray<int> intPooled = WallstopFastArrayPool<int>.Get(
                size,
                out int[] intBuffer
            );
            using PooledArray<long> longPooled = WallstopFastArrayPool<long>.Get(
                size,
                out long[] longBuffer
            );
            using PooledArray<float> floatPooled = WallstopFastArrayPool<float>.Get(
                size,
                out float[] floatBuffer
            );

            Assert.AreEqual(size, intBuffer.Length);
            Assert.AreEqual(size, longBuffer.Length);
            Assert.AreEqual(size, floatBuffer.Length);
        }

        [Test]
        public void WallstopFastArrayPoolLargeArraysWork()
        {
            const int size = 100000;
            using PooledArray<byte> pooled = WallstopFastArrayPool<byte>.Get(
                size,
                out byte[] array
            );

            Assert.AreEqual(size, array.Length);
        }

        [Test]
        public void WallstopFastArrayPoolNestedUsageWorks()
        {
            const int outerSize = 5;
            const int innerSize = 3;

            using PooledArray<int> outer = WallstopFastArrayPool<int>.Get(
                outerSize,
                out int[] outerArray
            );
            Assert.AreEqual(outerSize, outerArray.Length);
            Array.Clear(outerArray, 0, outerArray.Length);
            outerArray[0] = 1;

            using (
                PooledArray<int> inner = WallstopFastArrayPool<int>.Get(
                    innerSize,
                    out int[] innerArray
                )
            )
            {
                innerArray[0] = 2;
                Assert.AreEqual(innerSize, innerArray.Length);
                Assert.AreEqual(1, outerArray[0]);
                Assert.AreEqual(2, innerArray[0]);
            }

            Assert.AreEqual(outerSize, outerArray.Length);
            Assert.AreEqual(1, outerArray[0]);
        }

        [UnityTest]
        public IEnumerator WallstopFastArrayPoolStressTest()
        {
            const int iterations = 1000;
            const int maxSize = 100;
            IRandom random = new PcgRandom(42);

            for (int i = 0; i < iterations; i++)
            {
                int size = random.Next(1, maxSize);
                using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    size,
                    out int[] array
                );

                Assert.AreEqual(size, array.Length);

                for (int j = 0; j < Math.Min(10, size); j++)
                {
                    array[j] = random.Next();
                }

                if (i % 100 == 0)
                {
                    yield return null;
                }
            }
        }

        [Test]
        public void WallstopFastArrayPoolLifoOrderingSingleThread()
        {
            // Clear the pool first to ensure test isolation
            WallstopFastArrayPool<int>.ClearForTesting();

            const int arraySize = 15;
            const int arrayCount = 5;
            int[][] allocatedArrays = new int[arrayCount][];

            // Allocate 5 arrays of the same size
            List<PooledArray<int>> pooledArrays = new();
            for (int i = 0; i < arrayCount; i++)
            {
                PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    arraySize,
                    out int[] array
                );
                pooledArrays.Add(pooled);
                allocatedArrays[i] = array;
                array[0] = i; // Mark each array with its allocation order
            }

            // Verify all arrays are distinct
            HashSet<int[]> distinctArrays = allocatedArrays.ToHashSet();
            Assert.AreEqual(
                arrayCount,
                distinctArrays.Count,
                "All allocated arrays should be distinct instances."
            );

            // Return them in order (first allocated returned first)
            foreach (PooledArray<int> pooled in pooledArrays)
            {
                pooled.Dispose();
            }

            // Re-acquire them and verify LIFO order (last returned is first acquired)
            // Since we returned in order [0, 1, 2, 3, 4], the stack should be [4, 3, 2, 1, 0] (top to bottom)
            // So acquiring should give us: 4, 3, 2, 1, 0
            // NOTE: We intentionally do NOT use 'using' here - disposing would return arrays to the pool
            // during verification, corrupting the LIFO order we're trying to test.
            List<PooledArray<int>> verificationArrays = new();
            for (int i = arrayCount - 1; 0 <= i; i--)
            {
                PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    arraySize,
                    out int[] array
                );
                verificationArrays.Add(pooled);

                Assert.AreSame(
                    allocatedArrays[i],
                    array,
                    $"Expected array {i} (last returned should be first acquired - LIFO order). "
                        + $"Expected marker: {i}, Actual marker: {array[0]}, "
                        + $"Expected hash: {RuntimeHelpers.GetHashCode(allocatedArrays[i])}, "
                        + $"Actual hash: {RuntimeHelpers.GetHashCode(array)}"
                );
                Assert.AreEqual(i, array[0], $"Array marker should be {i} to confirm identity.");
            }

            // Dispose all arrays after verification is complete
            foreach (PooledArray<int> pooled in verificationArrays)
            {
                pooled.Dispose();
            }
        }

        [TestCase(1, 3, TestName = "WallstopFastArrayPoolLifoOrderingSize1Count3")]
        [TestCase(5, 5, TestName = "WallstopFastArrayPoolLifoOrderingSize5Count5")]
        [TestCase(10, 10, TestName = "WallstopFastArrayPoolLifoOrderingSize10Count10")]
        [TestCase(100, 3, TestName = "WallstopFastArrayPoolLifoOrderingSize100Count3")]
        [TestCase(256, 8, TestName = "WallstopFastArrayPoolLifoOrderingSize256Count8")]
        public void WallstopFastArrayPoolLifoOrderingParameterized(int arraySize, int arrayCount)
        {
            // Clear the pool first to ensure test isolation
            WallstopFastArrayPool<int>.ClearForTesting();

            int[][] allocatedArrays = new int[arrayCount][];
            List<PooledArray<int>> pooledArrays = new();

            // Allocate arrays of the specified size
            for (int i = 0; i < arrayCount; i++)
            {
                PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    arraySize,
                    out int[] array
                );
                pooledArrays.Add(pooled);
                allocatedArrays[i] = array;
                array[0] = i; // Mark each array with its allocation order
            }

            // Verify all arrays are distinct
            HashSet<int[]> distinctArrays = allocatedArrays.ToHashSet();
            Assert.AreEqual(
                arrayCount,
                distinctArrays.Count,
                $"All {arrayCount} allocated arrays of size {arraySize} should be distinct instances."
            );

            // Return them in order (first allocated returned first)
            foreach (PooledArray<int> pooled in pooledArrays)
            {
                pooled.Dispose();
            }

            // Re-acquire them and verify LIFO order
            // NOTE: We intentionally do NOT use 'using' here - disposing would return arrays to the pool
            // during verification, corrupting the LIFO order we're trying to test.
            List<PooledArray<int>> verificationArrays = new();
            for (int i = arrayCount - 1; 0 <= i; i--)
            {
                PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    arraySize,
                    out int[] array
                );
                verificationArrays.Add(pooled);

                Assert.AreSame(
                    allocatedArrays[i],
                    array,
                    $"LIFO violation at index {i} for size {arraySize}. "
                        + $"Expected marker: {i}, Actual marker: {array[0]}"
                );
            }

            // Dispose all arrays after verification is complete
            foreach (PooledArray<int> pooled in verificationArrays)
            {
                pooled.Dispose();
            }
        }

        [Test]
        public void WallstopFastArrayPoolClearForTestingClearsAllBuckets()
        {
            // Pre-populate the pool with various sizes
            int[] testSizes = { 5, 10, 20, 50, 100 };
            Dictionary<int, int[]> originalArrays = new();

            foreach (int size in testSizes)
            {
                PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] array);
                array[0] = size; // Mark with size for identification
                originalArrays[size] = array;
                pooled.Dispose();
            }

            // Clear the pool
            WallstopFastArrayPool<int>.ClearForTesting();

            // Re-acquire - should get NEW arrays, not the original ones
            foreach (int size in testSizes)
            {
                using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                    size,
                    out int[] array
                );

                Assert.AreNotSame(
                    originalArrays.ValueFor(size),
                    array,
                    $"After ClearForTesting, size {size} should return a new array, not the original. "
                        + $"Original hash: {RuntimeHelpers.GetHashCode(originalArrays.ValueFor(size))}, "
                        + $"New hash: {RuntimeHelpers.GetHashCode(array)}"
                );

                // New array should be zeroed (default for int)
                Assert.AreEqual(
                    0,
                    array[0],
                    $"New array of size {size} should be zeroed, not contain old marker {array[0]}"
                );
            }
        }

        [Test]
        public void WallstopFastArrayPoolClearForTestingOnEmptyPoolDoesNotThrow()
        {
            // Use a type that hasn't been used before to ensure empty pool
            Assert.DoesNotThrow(
                () => WallstopFastArrayPool<double>.ClearForTesting(),
                "ClearForTesting should not throw on an empty pool"
            );

            // Clear again to verify idempotency
            Assert.DoesNotThrow(
                () => WallstopFastArrayPool<double>.ClearForTesting(),
                "ClearForTesting should be idempotent"
            );
        }

        [Test]
        public void WallstopFastArrayPoolIsolatesSizes()
        {
            const int smallSize = 10;
            const int mediumSize = 20;
            const int largeSize = 30;

            // Allocate arrays of different sizes
            using PooledArray<int> smallPooled = WallstopFastArrayPool<int>.Get(
                smallSize,
                out int[] smallArray
            );
            using PooledArray<int> mediumPooled = WallstopFastArrayPool<int>.Get(
                mediumSize,
                out int[] mediumArray
            );
            using PooledArray<int> largePooled = WallstopFastArrayPool<int>.Get(
                largeSize,
                out int[] largeArray
            );

            // Mark each array with a distinctive value
            smallArray[0] = 111;
            mediumArray[0] = 222;
            largeArray[0] = 333;

            // Store references for later comparison
            int[] originalSmall = smallArray;
            int[] originalMedium = mediumArray;
            int[] originalLarge = largeArray;

            // Return all arrays to pool
            smallPooled.Dispose();
            mediumPooled.Dispose();
            largePooled.Dispose();

            // Re-acquire by size and verify each size gets back its own arrays
            using PooledArray<int> reacquiredSmall = WallstopFastArrayPool<int>.Get(
                smallSize,
                out int[] newSmallArray
            );
            using PooledArray<int> reacquiredMedium = WallstopFastArrayPool<int>.Get(
                mediumSize,
                out int[] newMediumArray
            );
            using PooledArray<int> reacquiredLarge = WallstopFastArrayPool<int>.Get(
                largeSize,
                out int[] newLargeArray
            );

            // Verify each size bucket returned the correct array (no cross-contamination)
            Assert.AreSame(
                originalSmall,
                newSmallArray,
                "Small size bucket should return the small array."
            );
            Assert.AreSame(
                originalMedium,
                newMediumArray,
                "Medium size bucket should return the medium array."
            );
            Assert.AreSame(
                originalLarge,
                newLargeArray,
                "Large size bucket should return the large array."
            );

            // Verify markers to confirm no data corruption
            Assert.AreEqual(111, newSmallArray[0], "Small array marker should be preserved.");
            Assert.AreEqual(222, newMediumArray[0], "Medium array marker should be preserved.");
            Assert.AreEqual(333, newLargeArray[0], "Large array marker should be preserved.");

            // Verify lengths are correct
            Assert.AreEqual(
                smallSize,
                newSmallArray.Length,
                "Small array should have correct length."
            );
            Assert.AreEqual(
                mediumSize,
                newMediumArray.Length,
                "Medium array should have correct length."
            );
            Assert.AreEqual(
                largeSize,
                newLargeArray.Length,
                "Large array should have correct length."
            );
        }

#if !SINGLE_THREADED

        [TestCaseSource(nameof(WallstopFastArrayPoolConcurrentScenarioCases))]
        public void WallstopFastArrayPoolConcurrentAccessScenarios(ConcurrentScenario scenario)
        {
            RunConcurrentScenario(scenario);
        }

        [TestCaseSource(nameof(WallstopArrayPoolConcurrentScenarioCases))]
        public void WallstopArrayPoolConcurrentAccessScenarios(ConcurrentScenario scenario)
        {
            RunConcurrentScenario(scenario);
        }

        private static IEnumerable<TestCaseData> WallstopFastArrayPoolConcurrentScenarioCases()
        {
            yield return CreateDifferentSizesScenario();
            yield return CreateSameSizeScenario();
            yield return CreateMixedSizesScenario();
            yield return CreateRapidAllocationScenario();
            yield return CreateOutOfOrderDisposeScenario();
        }

        private static IEnumerable<TestCaseData> WallstopArrayPoolConcurrentScenarioCases()
        {
            yield return CreateArrayPoolClearScenario();
            yield return CreateArrayPoolMixedSizesScenario();
        }

        private static void RunConcurrentScenario(ConcurrentScenario scenario)
        {
            ConcurrentQueue<ScenarioException> exceptions = new();
            Task[] tasks = new Task[scenario.ThreadCount];

            for (int t = 0; t < scenario.ThreadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(async () =>
                {
                    try
                    {
                        Task work = scenario.Work(threadId);
                        if (work == null)
                        {
                            throw new InvalidOperationException(
                                $"Scenario '{scenario.Name}' returned a null task for thread {threadId}."
                            );
                        }

                        await work.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(new ScenarioException(threadId, ex));
                    }
                });
            }

            Task.WaitAll(tasks);

            ScenarioException[] failures = exceptions.ToArray();
            if (failures.Length == 0)
            {
                return;
            }

            StringBuilder builder = new();
            foreach (ScenarioException failure in failures)
            {
                builder.AppendLine(
                    $"Thread {failure.ThreadId}: {failure.Exception.GetType().Name} - {failure.Exception.Message}"
                );

                if (failure.Exception.Data != null && 0 < failure.Exception.Data.Count)
                {
                    foreach (DictionaryEntry entry in failure.Exception.Data)
                    {
                        builder.AppendLine($"    {entry.Key}: {entry.Value}");
                    }
                }

                if (!string.IsNullOrEmpty(failure.Exception.StackTrace))
                {
                    builder.AppendLine(failure.Exception.StackTrace);
                }

                builder.AppendLine();
            }

            Assert.Fail(
                $"Scenario '{scenario.Name}' captured {failures.Length} exception(s).{Environment.NewLine}{builder}"
            );
        }

        private static TestCaseData CreateDifferentSizesScenario()
        {
            const int threadCount = 10;
            const int operationsPerThread = 100;

            ConcurrentScenario scenario = new(
                nameof(ConcurrentScenarioName.WallstopFastArrayPoolConcurrentAccessDifferentSizes),
                threadCount,
                async threadId =>
                {
                    PcgRandom random = new(threadId);
                    foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                    {
                        int size = random.Next(1, 50) + threadId;
                        using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                            size,
                            out int[] array
                        );

                        Assert.AreEqual(
                            size,
                            array.Length,
                            $"DifferentSizes thread {threadId} iteration {i} expected length {size}"
                        );

                        for (int j = 0; j < Math.Min(5, size); j++)
                        {
                            array[j] = threadId * 1000 + i * 10 + j;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(random.NextDouble()))
                            .ConfigureAwait(false);
                    }
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        private static TestCaseData CreateSameSizeScenario()
        {
            const int threadCount = 8;
            const int operationsPerThread = 200;
            const int arraySize = 25;

            ConcurrentScenario scenario = new(
                nameof(ConcurrentScenarioName.WallstopFastArrayPoolConcurrentAccessSameSize),
                threadCount,
                threadId =>
                {
                    PcgRandom random = new(threadId);
                    foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                    {
                        using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                            arraySize,
                            out int[] array
                        );

                        Array.Clear(array, 0, array.Length);
                        Assert.AreEqual(
                            arraySize,
                            array.Length,
                            $"SameSize thread {threadId} iteration {i} expected length {arraySize}"
                        );

                        for (int j = 0; j < arraySize; j++)
                        {
                            Assert.AreEqual(
                                0,
                                array[j],
                                $"SameSize thread {threadId} iteration {i} index {j} expected zero"
                            );
                            array[j] = threadId * 1000 + i;
                        }
                    }

                    return Task.CompletedTask;
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        private static TestCaseData CreateMixedSizesScenario()
        {
            const int threadCount = 6;
            const int operationsPerThread = 150;
            int[] sizes = { 1, 5, 10, 20, 50, 100 };

            ConcurrentScenario scenario = new(
                nameof(ConcurrentScenarioName.WallstopFastArrayPoolConcurrentAccessMixedSizes),
                threadCount,
                async threadId =>
                {
                    PcgRandom random = new(threadId + 100);
                    foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                    {
                        int size = random.NextOf(sizes);
                        using PooledArray<long> pooled = WallstopFastArrayPool<long>.Get(
                            size,
                            out long[] array
                        );
                        Array.Clear(array, 0, array.Length);

                        Assert.AreEqual(
                            size,
                            array.Length,
                            $"MixedSizes thread {threadId} iteration {i} expected length {size}"
                        );

                        for (int j = 0; j < size; j++)
                        {
                            Assert.AreEqual(
                                0,
                                array[j],
                                $"MixedSizes thread {threadId} iteration {i} index {j} expected zero"
                            );
                            long packed = ((long)threadId << 32) | ((long)i << 16) | (uint)j;
                            array[j] = packed;
                        }

                        if (i % 50 == 0)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(random.NextDouble()))
                                .ConfigureAwait(false);
                        }
                    }
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        private static TestCaseData CreateRapidAllocationScenario()
        {
            const int threadCount = 12;
            const int operationsPerThread = 500;

            ConcurrentScenario scenario = new(
                nameof(
                    ConcurrentScenarioName.WallstopFastArrayPoolConcurrentAccessRapidAllocationDeallocation
                ),
                threadCount,
                threadId =>
                {
                    PcgRandom random = new(threadId + 100);
                    foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                    {
                        int size = random.Next(1, 30);
                        using PooledArray<byte> pooled = WallstopFastArrayPool<byte>.Get(
                            size,
                            out byte[] array
                        );

                        Assert.AreEqual(
                            size,
                            array.Length,
                            $"RapidAllocation thread {threadId} iteration {i} expected length {size}"
                        );

                        for (int j = 0; j < size; j++)
                        {
                            array[j] = (byte)(threadId + i + j);
                        }
                    }

                    return Task.CompletedTask;
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        private static TestCaseData CreateOutOfOrderDisposeScenario()
        {
            const int threadCount = 4;
            const int allocationsPerThread = 64;

            // NOTE: ClearForTesting is called inside the lambda because this static method runs during
            // test case data creation (test discovery), not during test execution. Each thread uses
            // unique sizes (12+threadId*24), ensuring thread isolation without needing a global clear.
            // The per-thread unique sizes mean threads don't compete for the same pool buckets.

            ConcurrentScenario scenario = new(
                nameof(ConcurrentScenarioName.WallstopFastArrayPoolConcurrentOutOfOrderDispose),
                threadCount,
                threadId =>
                {
                    int baseSize = 12 + threadId * 24;
                    int[] threadSizes = { baseSize, baseSize + 3, baseSize + 6 };
                    List<PooledArray<int>> rentals = new();

                    for (int i = 0; i < allocationsPerThread; i++)
                    {
                        int size = threadSizes[i % threadSizes.Length];
                        PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                            size,
                            out int[] array
                        );
                        rentals.Add(pooled);
                        array[0] = threadId;
                        array[size - 1] = threadId;
                    }

                    // Use Stack (LIFO) to match pool behavior - arrays are disposed in reverse order
                    // and retrieved in the same LIFO order from the pool's internal stack
                    Dictionary<int, Stack<int[]>> expectedOrder = new();

                    for (int i = rentals.Count - 1; 0 <= i; i--)
                    {
                        PooledArray<int> pooled = rentals[i];
                        int size = pooled.length;
                        expectedOrder.GetOrAdd(size).Push(pooled.array);
                        pooled.Dispose();
                    }

                    // NOTE: We intentionally do NOT use 'using' here - disposing would return arrays to the pool
                    // during verification, corrupting the LIFO order we're trying to test.
                    List<PooledArray<int>> verificationArrays = new();
                    foreach (KeyValuePair<int, Stack<int[]>> pair in expectedOrder)
                    {
                        while (0 < pair.Value.Count)
                        {
                            int[] expected = pair.Value.Pop();
                            PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                                pair.Key,
                                out int[] array
                            );
                            verificationArrays.Add(pooled);

                            Assert.AreSame(
                                expected,
                                array,
                                $"OutOfOrderDispose thread {threadId} expected LIFO for size {pair.Key}. "
                                    + $"Expected hash: {RuntimeHelpers.GetHashCode(expected)}, "
                                    + $"Actual hash: {RuntimeHelpers.GetHashCode(array)}, "
                                    + $"Expected marker[0]: {expected[0]}, Actual marker[0]: {array[0]}"
                            );
                        }
                    }

                    // Dispose all arrays after verification is complete
                    foreach (PooledArray<int> pooled in verificationArrays)
                    {
                        pooled.Dispose();
                    }

                    return Task.CompletedTask;
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        private static TestCaseData CreateArrayPoolClearScenario()
        {
            const int threadCount = 6;
            const int operationsPerThread = 120;
            const int arraySize = 32;

            ConcurrentScenario scenario = new(
                nameof(ConcurrentScenarioName.WallstopArrayPoolClearOnReuse),
                threadCount,
                threadId =>
                {
                    PcgRandom random = new(threadId + 400);
                    foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                    {
                        using PooledArray<int> pooled = WallstopArrayPool<int>.Get(
                            arraySize,
                            out int[] arrayBuffer
                        );
                        Assert.AreEqual(
                            arraySize,
                            arrayBuffer.Length,
                            $"ArrayPoolClear thread {threadId} iteration {i} expected length {arraySize}"
                        );

                        for (int j = 0; j < arraySize; j++)
                        {
                            Assert.AreEqual(
                                0,
                                arrayBuffer[j],
                                $"ArrayPoolClear thread {threadId} iteration {i} index {j} expected zero"
                            );
                            arrayBuffer[j] = threadId + i + j;
                        }
                    }

                    return Task.CompletedTask;
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        private static TestCaseData CreateArrayPoolMixedSizesScenario()
        {
            const int threadCount = 5;
            const int operationsPerThread = 80;
            int[] sizes = { 2, 7, 13, 21, 34 };

            ConcurrentScenario scenario = new(
                nameof(ConcurrentScenarioName.WallstopArrayPoolConcurrentMixedSizesClearCheck),
                threadCount,
                async threadId =>
                {
                    PcgRandom random = new(threadId + 700);
                    foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                    {
                        int size = random.NextOf(sizes);
                        using PooledArray<byte> pooled = WallstopArrayPool<byte>.Get(
                            size,
                            out byte[] byteBuffer
                        );

                        Assert.AreEqual(
                            size,
                            byteBuffer.Length,
                            $"ArrayPoolMixedSizes thread {threadId} iteration {i} expected length {size}"
                        );

                        for (int j = 0; j < size; j++)
                        {
                            Assert.AreEqual(
                                0,
                                byteBuffer[j],
                                $"ArrayPoolMixedSizes thread {threadId} iteration {i} index {j} expected zero"
                            );
                            byteBuffer[j] = (byte)(threadId + i + j);
                        }

                        if (i % 25 == 0)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(random.NextDouble() * 2.0))
                                .ConfigureAwait(false);
                        }
                    }
                }
            );

            return new TestCaseData(scenario).SetName(scenario.Name);
        }

        [Test]
        public void WallstopFastArrayPoolThreadSafetyPoolExpansion()
        {
            const int threadCount = 8;
            const int maxPoolSize = 1000;
            Task[] tasks = new Task[threadCount];
            List<Exception> exceptions = new();
            Barrier barrier = new(threadCount);

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();

                        for (int size = threadId + 1; size <= maxPoolSize; size += threadCount)
                        {
                            using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                                size,
                                out int[] array
                            );
                            Assert.AreEqual(size, array.Length);

                            array[0] = threadId;
                            array[size - 1] = threadId;
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
            }

            Task.WaitAll(tasks);

            if (0 < exceptions.Count)
            {
                throw new AggregateException(exceptions);
            }
        }

        [UnityTest]
        public IEnumerator WallstopFastArrayPoolConcurrentStressTest()
        {
            const int threadCount = 4;
            const int operationsPerThread = 250;
            Task[] tasks = new Task[threadCount];
            List<Exception> exceptions = new();
            int completedThreads = 0;

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        PcgRandom random = new(threadId + 100);
                        foreach (int i in Enumerable.Range(0, operationsPerThread).Shuffled(random))
                        {
                            int size = random.Next(1, 100);
                            using PooledArray<float> pooled = WallstopFastArrayPool<float>.Get(
                                size,
                                out float[] array
                            );

                            Assert.AreEqual(size, array.Length);

                            for (int j = 0; j < Math.Min(10, size); j++)
                            {
                                array[j] = threadId * 1000.0f + i + j * 0.1f;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                    finally
                    {
                        Interlocked.Increment(ref completedThreads);
                    }
                });
            }

            while (completedThreads < threadCount)
            {
                yield return null;
            }

            Task.WaitAll(tasks);

            if (0 < exceptions.Count)
            {
                throw new AggregateException(exceptions);
            }
        }
#endif

        [Test]
        public void WallstopFastArrayPoolEdgeCaseVeryLargeSize()
        {
            const int veryLargeSize = 1000000;
            using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(
                veryLargeSize,
                out int[] array
            );

            Assert.AreEqual(veryLargeSize, array.Length);
            Assert.AreEqual(0, array[0]);
            Assert.AreEqual(0, array[veryLargeSize - 1]);
        }

        [Test]
        public void WallstopFastArrayPoolEdgeCaseSizeOne()
        {
            using PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(1, out int[] array);
            Array.Clear(array, 0, array.Length);
            Assert.AreEqual(1, array.Length);
            Assert.AreEqual(0, array[0]);

            array[0] = 42;
            Assert.AreEqual(42, array[0]);
        }

        [Test]
        public void WallstopFastArrayPoolPoolingBehaviorLifo()
        {
            const int size = 15;
            int[][] arrays = new int[3][];
            {
                using PooledArray<int> pooled1 = WallstopFastArrayPool<int>.Get(
                    size,
                    out int[] arr1
                );
                arrays[0] = arr1;
                using PooledArray<int> pooled2 = WallstopFastArrayPool<int>.Get(
                    size,
                    out int[] arr2
                );
                arrays[1] = arr2;
                using PooledArray<int> pooled3 = WallstopFastArrayPool<int>.Get(
                    size,
                    out int[] arr3
                );
                arrays[2] = arr3;
            }

            // NOTE: We intentionally do NOT use 'using' here - disposing would return arrays to the pool
            // during verification, corrupting the LIFO order we're trying to test.
            // Nested using statements dispose in reverse order (arr3, arr2, arr1), so after all dispose:
            // - arr3 pushed first, arr2 pushed second, arr1 pushed last
            // - Stack = [arr1 (top), arr2, arr3 (bottom)]
            // - First Get returns arr1, second returns arr2, third returns arr3
            PooledArray<int> pooledReuse1 = WallstopFastArrayPool<int>.Get(size, out int[] reuse1);
            Assert.AreSame(
                arrays[0],
                reuse1,
                $"Expected LIFO: arr1 disposed last should be retrieved first. Got hash {RuntimeHelpers.GetHashCode(reuse1)}, expected hash {RuntimeHelpers.GetHashCode(arrays[0])}"
            );

            PooledArray<int> pooledReuse2 = WallstopFastArrayPool<int>.Get(size, out int[] reuse2);
            Assert.AreSame(
                arrays[1],
                reuse2,
                $"Expected LIFO: arr2 disposed second should be retrieved second. Got hash {RuntimeHelpers.GetHashCode(reuse2)}, expected hash {RuntimeHelpers.GetHashCode(arrays[1])}"
            );

            PooledArray<int> pooledReuse3 = WallstopFastArrayPool<int>.Get(size, out int[] reuse3);
            Assert.AreSame(
                arrays[2],
                reuse3,
                $"Expected LIFO: arr3 disposed first should be retrieved last. Got hash {RuntimeHelpers.GetHashCode(reuse3)}, expected hash {RuntimeHelpers.GetHashCode(arrays[2])}"
            );

            // Dispose all arrays after verification
            pooledReuse1.Dispose();
            pooledReuse2.Dispose();
            pooledReuse3.Dispose();
        }

        [Test]
        public void WallstopArrayPoolGetNegativeSizeThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WallstopArrayPool<int>.Get(-1, out _));
        }

        [Test]
        public void WallstopArrayPoolGetZeroSizeReturnsEmptyArrayWithNoOpDispose()
        {
            using PooledArray<int> pooled = WallstopArrayPool<int>.Get(0, out int[] buffer);
            Assert.NotNull(buffer);
            Assert.AreEqual(0, buffer.Length);
            Assert.AreSame(Array.Empty<int>(), buffer);
        }

        [Test]
        public void WallstopArrayPoolGetPositiveSizeReturnsArrayWithCorrectLength()
        {
            const int size = 10;
            using PooledArray<int> pooled = WallstopArrayPool<int>.Get(size, out int[] buffer);

            Assert.NotNull(buffer);
            Assert.AreEqual(size, buffer.Length);
        }

        [Test]
        public void WallstopArrayPoolGetSameSizeReusesArrayAfterDispose()
        {
            const int size = 5;
            int[] firstArray;

            using (PooledArray<int> pooled = WallstopArrayPool<int>.Get(size, out int[] buffer))
            {
                firstArray = buffer;
                firstArray[0] = 42;
            }

            using PooledArray<int> pooledReused = WallstopArrayPool<int>.Get(
                size,
                out int[] reused
            );
            Assert.AreSame(firstArray, reused);
            Assert.AreEqual(0, reused[0]);
        }

        [Test]
        public void WallstopGenericPoolGetReturnsValidPooledResource()
        {
            using PooledResource<List<int>> pooled = _intPool.Get();

            Assert.NotNull(pooled.resource);
            Assert.AreEqual(0, pooled.resource.Count);
        }

        [Test]
        public void WallstopGenericPoolGetReusesInstanceAfterDispose()
        {
            List<int> firstList;

            using (PooledResource<List<int>> pooled = _intPool.Get())
            {
                firstList = pooled.resource;
                firstList.Add(42);
                firstList.Add(100);
            }

            using PooledResource<List<int>> pooledReused = _intPool.Get();
            Assert.AreSame(firstList, pooledReused.resource);
            Assert.AreEqual(0, pooledReused.resource.Count);
        }

        [Test]
        public void WallstopGenericPoolClearActionWorksWithCustomType()
        {
            using WallstopGenericPool<HashSet<string>> pool = new(() => new HashSet<string>());
            using PooledResource<HashSet<string>> pooled = pool.Get();

            pooled.resource.Add("test1");
            pooled.resource.Add("test2");
            Assert.AreEqual(2, pooled.resource.Count);
        }

        [Test]
        public void PooledResourceDisposeCallsOnDisposeAction()
        {
            bool clearCalled = false;
            bool disposeCalled = false;
            {
                using WallstopGenericPool<List<int>> pool = new(
                    () => new List<int>(),
                    onRelease: list =>
                    {
                        list.Clear();
                        clearCalled = true;
                    },
                    onDisposal: _ => disposeCalled = true
                );

                using (PooledResource<List<int>> pooled = pool.Get())
                {
                    Assert.NotNull(pooled.resource);
                    Assert.IsFalse(clearCalled);
                    Assert.IsFalse(disposeCalled);
                }

                Assert.IsTrue(clearCalled);
            }

            Assert.IsTrue(disposeCalled);
        }

        [Test]
        public void BuffersGetWaitForSecondsReturnsCachedInstance()
        {
            const float seconds = 1.5f;
            UnityEngine.WaitForSeconds first = Buffers.GetWaitForSeconds(seconds);
            UnityEngine.WaitForSeconds second = Buffers.GetWaitForSeconds(seconds);

            Assert.AreSame(first, second);
        }

        [Test]
        public void BuffersGetWaitForSecondsRealTimeReturnsCachedInstance()
        {
            const float seconds = 2.5f;
            UnityEngine.WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(seconds);
            UnityEngine.WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(seconds);

            Assert.AreSame(first, second);
        }

        [Test]
        public void BuffersWaitForFixedUpdateIsSingleton()
        {
            Assert.NotNull(Buffers.WaitForFixedUpdate);
            Assert.AreSame(Buffers.WaitForFixedUpdate, Buffers.WaitForFixedUpdate);
        }

        [Test]
        public void BuffersWaitForEndOfFrameIsSingleton()
        {
            Assert.NotNull(Buffers.WaitForEndOfFrame);
            Assert.AreSame(Buffers.WaitForEndOfFrame, Buffers.WaitForEndOfFrame);
        }

        [Test]
        public void BuffersStringBuilderPoolWorks()
        {
            using PooledResource<System.Text.StringBuilder> pooled = Buffers.StringBuilder.Get();

            Assert.NotNull(pooled.resource);
            pooled.resource.Append("test");
            Assert.AreEqual("test", pooled.resource.ToString());
        }

        [Test]
        public void BuffersStringBuilderPoolClearsOnRelease()
        {
            using (PooledResource<System.Text.StringBuilder> pooled = Buffers.StringBuilder.Get())
            {
                pooled.resource.Append("test content");
            }

            using PooledResource<System.Text.StringBuilder> pooledReused =
                Buffers.StringBuilder.Get();
            Assert.AreEqual(0, pooledReused.resource.Length);
        }

        [Test]
        public void BuffersGenericListPoolWorks()
        {
            using PooledResource<List<int>> pooled = Buffers<int>.List.Get(out List<int> list);

            Assert.NotNull(list);
            list.Add(42);
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void BuffersGenericHashSetPoolWorks()
        {
            using PooledResource<HashSet<string>> pooled = Buffers<string>.HashSet.Get(
                out HashSet<string> set
            );

            Assert.NotNull(set);
            set.Add("test");
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void BuffersGenericQueuePoolWorks()
        {
            using PooledResource<Queue<int>> pooled = Buffers<int>.Queue.Get(out Queue<int> queue);

            Assert.NotNull(queue);
            queue.Enqueue(1);
            queue.Enqueue(2);
            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual(1, queue.Dequeue());
        }

        [Test]
        public void BuffersGenericStackPoolWorks()
        {
            using PooledResource<Stack<int>> pooled = Buffers<int>.Stack.Get(out Stack<int> stack);

            Assert.NotNull(stack);
            stack.Push(1);
            stack.Push(2);
            Assert.AreEqual(2, stack.Count);
            Assert.AreEqual(2, stack.Pop());
        }

        [Test]
        public void SetBuffersSortedSetPoolWorks()
        {
            using PooledResource<SortedSet<int>> pooled = SetBuffers<int>.SortedSet.Get(
                out SortedSet<int> sortedSet
            );

            Assert.NotNull(sortedSet);
            sortedSet.Add(3);
            sortedSet.Add(1);
            sortedSet.Add(2);
            Assert.AreEqual(3, sortedSet.Count);
            Assert.AreEqual(1, sortedSet.Min);
        }

        [Test]
        public void SetBuffersGetSortedSetPoolWithCustomComparer()
        {
            IComparer<int> reverseComparer = Comparer<int>.Create((a, b) => b.CompareTo(a));
            WallstopGenericPool<SortedSet<int>> pool = SetBuffers<int>.GetSortedSetPool(
                reverseComparer
            );

            using PooledResource<SortedSet<int>> pooled = pool.Get();
            pooled.resource.Add(1);
            pooled.resource.Add(2);
            pooled.resource.Add(3);

            Assert.AreEqual(3, pooled.resource.Min);
        }

        [Test]
        public void SetBuffersGetHashSetPoolWithCustomComparer()
        {
            IEqualityComparer<string> caseInsensitiveComparer = StringComparer.OrdinalIgnoreCase;
            WallstopGenericPool<HashSet<string>> pool = SetBuffers<string>.GetHashSetPool(
                caseInsensitiveComparer
            );

            using PooledResource<HashSet<string>> pooled = pool.Get();
            pooled.resource.Add("Test");
            Assert.IsFalse(pooled.resource.Add("test"));
            Assert.AreEqual(1, pooled.resource.Count);
        }

        [Test]
        public void SetBuffersGetSortedSetPoolThrowsOnNullComparer()
        {
            Assert.Throws<ArgumentNullException>(() => SetBuffers<int>.GetSortedSetPool(null));
        }

        [Test]
        public void SetBuffersGetHashSetPoolThrowsOnNullComparer()
        {
            Assert.Throws<ArgumentNullException>(() => SetBuffers<int>.GetHashSetPool(null));
        }

        [Test]
        public void SetBuffersHasHashSetPoolWorks()
        {
            IEqualityComparer<int> comparer = EqualityComparer<int>.Default;
            Assert.IsFalse(SetBuffers<int>.HasHashSetPool(comparer));

            SetBuffers<int>.GetHashSetPool(comparer);
            Assert.IsTrue(SetBuffers<int>.HasHashSetPool(comparer));
        }

        [Test]
        public void SetBuffersHasSortedSetPoolWorks()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            Assert.IsFalse(SetBuffers<int>.HasSortedSetPool(comparer));

            SetBuffers<int>.GetSortedSetPool(comparer);
            Assert.IsTrue(SetBuffers<int>.HasSortedSetPool(comparer));
        }

        [Test]
        public void SetBuffersDestroyHashSetPoolWorks()
        {
            IEqualityComparer<int> comparer = EqualityComparer<int>.Default;
            SetBuffers<int>.GetHashSetPool(comparer);
            Assert.IsTrue(SetBuffers<int>.HasHashSetPool(comparer));

            Assert.IsTrue(SetBuffers<int>.DestroyHashSetPool(comparer));
            Assert.IsFalse(SetBuffers<int>.HasHashSetPool(comparer));
        }

        [Test]
        public void SetBuffersDestroySortedSetPoolWorks()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            SetBuffers<int>.GetSortedSetPool(comparer);
            Assert.IsTrue(SetBuffers<int>.HasSortedSetPool(comparer));

            Assert.IsTrue(SetBuffers<int>.DestroySortedSetPool(comparer));
            Assert.IsFalse(SetBuffers<int>.HasSortedSetPool(comparer));
        }

        [Test]
        public void LinkedListBufferWorks()
        {
            using PooledResource<LinkedList<int>> pooled = LinkedListBuffer<int>.LinkedList.Get();

            Assert.NotNull(pooled.resource);
            pooled.resource.AddLast(1);
            pooled.resource.AddLast(2);
            Assert.AreEqual(2, pooled.resource.Count);
        }

        [Test]
        public void DictionaryBufferDictionaryPoolWorks()
        {
            using PooledResource<Dictionary<string, int>> pooled = DictionaryBuffer<
                string,
                int
            >.Dictionary.Get();

            Assert.NotNull(pooled.resource);
            pooled.resource["key"] = 42;
            Assert.AreEqual(1, pooled.resource.Count);
            Assert.AreEqual(42, pooled.resource.ValueFor("key"));
        }

        [Test]
        public void DictionaryBufferSortedDictionaryPoolWorks()
        {
            using PooledResource<SortedDictionary<int, string>> pooled = DictionaryBuffer<
                int,
                string
            >.SortedDictionary.Get();

            Assert.NotNull(pooled.resource);
            pooled.resource[3] = "three";
            pooled.resource[1] = "one";
            pooled.resource[2] = "two";
            Assert.AreEqual(3, pooled.resource.Count);
            Assert.AreEqual("one", pooled.resource.First().Value);
        }

        [Test]
        public void DictionaryBufferGetDictionaryPoolWithCustomComparer()
        {
            IEqualityComparer<string> caseInsensitiveComparer = StringComparer.OrdinalIgnoreCase;
            WallstopGenericPool<Dictionary<string, int>> pool = DictionaryBuffer<
                string,
                int
            >.GetDictionaryPool(caseInsensitiveComparer);

            using PooledResource<Dictionary<string, int>> pooled = pool.Get();
            pooled.resource["Test"] = 1;
            pooled.resource["test"] = 2;
            Assert.AreEqual(1, pooled.resource.Count);
            Assert.AreEqual(2, pooled.resource.ValueFor("TEST"));
        }

        [Test]
        public void DictionaryBufferGetSortedDictionaryPoolWithCustomComparer()
        {
            IComparer<int> reverseComparer = Comparer<int>.Create((a, b) => b.CompareTo(a));
            WallstopGenericPool<SortedDictionary<int, string>> pool = DictionaryBuffer<
                int,
                string
            >.GetSortedDictionaryPool(reverseComparer);

            using PooledResource<SortedDictionary<int, string>> pooled = pool.Get();
            pooled.resource[1] = "one";
            pooled.resource[2] = "two";
            pooled.resource[3] = "three";

            Assert.AreEqual("three", pooled.resource.First().Value);
        }

        [Test]
        public void DictionaryBufferHasDictionaryPoolWorks()
        {
            IEqualityComparer<string> comparer = EqualityComparer<string>.Default;
            Assert.IsFalse(DictionaryBuffer<string, int>.HasDictionaryPool(comparer));

            DictionaryBuffer<string, int>.GetDictionaryPool(comparer);
            Assert.IsTrue(DictionaryBuffer<string, int>.HasDictionaryPool(comparer));
        }

        [Test]
        public void DictionaryBufferHasSortedDictionaryPoolWorks()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            Assert.IsFalse(DictionaryBuffer<int, string>.HasSortedDictionaryPool(comparer));

            DictionaryBuffer<int, string>.GetSortedDictionaryPool(comparer);
            Assert.IsTrue(DictionaryBuffer<int, string>.HasSortedDictionaryPool(comparer));
        }

        [Test]
        public void DictionaryBufferDestroyDictionaryPoolWorks()
        {
            IEqualityComparer<string> comparer = EqualityComparer<string>.Default;
            DictionaryBuffer<string, int>.GetDictionaryPool(comparer);
            Assert.IsTrue(DictionaryBuffer<string, int>.HasDictionaryPool(comparer));

            Assert.IsTrue(DictionaryBuffer<string, int>.DestroyDictionaryPool(comparer));
            Assert.IsFalse(DictionaryBuffer<string, int>.HasDictionaryPool(comparer));
        }

        [Test]
        public void DictionaryBufferDestroySortedDictionaryPoolWorks()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            DictionaryBuffer<int, string>.GetSortedDictionaryPool(comparer);
            Assert.IsTrue(DictionaryBuffer<int, string>.HasSortedDictionaryPool(comparer));

            Assert.IsTrue(DictionaryBuffer<int, string>.DestroySortedDictionaryPool(comparer));
            Assert.IsFalse(DictionaryBuffer<int, string>.HasSortedDictionaryPool(comparer));
        }

        [Test]
        public void WallstopGenericPoolProducerNullThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new WallstopGenericPool<int>(null));
        }

        [Test]
        public void WallstopGenericPoolOnGetIsCalledWhenRetrieving()
        {
            bool getCalled = false;
            using WallstopGenericPool<int> pool = new(() => 42, onGet: _ => getCalled = true);

            using PooledResource<int> pooled = pool.Get();
            Assert.IsTrue(getCalled);
        }

        [Test]
        public void WallstopGenericPoolMultipleGetReturnsDifferentInstancesWhenEmpty()
        {
            using WallstopGenericPool<List<int>> pool = new(() => new List<int>());

            List<int> first;
            List<int> second;

            using (PooledResource<List<int>> pooled = pool.Get(out first)) { }

            using (PooledResource<List<int>> pooled = pool.Get(out second)) { }

            Assert.AreSame(first, second);
        }

        [Test]
        public void PooledResourceDefaultIsNotInitialized()
        {
            PooledResource<int> defaultResource = default;
            defaultResource.Dispose();
        }

        [Test]
        public void WallstopArrayPoolArraysAreClearedOnReturn()
        {
            const int size = 10;
            using (PooledArray<int> pooled = WallstopArrayPool<int>.Get(size, out int[] array))
            {
                for (int i = 0; i < size; i++)
                {
                    array[i] = i + 1;
                }
            }

            using PooledArray<int> pooledReused = WallstopArrayPool<int>.Get(
                size,
                out int[] reused
            );
            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual(0, reused[i]);
            }
        }

        [Test]
        public void WallstopArrayPoolDifferentSizesReturnDifferentArrays()
        {
            using PooledArray<int> pooled5 = WallstopArrayPool<int>.Get(5, out int[] a5);
            using PooledArray<int> pooled10 = WallstopArrayPool<int>.Get(10, out int[] a10);

            Assert.AreNotSame(a5, a10);
            Assert.AreEqual(5, a5.Length);
            Assert.AreEqual(10, a10.Length);
        }

        [Test]
        public void WallstopFastArrayPoolMultipleGetsReturnDistinctArrays()
        {
            const int size = 7;
            List<int[]> arrays = new();

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] array);
                    arrays.Add(array);
                }

                Assert.AreEqual(3, arrays.Distinct().Count());
            }
            finally
            {
                foreach (int[] array in arrays)
                {
                    WallstopFastArrayPool<int>.Get(0, out _).Dispose();
                }
            }
        }

        [Test]
        public void SystemArrayPoolGetNegativeSizeThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SystemArrayPool<int>.Get(-1, out _));
        }

        [Test]
        public void SystemArrayPoolGetZeroSizeReturnsEmptyArrayAndZeroLength()
        {
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(0, out int[] buffer);
            Assert.IsTrue(buffer != null);
            Assert.AreEqual(0, buffer.Length);
            Assert.AreEqual(0, pooled.length);
            Assert.AreSame(Array.Empty<int>(), buffer);
        }

        [Test]
        public void SystemArrayPoolGetPositiveSizeReturnsArrayAtLeastRequestedSize()
        {
            const int requestedSize = 10;
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(
                requestedSize,
                out int[] buffer
            );

            Assert.IsTrue(buffer != null);
            Assert.GreaterOrEqual(buffer.Length, requestedSize);
            Assert.AreEqual(requestedSize, pooled.length);
        }

        [Test]
        public void SystemArrayPoolLengthPropertyReturnsOriginalRequestedSize()
        {
            int[] testSizes = { 1, 7, 15, 33, 100, 257, 1000 };
            foreach (int size in testSizes)
            {
                using PooledArray<int> pooled = SystemArrayPool<int>.Get(size, out int[] buffer);
                Assert.AreEqual(size, pooled.length);
                Assert.GreaterOrEqual(buffer.Length, size);
            }
        }

        [Test]
        public void SystemArrayPoolBufferMayBeLargerThanRequested()
        {
            const int requestedSize = 33;
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(
                requestedSize,
                out int[] buffer
            );

            Assert.AreEqual(requestedSize, pooled.length);
            Assert.GreaterOrEqual(buffer.Length, requestedSize);
        }

        [Test]
        public void SystemArrayPoolGetWithClearArrayClearsRequestedPortionOnly()
        {
            const int requestedSize = 50;
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(
                requestedSize,
                clearArray: true,
                out int[] buffer
            );

            for (int i = 0; i < requestedSize; i++)
            {
                Assert.AreEqual(0, buffer[i]);
            }
        }

        [Test]
        public void SystemArrayPoolDisposeReturnsArrayToPool()
        {
            const int requestedSize = 100;
            int[] firstArray;
            {
                PooledArray<int> pooled = SystemArrayPool<int>.Get(requestedSize, out int[] buffer);
                firstArray = buffer;
                pooled.Dispose();
            }

            using PooledArray<int> pooled2 = SystemArrayPool<int>.Get(
                requestedSize,
                out int[] reused
            );
            Assert.AreSame(firstArray, reused);
        }

        [Test]
        public void SystemArrayPoolDoubleDisposeIsNoOp()
        {
            const int requestedSize = 10;
            PooledArray<int> pooled = SystemArrayPool<int>.Get(requestedSize, out int[] buffer);

            pooled.Dispose();
            Assert.DoesNotThrow(() => pooled.Dispose());
        }

        [Test]
        public void SystemArrayPoolArrayPropertyReturnsBackingArray()
        {
            const int requestedSize = 25;
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(
                requestedSize,
                out int[] buffer
            );

            Assert.AreSame(buffer, pooled.array);
        }

        [Test]
        public void SystemArrayPoolGetWithoutOutParameterWorks()
        {
            const int requestedSize = 15;
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(requestedSize);

            Assert.IsTrue(pooled.array != null);
            Assert.GreaterOrEqual(pooled.array.Length, requestedSize);
            Assert.AreEqual(requestedSize, pooled.length);
        }

        [Test]
        public void SystemArrayPoolCanBeUsedWithVariousTypes()
        {
            using PooledArray<string> stringPooled = SystemArrayPool<string>.Get(
                10,
                out string[] stringBuffer
            );
            using PooledArray<double> doublePooled = SystemArrayPool<double>.Get(
                20,
                out double[] doubleBuffer
            );
            using PooledArray<object> objectPooled = SystemArrayPool<object>.Get(
                5,
                out object[] objectBuffer
            );

            Assert.GreaterOrEqual(stringBuffer.Length, 10);
            Assert.AreEqual(10, stringPooled.length);

            Assert.GreaterOrEqual(doubleBuffer.Length, 20);
            Assert.AreEqual(20, doublePooled.length);

            Assert.GreaterOrEqual(objectBuffer.Length, 5);
            Assert.AreEqual(5, objectPooled.length);
        }

        [Test]
        public void SystemArrayPoolWriteAndReadWithinRequestedLength()
        {
            const int requestedSize = 50;
            using PooledArray<int> pooled = SystemArrayPool<int>.Get(
                requestedSize,
                out int[] buffer
            );

            for (int i = 0; i < pooled.length; i++)
            {
                buffer[i] = i * 2;
            }

            for (int i = 0; i < pooled.length; i++)
            {
                Assert.AreEqual(i * 2, buffer[i]);
            }
        }

        [Test]
        public void SystemArrayPoolDefaultStructIsDisposable()
        {
            PooledArray<int> defaultPooled = default;
            Assert.DoesNotThrow(() => defaultPooled.Dispose());
            Assert.AreEqual(0, defaultPooled.length);
            Assert.IsTrue(defaultPooled.array == null);
        }

        [Test]
        public void SystemArrayPoolVariousSizesWorkCorrectly()
        {
            int[] sizes =
            {
                1,
                2,
                3,
                4,
                7,
                8,
                15,
                16,
                31,
                32,
                63,
                64,
                127,
                128,
                255,
                256,
                511,
                512,
                1023,
                1024,
            };

            foreach (int size in sizes)
            {
                using PooledArray<byte> pooled = SystemArrayPool<byte>.Get(size, out byte[] buffer);
                Assert.AreEqual(size, pooled.length);
                Assert.GreaterOrEqual(buffer.Length, size);

                for (int i = 0; i < pooled.length; i++)
                {
                    buffer[i] = (byte)(i % 256);
                }
            }
        }

        [Test]
        public void WallstopFastArrayPoolDoubleDisposeIsNoOp()
        {
            WallstopFastArrayPool<int>.ClearForTesting();

            const int size = 10;
            PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(size, out int[] firstArray);
            firstArray[0] = 42;

            // First dispose returns array to pool
            pooled.Dispose();

            // Second dispose should be a no-op (already disposed)
            Assert.DoesNotThrow(() => pooled.Dispose(), "Double dispose should not throw");

            // Get a new array - should get the same one back (LIFO)
            using PooledArray<int> pooled2 = WallstopFastArrayPool<int>.Get(
                size,
                out int[] secondArray
            );
            Assert.AreSame(
                firstArray,
                secondArray,
                "After double dispose, pool should still return the same array exactly once"
            );

            // Get another array - should be a fresh allocation since pool only had one array
            using PooledArray<int> pooled3 = WallstopFastArrayPool<int>.Get(
                size,
                out int[] thirdArray
            );
            Assert.AreNotSame(
                firstArray,
                thirdArray,
                "Third allocation should be a new array since pool was emptied"
            );
        }

        [Test]
        public void WallstopFastArrayPoolLifoWithInterleavedSizes()
        {
            WallstopFastArrayPool<int>.ClearForTesting();

            const int sizeA = 10;
            const int sizeB = 20;

            // Allocate interleaved sizes: A, B, A, B
            PooledArray<int> pooledA1 = WallstopFastArrayPool<int>.Get(sizeA, out int[] arrayA1);
            PooledArray<int> pooledB1 = WallstopFastArrayPool<int>.Get(sizeB, out int[] arrayB1);
            PooledArray<int> pooledA2 = WallstopFastArrayPool<int>.Get(sizeA, out int[] arrayA2);
            PooledArray<int> pooledB2 = WallstopFastArrayPool<int>.Get(sizeB, out int[] arrayB2);

            arrayA1[0] = 1;
            arrayA2[0] = 2;
            arrayB1[0] = 10;
            arrayB2[0] = 20;

            // Dispose in order: A1, B1, A2, B2
            pooledA1.Dispose();
            pooledB1.Dispose();
            pooledA2.Dispose();
            pooledB2.Dispose();

            // Size A stack should be: [A2 (top), A1]
            // Size B stack should be: [B2 (top), B1]

            // Get size A - should get A2 (LIFO)
            PooledArray<int> getA1 = WallstopFastArrayPool<int>.Get(sizeA, out int[] gotA1);
            Assert.AreSame(
                arrayA2,
                gotA1,
                $"Expected LIFO for size {sizeA}: got marker {gotA1[0]}, expected marker 2"
            );

            // Get size B - should get B2 (LIFO)
            PooledArray<int> getB1 = WallstopFastArrayPool<int>.Get(sizeB, out int[] gotB1);
            Assert.AreSame(
                arrayB2,
                gotB1,
                $"Expected LIFO for size {sizeB}: got marker {gotB1[0]}, expected marker 20"
            );

            // Get size A again - should get A1 (LIFO)
            PooledArray<int> getA2 = WallstopFastArrayPool<int>.Get(sizeA, out int[] gotA2);
            Assert.AreSame(
                arrayA1,
                gotA2,
                $"Expected LIFO for size {sizeA}: got marker {gotA2[0]}, expected marker 1"
            );

            // Get size B again - should get B1 (LIFO)
            PooledArray<int> getB2 = WallstopFastArrayPool<int>.Get(sizeB, out int[] gotB2);
            Assert.AreSame(
                arrayB1,
                gotB2,
                $"Expected LIFO for size {sizeB}: got marker {gotB2[0]}, expected marker 10"
            );

            // Cleanup
            getA1.Dispose();
            getA2.Dispose();
            getB1.Dispose();
            getB2.Dispose();
        }

        [Test]
        public void WallstopFastArrayPoolClearForTestingDuringActiveRentals()
        {
            WallstopFastArrayPool<int>.ClearForTesting();

            const int size = 15;

            // Get an array but don't dispose it yet
            PooledArray<int> activeRental = WallstopFastArrayPool<int>.Get(
                size,
                out int[] activeArray
            );
            activeArray[0] = 999;

            // Return some arrays to the pool
            PooledArray<int> returned1 = WallstopFastArrayPool<int>.Get(size, out int[] arr1);
            PooledArray<int> returned2 = WallstopFastArrayPool<int>.Get(size, out int[] arr2);
            returned1.Dispose();
            returned2.Dispose();

            // Clear the pool while activeRental is still held
            WallstopFastArrayPool<int>.ClearForTesting();

            // The active rental should still be valid
            Assert.AreEqual(
                999,
                activeArray[0],
                "Active rental should still be usable after clear"
            );

            // Getting new arrays should allocate fresh ones (pool was cleared)
            PooledArray<int> newAlloc = WallstopFastArrayPool<int>.Get(size, out int[] newArray);
            Assert.AreNotSame(
                arr1,
                newArray,
                "After ClearForTesting, should get fresh array, not previously pooled one"
            );
            Assert.AreNotSame(arr2, newArray);

            // Cleanup
            activeRental.Dispose();
            newAlloc.Dispose();
        }

        [TestCase(1, TestName = "WallstopFastArrayPoolLifoSingleElementSize1")]
        [TestCase(2, TestName = "WallstopFastArrayPoolLifoSingleElementSize2")]
        [TestCase(100, TestName = "WallstopFastArrayPoolLifoSingleElementSize100")]
        public void WallstopFastArrayPoolLifoSingleElement(int arraySize)
        {
            WallstopFastArrayPool<int>.ClearForTesting();

            // Allocate and return a single array
            PooledArray<int> pooled = WallstopFastArrayPool<int>.Get(arraySize, out int[] original);
            original[0] = 12345;
            pooled.Dispose();

            // Should get the same array back
            PooledArray<int> reacquired = WallstopFastArrayPool<int>.Get(
                arraySize,
                out int[] retrieved
            );
            Assert.AreSame(original, retrieved, $"Single element LIFO failed for size {arraySize}");

            reacquired.Dispose();
        }

        [Test]
        public void WallstopFastArrayPoolIsolationBetweenTypes()
        {
            WallstopFastArrayPool<int>.ClearForTesting();
            WallstopFastArrayPool<byte>.ClearForTesting();

            const int size = 10;

            // Allocate and return int array
            PooledArray<int> intPooled = WallstopFastArrayPool<int>.Get(size, out int[] intArray);
            intArray[0] = 42;
            intPooled.Dispose();

            // Allocate and return byte array of same size
            PooledArray<byte> bytePooled = WallstopFastArrayPool<byte>.Get(
                size,
                out byte[] byteArray
            );
            byteArray[0] = 255;
            bytePooled.Dispose();

            // Getting int array should get the int array back, not affected by byte pool
            PooledArray<int> intReacquired = WallstopFastArrayPool<int>.Get(
                size,
                out int[] retrievedInt
            );
            Assert.AreSame(intArray, retrievedInt, "Int pool should be isolated from byte pool");
            Assert.AreEqual(
                42,
                retrievedInt[0],
                "Int array should retain its marker (WallstopFastArrayPool doesn't clear)"
            );

            // Getting byte array should get the byte array back
            PooledArray<byte> byteReacquired = WallstopFastArrayPool<byte>.Get(
                size,
                out byte[] retrievedByte
            );
            Assert.AreSame(byteArray, retrievedByte, "Byte pool should be isolated from int pool");
            Assert.AreEqual(
                255,
                retrievedByte[0],
                "Byte array should retain its marker (WallstopFastArrayPool doesn't clear)"
            );

            intReacquired.Dispose();
            byteReacquired.Dispose();
        }

        [Test]
        public void SystemArrayPoolReferenceElementsAreNotRootedAfterDispose()
        {
            const int requestedSize = 100;
            PoolRootProbe[] rented;
            {
                using PooledArray<PoolRootProbe> pooled = SystemArrayPool<PoolRootProbe>.Get(
                    requestedSize,
                    out PoolRootProbe[] buffer
                );
                rented = buffer;
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = new PoolRootProbe();
                }
            }

            using PooledArray<PoolRootProbe> reacquired = SystemArrayPool<PoolRootProbe>.Get(
                requestedSize,
                out PoolRootProbe[] reused
            );
            Assert.AreSame(rented, reused);
            for (int i = 0; i < reused.Length; i++)
            {
                Assert.IsTrue(
                    reused[i] == null,
                    $"Slot {i} still roots a reference the pool handed back to the shared pool."
                );
            }
        }

        [Test]
        public void SystemArrayPoolStructElementsHoldingReferencesAreNotRootedAfterDispose()
        {
            const int requestedSize = 40;
            PoolRootProbeHolder[] rented;
            {
                using PooledArray<PoolRootProbeHolder> pooled =
                    SystemArrayPool<PoolRootProbeHolder>.Get(
                        requestedSize,
                        out PoolRootProbeHolder[] buffer
                    );
                rented = buffer;
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = new PoolRootProbeHolder { probe = new PoolRootProbe(), tag = i };
                }
            }

            using PooledArray<PoolRootProbeHolder> reacquired =
                SystemArrayPool<PoolRootProbeHolder>.Get(
                    requestedSize,
                    out PoolRootProbeHolder[] reused
                );
            Assert.AreSame(rented, reused);
            for (int i = 0; i < reused.Length; i++)
            {
                Assert.IsTrue(
                    reused[i].probe == null,
                    $"Slot {i} still roots a reference held inside a struct element."
                );
            }
        }

        // Pins the cost side of the same decision: a blittable element roots nothing, so clearing it
        // on return would be pure waste on the sort and geometry paths that rent the most.
        [Test]
        public void SystemArrayPoolBlittableElementsAreNotClearedOnReturn()
        {
            const int requestedSize = 64;
            PoolBlittableProbe[] rented;
            {
                using PooledArray<PoolBlittableProbe> pooled =
                    SystemArrayPool<PoolBlittableProbe>.Get(
                        requestedSize,
                        out PoolBlittableProbe[] buffer
                    );
                rented = buffer;
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = new PoolBlittableProbe { value = i + 1 };
                }
            }

            using PooledArray<PoolBlittableProbe> reacquired =
                SystemArrayPool<PoolBlittableProbe>.Get(
                    requestedSize,
                    out PoolBlittableProbe[] reused
                );
            Assert.AreSame(rented, reused);
            bool anySurvived = false;
            for (int i = 0; i < reused.Length; i++)
            {
                if (reused[i].value != 0)
                {
                    anySurvived = true;
                    break;
                }
            }

            Assert.IsTrue(
                anySurvived,
                "Blittable elements were cleared on return; that clear costs the whole rented array and prevents nothing."
            );
        }

        [Test]
        public void SystemArrayPoolGetWithClearArrayZeroesAnArrayReturnedDirtyByOtherCode()
        {
            const int requestedSize = 32;
            PoolBlittableProbe[] dirtied = System.Buffers.ArrayPool<PoolBlittableProbe>.Shared.Rent(
                requestedSize
            );
            for (int i = 0; i < dirtied.Length; i++)
            {
                dirtied[i] = new PoolBlittableProbe { value = -1 };
            }

            System.Buffers.ArrayPool<PoolBlittableProbe>.Shared.Return(dirtied, clearArray: false);

            using PooledArray<PoolBlittableProbe> pooled = SystemArrayPool<PoolBlittableProbe>.Get(
                requestedSize,
                clearArray: true,
                out PoolBlittableProbe[] buffer
            );
            Assert.AreSame(dirtied, buffer);
            for (int i = 0; i < requestedSize; i++)
            {
                Assert.AreEqual(0, buffer[i].value);
            }
        }

        private sealed class PoolRootProbe { }

        private struct PoolRootProbeHolder
        {
            public PoolRootProbe probe;
            public int tag;
        }

        private struct PoolBlittableProbe
        {
            public int value;
        }

#if !SINGLE_THREADED
        private enum ConcurrentScenarioName
        {
            WallstopFastArrayPoolConcurrentAccessDifferentSizes,
            WallstopFastArrayPoolConcurrentAccessSameSize,
            WallstopFastArrayPoolConcurrentAccessMixedSizes,
            WallstopFastArrayPoolConcurrentAccessRapidAllocationDeallocation,
            WallstopFastArrayPoolConcurrentOutOfOrderDispose,
            WallstopArrayPoolClearOnReuse,
            WallstopArrayPoolConcurrentMixedSizesClearCheck,
        }

        public sealed class ConcurrentScenario
        {
            public ConcurrentScenario(string name, int threadCount, Func<int, Task> work)
            {
                Name = name;
                ThreadCount = threadCount;
                Work = work;
            }

            public string Name { get; }
            public int ThreadCount { get; }
            public Func<int, Task> Work { get; }

            public override string ToString()
            {
                return Name;
            }
        }

        private sealed class ScenarioException
        {
            public ScenarioException(int threadId, Exception exception)
            {
                ThreadId = threadId;
                Exception = exception;
            }

            public int ThreadId { get; }
            public Exception Exception { get; }
        }
#endif
    }
}
