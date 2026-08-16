// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;

    public sealed class TouchConverter : JsonConverter<Touch>
    {
        public static readonly TouchConverter Instance = new();

        private TouchConverter() { }

        public override Touch Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            throw new NotSupportedException(
                $"{nameof(Touch)} is written for diagnostics only; the platform owns every field, "
                    + "so there is nothing to restore it into."
            );
        }

        public override void Write(
            Utf8JsonWriter writer,
            Touch value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteNumber("fingerId", value.fingerId);
            writer.WritePropertyName("position");
            JsonSerializer.Serialize(writer, value.position, options);
            writer.WritePropertyName("rawPosition");
            JsonSerializer.Serialize(writer, value.rawPosition, options);
            writer.WritePropertyName("deltaPosition");
            JsonSerializer.Serialize(writer, value.deltaPosition, options);
            writer.WriteNumber("deltaTime", value.deltaTime);
            writer.WriteNumber("tapCount", value.tapCount);
            writer.WritePropertyName("phase");
            JsonSerializer.Serialize(writer, value.phase, options);
#if UNITY_2020_1_OR_NEWER
            writer.WriteNumber("pressure", value.pressure);
            writer.WriteNumber("maximumPossiblePressure", value.maximumPossiblePressure);
            writer.WritePropertyName("type");
            JsonSerializer.Serialize(writer, value.type, options);
            writer.WriteNumber("altitudeAngle", value.altitudeAngle);
            writer.WriteNumber("azimuthAngle", value.azimuthAngle);
            writer.WriteNumber("radius", value.radius);
            writer.WriteNumber("radiusVariance", value.radiusVariance);
#endif
            writer.WriteEndObject();
        }
    }
}
