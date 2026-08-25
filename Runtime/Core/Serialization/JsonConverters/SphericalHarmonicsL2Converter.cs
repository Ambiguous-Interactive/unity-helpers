// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine.Rendering;
    using WallstopStudios.UnityHelpers.Utils;

    public sealed class SphericalHarmonicsL2Converter : JsonConverter<SphericalHarmonicsL2>
    {
        public static readonly SphericalHarmonicsL2Converter Instance = new();

        private static readonly JsonEncodedText CoeffsProp = JsonEncodedText.Encode("coefficients");

        private SphericalHarmonicsL2Converter() { }

        public override SphericalHarmonicsL2 Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            float[] coeffs = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (coeffs == null || coeffs.Length != 27)
                    {
                        throw new JsonException("SphericalHarmonicsL2 requires 27 coefficients");
                    }
                    SphericalHarmonicsL2 sh = new();
                    int idx = 0;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        for (int c = 0; c < 9; c++)
                        {
                            sh[ch, c] = coeffs[idx++];
                        }
                    }
                    return sh;
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("coefficients"))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("coefficients must be an array");
                        }
                        using (
                            // SystemArrayPool: every one of the 27 slots is written before it
                            // is read, and the count is validated separately, so the exact-size
                            // pool buys nothing here.
                            PooledArray<float> lease = SystemArrayPool<float>.Get(
                                27,
                                clearArray: false,
                                out float[] tmp
                            )
                        )
                        {
                            int i = 0;
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.EndArray)
                                {
                                    break;
                                }

                                if (27 <= i)
                                {
                                    throw new JsonException(
                                        "Too many coefficients for SphericalHarmonicsL2"
                                    );
                                }

                                tmp[i++] = reader.GetSingle();
                            }
                            if (i != 27)
                            {
                                throw new JsonException(
                                    "Expected 27 coefficients for SphericalHarmonicsL2"
                                );
                            }
                            coeffs = tmp;
                        }
                    }
                    else
                    {
                        throw new JsonException("Unknown property for SphericalHarmonicsL2");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for SphericalHarmonicsL2");
        }

        public override void Write(
            Utf8JsonWriter writer,
            SphericalHarmonicsL2 value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WritePropertyName(CoeffsProp);
            writer.WriteStartArray();
            for (int ch = 0; ch < 3; ch++)
            {
                for (int c = 0; c < 9; c++)
                {
                    writer.WriteNumberValue(value[ch, c]);
                }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
