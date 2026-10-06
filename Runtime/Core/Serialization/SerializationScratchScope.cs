// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Utils;

    internal struct SerializationScratchScope<T>
    {
        private SerializationScratchFrame<T> first;
        private List<SerializationScratchFrame<T>> overflow;
        private PooledResource<List<SerializationScratchFrame<T>>> overflowLease;
        private int depth;

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
            if (depth == 0)
            {
                first = frame;
            }
            else if (overflow == null)
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
                overflow = frames;
                overflowLease = rented;
            }
            else
            {
                overflow.Add(frame);
            }
            ++depth;
            items = null;
            lease = default;
        }

        internal void End(ref List<T> items, ref PooledResource<List<T>> lease, ref int capacity)
        {
            if (depth == 0)
            {
                return;
            }
            PooledResource<List<T>> completedLease = lease;
            PooledResource<List<SerializationScratchFrame<T>>> completedOverflow = default;
            SerializationScratchFrame<T> frame;
            --depth;
            if (depth == 0)
            {
                frame = first;
                first = default;
                completedOverflow = overflowLease;
                overflowLease = default;
                overflow = null;
            }
            else
            {
                int index = overflow.Count - 1;
                frame = overflow[index];
                overflow.RemoveAt(index);
            }
            items = frame.Items;
            lease = frame.Lease;
            capacity = frame.Capacity;
            Release(completedLease);
            Release(completedOverflow);
        }
    }
}
