// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
    using System;

    internal sealed class SynchronousScopeDisposable : IDisposable
    {
        internal int DisposeCount { get; private set; }

        private readonly Action _onDispose;

        internal SynchronousScopeDisposable(Action onDispose = null)
        {
            _onDispose = onDispose;
        }

        public override bool Equals(object obj)
        {
            return obj is SynchronousScopeDisposable;
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public void Dispose()
        {
            ++DisposeCount;
            _onDispose?.Invoke();
        }
    }
}
