// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Threading.Tasks;

    public readonly struct DeferredDisposalResult<T>
    {
        public readonly T result;

        private readonly Func<ValueTask> _disposeAsync;

        public DeferredDisposalResult(T result, Func<ValueTask> disposeAsync)
        {
            this.result = result;
            _disposeAsync = disposeAsync ?? throw new ArgumentNullException(nameof(disposeAsync));
        }

        public ValueTask DisposeAsync()
        {
            return _disposeAsync();
        }
    }
}
