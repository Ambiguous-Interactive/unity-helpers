// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.SharedEnums
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;

    /// <summary>
    /// Basic flags enum for testing flag-based toggle buttons and conditional attributes.
    /// </summary>
    [Flags]
    public enum TestFlagsEnum
    {
        None = 0,
        FlagA = 1 << 0,
        FlagB = 1 << 1,
        FlagC = 1 << 2,
    }
#endif
}
