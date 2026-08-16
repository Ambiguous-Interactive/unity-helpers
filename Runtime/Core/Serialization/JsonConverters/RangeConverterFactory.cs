// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.Math;

    public sealed class RangeConverterFactory : JsonConverterFactory
    {
        public static readonly RangeConverterFactory Instance = new();

        private RangeConverterFactory() { }

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(Range<>);
        }

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            Type tArg = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(RangeConverter<>).MakeGenericType(tArg);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        private sealed class RangeConverter<T> : JsonConverter<Range<T>>
            where T : IEquatable<T>, IComparable<T>
        {
            public override Range<T> Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException("Range<T> must be an object");
                }

                T min = default;
                T max = default;
                bool startInclusive = true;
                bool endInclusive = true;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        // The constructor validates the ordering and throws ArgumentException. A
                        // payload that omits "max" reaches it just as readily as a hostile one,
                        // because the omitted member defaults to zero.
                        if (0 < min.CompareTo(max))
                        {
                            throw new JsonException(
                                $"Range<{typeof(T).Name}> requires min <= max, but read "
                                    + $"min {min} and max {max}."
                            );
                        }

                        return new Range<T>(min, max, startInclusive, endInclusive);
                    }
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Expected property name for Range<T>");
                    }

                    if (reader.ValueTextEquals("min"))
                    {
                        reader.Read();
                        min = JsonSerializer.Deserialize<T>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("max"))
                    {
                        reader.Read();
                        max = JsonSerializer.Deserialize<T>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("startInclusive"))
                    {
                        reader.Read();
                        startInclusive = reader.GetBoolean();
                    }
                    else if (reader.ValueTextEquals("endInclusive"))
                    {
                        reader.Read();
                        endInclusive = reader.GetBoolean();
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Range<T>");
                    }
                }

                throw new JsonException("Incomplete JSON for Range<T>");
            }

            public override void Write(
                Utf8JsonWriter writer,
                Range<T> value,
                JsonSerializerOptions options
            )
            {
                writer.WriteStartObject();
                writer.WritePropertyName("min");
                JsonSerializer.Serialize(writer, value.min, options);
                writer.WritePropertyName("max");
                JsonSerializer.Serialize(writer, value.max, options);
                writer.WriteBoolean("startInclusive", value.startInclusive);
                writer.WriteBoolean("endInclusive", value.endInclusive);
                writer.WriteEndObject();
            }
        }
    }
}
