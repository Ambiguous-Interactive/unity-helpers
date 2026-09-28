// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Sources;

    internal sealed class CountingValueTaskSource<T> : IValueTaskSource<T>
    {
        internal int GetResultCount { get; private set; }

        internal int OnCompletedCount { get; private set; }

        internal ValueTask<T> Task => new(this, 0);

        private bool _completed;
        private T _result;
        private Action<object> _continuation;
        private object _continuationState;

        /// <summary>
        /// Reads the result and counts source consumption.
        /// </summary>
        public T GetResult(short token)
        {
            ++GetResultCount;
            if (!_completed)
            {
                throw new InvalidOperationException("The source has not completed.");
            }
            return _result;
        }

        /// <summary>
        /// Reports the current completion status.
        /// </summary>
        public ValueTaskSourceStatus GetStatus(short token)
        {
            return _completed ? ValueTaskSourceStatus.Succeeded : ValueTaskSourceStatus.Pending;
        }

        /// <summary>
        /// Registers and counts completion continuations.
        /// </summary>
        public void OnCompleted(
            Action<object> continuation,
            object state,
            short token,
            ValueTaskSourceOnCompletedFlags flags
        )
        {
            ++OnCompletedCount;
            if (_completed)
            {
                continuation(state);
                return;
            }
            _continuation = continuation;
            _continuationState = state;
        }

        internal void Complete(T result)
        {
            _result = result;
            _completed = true;
            Action<object> continuation = _continuation;
            object state = _continuationState;
            _continuation = null;
            _continuationState = null;
            continuation?.Invoke(state);
        }
    }
}
