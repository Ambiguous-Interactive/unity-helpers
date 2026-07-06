// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.StringInList
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for StringInList attribute with unsupported field type (float).
    /// </summary>
    internal sealed class OdinStringInListUnsupportedTypeTarget : SerializedScriptableObject
    {
        [StringInList("Option1", "Option2")]
        public float unsupportedField;
    }
#endif
}
