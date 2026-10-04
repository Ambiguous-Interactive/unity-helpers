// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>Returns a private retirement snapshot once across every copy of its lease.</summary>
    internal readonly struct PurgeBufferLease<T> : IDisposable
    {
        private readonly List<T> _buffer;
        private readonly DisposalLease _lease;

        internal PurgeBufferLease(List<T> buffer)
        {
            _buffer = buffer;
            _lease = buffer == null ? default : DisposalLeases.Acquire();
        }

        /// <summary>Clears and returns the snapshot if this lease still owns it.</summary>
        public void Dispose()
        {
            if (_lease.TryClaim())
            {
                PurgeBuffer<T>.Return(_buffer);
            }
        }
    }
}
