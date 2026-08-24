// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;

    public sealed class Color32Converter : JsonConverter<Color32>
    {
        public static readonly Color32Converter Instance = new();

        private static readonly JsonEncodedText RProp = JsonEncodedText.Encode("r");
        private static readonly JsonEncodedText GProp = JsonEncodedText.Encode("g");
        private static readonly JsonEncodedText BProp = JsonEncodedText.Encode("b");
        private static readonly JsonEncodedText AProp = JsonEncodedText.Encode("a");

        private Color32Converter() { }

        public override Color32 Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            byte r = 0;
            byte g = 0;
            byte b = 0;
            byte a = 255;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new Color32(r, g, b, a);
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("r"))
                    {
                        reader.Read();
                        r = ReadByte(ref reader);
                    }
                    else if (reader.ValueTextEquals("g"))
                    {
                        reader.Read();
                        g = ReadByte(ref reader);
                    }
                    else if (reader.ValueTextEquals("b"))
                    {
                        reader.Read();
                        b = ReadByte(ref reader);
                    }
                    else if (reader.ValueTextEquals("a"))
                    {
                        reader.Read();
                        a = ReadByte(ref reader);
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Color32");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for Color32");
        }

        private static byte ReadByte(ref Utf8JsonReader reader)
        {
            int v = reader.GetInt32();
            if (255u < (uint)v)
            {
                throw new JsonException("Color32 channel out of range");
            }
            return (byte)v;
        }

        public override void Write(
            Utf8JsonWriter writer,
            Color32 value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteNumber(RProp, value.r);
            writer.WriteNumber(GProp, value.g);
            writer.WriteNumber(BProp, value.b);
            writer.WriteNumber(AProp, value.a);
            writer.WriteEndObject();
        }
    }
}
