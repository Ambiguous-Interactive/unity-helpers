// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WValueDropDown with inline string options on SerializedScriptableObject.
    /// </summary>
    internal sealed class OdinValueDropDownScriptableObjectTarget : SerializedScriptableObject
    {
        [WValueDropDown("Option1", "Option2", "Option3")]
        public string selectedOption;
    }
#endif
}
