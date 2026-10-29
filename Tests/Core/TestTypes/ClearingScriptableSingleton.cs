// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    [ExcludeFromSingletonCreation]
    internal sealed class ClearingScriptableSingleton
        : ScriptableObjectSingleton<ClearingScriptableSingleton>
    {
        internal Action clearing;

        protected override void OnInstanceCleared()
        {
            clearing?.Invoke();
        }
    }
}
