// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;

    internal readonly struct WButtonCoroutineTicket : IEquatable<WButtonCoroutineTicket>
    {
        internal WButtonCoroutineTicket(Guid id)
        {
            Id = id;
        }

        internal Guid Id { get; }

        public bool Equals(WButtonCoroutineTicket other)
        {
            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            if (obj is WButtonCoroutineTicket other)
            {
                return Equals(other);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static readonly WButtonCoroutineTicket None = new(Guid.Empty);
    }

    internal static class WButtonCoroutineScheduler
    {
        private static readonly List<CoroutineInstance> Instances = new();
        private static bool _isSubscribed;

        internal static WButtonCoroutineTicket Schedule(
            System.Collections.IEnumerator routine,
            CancellationTokenSource cancellationSource,
            Action onCompleted,
            Action<Exception> onFaulted,
            Action onCancelled
        )
        {
            if (routine == null)
            {
                throw new ArgumentNullException(nameof(routine));
            }

            IEnumerator<object> wrapped = WrapLegacyEnumerator(routine);
            CoroutineInstance instance = new(
                wrapped,
                cancellationSource,
                onCompleted,
                onFaulted,
                onCancelled
            );
            Instances.Add(instance);
            EnsureSubscribed();
            return new WButtonCoroutineTicket(instance.Id);
        }

        internal static void Cancel(WButtonCoroutineTicket ticket)
        {
            if (ticket.Equals(WButtonCoroutineTicket.None))
            {
                return;
            }

            foreach (CoroutineInstance instance in Instances)
            {
                if (instance.Id == ticket.Id)
                {
                    instance.RequestCancel();
                    break;
                }
            }
        }

        private static void EnsureSubscribed()
        {
            if (_isSubscribed)
            {
                return;
            }

            EditorApplication.update += Update;
            _isSubscribed = true;
        }

        private static void Update()
        {
            if (Instances.Count == 0)
            {
                if (_isSubscribed)
                {
                    EditorApplication.update -= Update;
                    _isSubscribed = false;
                }
                return;
            }

            for (int index = Instances.Count - 1; 0 <= index; index--)
            {
                CoroutineInstance instance = Instances[index];
                instance.Tick();
                if (instance.IsCompleted)
                {
                    Instances.RemoveAt(index);
                }
            }
        }

        private static IEnumerator<object> WrapLegacyEnumerator(
            System.Collections.IEnumerator enumerator
        )
        {
            while (enumerator.MoveNext())
            {
                object yielded = enumerator.Current;
                if (yielded is IEnumerator<object> typed)
                {
                    yield return typed;
                }
                else if (yielded is System.Collections.IEnumerator legacy)
                {
                    yield return WrapLegacyEnumerator(legacy);
                }
                else
                {
                    yield return null;
                }
            }
        }

        private sealed class CoroutineInstance
        {
            private readonly Stack<IEnumerator<object>> _stack = new();
            private readonly CancellationTokenSource _cancellationSource;
            private readonly Action _onCompleted;
            private readonly Action<Exception> _onFaulted;
            private readonly Action _onCancelled;

            internal CoroutineInstance(
                IEnumerator<object> root,
                CancellationTokenSource cancellationSource,
                Action onCompleted,
                Action<Exception> onFaulted,
                Action onCancelled
            )
            {
                Id = Guid.NewGuid();
                _stack.Push(root);
                _cancellationSource = cancellationSource;
                _onCompleted = onCompleted;
                _onFaulted = onFaulted;
                _onCancelled = onCancelled;
            }

            internal Guid Id { get; }

            internal bool IsCompleted { get; private set; }

            internal void RequestCancel()
            {
                if (_cancellationSource is { IsCancellationRequested: false })
                {
                    _cancellationSource.Cancel();
                }
            }

            internal void Tick()
            {
                if (IsCompleted)
                {
                    return;
                }

                if (_cancellationSource is { IsCancellationRequested: true })
                {
                    IsCompleted = true;
                    _onCancelled?.Invoke();
                    return;
                }

                if (_stack.Count == 0)
                {
                    Complete();
                    return;
                }

                IEnumerator<object> current = _stack.Peek();
                try
                {
                    if (!current.MoveNext())
                    {
                        _stack.Pop();
                        if (_stack.Count == 0)
                        {
                            Complete();
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    IsCompleted = true;
                    _onFaulted?.Invoke(ex);
                    return;
                }

                object yielded = current.Current;
                if (yielded == null)
                {
                    return;
                }

                if (yielded is IEnumerator<object> nested)
                {
                    _stack.Push(nested);
                }
                else if (yielded is System.Collections.IEnumerator legacyEnumerator)
                {
                    _stack.Push(WrapLegacyEnumerator(legacyEnumerator));
                }
                else if (yielded is YieldInstruction) { }
            }

            private void Complete()
            {
                if (IsCompleted)
                {
                    return;
                }

                IsCompleted = true;
                _onCompleted?.Invoke();
            }
        }
    }
#endif
}
