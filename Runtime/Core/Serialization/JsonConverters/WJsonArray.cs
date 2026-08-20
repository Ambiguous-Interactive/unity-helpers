// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
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
        internal const int MaximumRetainedArrayCapacity = 4_096;

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

            PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> items);
            try
            {
                ReadElements(ref reader, options, owner, items);
                return items.ToArray();
            }
            finally
            {
                if (MaximumRetainedArrayCapacity < items.Capacity)
                {
                    items.Clear();
                    items.Capacity = 0;
                }

                lease.Dispose();
            }
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
}
