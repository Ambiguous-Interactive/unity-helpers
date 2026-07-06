// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.InLineEditor
{
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WInLineEditor on SerializedMonoBehaviour.
    /// </summary>
    public sealed class OdinInlineEditorMonoBehaviourTarget : SerializedMonoBehaviour
    {
        [WInLineEditor]
        public Material referencedMaterial;
    }
#endif
}
