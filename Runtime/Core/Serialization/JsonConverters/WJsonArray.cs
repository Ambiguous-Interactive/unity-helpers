// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Reads and writes a JSON array one element at a time, without asking System.Text.Json for a
    /// converter for the collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every collection converter this package ships used to delegate to
    /// <c>JsonSerializer.Deserialize&lt;List&lt;T&gt;&gt;</c> or <c>Deserialize&lt;T[]&gt;</c>. That
    /// is one line and it is fine in the editor, but System.Text.Json resolves a collection's
    /// converter through <b>its own</b> <c>JsonConverterFactory</c>, which builds
    /// <c>ListOfTConverter&lt;List&lt;T&gt;, T&gt;</c> with <c>Activator.CreateInstance</c> -- so a
    /// player throws
    /// </para>
    /// <code>
    /// ExecutionEngineException : Attempting to call method
    /// 'ListOfTConverter`2[[List`1[[System.SByte]]],[System.SByte]]::.ctor'
    /// for which no ahead of time (AOT) code was generated.
    /// </code>
    /// <para>
    /// Measured on a 2021.3 IL2CPP standalone player, from inside a converter this package had
    /// already registered successfully: giving <c>Deque&lt;T&gt;</c> its own converter was not
    /// enough while that converter's first act was to ask for a <c>List&lt;T&gt;</c> one.
    /// </para>
    /// <para>
    /// Reading elements individually asks only for <c>T</c>'s converter, which is a built-in for
    /// every primitive and enum, and a registered instance for anything this package or a consumer
    /// declares. Nothing here sizes an allocation from a number the payload states: the list grows
    /// from the elements actually delivered.
    /// </para>
    /// </remarks>
    internal static class WJsonArray
    {
        /// <summary>
        /// Reads a JSON array into a list, or returns <c>null</c> for a JSON null.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="reader">Positioned on the array's first token.</param>
        /// <param name="options">The options to read elements with.</param>
        /// <param name="owner">The type being read, named in any exception.</param>
        /// <returns>The elements, or <c>null</c>.</returns>
        internal static List<T> ReadList<T>(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options,
            string owner
        )
        {
            if (!TryBeginArray(ref reader, owner))
            {
                return null;
            }

            List<T> items = new();
            ReadElements(ref reader, options, owner, items);
            return items;
        }

        private static bool TryBeginArray(ref Utf8JsonReader reader, string owner)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return false;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"{owner} expects a JSON array, got {reader.TokenType}");
            }

            return true;
        }

        private static void ReadElements<T>(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options,
            string owner,
            List<T> items
        )
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return;
                }

                items.Add(JsonSerializer.Deserialize<T>(ref reader, options));
            }

            throw new JsonException($"Incomplete JSON array for {owner}");
        }

        /// <summary>
        /// Reads a JSON array into an array, or returns <c>null</c> for a JSON null.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="reader">Positioned on the array's first token.</param>
        /// <param name="options">The options to read elements with.</param>
        /// <param name="owner">The type being read, named in any exception.</param>
        /// <returns>The elements, or <c>null</c>.</returns>
        /// <remarks>
        /// Accumulates into a pooled buffer rather than a <see cref="List{T}"/>, because the list
        /// path paid twice for what it returned: every doubling of the list's backing array left
        /// one behind, and <c>ToArray</c> copied out of the last one into a third. A forward-only
        /// reader offers no element count to size from -- the rule this package keeps is that a
        /// capacity must come from bytes delivered, never bytes claimed -- so the growth itself
        /// stays, but each step rents through the shared pool instead of allocating. Steady state,
        /// the only allocation left is the exact-sized result.
        /// </remarks>
        internal static T[] ReadArray<T>(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options,
            string owner
        )
        {
            if (!TryBeginArray(ref reader, owner))
            {
                return null;
            }

            JsonArrayAccumulator<T> accumulator = default;
            try
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return accumulator.Finish();
                    }

                    accumulator.Add(JsonSerializer.Deserialize<T>(ref reader, options));
                }
            }
            finally
            {
                accumulator.Dispose();
            }

            throw new JsonException($"Incomplete JSON array for {owner}");
        }

        /// <summary>
        /// Writes a sequence as a JSON array, one element at a time.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="writer">The writer.</param>
        /// <param name="values">The elements, or <c>null</c>.</param>
        /// <param name="options">The options to write elements with.</param>
        internal static void Write<T>(
            Utf8JsonWriter writer,
            IReadOnlyList<T> values,
            JsonSerializerOptions options
        )
        {
            if (values == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            for (int index = 0; index < values.Count; index++)
            {
                JsonSerializer.Serialize(writer, values[index], options);
            }

            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Collects elements of unknown count into a pooled buffer and produces an exact-sized array.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <remarks>
    /// <para>
    /// A struct, used only as a local by the converter read paths this package owns, so collecting
    /// a sequence costs no object of its own -- which is what makes copying from or releasing the
    /// pooled buffer safe: no other scope observes the accumulator through a reference.
    /// </para>
    /// <para>
    /// Every growth step rents rather than allocates, so a caller reading similar-shaped payloads
    /// repeatedly stops paying for growth once the first few have warmed the shared pool's buckets;
    /// the only allocation left in steady state is the exact-sized result itself.
    /// </para>
    /// </remarks>
    internal struct JsonArrayAccumulator<T>
    {
        private const int InitialCapacity = 4;
        private const int MaximumArrayLength = 0x3FFF_FFFF;

        private PooledArray<T> _lease;
        private T[] _items;
        private int _count;

        /// <summary>How many elements have been collected.</summary>
        public int Count => _count;

        /// <summary>Appends one element.</summary>
        /// <param name="item">The element.</param>
        public void Add(T item)
        {
            if (_items == null)
            {
                Start();
            }
            else if (_count == _items.Length)
            {
                Grow();
            }

            _items[_count] = item;
            _count++;
        }

        /// <summary>
        /// Produces the exact-sized result and returns the scratch storage to its pool.
        /// </summary>
        /// <returns>The elements collected, in order; an empty array when none were.</returns>
        /// <remarks>
        /// The copy happens BEFORE the pooled buffer goes back: a reference-typed buffer may be
        /// cleared on return, and copying from a returned buffer would read whatever the pool did
        /// to it -- measured in CI as keys deserializing to nulls.
        /// </remarks>
        public T[] Finish()
        {
            int count = _count;
            if (count == 0)
            {
                Dispose();
                return Array.Empty<T>();
            }

            T[] items = _items;
            T[] exact = new T[count];
            Array.Copy(items, exact, count);
            Dispose();
            return exact;
        }

        /// <summary>Returns scratch storage to its pool without producing anything.</summary>
        /// <remarks>
        /// The cleanup half of <see cref="Finish"/>, for error paths that abandon collection: both
        /// are safe to call on a fresh accumulator, and each is safe to call twice, because the
        /// disposal lease claims before it returns.
        /// </remarks>
        public void Dispose()
        {
            /*
                A fresh accumulator holds a default lease whose TryClaim refuses, so disposing one
                that never started costs nothing -- and no ?. appears because PooledArray is a struct.
            */
            PooledArray<T> lease = _lease;
            this = default;
            lease.Dispose();
        }

        private void Start()
        {
            SystemArrayPool<T>.Get(InitialCapacity, out T[] rented);
            _lease = new PooledArray<T>(rented, InitialCapacity);
            _items = rented;
        }

        private void Grow()
        {
            /*
                The doubled length is computed from bytes actually delivered, but an int overflow
                would silently rent the wrong bucket, so it is refused instead.
            */
            if (MaximumArrayLength < _items.Length)
            {
                throw new JsonException("JSON array exceeds the maximum supported length.");
            }

            int nextCapacity = _items.Length * 2;
            SystemArrayPool<T>.Get(nextCapacity, out T[] replacement);
            Array.Copy(_items, 0, replacement, 0, _count);
            _lease.Dispose();
            _lease = new PooledArray<T>(replacement, nextCapacity);
            _items = replacement;
        }
    }
}
