// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;

    internal class WButtonDisposableEnumerator : IEnumerator, IDisposable
    {
        public object Current
        {
            get
            {
                if (FailCurrent)
                {
                    throw new InvalidOperationException("Current failed.");
                }
                OnCurrent?.Invoke();
                return Yielded;
            }
        }

        internal object Yielded { get; set; }
        internal Action OnMoveNext { get; set; }
        internal Action OnCurrent { get; set; }
        internal Action OnDispose { get; set; }
        internal bool FailMoveNext { get; set; }
        internal bool FailCurrent { get; set; }
        internal bool FailDispose { get; set; }
        internal int DisposeCount { get; private set; }
        internal int MoveNextCount { get; private set; }

        private readonly string _name;
        private readonly List<string> _disposed;
        private readonly int _yieldCount;

        internal WButtonDisposableEnumerator(string name, List<string> disposed, int yieldCount = 1)
        {
            _name = name;
            _disposed = disposed;
            _yieldCount = yieldCount;
        }

        public void Dispose()
        {
            ++DisposeCount;
            _disposed.Add(_name);
            OnDispose?.Invoke();
            if (FailDispose)
            {
                throw new InvalidOperationException("Dispose failed.");
            }
        }

        public bool MoveNext()
        {
            ++MoveNextCount;
            OnMoveNext?.Invoke();
            if (FailMoveNext)
            {
                throw new InvalidOperationException("MoveNext failed.");
            }
            return MoveNextCount <= _yieldCount;
        }

        public void Reset()
        {
            MoveNextCount = 0;
        }
    }
#endif
}
