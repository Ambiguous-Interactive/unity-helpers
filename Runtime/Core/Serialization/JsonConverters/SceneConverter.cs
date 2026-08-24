// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine.SceneManagement;

    public sealed class SceneConverter : JsonConverter<Scene>
    {
        public static readonly SceneConverter Instance = new();

        private static readonly JsonEncodedText NameProp = JsonEncodedText.Encode("name");
        private static readonly JsonEncodedText BuildIndexProp = JsonEncodedText.Encode(
            "buildIndex"
        );
        private static readonly JsonEncodedText PathProp = JsonEncodedText.Encode("path");

        private SceneConverter() { }

        public override Scene Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            string name = null;
            string path = null;
            int buildIndex = -1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        Scene s = SceneManager.GetSceneByPath(path);
                        if (s.IsValid())
                        {
                            return s;
                        }
                    }
                    if (0 <= buildIndex)
                    {
                        Scene s = SceneManager.GetSceneByBuildIndex(buildIndex);
                        if (s.IsValid())
                        {
                            return s;
                        }
                    }
                    if (!string.IsNullOrEmpty(name))
                    {
                        Scene s = SceneManager.GetSceneByName(name);
                        if (s.IsValid())
                        {
                            return s;
                        }
                    }
                    return default;
                }
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("name"))
                    {
                        reader.Read();
                        name = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("buildIndex"))
                    {
                        reader.Read();
                        buildIndex = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals("path"))
                    {
                        reader.Read();
                        path = reader.GetString();
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Scene");
                    }
                }
            }
            throw new JsonException("Incomplete JSON for Scene");
        }

        public override void Write(
            Utf8JsonWriter writer,
            Scene value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString(NameProp, value.name);
            writer.WriteNumber(BuildIndexProp, value.buildIndex);
            writer.WriteString(PathProp, value.path);
            writer.WriteEndObject();
        }
    }
}
