// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.InLineEditor
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR

    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WInLineEditor with scrolling disabled.
    /// </summary>
    internal sealed class OdinInlineEditorNoScrollingTarget : SerializedScriptableObject
    {
        [WInLineEditor(enableScrolling: false)]
        public OdinReferencedScriptableObject noScrollingReference;
    }

#endif
}
