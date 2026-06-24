// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class JsonConverterTests : CommonTestBase
    {
        [Test]
        public void QuaternionConverterSerializeAndDeserializeSuccess()
        {
            Quaternion original = new(0.1f, 0.2f, 0.3f, 0.9f);
            string json = Serializer.JsonStringify(original);
            Quaternion deserialized = Serializer.JsonDeserialize<Quaternion>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.z, deserialized.z);
            Assert.AreEqual(original.w, deserialized.w);
        }

        [Test]
        public void QuaternionConverterIdentityDefaultWSuccess()
        {
            string json = "{\"x\":0.0,\"y\":0.0,\"z\":0.0}";
            Quaternion deserialized = Serializer.JsonDeserialize<Quaternion>(json);

            Assert.AreEqual(0f, deserialized.x);
            Assert.AreEqual(0f, deserialized.y);
            Assert.AreEqual(0f, deserialized.z);
            Assert.AreEqual(1f, deserialized.w);
        }

        [Test]
        public void QuaternionConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Quaternion>(invalidJson)
            );
        }

        [Test]
        public void QuaternionConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":0,\"y\":0,\"z\":0,\"w\":1,\"extra\":0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Quaternion>(invalidJson)
            );
        }

        [Test]
        public void QuaternionConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":0.0,\"y\":0.0";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Quaternion>(incompleteJson)
            );
        }

        [Test]
        public void Color32ConverterSerializeAndDeserializeSuccess()
        {
            Color32 original = new(10, 20, 30, 40);
            string json = Serializer.JsonStringify(original);
            Color32 deserialized = Serializer.JsonDeserialize<Color32>(json);

            Assert.AreEqual(original.r, deserialized.r);
            Assert.AreEqual(original.g, deserialized.g);
            Assert.AreEqual(original.b, deserialized.b);
            Assert.AreEqual(original.a, deserialized.a);
        }

        [Test]
        public void Color32ConverterBoundaryValuesSuccess()
        {
            Color32 original = new(0, 255, 0, 255);
            string json = Serializer.JsonStringify(original);
            Color32 deserialized = Serializer.JsonDeserialize<Color32>(json);

            Assert.AreEqual(0, deserialized.r);
            Assert.AreEqual(255, deserialized.g);
            Assert.AreEqual(0, deserialized.b);
            Assert.AreEqual(255, deserialized.a);
        }

        [Test]
        public void Color32ConverterDefaultAlphaValueSuccess()
        {
            string json = "{\"r\":128,\"g\":64,\"b\":32}";
            Color32 deserialized = Serializer.JsonDeserialize<Color32>(json);

            Assert.AreEqual(128, deserialized.r);
            Assert.AreEqual(64, deserialized.g);
            Assert.AreEqual(32, deserialized.b);
            Assert.AreEqual(255, deserialized.a);
        }

        [Test]
        public void Color32ConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color32>(invalidJson)
            );
        }

        [Test]
        public void Color32ConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"r\":1,\"g\":2,\"b\":3,\"a\":4,\"x\":5}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color32>(invalidJson)
            );
        }

        [Test]
        public void Color32ConverterChannelOutOfRangeThrowsException()
        {
            string invalidJson = "{\"r\":-1,\"g\":0,\"b\":0,\"a\":0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color32>(invalidJson)
            );
        }

        [Test]
        public void Color32ConverterChannelAboveRangeThrowsException()
        {
            string invalidJson = "{\"r\":256,\"g\":0,\"b\":0,\"a\":0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color32>(invalidJson)
            );
        }

        [Test]
        public void Vector2IntConverterSerializeAndDeserializeSuccess()
        {
            Vector2Int original = new(5, -7);
            string json = Serializer.JsonStringify(original);
            Vector2Int deserialized = Serializer.JsonDeserialize<Vector2Int>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
        }

        [Test]
        public void Vector2IntConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector2Int>(invalidJson)
            );
        }

        [Test]
        public void Vector2IntConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":1,\"y\":2,\"z\":3}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector2Int>(invalidJson)
            );
        }

        [Test]
        public void Vector2IntConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":1";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector2Int>(incompleteJson)
            );
        }

        [Test]
        public void Vector3IntConverterSerializeAndDeserializeSuccess()
        {
            Vector3Int original = new(1, -2, 3);
            string json = Serializer.JsonStringify(original);
            Vector3Int deserialized = Serializer.JsonDeserialize<Vector3Int>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.z, deserialized.z);
        }

        [Test]
        public void Vector3IntConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector3Int>(invalidJson)
            );
        }

        [Test]
        public void Vector3IntConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":1,\"y\":2,\"z\":3,\"w\":4}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector3Int>(invalidJson)
            );
        }

        [Test]
        public void Vector3IntConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":1,\"y\":2";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector3Int>(incompleteJson)
            );
        }

        [Test]
        public void RectConverterSerializeAndDeserializeSuccess()
        {
            Rect original = new(1.5f, 2.5f, 10.0f, 20.0f);
            string json = Serializer.JsonStringify(original);
            Rect deserialized = Serializer.JsonDeserialize<Rect>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.width, deserialized.width);
            Assert.AreEqual(original.height, deserialized.height);
        }

        [Test]
        public void RectConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Rect>(invalidJson)
            );
        }

        [Test]
        public void RectConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"extra\":0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Rect>(invalidJson)
            );
        }

        [Test]
        public void RectConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":0,\"y\":0";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Rect>(incompleteJson)
            );
        }

        [Test]
        public void RectConverterDefaultsWhenMissingWidthHeight()
        {
            string json = "{\"x\":1.0,\"y\":2.0}";
            Rect deserialized = Serializer.JsonDeserialize<Rect>(json);
            Assert.AreEqual(1.0f, deserialized.x);
            Assert.AreEqual(2.0f, deserialized.y);
            Assert.AreEqual(0.0f, deserialized.width);
            Assert.AreEqual(0.0f, deserialized.height);
        }

        [Test]
        public void RectIntConverterSerializeAndDeserializeSuccess()
        {
            RectInt original = new(1, 2, 3, 4);
            string json = Serializer.JsonStringify(original);
            RectInt deserialized = Serializer.JsonDeserialize<RectInt>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.width, deserialized.width);
            Assert.AreEqual(original.height, deserialized.height);
        }

        [Test]
        public void RectIntConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<RectInt>(invalidJson)
            );
        }

        [Test]
        public void RectIntConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"extra\":0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<RectInt>(invalidJson)
            );
        }

        [Test]
        public void RectIntConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":0,\"y\":0";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<RectInt>(incompleteJson)
            );
        }

        [Test]
        public void BoundsConverterSerializeAndDeserializeSuccess()
        {
            Bounds original = new(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
            string json = Serializer.JsonStringify(original);
            Bounds deserialized = Serializer.JsonDeserialize<Bounds>(json);

            Assert.AreEqual(original.center, deserialized.center);
            Assert.AreEqual(original.size, deserialized.size);
        }

        [Test]
        public void BoundsConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Bounds>(invalidJson)
            );
        }

        [Test]
        public void BoundsConverterUnknownPropertyThrowsException()
        {
            string invalidJson =
                "{\"center\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"extra\":1}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Bounds>(invalidJson)
            );
        }

        [Test]
        public void BoundsConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"center\":{\"x\":0}}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Bounds>(incompleteJson)
            );
        }

        [Test]
        public void BoundsConverterDefaultsWhenMissingSize()
        {
            string json = "{\"center\":{\"x\":1,\"y\":2,\"z\":3}}";
            Bounds deserialized = Serializer.JsonDeserialize<Bounds>(json);
            Assert.AreEqual(new Vector3(1, 2, 3), deserialized.center);
            Assert.AreEqual(Vector3.zero, deserialized.size);
        }

        [Test]
        public void BoundsIntConverterSerializeAndDeserializeSuccess()
        {
            BoundsInt original = new(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
            string json = Serializer.JsonStringify(original);
            BoundsInt deserialized = Serializer.JsonDeserialize<BoundsInt>(json);

            Assert.AreEqual(original.position, deserialized.position);
            Assert.AreEqual(original.size, deserialized.size);
        }

        [Test]
        public void BoundsIntConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<BoundsInt>(invalidJson)
            );
        }

        [Test]
        public void BoundsIntConverterUnknownPropertyThrowsException()
        {
            string invalidJson =
                "{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"extra\":1}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<BoundsInt>(invalidJson)
            );
        }

        [Test]
        public void BoundsIntConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"position\":{\"x\":0}}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<BoundsInt>(incompleteJson)
            );
        }

        [Test]
        public void BoundsIntConverterDefaultsWhenMissingSize()
        {
            string json = "{\"position\":{\"x\":1,\"y\":2,\"z\":3}}";
            BoundsInt deserialized = Serializer.JsonDeserialize<BoundsInt>(json);
            Assert.AreEqual(new Vector3Int(1, 2, 3), deserialized.position);
            Assert.AreEqual(Vector3Int.zero, deserialized.size);
        }

        [Test]
        public void Vector2ConverterSerializeAndDeserializeSuccess()
        {
            Vector2 original = new(1.5f, 2.5f);
            string json = Serializer.JsonStringify(original);
            Vector2 deserialized = Serializer.JsonDeserialize<Vector2>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
        }

        [Test]
        public void Vector2ConverterZeroVectorSuccess()
        {
            Vector2 original = Vector2.zero;
            string json = Serializer.JsonStringify(original);
            Vector2 deserialized = Serializer.JsonDeserialize<Vector2>(json);

            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void Vector2ConverterNegativeValuesSuccess()
        {
            Vector2 original = new(-10.5f, -20.3f);
            string json = Serializer.JsonStringify(original);
            Vector2 deserialized = Serializer.JsonDeserialize<Vector2>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
        }

        [Test]
        public void Vector2ConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector2>(invalidJson)
            );
        }

        [Test]
        public void Vector2ConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":1.0,\"y\":2.0,\"z\":3.0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector2>(invalidJson)
            );
        }

        [Test]
        public void Vector2ConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":1.0";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector2>(incompleteJson)
            );
        }

        [Test]
        public void Vector3ConverterSerializeAndDeserializeSuccess()
        {
            Vector3 original = new(1.5f, 2.5f, 3.5f);
            string json = Serializer.JsonStringify(original);
            Vector3 deserialized = Serializer.JsonDeserialize<Vector3>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.z, deserialized.z);
        }

        [Test]
        public void Vector3ConverterZeroVectorSuccess()
        {
            Vector3 original = Vector3.zero;
            string json = Serializer.JsonStringify(original);
            Vector3 deserialized = Serializer.JsonDeserialize<Vector3>(json);

            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void Vector3ConverterNegativeValuesSuccess()
        {
            Vector3 original = new(-10.5f, -20.3f, -30.7f);
            string json = Serializer.JsonStringify(original);
            Vector3 deserialized = Serializer.JsonDeserialize<Vector3>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.z, deserialized.z);
        }

        [Test]
        public void Vector3ConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector3>(invalidJson)
            );
        }

        [Test]
        public void Vector3ConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":1.0,\"y\":2.0,\"z\":3.0,\"w\":4.0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector3>(invalidJson)
            );
        }

        [Test]
        public void Vector3ConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":1.0,\"y\":2.0";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector3>(incompleteJson)
            );
        }

        [Test]
        public void Vector4ConverterSerializeAndDeserializeSuccess()
        {
            Vector4 original = new(1.5f, 2.5f, 3.5f, 4.5f);
            string json = Serializer.JsonStringify(original);
            Vector4 deserialized = Serializer.JsonDeserialize<Vector4>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.z, deserialized.z);
            Assert.AreEqual(original.w, deserialized.w);
        }

        [Test]
        public void Vector4ConverterZeroVectorSuccess()
        {
            Vector4 original = Vector4.zero;
            string json = Serializer.JsonStringify(original);
            Vector4 deserialized = Serializer.JsonDeserialize<Vector4>(json);

            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void Vector4ConverterNegativeValuesSuccess()
        {
            Vector4 original = new(-10.5f, -20.3f, -30.7f, -40.1f);
            string json = Serializer.JsonStringify(original);
            Vector4 deserialized = Serializer.JsonDeserialize<Vector4>(json);

            Assert.AreEqual(original.x, deserialized.x);
            Assert.AreEqual(original.y, deserialized.y);
            Assert.AreEqual(original.z, deserialized.z);
            Assert.AreEqual(original.w, deserialized.w);
        }

        [Test]
        public void Vector4ConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector4>(invalidJson)
            );
        }

        [Test]
        public void Vector4ConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"x\":1.0,\"y\":2.0,\"z\":3.0,\"w\":4.0,\"extra\":5.0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector4>(invalidJson)
            );
        }

        [Test]
        public void Vector4ConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"x\":1.0,\"y\":2.0,\"z\":3.0";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Vector4>(incompleteJson)
            );
        }

        [Test]
        public void ColorConverterSerializeAndDeserializeSuccess()
        {
            Color original = new(0.1f, 0.2f, 0.3f, 0.4f);
            string json = Serializer.JsonStringify(original);
            Color deserialized = Serializer.JsonDeserialize<Color>(json);

            Assert.AreEqual(original.r, deserialized.r);
            Assert.AreEqual(original.g, deserialized.g);
            Assert.AreEqual(original.b, deserialized.b);
            Assert.AreEqual(original.a, deserialized.a);
        }

        [Test]
        public void ColorConverterBlackColorSuccess()
        {
            Color original = Color.black;
            string json = Serializer.JsonStringify(original);
            Color deserialized = Serializer.JsonDeserialize<Color>(json);

            Assert.AreEqual(original.r, deserialized.r);
            Assert.AreEqual(original.g, deserialized.g);
            Assert.AreEqual(original.b, deserialized.b);
            Assert.AreEqual(original.a, deserialized.a);
        }

        [Test]
        public void ColorConverterWhiteColorSuccess()
        {
            Color original = Color.white;
            string json = Serializer.JsonStringify(original);
            Color deserialized = Serializer.JsonDeserialize<Color>(json);

            Assert.AreEqual(original.r, deserialized.r);
            Assert.AreEqual(original.g, deserialized.g);
            Assert.AreEqual(original.b, deserialized.b);
            Assert.AreEqual(original.a, deserialized.a);
        }

        [Test]
        public void ColorConverterDefaultAlphaValueSuccess()
        {
            Color original = new(0.5f, 0.6f, 0.7f);
            string json = Serializer.JsonStringify(original);
            Color deserialized = Serializer.JsonDeserialize<Color>(json);

            Assert.AreEqual(original.r, deserialized.r);
            Assert.AreEqual(original.g, deserialized.g);
            Assert.AreEqual(original.b, deserialized.b);
            Assert.AreEqual(original.a, deserialized.a);
        }

        [Test]
        public void ColorConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color>(invalidJson)
            );
        }

        [Test]
        public void ColorConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"r\":0.5,\"g\":0.5,\"b\":0.5,\"a\":1.0,\"extra\":0.0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color>(invalidJson)
            );
        }

        [Test]
        public void ColorConverterIncompleteJsonThrowsException()
        {
            string incompleteJson = "{\"r\":0.5,\"g\":0.5";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Color>(incompleteJson)
            );
        }

        [Test]
        public void Matrix4X4ConverterSerializeAndDeserializeSuccess()
        {
            Matrix4x4 original = Matrix4x4.identity;
            string json = Serializer.JsonStringify(original);
            Matrix4x4 deserialized = Serializer.JsonDeserialize<Matrix4x4>(json);

            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void Matrix4X4ConverterCustomMatrixSuccess()
        {
            Matrix4x4 original = new();
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    original[row, col] = row * 4 + col;
                }
            }

            string json = Serializer.JsonStringify(original);
            Matrix4x4 deserialized = Serializer.JsonDeserialize<Matrix4x4>(json);

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    Assert.AreEqual(original[row, col], deserialized[row, col]);
                }
            }
        }

        [Test]
        public void Matrix4X4ConverterZeroMatrixSuccess()
        {
            Matrix4x4 original = Matrix4x4.zero;
            string json = Serializer.JsonStringify(original);
            Matrix4x4 deserialized = Serializer.JsonDeserialize<Matrix4x4>(json);

            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void Matrix4X4ConverterTranslationMatrixSuccess()
        {
            Matrix4x4 original = Matrix4x4.Translate(new Vector3(1, 2, 3));
            string json = Serializer.JsonStringify(original);
            Matrix4x4 deserialized = Serializer.JsonDeserialize<Matrix4x4>(json);

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    Assert.AreEqual(original[row, col], deserialized[row, col], 0.0001f);
                }
            }
        }

        [Test]
        public void Matrix4X4ConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "\"not an object\"";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Matrix4x4>(invalidJson)
            );
        }

        [Test]
        public void Matrix4X4ConverterMissingPropertyThrowsException()
        {
            string incompleteJson = "{\"m00\":1.0,\"m01\":0.0,\"m02\":0.0,\"m03\":0.0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Matrix4x4>(incompleteJson)
            );
        }

        [Test]
        public void Matrix4X4ConverterInvalidPropertyValueThrowsException()
        {
            string invalidJson =
                "{\"m00\":\"invalid\",\"m01\":0.0,\"m02\":0.0,\"m03\":0.0,\"m10\":0.0,\"m11\":1.0,\"m12\":0.0,\"m13\":0.0,\"m20\":0.0,\"m21\":0.0,\"m22\":1.0,\"m23\":0.0,\"m30\":0.0,\"m31\":0.0,\"m32\":0.0,\"m33\":1.0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Matrix4x4>(invalidJson)
            );
        }

        // Note: TypeConverter is tested within object contexts in JsonSerializationTest.SerializationWorks
        // Direct Type serialization tests are covered by the existing test suite

        private class TypeContainerForTest
        {
            public Type TypeProperty { get; set; }
            public System.Collections.Generic.List<Type> TypeList { get; set; }
        }

        [Test]
        public void TypeConverterWithinObjectSuccess()
        {
            TypeContainerForTest original = new()
            {
                TypeProperty = typeof(string),
                TypeList = new System.Collections.Generic.List<Type>
                {
                    typeof(int),
                    typeof(JsonConverterTests),
                    typeof(System.Collections.Generic.List<int>),
                },
            };

            string json = Serializer.JsonStringify(original);
            TypeContainerForTest deserialized = Serializer.JsonDeserialize<TypeContainerForTest>(
                json
            );

            Assert.AreEqual(original.TypeProperty, deserialized.TypeProperty);
            Assert.That(original.TypeList, Is.EquivalentTo(deserialized.TypeList));
        }

        [Test]
        public void TypeConverterNullTypeInObjectSuccess()
        {
            TypeContainerForTest original = new() { TypeProperty = null, TypeList = null };

            string json = Serializer.JsonStringify(original);
            TypeContainerForTest deserialized = Serializer.JsonDeserialize<TypeContainerForTest>(
                json
            );

            Assert.IsTrue(
                deserialized.TypeProperty == null,
                "TypeProperty should be null after round-trip of null"
            );
            Assert.IsTrue(
                deserialized.TypeList == null,
                "TypeList should be null after round-trip of null"
            );
        }

        [Test]
        public void TypeConverterEmptyStringReturnsNull()
        {
            string json = "\"\"";
            Type deserialized = Serializer.JsonDeserialize<Type>(json);

            Assert.IsTrue(deserialized == null, "Empty string should deserialize to null Type");
        }

        [Test]
        public void TypeConverterWhitespaceStringReturnsNull()
        {
            string json = "\"   \"";
            Type deserialized = Serializer.JsonDeserialize<Type>(json);

            Assert.IsTrue(
                deserialized == null,
                "Whitespace-only string should deserialize to null Type"
            );
        }

        [Test]
        public void TypeConverterInvalidTypeNameReturnsNull()
        {
            string json = "\"NonExistent.Type.Name\"";
            Type deserialized = Serializer.JsonDeserialize<Type>(json);

            Assert.IsTrue(
                deserialized == null,
                "Invalid type name should deserialize to null Type"
            );
        }

        [Test]
        public void GameObjectConverterSerializeValidGameObjectSuccess()
        {
            GameObject original = Track(new GameObject("TestObject"));
            string json = Serializer.JsonStringify(original);

            Assert.IsFalse(string.IsNullOrWhiteSpace(json));
            Assert.IsTrue(json.Contains("TestObject"));
            Assert.IsTrue(json.Contains("UnityEngine.GameObject"));
        }

        [Test]
        public void GameObjectConverterSerializeNullGameObjectSuccess()
        {
            GameObject original = null;
            string json = Serializer.JsonStringify(original);

            Assert.AreEqual("null", json);
        }

        [Test]
        public void GameObjectConverterSerializeDestroyedGameObjectReturnsEmptyObject()
        {
            GameObject original = Track(new GameObject("TestObject"));
            UnityEngine.Object.DestroyImmediate(original); // UNH-SUPPRESS: Test verifies serialization of destroyed object

            string json = Serializer.JsonStringify(original);
            Assert.IsFalse(string.IsNullOrWhiteSpace(json));
        }

        [Test]
        public void GameObjectConverterReadWrapsNotImplementedAsCorruptData()
        {
            string json = "{\"name\":\"Test\"}";

            // The GameObjectConverter.Read throws NotImplementedException; the Serializer wraps that
            // codec failure as SerializationCorruptDataException, preserving the original as inner.
            SerializationCorruptDataException ex = Assert.Throws<SerializationCorruptDataException>(
                () =>
                    Serializer.JsonDeserialize<GameObject>(json)
            );
            Assert.IsTrue(ex.InnerException is NotImplementedException);
        }

        [Test]
        public void LayerMaskConverterSerializeAndDeserializeSuccess()
        {
            LayerMask original =
                1 << LayerMask.NameToLayer("Default") | 1 << LayerMask.NameToLayer("UI");
            string json = Serializer.JsonStringify(original);
            LayerMask deserialized = Serializer.JsonDeserialize<LayerMask>(json);
            Assert.AreEqual(original.value, deserialized.value);
        }

        [Test]
        public void LayerMaskConverterAcceptsValueProperty()
        {
            int mask = (1 << LayerMask.NameToLayer("Default"));
            string json = $"{{\"value\":{mask}}}";
            LayerMask deserialized = Serializer.JsonDeserialize<LayerMask>(json);
            Assert.AreEqual(mask, deserialized.value);
        }

        [Test]
        public void LayerMaskConverterAcceptsLayersByName()
        {
            string json = "{\"layers\":[\"Default\",\"UI\"]}";
            LayerMask deserialized = Serializer.JsonDeserialize<LayerMask>(json);
            Assert.IsTrue((deserialized.value & (1 << LayerMask.NameToLayer("Default"))) != 0);
            Assert.IsTrue((deserialized.value & (1 << LayerMask.NameToLayer("UI"))) != 0);
        }

        [Test]
        public void LayerMaskConverterInvalidTokenTypeThrowsException()
        {
            string invalidJson = "{\"layers\":\"not array\"}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<LayerMask>(invalidJson)
            );
        }

        [Test]
        public void PoseConverterSerializeAndDeserializeSuccess()
        {
            Pose original = new(new Vector3(1, 2, 3), new Quaternion(0, 0, 0, 1));
            string json = Serializer.JsonStringify(original);
            Pose deserialized = Serializer.JsonDeserialize<Pose>(json);
            Assert.AreEqual(original.position, deserialized.position);
            Assert.AreEqual(original.rotation, deserialized.rotation);
        }

        [Test]
        public void PoseConverterUnknownPropertyThrowsException()
        {
            string invalidJson = "{\"position\":{\"x\":0,\"y\":0,\"z\":0},\"extra\":0}";
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.JsonDeserialize<Pose>(invalidJson)
            );
        }

        [Test]
        public void PlaneConverterSerializeAndDeserializeSuccess()
        {
            Plane original = new(new Vector3(0, 1, 0), 5f);
            string json = Serializer.JsonStringify(original);
            Plane deserialized = Serializer.JsonDeserialize<Plane>(json);
            Assert.AreEqual(original.normal, deserialized.normal);
            Assert.AreEqual(original.distance, deserialized.distance);
        }

        [Test]
        public void RayConverterSerializeAndDeserializeSuccess()
        {
            Ray original = new(new Vector3(1, 2, 3), new Vector3(0, 0, 1));
            string json = Serializer.JsonStringify(original);
            Ray deserialized = Serializer.JsonDeserialize<Ray>(json);
            Assert.AreEqual(original.origin, deserialized.origin);
            Assert.AreEqual(original.direction, deserialized.direction);
        }

        [Test]
        public void Ray2DConverterSerializeAndDeserializeSuccess()
        {
            Ray2D original = new(new Vector2(4, 5), new Vector2(1, 0));
            string json = Serializer.JsonStringify(original);
            Ray2D deserialized = Serializer.JsonDeserialize<Ray2D>(json);
            Assert.AreEqual(original.origin, deserialized.origin);
            Assert.AreEqual(original.direction, deserialized.direction);
        }

        [Test]
        public void RectOffsetConverterSerializeAndDeserializeSuccess()
        {
            RectOffset original = new(1, 2, 3, 4);
            string json = Serializer.JsonStringify(original);
            RectOffset deserialized = Serializer.JsonDeserialize<RectOffset>(json);
            Assert.AreEqual(original.left, deserialized.left);
            Assert.AreEqual(original.right, deserialized.right);
            Assert.AreEqual(original.top, deserialized.top);
            Assert.AreEqual(original.bottom, deserialized.bottom);
        }

        [Test]
        public void RangeIntConverterSerializeAndDeserializeSuccess()
        {
            RangeInt original = new(10, 5);
            string json = Serializer.JsonStringify(original);
            RangeInt deserialized = Serializer.JsonDeserialize<RangeInt>(json);
            Assert.AreEqual(original.start, deserialized.start);
            Assert.AreEqual(original.length, deserialized.length);
        }

        [Test]
        public void Hash128ConverterSerializeAndDeserializeSuccess()
        {
            Hash128 original = Hash128.Parse("0123456789abcdef0123456789abcdef");
            string json = Serializer.JsonStringify(original);
            Hash128 deserialized = Serializer.JsonDeserialize<Hash128>(json);
            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void Hash128ConverterObjectShapeSuccess()
        {
            Hash128 original = Hash128.Parse("fedcba9876543210fedcba9876543210");
            string json = $"{{\"value\":\"{original}\"}}";
            Hash128 deserialized = Serializer.JsonDeserialize<Hash128>(json);
            Assert.AreEqual(original, deserialized);
        }

        [Test]
        public void AnimationCurveConverterSerializeAndDeserializeSuccess()
        {
            AnimationCurve original = new(
                new Keyframe(0f, 0f, 0f, 1f),
                new Keyframe(1f, 1f, 1f, 0f)
            )
            {
                preWrapMode = WrapMode.ClampForever,
                postWrapMode = WrapMode.Once,
            };
            string json = Serializer.JsonStringify(original);
            AnimationCurve deserialized = Serializer.JsonDeserialize<AnimationCurve>(json);
            Assert.AreEqual(original.preWrapMode, deserialized.preWrapMode);
            Assert.AreEqual(original.postWrapMode, deserialized.postWrapMode);
            Assert.AreEqual(original.keys.Length, deserialized.keys.Length);
            Assert.AreEqual(original.keys[0].time, deserialized.keys[0].time);
            Assert.AreEqual(original.keys[0].value, deserialized.keys[0].value);
        }

        [Test]
        public void GradientConverterSerializeAndDeserializeSuccess()
        {
            Gradient original = new()
            {
                mode = GradientMode.Blend,
                colorKeys = new[]
                {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.blue, 1f),
                },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
            };
            string json = Serializer.JsonStringify(original);
            Gradient deserialized = Serializer.JsonDeserialize<Gradient>(json);
            Assert.AreEqual(original.mode, deserialized.mode);
            Assert.AreEqual(original.colorKeys.Length, deserialized.colorKeys.Length);
            Assert.AreEqual(original.alphaKeys.Length, deserialized.alphaKeys.Length);
            Assert.AreEqual(original.colorKeys[0].color, deserialized.colorKeys[0].color);
            Assert.AreEqual(original.colorKeys[0].time, deserialized.colorKeys[0].time);
        }

        [Test]
        public void SphericalHarmonicsL2ConverterSerializeAndDeserializeSuccess()
        {
            SphericalHarmonicsL2 sh = new();
            int index = 0;
            for (int ch = 0; ch < 3; ch++)
            {
                for (int c = 0; c < 9; c++)
                {
                    sh[ch, c] = index++;
                }
            }
            string json = Serializer.JsonStringify(sh);
            SphericalHarmonicsL2 deserialized = Serializer.JsonDeserialize<SphericalHarmonicsL2>(
                json
            );
            for (int ch = 0; ch < 3; ch++)
            {
                for (int c = 0; c < 9; c++)
                {
                    Assert.AreEqual(sh[ch, c], deserialized[ch, c]);
                }
            }
        }

        [Test]
        public void ResolutionConverterSerializeAndDeserializeSuccess()
        {
            Resolution original = new() { width = 1920, height = 1080 };
            string json = Serializer.JsonStringify(original);
            Resolution deserialized = Serializer.JsonDeserialize<Resolution>(json);
            Assert.AreEqual(original.width, deserialized.width);
            Assert.AreEqual(original.height, deserialized.height);
        }

        [Test]
        public void ResolutionConverterWritesRefreshFields()
        {
            Resolution original = new() { width = 1280, height = 720 };
            string json = Serializer.JsonStringify(original);
            Assert.IsTrue(json.Contains("\"refreshRate\""));
#if UNITY_2022_2_OR_NEWER
            Assert.IsTrue(json.Contains("\"refreshRateRatio\""));
            Assert.IsTrue(json.Contains("\"numerator\""));
            Assert.IsTrue(json.Contains("\"denominator\""));
#endif
        }

        [Test]
        public void ResolutionConverterReadsRatioShape()
        {
#if UNITY_2022_2_OR_NEWER
            string json =
                "{\"width\":800,\"height\":600,\"refreshRate\":60,\"refreshRateRatio\":{\"numerator\":60,\"denominator\":1,\"value\":60.0}}";
#else
            string json = "{\"width\":800,\"height\":600,\"refreshRate\":60}";
#endif
            Resolution deserialized = Serializer.JsonDeserialize<Resolution>(json);
            string round = Serializer.JsonStringify(deserialized);
            Assert.IsTrue(round.Contains("\"refreshRate\""));
        }

        [Test]
        public void RenderTextureDescriptorConverterSerializeAndDeserializeSuccess()
        {
            RenderTextureDescriptor original = new(256, 128)
            {
                msaaSamples = 1,
                volumeDepth = 1,
                mipCount = 1,
                sRGB = true,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = false,
                bindMS = false,
                useDynamicScale = false,
            };
            string json = Serializer.JsonStringify(original);
            RenderTextureDescriptor deserialized =
                Serializer.JsonDeserialize<RenderTextureDescriptor>(json);
            Assert.AreEqual(original.width, deserialized.width);
            Assert.AreEqual(original.height, deserialized.height);
            Assert.AreEqual(original.msaaSamples, deserialized.msaaSamples);
        }

        [Test]
        public void MinMaxCurveConverterSerializeAndDeserializeSuccess()
        {
            ParticleSystem.MinMaxCurve original = new(2f)
            {
                mode = ParticleSystemCurveMode.TwoConstants,
                constant = 3f,
                constantMin = 1f,
                constantMax = 5f,
                curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1)),
            };
            string json = Serializer.JsonStringify(original);
            ParticleSystem.MinMaxCurve deserialized =
                Serializer.JsonDeserialize<ParticleSystem.MinMaxCurve>(json);
            Assert.AreEqual(original.mode, deserialized.mode);
            Assert.AreEqual(original.constant, deserialized.constant);
            Assert.AreEqual(original.constantMin, deserialized.constantMin);
            Assert.AreEqual(original.constantMax, deserialized.constantMax);
        }

        [Test]
        public void MinMaxGradientConverterSerializeAndDeserializeSuccess()
        {
            ParticleSystem.MinMaxGradient original = new(Color.green)
            {
                mode = ParticleSystemGradientMode.TwoColors,
                color = Color.green,
                colorMin = Color.red,
                colorMax = Color.blue,
                gradient = new Gradient(),
            };
            string json = Serializer.JsonStringify(original);
            ParticleSystem.MinMaxGradient deserialized =
                Serializer.JsonDeserialize<ParticleSystem.MinMaxGradient>(json);
            Assert.AreEqual(original.mode, deserialized.mode);
            Assert.AreEqual(original.color, deserialized.color);
            Assert.AreEqual(original.colorMin, deserialized.colorMin);
            Assert.AreEqual(original.colorMax, deserialized.colorMax);
        }

        [Test]
        public void BoundingSphereConverterSerializeAndDeserializeSuccess()
        {
            BoundingSphere original = new(new Vector3(1, 2, 3), 4f);
            string json = Serializer.JsonStringify(original);
            BoundingSphere deserialized = Serializer.JsonDeserialize<BoundingSphere>(json);
            Assert.AreEqual(original.position, deserialized.position);
            Assert.AreEqual(original.radius, deserialized.radius);
        }

        [Test]
        public void RaycastHitConverterSerializeAndDeserializeSuccess()
        {
            RaycastHit original = new()
            {
                point = new Vector3(1, 2, 3),
                normal = Vector3.up,
                distance = 5f,
            };
            string json = Serializer.JsonStringify(original);
            RaycastHit deserialized = Serializer.JsonDeserialize<RaycastHit>(json);
            Assert.AreEqual(original.point, deserialized.point);
            Assert.AreEqual(original.normal, deserialized.normal);
            Assert.AreEqual(original.distance, deserialized.distance);
        }

        [Test]
        public void TouchConverterReadWrapsNotImplementedAsCorruptData()
        {
            string json = "{}";
            // TouchConverter.Read throws NotImplementedException; the Serializer wraps it as
            // SerializationCorruptDataException with the original preserved as inner.
            SerializationCorruptDataException ex = Assert.Throws<SerializationCorruptDataException>(
                () =>
                    Serializer.JsonDeserialize<Touch>(json)
            );
            Assert.IsTrue(ex.InnerException is NotImplementedException);
        }

        [Test]
        public void SceneConverterSerializeAndDeserializeSuccess()
        {
            Scene scene = SceneManager.GetActiveScene();
            string json = Serializer.JsonStringify(scene);
            Scene deserialized = Serializer.JsonDeserialize<Scene>(json);
            Assert.IsTrue(deserialized.IsValid());
            Assert.AreEqual(scene.name, deserialized.name);
        }

#if !UNITY_DISABLE_UI
        [Test]
        public void ColorBlockConverterSerializeAndDeserializeSuccess()
        {
            UnityEngine.UI.ColorBlock original = UnityEngine.UI.ColorBlock.defaultColorBlock;
            original.normalColor = Color.cyan;
            original.highlightedColor = Color.yellow;
            original.pressedColor = Color.gray;
            original.selectedColor = Color.white;
            original.disabledColor = Color.black;
            original.colorMultiplier = 1.5f;
            original.fadeDuration = 0.2f;
            string json = Serializer.JsonStringify(original);
            UnityEngine.UI.ColorBlock deserialized =
                Serializer.JsonDeserialize<UnityEngine.UI.ColorBlock>(json);
            Assert.AreEqual(original.normalColor, deserialized.normalColor);
            Assert.AreEqual(original.highlightedColor, deserialized.highlightedColor);
            Assert.AreEqual(original.pressedColor, deserialized.pressedColor);
            Assert.AreEqual(original.selectedColor, deserialized.selectedColor);
            Assert.AreEqual(original.disabledColor, deserialized.disabledColor);
            Assert.AreEqual(original.colorMultiplier, deserialized.colorMultiplier);
            Assert.AreEqual(original.fadeDuration, deserialized.fadeDuration);
        }
#endif
    }
}
