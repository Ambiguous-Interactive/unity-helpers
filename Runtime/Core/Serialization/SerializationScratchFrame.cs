// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Utils;

    internal readonly struct SerializationScratchFrame<T>
    {
        internal List<T> Items { get; }
        internal PooledResource<List<T>> Lease { get; }
        internal int Capacity { get; }

        internal SerializationScratchFrame(
            List<T> items,
            PooledResource<List<T>> lease,
            int capacity
        )
        {
            Items = items;
            Lease = lease;
            Capacity = capacity;
        }
    }
}
