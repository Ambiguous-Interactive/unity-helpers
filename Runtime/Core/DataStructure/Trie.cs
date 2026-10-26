// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// A highly optimized, array-backed trie implementation for fast prefix search and exact word lookup.
    /// Preallocates storage based on total characters in the input set and uses integer indices for traversal.
    /// Prefix search and enumeration reuse pooled scratch; returned strings allocate.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// Trie commands = new Trie(new[] { "spawn", "speed", "spectate" });
    /// foreach (string suggestion in commands.GetWordsWithPrefix("sp"))
    /// {
    ///     Debug.Log(suggestion);
    /// }
    /// ]]></code>
    /// </example>
    public sealed class Trie : IEnumerable<string>
    {
        private const int Poison = -1;

        private readonly char[] _chars;
        private readonly int[] _firstChild;
        private readonly int[] _nextSibling;
        private readonly bool[] _isWord;
        private readonly int _maxWordLength;
        private int _nodeCount;

        /// <summary>
        /// Constructs the Trie from the provided collection of words.
        /// </summary>
        /// <param name="words">All possible words to insert into the Trie.</param>
        /// <remarks>Input enumeration failures propagate after temporary pooled storage is returned.</remarks>
        public Trie(IEnumerable<string> words)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            IReadOnlyList<string> wordList = words as IReadOnlyList<string>;
            PooledResource<List<string>> pooledListResource = default;
            bool usedPooledList = false;
            try
            {
                if (wordList == null)
                {
                    pooledListResource = Buffers<string>.List.Get(out List<string> pooledList);
                    usedPooledList = true;
                    pooledList.AddRange(words);
                    wordList = pooledList;
                }

                int maxWordLength;
                if (0 < wordList.Count)
                {
                    maxWordLength = wordList[0].Length;
                    for (int i = 1; i < wordList.Count; ++i)
                    {
                        maxWordLength = Mathf.Max(maxWordLength, wordList[i].Length);
                    }
                }
                else
                {
                    maxWordLength = 0;
                }

                int capacity = 1;
                for (int i = 0; i < wordList.Count; ++i)
                {
                    capacity += wordList[i].Length;
                }

                _chars = new char[capacity];
                _firstChild = new int[capacity];
                _nextSibling = new int[capacity];
                _isWord = new bool[capacity];

                Array.Fill(_firstChild, Poison);
                Array.Fill(_nextSibling, Poison);

                _maxWordLength = maxWordLength;

                _nodeCount = 1;
                for (int i = 0; i < wordList.Count; ++i)
                {
                    string word = wordList[i];
                    Insert(word);
                }
            }
            finally
            {
                if (usedPooledList)
                {
                    pooledListResource.Dispose();
                }
            }
        }

        /// <summary>
        /// Determines whether the exact word exists in the Trie.
        /// </summary>
        public bool Contains(string word)
        {
            int node = 0;
            foreach (char c in word)
            {
                int child = _firstChild[node];
                while (child != Poison && _chars[child] != c)
                {
                    child = _nextSibling[child];
                }

                if (child == Poison)
                {
                    return false;
                }

                node = child;
            }
            return _isWord[node];
        }

        /// <summary>
        /// Collects up to maxResults words that start with the given prefix.
        /// Results are added into the provided list (which is cleared at the start).
        /// Returns the supplied list; traversal does not recurse with key depth.
        /// </summary>
        public List<string> GetWordsWithPrefix(
            string prefix,
            List<string> results,
            int maxResults = int.MaxValue
        )
        {
            results.Clear();
            if (maxResults <= 0)
            {
                return results;
            }

            int node = 0;
            foreach (char c in prefix)
            {
                int child = _firstChild[node];
                while (child != Poison && _chars[child] != c)
                {
                    child = _nextSibling[child];
                }

                if (child == Poison)
                {
                    return results;
                }

                node = child;
            }
            using PooledResource<StringBuilder> builderResource = Buffers.GetStringBuilder(
                Mathf.Max(_maxWordLength, prefix?.Length ?? 0),
                out StringBuilder builder
            );
            builder.Clear();
            builder.Append(prefix);
            Collect(node, results, maxResults, builder);
            return results;
        }

        /// <summary>
        /// Returns a value-based enumerator that reuses pooled traversal scratch.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private void Insert(string word)
        {
            int node = 0;
            foreach (char c in word)
            {
                int prev = Poison;
                int child = _firstChild[node];
                while (child != Poison && _chars[child] != c)
                {
                    prev = child;
                    child = _nextSibling[child];
                }
                if (child == Poison)
                {
                    child = _nodeCount++;
                    _chars[child] = c;
                    _firstChild[child] = Poison;
                    _nextSibling[child] = Poison;
                    if (prev == Poison)
                    {
                        _firstChild[node] = child;
                    }
                    else
                    {
                        _nextSibling[prev] = child;
                    }
                }
                node = child;
            }
            _isWord[node] = true;
        }

        private void Collect(int node, List<string> results, int maxResults, StringBuilder builder)
        {
            using PooledResource<Stack<(int node, int length)>> lease = Buffers<(
                int,
                int
            )>.Stack.Get(out Stack<(int node, int length)> pending);
            int root = node;
            while (true)
            {
                if (_isWord[node])
                {
                    results.Add(builder.ToString());
                    if (maxResults <= results.Count)
                    {
                        return;
                    }
                }

                int child = _firstChild[node];
                if (child != Poison)
                {
                    pending.Push((node, builder.Length));
                    node = child;
                    builder.Append(_chars[node]);
                    continue;
                }

                while (node == root || _nextSibling[node] == Poison)
                {
                    if (!pending.TryPop(out (int node, int length) parent))
                    {
                        return;
                    }
                    node = parent.node;
                    builder.Length = parent.length;
                }
                node = _nextSibling[node];
                --builder.Length;
                builder.Append(_chars[node]);
            }
        }

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
        {
            return new EnumeratorObject(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new EnumeratorObject(this);
        }

        /// <summary>
        /// Iterates words using one pooled prefix builder and a pooled traversal stack.
        /// </summary>
        public struct Enumerator : IDisposable, global::System.IEquatable<Enumerator>
        {
            /// <summary>Gets the last word returned by a successful advance.</summary>
            public string Current => _current;

            private readonly Trie _trie;
            private readonly PooledResource<Stack<(int node, int length)>> _stackResource;
            private readonly Stack<(int node, int length)> _stack;
            private readonly PooledResource<StringBuilder> _builderResource;
            private readonly StringBuilder _builder;
            private string _current;

            internal Enumerator(Trie trie)
            {
                _trie = trie;
                _stackResource = Buffers<(int, int)>.Stack.Get(out _stack);
                try
                {
                    _stack.Push((0, 0));
                    _builderResource = Buffers.StringBuilder.Get(out _builder);
                    _current = null;
                }
                catch
                {
                    _stackResource.Dispose();
                    throw;
                }
            }

            /// <summary>Advances to the next word in reverse sibling insertion order.</summary>
            public bool MoveNext()
            {
                if (!_stackResource.IsHeld || !_builderResource.IsHeld)
                {
                    return false;
                }

                while (_stack.TryPop(out (int node, int length) item))
                {
                    int node = item.node;
                    _builder.Length = item.length;
                    if (node != 0)
                    {
                        _builder.Append(_trie._chars[node]);
                    }

                    for (
                        int child = _trie._firstChild[node];
                        child != Poison;
                        child = _trie._nextSibling[child]
                    )
                    {
                        _stack.Push((child, _builder.Length));
                    }

                    if (_trie._isWord[node])
                    {
                        _current = _builder.ToString();
                        return true;
                    }
                }

                return false;
            }

            /// <summary>Returns traversal scratch once, including when the enumerator is copied.</summary>
            public void Dispose()
            {
                try
                {
                    _builderResource.Dispose();
                }
                finally
                {
                    _stackResource.Dispose();
                }
            }

            /// <summary>Compares the stored fields with another value.</summary>
            public bool Equals(Enumerator other)
            {
                return EqualityComparer<Trie>.Default.Equals(_trie, other._trie)
                    && _stackResource.Equals(other._stackResource)
                    && EqualityComparer<Stack<(int node, int length)>>.Default.Equals(
                        _stack,
                        other._stack
                    )
                    && _builderResource.Equals(other._builderResource)
                    && EqualityComparer<StringBuilder>.Default.Equals(_builder, other._builder)
                    && EqualityComparer<string>.Default.Equals(_current, other._current);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is Enumerator other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                    _trie,
                    _stackResource,
                    _stack,
                    _builderResource,
                    _builder,
                    _current
                );
            }
        }

        /// <summary>
        /// Reference-based enumerator for IEnumerable interface compatibility.
        /// </summary>
        private sealed class EnumeratorObject : IEnumerator<string>
        {
            public string Current => _enumerator.Current;

            object IEnumerator.Current => Current;

            private Enumerator _enumerator;

            internal EnumeratorObject(Trie trie)
            {
                _enumerator = new Enumerator(trie);
            }

            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                _enumerator.Dispose();
            }
        }
    }

    /// <summary>
    /// A highly optimized, array-backed generic Trie for mapping string keys to values of type T.
    /// Preallocates storage based on total characters in the key set and uses integer indices for traversal,
    /// minimizing memory allocations and indirections. Provides allocation-free prefix search (aside from
    /// the output list allocations themselves).
    /// </summary>
    public sealed class Trie<T> : IEnumerable<T>
    {
        private const int Poison = -1;

        private readonly char[] _chars;
        private readonly int[] _firstChild;
        private readonly int[] _nextSibling;
        private readonly bool[] _hasValue;
        private readonly T[] _values;
        private int _nodeCount;

        /// <summary>
        /// Constructs the Trie from the provided dictionary of keys to values.
        /// </summary>
        /// <param name="items">Mapping from unique string keys to values of type T.</param>
        public Trie(IReadOnlyDictionary<string, T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            using PooledResource<List<KeyValuePair<string, T>>> pooledListResource = Buffers<
                KeyValuePair<string, T>
            >.List.Get(out List<KeyValuePair<string, T>> itemList);
            int capacity = 1;
            foreach (KeyValuePair<string, T> entry in items.AsValueDictionaryEnumerableRequired())
            {
                capacity += entry.Key.Length;
                itemList.Add(entry);
            }

            _chars = new char[capacity];
            _firstChild = new int[capacity];
            _nextSibling = new int[capacity];
            _hasValue = new bool[capacity];
            _values = new T[capacity];

            Array.Fill(_firstChild, Poison);
            Array.Fill(_nextSibling, Poison);

            _nodeCount = 1;
            foreach (KeyValuePair<string, T> kv in itemList)
            {
                Insert(kv.Key, kv.Value);
            }
        }

        /// <summary>
        /// Attempts to retrieve the value associated with the exact key.
        /// </summary>
        public bool TryGetValue(string key, out T value)
        {
            int node = 0;
            foreach (char c in key)
            {
                int child = _firstChild[node];
                while (child != Poison && _chars[child] != c)
                {
                    child = _nextSibling[child];
                }

                if (child == Poison)
                {
                    value = default;
                    return false;
                }
                node = child;
            }
            if (_hasValue[node])
            {
                value = _values[node];
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Collects up to maxResults values whose keys start with the given prefix.
        /// Results are added into the provided list (which is cleared at the start).
        /// Returns the supplied list; traversal does not recurse with key depth.
        /// </summary>
        public List<T> GetValuesWithPrefix(
            string prefix,
            List<T> results,
            int maxResults = int.MaxValue
        )
        {
            results.Clear();
            if (maxResults <= 0)
            {
                return results;
            }

            int node = 0;
            foreach (char c in prefix)
            {
                int child = _firstChild[node];
                while (child != Poison && _chars[child] != c)
                {
                    child = _nextSibling[child];
                }

                if (child == Poison)
                {
                    return results;
                }

                node = child;
            }
            Collect(node, results, maxResults);
            return results;
        }

        /// <summary>
        /// Returns a value-based enumerator that reuses pooled traversal scratch.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private void Insert(string key, T value)
        {
            int node = 0;
            foreach (char c in key)
            {
                int prev = Poison;
                int child = _firstChild[node];
                while (child != Poison && _chars[child] != c)
                {
                    prev = child;
                    child = _nextSibling[child];
                }
                if (child == Poison)
                {
                    child = _nodeCount++;
                    _chars[child] = c;
                    _firstChild[child] = Poison;
                    _nextSibling[child] = Poison;
                    if (prev == Poison)
                    {
                        _firstChild[node] = child;
                    }
                    else
                    {
                        _nextSibling[prev] = child;
                    }
                }
                node = child;
            }
            _hasValue[node] = true;
            _values[node] = value;
        }

        private void Collect(int node, List<T> results, int maxResults)
        {
            using PooledResource<Stack<int>> lease = Buffers<int>.Stack.Get(out Stack<int> pending);
            int root = node;
            while (true)
            {
                if (_hasValue[node])
                {
                    results.Add(_values[node]);
                    if (maxResults <= results.Count)
                    {
                        return;
                    }
                }

                int child = _firstChild[node];
                if (child != Poison)
                {
                    pending.Push(node);
                    node = child;
                    continue;
                }

                while (node == root || _nextSibling[node] == Poison)
                {
                    if (!pending.TryPop(out node))
                    {
                        return;
                    }
                }
                node = _nextSibling[node];
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new EnumeratorObject(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new EnumeratorObject(this);
        }

        /// <summary>
        /// Value-based enumerator for efficient foreach iteration without heap allocations.
        /// Implements IDisposable to return pooled Stack.
        /// </summary>
        public struct Enumerator : IDisposable, global::System.IEquatable<Enumerator>
        {
            public T Current => _current;

            private readonly Trie<T> _trie;
            private readonly PooledResource<Stack<int>> _stackResource;
            private readonly Stack<int> _stack;
            private T _current;

            internal Enumerator(Trie<T> trie)
            {
                _trie = trie;
                _stackResource = Buffers<int>.Stack.Get(out Stack<int> stack);
                _stack = stack;
                _current = default;

                if (1 <= _trie._nodeCount)
                {
                    _stack.Push(0);
                }
            }

            /// <summary>Advances to the next value while traversal scratch remains owned.</summary>
            public bool MoveNext()
            {
                if (!_stackResource.IsHeld)
                {
                    return false;
                }

                while (_stack.TryPop(out int node))
                {
                    if (_trie._hasValue[node])
                    {
                        _current = _trie._values[node];

                        PushChildren(node);
                        return true;
                    }

                    for (
                        int child = _trie._firstChild[node];
                        child != Poison;
                        child = _trie._nextSibling[child]
                    )
                    {
                        _stack.Push(child);
                    }
                }

                return false;
            }

            public void Dispose()
            {
                _stackResource.Dispose();
            }

            /// <summary>Compares the stored fields with another value.</summary>
            public bool Equals(Enumerator other)
            {
                return EqualityComparer<Trie<T>>.Default.Equals(_trie, other._trie)
                    && EqualityComparer<PooledResource<Stack<int>>>.Default.Equals(
                        _stackResource,
                        other._stackResource
                    )
                    && EqualityComparer<Stack<int>>.Default.Equals(_stack, other._stack)
                    && EqualityComparer<T>.Default.Equals(_current, other._current);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is Enumerator other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                    _trie,
                    _stackResource,
                    _stack,
                    _current
                );
            }

            private void PushChildren(int node)
            {
                for (
                    int child = _trie._firstChild[node];
                    child != Poison;
                    child = _trie._nextSibling[child]
                )
                {
                    _stack.Push(child);
                }
            }
        }

        /// <summary>
        /// Reference-based enumerator for IEnumerable interface compatibility.
        /// </summary>
        private sealed class EnumeratorObject : IEnumerator<T>
        {
            public T Current => _enumerator.Current;

            object IEnumerator.Current => Current;

            private Enumerator _enumerator;

            internal EnumeratorObject(Trie<T> trie)
            {
                _enumerator = new Enumerator(trie);
            }

            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                _enumerator.Dispose();
            }
        }
    }
}
