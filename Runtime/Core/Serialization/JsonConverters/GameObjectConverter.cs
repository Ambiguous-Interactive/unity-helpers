// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;

    public sealed class GameObjectConverter : JsonConverter<GameObject>
    {
        public static readonly GameObjectConverter Instance = new();

        private GameObjectConverter() { }

        public override GameObject Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            throw new NotImplementedException(nameof(Read));
        }

        public override void Write(
            Utf8JsonWriter writer,
            GameObject value,
            JsonSerializerOptions options
        )
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("name", value.name);
            // Use AssemblyQualifiedName to disambiguate type for diagnostics
            writer.WriteString("type", value.GetType().AssemblyQualifiedName);
            // Emit the actual Unity instance ID without transformation for correctness.
            writer.WriteNumber("instanceId", value.GetUnityObjectId());
            writer.WriteEndObject();
        }
    }
}
