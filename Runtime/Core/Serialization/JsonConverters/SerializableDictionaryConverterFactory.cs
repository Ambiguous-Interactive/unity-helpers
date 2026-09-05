// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// JSON converter factory for SerializableDictionary types.
    /// Ensures serialization produces an object with "_keys" and "_values" fields rather than a JSON dictionary,
    /// which is necessary for proper order preservation across serialization cycles.
    /// </summary>
    public sealed class SerializableDictionaryConverterFactory : JsonConverterFactory
    {
        public static readonly SerializableDictionaryConverterFactory Instance = new();

        private SerializableDictionaryConverterFactory() { }

        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType)
            {
                return false;
            }

            Type genericDef = typeToConvert.GetGenericTypeDefinition();
            return genericDef == typeof(SerializableDictionary<,>)
                || genericDef == typeof(SerializableDictionary<,,>);
        }

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            // Prefer generated converters because IL2CPP cannot instantiate unseen generic closures.
            if (WJsonConverterRegistry.TryGet(typeToConvert, out JsonConverter generated))
            {
                return generated;
            }

            Type genericDef = typeToConvert.GetGenericTypeDefinition();
            Type[] typeArgs = typeToConvert.GetGenericArguments();

            Type converterType;
            if (genericDef == typeof(SerializableDictionary<,>))
            {
                converterType = typeof(SerializableDictionaryConverter<,>).MakeGenericType(
                    typeArgs
                );
            }
            else
            {
                converterType =
                    typeof(SerializableDictionaryWithCacheConverter<,,>).MakeGenericType(typeArgs);
            }

            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        public sealed class SerializableDictionaryConverter<TKey, TValue>
            : JsonConverter<SerializableDictionary<TKey, TValue>>
        {
            private const string KeysPropertyName =
                SerializableDictionarySerializedPropertyNames.Keys;
            private const string ValuesPropertyName =
                SerializableDictionarySerializedPropertyNames.Values;

            public override SerializableDictionary<TKey, TValue> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException(
                        $"Expected StartObject for SerializableDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}>, got {reader.TokenType}"
                    );
                }

                TKey[] keysArray = null;
                TValue[] valuesArray = null;

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

                    if (reader.ValueTextEquals(KeysPropertyName))
                    {
                        reader.Read();
                        keysArray = WJsonArray.ReadArray<TKey>(
                            ref reader,
                            options,
                            "SerializableDictionary<TKey, TValue>"
                        );
                    }
                    else if (reader.ValueTextEquals(ValuesPropertyName))
                    {
                        reader.Read();
                        valuesArray = WJsonArray.ReadArray<TValue>(
                            ref reader,
                            options,
                            "SerializableDictionary<TKey, TValue>"
                        );
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                SerializableDictionary<TKey, TValue> result = new();
                if (keysArray != null && valuesArray != null)
                {
                    SetSerializedArrays(result, keysArray, valuesArray);
                    result.OnAfterDeserialize();
                }

                return result;
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableDictionary<TKey, TValue> value,
                JsonSerializerOptions options
            )
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                value.OnBeforeSerialize();

                writer.WriteStartObject();
                writer.WritePropertyName(KeysPropertyName);
                WJsonArray.Write(writer, value.SerializedKeys, options);
                writer.WritePropertyName(ValuesPropertyName);
                WJsonArray.Write(writer, value.SerializedValues, options);
                writer.WriteEndObject();
            }

            private static void SetSerializedArrays(
                SerializableDictionary<TKey, TValue> dict,
                TKey[] keys,
                TValue[] values
            )
            {
                dict._keys = keys;
                dict._values = values;
            }
        }

        public sealed class SerializableDictionaryWithCacheConverter<TKey, TValue, TValueCache>
            : JsonConverter<SerializableDictionary<TKey, TValue, TValueCache>>
            where TValueCache : SerializableDictionary.Cache<TValue>, new()
        {
            private const string KeysPropertyName =
                SerializableDictionarySerializedPropertyNames.Keys;
            private const string ValuesPropertyName =
                SerializableDictionarySerializedPropertyNames.Values;

            public override SerializableDictionary<TKey, TValue, TValueCache> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException(
                        $"Expected StartObject for SerializableDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}, {typeof(TValueCache).Name}>, got {reader.TokenType}"
                    );
                }

                TKey[] keysArray = null;
                TValueCache[] valuesArray = null;

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

                    if (reader.ValueTextEquals(KeysPropertyName))
                    {
                        reader.Read();
                        keysArray = WJsonArray.ReadArray<TKey>(
                            ref reader,
                            options,
                            "SerializableDictionary<TKey, TValue, TValueCache>"
                        );
                    }
                    else if (reader.ValueTextEquals(ValuesPropertyName))
                    {
                        reader.Read();
                        valuesArray = WJsonArray.ReadArray<TValueCache>(
                            ref reader,
                            options,
                            "SerializableDictionary<TKey, TValue, TValueCache>"
                        );
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                SerializableDictionary<TKey, TValue, TValueCache> result = new();
                if (keysArray != null && valuesArray != null)
                {
                    SetSerializedArrays(result, keysArray, valuesArray);
                    result.OnAfterDeserialize();
                }

                return result;
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableDictionary<TKey, TValue, TValueCache> value,
                JsonSerializerOptions options
            )
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                value.OnBeforeSerialize();

                writer.WriteStartObject();
                writer.WritePropertyName(KeysPropertyName);
                WJsonArray.Write(writer, value.SerializedKeys, options);
                writer.WritePropertyName(ValuesPropertyName);
                WJsonArray.Write(writer, value.SerializedValues, options);
                writer.WriteEndObject();
            }

            private static void SetSerializedArrays(
                SerializableDictionary<TKey, TValue, TValueCache> dict,
                TKey[] keys,
                TValueCache[] values
            )
            {
                dict._keys = keys;
                dict._values = values;
            }
        }
    }
}
