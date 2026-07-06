// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.InLineEditor
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR

    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WInLineEditor with nested inline editors.
    /// </summary>
    internal sealed class OdinInlineEditorNestedTarget : SerializedScriptableObject
    {
        [WInLineEditor]
        public OdinInlineEditorScriptableObjectTarget nestedReference;
    }

#endif
}
