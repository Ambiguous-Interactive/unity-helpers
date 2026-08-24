// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable HeapView.CanAvoidClosure
namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Utils;
    using WallstopStudios.UnityHelpers.Visuals;
    using WallstopStudios.UnityHelpers.Visuals.UIToolkit;
    using Object = UnityEngine.Object;

    /// <summary>
    /// UI Toolkit-based multi-clip 2D animation viewer and lightweight editor. Load multiple
    /// AnimationClips, preview layered sprite animation, reorder frames via drag & drop, adjust
    /// preview FPS, and save an updated clip back to disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: quickly auditing and tweaking sprite-based clips without opening the
    /// full Animation window workflow; comparing multiple clips; and adjusting timing visually.
    /// </para>
    /// <para>
    /// How it works: for a selected clip, the tool resolves the <see cref="SpriteRenderer"/>
    /// binding path and extracts its frames. The frames list supports reordering via drag & drop
    /// with placeholders for clarity. Preview uses an in-editor <c>LayeredImage</c> to animate the
    /// sprite sequence at the chosen FPS.
    /// </para>
    /// <para>
    /// Usage:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Open via menu: Tools/Wallstop Studios/Unity Helpers/Sprite Animation Editor.</description></item>
    /// <item><description>Add clips (object field or project selection button).</description></item>
    /// <item><description>Drag frames to reorder, then Save to write an updated clip.</description></item>
    /// </list>
    /// <para>
    /// Pros: intuitive drag/drop, live preview, handles multiple clips in a session.
    /// Caveats: operates on SpriteRenderer curves only; saving overwrites the target clip asset.
    /// </para>
    /// </remarks>
    public sealed class AnimationViewerWindow : EditorWindow
    {
        private const float DragThresholdSqrMagnitude = 10f * 10f;
        private const int InvalidPointerId = -1;
        private const string DirToolName = "SpriteAnimationEditor";
        private const string DirContextKey = "Clips";

        internal sealed class EditorLayerData
        {
            public AnimationClip SourceClip { get; }
            public List<Sprite> Sprites { get; }
            public string ClipName => SourceClip != null ? SourceClip.name : "Unnamed Layer";
            public float OriginalClipFps { get; }
            public string BindingPath { get; }

            public EditorLayerData(AnimationClip clip)
            {
                SourceClip = clip;
                Sprites = new List<Sprite>();
                if (clip != null)
                {
                    foreach (Sprite s in clip.GetSpritesFromClip())
                    {
                        if (s != null)
                        {
                            Sprites.Add(s);
                        }
                    }
                }
                OriginalClipFps =
                    0 < clip.frameRate ? clip.frameRate : AnimatedSpriteLayer.FrameRate;

                BindingPath = string.Empty;
                if (SourceClip != null)
                {
                    foreach (
                        EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(
                            SourceClip
                        )
                    )
                    {
                        if (
                            binding.type == typeof(SpriteRenderer)
                            && string.Equals(
                                binding.propertyName,
                                UnityExtensions.SpriteBindingProperty,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            BindingPath = binding.path;
                            break;
                        }
                    }
                }
            }
        }

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sprite Animation Editor")]
        public static void ShowWindow()
        {
            AnimationViewerWindow wnd = GetWindow<AnimationViewerWindow>();
            wnd.titleContent = new GUIContent("2D Animation Viewer");
            wnd.minSize = new Vector2(750, 500);
        }

        private VisualTreeAsset _visualTree;
        private StyleSheet _styleSheet;

        private ObjectField _addAnimationClipField;
        private Button _browseAndAddButton;
        private FloatField _fpsField;
        private Button _applyFpsButton;
        private Button _saveClipButton;
        private VisualElement _loadedClipsContainer;
        private VisualElement _previewPanelHost;
        private LayeredImage _animationPreview;
        private VisualElement _framesContainer;
        private Label _fpsDebugLabel;
        private Label _framesPanelTitle;
        private MultiFileSelectorElement _fileSelector;

        private readonly List<EditorLayerData> _loadedEditorLayers = new();

        private EditorLayerData _activeEditorLayer;
        private float _currentPreviewFps = AnimatedSpriteLayer.FrameRate;

        private VisualElement _draggedFrameElement;
        private int _draggedFrameOriginalDataIndex;
        private VisualElement _frameDropPlaceholder;

        private VisualElement _draggedLoadedClipElement;
        private int _draggedLoadedClipOriginalIndex;
        private VisualElement _loadedClipDropPlaceholder;

        private bool _isClipDragPending;
        private Vector3 _clipDragStartPosition;
        private VisualElement _clipDragPendingElement;
        private int _clipDragPendingOriginalIndex;

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            TryLoadStyleSheets();

            if (_visualTree == null)
            {
                root.Add(new Label("Error: AnimationViewer.uxml not found."));
                return;
            }
            if (_styleSheet != null)
            {
                root.styleSheets.Add(_styleSheet);
            }

            _visualTree.CloneTree(root);

            _addAnimationClipField = root.Q<ObjectField>("addAnimationClipField");
            _browseAndAddButton = root.Q<Button>("browseAndAddButton");
            _fpsField = root.Q<FloatField>("fpsField");
            _applyFpsButton = root.Q<Button>("applyFpsButton");
            _saveClipButton = root.Q<Button>("saveClipButton");
            _loadedClipsContainer = root.Q<VisualElement>("loadedClipsContainer");
            _previewPanelHost = root.Q<VisualElement>("preview-panel");
            _framesContainer = root.Q<VisualElement>("framesContainer");
            _fpsDebugLabel = root.Q<Label>("fpsDebugLabel");
            _framesPanelTitle = root.Q<Label>("framesPanelTitle");

            _previewPanelHost.AddToClassList("animation-preview-container");

            _fpsField.value = _currentPreviewFps;
            _saveClipButton.SetEnabled(false);

            _addAnimationClipField.RegisterValueChangedCallback(OnAddAnimationClipFieldChanged);
            _browseAndAddButton.text = "Add Selected Clips from Project";
            _browseAndAddButton.clicked -= OnBrowseAndAddClicked;
            if (_browseAndAddButton != null)
            {
                _browseAndAddButton.text = "Browse Clips (Multi)...";
                _browseAndAddButton.clicked -= OnAddSelectedClipsFromProjectClicked;
                _browseAndAddButton.clicked += ToggleMultiFileSelector;
            }
            else
            {
                this.LogError(
                    $"'browseAndAddButton' not found in UXML. Multi-file browser cannot be triggered."
                );
            }
            _applyFpsButton.clicked += OnApplyFpsToPreviewClicked;
            _saveClipButton.clicked += OnSaveClipClicked;

            _frameDropPlaceholder = new VisualElement();
            _frameDropPlaceholder.AddToClassList("drop-placeholder");
            _frameDropPlaceholder.style.height = 5;
            _frameDropPlaceholder.style.visibility = Visibility.Hidden;

            _framesContainer.RegisterCallback<DragUpdatedEvent>(OnFramesContainerDragUpdated);
            _framesContainer.RegisterCallback<DragPerformEvent>(OnFramesContainerDragPerform);
            _framesContainer.RegisterCallback<DragLeaveEvent>(OnFramesContainerDragLeave);

            _loadedClipDropPlaceholder = new VisualElement();
            _loadedClipDropPlaceholder.AddToClassList("drop-placeholder");
            _loadedClipDropPlaceholder.style.height = 5;
            _loadedClipDropPlaceholder.style.visibility = Visibility.Hidden;

            _loadedClipsContainer.RegisterCallback<DragUpdatedEvent>(
                OnLoadedClipsContainerDragUpdated
            );
            _loadedClipsContainer.RegisterCallback<DragPerformEvent>(
                OnLoadedClipsContainerDragPerform
            );
            _loadedClipsContainer.RegisterCallback<DragLeaveEvent>(OnLoadedClipsContainerDragLeave);

            UpdateFramesPanelTitle();
            RebuildLoadedClipsUI();
            RecreatePreviewImage();
        }

        private void Update()
        {
            _animationPreview?.Update();
        }

        private void TryLoadStyleSheets()
        {
            // Load the stylesheet using DirectoryHelper which handles all package locations
            string styleSheetPath = DirectoryHelper.ResolvePackageAssetPath(
                "Editor/Styles/AnimationViewer.uss"
            );

            if (!string.IsNullOrWhiteSpace(styleSheetPath))
            {
                _styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath);
                if (_styleSheet == null)
                {
                    this.LogError(
                        $"Failed to load Animation Viewer stylesheet at resolved path: '{styleSheetPath}'."
                    );
                }
            }
            else
            {
                this.LogError(
                    $"Failed to resolve Animation Viewer stylesheet path. Package root could not be determined."
                );
            }

            // Load the visual tree asset
            string visualTreePath = DirectoryHelper.ResolvePackageAssetPath(
                "Editor/Styles/AnimationViewer.uxml"
            );

            if (!string.IsNullOrWhiteSpace(visualTreePath))
            {
                _visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(visualTreePath);
                if (_visualTree == null)
                {
                    this.LogError(
                        $"Failed to load Animation Viewer visual tree at resolved path: '{visualTreePath}'."
                    );
                }
            }
            else
            {
                this.LogError(
                    $"Failed to resolve Animation Viewer visual tree path. Package root could not be determined."
                );
            }
        }

        private void ToggleMultiFileSelector()
        {
            VisualElement root = rootVisualElement;
            if (_fileSelector == null)
            {
                _fileSelector = new MultiFileSelectorElement(
                    GetLastAnimationDirectory(),
                    new[] { ".anim" }
                );
                _fileSelector.OnFilesSelectedReadOnly += HandleFilesSelectedFromCustomBrowser;
                _fileSelector.OnCancelled += HideMultiFileSelector;
                root.Add(_fileSelector);
                if (1 < root.childCount)
                {
                    _fileSelector.PlaceInFront(root.Children().FirstOrDefault());
                }
            }
            else if (_fileSelector.parent == null)
            {
                _fileSelector.ResetAndShow(GetLastAnimationDirectory());
                root.Add(_fileSelector);
                if (1 < root.childCount)
                {
                    _fileSelector.PlaceInFront(root.Children().FirstOrDefault());
                }
            }
            else
            {
                HideMultiFileSelector();
            }
        }

        private void HideMultiFileSelector()
        {
            if (_fileSelector is { parent: not null })
            {
                _fileSelector.parent.Remove(_fileSelector);
            }
        }

        private void HandleFilesSelectedFromCustomBrowser(
            IReadOnlyCollection<string> selectedFullPaths
        )
        {
            HideMultiFileSelector();

            if (selectedFullPaths == null || selectedFullPaths.Count == 0)
            {
                return;
            }

            int clipsAddedCount = 0;
            string lastValidDirectory = null;

            foreach (string fullPath in selectedFullPaths)
            {
                string assetPath = fullPath.SanitizePath();
                if (!assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        assetPath.StartsWith(
                            Application.dataPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        this.LogWarn(
                            $"Selected file '{fullPath}' is outside the project's Assets folder or path is not project-relative. Skipping."
                        );
                        continue;
                    }
                }

                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip != null)
                {
                    bool noneMatch = true;
                    for (int i = 0; i < _loadedEditorLayers.Count; i++)
                    {
                        if (_loadedEditorLayers[i]?.SourceClip == clip)
                        {
                            noneMatch = false;
                            break;
                        }
                    }
                    if (noneMatch)
                    {
                        AddEditorLayer(clip);
                        clipsAddedCount++;
                        lastValidDirectory = Path.GetDirectoryName(assetPath);
                    }
                    else
                    {
                        this.LogWarn($"Clip '{clip.name}' already loaded. Skipping.");
                    }
                }
                else
                {
                    this.LogWarn($"Could not load AnimationClip from: {assetPath}");
                }
            }

            if (0 < clipsAddedCount)
            {
                this.Log($"Added {clipsAddedCount} clip(s).");
                if (!string.IsNullOrWhiteSpace(lastValidDirectory))
                {
                    RecordLastAnimationDirectory(lastValidDirectory);
                }
            }
        }

        private void OnAddAnimationClipFieldChanged(ChangeEvent<Object> evt)
        {
            AnimationClip clip = evt.newValue as AnimationClip;
            if (clip != null)
            {
                AddEditorLayer(clip);
                _addAnimationClipField.SetValueWithoutNotify(null);
            }
        }

        private void OnAddSelectedClipsFromProjectClicked()
        {
            Object[] selectedObjects = Selection.GetFiltered(
                typeof(AnimationClip),
                SelectionMode.Assets
            );

            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Utils.EditorUi.Info(
                    "No Clips Selected",
                    "Please select one or more AnimationClip assets in the Project window first."
                );
                return;
            }

            int clipsAddedCount = 0;
            foreach (Object obj in selectedObjects)
            {
                if (obj is AnimationClip clip)
                {
                    bool alreadyExists = false;
                    for (int i = 0; i < _loadedEditorLayers.Count; i++)
                    {
                        if (_loadedEditorLayers[i]?.SourceClip == clip)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    if (!alreadyExists)
                    {
                        AddEditorLayer(clip);
                        clipsAddedCount++;
                    }
                    else
                    {
                        this.LogWarn($"Clip '{clip.name}' is already loaded. Skipping.");
                    }
                }
            }

            if (0 < clipsAddedCount)
            {
                this.Log($"Added {clipsAddedCount} new AnimationClip(s) to the viewer.");
            }
            else if (0 < selectedObjects.Length)
            {
                this.Log($"All selected AnimationClips were already loaded.");
            }
        }

        private void OnBrowseAndAddClicked()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Select Animation Clip to Add",
                GetLastAnimationDirectory(),
                new[] { "Animation Clip", "anim" }
            );

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (path.StartsWith(Application.dataPath))
                {
                    path = "Assets" + path.Substring(Application.dataPath.Length);
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    RecordLastAnimationDirectory(dir);
                }

                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null)
                {
                    AddEditorLayer(clip);
                }
            }
        }

        private static string GetLastAnimationDirectory()
        {
            try
            {
                PersistentDirectorySettings settings = PersistentDirectorySettings.Instance;
                DirectoryUsageData[] paths =
                    settings != null
                        ? settings.GetPaths(DirToolName, DirContextKey, topOnly: true, topN: 1)
                        : Array.Empty<DirectoryUsageData>();
                string candidate = paths is { Length: > 0 } ? paths[0]?.path : null;

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return "Assets";
                }

                // Prefer Assets-relative paths for UI components that expect them
                if (!candidate.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    string assetsRoot = Application.dataPath.SanitizePath();
                    string full = candidate.SanitizePath();
                    if (full.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = "Assets" + full.Substring(assetsRoot.Length);
                    }
                }

                return string.IsNullOrWhiteSpace(candidate) ? "Assets" : candidate;
            }
            catch
            {
                return "Assets";
            }
        }

        private static void RecordLastAnimationDirectory(string assetsRelativeDir)
        {
            if (string.IsNullOrWhiteSpace(assetsRelativeDir))
            {
                return;
            }

            // Ensure Assets-relative if possible
            string path = assetsRelativeDir.SanitizePath();
            if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                string assetsRoot = Application.dataPath.SanitizePath();
                string full = path;
                if (full.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    path = "Assets" + full.Substring(assetsRoot.Length);
                }
            }

            PersistentDirectorySettings settings = PersistentDirectorySettings.Instance;
            if (settings != null)
            {
                settings.RecordPath(DirToolName, DirContextKey, path);
            }
        }

        private void AddEditorLayer(AnimationClip clip)
        {
            bool exists = false;
            for (int i = 0; i < _loadedEditorLayers.Count; i++)
            {
                if (_loadedEditorLayers[i]?.SourceClip == clip)
                {
                    exists = true;
                    break;
                }
            }
            if (clip == null || exists)
            {
                string clipName = clip != null ? clip.name : "<null>";
                this.LogWarn($"Clip '{clipName}' is null or already loaded.");
                return;
            }

            EditorLayerData newEditorLayer = new(clip);
            _loadedEditorLayers.Add(newEditorLayer);

            if (_activeEditorLayer == null && 0 < newEditorLayer.Sprites.Count)
            {
                SetActiveEditorLayer(newEditorLayer);
            }
            else if (_activeEditorLayer == null)
            {
                SetActiveEditorLayer(newEditorLayer);
            }

            RebuildLoadedClipsUI();
            RecreatePreviewImage();
        }

        private void RemoveEditorLayer(EditorLayerData layerToRemove)
        {
            if (layerToRemove == null)
            {
                return;
            }

            _loadedEditorLayers.Remove(layerToRemove);

            if (_activeEditorLayer == layerToRemove)
            {
                SetActiveEditorLayer(0 < _loadedEditorLayers.Count ? _loadedEditorLayers[0] : null);
            }

            RebuildLoadedClipsUI();
            RecreatePreviewImage();
        }

        private void SetActiveEditorLayer(EditorLayerData layer)
        {
            _activeEditorLayer = layer;
            _framesContainer.Clear();
            _fpsDebugLabel.text = "Detected FPS Info (Active Clip):";

            if (_activeEditorLayer != null)
            {
                UpdateFpsDebugLabelForActiveLayer();
                _saveClipButton.SetEnabled(_activeEditorLayer.SourceClip != null);
            }
            else
            {
                _saveClipButton.SetEnabled(false);
            }

            RebuildFramesListUI();
            RebuildLoadedClipsUI();
            UpdateFramesPanelTitle();
        }

        private void UpdateFramesPanelTitle()
        {
            _framesPanelTitle.text =
                _activeEditorLayer != null
                    ? $"Frames (Editing: {_activeEditorLayer.ClipName})"
                    : "Frames (No Active Clip Selected)";
        }

        private void RebuildLoadedClipsUI()
        {
            _loadedClipsContainer.Clear();
            if (_loadedClipDropPlaceholder.parent == _loadedClipsContainer)
            {
                _loadedClipsContainer.Remove(_loadedClipDropPlaceholder);
            }

            for (int i = 0; i < _loadedEditorLayers.Count; i++)
            {
                EditorLayerData editorLayer = _loadedEditorLayers[i];

                VisualElement itemElement = new();
                itemElement.AddToClassList("loaded-clip-item");
                if (editorLayer == _activeEditorLayer)
                {
                    itemElement.AddToClassList("loaded-clip-item--active");
                }
                itemElement.userData = i;

                Label label = new(editorLayer.ClipName);
                itemElement.Add(label);

                Button removeButton = new(() => RemoveEditorLayer(editorLayer)) { text = "X" };
                itemElement.Add(removeButton);

                itemElement.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0 && _draggedLoadedClipElement == null)
                    {
                        SetActiveEditorLayer(editorLayer);
                    }
                });

                int currentIndex = i;
                itemElement.RegisterCallback<PointerDownEvent>(evt =>
                    OnLoadedClipItemPointerDownSetup(evt, itemElement, currentIndex)
                );
                itemElement.RegisterCallback<PointerMoveEvent>(OnLoadedClipItemPointerMove);
                itemElement.RegisterCallback<PointerUpEvent>(evt =>
                    OnLoadedClipItemPointerUpForClick(evt, itemElement, currentIndex)
                );

                _loadedClipsContainer.Add(itemElement);
            }
        }

        private void OnLoadedClipItemPointerDownSetup(
            PointerDownEvent evt,
            VisualElement clipElement,
            int originalListIndex
        )
        {
            if (evt.button != 0 || _draggedLoadedClipElement != null || _isClipDragPending)
            {
                return;
            }

            _isClipDragPending = true;
            _clipDragPendingElement = clipElement;
            _clipDragPendingOriginalIndex = originalListIndex;
            _clipDragStartPosition = evt.position;

            clipElement.CapturePointer(evt.pointerId);

            evt.StopPropagation();
        }

        private void OnLoadedClipItemPointerMove(PointerMoveEvent evt)
        {
            if (!_isClipDragPending)
            {
                return;
            }

            float diffSqrMagnitude = (evt.position - _clipDragStartPosition).sqrMagnitude;

            if (DragThresholdSqrMagnitude <= diffSqrMagnitude)
            {
                _draggedLoadedClipElement = _clipDragPendingElement;
                _draggedLoadedClipOriginalIndex = _clipDragPendingOriginalIndex;

                _draggedLoadedClipElement.AddToClassList("frame-item-dragged");

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(
                    "DraggedLoadedClipIndex",
                    _draggedLoadedClipOriginalIndex
                );
                Object dragContextObject = _loadedEditorLayers[
                    _draggedLoadedClipOriginalIndex
                ]?.SourceClip;
                if (dragContextObject == null)
                {
                    dragContextObject = CreateInstance<ScriptableObject>();
                }
                DragAndDrop.objectReferences = new[] { dragContextObject };
                DragAndDrop.StartDrag(
                    _loadedEditorLayers[_draggedLoadedClipOriginalIndex].ClipName
                        ?? "Dragging Layer"
                );

                _isClipDragPending = false;
            }
        }

        private void OnLoadedClipItemPointerUpForClick(
            PointerUpEvent evt,
            VisualElement clipElement,
            int listIndex
        )
        {
            if (evt.button != 0)
            {
                return;
            }

            if (clipElement.HasPointerCapture(evt.pointerId))
            {
                clipElement.ReleasePointer(evt.pointerId);
            }
            if (_isClipDragPending)
            {
                if (0 <= listIndex && listIndex < _loadedEditorLayers.Count)
                {
                    SetActiveEditorLayer(_loadedEditorLayers[listIndex]);
                }
                _isClipDragPending = false;
                _clipDragPendingElement = null;
                evt.StopPropagation();
            }
            else if (_draggedLoadedClipElement == _clipDragPendingElement)
            {
                if (DragAndDrop.GetGenericData("DraggedLoadedClipIndex") != null)
                {
                    CleanupLoadedClipDragState(evt.pointerId);
                }
                evt.StopPropagation();
            }

            _isClipDragPending = false;
            _clipDragPendingElement = null;
        }

        private void OnDraggedLoadedClipItemPointerUp(PointerUpEvent evt)
        {
            if (_draggedLoadedClipElement == null || evt.currentTarget != _draggedLoadedClipElement)
            {
                return;
            }

            if (
                DragAndDrop.GetGenericData("DraggedLoadedClipIndex") != null
                || _draggedLoadedClipElement != null
                    && _draggedLoadedClipElement.HasPointerCapture(evt.pointerId)
            )
            {
                CleanupLoadedClipDragState(evt.pointerId);
            }

            evt.StopPropagation();
        }

        private void OnLoadedClipsContainerDragUpdated(DragUpdatedEvent evt)
        {
            object draggedIndexData = DragAndDrop.GetGenericData("DraggedLoadedClipIndex");
            if (draggedIndexData != null && _draggedLoadedClipElement != null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                float mouseY = evt.localMousePosition.y;
                int newVisualIndex = -1;

                if (_loadedClipDropPlaceholder.parent == _loadedClipsContainer)
                {
                    _loadedClipsContainer.Remove(_loadedClipDropPlaceholder);
                }

                for (int i = 0; i < _loadedClipsContainer.childCount; i++)
                {
                    VisualElement child = _loadedClipsContainer[i];
                    if (child == _draggedLoadedClipElement)
                    {
                        continue;
                    }

                    float childMidY = child.layout.yMin + child.layout.height / 2f;
                    if (mouseY < childMidY)
                    {
                        newVisualIndex = i;
                        break;
                    }
                }
                if (
                    newVisualIndex < 0
                    && 0 < _loadedClipsContainer.childCount
                    && _draggedLoadedClipElement
                        != _loadedClipsContainer.ElementAt(_loadedClipsContainer.childCount - 1)
                )
                {
                    newVisualIndex = _loadedClipsContainer.childCount;
                }

                if (0 <= newVisualIndex)
                {
                    _loadedClipsContainer.Insert(newVisualIndex, _loadedClipDropPlaceholder);
                    _loadedClipDropPlaceholder.style.visibility = Visibility.Visible;
                }
                else if (_loadedClipsContainer.childCount == 0 && _draggedLoadedClipElement != null)
                {
                    _loadedClipsContainer.Add(_loadedClipDropPlaceholder);
                    _loadedClipDropPlaceholder.style.visibility = Visibility.Visible;
                }
                else
                {
                    if (_loadedClipDropPlaceholder.parent == _loadedClipsContainer)
                    {
                        _loadedClipsContainer.Remove(_loadedClipDropPlaceholder);
                    }

                    _loadedClipDropPlaceholder.style.visibility = Visibility.Hidden;
                }
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }
            evt.StopPropagation();
        }

        private void OnLoadedClipsContainerDragPerform(DragPerformEvent evt)
        {
            object draggedIndexData = DragAndDrop.GetGenericData("DraggedLoadedClipIndex");
            if (draggedIndexData != null && _draggedLoadedClipElement != null)
            {
                int originalListIndex = (int)draggedIndexData;

                if (originalListIndex < 0 || _loadedEditorLayers.Count <= originalListIndex)
                {
                    this.LogError(
                        $"DragPerform (LoadedClips): Stale or invalid dragged index. Aborting drop."
                    );
                    CleanupLoadedClipDragState(InvalidPointerId);
                    evt.StopPropagation();
                    return;
                }

                EditorLayerData movedLayer = _loadedEditorLayers[originalListIndex];
                _loadedEditorLayers.RemoveAt(originalListIndex);

                int placeholderVisualIndex = _loadedClipsContainer.IndexOf(
                    _loadedClipDropPlaceholder
                );
                int targetListIndex;

                if (0 <= placeholderVisualIndex)
                {
                    int itemsBeforePlaceholder = -1;
                    for (int i = 0; i < placeholderVisualIndex; i++)
                    {
                        if (_loadedClipsContainer[i] != _loadedClipDropPlaceholder)
                        {
                            itemsBeforePlaceholder++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    targetListIndex = itemsBeforePlaceholder;
                }
                else
                {
                    targetListIndex = _loadedEditorLayers.Count;
                }
                targetListIndex = Mathf.Clamp(targetListIndex, 0, _loadedEditorLayers.Count);
                _loadedEditorLayers.Insert(targetListIndex, movedLayer);

                DragAndDrop.AcceptDrag();
                CleanupLoadedClipDragState(InvalidPointerId);

                RebuildLoadedClipsUI();
                RecreatePreviewImage();
            }
            else
            {
                if (DragAndDrop.GetGenericData("DraggedLoadedClipIndex") != null)
                {
                    CleanupLoadedClipDragState(InvalidPointerId);
                }
            }
            evt.StopPropagation();
        }

        private void OnLoadedClipsContainerDragLeave(DragLeaveEvent evt)
        {
            if (evt.target == _loadedClipsContainer)
            {
                if (_loadedClipDropPlaceholder.parent == _loadedClipsContainer)
                {
                    _loadedClipsContainer.Remove(_loadedClipDropPlaceholder);
                }

                if (_loadedClipDropPlaceholder != null)
                {
                    _loadedClipDropPlaceholder.style.visibility = Visibility.Hidden;
                }
            }
        }

        private void CleanupLoadedClipDragState(int pointerIdToRelease)
        {
            if (_draggedLoadedClipElement != null)
            {
                if (
                    pointerIdToRelease != InvalidPointerId
                    && _draggedLoadedClipElement.HasPointerCapture(pointerIdToRelease)
                )
                {
                    _draggedLoadedClipElement.ReleasePointer(pointerIdToRelease);
                }

                _draggedLoadedClipElement.UnregisterCallback<PointerUpEvent>(
                    OnDraggedLoadedClipItemPointerUp
                );

                _draggedLoadedClipElement.RemoveFromClassList("frame-item-dragged");
                _draggedLoadedClipElement = null;
            }
            _draggedLoadedClipOriginalIndex = -1;

            _isClipDragPending = false;
            if (_clipDragPendingElement != null)
            {
                if (
                    pointerIdToRelease != InvalidPointerId
                    && _clipDragPendingElement.HasPointerCapture(pointerIdToRelease)
                )
                {
                    _clipDragPendingElement.ReleasePointer(pointerIdToRelease);
                }

                _clipDragPendingElement = null;
            }

            if (_loadedClipDropPlaceholder != null)
            {
                if (_loadedClipDropPlaceholder.parent == _loadedClipsContainer)
                {
                    _loadedClipsContainer.Remove(_loadedClipDropPlaceholder);
                }

                _loadedClipDropPlaceholder.style.visibility = Visibility.Hidden;
            }
            DragAndDrop.SetGenericData("DraggedLoadedClipIndex", null);
        }

        private void RecreatePreviewImage()
        {
            if (_animationPreview != null)
            {
                if (_animationPreview.parent == _previewPanelHost)
                {
                    _previewPanelHost.Remove(_animationPreview);
                }

                _animationPreview = null;
            }

            if (_previewPanelHost == null)
            {
                return;
            }

            using PooledResource<List<AnimatedSpriteLayer>> layerLease =
                Buffers<AnimatedSpriteLayer>.List.Get(
                    out List<AnimatedSpriteLayer> animatedSpriteLayers
                );

            if (0 < _loadedEditorLayers.Count)
            {
                foreach (EditorLayerData editorLayer in _loadedEditorLayers)
                {
                    animatedSpriteLayers.Add(new AnimatedSpriteLayer(editorLayer.Sprites));
                }
            }

            _animationPreview = new LayeredImage(
                animatedSpriteLayers,
                Color.clear,
                _currentPreviewFps,
                updatesSelf: false
            )
            {
                name = "animationPreviewElement",
            };

            _previewPanelHost.Add(_animationPreview);
        }

        private void OnFramesContainerDragUpdated(DragUpdatedEvent evt)
        {
            object draggedIndexData = DragAndDrop.GetGenericData("DraggedFrameDataIndex");
            if (draggedIndexData != null && _draggedFrameElement != null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                float mouseY = evt.localMousePosition.y;
                int newVisualIndex = -1;

                if (_frameDropPlaceholder.parent == _framesContainer)
                {
                    _framesContainer.Remove(_frameDropPlaceholder);
                }

                for (int i = 0; i < _framesContainer.childCount; i++)
                {
                    VisualElement child = _framesContainer[i];
                    if (child == _draggedFrameElement)
                    {
                        continue;
                    }

                    float childMidY = child.layout.yMin + child.layout.height / 2f;
                    if (mouseY < childMidY)
                    {
                        newVisualIndex = i;
                        break;
                    }
                }
                if (
                    newVisualIndex < 0
                    && 0 < _framesContainer.childCount
                    && _draggedFrameElement
                        != _framesContainer.ElementAt(_framesContainer.childCount - 1)
                )
                {
                    newVisualIndex = _framesContainer.childCount;
                }

                if (0 <= newVisualIndex)
                {
                    _framesContainer.Insert(newVisualIndex, _frameDropPlaceholder);
                    _frameDropPlaceholder.style.visibility = Visibility.Visible;
                }
                else if (_framesContainer.childCount == 0 && _draggedFrameElement != null)
                {
                    _framesContainer.Add(_frameDropPlaceholder);
                    _frameDropPlaceholder.style.visibility = Visibility.Visible;
                }
                else
                {
                    if (_frameDropPlaceholder.parent == _framesContainer)
                    {
                        _framesContainer.Remove(_frameDropPlaceholder);
                    }

                    _frameDropPlaceholder.style.visibility = Visibility.Hidden;
                }
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }
            evt.StopPropagation();
        }

        private void OnFramesContainerDragPerform(DragPerformEvent evt)
        {
            object draggedIndexData = DragAndDrop.GetGenericData("DraggedFrameDataIndex");
            if (
                draggedIndexData != null
                && _draggedFrameElement != null
                && _activeEditorLayer != null
            )
            {
                int originalDataIndex = (int)draggedIndexData;

                if (originalDataIndex < 0 || _activeEditorLayer.Sprites.Count <= originalDataIndex)
                {
                    this.LogError(
                        $"DragPerform (Frames): Stale or invalid dragged index. Aborting drop."
                    );
                    CleanupFrameDragState(InvalidPointerId);
                    evt.StopPropagation();
                    return;
                }

                Sprite movedSprite = _activeEditorLayer.Sprites[originalDataIndex];
                _activeEditorLayer.Sprites.RemoveAt(originalDataIndex);

                int placeholderVisualIndex = _framesContainer.IndexOf(_frameDropPlaceholder);
                int targetDataIndex;

                if (0 <= placeholderVisualIndex)
                {
                    int itemsBeforePlaceholder = 0;
                    for (int i = 0; i < placeholderVisualIndex; i++)
                    {
                        if (
                            _framesContainer[i] != _draggedFrameElement
                            && _framesContainer[i] != _frameDropPlaceholder
                        )
                        {
                            itemsBeforePlaceholder++;
                        }
                    }
                    targetDataIndex = itemsBeforePlaceholder;
                }
                else
                {
                    targetDataIndex = _activeEditorLayer.Sprites.Count;
                }
                targetDataIndex = Mathf.Clamp(targetDataIndex, 0, _activeEditorLayer.Sprites.Count);
                _activeEditorLayer.Sprites.Insert(targetDataIndex, movedSprite);

                DragAndDrop.AcceptDrag();
                CleanupFrameDragState(InvalidPointerId);

                RebuildFramesListUI();
                RecreatePreviewImage();
            }
            else
            {
                if (DragAndDrop.GetGenericData("DraggedFrameDataIndex") != null)
                {
                    CleanupFrameDragState(InvalidPointerId);
                }
            }
            evt.StopPropagation();
        }

        private void OnFramesContainerDragLeave(DragLeaveEvent evt)
        {
            if (evt.target == _framesContainer)
            {
                if (_frameDropPlaceholder.parent == _framesContainer)
                {
                    _framesContainer.Remove(_frameDropPlaceholder);
                }

                if (_frameDropPlaceholder != null)
                {
                    _frameDropPlaceholder.style.visibility = Visibility.Hidden;
                }
            }

            CleanupFrameDragState(InvalidPointerId);
        }

        private void CleanupFrameDragState(int pointerIdToRelease)
        {
            this.Log($"Cleaning up frame drag state with pointer {pointerIdToRelease}");
            if (_draggedFrameElement != null)
            {
                if (
                    pointerIdToRelease != InvalidPointerId
                    && _draggedFrameElement.HasPointerCapture(pointerIdToRelease)
                )
                {
                    _draggedFrameElement.ReleasePointer(pointerIdToRelease);
                }

                _draggedFrameElement.UnregisterCallback<PointerUpEvent>(
                    OnDraggedFrameItemPointerUp
                );
                _draggedFrameElement.RemoveFromClassList("frame-item-dragged");
                _draggedFrameElement = null;
            }
            _draggedFrameOriginalDataIndex = -1;

            if (_frameDropPlaceholder != null)
            {
                if (_frameDropPlaceholder.parent == _framesContainer)
                {
                    _framesContainer.Remove(_frameDropPlaceholder);
                }

                _frameDropPlaceholder.style.visibility = Visibility.Hidden;
            }
            DragAndDrop.SetGenericData("DraggedFrameDataIndex", null);
        }

        private void UpdateFpsDebugLabelForActiveLayer()
        {
            if (_activeEditorLayer == null)
            {
                _fpsDebugLabel.text = "Detected FPS Info: No active clip.";
                return;
            }

            _fpsDebugLabel.text =
                $"Active Clip FPS (Original): {FormatFps(_activeEditorLayer.OriginalClipFps)}fps. Preview uses global FPS.";
        }

        private static string FormatFps(float fps)
        {
            return fps.ToString("F1");
        }

        private void RebuildFramesListUI()
        {
            _framesContainer.Clear();

            if (_frameDropPlaceholder != null && _frameDropPlaceholder.parent == _framesContainer)
            {
                _framesContainer.Remove(_frameDropPlaceholder);
            }

            if (_activeEditorLayer?.Sprites == null)
            {
                return;
            }

            for (int i = 0; i < _activeEditorLayer.Sprites.Count; i++)
            {
                Sprite sprite = _activeEditorLayer.Sprites[i];

                VisualElement frameElement = new();
                frameElement.AddToClassList("frame-item");

                Image frameImage = new() { sprite = sprite, scaleMode = ScaleMode.ScaleToFit };
                frameImage.AddToClassList("frame-image");
                frameElement.Add(frameImage);

                VisualElement frameInfo = new();
                frameInfo.AddToClassList("frame-info");
                frameInfo.Add(new Label($"Frame: {i + 1}"));
                frameInfo.Add(new Label($"Sprite: {(sprite != null ? sprite.name : "(None)")}"));
                frameElement.Add(frameInfo);

                IntegerField indexField = new(null) { value = i + 1 };
                indexField.AddToClassList("frame-index-field");
                frameElement.Add(indexField);

                VisualElement orderFieldContainer = new()
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        marginLeft = StyleKeyword.Auto,
                    },
                };

                Label orderLabel = new("Order:") { style = { marginRight = 3 } };
                orderFieldContainer.Add(orderLabel);
                orderFieldContainer.Add(indexField);
                frameElement.Add(orderFieldContainer);

                int currentDataIndex = i;
                frameElement.RegisterCallback<PointerDownEvent>(evt =>
                    OnFrameItemPointerDown(evt, frameElement, currentDataIndex)
                );

                indexField.userData = currentDataIndex;

                indexField.RegisterCallback<FocusInEvent>(_ =>
                {
                    CleanupFrameDragState(InvalidPointerId);
                    CleanupLoadedClipDragState(InvalidPointerId);
                });

                indexField.RegisterCallback<FocusOutEvent>(_ =>
                    OnFrameIndexFieldChanged(indexField)
                );
                indexField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                    {
                        OnFrameIndexFieldChanged(indexField);
                        indexField.Blur();
                    }
                });

                _framesContainer.Add(frameElement);
            }
        }

        private void OnFrameIndexFieldChanged(IntegerField field)
        {
            CleanupFrameDragState(InvalidPointerId);
            CleanupLoadedClipDragState(InvalidPointerId);
            if (_activeEditorLayer?.Sprites == null)
            {
                return;
            }

            int originalDataIndex = (int)field.userData;
            int newUiIndex = field.value;
            int newRequestedDataIndex = newUiIndex - 1;
            int newClampedDataIndex = Mathf.Clamp(
                newRequestedDataIndex,
                0,
                _activeEditorLayer.Sprites.Count - 1
            );

            if (newClampedDataIndex != originalDataIndex)
            {
                if (originalDataIndex < 0 || _activeEditorLayer.Sprites.Count <= originalDataIndex)
                {
                    this.LogWarn(
                        $"Original index {originalDataIndex} out of bounds. Rebuilding UI to correct."
                    );
                    RebuildFramesListUI();
                    return;
                }
                Sprite spriteToMove = _activeEditorLayer.Sprites[originalDataIndex];
                _activeEditorLayer.Sprites.RemoveAt(originalDataIndex);
                _activeEditorLayer.Sprites.Insert(newClampedDataIndex, spriteToMove);
                RebuildFramesListUI();
                RecreatePreviewImage();
            }
            else if (newUiIndex - 1 != newClampedDataIndex)
            {
                RebuildFramesListUI();
            }
        }

        private void OnFrameItemPointerDown(
            PointerDownEvent evt,
            VisualElement frameElement,
            int originalDataIndex
        )
        {
            if (evt.button != 0 || _draggedFrameElement != null)
            {
                return;
            }

            if (
                _activeEditorLayer == null
                || originalDataIndex < 0
                || _activeEditorLayer.Sprites.Count <= originalDataIndex
            )
            {
                this.LogError(
                    $"OnFrameItemPointerDown: Invalid originalDataIndex ({originalDataIndex}) or no active layer. Sprite count: {_activeEditorLayer?.Sprites?.Count ?? -1}"
                );
                return;
            }

            _draggedFrameElement = frameElement;
            _draggedFrameOriginalDataIndex = originalDataIndex;

            try
            {
                _draggedFrameElement.RegisterCallback<PointerUpEvent>(OnDraggedFrameItemPointerUp);
                _draggedFrameElement.AddToClassList("frame-item-dragged");

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("DraggedFrameDataIndex", _draggedFrameOriginalDataIndex);

                Object dragContextObject = _activeEditorLayer.SourceClip;
                if (dragContextObject == null)
                {
                    dragContextObject = CreateInstance<ScriptableObject>();
                }
                if (dragContextObject == null)
                {
                    this.LogError($"Failed to create dragContextObject for frame drag.");

                    _draggedFrameElement.ReleasePointer(evt.pointerId);
                    _draggedFrameElement.UnregisterCallback<PointerUpEvent>(
                        OnDraggedFrameItemPointerUp
                    );
                    _draggedFrameElement.RemoveFromClassList("frame-item-dragged");
                    _draggedFrameElement = null;
                    return;
                }
                DragAndDrop.objectReferences = new[] { dragContextObject };

                Sprite spriteBeingDragged = _activeEditorLayer.Sprites[originalDataIndex];
                string dragTitle;
                if (spriteBeingDragged != null)
                {
                    dragTitle = !string.IsNullOrWhiteSpace(spriteBeingDragged.name)
                        ? spriteBeingDragged.name
                        : $"Unnamed Sprite Frame {originalDataIndex + 1}";
                }
                else
                {
                    dragTitle = $"Empty Frame {originalDataIndex + 1}";
                }

                if (string.IsNullOrWhiteSpace(dragTitle))
                {
                    dragTitle = "Dragging Frame";
                }

                DragAndDrop.StartDrag(dragTitle);
            }
            catch (Exception e)
            {
                this.LogError($"Exception during OnFrameItemPointerDown before StartDrag", e);

                if (_draggedFrameElement != null)
                {
                    if (_draggedFrameElement.HasPointerCapture(evt.pointerId))
                    {
                        _draggedFrameElement.ReleasePointer(evt.pointerId);
                    }

                    _draggedFrameElement.UnregisterCallback<PointerUpEvent>(
                        OnDraggedFrameItemPointerUp
                    );
                    _draggedFrameElement.RemoveFromClassList("frame-item-dragged");
                    _draggedFrameElement = null;
                }
                _draggedFrameOriginalDataIndex = -1;
            }
        }

        private void OnDraggedFrameItemPointerUp(PointerUpEvent evt)
        {
            if (_draggedFrameElement == null || evt.currentTarget != _draggedFrameElement)
            {
                return;
            }

            if (DragAndDrop.GetGenericData("DraggedFrameDataIndex") != null)
            {
                CleanupFrameDragState(evt.pointerId);
            }
            else if (
                _draggedFrameElement != null
                && _draggedFrameElement.HasPointerCapture(evt.pointerId)
            )
            {
                _draggedFrameElement.ReleasePointer(evt.pointerId);
                _draggedFrameElement.UnregisterCallback<PointerUpEvent>(
                    OnDraggedFrameItemPointerUp
                );
                _draggedFrameElement.RemoveFromClassList("frame-item-dragged");
                _draggedFrameElement = null;
            }
            evt.StopPropagation();
        }

        private void OnApplyFpsToPreviewClicked()
        {
            _currentPreviewFps = Mathf.Max(0.1f, _fpsField.value);
            _fpsField.SetValueWithoutNotify(_currentPreviewFps);
            if (_animationPreview != null)
            {
                _animationPreview.Fps = _currentPreviewFps;
            }
        }

        private void OnSaveClipClicked()
        {
            if (_activeEditorLayer == null || _activeEditorLayer.SourceClip == null)
            {
                this.LogError($"No active animation clip to save.");
                return;
            }

            AnimationClip clipToSave = _activeEditorLayer.SourceClip;
            string bindingPath = _activeEditorLayer.BindingPath;

            EditorCurveBinding spriteBinding = default;
            bool bindingFound = false;
            EditorCurveBinding[] allBindings = AnimationUtility.GetObjectReferenceCurveBindings(
                clipToSave
            );

            foreach (EditorCurveBinding b in allBindings)
            {
                if (
                    b.type == typeof(SpriteRenderer)
                    && string.Equals(
                        b.propertyName,
                        UnityExtensions.SpriteBindingProperty,
                        StringComparison.Ordinal
                    )
                    && (
                        string.IsNullOrWhiteSpace(bindingPath)
                        || string.Equals(b.path, bindingPath, StringComparison.Ordinal)
                    )
                )
                {
                    spriteBinding = b;
                    bindingFound = true;
                    break;
                }
            }

            if (!bindingFound)
            {
                foreach (EditorCurveBinding b in allBindings)
                {
                    if (
                        b.type == typeof(SpriteRenderer)
                        && string.Equals(
                            b.propertyName,
                            UnityExtensions.SpriteBindingProperty,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        spriteBinding = b;
                        bindingFound = true;
                        this.LogWarn(
                            $"Saving to first available m_Sprite binding on '{clipToSave.name}' as specific path '{bindingPath}' was not found or empty."
                        );
                        break;
                    }
                }
            }

            if (!bindingFound)
            {
                this.LogError(
                    $"Cannot save '{clipToSave.name}': No SpriteRenderer m_Sprite binding found (Path Hint: '{bindingPath}'). Clip might be empty or not a sprite animation."
                );
                return;
            }

            List<Sprite> spritesToSave = _activeEditorLayer.Sprites;
            ObjectReferenceKeyframe[] newKeyframes = new ObjectReferenceKeyframe[
                spritesToSave.Count
            ];
            float timePerFrame = 0 < _currentPreviewFps ? 1.0f / _currentPreviewFps : 0f;

            for (int i = 0; i < spritesToSave.Count; i++)
            {
                newKeyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i * timePerFrame,
                    value = spritesToSave[i],
                };
            }

            Undo.RecordObject(clipToSave, "Modify Animation Clip Frames");
            AnimationUtility.SetObjectReferenceCurve(clipToSave, spriteBinding, newKeyframes);
            clipToSave.frameRate = _currentPreviewFps;

            EditorUtility.SetDirty(clipToSave);
            AssetDatabase.SaveAssets();

            this.Log(
                $"Animation clip '{clipToSave.name}' saved with {spritesToSave.Count} frames at {_currentPreviewFps} FPS."
            );
        }

        private void OnDisable()
        {
            CleanupFrameDragState(InvalidPointerId);
            CleanupLoadedClipDragState(InvalidPointerId);

            if (_animationPreview != null && _animationPreview.parent == _previewPanelHost)
            {
                _previewPanelHost.Remove(_animationPreview);
            }
            _animationPreview = null;
        }
    }
#endif
}
