// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public readonly struct DeferredDisposalResult<T>
        : global::System.IEquatable<DeferredDisposalResult<T>>
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

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(DeferredDisposalResult<T> other)
        {
            return EqualityComparer<T>.Default.Equals(result, other.result)
                && EqualityComparer<Func<ValueTask>>.Default.Equals(
                    _disposeAsync,
                    other._disposeAsync
                );
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is DeferredDisposalResult<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                result,
                _disposeAsync
            );
        }
    }
}
