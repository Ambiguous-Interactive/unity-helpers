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

        /// <summary>
        /// Whether opening this field charged a level against the nesting bound.
        /// </summary>
        /// <remarks>
        /// Carried on the token rather than passed to the close, so the two halves cannot disagree.
        /// A sub-message spends a level because it can contain another message; a PACKED RUN spends
        /// none, because it holds bare scalars and cannot recurse at all. The reader already works
        /// this way -- <c>TryReadPackedRun</c> hands the nested reader the SAME depth -- so charging
        /// it on the write side would make a deep-but-legal message decodable and not encodable.
        /// </remarks>
        internal readonly bool Nested;

        internal WProtoLengthToken(int prefixStart, int payloadStart, bool nested)
        {
            PrefixStart = prefixStart;
            PayloadStart = payloadStart;
            Nested = nested;
        }
    }
}
