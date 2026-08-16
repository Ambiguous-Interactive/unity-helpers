// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    public sealed class GradientConverter : JsonConverter<Gradient>
    {
        public static readonly GradientConverter Instance = new();

        private static readonly JsonEncodedText ModeProp = JsonEncodedText.Encode("mode");
        private static readonly JsonEncodedText ColorKeysProp = JsonEncodedText.Encode("colorKeys");
        private static readonly JsonEncodedText AlphaKeysProp = JsonEncodedText.Encode("alphaKeys");

        private static readonly JsonEncodedText ColorProp = JsonEncodedText.Encode("color");
        private static readonly JsonEncodedText AlphaProp = JsonEncodedText.Encode("alpha");
        private static readonly JsonEncodedText TimeProp = JsonEncodedText.Encode("time");

        /// <summary>The number of keys a <see cref="Gradient"/> stores per channel.</summary>
        private const int MaximumKeys = 8;

        private GradientConverter() { }

        public override Gradient Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            GradientColorKey[] colorKeys = null;
            GradientAlphaKey[] alphaKeys = null;
            GradientMode mode = GradientMode.Blend;
            bool haveMode = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    Gradient g = new();
                    if (colorKeys != null)
                    {
                        g.colorKeys = colorKeys;
                    }

                    if (alphaKeys != null)
                    {
                        g.alphaKeys = alphaKeys;
                    }

                    g.mode = haveMode ? mode : GradientMode.Blend;
                    return g;
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("mode"))
                    {
                        reader.Read();
                        mode = JsonSerializer.Deserialize<GradientMode>(ref reader, options);
                        haveMode = true;
                    }
                    else if (reader.ValueTextEquals("colorKeys"))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("colorKeys must be an array");
                        }
                        using PooledResource<List<GradientColorKey>> pooled =
                            Buffers<GradientColorKey>.List.Get(out List<GradientColorKey> list);
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }

                            list.Add(ReadColorKey(ref reader, options));
                        }
                        RefuseKeyOverflow(list.Count, "colorKeys");
                        colorKeys =
                            list.Count == 0 ? Array.Empty<GradientColorKey>() : list.ToArray();
                    }
                    else if (reader.ValueTextEquals("alphaKeys"))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("alphaKeys must be an array");
                        }
                        using PooledResource<List<GradientAlphaKey>> pooled =
                            Buffers<GradientAlphaKey>.List.Get(out List<GradientAlphaKey> list);
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }

                            list.Add(ReadAlphaKey(ref reader));
                        }
                        RefuseKeyOverflow(list.Count, "alphaKeys");
                        alphaKeys =
                            list.Count == 0 ? Array.Empty<GradientAlphaKey>() : list.ToArray();
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Gradient");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for Gradient");
        }

        /// <remarks>
        /// Unity's <see cref="Gradient"/> setters do not throw past the ceiling: they log an error
        /// and keep the first eight keys, so a payload with more silently loses the rest and fills
        /// the player log. Refusing it names the payload instead.
        /// </remarks>
        private static void RefuseKeyOverflow(int count, string member)
        {
            if (MaximumKeys < count)
            {
                throw new JsonException(
                    $"Gradient.{member} holds at most {MaximumKeys} keys, but the payload "
                        + $"delivered {count}."
                );
            }
        }

        private static GradientColorKey ReadColorKey(
            ref Utf8JsonReader reader,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Invalid colorKey token");
            }

            Color c = default;
            float t = 0f;
            bool haveColor = false;
            bool haveTime = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new GradientColorKey(haveColor ? c : default, haveTime ? t : 0f);
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("color"))
                    {
                        reader.Read();
                        c = JsonSerializer.Deserialize<Color>(ref reader, options);
                        haveColor = true;
                    }
                    else if (reader.ValueTextEquals("time"))
                    {
                        reader.Read();
                        t = reader.GetSingle();
                        haveTime = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for GradientColorKey");
                    }
                }
            }
            throw new JsonException("Incomplete JSON for GradientColorKey");
        }

        private static GradientAlphaKey ReadAlphaKey(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Invalid alphaKey token");
            }

            float a = 0f;
            float t = 0f;
            bool haveA = false;
            bool haveT = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new GradientAlphaKey(haveA ? a : 0f, haveT ? t : 0f);
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("alpha"))
                    {
                        reader.Read();
                        a = reader.GetSingle();
                        haveA = true;
                    }
                    else if (reader.ValueTextEquals("time"))
                    {
                        reader.Read();
                        t = reader.GetSingle();
                        haveT = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for GradientAlphaKey");
                    }
                }
            }
            throw new JsonException("Incomplete JSON for GradientAlphaKey");
        }

        public override void Write(
            Utf8JsonWriter writer,
            Gradient value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WritePropertyName(ModeProp);
            JsonSerializer.Serialize(writer, value.mode, options);

            writer.WritePropertyName(ColorKeysProp);
            writer.WriteStartArray();
            foreach (GradientColorKey ck in value.colorKeys)
            {
                writer.WriteStartObject();
                writer.WritePropertyName(ColorProp);
                JsonSerializer.Serialize(writer, ck.color, options);
                writer.WriteNumber(TimeProp, ck.time);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName(AlphaKeysProp);
            writer.WriteStartArray();
            foreach (GradientAlphaKey ak in value.alphaKeys)
            {
                writer.WriteStartObject();
                writer.WriteNumber(AlphaProp, ak.alpha);
                writer.WriteNumber(TimeProp, ak.time);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
    }
}
