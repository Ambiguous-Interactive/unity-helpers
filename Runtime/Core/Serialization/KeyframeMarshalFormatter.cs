// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if UNITY_5_3_OR_NEWER
    using UnityEngine;

    /// <summary>Serializes a Keyframe root through its surrogate.</summary>
    internal sealed class KeyframeMarshalFormatter
        : SurrogateMarshalFormatter<Keyframe, KeyframeSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="KeyframeMarshalFormatter"/> class.</summary>
        public KeyframeMarshalFormatter()
            : base(KeyframeSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override KeyframeSurrogate ToSurrogate(in Keyframe value)
        {
            return value;
        }

        /// <inheritdoc />
        protected override Keyframe FromSurrogate(in KeyframeSurrogate surrogate)
        {
            return surrogate;
        }
    }
#endif
}
