// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TypeConverterTests
    {
        [Test]
        public void JsonTypeConverterResolvesTypes()
        {
            TypeHolder holder = new() { T = typeof(ReflectionHelpers) };
            JsonSerializerOptions options = new()
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(), TypeConverter.Instance },
            };

            string json = JsonSerializer.Serialize(holder, options);
            TypeHolder roundtrip = JsonSerializer.Deserialize<TypeHolder>(json, options);
            Assert.IsTrue(roundtrip != null, "Deserialized TypeHolder should not be null");
            Assert.AreEqual(typeof(ReflectionHelpers), roundtrip.T);
        }

        [Test]
        public void JsonTypeConverterRecoversMovedGenericArgumentAndNormalizesOnWrite()
        {
            Type expected = typeof(Dictionary<string, List<SerializableType[]>>);
            string original = AssemblyQualifiedTypeNameBuilder.Build(expected);
            Assert.AreSame(expected, Type.GetType(original, throwOnError: false));
            string moved = original.Replace(
                typeof(SerializableType).Assembly.FullName,
                "MissingAssembly"
            );
            Assert.IsTrue(Type.GetType(moved, throwOnError: false) == null);
            JsonSerializerOptions options = new() { Converters = { TypeConverter.Instance } };
            Dictionary<string, string> payload = new() { [nameof(TypeHolder.T)] = moved };

            TypeHolder recovered = JsonSerializer.Deserialize<TypeHolder>(
                JsonSerializer.Serialize(payload),
                options
            );

            Assert.IsTrue(recovered != null);
            Assert.AreSame(expected, recovered.T);
            string normalized = JsonSerializer.Serialize(recovered, options);
            TypeHolder roundtrip = JsonSerializer.Deserialize<TypeHolder>(normalized, options);
            Assert.IsTrue(roundtrip != null);
            Assert.AreSame(expected, roundtrip.T);
            using JsonDocument document = JsonDocument.Parse(normalized);
            Assert.AreEqual(
                original,
                document.RootElement.GetProperty(nameof(TypeHolder.T)).GetString()
            );
        }

        private sealed class TypeHolder
        {
            public Type T { get; set; }
        }
    }
}
