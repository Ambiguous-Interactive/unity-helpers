// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;

    public static partial class IListExtensions
    {
        // Every sort runs over a T[]: an IList<T> indexer is an interface call per element, which costs
        // more across a whole sort than copying the list into a pooled array and back again does.
        private static void SortSwap<T>(T[] array, int left, int right)
        {
            (array[left], array[right]) = (array[right], array[left]);
        }

        private static void SortReverse<T>(T[] array, int start, int end)
        {
            while (start < end)
            {
                (array[start], array[end]) = (array[end], array[start]);
                start++;
                end--;
            }
        }

        private static int SelectPivotIndex<T, TComparer>(
            T[] array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int mid = left + ((right - left) >> 1);
            if (0 < comparer.Compare(array[left], array[mid]))
            {
                SortSwap(array, left, mid);
            }
            if (0 < comparer.Compare(array[left], array[right]))
            {
                SortSwap(array, left, right);
            }
            if (0 < comparer.Compare(array[mid], array[right]))
            {
                SortSwap(array, mid, right);
            }
            return mid;
        }

        private static void MergeRuns<T, TComparer>(
            T[] array,
            T[] buffer,
            int leftStart,
            int leftLength,
            int rightStart,
            int rightLength,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (leftLength == 0 || rightLength == 0)
            {
                return;
            }

            int leftEnd = leftStart + leftLength - 1;
            int rightEnd = rightStart + rightLength - 1;
            if (comparer.Compare(array[leftEnd], array[rightStart]) <= 0)
            {
                return;
            }

            if (leftLength <= rightLength)
            {
                for (int i = 0; i < leftLength; ++i)
                {
                    buffer[i] = array[leftStart + i];
                }

                int leftIndex = 0;
                int rightIndex = rightStart;
                int dest = leftStart;
                int leftLimit = leftLength;

                while (leftIndex < leftLimit && rightIndex <= rightEnd)
                {
                    if (0 < comparer.Compare(buffer[leftIndex], array[rightIndex]))
                    {
                        array[dest] = array[rightIndex];
                        rightIndex++;
                    }
                    else
                    {
                        array[dest] = buffer[leftIndex];
                        leftIndex++;
                    }
                    dest++;
                }

                while (leftIndex < leftLimit)
                {
                    array[dest] = buffer[leftIndex];
                    leftIndex++;
                    dest++;
                }
            }
            else
            {
                for (int i = 0; i < rightLength; ++i)
                {
                    buffer[i] = array[rightStart + i];
                }

                int leftIndex = leftEnd;
                int rightIndex = rightLength - 1;
                int dest = rightEnd;

                while (leftIndex >= leftStart && rightIndex >= 0)
                {
                    if (0 < comparer.Compare(array[leftIndex], buffer[rightIndex]))
                    {
                        array[dest] = array[leftIndex];
                        leftIndex--;
                    }
                    else
                    {
                        array[dest] = buffer[rightIndex];
                        rightIndex--;
                    }
                    dest--;
                }

                while (rightIndex >= 0)
                {
                    array[dest] = buffer[rightIndex];
                    rightIndex--;
                    dest--;
                }
            }
        }

        private static void CollectNaturalRuns<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer,
            List<(int start, int length)> runs
        )
            where TComparer : IComparer<T>
        {
            runs.Clear();
            int index = 0;
            while (index < count)
            {
                int start = index;
                index++;
                if (index == count)
                {
                    runs.Add((start, 1));
                    break;
                }

                int compare = comparer.Compare(array[index - 1], array[index]);
                bool ascending = compare <= 0;

                while (index < count)
                {
                    int nextCompare = comparer.Compare(array[index - 1], array[index]);
                    if (ascending)
                    {
                        if (nextCompare <= 0)
                        {
                            index++;
                            continue;
                        }
                    }
                    else
                    {
                        // A descending run is reversed to become ascending, so it may only hold strictly
                        // descending elements: reversing a pair that compares equal would reorder them.
                        if (nextCompare > 0)
                        {
                            index++;
                            continue;
                        }
                    }
                    break;
                }

                int end = index - 1;
                if (!ascending && start < end)
                {
                    SortReverse(array, start, end);
                }

                runs.Add((start, end - start + 1));
            }
        }

        private static int MakeAscendingRun<T, TComparer>(
            T[] array,
            int start,
            int count,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (start >= count - 1)
            {
                return count - start;
            }

            int runEnd = start + 1;
            int compare = comparer.Compare(array[runEnd], array[runEnd - 1]);
            bool ascending = compare >= 0;

            if (ascending)
            {
                while (runEnd < count && comparer.Compare(array[runEnd], array[runEnd - 1]) >= 0)
                {
                    runEnd++;
                }
            }
            else
            {
                while (runEnd < count && comparer.Compare(array[runEnd], array[runEnd - 1]) < 0)
                {
                    runEnd++;
                }

                SortReverse(array, start, runEnd - 1);
            }

            return runEnd - start;
        }

        private static int MedianOfFiveIndices<T, TComparer>(
            T[] array,
            int first,
            int second,
            int third,
            int fourth,
            int fifth,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int[] indices = { first, second, third, fourth, fifth };
            for (int i = 1; i < indices.Length; ++i)
            {
                int candidate = indices[i];
                T candidateValue = array[candidate];
                int j = i - 1;
                while (j >= 0 && comparer.Compare(array[indices[j]], candidateValue) > 0)
                {
                    indices[j + 1] = indices[j];
                    j--;
                }
                indices[j + 1] = candidate;
            }

            return indices[2];
        }

        private static void InsertionSortRange<T, TComparer>(
            T[] array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            if (left >= right)
            {
                return;
            }

            for (int i = left + 1; i <= right; ++i)
            {
                T key = array[i];
                int j = i - 1;
                while (j >= left && 0 < comparer.Compare(array[j], key))
                {
                    array[j + 1] = array[j];
                    j--;
                }
                array[j + 1] = key;
            }
        }

        private static void HeapSortRange<T, TComparer>(
            T[] array,
            int start,
            int end,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int length = end - start + 1;
            if (length <= 1)
            {
                return;
            }

            for (int i = (length >> 1) - 1; i >= 0; --i)
            {
                SiftDown(array, start, length, i, comparer);
            }

            for (int i = length - 1; i > 0; --i)
            {
                SortSwap(array, start, start + i);
                SiftDown(array, start, i, 0, comparer);
            }
        }

        private static void SiftDown<T, TComparer>(
            T[] array,
            int start,
            int length,
            int root,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            while (true)
            {
                int child = (root << 1) + 1;
                if (child >= length)
                {
                    return;
                }

                int rightChild = child + 1;
                if (
                    rightChild < length
                    && comparer.Compare(array[start + child], array[start + rightChild]) < 0
                )
                {
                    child = rightChild;
                }

                if (comparer.Compare(array[start + root], array[start + child]) >= 0)
                {
                    return;
                }

                SortSwap(array, start + root, start + child);
                root = child;
            }
        }

        private static bool IsRangeSorted<T, TComparer>(
            T[] array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            for (int i = left + 1; i <= right; ++i)
            {
                if (0 < comparer.Compare(array[i - 1], array[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static int FloorLog2(int value)
        {
            int result = 0;
            while (value > 1)
            {
                value >>= 1;
                result++;
            }
            return result;
        }
    }
}
