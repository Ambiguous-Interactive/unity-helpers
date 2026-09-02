// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Stands in for a singleton whose first load threw, which <c>Lazy</c> caches and rethrows.
    /// </summary>
    [ExcludeFromSingletonCreation]
    internal sealed class PoisonedLoadSingleton : ScriptableObjectSingleton<PoisonedLoadSingleton>
    {
        public string note = "poisoned";
    }
}
#endif
