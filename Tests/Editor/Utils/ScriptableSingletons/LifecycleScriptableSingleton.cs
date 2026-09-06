// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    [ExcludeFromSingletonCreation]
    internal sealed class LifecycleScriptableSingleton
        : ScriptableObjectSingleton<LifecycleScriptableSingleton>
    {
        public static int ClearedCount;

        internal Action clearing;

        protected override void OnInstanceCleared()
        {
            base.OnInstanceCleared();
            ClearedCount++;
            clearing?.Invoke();
        }
    }
}
#endif
