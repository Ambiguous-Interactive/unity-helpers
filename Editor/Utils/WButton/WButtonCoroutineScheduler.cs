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
        public static readonly WButtonCoroutineTicket None = new(Guid.Empty);

        internal Guid Id { get; }

        internal WButtonCoroutineTicket(Guid id)
        {
            Id = id;
        }

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
    }

    internal static class WButtonCoroutineScheduler
    {
        private static readonly List<CoroutineInstance> Instances = new();
        private static bool _isSubscribed;

        /// <summary>Owns a coroutine and releases its enumerators on completion, cancellation, or failure.</summary>
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

            CoroutineInstance instance = new(
                routine,
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

        internal static void Update()
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

        private static void EnsureSubscribed()
        {
            if (_isSubscribed)
            {
                return;
            }

            EditorApplication.update += Update;
            _isSubscribed = true;
        }

        private sealed class CoroutineInstance
        {
            internal Guid Id { get; }

            internal bool IsCompleted { get; private set; }

            private readonly Stack<System.Collections.IEnumerator> _stack = new();
            private readonly CancellationTokenSource _cancellationSource;
            private readonly Action _onCompleted;
            private readonly Action<Exception> _onFaulted;
            private readonly Action _onCancelled;
            private bool _cancelRequested;

            internal CoroutineInstance(
                System.Collections.IEnumerator root,
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

            internal void RequestCancel()
            {
                if (IsCompleted)
                {
                    return;
                }
                _cancelRequested = true;
                try
                {
                    if (_cancellationSource is { IsCancellationRequested: false })
                    {
                        _cancellationSource.Cancel();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            internal void Tick()
            {
                if (IsCompleted)
                {
                    return;
                }
                if (_cancelRequested || _cancellationSource is { IsCancellationRequested: true })
                {
                    Finish(null, cancelled: true);
                    return;
                }
                if (_stack.Count == 0)
                {
                    Finish(null, cancelled: false);
                    return;
                }

                System.Collections.IEnumerator current = _stack.Peek();
                try
                {
                    if (!current.MoveNext())
                    {
                        _stack.Pop();
                        if (current is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                        if (_stack.Count == 0)
                        {
                            Finish(null, cancelled: false);
                        }
                        return;
                    }
                    if (current.Current is System.Collections.IEnumerator nested)
                    {
                        _stack.Push(nested);
                    }
                }
                catch (Exception exception)
                {
                    Finish(exception, cancelled: false);
                }
            }

            private void Finish(Exception failure, bool cancelled)
            {
                if (IsCompleted)
                {
                    return;
                }
                IsCompleted = true;
                while (0 < _stack.Count)
                {
                    System.Collections.IEnumerator enumerator = _stack.Pop();
                    try
                    {
                        if (enumerator is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                    catch (Exception exception)
                    {
                        if (failure == null)
                        {
                            failure = exception;
                        }
                        else
                        {
                            Debug.LogException(exception);
                        }
                    }
                }
                try
                {
                    if (failure != null)
                    {
                        _onFaulted?.Invoke(failure);
                    }
                    else if (cancelled)
                    {
                        _onCancelled?.Invoke();
                    }
                    else
                    {
                        _onCompleted?.Invoke();
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
#endif
}
