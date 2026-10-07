// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Utils;

    internal struct SerializationScratchScope<T>
    {
        private SerializationScratchFrame<T> _first;
        private List<SerializationScratchFrame<T>> _overflow;
        private PooledResource<List<SerializationScratchFrame<T>>> _overflowLease;
        private int _depth;

        private static void Release<TResource>(PooledResource<TResource> resource)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception)
            {
                /* Preserve the writer failure if its pool cannot accept a returned resource. */
            }
        }

        internal void Begin(ref List<T> items, ref PooledResource<List<T>> lease, int capacity)
        {
            SerializationScratchFrame<T> frame = new SerializationScratchFrame<T>(
                items,
                lease,
                capacity
            );
            if (_depth == 0)
            {
                _first = frame;
            }
            else if (_overflow == null)
            {
                PooledResource<List<SerializationScratchFrame<T>>> rented = Buffers<
                    SerializationScratchFrame<T>
                >.List.Get(out List<SerializationScratchFrame<T>> frames);
                try
                {
                    frames.Add(frame);
                }
                catch
                {
                    Release(rented);
                    throw;
                }
                _overflow = frames;
                _overflowLease = rented;
            }
            else
            {
                _overflow.Add(frame);
            }
            ++_depth;
            items = null;
            lease = default;
        }

        internal void End(ref List<T> items, ref PooledResource<List<T>> lease, ref int capacity)
        {
            if (_depth == 0)
            {
                return;
            }
            PooledResource<List<T>> completedLease = lease;
            PooledResource<List<SerializationScratchFrame<T>>> completedOverflow = default;
            SerializationScratchFrame<T> frame;
            --_depth;
            if (_depth == 0)
            {
                frame = _first;
                _first = default;
                completedOverflow = _overflowLease;
                _overflowLease = default;
                _overflow = null;
            }
            else
            {
                int index = _overflow.Count - 1;
                frame = _overflow[index];
                _overflow.RemoveAt(index);
            }
            items = frame.Items;
            lease = frame.Lease;
            capacity = frame.Capacity;
            Release(completedLease);
            Release(completedOverflow);
        }
    }
}
