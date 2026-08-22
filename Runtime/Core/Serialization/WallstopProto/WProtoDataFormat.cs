// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Selects which of protobuf's encodings a signed integer member uses.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ProtoBuf.DataFormat</c> member for member, so a contract annotated for both
    /// serializers says the same thing twice rather than two different things. Zero is
    /// <see cref="Default"/> rather than a "none" sentinel because an unset annotation is a real
    /// answer -- the member is encoded the way protobuf encodes its type -- and because it has to
    /// agree with protobuf-net, whose own default is also zero.
    /// </remarks>
    public enum WProtoDataFormat
    {
        /// <summary>Protobuf's default encoding for the member's type: <c>int32</c>, <c>int64</c>.</summary>
        Default = 0,

        /// <summary>
        /// ZigZag varint -- <c>sint32</c> or <c>sint64</c> -- which encodes small magnitudes short
        /// whatever their sign.
        /// </summary>
        /// <remarks>
        /// The default encoding sign-extends a negative value to 64 bits, so <c>-1</c> costs ten
        /// bytes where <c>1</c> costs one. ZigZag maps <c>-1</c> onto <c>1</c> and <c>-2</c> onto
        /// <c>3</c>, so width follows distance from zero rather than which side of it the value
        /// sits. Worth it for a member that is negative often, and a loss for one that is a large
        /// positive number: zigzag spends the low bit on the sign.
        /// </remarks>
        ZigZag = 1,
    }
}
