// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class JsonConverterAdditionalTests : CommonTestBase
    {
        [Test]
        public void GameObjectConverterWritesStructuredJson()
        {
            GameObject go = Track(new GameObject("ConverterTestObject"));
            long expectedId = go.GetUnityObjectId();
            string json = Serializer.JsonStringify(go);
            Assert.IsFalse(string.IsNullOrWhiteSpace(json));

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            Assert.AreEqual(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("name", out JsonElement name));
            Assert.True(root.TryGetProperty("type", out JsonElement type));
            Assert.True(root.TryGetProperty("instanceId", out JsonElement id));
            Assert.AreEqual("ConverterTestObject", name.GetString());
            StringAssert.Contains("UnityEngine.GameObject", type.GetString());
            Assert.AreEqual(expectedId, id.GetInt64());
        }
    }
}
