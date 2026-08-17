// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;

    public static partial class IListExtensions
    {
        /// <summary>
        /// Writes a scratch array back over the list it was copied from, replacing every element.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A list that offers bulk replacement takes the whole range in one <c>Array.Copy</c>:
        /// <c>AddRange</c> takes its <see cref="ICollection{T}"/> fast path for an
        /// <see cref="ArraySegment{T}"/>, which measures 4.4x to 13x faster than assigning through an
        /// indexer. <see cref="IList{T}"/> declares no bulk setter at all, so anything else has only
        /// its indexer, one call per element.
        /// </para>
        /// <para>
        /// Reading the other way needs no such split, because <c>CopyTo</c> is already on
        /// <see cref="ICollection{T}"/>.
        /// </para>
        /// </remarks>
        private static void WriteBack<T>(IList<T> list, T[] scratch, int count)
        {
            if (list is List<T> concrete)
            {
                concrete.Clear();
                concrete.AddRange(new ArraySegment<T>(scratch, 0, count));
                return;
            }

            if (list is SerializableList<T> serializable)
            {
                serializable.Clear();
                serializable.AddRange(new ArraySegment<T>(scratch, 0, count));
                return;
            }

            for (int i = 0; i < count; i++)
            {
                list[i] = scratch[i];
            }
        }

        /// <summary>
        /// Writes a scratch array back over the list it was copied from, starting at
        /// <paramref name="head"/> and wrapping, which is a rotation.
        /// </summary>
        /// <remarks>
        /// The two runs are contiguous in the scratch array, so a list with bulk replacement takes
        /// the whole rotation in two <c>Array.Copy</c> calls and never reverses anything.
        /// </remarks>
        private static void WriteBackRotated<T>(IList<T> list, T[] scratch, int head, int count)
        {
            int tail = count - head;
            if (list is List<T> concrete)
            {
                concrete.Clear();
                concrete.AddRange(new ArraySegment<T>(scratch, head, tail));
                concrete.AddRange(new ArraySegment<T>(scratch, 0, head));
                return;
            }

            if (list is SerializableList<T> serializable)
            {
                serializable.Clear();
                serializable.AddRange(new ArraySegment<T>(scratch, head, tail));
                serializable.AddRange(new ArraySegment<T>(scratch, 0, head));
                return;
            }

            for (int i = 0; i < tail; i++)
            {
                list[i] = scratch[head + i];
            }

            for (int i = 0; i < head; i++)
            {
                list[tail + i] = scratch[i];
            }
        }
    }
}
