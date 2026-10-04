// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using static WallstopStudios.UnityHelpers.Utils.Buffers;
    using Buffers = WallstopStudios.UnityHelpers.Utils.Buffers;

    internal sealed class WaitInstructionCacheScope : IDisposable
    {
        private readonly WaitInstructionCacheSnapshot<WaitForSeconds> _waitForSecondsSnapshot;
        private readonly WaitInstructionCacheSnapshot<WaitForSecondsRealtime> _waitForSecondsRealtimeSnapshot;
        private readonly float _quantizationStepSnapshot;
        private readonly int _maxDistinctEntriesSnapshot;
        private readonly bool _useLruSnapshot;
        private readonly int _waitForSecondsLimitHitsSnapshot;
        private readonly int _waitForSecondsRealtimeLimitHitsSnapshot;
        private readonly int _waitForSecondsEvictionsSnapshot;
        private readonly int _waitForSecondsRealtimeEvictionsSnapshot;
        private bool _disposed;

        internal WaitInstructionCacheScope()
        {
            _waitForSecondsSnapshot = SnapshotCache(Buffers.WaitForSeconds, WaitForSecondsOrder);
            _waitForSecondsRealtimeSnapshot = SnapshotCache(
                Buffers.WaitForSecondsRealtime,
                WaitForSecondsRealtimeOrder
            );
            _quantizationStepSnapshot = WaitInstructionQuantizationStepSeconds;
            _maxDistinctEntriesSnapshot = WaitInstructionMaxDistinctEntries;
            _useLruSnapshot = WaitInstructionUseLruEviction;
            _waitForSecondsLimitHitsSnapshot = Volatile.Read(ref _waitForSecondsLimitHits);
            _waitForSecondsRealtimeLimitHitsSnapshot = Volatile.Read(
                ref _waitForSecondsRealtimeLimitHits
            );
            _waitForSecondsEvictionsSnapshot = Volatile.Read(ref _waitForSecondsEvictions);
            _waitForSecondsRealtimeEvictionsSnapshot = Volatile.Read(
                ref _waitForSecondsRealtimeEvictions
            );

            Reset();
        }

        internal static void Reset()
        {
#if !SINGLE_THREADED
            lock (WaitInstructionCacheLock)
            {
#endif
                Buffers.WaitForSeconds.Clear();
                Buffers.WaitForSecondsRealtime.Clear();
                WaitForSecondsOrder.Clear();
                WaitForSecondsRealtimeOrder.Clear();
#if !SINGLE_THREADED
            }
#endif
            WaitInstructionQuantizationStepSeconds = 0f;
            WaitInstructionMaxDistinctEntries = WaitInstructionDefaultMaxDistinctEntries;
            WaitInstructionUseLruEviction = false;
            Volatile.Write(ref _waitForSecondsLimitHits, 0);
            Volatile.Write(ref _waitForSecondsRealtimeLimitHits, 0);
            Volatile.Write(ref _waitForSecondsEvictions, 0);
            Volatile.Write(ref _waitForSecondsRealtimeEvictions, 0);
        }

        private static WaitInstructionCacheSnapshot<TInstruction> SnapshotCache<TInstruction>(
            Dictionary<float, WaitInstructionCacheEntry<TInstruction>> cache,
            LinkedList<float> order
        )
            where TInstruction : class
        {
#if !SINGLE_THREADED
            lock (WaitInstructionCacheLock)
            {
#endif
                Dictionary<float, TInstruction> entries = new(cache.Count);
                foreach (KeyValuePair<float, WaitInstructionCacheEntry<TInstruction>> pair in cache)
                {
                    entries[pair.Key] = pair.Value._value;
                }

                List<float> ordering = new(order);
                return new WaitInstructionCacheSnapshot<TInstruction>(entries, ordering);
#if !SINGLE_THREADED
            }
#endif
        }

        private static void RestoreCache<TInstruction>(
            Dictionary<float, WaitInstructionCacheEntry<TInstruction>> cache,
            LinkedList<float> order,
            WaitInstructionCacheSnapshot<TInstruction> snapshot
        )
            where TInstruction : class
        {
#if !SINGLE_THREADED
            lock (WaitInstructionCacheLock)
            {
#endif
                cache.Clear();
                order.Clear();

                if (snapshot.Order == null || snapshot.Entries == null)
                {
                    return;
                }

                Dictionary<float, LinkedListNode<float>> nodes = new(snapshot.Order.Count);
                foreach (float key in snapshot.Order)
                {
                    LinkedListNode<float> node = order.AddLast(key);
                    nodes[key] = node;
                }

                foreach (KeyValuePair<float, TInstruction> pair in snapshot.Entries)
                {
                    if (!nodes.TryGetValue(pair.Key, out LinkedListNode<float> node))
                    {
                        node = order.AddLast(pair.Key);
                        nodes[pair.Key] = node;
                    }

                    cache[pair.Key] = new WaitInstructionCacheEntry<TInstruction>(pair.Value, node);
                }
#if !SINGLE_THREADED
            }
#endif
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            WaitInstructionQuantizationStepSeconds = _quantizationStepSnapshot;
            WaitInstructionMaxDistinctEntries = _maxDistinctEntriesSnapshot;
            WaitInstructionUseLruEviction = _useLruSnapshot;
            Volatile.Write(ref _waitForSecondsLimitHits, _waitForSecondsLimitHitsSnapshot);
            Volatile.Write(
                ref _waitForSecondsRealtimeLimitHits,
                _waitForSecondsRealtimeLimitHitsSnapshot
            );
            Volatile.Write(ref _waitForSecondsEvictions, _waitForSecondsEvictionsSnapshot);
            Volatile.Write(
                ref _waitForSecondsRealtimeEvictions,
                _waitForSecondsRealtimeEvictionsSnapshot
            );

            RestoreCache(Buffers.WaitForSeconds, WaitForSecondsOrder, _waitForSecondsSnapshot);
            RestoreCache(
                Buffers.WaitForSecondsRealtime,
                WaitForSecondsRealtimeOrder,
                _waitForSecondsRealtimeSnapshot
            );
        }

        private readonly struct WaitInstructionCacheSnapshot<TInstruction>
            where TInstruction : class
        {
            internal Dictionary<float, TInstruction> Entries { get; }

            internal List<float> Order { get; }

            internal WaitInstructionCacheSnapshot(
                Dictionary<float, TInstruction> entries,
                List<float> order
            )
            {
                Entries = entries;
                Order = order;
            }
        }
    }
}
