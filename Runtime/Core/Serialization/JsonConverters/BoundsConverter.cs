// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;

    public sealed class BoundsConverter : JsonConverter<Bounds>
    {
        public static readonly BoundsConverter Instance = new();

        private static readonly JsonEncodedText CenterProp = JsonEncodedText.Encode("center");
        private static readonly JsonEncodedText SizeProp = JsonEncodedText.Encode("size");

        private BoundsConverter() { }

        private static Vector3 ReadVector3Strict(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Vector3 must be an object");
            }

            bool haveX = false;
            bool haveY = false;
            bool haveZ = false;
            float x = 0;
            float y = 0;
            float z = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!haveX || !haveY || !haveZ)
                    {
                        throw new JsonException("Incomplete JSON for Vector3");
                    }
                    return new Vector3(x, y, z);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("x"))
                    {
                        reader.Read();
                        x = reader.GetSingle();
                        haveX = true;
                    }
                    else if (reader.ValueTextEquals("y"))
                    {
                        reader.Read();
                        y = reader.GetSingle();
                        haveY = true;
                    }
                    else if (reader.ValueTextEquals("z"))
                    {
                        reader.Read();
                        z = reader.GetSingle();
                        haveZ = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Vector3");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for Vector3");
        }

        public override Bounds Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            Vector3 center = default;
            Vector3 size = default;
            bool haveCenter = false;
            bool haveSize = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new Bounds(haveCenter ? center : default, haveSize ? size : default);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("center"))
                    {
                        reader.Read();
                        center = ReadVector3Strict(ref reader);
                        haveCenter = true;
                    }
                    else if (reader.ValueTextEquals("size"))
                    {
                        reader.Read();
                        size = ReadVector3Strict(ref reader);
                        haveSize = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Bounds");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for Bounds");
        }

        public override void Write(
            Utf8JsonWriter writer,
            Bounds value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WritePropertyName(CenterProp);
            JsonSerializer.Serialize(writer, value.center, options);
            writer.WritePropertyName(SizeProp);
            JsonSerializer.Serialize(writer, value.size, options);
            writer.WriteEndObject();
        }
    }
}
