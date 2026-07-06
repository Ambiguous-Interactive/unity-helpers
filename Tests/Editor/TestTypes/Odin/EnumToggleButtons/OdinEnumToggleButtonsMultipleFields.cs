// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.EnumToggleButtons
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WEnumToggleButtons with multiple enum fields on same target.
    /// </summary>
    internal sealed class OdinEnumToggleButtonsMultipleFields : SerializedScriptableObject
    {
        [WEnumToggleButtons]
        public SimpleTestEnum firstEnum;

        [WEnumToggleButtons]
        public SimpleTestEnum secondEnum;

        [WEnumToggleButtons]
        public TestFlagsEnum firstFlags;

        [WEnumToggleButtons]
        public TestFlagsEnum secondFlags;

        [WEnumToggleButtons(buttonsPerRow: 2)]
        public MediumTestEnum customLayoutEnum;
    }
#endif
}
