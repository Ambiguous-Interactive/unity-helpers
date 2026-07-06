// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for IntDropDown attribute applied to an invalid field type (string instead of int).
    /// </summary>
    internal sealed class OdinIntDropDownInvalidTypeTarget : SerializedScriptableObject
    {
        [IntDropDown(1, 2, 3)]
        public string invalidTypeField;
    }
#endif
}
