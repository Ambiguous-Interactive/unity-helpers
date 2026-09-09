// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;

    public sealed class Matrix4x4Converter : JsonConverter<Matrix4x4>
    {
        public static readonly Matrix4x4Converter Instance = new();

        private static readonly string[] PropertyNames =
        {
            "m00",
            "m01",
            "m02",
            "m03",
            "m10",
            "m11",
            "m12",
            "m13",
            "m20",
            "m21",
            "m22",
            "m23",
            "m30",
            "m31",
            "m32",
            "m33",
        };

        private Matrix4x4Converter() { }

        public override Matrix4x4 Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token when parsing Matrix4x4.");
            }

            float m00 = 0;
            float m01 = 0;
            float m02 = 0;
            float m03 = 0;
            float m10 = 0;
            float m11 = 0;
            float m12 = 0;
            float m13 = 0;
            float m20 = 0;
            float m21 = 0;
            float m22 = 0;
            float m23 = 0;
            float m30 = 0;
            float m31 = 0;
            float m32 = 0;
            float m33 = 0;

            bool[] found = new bool[16];

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected property name in Matrix4x4 JSON.");
                }
                if (reader.ValueTextEquals("m00"))
                {
                    reader.Read();
                    m00 = reader.GetSingle();
                    found[0] = true;
                }
                else if (reader.ValueTextEquals("m01"))
                {
                    reader.Read();
                    m01 = reader.GetSingle();
                    found[1] = true;
                }
                else if (reader.ValueTextEquals("m02"))
                {
                    reader.Read();
                    m02 = reader.GetSingle();
                    found[2] = true;
                }
                else if (reader.ValueTextEquals("m03"))
                {
                    reader.Read();
                    m03 = reader.GetSingle();
                    found[3] = true;
                }
                else if (reader.ValueTextEquals("m10"))
                {
                    reader.Read();
                    m10 = reader.GetSingle();
                    found[4] = true;
                }
                else if (reader.ValueTextEquals("m11"))
                {
                    reader.Read();
                    m11 = reader.GetSingle();
                    found[5] = true;
                }
                else if (reader.ValueTextEquals("m12"))
                {
                    reader.Read();
                    m12 = reader.GetSingle();
                    found[6] = true;
                }
                else if (reader.ValueTextEquals("m13"))
                {
                    reader.Read();
                    m13 = reader.GetSingle();
                    found[7] = true;
                }
                else if (reader.ValueTextEquals("m20"))
                {
                    reader.Read();
                    m20 = reader.GetSingle();
                    found[8] = true;
                }
                else if (reader.ValueTextEquals("m21"))
                {
                    reader.Read();
                    m21 = reader.GetSingle();
                    found[9] = true;
                }
                else if (reader.ValueTextEquals("m22"))
                {
                    reader.Read();
                    m22 = reader.GetSingle();
                    found[10] = true;
                }
                else if (reader.ValueTextEquals("m23"))
                {
                    reader.Read();
                    m23 = reader.GetSingle();
                    found[11] = true;
                }
                else if (reader.ValueTextEquals("m30"))
                {
                    reader.Read();
                    m30 = reader.GetSingle();
                    found[12] = true;
                }
                else if (reader.ValueTextEquals("m31"))
                {
                    reader.Read();
                    m31 = reader.GetSingle();
                    found[13] = true;
                }
                else if (reader.ValueTextEquals("m32"))
                {
                    reader.Read();
                    m32 = reader.GetSingle();
                    found[14] = true;
                }
                else if (reader.ValueTextEquals("m33"))
                {
                    reader.Read();
                    m33 = reader.GetSingle();
                    found[15] = true;
                }
                else
                {
                    throw new JsonException("Unknown property for Matrix4x4.");
                }
            }

            for (int i = 0; i < found.Length; i++)
            {
                if (!found[i])
                {
                    throw new JsonException(
                        $"Missing property '{PropertyNames[i]}' for Matrix4x4."
                    );
                }
            }

            Matrix4x4 matrix = new()
            {
                m00 = m00,
                m01 = m01,
                m02 = m02,
                m03 = m03,
                m10 = m10,
                m11 = m11,
                m12 = m12,
                m13 = m13,
                m20 = m20,
                m21 = m21,
                m22 = m22,
                m23 = m23,
                m30 = m30,
                m31 = m31,
                m32 = m32,
                m33 = m33,
            };

            return matrix;
        }

        public override void Write(
            Utf8JsonWriter writer,
            Matrix4x4 matrix,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();

            writer.WriteNumber("m00", matrix.m00);
            writer.WriteNumber("m01", matrix.m01);
            writer.WriteNumber("m02", matrix.m02);
            writer.WriteNumber("m03", matrix.m03);
            writer.WriteNumber("m10", matrix.m10);
            writer.WriteNumber("m11", matrix.m11);
            writer.WriteNumber("m12", matrix.m12);
            writer.WriteNumber("m13", matrix.m13);
            writer.WriteNumber("m20", matrix.m20);
            writer.WriteNumber("m21", matrix.m21);
            writer.WriteNumber("m22", matrix.m22);
            writer.WriteNumber("m23", matrix.m23);
            writer.WriteNumber("m30", matrix.m30);
            writer.WriteNumber("m31", matrix.m31);
            writer.WriteNumber("m32", matrix.m32);
            writer.WriteNumber("m33", matrix.m33);

            writer.WriteEndObject();
        }
    }
}
