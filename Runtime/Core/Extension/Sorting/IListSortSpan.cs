// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using Utils;

    public static partial class IListExtensions
    {
        /// <summary>Sorts a span using the selected list sorting algorithm and comparer.</summary>
        /// <remarks>
        /// Copies through one pooled buffer of the span's length, preserving the algorithm's
        /// ordering and stability guarantees. Only the supplied span is written back, after sorting
        /// succeeds. Comparer exceptions propagate and leave the span unchanged. The buffer is
        /// returned on failure as well as success, clearing managed references on return.
        /// The algorithm may rent additional scratch storage; cold pools can allocate.
        /// Empty and single-element spans require no buffer and do not invoke the comparer.
        /// The algorithm argument is required to distinguish this from framework span sorting.
        /// </remarks>
        /// <exception cref="InvalidEnumArgumentException">The selected algorithm is undefined.</exception>
        public static void Sort<T, TComparer>(
            this Span<T> span,
            TComparer comparer,
            SortAlgorithm sortAlgorithm
        )
            where TComparer : IComparer<T>
        {
            if (!sortAlgorithm.IsValid())
            {
                throw new InvalidEnumArgumentException(
                    nameof(sortAlgorithm),
                    (int)sortAlgorithm,
                    typeof(SortAlgorithm)
                );
            }
            int count = span.Length;
            if (count < 2)
            {
                return;
            }
            using PooledArray<T> scratchLease = SystemArrayPool<T>.Get(count, out T[] scratch);
            span.CopyTo(scratch);
            SortArrayRange(scratch, count, comparer, sortAlgorithm);
            scratch.AsSpan(0, count).CopyTo(span);
        }

        /// <summary>Sorts a span using GhostSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void GhostSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Ghost);
        }

        /// <summary>Sorts a span using InsertionSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void InsertionSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Insertion);
        }

        /// <summary>Sorts a span using MeteorSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void MeteorSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Meteor);
        }

        /// <summary>Sorts a span using PatternDefeatingQuickSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void PatternDefeatingQuickSort<T, TComparer>(
            this Span<T> span,
            TComparer comparer
        )
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.PatternDefeatingQuickSort);
        }

        /// <summary>Sorts a span using GrailSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void GrailSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Grail);
        }

        /// <summary>Sorts a span using PowerSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void PowerSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Power);
        }

        /// <summary>Sorts a span using TimSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void TimSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Tim);
        }

        /// <summary>Sorts a span using JesseSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void JesseSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Jesse);
        }

        /// <summary>Sorts a span using GreenSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void GreenSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Green);
        }

        /// <summary>Sorts a span using SkaSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void SkaSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Ska);
        }

        /// <summary>Sorts a span using IpnSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void IpnSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Ipn);
        }

        /// <summary>Sorts a span using SmoothSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void SmoothSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Smooth);
        }

        /// <summary>Sorts a span using BlockMergeSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void BlockMergeSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Block);
        }

        /// <summary>Sorts a span using Ips4oSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void Ips4oSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Ips4o);
        }

        /// <summary>Sorts a span using PowerSortPlus and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void PowerSortPlus<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.PowerPlus);
        }

        /// <summary>Sorts a span using GlideSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void GlideSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Glide);
        }

        /// <summary>Sorts a span using FluxSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void FluxSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Flux);
        }

        /// <summary>Sorts a span using YamSort and the supplied comparer.</summary>
        /// <remarks>Uses the pooled-copy and failure guarantees of <see cref="Sort{T, TComparer}(Span{T}, TComparer, SortAlgorithm)"/>.</remarks>
        public static void YamSort<T, TComparer>(this Span<T> span, TComparer comparer)
            where TComparer : IComparer<T>
        {
            span.Sort(comparer, SortAlgorithm.Yam);
        }

        private static void SortArrayRange<T, TComparer>(
            T[] array,
            int count,
            TComparer comparer,
            SortAlgorithm sortAlgorithm
        )
            where TComparer : IComparer<T>
        {
            switch (sortAlgorithm)
            {
                case SortAlgorithm.Ghost:
                    GhostSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Insertion:
                    InsertionSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Meteor:
                    MeteorSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.PatternDefeatingQuickSort:
                    PatternDefeatingQuickSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Grail:
                    GrailSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Power:
                    PowerSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Tim:
                    TimSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Jesse:
                    JesseSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Green:
                    GreenSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Ska:
                    SkaSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Ipn:
                    IpnSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Smooth:
                    SmoothSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Block:
                    BlockMergeSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Ips4o:
                    Ips4oSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.PowerPlus:
                    PowerSortPlusCore(array, count, comparer);
                    return;
                case SortAlgorithm.Glide:
                    GlideSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Flux:
                    FluxSortCore(array, count, comparer);
                    return;
                case SortAlgorithm.Yam:
                    YamSortCore(array, count, comparer);
                    return;
            }
        }
    }
}
