// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WShowIf flags enum condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfFlagsTarget : SerializedScriptableObject
    {
        public TestFlagsEnum flags;

        [WShowIf(nameof(flags), expectedValues: new object[] { TestFlagsEnum.FlagA })]
        public int dependentField;
    }
#endif
}
