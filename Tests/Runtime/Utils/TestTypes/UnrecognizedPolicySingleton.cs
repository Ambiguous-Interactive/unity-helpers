// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    // A value this build does not recognize must not be read as a refusal: a singleton silently
    // ceasing to exist is a worse answer to an unknown policy than creating one.
    [SingletonCreation((SingletonCreationPolicy)200)]
    internal sealed class UnrecognizedPolicySingleton
        : RuntimeSingleton<UnrecognizedPolicySingleton> { }
}
