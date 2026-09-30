// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if UNITY_5_3_OR_NEWER
    using UnityEngine;

    /// <summary>Serializes a Matrix4x4 root through its surrogate.</summary>
    internal sealed class Matrix4x4MarshalFormatter
        : SurrogateMarshalFormatter<Matrix4x4, Matrix4x4Surrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Matrix4x4MarshalFormatter"/> class.</summary>
        public Matrix4x4MarshalFormatter()
            : base(Matrix4x4Surrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Matrix4x4Surrogate ToSurrogate(in Matrix4x4 value)
        {
            return value;
        }

        /// <inheritdoc />
        protected override Matrix4x4 FromSurrogate(in Matrix4x4Surrogate surrogate)
        {
            return surrogate;
        }
    }
#endif
}
