// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target with multiple WShowIf fields for integration tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfMultipleFields : SerializedScriptableObject
    {
        public bool boolCondition;
        public int intCondition;
        public TestModeEnum enumCondition;

        [WShowIf(nameof(boolCondition))]
        public int boolDependent;

        [WShowIf(nameof(intCondition), WShowIfComparison.GreaterThan, 0)]
        public int intDependent;

        [WShowIf(nameof(enumCondition), expectedValues: new object[] { TestModeEnum.ModeA })]
        public int enumDependent;

        [WShowIf(nameof(boolCondition), inverse: true)]
        public int inverseBoolDependent;
    }
#endif
}
