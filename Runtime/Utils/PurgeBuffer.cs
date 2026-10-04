// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>Reuses privately owned retirement snapshots independently of the pools they retire.</summary>
    internal static class PurgeBuffer<T>
    {
        internal const int MaximumRetainedBuffers = 4;
        internal const int MaximumRetainedCapacity = 4096;

        [ThreadStatic]
        private static List<T>[] _available;

        [ThreadStatic]
        private static int _availableCount;

        internal static PurgeBufferLease<T> Get(out List<T> buffer)
        {
            buffer = Rent();
            return new PurgeBufferLease<T>(buffer);
        }

        internal static List<T> Rent()
        {
            if (_availableCount == 0)
            {
                return new List<T>();
            }

            int index = --_availableCount;
            List<T> buffer = _available[index];
            _available[index] = null;
            return buffer;
        }

        internal static void Return(List<T> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            if (
                MaximumRetainedCapacity < buffer.Capacity
                || MaximumRetainedBuffers <= _availableCount
            )
            {
                return;
            }

            if (_available == null)
            {
                _available = new List<T>[MaximumRetainedBuffers];
            }

            _available[_availableCount++] = buffer;
        }
    }
}
