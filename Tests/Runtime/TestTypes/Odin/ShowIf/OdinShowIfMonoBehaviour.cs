// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.ShowIf
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.SharedEnums;

    /// <summary>
    /// Test MonoBehaviour for WShowIf integration tests with Odin Inspector.
    /// </summary>
    public sealed class OdinShowIfMonoBehaviour : SerializedMonoBehaviour
    {
        public bool showDependent;

        [WShowIf(nameof(showDependent))]
        public int dependentField;

        public TestModeEnum mode;

        [WShowIf(nameof(mode), expectedValues: new object[] { TestModeEnum.ModeA })]
        public float modeAField;

        [WShowIf(nameof(mode), WShowIfComparison.NotEqual, TestModeEnum.ModeA)]
        public float notModeAField;
    }
#endif
}
