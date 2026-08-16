// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;
    using UnityEngine.UI;

    public sealed class ColorBlockConverter : JsonConverter<ColorBlock>
    {
        public static readonly ColorBlockConverter Instance = new();

        private static readonly JsonEncodedText NormalProp = JsonEncodedText.Encode("normalColor");
        private static readonly JsonEncodedText HighlightedProp = JsonEncodedText.Encode(
            "highlightedColor"
        );
        private static readonly JsonEncodedText PressedProp = JsonEncodedText.Encode(
            "pressedColor"
        );
        private static readonly JsonEncodedText SelectedProp = JsonEncodedText.Encode(
            "selectedColor"
        );
        private static readonly JsonEncodedText DisabledProp = JsonEncodedText.Encode(
            "disabledColor"
        );
        private static readonly JsonEncodedText MultiplierProp = JsonEncodedText.Encode(
            "colorMultiplier"
        );
        private static readonly JsonEncodedText FadeProp = JsonEncodedText.Encode("fadeDuration");

        private ColorBlockConverter() { }

        public override ColorBlock Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            ColorBlock b = ColorBlock.defaultColorBlock;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return b;
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("normalColor"))
                    {
                        reader.Read();
                        b.normalColor = JsonSerializer.Deserialize<Color>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("highlightedColor"))
                    {
                        reader.Read();
                        b.highlightedColor = JsonSerializer.Deserialize<Color>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("pressedColor"))
                    {
                        reader.Read();
                        b.pressedColor = JsonSerializer.Deserialize<Color>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("selectedColor"))
                    {
                        reader.Read();
                        b.selectedColor = JsonSerializer.Deserialize<Color>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("disabledColor"))
                    {
                        reader.Read();
                        b.disabledColor = JsonSerializer.Deserialize<Color>(ref reader, options);
                    }
                    else if (reader.ValueTextEquals("colorMultiplier"))
                    {
                        reader.Read();
                        b.colorMultiplier = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals("fadeDuration"))
                    {
                        reader.Read();
                        b.fadeDuration = reader.GetSingle();
                    }
                    else
                    {
                        throw new JsonException("Unknown property for ColorBlock");
                    }
                }
            }
            throw new JsonException("Incomplete JSON for ColorBlock");
        }

        public override void Write(
            Utf8JsonWriter writer,
            ColorBlock value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WritePropertyName(NormalProp);
            JsonSerializer.Serialize(writer, value.normalColor, options);
            writer.WritePropertyName(HighlightedProp);
            JsonSerializer.Serialize(writer, value.highlightedColor, options);
            writer.WritePropertyName(PressedProp);
            JsonSerializer.Serialize(writer, value.pressedColor, options);
            writer.WritePropertyName(SelectedProp);
            JsonSerializer.Serialize(writer, value.selectedColor, options);
            writer.WritePropertyName(DisabledProp);
            JsonSerializer.Serialize(writer, value.disabledColor, options);
            writer.WriteNumber(MultiplierProp, value.colorMultiplier);
            writer.WriteNumber(FadeProp, value.fadeDuration);
            writer.WriteEndObject();
        }
    }
}
