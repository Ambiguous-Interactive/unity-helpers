// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Stands in for a singleton that has no asset at all, so a resolved null is the right answer
    /// and re-running the search on every access would be the wrong one.
    /// </summary>
    [ExcludeFromSingletonCreation]
    internal sealed class AbsentAssetSingleton : ScriptableObjectSingleton<AbsentAssetSingleton>
    {
        public string note = "absent";
    }
}
#endif
