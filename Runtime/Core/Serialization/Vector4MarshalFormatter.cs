// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if UNITY_5_3_OR_NEWER
    using UnityEngine;

    /// <summary>Serializes a Vector4 root through its surrogate.</summary>
    internal sealed class Vector4MarshalFormatter
        : SurrogateMarshalFormatter<Vector4, Vector4Surrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Vector4MarshalFormatter"/> class.</summary>
        public Vector4MarshalFormatter()
            : base(Vector4Surrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Vector4Surrogate ToSurrogate(in Vector4 value)
        {
            return value;
        }

        /// <inheritdoc />
        protected override Vector4 FromSurrogate(in Vector4Surrogate surrogate)
        {
            return surrogate;
        }
    }
#endif
}
