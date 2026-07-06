// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;

    /// <summary>
    /// Flags enum with composite value for testing combined flag handling.
    /// </summary>
    [Flags]
    public enum TestFlagsEnumWithComposite
    {
        None = 0,
        FlagA = 1 << 0,
        FlagB = 1 << 1,
        FlagC = 1 << 2,
        All = FlagA | FlagB | FlagC,
    }
#endif
}
