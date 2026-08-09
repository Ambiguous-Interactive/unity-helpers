// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using DataStructure.Adapters;
    using Random;

    /// <summary>
    /// Registers the formatters this package ships for its own contracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration is explicit rather than discovered, because discovery means reflecting over
    /// loaded assemblies at startup -- the cost and the AOT hazard this serializer exists to avoid.
    /// When the source generator lands it will emit the equivalent of this method per assembly,
    /// including into a consumer's own assembly for a consumer's own contracts.
    /// </para>
    /// <para>
    /// Calling it more than once is harmless. A consumer that deliberately replaces one of these
    /// formatters should register theirs after calling this, since last registration wins.
    /// </para>
    /// </remarks>
    public static class WProtoBuiltInFormatters
    {
        private static bool _registered;

        /// <summary>
        /// Registers every built-in formatter, once.
        /// </summary>
        public static void RegisterAll()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;

            WProtoFormatterProvider.Register(FastVector2Int.WProtoFormatter.Instance);
            WProtoFormatterProvider.Register(FastVector3Int.WProtoFormatter.Instance);
            WProtoFormatterProvider.Register(WGuid.WProtoFormatter.Instance);
            WProtoFormatterProvider.Register(RandomState.WProtoFormatter.Instance);
        }
    }
}
