// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using Object = UnityEngine.Object;

    [Flags]
    public enum AssetChangeFlags
    {
        None = 0,
        Created = 1 << 0,
        Deleted = 1 << 1,
    }

    [Flags]
    public enum DetectAssetChangedOptions
    {
        None = 0,
        IncludeAssignableTypes = 1 << 0,
        SearchPrefabs = 1 << 1,
        SearchSceneObjects = 1 << 2,
    }

    /// <summary>
    /// Annotates an instance method that should run whenever assets of the specified type change.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class DetectAssetChangedAttribute : Attribute
    {
        public Type AssetType { get; }

        public AssetChangeFlags Flags { get; }

        public DetectAssetChangedOptions Options { get; }

        public bool IncludeAssignableTypes =>
            (Options & DetectAssetChangedOptions.IncludeAssignableTypes) != 0;

        public bool SearchPrefabs => (Options & DetectAssetChangedOptions.SearchPrefabs) != 0;

        public bool SearchSceneObjects =>
            (Options & DetectAssetChangedOptions.SearchSceneObjects) != 0;

        public DetectAssetChangedAttribute(Type assetType, AssetChangeFlags flags)
            : this(assetType, flags, DetectAssetChangedOptions.None) { }

        public DetectAssetChangedAttribute(Type assetType, DetectAssetChangedOptions options)
            : this(assetType, AssetChangeFlags.Created | AssetChangeFlags.Deleted, options) { }

        public DetectAssetChangedAttribute(
            Type assetType,
            AssetChangeFlags flags = AssetChangeFlags.Created | AssetChangeFlags.Deleted,
            DetectAssetChangedOptions options = DetectAssetChangedOptions.None
        )
        {
            if (assetType == null)
            {
                throw new ArgumentNullException(nameof(assetType));
            }

            bool includeAssignableTypes =
                (options & DetectAssetChangedOptions.IncludeAssignableTypes) != 0;
            bool derivesFromUnityObject = typeof(Object).IsAssignableFrom(assetType);

            if (!derivesFromUnityObject && !includeAssignableTypes)
            {
                throw new ArgumentException(
                    $"{assetType.FullName} does not derive from {nameof(Object)}. Enable {nameof(DetectAssetChangedOptions.IncludeAssignableTypes)} to watch assignable assets.",
                    nameof(assetType)
                );
            }

            if (!assetType.IsClass && !assetType.IsInterface)
            {
                throw new ArgumentException(
                    $"{assetType.FullName} must be a class or interface.",
                    nameof(assetType)
                );
            }

            AssetType = assetType;
            Flags =
                flags == AssetChangeFlags.None
                    ? AssetChangeFlags.Created | AssetChangeFlags.Deleted
                    : flags;
            Options = options;
        }
    }
}
