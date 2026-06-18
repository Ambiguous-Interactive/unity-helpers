// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEditor.IMGUI.Controls;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Unity Editor window for analyzing C# code for inheritance and Unity method issues.
    /// </summary>
    public sealed class UnityMethodAnalyzerWindow : EditorWindow
    {
        private static bool SuppressUserPrompts { get; set; }

        static UnityMethodAnalyzerWindow()
        {
            try
            {
                if (Application.isBatchMode || IsInvokedByTestRunner())
                {
                    SuppressUserPrompts = true;
                }
            }
            catch
            {
                // Swallow
            }
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

        [SerializeField]
        internal List<string> _sourcePaths = new();

        [SerializeField]
        private TreeViewState _treeViewState;

        [SerializeField]
        private bool _sourcePathsFoldout = true;

        private Vector2 _sourcePathsScrollPosition;

        internal MethodAnalyzer _analyzer;
        private IssueTreeView _treeView;
        private Vector2 _detailScrollPosition;

        private AnalyzerIssue _selectedIssue;
        internal bool _isAnalyzing;
        internal float _analysisProgress;
        internal string _statusMessage = "Ready to analyze";
        internal CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Internal TaskCompletionSource for tests to await analysis completion.
        /// Set before StartAnalysis() to enable awaiting completion.
        /// </summary>
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        internal TaskCompletionSource<bool> _analysisCompletionSource;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

        /// <summary>
        /// Internal reference to the current analysis task for test synchronization.
        /// Tests can await this task to know when the async analysis work is complete,
        /// then call FlushMainThreadQueue() to process the completion callback.
        /// </summary>
        internal Task _analysisTask;

        internal bool _groupByFile = true;
        internal bool _groupBySeverity;
        internal bool _groupByCategory;
        private IssueSeverity? _severityFilter;
        private IssueCategory? _categoryFilter;
        private string _searchFilter = string.Empty;

        internal int _criticalCount;
        internal int _highCount;
        internal int _mediumCount;
        internal int _lowCount;
        internal int _infoCount;
        internal int _totalCount;

        private const float ToolbarHeight = 40f;
        private const float FilterHeight = 50f;
        private const float SummaryHeight = 30f;
        private const float DetailPanelMinHeight = 150f;
        private const float SplitterHeight = 4f;
        private const float MinSourcePathsSectionHeight = 80f;
        private const float MaxSourcePathsSectionHeight = 200f;
        private const float NarrowLayoutThreshold = 700f;
        private const float VeryNarrowLayoutThreshold = 500f;

        private float _detailPanelHeight = 200f;
        private bool _isResizingDetailPanel;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Unity Method Analyzer")]
        public static void ShowWindow()
        {
            UnityMethodAnalyzerWindow window = GetWindow<UnityMethodAnalyzerWindow>(
                "Unity Method Analyzer"
            );
            window.minSize = new Vector2(450, 400);
            window.Show();
        }

        private void OnEnable()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the window's analyzer and tree view. Called from OnEnable().
        /// Also accessible for testing purposes.
        /// </summary>
        internal void Initialize()
        {
            _analyzer = new MethodAnalyzer();
            _treeViewState ??= new TreeViewState();
            _treeView = new IssueTreeView(_treeViewState);
            _treeView.OnIssueSelected += OnIssueSelected;
            _treeView.OnOpenFile += OpenFileAtLine;
            _treeView.OnRevealInExplorer += RevealFileInExplorer;
            _treeView.OnCopyIssueAsJson += CopyIssueToClipboardAsJson;
            _treeView.OnCopyIssueAsMarkdown += CopyIssueToClipboardAsMarkdown;
            _treeView.OnCopyAllAsJson += CopyAllIssuesToClipboardAsJson;
            _treeView.OnCopyAllAsMarkdown += CopyAllIssuesToClipboardAsMarkdown;

            if (_sourcePaths == null || _sourcePaths.Count == 0)
            {
                _sourcePaths = new List<string> { GetProjectRoot() };
            }
        }

        internal void OnDisable()
        {
            CancellationTokenSource cts = _cancellationTokenSource;
            if (cts != null)
            {
                try
                {
                    // Both IsCancellationRequested and Cancel() can throw if disposed
                    if (!cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // CTS was already disposed, safe to ignore
                }

                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, safe to ignore
                }

                _cancellationTokenSource = null;
            }

            if (_treeView != null)
            {
                _treeView.OnIssueSelected -= OnIssueSelected;
                _treeView.OnOpenFile -= OpenFileAtLine;
                _treeView.OnRevealInExplorer -= RevealFileInExplorer;
                _treeView.OnCopyIssueAsJson -= CopyIssueToClipboardAsJson;
                _treeView.OnCopyIssueAsMarkdown -= CopyIssueToClipboardAsMarkdown;
                _treeView.OnCopyAllAsJson -= CopyAllIssuesToClipboardAsJson;
                _treeView.OnCopyAllAsMarkdown -= CopyAllIssuesToClipboardAsMarkdown;
            }
        }

        private static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        private void OnGUI()
        {
            float windowWidth = position.width;
            bool isNarrowLayout = windowWidth < NarrowLayoutThreshold;
            bool isVeryNarrowLayout = windowWidth < VeryNarrowLayoutThreshold;

            float sourcePathsHeight = DrawSourcePathsSection(isNarrowLayout);
            DrawToolbar(isNarrowLayout);
            DrawFilters(isNarrowLayout, isVeryNarrowLayout);
            DrawSummary(isNarrowLayout);

            float fixedHeight = ToolbarHeight + FilterHeight + SummaryHeight + sourcePathsHeight;
            float remainingHeight = position.height - fixedHeight - _detailPanelHeight;

            DrawTreeView(Mathf.Max(remainingHeight, 100f));
            DrawSplitter();
            DrawDetailPanel();

            if (_isAnalyzing)
            {
                Repaint();
            }
        }

        private float DrawSourcePathsSection(bool isNarrowLayout)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            _sourcePathsFoldout = EditorGUILayout.Foldout(
                _sourcePathsFoldout,
                $"Source Directories ({_sourcePaths?.Count ?? 0})",
                true
            );

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+", GUILayout.Width(25)))
            {
                string browsePath =
                    _sourcePaths?.Count > 0 && !string.IsNullOrEmpty(_sourcePaths[^1])
                        ? _sourcePaths[^1]
                        : GetProjectRoot();

                string selectedPath = EditorUi.OpenFolderPanel(
                    "Select Source Directory",
                    browsePath,
                    ""
                );

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    _sourcePaths ??= new List<string>();
                    if (!_sourcePaths.Contains(selectedPath))
                    {
                        _sourcePaths.Add(selectedPath);
                    }
                }
            }

            GUILayout.EndHorizontal();

            float sectionHeight = EditorGUIUtility.singleLineHeight + 6f;

            if (_sourcePathsFoldout && _sourcePaths is { Count: > 0 })
            {
                float pathsListHeight = Mathf.Min(
                    _sourcePaths.Count * (EditorGUIUtility.singleLineHeight + 4f),
                    MaxSourcePathsSectionHeight - sectionHeight
                );
                pathsListHeight = Mathf.Max(
                    pathsListHeight,
                    MinSourcePathsSectionHeight - sectionHeight
                );

                _sourcePathsScrollPosition = GUILayout.BeginScrollView(
                    _sourcePathsScrollPosition,
                    GUILayout.Height(pathsListHeight)
                );

                int removeIndex = -1;
                for (int i = 0; i < _sourcePaths.Count; i++)
                {
                    GUILayout.BeginHorizontal();

                    string displayPath = _sourcePaths[i];
                    string projectRoot = GetProjectRoot();
                    if (
                        !string.IsNullOrEmpty(displayPath)
                        && displayPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        displayPath =
                            displayPath.Length > projectRoot.Length
                                ? "." + displayPath.Substring(projectRoot.Length)
                                : ".";
                    }

                    bool pathExists =
                        !string.IsNullOrEmpty(_sourcePaths[i]) && Directory.Exists(_sourcePaths[i]);
                    GUIStyle pathStyle = pathExists
                        ? EditorStyles.label
                        : new GUIStyle(EditorStyles.label)
                        {
                            normal = { textColor = new Color(1f, 0.4f, 0.4f) },
                        };

                    float labelWidth = isNarrowLayout
                        ? position.width - 85f
                        : position.width - 105f;
                    GUILayout.Label(
                        new GUIContent(displayPath, _sourcePaths[i]),
                        pathStyle,
                        GUILayout.Width(labelWidth)
                    );

                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        string browsePath =
                            !string.IsNullOrEmpty(_sourcePaths[i])
                            && Directory.Exists(_sourcePaths[i])
                                ? _sourcePaths[i]
                                : GetProjectRoot();

                        string selectedPath = EditorUi.OpenFolderPanel(
                            "Select Source Directory",
                            browsePath,
                            ""
                        );

                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            _sourcePaths[i] = selectedPath;
                        }
                    }

                    if (GUILayout.Button("-", GUILayout.Width(25)))
                    {
                        removeIndex = i;
                    }

                    GUILayout.EndHorizontal();
                }

                if (removeIndex >= 0)
                {
                    _sourcePaths.RemoveAt(removeIndex);
                }

                GUILayout.EndScrollView();
                sectionHeight += pathsListHeight;
            }

            GUILayout.EndVertical();

            return sectionHeight + 4f;
        }

        private static readonly Color AnalyzeButtonColor = new(0.25f, 0.68f, 0.38f, 1f);
        private static readonly Color CancelButtonColor = new(0.92f, 0.29f, 0.33f, 1f);
        private static readonly Color DisabledButtonColor = new(0.5f, 0.5f, 0.5f, 1f);

        private void DrawToolbar(bool isNarrowLayout)
        {
            GUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(ToolbarHeight));

            bool hasValidPaths =
                _sourcePaths != null
                && _sourcePaths.Any(p => !string.IsNullOrEmpty(p) && Directory.Exists(p));

            bool analyzeEnabled = !_isAnalyzing && hasValidPaths;
            Color analyzeColor = analyzeEnabled ? AnalyzeButtonColor : DisabledButtonColor;
            Color analyzeTextColor = WButtonColorUtility.GetReadableTextColor(analyzeColor);

            GUIStyle analyzeButtonStyle = WButtonStyles.GetColoredButtonStyle(
                analyzeColor,
                analyzeTextColor
            );
            analyzeButtonStyle = new GUIStyle(analyzeButtonStyle)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                fixedHeight = ToolbarHeight - 8,
                padding = new RectOffset(12, 12, 4, 4),
            };

            GUI.enabled = analyzeEnabled;

            string buttonText = isNarrowLayout ? "▶ Analyze" : "▶ Analyze Code";
            float buttonWidth = isNarrowLayout ? 95f : 130f;

            if (GUILayout.Button(buttonText, analyzeButtonStyle, GUILayout.Width(buttonWidth)))
            {
                StartAnalysis();
            }

            GUI.enabled = true;

            if (_isAnalyzing)
            {
                GUILayout.Space(8);

                Color cancelTextColor = WButtonColorUtility.GetReadableTextColor(CancelButtonColor);
                GUIStyle cancelButtonStyle = WButtonStyles.GetColoredButtonStyle(
                    CancelButtonColor,
                    cancelTextColor
                );
                cancelButtonStyle = new GUIStyle(cancelButtonStyle)
                {
                    fixedHeight = ToolbarHeight - 8,
                };

                if (GUILayout.Button("Cancel", cancelButtonStyle, GUILayout.Width(60)))
                {
                    CancelAnalysis();
                }

                GUILayout.Space(8);

                Rect progressRect = GUILayoutUtility.GetRect(
                    isNarrowLayout ? 80f : 120f,
                    ToolbarHeight - 12,
                    GUILayout.ExpandWidth(false)
                );
                progressRect.y += 2;
                EditorGUI.ProgressBar(
                    progressRect,
                    _analysisProgress,
                    $"{_analysisProgress * 100:F0}%"
                );
            }

            GUILayout.FlexibleSpace();

            GUIStyle statusStyle = new(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = true,
            };
            GUILayout.Label(
                _statusMessage,
                statusStyle,
                GUILayout.MaxWidth(isNarrowLayout ? 150f : 300f)
            );

            GUILayout.EndHorizontal();
        }

        private void DrawFilters(bool isNarrowLayout, bool isVeryNarrowLayout)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            if (isVeryNarrowLayout)
            {
                DrawFiltersVerticalLayout();
            }
            else if (isNarrowLayout)
            {
                DrawFiltersTwoRowLayout();
            }
            else
            {
                DrawFiltersSingleRowLayout();
            }

            GUILayout.EndVertical();
        }

        private void DrawFiltersSingleRowLayout()
        {
            GUILayout.BeginHorizontal();

            DrawGroupBySection();
            GUILayout.Space(15);
            DrawSeverityFilter();
            GUILayout.Space(10);
            DrawCategoryFilter();
            GUILayout.Space(10);
            DrawSearchFilter();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawFiltersTwoRowLayout()
        {
            GUILayout.BeginHorizontal();
            DrawGroupBySection();
            GUILayout.FlexibleSpace();
            DrawSearchFilter();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawSeverityFilter();
            GUILayout.Space(10);
            DrawCategoryFilter();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawFiltersVerticalLayout()
        {
            DrawGroupBySection();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            DrawSeverityFilter();
            GUILayout.Space(10);
            DrawCategoryFilter();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawSearchFilter();
        }

        private void DrawGroupBySection()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Group By:", GUILayout.Width(60));

            bool newGroupByFile = GUILayout.Toggle(
                _groupByFile,
                "File",
                EditorStyles.miniButtonLeft,
                GUILayout.Width(50)
            );
            bool newGroupBySeverity = GUILayout.Toggle(
                _groupBySeverity,
                "Severity",
                EditorStyles.miniButtonMid,
                GUILayout.Width(60)
            );
            bool newGroupByCategory = GUILayout.Toggle(
                _groupByCategory,
                "Category",
                EditorStyles.miniButtonRight,
                GUILayout.Width(65)
            );
            GUILayout.EndHorizontal();

            // These toggles act as radio buttons - exactly one must be selected.
            // Detect which button was clicked by checking for a transition from false to true.
            // If a button transitions from true to false (user clicked currently selected), ignore it.
            // Use else-if to ensure only one transition is processed per frame.
            bool fileClicked = newGroupByFile && !_groupByFile;
            bool severityClicked = newGroupBySeverity && !_groupBySeverity;
            bool categoryClicked = newGroupByCategory && !_groupByCategory;

            if (fileClicked)
            {
                _groupByFile = true;
                _groupBySeverity = false;
                _groupByCategory = false;
                UpdateTreeViewGrouping();
            }
            else if (severityClicked)
            {
                _groupByFile = false;
                _groupBySeverity = true;
                _groupByCategory = false;
                UpdateTreeViewGrouping();
            }
            else if (categoryClicked)
            {
                _groupByFile = false;
                _groupBySeverity = false;
                _groupByCategory = true;
                UpdateTreeViewGrouping();
            }
        }

        private void DrawSeverityFilter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Severity:", GUILayout.Width(55));
            int severityIndex = _severityFilter.HasValue ? (int)_severityFilter.Value : 0;
            string[] severityOptions = { "All", "Critical", "High", "Medium", "Low", "Info" };
            int newSeverityIndex = EditorGUILayout.Popup(
                severityIndex,
                severityOptions,
                GUILayout.Width(80)
            );
            GUILayout.EndHorizontal();

            if (newSeverityIndex != severityIndex)
            {
                _severityFilter = newSeverityIndex == 0 ? null : (IssueSeverity)newSeverityIndex;
                UpdateTreeViewFilters();
            }
        }

        private void DrawCategoryFilter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Category:", GUILayout.Width(60));
            int categoryIndex = _categoryFilter.HasValue ? (int)_categoryFilter.Value + 1 : 0;
            string[] categoryOptions =
            {
                "All",
                "Unity Lifecycle",
                "Unity Inheritance",
                "General Inheritance",
            };
            int newCategoryIndex = EditorGUILayout.Popup(
                categoryIndex,
                categoryOptions,
                GUILayout.Width(120)
            );
            GUILayout.EndHorizontal();

            if (newCategoryIndex != categoryIndex)
            {
                _categoryFilter =
                    newCategoryIndex == 0 ? null : (IssueCategory)(newCategoryIndex - 1);
                UpdateTreeViewFilters();
            }
        }

        private void DrawSearchFilter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(50));
            string newSearch = EditorGUILayout.TextField(_searchFilter, GUILayout.MinWidth(100));
            GUILayout.EndHorizontal();

            if (newSearch != _searchFilter)
            {
                _searchFilter = newSearch;
                UpdateTreeViewFilters();
            }
        }

        private void DrawSummary(bool isNarrowLayout)
        {
            GUILayout.BeginHorizontal(EditorStyles.helpBox);

            GUIStyle totalStyle = new(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            GUILayout.Label(
                $"Total: {_totalCount}",
                totalStyle,
                GUILayout.Width(isNarrowLayout ? 60f : 80f)
            );

            GUIStyle criticalStyle = new(EditorStyles.label)
            {
                normal = { textColor = new Color(1f, 0.3f, 0.3f) },
            };

            GUIStyle highStyle = new(EditorStyles.label)
            {
                normal = { textColor = new Color(1f, 0.6f, 0.2f) },
            };

            GUIStyle mediumStyle = new(EditorStyles.label)
            {
                normal = { textColor = new Color(1f, 0.9f, 0.2f) },
            };

            GUIStyle lowStyle = new(EditorStyles.label)
            {
                normal = { textColor = new Color(0.5f, 0.9f, 0.5f) },
            };

            GUIStyle infoStyle = new(EditorStyles.label)
            {
                normal = { textColor = new Color(0.5f, 0.7f, 1f) },
            };

            if (isNarrowLayout)
            {
                GUILayout.Label($"🔴{_criticalCount}", criticalStyle);
                GUILayout.Label($"🟠{_highCount}", highStyle);
                GUILayout.Label($"🟡{_mediumCount}", mediumStyle);
                GUILayout.Label($"🟢{_lowCount}", lowStyle);
                GUILayout.Label($"🔵{_infoCount}", infoStyle);
            }
            else
            {
                GUILayout.Label(
                    $"🔴 Critical: {_criticalCount}",
                    criticalStyle,
                    GUILayout.Width(100)
                );
                GUILayout.Label($"🟠 High: {_highCount}", highStyle, GUILayout.Width(80));
                GUILayout.Label($"🟡 Medium: {_mediumCount}", mediumStyle, GUILayout.Width(100));
                GUILayout.Label($"🟢 Low: {_lowCount}", lowStyle, GUILayout.Width(70));
                GUILayout.Label($"🔵 Info: {_infoCount}", infoStyle, GUILayout.Width(70));
            }

            GUILayout.FlexibleSpace();

            if (_totalCount > 0)
            {
                if (GUILayout.Button("Export ▾", GUILayout.Width(isNarrowLayout ? 70f : 100f)))
                {
                    ShowExportMenu();
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawTreeView(float height)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(height));

            GUILayout.Label("Issues", EditorStyles.boldLabel);

            float innerHeight = height - EditorGUIUtility.singleLineHeight - 8f;
            Rect treeViewRect = GUILayoutUtility.GetRect(
                0,
                innerHeight,
                GUILayout.ExpandWidth(true)
            );
            _treeView?.OnGUI(treeViewRect);

            GUILayout.EndVertical();
        }

        private void DrawSplitter()
        {
            Rect splitterRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.Height(SplitterHeight),
                GUILayout.ExpandWidth(true)
            );

            EditorGUI.DrawRect(splitterRect, new Color(0.2f, 0.2f, 0.2f));
            if (!EditorUi.Suppress)
            {
                EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);
            }

            if (
                Event.current.type == EventType.MouseDown
                && splitterRect.Contains(Event.current.mousePosition)
            )
            {
                _isResizingDetailPanel = true;
                Event.current.Use();
            }

            if (_isResizingDetailPanel)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _detailPanelHeight -= Event.current.delta.y;
                    _detailPanelHeight = Mathf.Clamp(
                        _detailPanelHeight,
                        DetailPanelMinHeight,
                        position.height - 200
                    );
                    Repaint();
                    Event.current.Use();
                }

                if (Event.current.type == EventType.MouseUp)
                {
                    _isResizingDetailPanel = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawDetailPanel()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(_detailPanelHeight));

            GUILayout.Label("Issue Details", EditorStyles.boldLabel);

            if (_selectedIssue == null)
            {
                GUILayout.Label(
                    "Select an issue to view details. Double-click to open the file.",
                    EditorStyles.centeredGreyMiniLabel
                );
            }
            else
            {
                _detailScrollPosition = GUILayout.BeginScrollView(_detailScrollPosition);

                GUILayout.BeginHorizontal();
                GUILayout.Label("File:", EditorStyles.boldLabel, GUILayout.Width(100));
                if (
                    GUILayout.Button(
                        $"{_selectedIssue.FilePath}:{_selectedIssue.LineNumber}",
                        EditorStyles.linkLabel
                    )
                )
                {
                    OpenFileAtLine(_selectedIssue.FilePath, _selectedIssue.LineNumber);
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Class:", EditorStyles.boldLabel, GUILayout.Width(100));
                GUILayout.Label(_selectedIssue.ClassName);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Method:", EditorStyles.boldLabel, GUILayout.Width(100));
                GUILayout.Label(_selectedIssue.MethodName);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Issue Type:", EditorStyles.boldLabel, GUILayout.Width(100));
                GUILayout.Label(_selectedIssue.IssueType);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Severity:", EditorStyles.boldLabel, GUILayout.Width(100));
                GUILayout.Label(GetSeverityDisplay(_selectedIssue.Severity));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Category:", EditorStyles.boldLabel, GUILayout.Width(100));
                GUILayout.Label(GetCategoryDisplay(_selectedIssue.Category));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                GUILayout.Label("Description:", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_selectedIssue.Description, MessageType.Warning);

                GUILayout.Label("Recommended Fix:", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_selectedIssue.RecommendedFix, MessageType.Info);

                if (!string.IsNullOrEmpty(_selectedIssue.BaseClassName))
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Inheritance Details:", EditorStyles.boldLabel);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Base Class:", GUILayout.Width(100));
                    GUILayout.Label(_selectedIssue.BaseClassName);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    if (!string.IsNullOrEmpty(_selectedIssue.BaseMethodSignature))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Base Method:", GUILayout.Width(100));
                        GUILayout.Label(_selectedIssue.BaseMethodSignature, EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    if (!string.IsNullOrEmpty(_selectedIssue.DerivedMethodSignature))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Derived Method:", GUILayout.Width(100));
                        GUILayout.Label(
                            _selectedIssue.DerivedMethodSignature,
                            EditorStyles.miniLabel
                        );
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private static string GetSeverityDisplay(IssueSeverity severity)
        {
            return severity switch
            {
                IssueSeverity.Critical => "🔴 Critical - Will definitely cause bugs",
                IssueSeverity.High => "🟠 High - Very likely to cause bugs",
                IssueSeverity.Medium => "🟡 Medium - May cause subtle bugs",
                IssueSeverity.Low => "🟢 Low - Code smell or maintainability issue",
                IssueSeverity.Info => "🔵 Info - Informational only",
                _ => "Unknown",
            };
        }

        private static string GetCategoryDisplay(IssueCategory category)
        {
            return category switch
            {
                IssueCategory.UnityLifecycle => "🎮 Unity Lifecycle",
                IssueCategory.UnityInheritance => "🔷 Unity Inheritance",
                IssueCategory.GeneralInheritance => "📦 General Inheritance",
                _ => "Unknown",
            };
        }

        internal void StartAnalysis()
        {
            if (_isAnalyzing)
            {
                return;
            }

            if (_analyzer == null)
            {
                _statusMessage = "Analyzer not initialized";
                return;
            }

            _isAnalyzing = true;
            _analysisProgress = 0f;
            _statusMessage = "Analyzing...";
            _selectedIssue = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            List<string> directories = new();
            string rootPath = GetProjectRoot();

            if (_sourcePaths != null)
            {
                foreach (string sourcePath in _sourcePaths)
                {
                    if (string.IsNullOrEmpty(sourcePath))
                    {
                        continue;
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        directories.Add(sourcePath);
                    }
                }
            }

            if (directories.Count == 0)
            {
                _statusMessage = "No valid directories selected";
                FinalizeAnalysis();
                return;
            }

            Progress<float> progress = new(p =>
            {
                // Guard against late progress updates after analysis has been reset.
                // Progress<T> uses SynchronizationContext.Post() which can deliver callbacks
                // after the analysis task completion callback has already run ResetAnalysisState().
                if (_isAnalyzing)
                {
                    _analysisProgress = p;
                }
            });

            CancellationToken token = _cancellationTokenSource.Token;

            Task analysisTask = _analyzer.AnalyzeAsync(rootPath, directories, progress, token);
            _analysisTask = analysisTask;

            analysisTask.ContinueWith(
                task =>
                {
                    EnqueueOnMainThread(() => HandleAnalysisCompletion(task));
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            );
        }

        /// <summary>
        /// Handles the completion of the analysis task on the main thread.
        /// </summary>
        private void HandleAnalysisCompletion(Task task)
        {
            try
            {
                // Capture to local variable to avoid TOCTOU race, since OnDisable() may null the field
                CancellationTokenSource cts = _cancellationTokenSource;
                if (task.IsCanceled || (cts?.IsCancellationRequested ?? false))
                {
                    _statusMessage = "Analysis cancelled";
                }
                else if (task.IsFaulted)
                {
                    Exception e = task.Exception?.GetBaseException() ?? task.Exception;
                    _statusMessage = $"Analysis failed: {e?.Message ?? "Unknown error"}";
                }
                else
                {
                    UpdateIssueCounts();
                    string rootPath = GetProjectRoot();
                    _treeView?.SetIssues(_analyzer.Issues, rootPath);
                    _statusMessage = $"Analysis complete: {_totalCount} issues found";
                }
            }
            catch (Exception e)
            {
                _statusMessage = $"Analysis failed";
                this.LogError($"Analysis failed", e);
            }
            finally
            {
                FinalizeAnalysis();
            }
        }

        /// <summary>
        /// Finalizes the analysis by resetting state and signaling completion.
        /// Called after analysis completes, fails, or is cancelled.
        /// </summary>
        private void FinalizeAnalysis()
        {
            ResetAnalysisState();
            // Signal completion for test synchronization
            _analysisCompletionSource?.TrySetResult(true);
        }

        private static readonly object MainThreadQueueLock = new();
        private static readonly Queue<Action> MainThreadQueue = new();
        private static bool _isUpdateSubscribed;

        /// <summary>
        /// Enqueues an action to be executed on the main thread via EditorApplication.update.
        /// This ensures async continuations are properly executed on the main thread in all scenarios,
        /// including Unity Editor tests.
        /// </summary>
        private static void EnqueueOnMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (MainThreadQueueLock)
            {
                MainThreadQueue.Enqueue(action);
                if (!_isUpdateSubscribed)
                {
                    EditorApplication.update += ProcessMainThreadQueue;
                    _isUpdateSubscribed = true;
                }
            }
        }

        private static void ProcessMainThreadQueue()
        {
            Action[] actionsToProcess;
            lock (MainThreadQueueLock)
            {
                if (MainThreadQueue.Count == 0)
                {
                    EditorApplication.update -= ProcessMainThreadQueue;
                    _isUpdateSubscribed = false;
                    return;
                }

                actionsToProcess = MainThreadQueue.ToArray();
                MainThreadQueue.Clear();
            }

            foreach (Action action in actionsToProcess)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>
        /// Forces processing of the main thread queue. This is useful in test scenarios
        /// where EditorApplication.update may not be called reliably.
        /// </summary>
        internal static void FlushMainThreadQueue()
        {
            Action[] actionsToProcess;
            lock (MainThreadQueueLock)
            {
                if (MainThreadQueue.Count == 0)
                {
                    return;
                }

                actionsToProcess = MainThreadQueue.ToArray();
                MainThreadQueue.Clear();
            }

            foreach (Action action in actionsToProcess)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal void CancelAnalysis()
        {
            CancellationTokenSource cts = _cancellationTokenSource;
            if (cts == null)
            {
                return;
            }

            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // CTS was already disposed, which means the analysis has completed
                // or was already cancelled. Safe to ignore.
            }

            // Immediately reset state since the ContinueWith callback may not execute
            // promptly in certain scenarios. The HandleAnalysisCompletion will also
            // call FinalizeAnalysis, but calling it here ensures immediate responsiveness.
            // FinalizeAnalysis is idempotent via TrySetResult, so multiple calls are safe.
            _statusMessage = "Analysis cancelled";
            FinalizeAnalysis();
        }

        /// <summary>
        /// Resets the analysis state and UI to allow new analysis.
        /// Called after analysis completes, fails, or is cancelled.
        /// </summary>
        internal void ResetAnalysisState()
        {
            _isAnalyzing = false;
            _analysisProgress = 0f;
            Repaint();
        }

        internal void UpdateIssueCounts()
        {
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            _totalCount = issues.Count;

            // Count all severities in a single pass
            int criticalCount = 0;
            int highCount = 0;
            int mediumCount = 0;
            int lowCount = 0;
            int infoCount = 0;

            foreach (AnalyzerIssue issue in issues)
            {
                switch (issue.Severity)
                {
                    case IssueSeverity.Critical:
                        criticalCount++;
                        break;
                    case IssueSeverity.High:
                        highCount++;
                        break;
                    case IssueSeverity.Medium:
                        mediumCount++;
                        break;
                    case IssueSeverity.Low:
                        lowCount++;
                        break;
                    case IssueSeverity.Info:
                        infoCount++;
                        break;
                }
            }

            _criticalCount = criticalCount;
            _highCount = highCount;
            _mediumCount = mediumCount;
            _lowCount = lowCount;
            _infoCount = infoCount;
        }

        private void UpdateTreeViewGrouping()
        {
            _treeView?.SetGrouping(_groupByFile, _groupBySeverity, _groupByCategory);
        }

        private void UpdateTreeViewFilters()
        {
            _treeView?.SetFilters(_severityFilter, _categoryFilter, _searchFilter);
        }

        private void OnIssueSelected(AnalyzerIssue issue)
        {
            _selectedIssue = issue;
            Repaint();
        }

        private void OpenFileAtLine(string filePath, int lineNumber)
        {
            // Handle both absolute and relative paths
            string fullPath;
            if (Path.IsPathRooted(filePath))
            {
                // Already an absolute path, use it directly
                fullPath = Path.GetFullPath(filePath);
            }
            else
            {
                // Relative path - try combining with project root
                fullPath = Path.Combine(Application.dataPath, "..", filePath);
                fullPath = Path.GetFullPath(fullPath);
            }

            if (!File.Exists(fullPath))
            {
                // Try as relative to Assets folder
                string assetsPath = Path.Combine(Application.dataPath, filePath);
                if (File.Exists(assetsPath))
                {
                    fullPath = assetsPath;
                }
                else
                {
                    // Search for the file by name in Assets folder
                    string[] foundFiles = Directory.GetFiles(
                        Application.dataPath,
                        Path.GetFileName(filePath),
                        SearchOption.AllDirectories
                    );

                    if (foundFiles.Length > 0)
                    {
                        fullPath = foundFiles[0];
                    }
                }
            }

            // Convert to asset path for AssetDatabase
            string assetPath = ConvertToAssetPath(fullPath);

            if (assetPath != null)
            {
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset, lineNumber);
                    return;
                }
            }

            // Fallback to external editor
            if (File.Exists(fullPath))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(
                    fullPath,
                    lineNumber
                );
            }
            else
            {
                this.LogWarn($"Could not find file: {filePath}");
            }
        }

        private static string ConvertToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            string normalizedPath = fullPath.Replace('\\', '/');

            // Check if path is within Assets folder
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (normalizedPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + normalizedPath.Substring(dataPath.Length);
            }

            // Check if path is within a Package folder
            // Packages can be in <ProjectRoot>/Packages/ or in the global package cache
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            string packagesPath = projectRoot + "/Packages";

            if (normalizedPath.StartsWith(packagesPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Packages" + normalizedPath.Substring(packagesPath.Length);
            }

            // Check if path is in Library/PackageCache (UPM cached packages)
            string packageCachePath = projectRoot + "/Library/PackageCache";
            if (normalizedPath.StartsWith(packageCachePath, StringComparison.OrdinalIgnoreCase))
            {
                // Extract the portion after Library/PackageCache/
                string afterCache = normalizedPath.Substring(packageCachePath.Length + 1);
                // The package folder has version suffix like "com.package@1.0.0"
                int firstSlash = afterCache.IndexOf('/');
                if (firstSlash > 0)
                {
                    string packageFolderName = afterCache.Substring(0, firstSlash);
                    string pathInsidePackage = afterCache.Substring(firstSlash + 1);
                    // Extract package ID by removing version suffix (everything after @)
                    int atIndex = packageFolderName.IndexOf('@');
                    string packageId =
                        atIndex > 0 ? packageFolderName.Substring(0, atIndex) : packageFolderName;
                    return "Packages/" + packageId + "/" + pathInsidePackage;
                }
            }

            // Check for Library/PackageCache marker anywhere in path (handles different root paths)
            const string packageCacheMarker = "Library/PackageCache/";
            int cacheIndex = normalizedPath.IndexOf(
                packageCacheMarker,
                StringComparison.OrdinalIgnoreCase
            );
            if (cacheIndex >= 0)
            {
                string afterCache = normalizedPath.Substring(
                    cacheIndex + packageCacheMarker.Length
                );
                int firstSlash = afterCache.IndexOf('/');
                if (firstSlash > 0)
                {
                    string packageFolderName = afterCache.Substring(0, firstSlash);
                    string pathInsidePackage = afterCache.Substring(firstSlash + 1);
                    int atIndex = packageFolderName.IndexOf('@');
                    string packageId =
                        atIndex > 0 ? packageFolderName.Substring(0, atIndex) : packageFolderName;
                    return "Packages/" + packageId + "/" + pathInsidePackage;
                }
            }

            // Check if we can find a matching Packages/* path by looking for package folder markers
            int packagesIndex = normalizedPath.IndexOf(
                "/Packages/",
                StringComparison.OrdinalIgnoreCase
            );
            if (packagesIndex >= 0)
            {
                return normalizedPath.Substring(packagesIndex + 1);
            }

            // Also handle case where "Packages" appears as a parent folder
            string[] pathParts = normalizedPath.Split('/');
            for (int i = 0; i < pathParts.Length; i++)
            {
                if (
                    pathParts[i].Equals("Packages", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < pathParts.Length
                )
                {
                    // Check if the next part looks like a package name (contains '.')
                    if (pathParts[i + 1].Contains('.'))
                    {
                        return string.Join("/", pathParts, i, pathParts.Length - i);
                    }
                }
            }

            // Path is not in Assets or Packages - return null to indicate external file
            return null;
        }

        private void RevealFileInExplorer(string filePath)
        {
            // Handle both absolute and relative paths
            string fullPath;
            if (Path.IsPathRooted(filePath))
            {
                // Already an absolute path, use it directly
                fullPath = Path.GetFullPath(filePath);
            }
            else
            {
                // Relative path - try combining with project root
                fullPath = Path.Combine(Application.dataPath, "..", filePath);
                fullPath = Path.GetFullPath(fullPath);
            }

            if (!File.Exists(fullPath))
            {
                // Try as relative to Assets folder
                string assetsPath = Path.Combine(Application.dataPath, filePath);
                if (File.Exists(assetsPath))
                {
                    fullPath = assetsPath;
                }
                else
                {
                    // Search for the file by name in Assets folder
                    string[] foundFiles = Directory.GetFiles(
                        Application.dataPath,
                        Path.GetFileName(filePath),
                        SearchOption.AllDirectories
                    );

                    if (foundFiles.Length > 0)
                    {
                        fullPath = foundFiles[0];
                    }
                }
            }

            if (File.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
            else
            {
                this.LogWarn($"Could not find file to reveal: {filePath}");
            }
        }

        private void ExportReport()
        {
            string defaultName = $"method-analysis-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.md";
            string path = EditorUtility.SaveFilePanel(
                "Export Analysis Report",
                "",
                defaultName,
                "md"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string report = GenerateMarkdownReport();
                File.WriteAllText(path, report);
                _statusMessage = $"Report exported to: {Path.GetFileName(path)}";
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                _statusMessage = $"Export failed";
                this.LogError($"Export failed", e);
            }
        }

        private string GenerateMarkdownReport()
        {
            System.Text.StringBuilder sb = new();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            sb.AppendLine("# Unity Method Analysis Report");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"**Total Issues Found:** {issues.Count}");
            sb.AppendLine();

            sb.AppendLine("## Summary by Severity");
            sb.AppendLine();
            sb.AppendLine("| Severity | Count |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine($"| 🔴 Critical | {_criticalCount} |");
            sb.AppendLine($"| 🟠 High | {_highCount} |");
            sb.AppendLine($"| 🟡 Medium | {_mediumCount} |");
            sb.AppendLine($"| 🟢 Low | {_lowCount} |");
            sb.AppendLine($"| 🔵 Info | {_infoCount} |");
            sb.AppendLine();

            sb.AppendLine("## Summary by Category");
            sb.AppendLine();
            sb.AppendLine("| Category | Count |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine(
                $"| 🎮 Unity Lifecycle | {issues.Count(i => i.Category == IssueCategory.UnityLifecycle)} |"
            );
            sb.AppendLine(
                $"| 🔷 Unity Inheritance | {issues.Count(i => i.Category == IssueCategory.UnityInheritance)} |"
            );
            sb.AppendLine(
                $"| 📦 General Inheritance | {issues.Count(i => i.Category == IssueCategory.GeneralInheritance)} |"
            );
            sb.AppendLine();

            sb.AppendLine("## Detailed Issues");
            sb.AppendLine();

            IOrderedEnumerable<IGrouping<string, AnalyzerIssue>> issuesByFile = issues
                .OrderBy(i => (int)i.Severity)
                .ThenBy(i => i.FilePath)
                .GroupBy(i => i.FilePath)
                .OrderBy(g => g.Key);

            foreach (IGrouping<string, AnalyzerIssue> fileGroup in issuesByFile)
            {
                sb.AppendLine($"### `{fileGroup.Key}`");
                sb.AppendLine();

                foreach (AnalyzerIssue issue in fileGroup.OrderBy(i => i.LineNumber))
                {
                    string severityEmoji = issue.Severity switch
                    {
                        IssueSeverity.Critical => "🔴",
                        IssueSeverity.High => "🟠",
                        IssueSeverity.Medium => "🟡",
                        IssueSeverity.Low => "🟢",
                        IssueSeverity.Info => "🔵",
                        _ => "⚪",
                    };

                    sb.AppendLine(
                        $"#### {severityEmoji} Line {issue.LineNumber}: `{issue.ClassName}.{issue.MethodName}` - {issue.IssueType}"
                    );
                    sb.AppendLine();
                    sb.AppendLine($"**Category:** {issue.Category}");
                    sb.AppendLine();
                    sb.AppendLine($"**Description:** {issue.Description}");
                    sb.AppendLine();

                    if (!string.IsNullOrEmpty(issue.BaseClassName))
                    {
                        sb.AppendLine($"**Base Class:** `{issue.BaseClassName}`");
                        if (!string.IsNullOrEmpty(issue.BaseMethodSignature))
                        {
                            sb.AppendLine($"**Base Method:** `{issue.BaseMethodSignature}`");
                        }
                    }

                    if (!string.IsNullOrEmpty(issue.DerivedMethodSignature))
                    {
                        sb.AppendLine($"**Derived Method:** `{issue.DerivedMethodSignature}`");
                    }

                    sb.AppendLine();
                    sb.AppendLine($"**Recommended Fix:** {issue.RecommendedFix}");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private void ShowExportMenu()
        {
            GenericMenu menu = new();
            menu.AddItem(new GUIContent("Copy All as JSON"), false, CopyAllIssuesToClipboardAsJson);
            menu.AddItem(
                new GUIContent("Copy All as Markdown"),
                false,
                CopyAllIssuesToClipboardAsMarkdown
            );
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save as JSON..."), false, ExportReportAsJson);
            menu.AddItem(new GUIContent("Save as Markdown..."), false, ExportReport);
            menu.ShowAsContext();
        }

        private void CopyIssueToClipboardAsJson(AnalyzerIssue issue)
        {
            if (issue == null)
            {
                return;
            }

            try
            {
                string json = GenerateIssueJson(issue);
                GUIUtility.systemCopyBuffer = json;
                _statusMessage = "Issue copied to clipboard as JSON";
            }
            catch (Exception e)
            {
                _statusMessage = $"Copy failed";
                this.LogError($"Copy failed", e);
            }
        }

        private void CopyIssueToClipboardAsMarkdown(AnalyzerIssue issue)
        {
            if (issue == null)
            {
                return;
            }

            try
            {
                string markdown = GenerateIssueMarkdown(issue);
                GUIUtility.systemCopyBuffer = markdown;
                _statusMessage = "Issue copied to clipboard as Markdown";
            }
            catch (Exception e)
            {
                _statusMessage = $"Copy failed";
                this.LogError($"Copy failed", e);
            }
        }

        private void CopyAllIssuesToClipboardAsJson()
        {
            if (_analyzer?.Issues == null || _analyzer.Issues.Count == 0)
            {
                _statusMessage = "No issues to copy";
                return;
            }

            try
            {
                string json = GenerateJsonReport();
                GUIUtility.systemCopyBuffer = json;
                _statusMessage = $"Copied {_analyzer.Issues.Count} issues to clipboard as JSON";
            }
            catch (Exception e)
            {
                _statusMessage = $"Copy failed";
                this.LogError($"Copy failed", e);
            }
        }

        private void CopyAllIssuesToClipboardAsMarkdown()
        {
            if (_analyzer?.Issues == null || _analyzer.Issues.Count == 0)
            {
                _statusMessage = "No issues to copy";
                return;
            }

            try
            {
                string markdown = GenerateMarkdownReport();
                GUIUtility.systemCopyBuffer = markdown;
                _statusMessage = $"Copied {_analyzer.Issues.Count} issues to clipboard as Markdown";
            }
            catch (Exception e)
            {
                _statusMessage = $"Copy failed";
                this.LogError($"Copy failed", e);
            }
        }

        private void ExportReportAsJson()
        {
            string defaultName = $"method-analysis-report-{DateTime.Now:yyyy-MM-dd-HHmmss}.json";
            string path = EditorUtility.SaveFilePanel(
                "Export Analysis Report as JSON",
                "",
                defaultName,
                "json"
            );

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string report = GenerateJsonReport();
                File.WriteAllText(path, report);
                _statusMessage = $"Report exported to: {Path.GetFileName(path)}";
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                _statusMessage = $"Export failed";
                this.LogError($"Export failed", e);
            }
        }

        private string GenerateIssueJson(AnalyzerIssue issue)
        {
            IssueJsonModel model = new(issue);
            return Serializer.JsonStringify(model, pretty: true);
        }

        private string GenerateIssueMarkdown(AnalyzerIssue issue)
        {
            System.Text.StringBuilder sb = new();

            string severityEmoji = issue.Severity switch
            {
                IssueSeverity.Critical => "🔴",
                IssueSeverity.High => "🟠",
                IssueSeverity.Medium => "🟡",
                IssueSeverity.Low => "🟢",
                IssueSeverity.Info => "🔵",
                _ => "⚪",
            };

            sb.AppendLine(
                $"## {severityEmoji} `{issue.ClassName}.{issue.MethodName}` - {issue.IssueType}"
            );
            sb.AppendLine();
            sb.AppendLine($"**File:** `{issue.FilePath}:{issue.LineNumber}`");
            sb.AppendLine();
            sb.AppendLine($"**Severity:** {issue.Severity}");
            sb.AppendLine();
            sb.AppendLine($"**Category:** {issue.Category}");
            sb.AppendLine();
            sb.AppendLine($"**Description:** {issue.Description}");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(issue.BaseClassName))
            {
                sb.AppendLine($"**Base Class:** `{issue.BaseClassName}`");
                if (!string.IsNullOrEmpty(issue.BaseMethodSignature))
                {
                    sb.AppendLine($"**Base Method:** `{issue.BaseMethodSignature}`");
                }
            }

            if (!string.IsNullOrEmpty(issue.DerivedMethodSignature))
            {
                sb.AppendLine($"**Derived Method:** `{issue.DerivedMethodSignature}`");
            }

            sb.AppendLine();
            sb.AppendLine($"**Recommended Fix:** {issue.RecommendedFix}");

            return sb.ToString();
        }

        private string GenerateJsonReport()
        {
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            AnalysisReportJsonModel report = new()
            {
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalIssues = issues.Count,
                Summary = new SummaryJsonModel
                {
                    BySeverity = new SeveritySummaryJsonModel
                    {
                        Critical = _criticalCount,
                        High = _highCount,
                        Medium = _mediumCount,
                        Low = _lowCount,
                        Info = _infoCount,
                    },
                    ByCategory = new CategorySummaryJsonModel
                    {
                        UnityLifecycle = issues.Count(i =>
                            i.Category == IssueCategory.UnityLifecycle
                        ),
                        UnityInheritance = issues.Count(i =>
                            i.Category == IssueCategory.UnityInheritance
                        ),
                        GeneralInheritance = issues.Count(i =>
                            i.Category == IssueCategory.GeneralInheritance
                        ),
                    },
                },
                Issues = issues.Select(i => new IssueJsonModel(i)).ToList(),
            };

            return Serializer.JsonStringify(report, pretty: true);
        }

        private sealed class AnalysisReportJsonModel
        {
            [JsonPropertyName("generatedAt")]
            public string GeneratedAt { get; set; }

            [JsonPropertyName("totalIssues")]
            public int TotalIssues { get; set; }

            [JsonPropertyName("summary")]
            public SummaryJsonModel Summary { get; set; }

            [JsonPropertyName("issues")]
            public List<IssueJsonModel> Issues { get; set; }
        }

        private sealed class SummaryJsonModel
        {
            [JsonPropertyName("bySeverity")]
            public SeveritySummaryJsonModel BySeverity { get; set; }

            [JsonPropertyName("byCategory")]
            public CategorySummaryJsonModel ByCategory { get; set; }
        }

        private sealed class SeveritySummaryJsonModel
        {
            [JsonPropertyName("critical")]
            public int Critical { get; set; }

            [JsonPropertyName("high")]
            public int High { get; set; }

            [JsonPropertyName("medium")]
            public int Medium { get; set; }

            [JsonPropertyName("low")]
            public int Low { get; set; }

            [JsonPropertyName("info")]
            public int Info { get; set; }
        }

        private sealed class CategorySummaryJsonModel
        {
            [JsonPropertyName("unityLifecycle")]
            public int UnityLifecycle { get; set; }

            [JsonPropertyName("unityInheritance")]
            public int UnityInheritance { get; set; }

            [JsonPropertyName("generalInheritance")]
            public int GeneralInheritance { get; set; }
        }

        private sealed class IssueJsonModel
        {
            [JsonPropertyName("filePath")]
            public string FilePath { get; set; }

            [JsonPropertyName("lineNumber")]
            public int LineNumber { get; set; }

            [JsonPropertyName("className")]
            public string ClassName { get; set; }

            [JsonPropertyName("methodName")]
            public string MethodName { get; set; }

            [JsonPropertyName("issueType")]
            public string IssueType { get; set; }

            [JsonPropertyName("severity")]
            public string Severity { get; set; }

            [JsonPropertyName("category")]
            public string Category { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("recommendedFix")]
            public string RecommendedFix { get; set; }

            [JsonPropertyName("baseClassName")]
            public string BaseClassName { get; set; }

            [JsonPropertyName("baseMethodSignature")]
            public string BaseMethodSignature { get; set; }

            [JsonPropertyName("derivedMethodSignature")]
            public string DerivedMethodSignature { get; set; }

            public IssueJsonModel() { }

            public IssueJsonModel(AnalyzerIssue issue)
            {
                FilePath = issue.FilePath;
                LineNumber = issue.LineNumber;
                ClassName = issue.ClassName;
                MethodName = issue.MethodName;
                IssueType = issue.IssueType;
                Severity = issue.Severity.ToString();
                Category = issue.Category.ToString();
                Description = issue.Description;
                RecommendedFix = issue.RecommendedFix;
                BaseClassName = issue.BaseClassName;
                BaseMethodSignature = issue.BaseMethodSignature;
                DerivedMethodSignature = issue.DerivedMethodSignature;
            }
        }
    }
#endif
}
