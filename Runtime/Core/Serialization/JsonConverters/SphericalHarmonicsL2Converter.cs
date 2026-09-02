// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine.Rendering;

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

            /*
                stackalloc rather than a pooled rent: 27 floats is 108 bytes, and the previous shape
                assigned the rented array to a local that OUTLIVED its `using`, so the coefficients
                were read after the array had gone back to the pool. That survived only because the
                pool it used does not clear on release. It also validated the count by reading the
                array's own length, which silently required a pool returning the exact size asked for.
            */
            const int CoefficientCount = 27;
            Span<float> coeffs = stackalloc float[CoefficientCount];
            bool haveCoefficients = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!haveCoefficients)
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

                        int i = 0;
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }

                            if (CoefficientCount <= i)
                            {
                                throw new JsonException(
                                    "Too many coefficients for SphericalHarmonicsL2"
                                );
                            }

                            coeffs[i++] = reader.GetSingle();
                        }
                        if (i != CoefficientCount)
                        {
                            throw new JsonException(
                                "Expected 27 coefficients for SphericalHarmonicsL2"
                            );
                        }
                        haveCoefficients = true;
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
