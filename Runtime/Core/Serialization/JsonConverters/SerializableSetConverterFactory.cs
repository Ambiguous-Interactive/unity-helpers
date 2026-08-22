// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// JSON converter factory for SerializableHashSet and SerializableSortedSet types.
    /// Ensures serialization produces an object with "_items" field rather than a JSON array,
    /// which is necessary for proper order preservation across serialization cycles.
    /// </summary>
    public sealed class SerializableSetConverterFactory : JsonConverterFactory
    {
        public static readonly SerializableSetConverterFactory Instance = new();

        private SerializableSetConverterFactory() { }

        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType)
            {
                return false;
            }

            Type genericDef = typeToConvert.GetGenericTypeDefinition();
            return genericDef == typeof(SerializableHashSet<>)
                || genericDef == typeof(SerializableSortedSet<>);
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
            Type genericDef = typeToConvert.GetGenericTypeDefinition();

            Type converterType;
            if (genericDef == typeof(SerializableHashSet<>))
            {
                converterType = typeof(SerializableHashSetConverter<>).MakeGenericType(elementType);
            }
            else
            {
                converterType = typeof(SerializableSortedSetConverter<>).MakeGenericType(
                    elementType
                );
            }

            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        public sealed class SerializableHashSetConverter<T> : JsonConverter<SerializableHashSet<T>>
        {
            private const string ItemsPropertyName =
                SerializableHashSetSerializedPropertyNames.Items;

            public override SerializableHashSet<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                // Handle array format (legacy/fallback)
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    T[] items = WJsonArray.ReadArray<T>(
                        ref reader,
                        options,
                        "SerializableHashSet<T>"
                    );
                    SerializableHashSet<T> set = new();
                    if (items != null)
                    {
                        SetItemsField(set, items);
                        set.OnAfterDeserialize();
                    }
                    return set;
                }

                // Handle object format with _items property
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException(
                        $"Expected StartObject or StartArray for SerializableHashSet<{typeof(T).Name}>, got {reader.TokenType}"
                    );
                }

                T[] itemsArray = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    string propertyName = reader.GetString();
                    reader.Read();

                    if (
                        string.Equals(
                            propertyName,
                            ItemsPropertyName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        itemsArray = WJsonArray.ReadArray<T>(
                            ref reader,
                            options,
                            "SerializableHashSet<T>"
                        );
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                SerializableHashSet<T> result = new();
                if (itemsArray != null)
                {
                    SetItemsField(result, itemsArray);
                    result.OnAfterDeserialize();
                }

                return result;
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableHashSet<T> value,
                JsonSerializerOptions options
            )
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                // Ensure serialized arrays are up to date
                value.OnBeforeSerialize();

                writer.WriteStartObject();
                writer.WritePropertyName(ItemsPropertyName);
                WJsonArray.Write(writer, value.SerializedItems, options);
                writer.WriteEndObject();
            }

            private static void SetItemsField(SerializableHashSet<T> set, T[] items)
            {
                set._items = items;
            }
        }

        public sealed class SerializableSortedSetConverter<T>
            : JsonConverter<SerializableSortedSet<T>>
            where T : IComparable<T>
        {
            private const string ItemsPropertyName =
                SerializableHashSetSerializedPropertyNames.Items;

            public override SerializableSortedSet<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                // Handle array format (legacy/fallback)
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    T[] items = WJsonArray.ReadArray<T>(
                        ref reader,
                        options,
                        "SerializableSortedSet<T>"
                    );
                    SerializableSortedSet<T> set = new();
                    if (items != null)
                    {
                        SetItemsField(set, items);
                        set.OnAfterDeserialize();
                    }
                    return set;
                }

                // Handle object format with _items property
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException(
                        $"Expected StartObject or StartArray for SerializableSortedSet<{typeof(T).Name}>, got {reader.TokenType}"
                    );
                }

                T[] itemsArray = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    string propertyName = reader.GetString();
                    reader.Read();

                    if (
                        string.Equals(
                            propertyName,
                            ItemsPropertyName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        itemsArray = WJsonArray.ReadArray<T>(
                            ref reader,
                            options,
                            "SerializableSortedSet<T>"
                        );
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                SerializableSortedSet<T> result = new();
                if (itemsArray != null)
                {
                    SetItemsField(result, itemsArray);
                    result.OnAfterDeserialize();
                }

                return result;
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableSortedSet<T> value,
                JsonSerializerOptions options
            )
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                // Ensure serialized arrays are up to date
                value.OnBeforeSerialize();

                writer.WriteStartObject();
                writer.WritePropertyName(ItemsPropertyName);
                WJsonArray.Write(writer, value.SerializedItems, options);
                writer.WriteEndObject();
            }

            private static void SetItemsField(SerializableSortedSet<T> set, T[] items)
            {
                set._items = items;
            }
        }
    }
}
