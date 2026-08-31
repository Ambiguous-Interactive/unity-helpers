// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    internal sealed class RestorableEditorGlobal<T>
    {
        private readonly Func<T> _read;
        private readonly Action<T> _write;
        private T[] _values = new T[4];
        private T[] _restoreValues = new T[4];
        private long[] _generations = new long[4];
        private int[] _previous = { -1, -1, -1, -1 };
        private int[] _next = { -1, -1, -1, -1 };
        private int[] _freeNext = { -1, -1, -1, -1 };
        private bool[] _active = new bool[4];
        private long _nextGeneration;
        private int _nextSlot;
        private int _freeHead = -1;
        private int _tail = -1;

        internal RestorableEditorGlobal(Func<T> read, Action<T> write)
        {
            _read = read;
            _write = write;
        }

        internal Scope Acquire(T value)
        {
            T restoreValue = _read();
            _write(value);

            int slot = TakeSlot();
            long generation = ++_nextGeneration;
            _values[slot] = value;
            _restoreValues[slot] = restoreValue;
            _generations[slot] = generation;
            _active[slot] = true;
            _previous[slot] = _tail;
            _next[slot] = -1;
            if (0 <= _tail)
            {
                _next[_tail] = slot;
            }

            _tail = slot;
            return new Scope(this, slot, generation);
        }

        private int TakeSlot()
        {
            if (0 <= _freeHead)
            {
                int slot = _freeHead;
                _freeHead = _freeNext[slot];
                return slot;
            }

            int created = _nextSlot;
            _nextSlot++;
            EnsureCapacity(_nextSlot);
            return created;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _values.Length)
            {
                return;
            }

            int previousCapacity = _values.Length;
            int capacity = previousCapacity * 2;
            while (capacity < required)
            {
                capacity *= 2;
            }

            Array.Resize(ref _values, capacity);
            Array.Resize(ref _restoreValues, capacity);
            Array.Resize(ref _generations, capacity);
            Array.Resize(ref _previous, capacity);
            Array.Resize(ref _next, capacity);
            Array.Resize(ref _freeNext, capacity);
            Array.Resize(ref _active, capacity);
            for (int index = previousCapacity; index < capacity; index++)
            {
                _previous[index] = -1;
                _next[index] = -1;
                _freeNext[index] = -1;
            }
        }

        private void Release(int slot, long generation)
        {
            if (slot < 0 || _nextSlot <= slot || !_active[slot] || _generations[slot] != generation)
            {
                return;
            }

            int previous = _previous[slot];
            int next = _next[slot];
            if (0 <= previous)
            {
                _next[previous] = next;
            }
            if (0 <= next)
            {
                _previous[next] = previous;
                if (EqualityComparer<T>.Default.Equals(_restoreValues[next], _values[slot]))
                {
                    _restoreValues[next] = _restoreValues[slot];
                }
            }

            bool wasTail = _tail == slot;
            if (wasTail)
            {
                _tail = previous;
            }

            _active[slot] = false;
            _values[slot] = default;
            T restoreValue = _restoreValues[slot];
            _restoreValues[slot] = default;
            _previous[slot] = -1;
            _next[slot] = -1;
            _freeNext[slot] = _freeHead;
            _freeHead = slot;

            if (wasTail)
            {
                _write(restoreValue);
            }
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly RestorableEditorGlobal<T> _owner;
            private readonly int _slot;
            private readonly long _generation;

            internal Scope(RestorableEditorGlobal<T> owner, int slot, long generation)
            {
                _owner = owner;
                _slot = slot;
                _generation = generation;
            }

            public void Dispose()
            {
                _owner?.Release(_slot, _generation);
            }
        }
    }

    internal static class EditorGlobalScopes
    {
        internal static readonly RestorableEditorGlobal<int> IndentLevel =
            new RestorableEditorGlobal<int>(
                () => EditorGUI.indentLevel,
                value => EditorGUI.indentLevel = value
            );

        internal static readonly RestorableEditorGlobal<float> LabelWidth =
            new RestorableEditorGlobal<float>(
                () => EditorGUIUtility.labelWidth,
                value => EditorGUIUtility.labelWidth = value
            );
    }
#endif
}
