// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;
using WallstopStudios.UnityHelpers.Proto.Generator.Tests;

// Consumer-only type arguments ensure these registrations come from scanning this assembly.
[assembly: WJsonConverter(typeof(ProbeBox<>), typeof(ProbeBoxConverter<>))]
[assembly: WJsonConverter(typeof(ProbePair<,>), typeof(ProbePairConverter<,>))]

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>A value type nothing outside this assembly names, so no build shares its closures.</summary>
    public struct ProbeStruct
    {
        /// <summary>The only field, so a round-trip has something to compare.</summary>
        public int Value;
    }

    /// <summary>A generic container standing in for <c>Deque&lt;T&gt;</c> and its siblings.</summary>
    /// <typeparam name="T">The contained type.</typeparam>
    public sealed class ProbeBox<T>
    {
        /// <summary>The contained value.</summary>
        public T Value;
    }

    /// <summary>A two-parameter container, so arity beyond one is covered.</summary>
    /// <typeparam name="TFirst">The first contained type.</typeparam>
    /// <typeparam name="TSecond">The second contained type.</typeparam>
    public sealed class ProbePair<TFirst, TSecond>
    {
        /// <summary>The first value.</summary>
        public TFirst First;

        /// <summary>The second value.</summary>
        public TSecond Second;
    }

    /// <summary>The converter the generator closes and constructs.</summary>
    /// <typeparam name="T">The contained type.</typeparam>
    public sealed class ProbeBoxConverter<T> : JsonConverter<ProbeBox<T>>
    {
        /// <inheritdoc />
        public override ProbeBox<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            return new ProbeBox<T> { Value = JsonSerializer.Deserialize<T>(ref reader, options) };
        }

        /// <inheritdoc />
        public override void Write(
            Utf8JsonWriter writer,
            ProbeBox<T> value,
            JsonSerializerOptions options
        )
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }

    /// <summary>The two-parameter converter.</summary>
    /// <typeparam name="TFirst">The first contained type.</typeparam>
    /// <typeparam name="TSecond">The second contained type.</typeparam>
    public sealed class ProbePairConverter<TFirst, TSecond>
        : JsonConverter<ProbePair<TFirst, TSecond>>
    {
        /// <inheritdoc />
        public override ProbePair<TFirst, TSecond> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            reader.Read();
            TFirst first = JsonSerializer.Deserialize<TFirst>(ref reader, options);
            reader.Read();
            TSecond second = JsonSerializer.Deserialize<TSecond>(ref reader, options);
            reader.Read();
            return new ProbePair<TFirst, TSecond> { First = first, Second = second };
        }

        /// <inheritdoc />
        public override void Write(
            Utf8JsonWriter writer,
            ProbePair<TFirst, TSecond> value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartArray();
            JsonSerializer.Serialize(writer, value.First, options);
            JsonSerializer.Serialize(writer, value.Second, options);
            writer.WriteEndArray();
        }
    }
}
