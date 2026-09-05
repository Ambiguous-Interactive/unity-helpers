// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public sealed class BitSetConverter : JsonConverter<BitSet>
    {
        public static readonly BitSetConverter Instance = new();

        private static readonly JsonEncodedText CapacityProp = JsonEncodedText.Encode("capacity");
        private static readonly JsonEncodedText SetIndicesProp = JsonEncodedText.Encode(
            "setIndices"
        );

        private BitSetConverter() { }

        public override BitSet Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token {reader.TokenType} for BitSet");
            }

            int capacity = 0;
            System.Collections.Generic.List<int> indices = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    // Widen index + 1 before validating capacity so int.MaxValue cannot wrap to an empty requirement.
                    long required = 0;
                    if (indices is { Count: > 0 })
                    {
                        foreach (int index in indices)
                        {
                            if (index < 0)
                            {
                                continue;
                            }

                            if (required < (long)index + 1)
                            {
                                required = (long)index + 1;
                            }
                        }
                    }

                    // Clamp capacity hints but reject unrepresentable indices; silently dropping an index changes the set.
                    if (
                        int.MaxValue < required
                        || !SerializationCapacityLimits.TryAccept((int)required, 0, out int _)
                    )
                    {
                        throw new JsonException(
                            SerializationCapacityLimits.Refusal(
                                nameof(BitSet),
                                int.MaxValue < required ? int.MaxValue : (int)required
                            )
                        );
                    }

                    int finalCapacity = SerializationCapacityLimits.Clamp(capacity, (int)required);

                    BitSet bitset = new(0 < finalCapacity ? finalCapacity : 64);
                    if (indices != null)
                    {
                        foreach (int index in indices)
                        {
                            bitset.TrySet(index);
                        }
                    }
                    return bitset;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected property name in BitSet JSON");
                }

                if (reader.ValueTextEquals("capacity"))
                {
                    reader.Read();
                    capacity = reader.GetInt32();
                }
                else if (reader.ValueTextEquals("setIndices"))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException("setIndices must be an array");
                    }
                    indices = new System.Collections.Generic.List<int>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                        {
                            break;
                        }
                        indices.Add(reader.GetInt32());
                    }
                }
                else
                {
                    throw new JsonException("Unknown property for BitSet");
                }
            }

            throw new JsonException("Incomplete JSON for BitSet");
        }

        public override void Write(
            Utf8JsonWriter writer,
            BitSet value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteNumber(CapacityProp, value.Capacity);
            writer.WritePropertyName(SetIndicesProp);
            writer.WriteStartArray();
            foreach (int idx in value.EnumerateSetIndices())
            {
                writer.WriteNumberValue(idx);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
