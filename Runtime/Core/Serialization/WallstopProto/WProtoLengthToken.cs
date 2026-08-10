// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Marks where a length-delimited field's prefix and payload begin, so the prefix can be
    /// back-filled once the payload's size is known.
    /// </summary>
    /// <remarks>
    /// Returned by <see cref="WProtoWriter.TryBeginLengthDelimited"/> and consumed by
    /// <see cref="WProtoWriter.TryCloseLengthDelimited"/>. It carries positions rather than a length
    /// because the length is precisely what the caller does not yet know -- which is the point of
    /// writing the payload first and the prefix afterwards.
    /// </remarks>
    public readonly struct WProtoLengthToken
    {
        internal readonly int PrefixStart;
        internal readonly int PayloadStart;

        internal WProtoLengthToken(int prefixStart, int payloadStart)
        {
            PrefixStart = prefixStart;
            PayloadStart = payloadStart;
        }
    }
}
