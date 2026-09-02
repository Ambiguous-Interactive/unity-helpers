// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Stands in for a singleton whose cached asset was destroyed under it by a reimport.
    /// </summary>
    [ExcludeFromSingletonCreation]
    internal sealed class DestroyedAssetSingleton
        : ScriptableObjectSingleton<DestroyedAssetSingleton>
    {
        public string note = "destroyed";
    }
}
#endif
