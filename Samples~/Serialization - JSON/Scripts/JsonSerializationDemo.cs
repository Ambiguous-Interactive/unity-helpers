// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.Serialization.Json
{
    using System;
    using System.Text.Json;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    /// <summary>Stores the values used by the JSON example.</summary>
    [Serializable]
    public struct SampleSave : IEquatable<SampleSave>
    {
#pragma warning disable WPROTO028 // This sample demonstrates JSON, not WallstopProto serialization.
        public Vector3 position;
        public Quaternion rotation;
        public Color color;
        public Rect screenRect;
#pragma warning restore WPROTO028

        /// <summary>Compares the saved values.</summary>
        public bool Equals(SampleSave other)
        {
            return position.Equals(other.position)
                && rotation.Equals(other.rotation)
                && color.Equals(other.color)
                && screenRect.Equals(other.screenRect);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is SampleSave other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(position, rotation, color, screenRect);
        }
    }

    /// <summary>
    /// Demonstrates JSON serialization using Unity-aware converters and helper APIs.
    /// </summary>
    public sealed class JsonSerializationDemo : MonoBehaviour
    {
        private void Start()
        {
            SampleSave save = new SampleSave
            {
                position = new Vector3(1f, 2f, 3f),
                rotation = Quaternion.Euler(10f, 20f, 30f),
                color = new Color(1.2f, 0.5f, 0.3f, 1f),
                screenRect = new Rect(10f, 20f, 1920f, 1080f),
            };

            string json = Serializer.JsonStringify(save, pretty: true);
            Debug.Log("Serialized (pretty):\n" + json);

            SampleSave roundtrip = Serializer.JsonDeserialize<SampleSave>(json);
            Debug.Log(
                $"Roundtrip → pos={roundtrip.position}, rot={roundtrip.rotation.eulerAngles}"
            );

            JsonSerializerOptions fast = Serializer.CreateFastJsonOptions();
            byte[] bytes = Serializer.JsonSerialize(save, fast);
            SampleSave fastDecoded = Serializer.JsonDeserialize<SampleSave>(
                bytes,
                typeof(SampleSave),
                fast
            );
            Debug.Log($"Fast path bytes length: {bytes.Length}");
        }
    }
}
