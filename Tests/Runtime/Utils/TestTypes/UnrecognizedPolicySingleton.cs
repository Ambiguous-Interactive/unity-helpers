// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    // An unknown policy must preserve singleton availability rather than silently forbid creation.
    [SingletonCreation((SingletonCreationPolicy)200)]
    internal sealed class UnrecognizedPolicySingleton
        : RuntimeSingleton<UnrecognizedPolicySingleton> { }
}
