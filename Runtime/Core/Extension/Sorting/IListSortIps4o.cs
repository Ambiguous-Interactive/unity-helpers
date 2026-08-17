// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is IPS4o (in-place parallel super scalar samplesort), from the paper by
// Michael Axtmann, Sascha Witt, Daniel Ferizovic, and Peter Sanders, https://arxiv.org/abs/1705.02257.
// This is an independent implementation of a published algorithm; the design is the original authors'.
// See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        private const int Ips4oInsertionThreshold = 32;

        private const int Ips4oFallbackThreshold = 256;

        private const int Ips4oTargetBucketSize = 64;

        private const int Ips4oMaxBucketCount = 32;

        /// <summary>
        /// Sorts the list using IPS⁴o, a cache-aware samplesort that partitions into multiple buckets per pass.
        /// </summary>
        /// <remarks>
        /// Implementation reference: IPS⁴o samplesort (Axtmann, Witt, Ferizovic, Sanders), https://arxiv.org/abs/1705.02257.
        /// </remarks>
        public static void Ips4oSort<T, TComparer>(this IList<T> array, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = array.Count;
            if (count < 2)
            {
                return;
            }

            Ips4oSortRange(array, 0, count - 1, comparer);
        }

        private static void Ips4oSortRange<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int length = right - left + 1;
            if (length <= Ips4oInsertionThreshold)
            {
                InsertionSortRange(array, left, right, comparer);
                return;
            }

            if (length <= Ips4oFallbackThreshold)
            {
                PatternDefeatingQuickSortRange(array, left, right, comparer, 2 * FloorLog2(length));
                return;
            }

            int bucketCount = DetermineIps4oBucketCount(length);
            int pivotCount = bucketCount - 1;
            int sampleSize = Math.Min(length, bucketCount * 2 - 1);

            using PooledArray<T> sampleLease = SystemArrayPool<T>.Get(sampleSize, out T[] sample);
            Ips4oBuildSample(array, left, right, sample, sampleSize);
            Array.Sort(sample, 0, sampleSize, comparer);

            using PooledArray<T> pivotLease = SystemArrayPool<T>.Get(pivotCount, out T[] pivots);
            Ips4oSelectPivots(sample, sampleSize, pivots, bucketCount);

            using PooledArray<int> countLease = WallstopArrayPool<int>.Get(
                bucketCount,
                out int[] bucketCounts
            );
            using PooledArray<int> offsetLease = WallstopArrayPool<int>.Get(
                bucketCount,
                out int[] bucketOffsets
            );
            using PooledArray<int> positionLease = WallstopArrayPool<int>.Get(
                bucketCount,
                out int[] bucketPositions
            );
            Array.Clear(bucketCounts, 0, bucketCount);

            for (int i = left; i <= right; ++i)
            {
                int bucket = Ips4oLocateBucket(array[i], pivots, pivotCount, comparer);
                bucketCounts[bucket]++;
            }

            int running = 0;
            for (int i = 0; i < bucketCount; ++i)
            {
                bucketOffsets[i] = running;
                running += bucketCounts[i];
            }

            Array.Copy(bucketOffsets, bucketPositions, bucketCount);

            using PooledArray<T> bufferLease = SystemArrayPool<T>.Get(length, out T[] buffer);
            for (int i = left; i <= right; ++i)
            {
                T value = array[i];
                int bucket = Ips4oLocateBucket(value, pivots, pivotCount, comparer);
                int destination = bucketPositions[bucket]++;
                buffer[destination] = value;
            }

            for (int i = 0; i < length; ++i)
            {
                array[left + i] = buffer[i];
            }

            bool degeneratePartition = false;
            for (int i = 0; i < bucketCount; ++i)
            {
                if (bucketCounts[i] == length)
                {
                    degeneratePartition = true;
                    break;
                }
            }

            if (degeneratePartition)
            {
                PatternDefeatingQuickSortRange(array, left, right, comparer, 2 * FloorLog2(length));
                return;
            }

            for (int i = 0; i < bucketCount; ++i)
            {
                int bucketSize = bucketCounts[i];
                if (bucketSize <= 1)
                {
                    continue;
                }

                int bucketLeft = left + bucketOffsets[i];
                int bucketRight = bucketLeft + bucketSize - 1;
                Ips4oSortRange(array, bucketLeft, bucketRight, comparer);
            }
        }

        private static int DetermineIps4oBucketCount(int length)
        {
            int estimate = length / Ips4oTargetBucketSize;
            if (estimate < 4)
            {
                estimate = 4;
            }
            else if (estimate > Ips4oMaxBucketCount)
            {
                estimate = Ips4oMaxBucketCount;
            }

            return estimate;
        }

        private static void Ips4oBuildSample<T>(
            IList<T> array,
            int left,
            int right,
            T[] sample,
            int sampleSize
        )
        {
            if (sampleSize == 0)
            {
                return;
            }

            int length = right - left + 1;
            double stride = (double)length / sampleSize;
            double position = stride * 0.5d;

            for (int i = 0; i < sampleSize; ++i)
            {
                int offset = (int)Math.Max(0, Math.Min(length - 1, Math.Floor(position)));
                sample[i] = array[left + offset];
                position += stride;
            }
        }

        private static void Ips4oSelectPivots<T>(
            T[] sample,
            int sampleSize,
            T[] pivots,
            int bucketCount
        )
        {
            if (pivots.Length == 0)
            {
                return;
            }

            for (int i = 1; i < bucketCount; ++i)
            {
                int index = i * sampleSize / bucketCount;
                if (index >= sampleSize)
                {
                    index = sampleSize - 1;
                }
                pivots[i - 1] = sample[index];
            }
        }

        private static int Ips4oLocateBucket<T, TComparer>(
            T value,
            T[] pivots,
            int pivotCount,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (pivotCount == 0)
            {
                return 0;
            }

            int low = 0;
            int high = pivotCount;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (comparer.Compare(value, pivots[mid]) > 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }
}
