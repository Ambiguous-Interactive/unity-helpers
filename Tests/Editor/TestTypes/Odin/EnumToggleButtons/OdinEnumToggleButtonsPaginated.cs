// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.EnumToggleButtons
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WEnumToggleButtons with pagination configurations.
    /// </summary>
    internal sealed class OdinEnumToggleButtonsPaginated : SerializedScriptableObject
    {
        [WEnumToggleButtons(enablePagination: true, pageSize: 3)]
        public LargeTestEnum smallPages;

        [WEnumToggleButtons(enablePagination: true, pageSize: 5)]
        public LargeTestEnum mediumPages;

        [WEnumToggleButtons(enablePagination: true, pageSize: 10)]
        public LargeTestEnum largePages;

        [WEnumToggleButtons(enablePagination: false)]
        public LargeTestEnum noPagination;
    }
#endif
}
