// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System.Text;

    /// <summary>
    /// Shared text primitives for wire-format decoding.
    /// </summary>
    internal static class WProtoText
    {
        /// <summary>
        /// The only decoder wire bytes may pass through.
        /// </summary>
        /// <remarks>
        /// The BCL's <see cref="Encoding.UTF8"/> replaces an invalid byte with U+FFFD instead of
        /// reporting it, which turns corrupt wire bytes into a silently different string -- a value
        /// no honest writer could have produced. This decoder throws on the first invalid sequence,
        /// so every decode site can refuse the payload rather than manufacture text.
        /// </remarks>
        internal static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true
        );
    }
}
