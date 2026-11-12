// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Random;

    public static partial class SpanExtensions
    {
        /// <summary>Finds the first position whose value is not less than the search value.</summary>
        /// <remarks>The span must already be sorted by the supplied comparer. Runs in O(log n) without boxing the comparer.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LowerBound<T, TComparer>(
            this ReadOnlySpan<T> span,
            T value,
            TComparer comparer
        )
            where TComparer : struct, IComparer<T>
        {
            return FindBound(span, value, comparer, upper: false);
        }

        /// <summary>Finds the first position whose value is greater than the search value.</summary>
        /// <remarks>The span must already be sorted by the supplied comparer. Runs in O(log n) without boxing the comparer.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UpperBound<T, TComparer>(
            this ReadOnlySpan<T> span,
            T value,
            TComparer comparer
        )
            where TComparer : struct, IComparer<T>
        {
            return FindBound(span, value, comparer, upper: true);
        }

        /// <summary>Finds the lower insertion bound in an already sorted mutable span.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int LowerBound<T, TComparer>(this Span<T> span, T value, TComparer comparer)
            where TComparer : struct, IComparer<T>
        {
            return FindBound((ReadOnlySpan<T>)span, value, comparer, upper: false);
        }

        /// <summary>Finds the upper insertion bound in an already sorted mutable span.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int UpperBound<T, TComparer>(this Span<T> span, T value, TComparer comparer)
            where TComparer : struct, IComparer<T>
        {
            return FindBound((ReadOnlySpan<T>)span, value, comparer, upper: true);
        }

        /// <summary>Shuffles in descending index order with one Next(i + 1) draw per index above zero.</summary>
        /// <remarks>A null generator uses PRNG.Instance. Empty and single-element spans draw nothing. The existing ascending Shuffle draw order is unchanged.</remarks>
        public static void ShuffleDescending<T>(this Span<T> span, IRandom random = null)
        {
            int count = span.Length;
            if (count < 2)
            {
                return;
            }
            random ??= PRNG.Instance;
            for (int index = count - 1; 0 < index; --index)
            {
                int selected = random.Next(index + 1);
                (span[index], span[selected]) = (span[selected], span[index]);
            }
        }

        /// <summary>Applies the same ascending shuffle to two equal-length spans.</summary>
        /// <remarks>Uses Next(i, count), matching the existing Shuffle. Spans must have disjoint storage; reinterpreted overlapping memory is unsupported. Refusal consumes no draw or write.</remarks>
        public static bool Shuffle<TFirst, TSecond>(
            this Span<TFirst> first,
            Span<TSecond> second,
            IRandom random = null
        )
        {
            int count = first.Length;
            if (second.Length != count)
            {
                return false;
            }
            if (count < 2)
            {
                return true;
            }
            random ??= PRNG.Instance;
            for (int index = 0; index < count - 1; ++index)
            {
                int selected = random.Next(index, count);
                (first[index], first[selected]) = (first[selected], first[index]);
                (second[index], second[selected]) = (second[selected], second[index]);
            }
            return true;
        }

        private static int FindBound<T, TComparer>(
            ReadOnlySpan<T> span,
            T value,
            TComparer comparer,
            bool upper
        )
            where TComparer : struct, IComparer<T>
        {
            int lower = 0;
            int high = span.Length;
            while (lower < high)
            {
                int middle = lower + (high - lower) / 2;
                int compared = comparer.Compare(span[middle], value);
                if (compared < 0 || (upper && compared == 0))
                {
                    lower = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return lower;
        }
    }
}
