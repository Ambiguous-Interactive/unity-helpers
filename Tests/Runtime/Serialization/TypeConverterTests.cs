// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;

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

        private sealed class TypeHolder
        {
            public Type T { get; set; }
        }
    }
}
