// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System.Collections.Generic;

    /// <summary>
    /// Weighted no-replacement sampler that emits exact ticket counts before repeating.
    /// </summary>
    /// <typeparam name="T">The sampled item type.</typeparam>
    /// <remarks>
    /// Add each item with an integer ticket count. Calls to <see cref="TryNext(IRandom, out T)"/>
    /// draw uniformly from the remaining tickets and remove the selected ticket. Once the bag is
    /// exhausted it automatically resets, so each full cycle contains the exact configured counts.
    /// This is useful for "feel good" loot tables, spawn decks, and other cases where independent
    /// weighted rolls can create frustrating clumps.
    /// </remarks>
    public sealed class WeightedShuffleBag<T>
    {
        /// <summary>
        /// Gets the maximum configured ticket count supported by a bag instance.
        /// </summary>
        public const int MaxTickets = 1_000_000;

        private readonly List<T> _entries = new();
        private readonly List<T> _remaining = new();

        /// <summary>
        /// Gets the total configured ticket count.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Gets the ticket count remaining in the current cycle.
        /// </summary>
        public int RemainingCount => _remaining.Count;

        /// <summary>
        /// Adds an item with the specified number of tickets.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="tickets">The non-negative ticket count to add.</param>
        /// <returns>
        /// True when <paramref name="tickets"/> is valid and the bag can be safely mutated; otherwise false.
        /// Positive tickets can only be added before a cycle starts or after a cycle is exhausted.
        /// </returns>
        public bool TryAdd(T item, int tickets)
        {
            if (tickets < 0)
            {
                return false;
            }

            if (tickets == 0)
            {
                return true;
            }

            if (MaxTickets - _entries.Count < tickets)
            {
                return false;
            }

            bool hasActivePartialCycle = 0 < _remaining.Count && _remaining.Count < _entries.Count;
            if (hasActivePartialCycle)
            {
                return false;
            }

            bool resetAfterAdd = _entries.Count > 0 && _remaining.Count == 0;
            for (int i = 0; i < tickets; ++i)
            {
                _entries.Add(item);
                if (!resetAfterAdd)
                {
                    _remaining.Add(item);
                }
            }

            if (resetAfterAdd)
            {
                Reset();
            }

            return true;
        }

        /// <summary>
        /// Draws the next item using <see cref="PRNG.Instance"/>.
        /// </summary>
        /// <param name="item">The drawn item when one is available.</param>
        /// <returns>True when an item was drawn; otherwise false.</returns>
        public bool TryNext(out T item)
        {
            return TryNext(PRNG.Instance, out item);
        }

        /// <summary>
        /// Draws the next item, removing that ticket from the current cycle.
        /// </summary>
        /// <param name="random">The random generator to use. When null, <see cref="PRNG.Instance"/> is used.</param>
        /// <param name="item">The drawn item when one is available.</param>
        /// <returns>True when an item was drawn; otherwise false.</returns>
        public bool TryNext(IRandom random, out T item)
        {
            if (_entries.Count == 0)
            {
                item = default;
                return false;
            }

            if (_remaining.Count == 0)
            {
                Reset();
            }

            IRandom generator = random ?? PRNG.Instance;
            int index = generator.Next(_remaining.Count);
            item = _remaining[index];

            int lastIndex = _remaining.Count - 1;
            _remaining[index] = _remaining[lastIndex];
            _remaining.RemoveAt(lastIndex);
            return true;
        }

        /// <summary>
        /// Restores the current cycle to the full configured ticket set.
        /// </summary>
        public void Reset()
        {
            _remaining.Clear();
            _remaining.AddRange(_entries);
        }

        /// <summary>
        /// Copies the configured ticket sequence to a destination collection for persistence or diagnostics.
        /// </summary>
        /// <param name="destination">The destination collection.</param>
        /// <returns>True when the tickets were copied; otherwise false.</returns>
        public bool TryCopyConfiguredTicketsTo(ICollection<T> destination)
        {
            return TryCopyTicketsTo(_entries, destination);
        }

        /// <summary>
        /// Copies the remaining ticket sequence to a destination collection for persistence or diagnostics.
        /// </summary>
        /// <param name="destination">The destination collection.</param>
        /// <returns>True when the tickets were copied; otherwise false.</returns>
        public bool TryCopyRemainingTicketsTo(ICollection<T> destination)
        {
            return TryCopyTicketsTo(_remaining, destination);
        }

        /// <summary>
        /// Restores the remaining tickets for the current cycle from persisted state.
        /// </summary>
        /// <param name="remainingTickets">The remaining tickets to restore.</param>
        /// <returns>True when the state is valid for the configured tickets and was restored; otherwise false.</returns>
        public bool TryRestoreRemaining(IReadOnlyList<T> remainingTickets)
        {
            if (remainingTickets == null || _entries.Count < remainingTickets.Count)
            {
                return false;
            }

            if (!RemainingTicketsFitConfiguredCounts(remainingTickets))
            {
                return false;
            }

            _remaining.Clear();
            for (int i = 0; i < remainingTickets.Count; ++i)
            {
                _remaining.Add(remainingTickets[i]);
            }

            return true;
        }

        /// <summary>
        /// Removes all configured and remaining tickets.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            _remaining.Clear();
        }

        private static bool TryCopyTicketsTo(IReadOnlyList<T> source, ICollection<T> destination)
        {
            if (destination == null || destination.IsReadOnly)
            {
                return false;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                destination.Add(source[i]);
            }

            return true;
        }

        private bool RemainingTicketsFitConfiguredCounts(IReadOnlyList<T> remainingTickets)
        {
            Dictionary<T, int> counts = new();
            int nullCount = 0;
            for (int i = 0; i < _entries.Count; ++i)
            {
                AddCount(counts, _entries[i], ref nullCount);
            }

            for (int i = 0; i < remainingTickets.Count; ++i)
            {
                if (!RemoveCount(counts, remainingTickets[i], ref nullCount))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddCount(Dictionary<T, int> counts, T item, ref int nullCount)
        {
            if (item is null)
            {
                ++nullCount;
                return;
            }

            counts.TryGetValue(item, out int count);
            counts[item] = count + 1;
        }

        private static bool RemoveCount(Dictionary<T, int> counts, T item, ref int nullCount)
        {
            if (item is null)
            {
                if (nullCount <= 0)
                {
                    return false;
                }

                --nullCount;
                return true;
            }

            if (!counts.TryGetValue(item, out int count) || count <= 0)
            {
                return false;
            }

            if (count == 1)
            {
                counts.Remove(item);
            }
            else
            {
                counts[item] = count - 1;
            }

            return true;
        }
    }
}
