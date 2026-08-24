// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Protobuf wire types, as the three low bits of a field key.
    /// </summary>
    /// <remarks>
    /// Constants rather than an enum because the values are fixed by the wire format --
    /// <c>0</c> is <see cref="Varint"/>, not a "none" sentinel -- and because they are
    /// compared against raw <see cref="int"/> values decoded from the stream.
    /// </remarks>
    public static class WProtoWireType
    {
        /// <summary>Base-128 variable-length integer.</summary>
        public const int Varint = 0;

        /// <summary>Fixed eight-byte little-endian value.</summary>
        public const int Fixed64 = 1;

        /// <summary>Varint byte count followed by that many bytes.</summary>
        public const int LengthDelimited = 2;

        /// <summary>Deprecated group opener; retained so unknown fields can be skipped.</summary>
        public const int StartGroup = 3;

        /// <summary>Deprecated group terminator; retained so unknown fields can be skipped.</summary>
        public const int EndGroup = 4;

        /// <summary>Fixed four-byte little-endian value.</summary>
        public const int Fixed32 = 5;

        /// <summary>Largest field number the wire format can express.</summary>
        public const int MaxFieldNumber = (1 << 29) - 1;

        /// <summary>
        /// Indicates whether <paramref name="wireType"/> is one of the six defined wire types.
        /// </summary>
        /// <param name="wireType">The three-bit wire type decoded from a field key.</param>
        /// <returns><c>true</c> when the wire type is defined; otherwise <c>false</c>.</returns>
        public static bool IsDefined(int wireType)
        {
            return Varint <= wireType && wireType <= Fixed32;
        }
    }
}
