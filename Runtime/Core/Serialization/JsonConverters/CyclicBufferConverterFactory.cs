// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public sealed class CyclicBufferConverterFactory : JsonConverterFactory
    {
        public static readonly CyclicBufferConverterFactory Instance = new();

        private CyclicBufferConverterFactory() { }

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(CyclicBuffer<>);
        }

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            /*
                Asked before the reflective path below, which is the whole AOT story: the generator
                has already constructed this closure's converter where the closure was written, and
                MakeGenericType is the one call IL2CPP cannot compile. The reflective path stays for
                a closure no build named -- the editor, Mono, and anything constructed at run time.
            */
            if (WJsonConverterRegistry.TryGet(typeToConvert, out JsonConverter generated))
            {
                return generated;
            }

            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type convType = typeof(CyclicBufferConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(convType);
        }

        public sealed class CyclicBufferConverter<T> : JsonConverter<CyclicBuffer<T>>
        {
            private static readonly JsonEncodedText CapacityProp = JsonEncodedText.Encode(
                "capacity"
            );
            private static readonly JsonEncodedText ItemsProp = JsonEncodedText.Encode("items");

            public override CyclicBuffer<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException(
                        "CyclicBuffer<T> expects an object with capacity and items"
                    );
                }

                int capacity = 0;
                List<T> items = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        if (capacity < 0)
                        {
                            throw new JsonException("capacity must be non-negative");
                        }

                        /*
                            Refused rather than clamped downward: a cyclic buffer's capacity is
                            where it starts overwriting, so shrinking it silently would change
                            behavior rather than allocation. A capacity BELOW the items delivered
                            is raised to hold them, which is what the two binary paths have always
                            done -- the document is self-inconsistent and the items are the half
                            that exists.
                        */
                        if (
                            !SerializationCapacityLimits.TryAccept(
                                capacity,
                                items == null ? 0 : items.Count,
                                out int accepted
                            )
                        )
                        {
                            throw new JsonException(
                                SerializationCapacityLimits.Refusal(
                                    nameof(CyclicBuffer<T>),
                                    capacity
                                )
                            );
                        }
                        return new CyclicBuffer<T>(accepted, items);
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Expected property name");
                    }

                    if (reader.ValueTextEquals("capacity"))
                    {
                        reader.Read();
                        capacity = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals("items"))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("items must be an array");
                        }
                        items = WJsonArray.ReadList<T>(ref reader, options, "CyclicBuffer<T>");
                    }
                    else
                    {
                        throw new JsonException("Unknown property for CyclicBuffer");
                    }
                }

                throw new JsonException("Incomplete JSON for CyclicBuffer");
            }

            public override void Write(
                Utf8JsonWriter writer,
                CyclicBuffer<T> value,
                JsonSerializerOptions options
            )
            {
                writer.WriteStartObject();
                writer.WriteNumber(CapacityProp, value.Capacity);
                writer.WritePropertyName(ItemsProp);
                writer.WriteStartArray();
                foreach (T item in value)
                {
                    JsonSerializer.Serialize(writer, item, options);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
    }
}
