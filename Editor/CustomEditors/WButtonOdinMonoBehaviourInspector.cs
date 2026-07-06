// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;

    /// <summary>
    /// Custom editor for Odin Inspector's SerializedMonoBehaviour that adds WButton and WGroup support.
    /// This editor takes precedence over Odin's default editor when the package-owned Odin symbol is defined.
    /// </summary>
    [CustomEditor(typeof(SerializedMonoBehaviour), editorForChildClasses: true)]
    [CanEditMultipleObjects]
    public sealed class WButtonOdinMonoBehaviourInspector : Editor
    {
        private readonly Dictionary<WButtonGroupKey, WButtonPaginationState> _paginationStates =
            new();
        private readonly Dictionary<WButtonGroupKey, bool> _foldoutStates = new();
        private readonly Dictionary<int, bool> _groupFoldoutStates = new();

        public override void OnInspectorGUI()
        {
            WButtonOdinInspectorHelper.DrawInspectorGUI(
                this,
                _paginationStates,
                _foldoutStates,
                _groupFoldoutStates
            );
        }
    }
#endif
}
