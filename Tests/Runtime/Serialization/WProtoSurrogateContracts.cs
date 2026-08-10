// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A wire shape for <see cref="Vector3"/>, which is not ours to annotate.
    /// </summary>
    /// <remarks>
    /// This is the shape the 16 registrations in <c>ProtobufUnitySurrogates</c> will take. The
    /// surrogate's field numbers alone define the bytes; <see cref="Vector3"/> contributes only its
    /// values, and the conversion happens at the boundary.
    /// </remarks>
    [WProtoContract]
    public partial struct WProtoVector3Surrogate
    {
        /// <summary>The x component.</summary>
        [WProtoMember(1)]
        public float x;

        /// <summary>The y component.</summary>
        [WProtoMember(2)]
        public float y;

        /// <summary>The z component.</summary>
        [WProtoMember(3)]
        public float z;

        /// <summary>Converts to the engine type.</summary>
        /// <param name="value">The surrogate.</param>
        public static implicit operator Vector3(WProtoVector3Surrogate value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        /// <summary>Converts from the engine type.</summary>
        /// <param name="value">The engine value.</param>
        public static implicit operator WProtoVector3Surrogate(Vector3 value)
        {
            return new WProtoVector3Surrogate
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }
    }

    /// <summary>Holds surrogated engine values in each position a member can occupy.</summary>
    [WProtoContract]
    public sealed partial class WProtoSurrogateHolder
    {
        /// <summary>A plain surrogated member.</summary>
        [WProtoMember(1)]
        public Vector3 Position;

        /// <summary>A repeated surrogated element.</summary>
        [WProtoMember(2)]
        public Vector3[] Path;

        /// <summary>A scalar after them, to pin ordering.</summary>
        [WProtoMember(3)]
        public int Trailer;
    }
}
