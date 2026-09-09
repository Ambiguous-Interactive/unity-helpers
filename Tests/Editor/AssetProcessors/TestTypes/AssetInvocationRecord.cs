// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Record of an asset change handler invocation for testing purposes.
    /// </summary>
    internal readonly struct AssetInvocationRecord
    {
        public Type AssetType { get; }

        public AssetChangeFlags Flags { get; }

        public AssetInvocationRecord(Type assetType, AssetChangeFlags flags)
        {
            AssetType = assetType;
            Flags = flags;
        }
    }
}
#endif
