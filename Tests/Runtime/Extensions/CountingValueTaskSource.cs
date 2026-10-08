// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Sources;

    internal sealed class CountingValueTaskSource<T> : IValueTaskSource<T>, IValueTaskSource
    {
        internal int GetResultCount { get; private set; }

        internal int OnCompletedCount { get; private set; }

        internal bool OwnsSlot { get; private set; } = true;

        internal ValueTask<T> Task => new(this, 0);

        internal ValueTask VoidTask => new(this, 0);

        private bool _completed;
        private T _result;
        private Exception _failure;
        private Action<object> _continuation;
        private object _continuationState;

        /// <summary>
        /// Reads the result and counts source consumption.
        /// </summary>
        public T GetResult(short token)
        {
            if (!_completed || !OwnsSlot || token != 0)
            {
                throw new InvalidOperationException(
                    "The source is pending, already consumed, or has an invalid token."
                );
            }
            ++GetResultCount;
            OwnsSlot = false;
            if (_failure != null)
            {
                throw _failure;
            }
            return _result;
        }

        /// <summary>
        /// Reports the current completion status.
        /// </summary>
        public ValueTaskSourceStatus GetStatus(short token)
        {
            if (!_completed)
            {
                return ValueTaskSourceStatus.Pending;
            }
            if (_failure is OperationCanceledException)
            {
                return ValueTaskSourceStatus.Canceled;
            }
            return _failure != null
                ? ValueTaskSourceStatus.Faulted
                : ValueTaskSourceStatus.Succeeded;
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
            CompleteCore(result, null);
        }

        internal void CompleteException(Exception failure)
        {
            CompleteCore(default, failure);
        }

        private void CompleteCore(T result, Exception failure)
        {
            _result = result;
            _failure = failure;
            _completed = true;
            Action<object> continuation = _continuation;
            object state = _continuationState;
            _continuation = null;
            _continuationState = null;
            continuation?.Invoke(state);
        }

        void IValueTaskSource.GetResult(short token)
        {
            GetResult(token);
        }
    }
}
