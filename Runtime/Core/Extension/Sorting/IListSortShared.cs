// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;

    public static partial class IListExtensions
    {
        private static int SelectPivotIndex<T, TComparer>(
            IList<T> array,
            int left,
            int right,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            int mid = left + ((right - left) >> 1);
            if (0 < comparer.Compare(array[left], array[mid]))
            {
                array.Swap(left, mid);
            }
            if (0 < comparer.Compare(array[left], array[right]))
            {
                array.Swap(left, right);
            }
            if (0 < comparer.Compare(array[mid], array[right]))
            {
                array.Swap(mid, right);
            }
            return mid;
        }

        private static void MergeRuns<T, TComparer>(
            IList<T> array,
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
            IList<T> array,
            TComparer comparer,
            List<(int start, int length)> runs
        )
            where TComparer : IComparer<T>
        {
            runs.Clear();
            int count = array.Count;
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
                        if (nextCompare >= 0)
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
                    array.Reverse(start, end);
                }

                runs.Add((start, end - start + 1));
            }
        }

        private static int MakeAscendingRun<T, TComparer>(
            IList<T> array,
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

                array.Reverse(start, runEnd - 1);
            }

            return runEnd - start;
        }

        private static int MedianOfFiveIndices<T, TComparer>(
            IList<T> array,
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
            IList<T> array,
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
            IList<T> array,
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
                array.Swap(start, start + i);
                SiftDown(array, start, i, 0, comparer);
            }
        }

        private static void SiftDown<T, TComparer>(
            IList<T> array,
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

                array.Swap(start + root, start + child);
                root = child;
            }
        }

        private static bool IsRangeSorted<T, TComparer>(
            IList<T> array,
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
