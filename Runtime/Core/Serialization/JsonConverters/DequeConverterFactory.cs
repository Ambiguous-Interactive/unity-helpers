// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public sealed class DequeConverterFactory : JsonConverterFactory
    {
        public static readonly DequeConverterFactory Instance = new();

        private DequeConverterFactory() { }

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(Deque<>);
        }

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
            Type convType = typeof(DequeConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(convType);
        }

        public sealed class DequeConverter<T> : JsonConverter<Deque<T>>
        {
            public override Deque<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException("Deque<T> expects a JSON array");
                }
                List<T> items = JsonSerializer.Deserialize<List<T>>(ref reader, options);
                return items == null ? new Deque<T>(Deque<T>.DefaultCapacity) : new Deque<T>(items);
            }

            public override void Write(
                Utf8JsonWriter writer,
                Deque<T> value,
                JsonSerializerOptions options
            )
            {
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
