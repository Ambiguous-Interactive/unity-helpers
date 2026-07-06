// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.EnumToggleButtons
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WEnumToggleButtons with all attribute configurations.
    /// </summary>
    internal sealed class OdinEnumToggleButtonsAllConfigs : SerializedScriptableObject
    {
        [WEnumToggleButtons(buttonsPerRow: 0)]
        public SimpleTestEnum autoLayout;

        [WEnumToggleButtons(buttonsPerRow: 2)]
        public SimpleTestEnum twoPerRow;

        [WEnumToggleButtons(showSelectAll: true, showSelectNone: true)]
        public TestFlagsEnum withToolbar;

        [WEnumToggleButtons(showSelectAll: false, showSelectNone: false)]
        public TestFlagsEnum withoutToolbar;

        [WEnumToggleButtons(enablePagination: true, pageSize: 5)]
        public LargeTestEnum paginated;

        [WEnumToggleButtons(enablePagination: false)]
        public LargeTestEnum notPaginated;
    }
#endif
}
