// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WValueDropDown with inline integer options.
    /// </summary>
    internal sealed class OdinValueDropDownInlineIntTarget : SerializedScriptableObject
    {
        [WValueDropDown(0, 25, 50, 75, 100)]
        public int percentage;
    }
#endif
}
