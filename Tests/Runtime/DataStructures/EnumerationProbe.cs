// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    internal sealed class EnumerationProbe : IReadOnlyList<int>, IEnumerator<int>
    {
        public int Count => throw new InvalidOperationException("Count must not be read.");
        public int Current
        {
            get
            {
                Events.Add(nameof(Current));
                if (Failure == 3)
                {
                    throw new InvalidOperationException(nameof(Current));
                }
                return Values[_index];
            }
        }
        public int this[int index] =>
            throw new InvalidOperationException("Indexer must not be read.");
        object IEnumerator.Current => Current;
        public readonly List<string> Events = new List<string>();
        public readonly int[] Values = { 1, 2, 3 };
        public int Failure;
        private int _index = -1;

        public IEnumerator<int> GetEnumerator()
        {
            Events.Add(nameof(GetEnumerator));
            if (Failure == 1)
            {
                throw new InvalidOperationException(nameof(GetEnumerator));
            }
            _index = -1;
            return this;
        }

        public bool MoveNext()
        {
            Events.Add(nameof(MoveNext));
            if (Failure == 2)
            {
                throw new InvalidOperationException(nameof(MoveNext));
            }
            ++_index;
            return _index < Values.Length;
        }

        public void Dispose()
        {
            Events.Add(nameof(Dispose));
            if (Failure == 4)
            {
                throw new InvalidOperationException(nameof(Dispose));
            }
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
