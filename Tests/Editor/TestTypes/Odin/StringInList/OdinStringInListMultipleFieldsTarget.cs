// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.StringInList
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for multiple StringInList fields on the same ScriptableObject.
    /// </summary>
    internal sealed class OdinStringInListMultipleFieldsTarget : SerializedScriptableObject
    {
        [StringInList("A", "B", "C")]
        public string firstField;

        [StringInList("X", "Y", "Z")]
        public string secondField;

        [StringInList("One", "Two", "Three")]
        public int thirdFieldIndex;
    }
#endif
}
