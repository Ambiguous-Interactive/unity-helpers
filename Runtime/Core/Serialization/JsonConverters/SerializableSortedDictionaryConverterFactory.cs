// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// JSON converter factory for SerializableSortedDictionary types.
    /// Ensures serialization produces an object with "_keys" and "_values" fields rather than a JSON dictionary,
    /// which is necessary for proper order preservation across serialization cycles.
    /// </summary>
    public sealed class SerializableSortedDictionaryConverterFactory : JsonConverterFactory
    {
        public static readonly SerializableSortedDictionaryConverterFactory Instance = new();

        private SerializableSortedDictionaryConverterFactory() { }

        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType)
            {
                return false;
            }

            Type genericDef = typeToConvert.GetGenericTypeDefinition();
            return genericDef == typeof(SerializableSortedDictionary<,>)
                || genericDef == typeof(SerializableSortedDictionary<,,>);
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

            Type genericDef = typeToConvert.GetGenericTypeDefinition();
            Type[] typeArgs = typeToConvert.GetGenericArguments();

            Type converterType;
            if (genericDef == typeof(SerializableSortedDictionary<,>))
            {
                // SerializableSortedDictionary<TKey, TValue> where TValueCache = TValue
                converterType = typeof(SerializableSortedDictionaryConverter<,>).MakeGenericType(
                    typeArgs
                );
            }
            else
            {
                // SerializableSortedDictionary<TKey, TValue, TValueCache>
                converterType =
                    typeof(SerializableSortedDictionaryWithCacheConverter<,,>).MakeGenericType(
                        typeArgs
                    );
            }

            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        public sealed class SerializableSortedDictionaryConverter<TKey, TValue>
            : JsonConverter<SerializableSortedDictionary<TKey, TValue>>
            where TKey : IComparable<TKey>
        {
            private const string KeysPropertyName =
                SerializableDictionarySerializedPropertyNames.Keys;
            private const string ValuesPropertyName =
                SerializableDictionarySerializedPropertyNames.Values;

            public override SerializableSortedDictionary<TKey, TValue> Read(
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
                        $"Expected StartObject for SerializableSortedDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}>, got {reader.TokenType}"
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

                    string propertyName = reader.GetString();
                    reader.Read();

                    if (
                        string.Equals(
                            propertyName,
                            KeysPropertyName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        keysArray = WJsonArray.ReadArray<TKey>(
                            ref reader,
                            options,
                            "SerializableSortedDictionary<TKey, TValue>"
                        );
                    }
                    else if (
                        string.Equals(
                            propertyName,
                            ValuesPropertyName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        valuesArray = WJsonArray.ReadArray<TValue>(
                            ref reader,
                            options,
                            "SerializableSortedDictionary<TKey, TValue>"
                        );
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                SerializableSortedDictionary<TKey, TValue> result = new();
                if (keysArray != null && valuesArray != null)
                {
                    SetSerializedArrays(result, keysArray, valuesArray);
                    result.OnAfterDeserialize();
                }

                return result;
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableSortedDictionary<TKey, TValue> value,
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
                writer.WritePropertyName(KeysPropertyName);
                WJsonArray.Write(writer, value.SerializedKeys, options);
                writer.WritePropertyName(ValuesPropertyName);
                WJsonArray.Write(writer, value.SerializedValues, options);
                writer.WriteEndObject();
            }

            private static void SetSerializedArrays(
                SerializableSortedDictionary<TKey, TValue> dict,
                TKey[] keys,
                TValue[] values
            )
            {
                // Use reflection to set the internal _keys and _values fields
                Type baseType = typeof(SerializableSortedDictionaryBase<TKey, TValue, TValue>);

                FieldInfo keysField = baseType.GetField(
                    KeysPropertyName,
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                FieldInfo valuesField = baseType.GetField(
                    ValuesPropertyName,
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                keysField?.SetValue(dict, keys);
                valuesField?.SetValue(dict, values);
            }
        }

        public sealed class SerializableSortedDictionaryWithCacheConverter<
            TKey,
            TValue,
            TValueCache
        > : JsonConverter<SerializableSortedDictionary<TKey, TValue, TValueCache>>
            where TKey : IComparable<TKey>
            where TValueCache : SerializableDictionary.Cache<TValue>, new()
        {
            private const string KeysPropertyName =
                SerializableDictionarySerializedPropertyNames.Keys;
            private const string ValuesPropertyName =
                SerializableDictionarySerializedPropertyNames.Values;

            public override SerializableSortedDictionary<TKey, TValue, TValueCache> Read(
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
                        $"Expected StartObject for SerializableSortedDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}, {typeof(TValueCache).Name}>, got {reader.TokenType}"
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

                    string propertyName = reader.GetString();
                    reader.Read();

                    if (
                        string.Equals(
                            propertyName,
                            KeysPropertyName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        keysArray = WJsonArray.ReadArray<TKey>(
                            ref reader,
                            options,
                            "SerializableSortedDictionary<TKey, TValue, TValueCache>"
                        );
                    }
                    else if (
                        string.Equals(
                            propertyName,
                            ValuesPropertyName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        valuesArray = WJsonArray.ReadArray<TValueCache>(
                            ref reader,
                            options,
                            "SerializableSortedDictionary<TKey, TValue, TValueCache>"
                        );
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                SerializableSortedDictionary<TKey, TValue, TValueCache> result = new();
                if (keysArray != null && valuesArray != null)
                {
                    SetSerializedArrays(result, keysArray, valuesArray);
                    result.OnAfterDeserialize();
                }

                return result;
            }

            public override void Write(
                Utf8JsonWriter writer,
                SerializableSortedDictionary<TKey, TValue, TValueCache> value,
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
                writer.WritePropertyName(KeysPropertyName);
                WJsonArray.Write(writer, value.SerializedKeys, options);
                writer.WritePropertyName(ValuesPropertyName);
                WJsonArray.Write(writer, value.SerializedValues, options);
                writer.WriteEndObject();
            }

            private static void SetSerializedArrays(
                SerializableSortedDictionary<TKey, TValue, TValueCache> dict,
                TKey[] keys,
                TValueCache[] values
            )
            {
                // Use reflection to set the internal _keys and _values fields
                Type baseType = typeof(SerializableSortedDictionaryBase<TKey, TValue, TValueCache>);

                FieldInfo keysField = baseType.GetField(
                    KeysPropertyName,
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                FieldInfo valuesField = baseType.GetField(
                    ValuesPropertyName,
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                keysField?.SetValue(dict, keys);
                valuesField?.SetValue(dict, values);
            }
        }
    }
}
