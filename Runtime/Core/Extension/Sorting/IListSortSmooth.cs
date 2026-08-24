// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is SmoothSort, from the published work of Edsger W. Dijkstra, with later
// analysis by Stefan Edelkamp and Armin Wegener, and explanatory write-ups by Nico Lomuto and Keith
// Schwarz. This is an independent implementation of a published algorithm; the design is the original
// author's. See docs/project/third-party-notices.md.

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using Utils;

    public static partial class IListExtensions
    {
        private static readonly int[] SmoothSortLeonardoNumbers =
        {
            1,
            1,
            3,
            5,
            9,
            15,
            25,
            41,
            67,
            109,
            177,
            287,
            465,
            753,
            1219,
            1973,
            3193,
            5167,
            8361,
            13529,
            21891,
            35421,
            57313,
            92735,
            150049,
            242785,
            392835,
            635621,
            1028457,
            1664079,
            2692537,
            4356617,
            7049155,
            11405773,
            18454929,
            29860703,
            48315633,
            78176337,
            126491971,
            204668309,
            331160281,
            535828591,
            866988873,
            1402817465,
            int.MaxValue,
        };

        /// <summary>
        /// Sorts the list using SmoothSort, providing O(n) behavior on nearly sorted data while remaining in-place.
        /// </summary>
        /// <remarks>
        /// Implementation reference: SmoothSort (Dijkstra, Edelkamp/Wegener) via Nico Lomuto’s refresher and Keith Schwarz’s lecture notes.
        /// </remarks>
        public static void SmoothSort<T, TComparer>(this IList<T> list, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int count = list.Count;
            if (count < 2)
            {
                return;
            }

            if (list is T[] array)
            {
                SmoothSortCore(array, count, comparer);
                return;
            }

            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            SmoothSortCore(scratch, count, comparer);
            WriteBack(list, scratch, count);
        }

        private static void SmoothSortCore<T, TComparer>(T[] array, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int head = 0;
            int last = count - 1;
            int p = 1;
            int pshift = 1;

            while (head < last)
            {
                if ((p & 3) == 3)
                {
                    SmoothSortSift(array, head, pshift, comparer);
                    p >>= 2;
                    pshift += 2;
                }
                else
                {
                    if (last - head <= SmoothSortLeonardoNumbers[pshift - 1])
                    {
                        SmoothSortTrinkle(array, head, p, pshift, false, comparer);
                    }
                    else
                    {
                        SmoothSortSift(array, head, pshift, comparer);
                    }

                    if (pshift == 1)
                    {
                        p <<= 1;
                        pshift--;
                    }
                    else
                    {
                        p <<= pshift - 1;
                        pshift = 1;
                    }
                }

                p |= 1;
                head++;
            }

            SmoothSortTrinkle(array, head, p, pshift, false, comparer);

            while (pshift != 1 || p != 1)
            {
                if (pshift <= 1)
                {
                    int trailing = SmoothSortTrailingZeroCount(p & ~1);
                    p >>= trailing;
                    pshift += trailing;
                }
                else
                {
                    p <<= 2;
                    p ^= 7;
                    pshift -= 2;

                    SmoothSortTrinkle(
                        array,
                        head - SmoothSortLeonardoNumbers[pshift] - 1,
                        p >> 1,
                        pshift + 1,
                        true,
                        comparer
                    );
                    SmoothSortTrinkle(array, head - 1, p, pshift, true, comparer);
                }

                head--;
            }
        }

        private static void SmoothSortSift<T, TComparer>(
            T[] array,
            int head,
            int pshift,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            T value = array[head];

            while (1 < pshift)
            {
                int right = head - 1;
                int left = head - 1 - SmoothSortLeonardoNumbers[pshift - 2];

                if (
                    0 <= comparer.Compare(value, array[left])
                    && 0 <= comparer.Compare(value, array[right])
                )
                {
                    break;
                }

                if (0 <= comparer.Compare(array[left], array[right]))
                {
                    array[head] = array[left];
                    head = left;
                    pshift--;
                }
                else
                {
                    array[head] = array[right];
                    head = right;
                    pshift -= 2;
                }
            }

            array[head] = value;
        }

        private static void SmoothSortTrinkle<T, TComparer>(
            T[] array,
            int head,
            int p,
            int pshift,
            bool isTrusty,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            T value = array[head];

            while (p != 1)
            {
                int stepson = head - SmoothSortLeonardoNumbers[pshift];
                if (comparer.Compare(array[stepson], value) <= 0)
                {
                    break;
                }

                if (!isTrusty && 1 < pshift)
                {
                    int right = head - 1;
                    int left = head - 1 - SmoothSortLeonardoNumbers[pshift - 2];
                    if (
                        0 <= comparer.Compare(array[right], array[stepson])
                        || 0 <= comparer.Compare(array[left], array[stepson])
                    )
                    {
                        break;
                    }
                }

                array[head] = array[stepson];
                head = stepson;
                int trailing = SmoothSortTrailingZeroCount(p & ~1);
                p >>= trailing;
                pshift += trailing;
                isTrusty = false;
            }

            if (!isTrusty)
            {
                array[head] = value;
                SmoothSortSift(array, head, pshift, comparer);
                return;
            }

            array[head] = value;
        }

        private static int SmoothSortTrailingZeroCount(int value)
        {
            if (value == 0)
            {
                return 32;
            }

            int count = 0;
            while ((value & 1) == 0)
            {
                count++;
                value >>= 1;
            }

            return count;
        }
    }
}
