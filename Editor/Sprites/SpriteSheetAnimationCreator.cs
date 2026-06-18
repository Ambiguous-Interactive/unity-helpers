// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable HeapView.CanAvoidClosure
namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Creates one or more AnimationClips from a single sprite sheet by selecting sprite ranges,
    /// defining loop/cycle offset, and configuring per-animation constant or curve-based frame
    /// rates. Includes live preview and per-definition controls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: turning a sliced sprite sheet into multiple clips (e.g., Idle, Walk,
    /// Attack) with minimal friction and previewing playback before saving.
    /// </para>
    /// <para>
    /// How it works: load a Texture2D with multiple sprites (sliced), pick index ranges to form an
    /// animation definition, and optionally use an <see cref="AnimationCurve"/> for variable frame
    /// rate. Preview playback with transport controls; save generated clips to assets.
    /// </para>
    /// <para>
    /// Pros: rapid clip creation from a single sheet; visual selection and iteration.
    /// Caveats: relies on existing sprite slicing; saving overwrites/creates .anim assets.
    /// </para>
    /// </remarks>
    public sealed class SpriteSheetAnimationCreator : EditorWindow
    {
        private static bool SuppressUserPrompts { get; set; }

        static SpriteSheetAnimationCreator()
        {
            try
            {
                if (Application.isBatchMode || IsInvokedByTestRunner())
                {
                    SuppressUserPrompts = true;
                }
            }
            catch { }
        }

        private static bool IsInvokedByTestRunner()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                string a = args[i];
                if (
                    a.IndexOf("runTests", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.IndexOf("testResults", StringComparison.OrdinalIgnoreCase) >= 0
                    || a.IndexOf("testPlatform", StringComparison.OrdinalIgnoreCase) >= 0
                )
                {
                    return true;
                }
            }
            return false;
        }

        private const float ThumbnailSize = 64f;

        private Texture2D _selectedSpriteSheet;
        private readonly List<Sprite> _availableSprites = new();

        private readonly List<AnimationDefinition> _animationDefinitions = new();

        private ObjectField _spriteSheetField;
        private Button _refreshSpritesButton;
        private Button _loadSpritesButton;
        private ScrollView _spriteThumbnailsScrollView;
        private VisualElement _spriteThumbnailsContainer;
        private ListView _animationDefinitionsListView;
        private Button _addAnimationDefinitionButton;
        private Button _generateAnimationsButton;

        private VisualElement _previewContainer;
        private Image _previewImage;
        private Label _previewFrameLabel;
        private Button _playPreviewButton;
        private Button _stopPreviewButton;
        private Button _prevFrameButton;
        private Button _nextFrameButton;
        private Slider _previewScrubber;

        private bool _isDraggingToSelectSprites;
        private int _spriteSelectionDragStartIndex = -1;
        private int _spriteSelectionDragCurrentIndex = -1;
        private StyleColor _selectedThumbnailBackgroundColor = new(
            new Color(0.2f, 0.5f, 0.8f, 0.4f)
        );
        private readonly StyleColor _defaultThumbnailBackgroundColor = new(StyleKeyword.Null);

        private bool _isPreviewing;
        private int _currentPreviewAnimDefIndex = -1;
        private int _currentPreviewSpriteIndex;
        private AnimationDefinition _currentPreviewDefinition;
        private readonly EditorApplication.CallbackFunction _editorUpdateCallback;
        private readonly Stopwatch _timer = Stopwatch.StartNew();

        private TimeSpan? _lastTick;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sprite Sheet Animation Creator")]
        public static void ShowWindow()
        {
            SpriteSheetAnimationCreator window = GetWindow<SpriteSheetAnimationCreator>();
            window.titleContent = new GUIContent("Sprite Animation Creator");
            window.minSize = new Vector2(600, 700);
        }

        [Serializable]
        [DataContract]
        public sealed class AnimationDefinition
        {
            public string Name = "New Animation";
            public bool loop;
            public float cycleOffset;
            public int StartSpriteIndex;
            public int EndSpriteIndex;
            public float DefaultFrameRate = 12f;
            public AnimationCurve FrameRateCurve = AnimationCurve.Constant(0, 1, 12f);
            public List<Sprite> SpritesToAnimate = new();

            public TextField nameField;
            public IntegerField startIndexField;
            public IntegerField endIndexField;
            public FloatField defaultFrameRateField;
            public CurveField frameRateCurveField;
            public Label spriteCountLabel;
            public Button previewButton;
            public Button removeButton;
            public Toggle loopingField;
            public FloatField cycleOffsetField;
        }

        public SpriteSheetAnimationCreator()
        {
            _editorUpdateCallback = OnEditorUpdate;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            VisualElement topSection = new()
            {
                style = { flexDirection = FlexDirection.Row, marginBottom = 10 },
            };
            _spriteSheetField = new ObjectField("Sprite Sheet")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false,
                style =
                {
                    flexGrow = 1,
                    flexShrink = 0,
                    minHeight = 20,
                },
            };
            _spriteSheetField.RegisterValueChangedCallback(OnSpriteSheetSelected);
            topSection.Add(_spriteSheetField);

            _loadSpritesButton = new Button(() =>
            {
                string filePath = string.Empty;
                if (_spriteSheetField.value != null)
                {
                    filePath = AssetDatabase.GetAssetPath(_spriteSheetField.value);
                }

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    filePath = Application.dataPath;
                }

                string selectedPath = Utils.EditorUi.OpenFilePanel(
                    "Select Sprite Sheet",
                    filePath,
                    "png,jpg,gif,bmp,psd"
                );
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                string relativePath = DirectoryHelper.AbsoluteToUnityRelativePath(selectedPath);
                if (!string.IsNullOrWhiteSpace(relativePath))
                {
                    Texture2D loadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        relativePath
                    );
                    if (loadedTexture != null)
                    {
                        _spriteSheetField.value = loadedTexture;
                    }
                }
            })
            {
                text = "Load Sprites",
                style = { marginLeft = 5, minHeight = 20 },
            };
            topSection.Add(_loadSpritesButton);

            _refreshSpritesButton = new Button(LoadAndDisplaySprites)
            {
                text = "Refresh Sprites",
                style = { marginLeft = 5, minHeight = 20 },
            };
            topSection.Add(_refreshSpritesButton);
            root.Add(topSection);

            Label thumbnailsLabel = new(
                "Available Sprites (Drag to select range for new animation):"
            )
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 5,
                    marginBottom = 5,
                },
            };
            root.Add(thumbnailsLabel);
            _spriteThumbnailsScrollView = new ScrollView(ScrollViewMode.Horizontal)
            {
                style =
                {
                    height = ThumbnailSize + 20 + 10,
                    minHeight = ThumbnailSize + 20 + 10,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderBottomColor = Color.gray,
                    borderTopColor = Color.gray,
                    borderLeftColor = Color.gray,
                    borderRightColor = Color.gray,
                    paddingLeft = 5,
                    paddingRight = 5,
                    paddingTop = 5,
                    paddingBottom = 5,
                    marginBottom = 10,
                },
            };
            _spriteThumbnailsContainer = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row },
            };

            _spriteThumbnailsContainer.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (
                    _isDraggingToSelectSprites
                    && _spriteThumbnailsContainer.HasPointerCapture(evt.pointerId)
                )
                {
                    VisualElement currentElementOver = evt.target as VisualElement;

                    VisualElement thumbChild = currentElementOver;
                    while (thumbChild != null && thumbChild.parent != _spriteThumbnailsContainer)
                    {
                        thumbChild = thumbChild.parent;
                    }

                    if (
                        thumbChild is { userData: int hoveredIndex }
                        && _spriteSelectionDragCurrentIndex != hoveredIndex
                    )
                    {
                        _spriteSelectionDragCurrentIndex = hoveredIndex;
                        UpdateSpriteSelectionHighlight();
                    }
                }
            });

            _spriteThumbnailsContainer.RegisterCallback<PointerUpEvent>(
                evt =>
                {
                    if (
                        evt.button == 0
                        && _isDraggingToSelectSprites
                        && _spriteThumbnailsContainer.HasPointerCapture(evt.pointerId)
                    )
                    {
                        _spriteThumbnailsContainer.ReleasePointer(evt.pointerId);
                        _isDraggingToSelectSprites = false;

                        if (
                            _spriteSelectionDragStartIndex != -1
                            && _spriteSelectionDragCurrentIndex != -1
                        )
                        {
                            int start = Mathf.Min(
                                _spriteSelectionDragStartIndex,
                                _spriteSelectionDragCurrentIndex
                            );
                            int end = Mathf.Max(
                                _spriteSelectionDragStartIndex,
                                _spriteSelectionDragCurrentIndex
                            );

                            if (start <= end)
                            {
                                CreateAnimationDefinitionFromSelection(start, end);
                            }
                        }
                        ClearSpriteSelectionHighlight();
                        _spriteSelectionDragStartIndex = -1;
                        _spriteSelectionDragCurrentIndex = -1;
                    }
                },
                TrickleDown.TrickleDown
            );

            _spriteThumbnailsScrollView.Add(_spriteThumbnailsContainer);
            root.Add(_spriteThumbnailsScrollView);

            Label animDefsLabel = new("Animation Definitions:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 10,
                    marginBottom = 5,
                },
            };
            root.Add(animDefsLabel);

            _animationDefinitionsListView = new ListView(
                _animationDefinitions,
                130,
                MakeAnimationDefinitionItem,
                BindAnimationDefinitionItem
            )
            {
                selectionType = SelectionType.None,
                style = { flexGrow = 1, minHeight = 200 },
            };
            root.Add(_animationDefinitionsListView);

            _addAnimationDefinitionButton = new Button(AddAnimationDefinition)
            {
                text = "Add Animation Definition",
                style = { marginTop = 5 },
            };
            root.Add(_addAnimationDefinitionButton);

            Label previewSectionLabel = new("Animation Preview:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 15,
                    marginBottom = 5,
                },
            };
            root.Add(previewSectionLabel);

            _previewContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.Center,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderBottomColor = Color.gray,
                    borderTopColor = Color.gray,
                    borderLeftColor = Color.gray,
                    borderRightColor = Color.gray,
                    paddingBottom = 10,
                    paddingTop = 10,
                    minHeight = 150,
                },
            };
            _previewImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 128,
                    height = 128,
                    marginBottom = 10,
                    backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f)),
                },
            };
            _previewContainer.Add(_previewImage);

            _previewFrameLabel = new Label("Frame: -/- | FPS: -")
            {
                style = { alignSelf = Align.Center, marginBottom = 5 },
            };
            _previewContainer.Add(_previewFrameLabel);

            _previewScrubber = new Slider(0, 1)
            {
                style =
                {
                    minWidth = 200,
                    marginBottom = 5,
                    visibility = Visibility.Hidden,
                },
            };
            _previewScrubber.RegisterValueChangedCallback(evt =>
            {
                if (
                    _currentPreviewDefinition != null
                    && 0 < _currentPreviewDefinition.SpritesToAnimate.Count
                )
                {
                    int frame = Mathf.FloorToInt(
                        evt.newValue * (_currentPreviewDefinition.SpritesToAnimate.Count - 1)
                    );
                    SetPreviewFrame(frame);
                }
            });
            _previewContainer.Add(_previewScrubber);

            VisualElement previewControls = new()
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.Center },
            };
            _prevFrameButton = new Button(() => AdjustPreviewFrame(-1))
            {
                text = "◀",
                style = { minWidth = 40 },
            };
            _playPreviewButton = new Button(PlayCurrentPreview)
            {
                text = "▶ Play",
                style = { minWidth = 70 },
            };
            _stopPreviewButton = new Button(StopCurrentPreview)
            {
                text = "◼ Stop",
                style = { minWidth = 70, display = DisplayStyle.None },
            };
            _nextFrameButton = new Button(() => AdjustPreviewFrame(1))
            {
                text = "▶",
                style = { minWidth = 40 },
            };
            previewControls.Add(_prevFrameButton);
            previewControls.Add(_playPreviewButton);
            previewControls.Add(_stopPreviewButton);
            previewControls.Add(_nextFrameButton);
            _previewContainer.Add(previewControls);
            root.Add(_previewContainer);

            _generateAnimationsButton = new Button(GenerateAnimations)
            {
                text = "Generate Animation Files",
                style = { marginTop = 15, height = 30 },
            };
            root.Add(_generateAnimationsButton);

            if (_selectedSpriteSheet != null)
            {
                _spriteSheetField.SetValueWithoutNotify(_selectedSpriteSheet);
                LoadAndDisplaySprites();
            }
            _animationDefinitionsListView.Rebuild();
        }

        private void OnEnable()
        {
            EditorApplication.update += _editorUpdateCallback;

            string data = SessionState.GetString(GetType().FullName, "");
            if (!string.IsNullOrEmpty(data))
            {
                JsonUtility.FromJsonOverwrite(data, this);
            }
            if (_selectedSpriteSheet != null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (_spriteSheetField != null)
                    {
                        _spriteSheetField.value = _selectedSpriteSheet;
                    }
                    LoadAndDisplaySprites();
                    _animationDefinitionsListView.Rebuild();
                };
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= _editorUpdateCallback;
            StopCurrentPreview();

            string data = JsonUtility.ToJson(this);
            SessionState.SetString(GetType().FullName, data);
        }

        private void OnEditorUpdate()
        {
            if (
                !_isPreviewing
                || _currentPreviewDefinition is not { SpritesToAnimate: { Count: > 0 } }
            )
            {
                return;
            }

            _lastTick ??= _timer.Elapsed;
            float targetFps = 0;
            if (1 < _currentPreviewDefinition.SpritesToAnimate.Count)
            {
                _currentPreviewDefinition.FrameRateCurve.Evaluate(
                    _currentPreviewSpriteIndex
                        / (_currentPreviewDefinition.SpritesToAnimate.Count - 1f)
                );
            }

            if (targetFps <= 0)
            {
                targetFps = _currentPreviewDefinition.DefaultFrameRate;
            }

            if (targetFps <= 0)
            {
                targetFps = 1;
            }

            TimeSpan elapsed = _timer.Elapsed;
            TimeSpan deltaTime = TimeSpan.FromMilliseconds(1000 / targetFps);

            // Prevent time accumulation drift: if _lastTick has fallen significantly behind
            // (e.g., editor was paused/unfocused), clamp it BEFORE checking the frame advance
            // condition. This prevents rapid "catch-up" animation that makes the preview
            // appear to run at too high FPS.
            // Allow at most one frame of lag before resetting to current time.
            if (elapsed - _lastTick.Value > deltaTime + deltaTime)
            {
                _lastTick = elapsed - deltaTime;
            }

            if (_lastTick + deltaTime > elapsed)
            {
                return;
            }

            _lastTick += deltaTime;

            int nextFrame = _currentPreviewSpriteIndex.WrappedIncrement(
                _currentPreviewDefinition.SpritesToAnimate.Count
            );
            SetPreviewFrame(nextFrame);
        }

        private void UpdateSpriteSelectionHighlight()
        {
            if (
                !_isDraggingToSelectSprites
                || _spriteSelectionDragStartIndex == -1
                || _spriteSelectionDragCurrentIndex == -1
            )
            {
                ClearSpriteSelectionHighlight();
                return;
            }

            int minIdx = Mathf.Min(
                _spriteSelectionDragStartIndex,
                _spriteSelectionDragCurrentIndex
            );
            int maxIdx = Mathf.Max(
                _spriteSelectionDragStartIndex,
                _spriteSelectionDragCurrentIndex
            );

            for (int i = 0; i < _spriteThumbnailsContainer.childCount; i++)
            {
                VisualElement thumb = _spriteThumbnailsContainer.ElementAt(i);
                if (thumb.userData is int thumbIndex)
                {
                    if (thumbIndex >= minIdx && thumbIndex <= maxIdx)
                    {
                        thumb.style.backgroundColor = _selectedThumbnailBackgroundColor;
                        thumb.style.borderBottomColor =
                            _selectedThumbnailBackgroundColor.value * 1.5f;
                        thumb.style.borderTopColor = _selectedThumbnailBackgroundColor.value * 1.5f;
                        thumb.style.borderLeftColor =
                            _selectedThumbnailBackgroundColor.value * 1.5f;
                        thumb.style.borderRightColor =
                            _selectedThumbnailBackgroundColor.value * 1.5f;
                    }
                    else
                    {
                        thumb.style.backgroundColor = _defaultThumbnailBackgroundColor;
                        thumb.style.borderBottomColor = Color.clear;
                        thumb.style.borderTopColor = Color.clear;
                        thumb.style.borderLeftColor = Color.clear;
                        thumb.style.borderRightColor = Color.clear;
                    }
                }
            }
        }

        private void ClearSpriteSelectionHighlight()
        {
            for (int i = 0; i < _spriteThumbnailsContainer.childCount; i++)
            {
                VisualElement thumb = _spriteThumbnailsContainer.ElementAt(i);
                thumb.style.backgroundColor = _defaultThumbnailBackgroundColor;
                thumb.style.borderBottomColor = Color.clear;
                thumb.style.borderTopColor = Color.clear;
                thumb.style.borderLeftColor = Color.clear;
                thumb.style.borderRightColor = Color.clear;
            }
        }

        private void CreateAnimationDefinitionFromSelection(
            int startSpriteIndex,
            int endSpriteIndex
        )
        {
            if (
                startSpriteIndex < 0
                || endSpriteIndex < 0
                || startSpriteIndex >= _availableSprites.Count
                || endSpriteIndex >= _availableSprites.Count
            )
            {
                this.LogWarn(
                    $"Invalid sprite indices for new animation definition from selection."
                );
                return;
            }

            AnimationDefinition newDefinition = new()
            {
                Name =
                    _selectedSpriteSheet != null
                        ? $"{_selectedSpriteSheet.name}_Anim_{_animationDefinitions.Count}"
                        : $"New_Animation_{_animationDefinitions.Count}",
                StartSpriteIndex = startSpriteIndex,
                EndSpriteIndex = endSpriteIndex,
                DefaultFrameRate = 12f,
            };

            newDefinition.FrameRateCurve = AnimationCurve.Constant(
                0,
                1,
                newDefinition.DefaultFrameRate
            );

            _animationDefinitions.Add(newDefinition);
            UpdateSpritesForDefinition(newDefinition);
            _currentPreviewAnimDefIndex = _animationDefinitions.Count - 1;
            StartOrUpdateCurrentPreview(newDefinition);
            _animationDefinitionsListView.Rebuild();

            if (_animationDefinitionsListView.itemsSource.Count > 0)
            {
                _animationDefinitionsListView.ScrollToItem(_animationDefinitions.Count - 1);
            }
        }

        private void OnSpriteSheetSelected(ChangeEvent<Object> evt)
        {
            _selectedSpriteSheet = evt.newValue as Texture2D;
            _animationDefinitions.Clear();
            AddAnimationDefinition();
            _animationDefinitionsListView.Rebuild();
            LoadAndDisplaySprites();
            StopCurrentPreview();
            _previewImage.sprite = null;
            _previewImage.style.backgroundImage = null;
            _previewFrameLabel.text = "Frame: -/- | FPS: -";
        }

        private void LoadAndDisplaySprites()
        {
            if (_selectedSpriteSheet == null)
            {
                Utils.EditorUi.Info("Error", "No sprite sheet selected.");
                _availableSprites.Clear();
                UpdateSpriteThumbnails();
                return;
            }

            string path = AssetDatabase.GetAssetPath(_selectedSpriteSheet);
            if (string.IsNullOrEmpty(path))
            {
                Utils.EditorUi.Info("Error", "Selected texture is not an asset.");
                return;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Utils.EditorUi.Info(
                    "Error",
                    "Could not get TextureImporter for the selected texture."
                );
                return;
            }

            bool importSettingsChanged = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importSettingsChanged = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                bool fix = SuppressUserPrompts
                    ? false
                    : Utils.EditorUi.Confirm(
                        "Sprite Mode",
                        "The selected texture is not in 'Sprite Mode: Multiple'. This is required to extract individual sprites.\n\nAttempt to change it automatically?",
                        "Yes, Change It",
                        "No, I'll Do It",
                        defaultWhenSuppressed: true
                    );
                if (fix)
                {
                    importer.spriteImportMode = SpriteImportMode.Multiple;

                    importSettingsChanged = true;
                }
                else
                {
                    _availableSprites.Clear();
                    UpdateSpriteThumbnails();
                    return;
                }
            }

            if (importSettingsChanged)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                AssetDatabase.Refresh();
            }

            _availableSprites.Clear();
            Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is Sprite sprite && sprite != null)
                {
                    _availableSprites.Add(sprite);
                }
            }

            _availableSprites.SortByName();

            if (_availableSprites.Count == 0)
            {
                Utils.EditorUi.Info(
                    "No Sprites",
                    "No sprites found in the selected Texture. Ensure it's sliced correctly in the Sprite Editor."
                );
            }

            UpdateSpriteThumbnails();
            UpdateAllAnimationDefinitionSprites();
            _animationDefinitionsListView.Rebuild();
        }

        private void UpdateSpriteThumbnails()
        {
            _spriteThumbnailsContainer.Clear();
            if (_availableSprites.Count == 0)
            {
                _spriteThumbnailsContainer.Add(new Label("No sprites loaded or sheet not sliced."));
                return;
            }

            for (int i = 0; i < _availableSprites.Count; ++i)
            {
                Sprite sprite = _availableSprites[i];
                VisualElement thumbContainer = new()
                {
                    style =
                    {
                        alignItems = Align.Center,
                        marginRight = 5,
                        paddingBottom = 2,
                        borderBottomWidth = 1,
                        borderLeftWidth = 1,
                        borderRightWidth = 1,
                        borderTopWidth = 1,
                        borderBottomColor = Color.clear,
                        borderTopColor = Color.clear,
                        borderLeftColor = Color.clear,
                        borderRightColor = Color.clear,
                    },
                };
                Image img = new()
                {
                    sprite = sprite,
                    scaleMode = ScaleMode.ScaleToFit,
                    style = { width = ThumbnailSize, height = ThumbnailSize },
                };
                thumbContainer.Add(img);
                thumbContainer.Add(new Label($"{i}") { style = { fontSize = 9 } });

                int currentIndex = i;
                thumbContainer.userData = currentIndex;

                thumbContainer.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        _isDraggingToSelectSprites = true;
                        _spriteSelectionDragStartIndex = currentIndex;
                        _spriteSelectionDragCurrentIndex = currentIndex;
                        UpdateSpriteSelectionHighlight();

                        _spriteThumbnailsContainer.CapturePointer(evt.pointerId);
                        evt.StopPropagation();
                    }
                });

                thumbContainer.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (
                        _isDraggingToSelectSprites
                        && _spriteThumbnailsContainer.HasPointerCapture(PointerId.mousePointerId)
                        && _spriteSelectionDragCurrentIndex != currentIndex
                    )
                    {
                        _spriteSelectionDragCurrentIndex = currentIndex;
                        UpdateSpriteSelectionHighlight();
                    }
                });
                _spriteThumbnailsContainer.Add(thumbContainer);
            }
        }

        private void UpdateAllAnimationDefinitionSprites()
        {
            foreach (AnimationDefinition def in _animationDefinitions)
            {
                UpdateSpritesForDefinition(def);
            }
        }

        private static VisualElement MakeAnimationDefinitionItem()
        {
            VisualElement container = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.gray,
                    paddingBottom = 10,
                    paddingTop = 5,
                },
            };
            VisualElement firstRow = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 3,
                },
            };
            VisualElement secondRow = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 3,
                },
            };
            VisualElement thirdRow = new()
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center },
            };
            VisualElement fourthRow = new()
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center },
            };

            TextField nameField = new("Name:")
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
            };
            Label spriteCountLabel = new("Sprites: 0")
            {
                style = { minWidth = 80, marginRight = 5 },
            };
            Button removeButton = new() { text = "Remove", style = { minWidth = 60 } };

            firstRow.Add(nameField);
            firstRow.Add(spriteCountLabel);
            firstRow.Add(removeButton);

            IntegerField startField = new("Start Idx:")
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
                tooltip = "Index of the first sprite (from 'Available Sprites' above, 0-based).",
            };
            IntegerField endField = new("End Idx:")
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
                tooltip = "Index of the last sprite (inclusive).",
            };
            Button previewButton = new() { text = "Preview This", style = { minWidth = 100 } };

            secondRow.Add(startField);
            secondRow.Add(endField);
            secondRow.Add(previewButton);

            FloatField fpsField = new("Default FPS:")
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
            };
            CurveField curveField = new("FPS Curve:") { style = { flexGrow = 1, flexShrink = 1 } };

            thirdRow.Add(fpsField);
            thirdRow.Add(curveField);

            Toggle looping = new("Looping:")
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
            };

            FloatField cycleOffset = new("Cycle Offset:")
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
            };

            fourthRow.Add(looping);
            fourthRow.Add(cycleOffset);

            container.Add(firstRow);
            container.Add(secondRow);
            container.Add(thirdRow);
            container.Add(fourthRow);

            container.userData = new AnimationDefUITags
            {
                nameField = nameField,
                startIndexField = startField,
                endIndexField = endField,
                defaultFrameRateField = fpsField,
                frameRateCurveField = curveField,
                spriteCountLabel = spriteCountLabel,
                previewButton = previewButton,
                removeButton = removeButton,
                looping = looping,
                cycleOffset = cycleOffset,
            };
            return container;
        }

        private class AnimationDefUITags
        {
            public TextField nameField;
            public IntegerField startIndexField;
            public IntegerField endIndexField;
            public FloatField defaultFrameRateField;
            public CurveField frameRateCurveField;
            public Label spriteCountLabel;
            public Button previewButton;
            public Button removeButton;
            public Toggle looping;
            public FloatField cycleOffset;
        }

        private void BindAnimationDefinitionItem(VisualElement element, int index)
        {
            AnimationDefinition definition = _animationDefinitions[index];
            if (element.userData is not AnimationDefUITags tags)
            {
                this.LogError(
                    $"Element UserData was not AnimationDefUITags, found: {element.userData?.GetType()}."
                );
                return;
            }

            definition.nameField?.UnregisterValueChangedCallback(
                definition.nameField.userData as EventCallback<ChangeEvent<string>>
            );

            definition.startIndexField?.UnregisterValueChangedCallback(
                definition.startIndexField.userData as EventCallback<ChangeEvent<int>>
            );

            definition.endIndexField?.UnregisterValueChangedCallback(
                definition.endIndexField.userData as EventCallback<ChangeEvent<int>>
            );

            definition.defaultFrameRateField?.UnregisterValueChangedCallback(
                definition.defaultFrameRateField.userData as EventCallback<ChangeEvent<float>>
            );

            definition.frameRateCurveField?.UnregisterValueChangedCallback(
                definition.frameRateCurveField.userData
                    as EventCallback<ChangeEvent<AnimationCurve>>
            );

            if (definition.removeButton != null)
            {
                definition.removeButton.clicked -= (Action)definition.removeButton.userData;
            }

            if (definition.previewButton != null)
            {
                definition.previewButton.clicked -= (Action)definition.previewButton.userData;
            }

            definition.loopingField?.UnregisterValueChangedCallback(
                (EventCallback<ChangeEvent<bool>>)definition.loopingField.userData
            );
            definition.cycleOffsetField?.UnregisterValueChangedCallback(
                (EventCallback<ChangeEvent<float>>)definition.cycleOffsetField.userData
            );

            definition.nameField = tags.nameField;
            definition.startIndexField = tags.startIndexField;
            definition.endIndexField = tags.endIndexField;
            definition.defaultFrameRateField = tags.defaultFrameRateField;
            definition.frameRateCurveField = tags.frameRateCurveField;
            definition.spriteCountLabel = tags.spriteCountLabel;
            definition.removeButton = tags.removeButton;
            definition.previewButton = tags.previewButton;
            definition.loopingField = tags.looping;
            definition.cycleOffsetField = tags.cycleOffset;

            definition.nameField.SetValueWithoutNotify(definition.Name);
            EventCallback<ChangeEvent<string>> nameChangeCallback = evt =>
            {
                definition.Name = evt.newValue;
            };
            definition.nameField.RegisterValueChangedCallback(nameChangeCallback);
            definition.nameField.userData = nameChangeCallback;

            definition.startIndexField.SetValueWithoutNotify(definition.StartSpriteIndex);
            EventCallback<ChangeEvent<int>> startChangeCallback = evt =>
            {
                definition.StartSpriteIndex = Mathf.Clamp(
                    evt.newValue,
                    0,
                    _availableSprites.Count > 0 ? _availableSprites.Count - 1 : 0
                );
                if (
                    definition.StartSpriteIndex > definition.EndSpriteIndex
                    && 0 < _availableSprites.Count
                )
                {
                    definition.EndSpriteIndex = definition.StartSpriteIndex;
                }

                definition.startIndexField.SetValueWithoutNotify(definition.StartSpriteIndex);
                UpdateSpritesForDefinition(definition);
                if (_currentPreviewAnimDefIndex == index)
                {
                    StartOrUpdateCurrentPreview(definition);
                }
            };
            definition.startIndexField.RegisterValueChangedCallback(startChangeCallback);
            definition.startIndexField.userData = startChangeCallback;

            definition.endIndexField.SetValueWithoutNotify(definition.EndSpriteIndex);
            EventCallback<ChangeEvent<int>> endChangeCallback = evt =>
            {
                definition.EndSpriteIndex = Mathf.Clamp(
                    evt.newValue,
                    0,
                    _availableSprites.Count > 0 ? _availableSprites.Count - 1 : 0
                );
                if (
                    definition.EndSpriteIndex < definition.StartSpriteIndex
                    && 0 < _availableSprites.Count
                )
                {
                    definition.StartSpriteIndex = definition.EndSpriteIndex;
                }

                definition.endIndexField.SetValueWithoutNotify(definition.EndSpriteIndex);
                UpdateSpritesForDefinition(definition);
                if (_currentPreviewAnimDefIndex == index)
                {
                    StartOrUpdateCurrentPreview(definition);
                }
            };
            definition.endIndexField.RegisterValueChangedCallback(endChangeCallback);
            definition.endIndexField.userData = endChangeCallback;

            definition.defaultFrameRateField.SetValueWithoutNotify(definition.DefaultFrameRate);
            EventCallback<ChangeEvent<float>> fpsChangeCallback = evt =>
            {
                definition.DefaultFrameRate = Mathf.Max(0.1f, evt.newValue);
                definition.defaultFrameRateField.SetValueWithoutNotify(definition.DefaultFrameRate);

                if (IsCurveConstant(definition.FrameRateCurve))
                {
                    definition.FrameRateCurve = AnimationCurve.Constant(
                        0,
                        1,
                        definition.DefaultFrameRate
                    );
                    definition.frameRateCurveField.SetValueWithoutNotify(definition.FrameRateCurve);
                }
                if (_currentPreviewAnimDefIndex == index)
                {
                    StartOrUpdateCurrentPreview(definition);
                }
            };
            definition.defaultFrameRateField.RegisterValueChangedCallback(fpsChangeCallback);
            definition.defaultFrameRateField.userData = fpsChangeCallback;

            definition.frameRateCurveField.SetValueWithoutNotify(definition.FrameRateCurve);
            EventCallback<ChangeEvent<AnimationCurve>> curveChangeCallback = evt =>
            {
                definition.FrameRateCurve = evt.newValue;
                if (_currentPreviewAnimDefIndex == index)
                {
                    StartOrUpdateCurrentPreview(definition);
                }
            };
            definition.frameRateCurveField.RegisterValueChangedCallback(curveChangeCallback);
            definition.frameRateCurveField.userData = curveChangeCallback;

            Action removeAction = () => RemoveAnimationDefinition(index);
            definition.removeButton.clicked += removeAction;
            definition.removeButton.userData = removeAction;

            Action previewAction = () =>
            {
                _currentPreviewAnimDefIndex = index;
                StartOrUpdateCurrentPreview(definition);
            };
            definition.previewButton.clicked += previewAction;
            definition.previewButton.userData = previewAction;

            EventCallback<ChangeEvent<bool>> loopingChangeCallback = evt =>
            {
                definition.loop = evt.newValue;
            };

            definition.loopingField.RegisterValueChangedCallback(loopingChangeCallback);
            definition.loopingField.userData = loopingChangeCallback;

            EventCallback<ChangeEvent<float>> cycleOffsetChangeCallback = evt =>
            {
                definition.cycleOffset = evt.newValue;
            };
            definition.cycleOffsetField.RegisterValueChangedCallback(cycleOffsetChangeCallback);
            definition.cycleOffsetField.userData = cycleOffsetChangeCallback;

            UpdateSpritesForDefinition(definition);
        }

        private static bool IsCurveConstant(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length < 2)
            {
                return true;
            }

            float firstValue = curve.keys[0].value;
            for (int i = 1; i < curve.keys.Length; ++i)
            {
                if (!Mathf.Approximately(curve.keys[i].value, firstValue))
                {
                    return false;
                }
            }
            return true;
        }

        private void UpdateSpritesForDefinition(AnimationDefinition def)
        {
            def.SpritesToAnimate.Clear();
            if (
                0 < _availableSprites.Count
                && def.StartSpriteIndex <= def.EndSpriteIndex
                && def.StartSpriteIndex < _availableSprites.Count
                && def.EndSpriteIndex < _availableSprites.Count
                && def.StartSpriteIndex >= 0
                && def.EndSpriteIndex >= 0
            )
            {
                for (int i = def.StartSpriteIndex; i <= def.EndSpriteIndex; ++i)
                {
                    def.SpritesToAnimate.Add(_availableSprites[i]);
                }
            }
            if (def.spriteCountLabel != null)
            {
                def.spriteCountLabel.text = $"Sprites: {def.SpritesToAnimate.Count}";
            }
        }

        private void AddAnimationDefinition()
        {
            AnimationDefinition newDefinition = new();
            if (_selectedSpriteSheet != null)
            {
                newDefinition.Name =
                    $"{_selectedSpriteSheet.name}_Anim_{_animationDefinitions.Count}";
            }
            if (0 < _availableSprites.Count)
            {
                if (0 < _animationDefinitions.Count)
                {
                    int nextStartIndex = _animationDefinitions[^1].EndSpriteIndex + 1;
                    if (_availableSprites.Count - 1 < nextStartIndex)
                    {
                        nextStartIndex = 0;
                    }
                    newDefinition.StartSpriteIndex = nextStartIndex;
                }

                newDefinition.EndSpriteIndex = _availableSprites.Count - 1;
            }
            newDefinition.FrameRateCurve = AnimationCurve.Constant(
                0,
                1,
                newDefinition.DefaultFrameRate
            );
            _animationDefinitions.Add(newDefinition);
            _currentPreviewAnimDefIndex = _animationDefinitions.Count - 1;
            StartOrUpdateCurrentPreview(newDefinition);
            UpdateSpritesForDefinition(newDefinition);
            _animationDefinitionsListView.Rebuild();
        }

        private void RemoveAnimationDefinition(int index)
        {
            if (index >= 0 && index < _animationDefinitions.Count)
            {
                if (_currentPreviewAnimDefIndex == index)
                {
                    StopCurrentPreview();
                }

                _animationDefinitions.RemoveAt(index);
                _animationDefinitionsListView.Rebuild();
                if (_currentPreviewAnimDefIndex > index)
                {
                    _currentPreviewAnimDefIndex--;
                }
            }
        }

        private void StartOrUpdateCurrentPreview(AnimationDefinition def)
        {
            _currentPreviewDefinition = def;
            if (def == null || def.SpritesToAnimate.Count == 0)
            {
                StopCurrentPreview();
                _previewImage.sprite = null;
                _previewImage.style.backgroundImage = null;
                _previewFrameLabel.text = "Frame: -/- | FPS: -";
                _previewScrubber.style.visibility = Visibility.Hidden;
                return;
            }

            _previewScrubber.style.visibility = Visibility.Visible;
            _previewScrubber.highValue = def.SpritesToAnimate.Count > 1 ? 1f : 0f;
            _previewScrubber.SetValueWithoutNotify(0);

            SetPreviewFrame(0);
        }

        private void PlayCurrentPreview()
        {
            if (
                _currentPreviewDefinition == null
                || _currentPreviewDefinition.SpritesToAnimate.Count == 0
            )
            {
                Utils.EditorUi.Info(
                    "Preview Error",
                    "No animation definition selected or definition has no sprites. Click 'Preview This' on an animation definition first."
                );
                return;
            }

            _isPreviewing = true;
            _playPreviewButton.style.display = DisplayStyle.None;
            _stopPreviewButton.style.display = DisplayStyle.Flex;
        }

        private void StopCurrentPreview()
        {
            _isPreviewing = false;
            _lastTick = null;
            _playPreviewButton.style.display = DisplayStyle.Flex;
            _stopPreviewButton.style.display = DisplayStyle.None;
        }

        private void SetPreviewFrame(int frameIndex)
        {
            if (_currentPreviewDefinition is not { SpritesToAnimate: { Count: > 0 } })
            {
                return;
            }

            _currentPreviewSpriteIndex = Mathf.Clamp(
                frameIndex,
                0,
                _currentPreviewDefinition.SpritesToAnimate.Count - 1
            );

            Sprite spriteToShow = _currentPreviewDefinition.SpritesToAnimate[
                _currentPreviewSpriteIndex
            ];
            _previewImage.sprite = spriteToShow;
            _previewImage.MarkDirtyRepaint();

            float currentCurveTime = 0f;
            if (_currentPreviewDefinition.SpritesToAnimate.Count > 1)
            {
                currentCurveTime =
                    (float)_currentPreviewSpriteIndex
                    / (_currentPreviewDefinition.SpritesToAnimate.Count - 1);
            }

            float fpsAtCurrent = _currentPreviewDefinition.FrameRateCurve.Evaluate(
                currentCurveTime
                    * _currentPreviewDefinition.FrameRateCurve.keys.LastOrDefault().time
            );
            if (fpsAtCurrent <= 0)
            {
                fpsAtCurrent = _currentPreviewDefinition.DefaultFrameRate;
            }

            _previewFrameLabel.text =
                $"Frame: {_currentPreviewSpriteIndex + 1}/{_currentPreviewDefinition.SpritesToAnimate.Count} | FPS: {fpsAtCurrent:F1}";

            if (_currentPreviewDefinition.SpritesToAnimate.Count > 1)
            {
                _previewScrubber.SetValueWithoutNotify(
                    (float)_currentPreviewSpriteIndex
                        / (_currentPreviewDefinition.SpritesToAnimate.Count - 1)
                );
            }
            else
            {
                _previewScrubber.SetValueWithoutNotify(0);
            }
        }

        private void AdjustPreviewFrame(int direction)
        {
            if (_currentPreviewDefinition is not { SpritesToAnimate: { Count: > 0 } })
            {
                return;
            }

            StopCurrentPreview();

            int newFrame = _currentPreviewSpriteIndex + direction;

            int count = _currentPreviewDefinition.SpritesToAnimate.Count;
            if (newFrame < 0)
            {
                newFrame = count - 1;
            }

            if (newFrame >= count)
            {
                newFrame = 0;
            }

            SetPreviewFrame(newFrame);
        }

        private void GenerateAnimations()
        {
            if (_selectedSpriteSheet == null)
            {
                Utils.EditorUi.Info("Error", "No sprite sheet loaded.");
                return;
            }
            if (_animationDefinitions.Count == 0)
            {
                Utils.EditorUi.Info("Error", "No animation definitions created.");
                return;
            }

            string sheetPath = AssetDatabase.GetAssetPath(_selectedSpriteSheet);
            string directory = Path.GetDirectoryName(sheetPath);
            string animationsFolder = SuppressUserPrompts
                ? string.Empty
                : Utils.EditorUi.OpenFolderPanel(
                    "Select Output Directory",
                    directory,
                    string.Empty
                );
            if (string.IsNullOrWhiteSpace(animationsFolder))
            {
                return;
            }

            if (!Directory.Exists(animationsFolder))
            {
                Directory.CreateDirectory(animationsFolder);
            }

            if (!animationsFolder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                animationsFolder = DirectoryHelper.AbsoluteToUnityRelativePath(animationsFolder);
            }

            int createdCount = 0;
            foreach (AnimationDefinition definition in _animationDefinitions)
            {
                if (definition.SpritesToAnimate.Count == 0)
                {
                    this.LogWarn($"Skipping animation '{definition.Name}' as it has no sprites.");
                    continue;
                }

                AnimationClip clip = new() { frameRate = 60 };

                EditorCurveBinding spriteBinding = new()
                {
                    type = typeof(SpriteRenderer),
                    path = "",
                    propertyName = "m_Sprite",
                };

                ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[
                    definition.SpritesToAnimate.Count
                ];
                float currentTime = 0f;
                AnimationCurve curve = definition.FrameRateCurve;
                if (curve == null || curve.keys.Length == 0)
                {
                    this.LogWarn(
                        $"Animation '{definition.Name}' has an invalid FrameRateCurve. Falling back to DefaultFrameRate."
                    );
                    curve = AnimationCurve.Constant(0, 1, definition.DefaultFrameRate);
                }

                if (curve.keys.Length == 0)
                {
                    curve.AddKey(0, definition.DefaultFrameRate);
                }

                float curveDuration = curve.keys.LastOrDefault().time;
                if (curveDuration <= 0)
                {
                    curveDuration = 1f;
                }

                for (int i = 0; i < definition.SpritesToAnimate.Count; ++i)
                {
                    keyframes[i] = new ObjectReferenceKeyframe
                    {
                        time = currentTime,
                        value = definition.SpritesToAnimate[i],
                    };

                    if (i < definition.SpritesToAnimate.Count - 1)
                    {
                        float normalizedTimeForCurve =
                            definition.SpritesToAnimate.Count > 1
                                ? (float)i / (definition.SpritesToAnimate.Count - 1)
                                : 0;
                        float timeForCurveEval = normalizedTimeForCurve * curveDuration;

                        float fps = curve.Evaluate(timeForCurveEval);
                        if (fps <= 0)
                        {
                            fps = definition.DefaultFrameRate;
                        }

                        if (fps <= 0)
                        {
                            fps = 1;
                        }

                        currentTime += 1.0f / fps;
                    }
                }

                AnimationUtility.SetObjectReferenceCurve(clip, spriteBinding, keyframes);

                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = definition.loop;
                settings.cycleOffset = definition.cycleOffset;
                AnimationUtility.SetAnimationClipSettings(clip, settings);

                string animName = string.IsNullOrEmpty(definition.Name)
                    ? "UnnamedAnim"
                    : definition.Name;

                animName = Path.GetInvalidFileNameChars()
                    .Aggregate(animName, (current, character) => current.Replace(character, '_'));
                string assetPath = Path.Combine(animationsFolder, $"{animName}.anim");
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

                AssetDatabaseBatchHelper.EnsureAssetParentFolder(assetPath);
                AssetDatabase.CreateAsset(clip, assetPath);
                createdCount++;
            }

            if (createdCount > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Utils.EditorUi.Info(
                    "Success",
                    $"{createdCount} animation(s) created in:\n{animationsFolder}"
                );
            }
            else
            {
                Utils.EditorUi.Info("Finished", "No valid animations were generated.");
            }
        }

        private static void OnRootDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.objectReferences.Any(obj => obj is Texture2D))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnRootDragPerform(DragPerformEvent evt)
        {
            Texture2D draggedTexture =
                DragAndDrop.objectReferences.FirstOrDefault(obj => obj is Texture2D) as Texture2D;
            if (draggedTexture != null)
            {
                _spriteSheetField.value = draggedTexture;
                DragAndDrop.AcceptDrag();
            }
        }

        public void OnBecameVisible()
        {
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(OnRootDragUpdated);
            rootVisualElement.RegisterCallback<DragPerformEvent>(OnRootDragPerform);
        }

        public void OnBecameInvisible()
        {
            rootVisualElement.UnregisterCallback<DragUpdatedEvent>(OnRootDragUpdated);
            rootVisualElement.UnregisterCallback<DragPerformEvent>(OnRootDragPerform);
        }
    }
#endif
}
