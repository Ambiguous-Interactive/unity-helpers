// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WValueDropDown with multiple dropdown fields on a single target.
    /// </summary>
    internal sealed class OdinValueDropDownMultipleFieldsTarget : SerializedScriptableObject
    {
        [WValueDropDown("A", "B", "C")]
        public string firstDropdown;

        [WValueDropDown(1, 2, 3)]
        public int secondDropdown;

        [WValueDropDown("X", "Y", "Z")]
        public string thirdDropdown;
    }
#endif
}
