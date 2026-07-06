// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.InLineEditor
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR

    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WInLineEditor with all options combined.
    /// </summary>
    internal sealed class OdinInlineEditorAllOptionsTarget : SerializedScriptableObject
    {
        [WInLineEditor(
            mode: WInLineEditorMode.FoldoutExpanded,
            inspectorHeight: 250f,
            drawPreview: true,
            previewHeight: 96f,
            drawObjectField: true,
            drawHeader: true,
            enableScrolling: true
        )]
        public OdinReferencedScriptableObject allOptionsReference;
    }

#endif
}
