// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provides details about the assets that triggered a DetectAssetChanged callback.
    /// </summary>
    public sealed class AssetChangeContext
    {
        public AssetChangeContext(
            Type assetType,
            AssetChangeFlags flags,
            IReadOnlyList<string> createdAssetPaths,
            IReadOnlyList<string> deletedAssetPaths
        )
        {
            AssetType = assetType ?? throw new ArgumentNullException(nameof(assetType));
            Flags = flags;
            CreatedAssetPaths = createdAssetPaths ?? Array.Empty<string>();
            DeletedAssetPaths = deletedAssetPaths ?? Array.Empty<string>();
        }

        public Type AssetType { get; }

        public AssetChangeFlags Flags { get; }

        public IReadOnlyList<string> CreatedAssetPaths { get; }

        public IReadOnlyList<string> DeletedAssetPaths { get; }

        public bool HasCreatedAssets => 0 < CreatedAssetPaths.Count;

        public bool HasDeletedAssets => 0 < DeletedAssetPaths.Count;
    }
}
