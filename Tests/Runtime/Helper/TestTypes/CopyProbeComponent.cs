// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class CopyProbeComponent : MonoBehaviour
    {
        public int PublicValue;

        [SerializeField]
        private string _serializedValue;
        public Vector3 AutomaticProperty { get; private set; }

        [SerializeField]
        private List<int> _values = new();

        public IReadOnlyList<int> Values => _values;

        public string SerializedValue => _serializedValue;

        public void Configure(int value, string serialized, Vector3 vector)
        {
            PublicValue = value;
            _serializedValue = serialized;
            AutomaticProperty = vector;
            _values.Clear();
            _values.AddRange(new[] { 1, 2, 3 });
        }
    }
}
