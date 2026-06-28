// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [DataContract]
    public sealed class TestDataObject
    {
        public string field;
        public int Property { get; set; }

        [JsonPropertyName("DifferentPropertyName")]
        public float NamedProperty { get; set; }

        public Dictionary<string, bool> DictionaryProperty { get; set; } = new();

        public List<int> ListProperty { get; set; } = new();

        public List<Type> TypeProperties { get; set; } = new();
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class JsonSerializationTest : CommonTestBase
    {
        [Test]
        public void UnityEngineObjectSerializationWorks()
        {
            GameObject testGo = Track(new GameObject("Test GameObject", typeof(SpriteRenderer)));
            long expectedId = testGo.GetUnityObjectId();
            string json = testGo.ToJson();
            Assert.IsFalse(string.IsNullOrWhiteSpace(json), json);
            Assert.AreNotEqual("{}", json);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            Assert.AreEqual(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("name", out JsonElement name));
            Assert.True(root.TryGetProperty("type", out JsonElement type));
            Assert.True(root.TryGetProperty("instanceId", out JsonElement id));
            Assert.AreEqual("Test GameObject", name.GetString());
            StringAssert.Contains("UnityEngine.GameObject", type.GetString());
            Assert.AreEqual(expectedId, id.GetInt64());
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator NullGameObjectSerializationWorks()
        {
            GameObject testGo = null;
            string json = testGo.ToJson();
            Assert.AreEqual("null", json);

            testGo = Track(new GameObject());
            testGo.Destroy();
            // Destroy is async in PlayMode; poll until Unity nullifies the wrapper rather than
            // assuming a single frame settles it (version/CI-load flaky otherwise).
            yield return WaitUntilDestroyed(testGo);
            Assert.IsTrue(testGo == null);
            json = testGo.ToJson();
            Assert.AreEqual("null", json);
        }

        [Test]
        public void TransformSerializationWorks()
        {
            GameObject testGo = Track(new GameObject("Test GameObject", typeof(SpriteRenderer)));
            Transform transform = testGo.transform;
            string json = transform.ToJson();
            Assert.AreEqual("[]", json);
        }

        [Test]
        public void ColorSerializationWorks()
        {
            Color color = new(0.5f, 0.5f, 0.5f, 0.5f);
            string json = color.ToJson();
            Color deserialized = Serializer.JsonDeserialize<Color>(json);
            Assert.AreEqual(color, deserialized);

            color = new Color(0.7f, 0.1f, 0.3f);
            json = color.ToJson();
            deserialized = Serializer.JsonDeserialize<Color>(json);
            Assert.AreEqual(color, deserialized);
        }

        [Test]
        public void Vector4SerializationWorks()
        {
            Vector4 vector = new(0.5f, 0.5f, 0.5f, 0.5f);
            string json = vector.ToJson();
            Vector4 deserialized = Serializer.JsonDeserialize<Vector4>(json);
            Assert.AreEqual(vector, deserialized);

            vector = new Vector4(0.7f, 0.1f, 0.3f);
            json = vector.ToJson();
            deserialized = Serializer.JsonDeserialize<Vector4>(json);
            Assert.AreEqual(vector, deserialized);
        }

        [Test]
        public void SerializationWorks()
        {
            IRandom random = PRNG.Instance;
            TestDataObject input = new()
            {
                field = Guid.NewGuid().ToString(),
                Property = random.Next(),
                NamedProperty = random.NextFloat(),
                TypeProperties = new List<Type>()
                {
                    typeof(int),
                    typeof(Serializer),
                    typeof(TestDataObject),
                },
            };

            int dictionaryProperties = random.Next(4, 10);
            for (int i = 0; i < dictionaryProperties; ++i)
            {
                input.DictionaryProperty[Guid.NewGuid().ToString()] = random.NextBool();
            }

            int listProperties = random.Next(4, 10);
            for (int i = 0; i < listProperties; ++i)
            {
                input.ListProperty.Add(random.Next());
            }

            string json = input.ToJson();
            Assert.IsTrue(
                json.Contains("DifferentPropertyName"),
                $"DifferentPropertyName failed to serialize! JSON: {json}"
            );

            TestDataObject deserialized = Serializer.JsonDeserialize<TestDataObject>(json);
            Assert.AreEqual(
                input.field,
                deserialized.field,
                $"Unexpected {nameof(deserialized.field)}! JSON: {json}"
            );
            Assert.AreEqual(
                input.Property,
                deserialized.Property,
                $"Unexpected {nameof(deserialized.Property)}! JSON: {json}"
            );
            Assert.AreEqual(
                input.NamedProperty,
                deserialized.NamedProperty,
                $"Unexpected {nameof(deserialized.NamedProperty)}! JSON: {json}"
            );
            Assert.IsTrue(
                input.DictionaryProperty.ContentEquals(deserialized.DictionaryProperty),
                $"Unexpected {nameof(deserialized.DictionaryProperty)}! JSON: {json}"
            );
            Assert.IsTrue(
                input.ListProperty.SequenceEqual(deserialized.ListProperty),
                $"Unexpected {nameof(deserialized.ListProperty)}! JSON: {json}"
            );
            Assert.That(input.TypeProperties, Is.EquivalentTo(deserialized.TypeProperties));
        }
    }
}
