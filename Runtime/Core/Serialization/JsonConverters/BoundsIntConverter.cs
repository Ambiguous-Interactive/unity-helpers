// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;

    public sealed class BoundsIntConverter : JsonConverter<BoundsInt>
    {
        public static readonly BoundsIntConverter Instance = new();

        private static readonly JsonEncodedText PositionProp = JsonEncodedText.Encode("position");
        private static readonly JsonEncodedText SizeProp = JsonEncodedText.Encode("size");

        private BoundsIntConverter() { }

        private static Vector3Int ReadVector3IntStrict(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Vector3Int must be an object");
            }

            bool haveX = false;
            bool haveY = false;
            bool haveZ = false;
            int x = 0;
            int y = 0;
            int z = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!haveX || !haveY || !haveZ)
                    {
                        throw new JsonException("Incomplete JSON for Vector3Int");
                    }
                    return new Vector3Int(x, y, z);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("x"))
                    {
                        reader.Read();
                        x = reader.GetInt32();
                        haveX = true;
                    }
                    else if (reader.ValueTextEquals("y"))
                    {
                        reader.Read();
                        y = reader.GetInt32();
                        haveY = true;
                    }
                    else if (reader.ValueTextEquals("z"))
                    {
                        reader.Read();
                        z = reader.GetInt32();
                        haveZ = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Vector3Int");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for Vector3Int");
        }

        public override BoundsInt Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            Vector3Int position = default;
            Vector3Int size = default;
            bool havePosition = false;
            bool haveSize = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new BoundsInt(
                        havePosition ? position : default,
                        haveSize ? size : default
                    );
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("position"))
                    {
                        reader.Read();
                        position = ReadVector3IntStrict(ref reader);
                        havePosition = true;
                    }
                    else if (reader.ValueTextEquals("size"))
                    {
                        reader.Read();
                        size = ReadVector3IntStrict(ref reader);
                        haveSize = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for BoundsInt");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for BoundsInt");
        }

        public override void Write(
            Utf8JsonWriter writer,
            BoundsInt value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WritePropertyName(PositionProp);
            JsonSerializer.Serialize(writer, value.position, options);
            writer.WritePropertyName(SizeProp);
            JsonSerializer.Serialize(writer, value.size, options);
            writer.WriteEndObject();
        }
    }
}
