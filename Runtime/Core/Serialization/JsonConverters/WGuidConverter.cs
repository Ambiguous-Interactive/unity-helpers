// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Buffers.Binary;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Serializes WGuid values as canonical Guid strings while remaining tolerant of legacy payloads.
    /// </summary>
    public sealed class WGuidConverter : JsonConverter<WGuid>
    {
        public static readonly WGuidConverter Instance = new();

        private WGuidConverter() { }

        public override WGuid Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string value = reader.GetString();
                if (string.IsNullOrEmpty(value))
                {
                    return WGuid.Empty;
                }

                if (TryReadText(value, out WGuid parsed))
                {
                    return parsed;
                }

                throw new JsonException($"Invalid {nameof(WGuid)} string value.");
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                return ReadFromObject(ref reader);
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return WGuid.Empty;
            }

            throw new JsonException($"{nameof(WGuid)} must be encoded as a JSON string.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            WGuid value,
            JsonSerializerOptions options
        )
        {
            // Format directly into stack storage to avoid a temporary string for every entity ID.
            Span<char> text = stackalloc char[36];
            if (value.TryFormat(text, out int written))
            {
                writer.WriteStringValue(text.Slice(0, written));
                return;
            }

            writer.WriteStringValue(value.ToString());
        }

        /// <summary>
        /// The single place a GUID's text becomes a <see cref="WGuid"/>, because this converter
        /// accepts it in two shapes and they must agree.
        /// </summary>
        /// <remarks>
        /// <see cref="WGuid.TryParse(string, out WGuid)"/> alone is not enough: <see cref="WGuid.Empty"/> is
        /// what an unset id writes and it is not a version-4 GUID, so parsing rejects the very text
        /// this converter produced.
        /// </remarks>
        private static bool TryReadText(string value, out WGuid result)
        {
            if (WGuid.TryParse(value, out result))
            {
                return true;
            }

            return Guid.TryParse(value, out Guid raw) && WGuid.TryCreate(raw, out result);
        }

        private static WGuid ReadFromObject(ref Utf8JsonReader reader)
        {
            long low = 0L;
            long high = 0L;
            bool sawLow = false;
            bool sawHigh = false;
            string guidString = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!string.IsNullOrEmpty(guidString))
                    {
                        if (TryReadText(guidString, out WGuid parsed))
                        {
                            return parsed;
                        }

                        throw new JsonException(
                            $"Invalid {nameof(Guid)} property for {nameof(WGuid)}."
                        );
                    }

                    if (sawLow && sawHigh)
                    {
                        Span<byte> buffer = stackalloc byte[16];
                        BinaryPrimitives.WriteUInt64LittleEndian(
                            buffer.Slice(0, 8),
                            unchecked((ulong)low)
                        );
                        BinaryPrimitives.WriteUInt64LittleEndian(
                            buffer.Slice(8, 8),
                            unchecked((ulong)high)
                        );
                        // Convert constructor validation failures into the JSON reader error contract.
                        if (WGuid.TryCreate(new Guid(buffer), out WGuid fromHalves))
                        {
                            return fromHalves;
                        }

                        throw new JsonException(
                            $"{WGuid.LowFieldName}/{WGuid.HighFieldName} on {nameof(WGuid)} do not "
                                + "form a version 4 GUID."
                        );
                    }

                    return WGuid.Empty;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException(
                        $"Unexpected token while reading {nameof(WGuid)} object."
                    );
                }

                if (reader.ValueTextEquals(WGuid.LowFieldName))
                {
                    reader.Read();
                    low = reader.GetInt64();
                    sawLow = true;
                }
                else if (reader.ValueTextEquals(WGuid.HighFieldName))
                {
                    reader.Read();
                    high = reader.GetInt64();
                    sawHigh = true;
                }
                else if (reader.ValueTextEquals(WGuid.GuidPropertyName))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        guidString = reader.GetString();
                    }
                    else
                    {
                        throw new JsonException(
                            $"{WGuid.GuidPropertyName} property on {nameof(WGuid)} must be a string."
                        );
                    }
                }
                else
                {
                    reader.Skip();
                }
            }

            throw new JsonException(
                $"Unexpected end of JSON while reading {nameof(WGuid)} object."
            );
        }
    }
}
