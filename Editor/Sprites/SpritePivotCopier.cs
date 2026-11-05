// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>Copies Single-mode sprite pivots using configurable counterpart names.</summary>
    public sealed class SpritePivotCopier : EditorWindow
    {
        private static readonly GUIContent DirectoryLabel = new("Sprite Folder");
        private static readonly GUIContent MarkerLabel = new("Source Name Marker");
        private static readonly GUIContent ReplacementLabel = new("Replace Marker With");

        [SerializeField]
        private string _directoryPath = "Assets";

        [SerializeField]
        private string _marker = SpritePivotCopierAPI.DefaultMarker;

        [SerializeField]
        private string _replacement = SpritePivotCopierAPI.DefaultReplacement;
        private SerializedObject _serializedObject;
        private SerializedProperty _directoryProperty;
        private SerializedProperty _markerProperty;
        private SerializedProperty _replacementProperty;
        private string _lastMessage;
        private bool _lastSucceeded;

        /// <summary>Opens the sprite pivot copier window.</summary>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sprite Pivot Copier", priority = -1)]
        public static void ShowWindow()
        {
            GetWindow<SpritePivotCopier>("Sprite Pivot Copier");
        }

        private static bool ShowCopyProgress(int processed, int total, string path)
        {
            return EditorUi.CancelableProgress(
                "Copy Sprite Pivots",
                path,
                0 < total ? (float)processed / total : 0f
            );
        }

        private void OnEnable()
        {
            _serializedObject = new SerializedObject(this);
            _directoryProperty = _serializedObject.FindProperty(nameof(_directoryPath));
            _markerProperty = _serializedObject.FindProperty(nameof(_marker));
            _replacementProperty = _serializedObject.FindProperty(nameof(_replacement));
        }

        private void OnDisable()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;
            _directoryProperty = null;
            _markerProperty = null;
            _replacementProperty = null;
        }

        private void OnGUI()
        {
            if (_serializedObject == null)
            {
                OnEnable();
            }
            _serializedObject.Update();
            PersistentDirectoryGUI.PathSelectorString(
                _directoryProperty,
                nameof(SpritePivotCopier),
                "Choose Sprite Folder",
                DirectoryLabel
            );
            EditorGUILayout.PropertyField(_markerProperty, MarkerLabel);
            EditorGUILayout.PropertyField(_replacementProperty, ReplacementLabel);
            _serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "Copies effective pivots between Single-mode sprites. Importer Undo is recorded; reimport and file effects are not fully reversible.",
                MessageType.Info
            );
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_marker)))
            {
                if (GUILayout.Button("Copy Pivots"))
                {
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Copy Sprite Pivots");
                    try
                    {
                        _lastSucceeded = SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                            _directoryPath,
                            out int changed,
                            out string error,
                            _marker,
                            _replacement,
                            ShowCopyProgress
                        );
                        _lastMessage = _lastSucceeded
                            ? $"Updated {changed} sprite pivots."
                            : $"Updated {changed} sprite pivots. {error}";
                    }
                    finally
                    {
                        EditorUi.ClearProgress();
                        Undo.CollapseUndoOperations(group);
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(_lastMessage))
            {
                EditorGUILayout.HelpBox(
                    _lastMessage,
                    _lastSucceeded ? MessageType.Info : MessageType.Error
                );
            }
        }
    }
#endif
}
