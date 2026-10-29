// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    [ExcludeFromSingletonCreation]
    internal sealed class ClearingPeerScriptableSingleton
        : ScriptableObjectSingleton<ClearingPeerScriptableSingleton> { }
}
