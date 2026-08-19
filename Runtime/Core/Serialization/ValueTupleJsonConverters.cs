// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Serves a hand-written converter for <see cref="ValueTuple{T1, T2}"/> and
    /// <see cref="ValueTuple{T1, T2, T3}"/>.
    /// </summary>
    /// <remarks>
    /// Without it, System.Text.Json falls back to <c>ObjectDefaultConverter&lt;T&gt;</c>, which it
    /// instantiates reflectively over the closed tuple -- so <c>JsonStringify((7, 1.5f))</c> works in
    /// the editor and throws <c>ExecutionEngineException</c> on an IL2CPP player. Measured on Unity
    /// 2021.3.
    ///
    /// The output is delegated to <see cref="SerializableValueTuple{T1, T2}"/>'s own converter rather
    /// than written twice, so a tuple and its serializable stand-in produce the same JSON by
    /// construction.
    /// </remarks>
    internal sealed class ValueTupleJsonConverterFactory : JsonConverterFactory
    {
        /// <summary>The shared instance registered into the package's serializer options.</summary>
        public static readonly ValueTupleJsonConverterFactory Instance = new();

        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert == null || !typeToConvert.IsGenericType)
            {
                return false;
            }

            Type definition = typeToConvert.GetGenericTypeDefinition();
            return definition == typeof(ValueTuple<,>) || definition == typeof(ValueTuple<,,>);
        }

        /// <inheritdoc/>
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
        {
            if (type == null)
            {
                return null;
            }

            Type[] arguments = type.GetGenericArguments();
            Type converter =
                arguments.Length == 2
                    ? typeof(ValueTupleJsonConverter<,>).MakeGenericType(arguments)
                    : typeof(ValueTripleJsonConverter<,,>).MakeGenericType(arguments);
            return (JsonConverter)Activator.CreateInstance(converter);
        }
    }

    internal sealed class ValueTupleJsonConverter<T1, T2> : JsonConverter<ValueTuple<T1, T2>>
    {
        /// <inheritdoc/>
        public override ValueTuple<T1, T2> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            SerializableValueTuple<T1, T2> value = JsonSerializer.Deserialize<
                SerializableValueTuple<T1, T2>
            >(ref reader, options);
            return new ValueTuple<T1, T2>(value.Item1, value.Item2);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            ValueTuple<T1, T2> value,
            JsonSerializerOptions options
        )
        {
            JsonSerializer.Serialize(
                writer,
                new SerializableValueTuple<T1, T2>(value.Item1, value.Item2),
                options
            );
        }
    }

    internal sealed class ValueTripleJsonConverter<T1, T2, T3>
        : JsonConverter<ValueTuple<T1, T2, T3>>
    {
        /// <inheritdoc/>
        public override ValueTuple<T1, T2, T3> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            SerializableValueTuple<T1, T2, T3> value = JsonSerializer.Deserialize<
                SerializableValueTuple<T1, T2, T3>
            >(ref reader, options);
            return new ValueTuple<T1, T2, T3>(value.Item1, value.Item2, value.Item3);
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            ValueTuple<T1, T2, T3> value,
            JsonSerializerOptions options
        )
        {
            JsonSerializer.Serialize(
                writer,
                new SerializableValueTuple<T1, T2, T3>(value.Item1, value.Item2, value.Item3),
                options
            );
        }
    }
}
