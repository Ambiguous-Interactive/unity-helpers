// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.EnumToggleButtons
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WEnumToggleButtons with custom buttons per row layouts.
    /// </summary>
    internal sealed class OdinEnumToggleButtonsCustomLayout : SerializedScriptableObject
    {
        [WEnumToggleButtons(buttonsPerRow: 1)]
        public SimpleTestEnum onePerRow;

        [WEnumToggleButtons(buttonsPerRow: 2)]
        public SimpleTestEnum twoPerRow;

        [WEnumToggleButtons(buttonsPerRow: 3)]
        public SimpleTestEnum threePerRow;

        [WEnumToggleButtons(buttonsPerRow: 4)]
        public MediumTestEnum fourPerRow;
    }
#endif
}
