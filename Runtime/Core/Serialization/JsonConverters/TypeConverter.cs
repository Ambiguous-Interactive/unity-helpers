// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Converts trusted JSON type names and recovers unique component assembly moves.
    /// </summary>
    public sealed class TypeConverter : JsonConverter<Type>
    {
        public static readonly TypeConverter Instance = new();

        private TypeConverter() { }

        public override Type Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            string typeName = reader.GetString();
            return string.IsNullOrWhiteSpace(typeName)
                ? null
                : Helper.ReflectionHelpers.TryResolveType(typeName);
        }

        public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(Helper.ReflectionHelpers.GetAssemblyQualifiedName(value));
        }
    }
}
