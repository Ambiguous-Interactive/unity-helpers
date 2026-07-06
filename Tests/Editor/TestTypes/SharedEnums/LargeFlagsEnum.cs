// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;

    /// <summary>
    /// Large flags enum with twelve options for pagination and mask calculation tests.
    /// </summary>
    [Flags]
    public enum LargeFlagsEnum
    {
        None = 0,
        Flag1 = 1 << 0,
        Flag2 = 1 << 1,
        Flag3 = 1 << 2,
        Flag4 = 1 << 3,
        Flag5 = 1 << 4,
        Flag6 = 1 << 5,
        Flag7 = 1 << 6,
        Flag8 = 1 << 7,
        Flag9 = 1 << 8,
        Flag10 = 1 << 9,
        Flag11 = 1 << 10,
        Flag12 = 1 << 11,
    }
#endif
}
