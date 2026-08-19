// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Serializes <see cref="SerializableList{T}"/> as a plain JSON array, so wrapping a collection
    /// for Unity does not change its JSON shape.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// JsonSerializerOptions options = new JsonSerializerOptions();
    /// options.Converters.Add(SerializableListConverterFactory.Instance);
    /// ]]></code>
    /// </example>
    public sealed class SerializableListConverterFactory : JsonConverterFactory
    {
        /// <summary>
        /// Shared factory instance.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// JsonConverterFactory factory = SerializableListConverterFactory.Instance;
        /// ]]></code>
        /// </example>
        public static readonly SerializableListConverterFactory Instance = new();

        /// <summary>
        /// Determines whether the supplied type is a <see cref="SerializableList{T}"/>.
        /// </summary>
        /// <param name="typeToConvert">Type the serializer is asking about.</param>
        /// <returns><c>true</c> when this factory can supply a converter.</returns>
        /// <example>
        /// <code><![CDATA[
        /// bool convertible = SerializableListConverterFactory.Instance.CanConvert(
        ///     typeof(SerializableList<int>));
        /// ]]></code>
        /// </example>
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert != null
                && typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(SerializableList<>);
        }

        /// <summary>
        /// Creates the converter for a closed <see cref="SerializableList{T}"/>.
        /// </summary>
        /// <param name="typeToConvert">Closed generic type to convert.</param>
        /// <param name="options">Serializer options in effect.</param>
        /// <returns>A converter for <paramref name="typeToConvert"/>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// JsonConverter converter = SerializableListConverterFactory.Instance.CreateConverter(
        ///     typeof(SerializableList<int>), new JsonSerializerOptions());
        /// ]]></code>
        /// </example>
        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            // Asked before the reflective path below, which is the whole AOT story: the generator
            // has already constructed this closure's converter where the closure was written, and
            // MakeGenericType is the one call IL2CPP cannot compile. The reflective path stays for
            // a closure no build named -- the editor, Mono, and anything constructed at run time.
            if (WJsonConverterRegistry.TryGet(typeToConvert, out JsonConverter generated))
            {
                return generated;
            }

            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(SerializableListConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        /// <summary>
        /// Creates a factory instance. Public because <see cref="SerializableList{T}"/> carries
        /// <see cref="JsonConverterAttribute"/>, and the attribute path constructs the converter
        /// itself rather than reading <see cref="Instance"/>.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// JsonConverterFactory factory = new SerializableListConverterFactory();
        /// ]]></code>
        /// </example>
        public SerializableListConverterFactory() { }

        public sealed class SerializableListConverter<T> : JsonConverter<SerializableList<T>>
        {
            public override SerializableList<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                List<T> items = JsonSerializer.Deserialize<List<T>>(ref reader, options);
                return new SerializableList<T>(items);
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableList<T> value,
                JsonSerializerOptions options
            )
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartArray();
                foreach (T item in value)
                {
                    JsonSerializer.Serialize(writer, item, options);
                }

                writer.WriteEndArray();
            }
        }
    }
}
