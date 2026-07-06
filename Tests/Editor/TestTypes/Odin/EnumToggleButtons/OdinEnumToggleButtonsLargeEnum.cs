// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.EnumToggleButtons
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WEnumToggleButtons with large flags enum and pagination.
    /// </summary>
    internal sealed class OdinEnumToggleButtonsLargeEnum : SerializedScriptableObject
    {
        [WEnumToggleButtons(enablePagination: true, pageSize: 4)]
        public LargeFlagsEnum largeFlags;
    }
#endif
}
