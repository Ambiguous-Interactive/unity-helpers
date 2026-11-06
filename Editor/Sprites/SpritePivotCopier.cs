// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Copies Single-mode sprite pivots using configurable counterpart names.</summary>
    public sealed class SpritePivotCopier : EditorWindow
    {
        private const string CopyOperationTitle = "Copy Sprite Pivots";
        private const string WindowTitle = "Sprite Pivot Copier";
        private const string FolderDialogTitle = "Choose Sprite Folder";
        private const string CopyButtonLabel = "Copy Pivots";
        private const string DirectoryLabelText = "Sprite Folder";
        private const string MarkerLabelText = "Source Name Marker";
        private const string ReplacementLabelText = "Replace Marker With";
        private const string UndoNotice =
            "Copies effective pivots between Single-mode sprites. Importer Undo is recorded; reimport and file effects are not fully reversible.";

        private const string MenuPath = "Tools/Wallstop Studios/Unity Helpers/Sprite Pivot Copier";
        private const int MenuPriority = -1;
        private static readonly Func<int, int, string, bool> ProgressCallback = ShowCopyProgress;

        private static readonly GUIContent DirectoryLabel = new(DirectoryLabelText);
        private static readonly GUIContent MarkerLabel = new(MarkerLabelText);
        private static readonly GUIContent ReplacementLabel = new(ReplacementLabelText);

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
        [MenuItem(MenuPath, priority = MenuPriority)]
        public static void ShowWindow()
        {
            GetWindow<SpritePivotCopier>(WindowTitle);
        }

        private static bool ShowCopyProgress(int processed, int total, string path)
        {
            float progress = 0 < total ? (float)processed / total : 0f;
            return EditorUi.CancelableProgress(CopyOperationTitle, path, progress);
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
                FolderDialogTitle,
                DirectoryLabel
            );
            EditorGUILayout.PropertyField(_markerProperty, MarkerLabel);
            EditorGUILayout.PropertyField(_replacementProperty, ReplacementLabel);
            _serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(UndoNotice, MessageType.Info);
            bool markerMissing = string.IsNullOrWhiteSpace(_marker);
            using (new EditorGUI.DisabledScope(markerMissing))
            {
                if (GUILayout.Button(CopyButtonLabel))
                {
                    using UndoGroupScope undoScope = UndoGroupScope.Begin(CopyOperationTitle);
                    if (!undoScope.IsHeld)
                    {
                        _lastSucceeded = false;
                        _lastMessage = "Could not begin the pivot Undo group.";
                        return;
                    }
                    using DisposableScope progressScope = EditorUi.BeginProgressScope();
                    _lastSucceeded = SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                        _directoryPath,
                        out int changed,
                        out string error,
                        _marker,
                        _replacement,
                        ProgressCallback
                    );
                    _lastMessage = _lastSucceeded
                        ? $"Updated {changed} sprite pivots."
                        : $"Updated {changed} sprite pivots. {error}";
                }
            }
            if (!string.IsNullOrWhiteSpace(_lastMessage))
            {
                MessageType messageType = _lastSucceeded ? MessageType.Info : MessageType.Error;
                EditorGUILayout.HelpBox(_lastMessage, messageType);
            }
        }
    }
#endif
}
