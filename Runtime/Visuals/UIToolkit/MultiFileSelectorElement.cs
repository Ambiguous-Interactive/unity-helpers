// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals.UIToolkit
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Multi-file selector overlay built with UI Toolkit.
    /// </summary>
    /// <remarks>
    /// Purpose
    /// - Lets users quickly navigate folders (constrained to the Unity project root/Assets) and select multiple files.
    /// - Optimized for large directories via virtualization and pooled buffers to keep the Editor responsive.
    ///
    /// Highlights
    /// - Virtualized ListView with folder-first ordering and live search filter.
    /// - Zero-allocation streaming directory enumeration with pooled lists (see Buffers&lt;T&gt;).
    /// - HashSet-backed selection with Select All / Clear / Invert helpers.
    /// - Clickable breadcrumbs and double-click directory navigation.
    /// - Smart persistence of last directory and search per logical scope or provided key.
    ///
    /// When to use
    /// - Any Editor tooling requiring an in-UI multi-file picker where the native file dialog is too limited or needs custom selection semantics.
    /// - Scenarios where you need to keep selections across multiple navigations and filter by file extension.
    ///
    /// Usage
    /// <code>
    /// // Typical usage inside an EditorWindow
    /// var selector = new MultiFileSelectorElement(
    ///     initialPath: "Assets/Animations",
    ///     filterExtensions: new[] { ".anim" },
    ///     persistenceKey: "AnimationViewer"
    /// );
    /// selector.OnFilesSelectedReadOnly += files => Debug.Log($"Selected {files.Count} files");
    /// selector.OnCancelled += () => Debug.Log("Selection cancelled");
    /// rootVisualElement.Add(selector);
    /// // To hide: selector.parent.Remove(selector);
    /// </code>
    public sealed class MultiFileSelectorElement : VisualElement
    {
        private const string DefaultRootPath = "Assets";
        private const string PrefKey_LastDir = "WallstopStudios.MultiFileSelector.lastDirectory";
        private const string PrefKey_LastSearch = "WallstopStudios.MultiFileSelector.lastSearch";
        private const string PrefKey_LastUsed = "WallstopStudios.MultiFileSelector.lastUsed";
        private const string PrefKey_ScopesIndex = "WallstopStudios.MultiFileSelector.scopes";

        /// <summary>
        /// Invoked when the user confirms selection. Provides absolute file system paths.
        /// </summary>
        [Obsolete("Use OnFilesSelectedReadOnly for allocation-free callbacks.")]
        public event Action<List<string>> OnFilesSelected;

        /// <summary>
        /// Invoked when the user confirms selection without allocating a copy of the paths.
        /// </summary>
        public event Action<IReadOnlyCollection<string>> OnFilesSelectedReadOnly;

        /// <summary>
        /// Invoked when the user cancels the dialog without confirming.
        /// </summary>
        public event Action OnCancelled;

        private readonly TextField _pathField;
        private readonly Button _upButton;
        private readonly ListView _listView;
        private readonly HashSet<string> _filterExtensions;
        private readonly HashSet<string> _selectedSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Item> _items = new();
        private readonly string _projectRootPath;
        private readonly string _assetsFullPath;
        private readonly Button _confirmButton;
        private readonly TextField _searchField;
        private readonly VisualElement _breadcrumbBar;

        private string _currentDirectory;
        private string _searchText = string.Empty;
        private readonly string _prefsScope;

        /// <summary>
        /// Creates a new multi-file selector.
        /// </summary>
        /// <param name="initialPath">Project-relative path (e.g., "Assets/..."). If null or invalid, defaults to Assets.</param>
        /// <param name="filterExtensions">Allowed file extensions (with or without leading dot). Empty/null means all files.</param>
        /// <param name="persistenceKey">Optional key to persist last directory and search across sessions for this selector. If null or empty, no persistence occurs.</param>
        public MultiFileSelectorElement(
            string initialPath,
            string[] filterExtensions,
            string persistenceKey = null
        )
        {
            _projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _assetsFullPath = Path.GetFullPath(Application.dataPath);

            // Normalize filters: ensure leading '.' and OrdinalIgnoreCase
            if (filterExtensions is { Length: > 0 })
            {
                _filterExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string ext in filterExtensions)
                {
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        continue;
                    }

                    string e = ext.StartsWith(".", StringComparison.Ordinal) ? ext : "." + ext;
                    _filterExtensions.Add(e);
                }
            }
            else
            {
                _filterExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            _prefsScope = BuildScope(initialPath, _filterExtensions, persistenceKey);
            if (!string.IsNullOrEmpty(_prefsScope))
            {
                RegisterScope(_prefsScope);
            }

            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.9f));
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 20;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;

            VisualElement contentBox = new()
            {
                style =
                {
                    width = new Length(80, LengthUnit.Percent),
                    height = new Length(80, LengthUnit.Percent),
                    maxWidth = 700,
                    maxHeight = 500,
                    backgroundColor = new StyleColor(Color.gray),
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 10,
                    paddingBottom = 10,
                    flexDirection = FlexDirection.Column,
                    borderTopLeftRadius = 5,
                    borderTopRightRadius = 5,
                    borderBottomLeftRadius = 5,
                    borderBottomRightRadius = 5,
                },
            };
            Add(contentBox);

            VisualElement headerControls = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 5,
                    alignItems = Align.Center,
                },
            };
            Button assetsFolderButton = new(() => NavigateTo(DefaultRootPath))
            {
                text = "Assets",
                style = { width = 60, marginRight = 5 },
            };
            _upButton = new Button(NavigateUp)
            {
                text = "Up",
                style = { width = 40, marginRight = 5 },
            };
            _pathField = new TextField(null)
            {
                isReadOnly = true,
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    marginRight = 5,
                },
            };

            _searchField = new TextField(null)
            {
                isReadOnly = false,
                style = { width = 150, marginRight = 5 },
                tooltip = "Filter by name",
            };
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue ?? string.Empty;
                PersistString(ScopedKey(PrefKey_LastSearch), _searchText);
                UpdateLastUsedNow();
                PopulateFileList();
            });
            // Initialize search from persisted value
            string persistedSearch = LoadString(ScopedKey(PrefKey_LastSearch), string.Empty);
            if (!string.IsNullOrEmpty(persistedSearch))
            {
                _searchText = persistedSearch;
                _searchField.SetValueWithoutNotify(persistedSearch);
            }

            headerControls.Add(assetsFolderButton);
            headerControls.Add(_upButton);
            headerControls.Add(_pathField);
            headerControls.Add(_searchField);
#if UNITY_EDITOR
            Button openInExplorer = new(OpenInExplorer) { text = "Open", style = { width = 60 } };
            headerControls.Add(openInExplorer);
#endif
            contentBox.Add(headerControls);

            _breadcrumbBar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    marginBottom = 5,
                    flexWrap = Wrap.Wrap,
                    alignItems = Align.Center,
                },
            };
            contentBox.Add(_breadcrumbBar);

            _listView = new ListView
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                style =
                {
                    flexGrow = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderBottomColor = Color.black,
                    borderTopColor = Color.black,
                    borderLeftColor = Color.black,
                    borderRightColor = Color.black,
                    marginBottom = 5,
                    /*
                        Stated rather than scaled: Color * float multiplies alpha too, so the old
                        Color.white * 0.15f was white at 15% opacity, not the dark panel it reads as.
                    */
                    backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 1f)),
                },
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _items,
            };
            contentBox.Add(_listView);

            VisualElement footerControls = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 5,
                },
            };
            Button cancelButton = new(() => OnCancelled?.Invoke())
            {
                text = "Cancel",
                style = { marginRight = 10 },
            };
            Button selectAllButton = new(SelectAllInView)
            {
                text = "Select All",
                style = { marginRight = 5 },
            };
            Button clearButton = new(ClearSelectionInView)
            {
                text = "Clear",
                style = { marginRight = 5 },
            };
            Button invertButton = new(InvertSelectionInView)
            {
                text = "Invert",
                style = { marginRight = 10 },
            };
            _confirmButton = new Button(ConfirmSelection) { text = "Add Selected (0)" };
            footerControls.Add(selectAllButton);
            footerControls.Add(clearButton);
            footerControls.Add(invertButton);
            footerControls.Add(cancelButton);
            footerControls.Add(_confirmButton);
            contentBox.Add(footerControls);

            string validInitialPath = initialPath;
            // Prefer persisted directory when scope is present
            string persistedStart = LoadString(ScopedKey(PrefKey_LastDir), null);
            if (!string.IsNullOrEmpty(persistedStart))
            {
                validInitialPath = persistedStart;
            }
            else if (string.IsNullOrEmpty(validInitialPath))
            {
                validInitialPath = DefaultRootPath;
            }
            if (
                string.IsNullOrEmpty(validInitialPath)
                || !Directory.Exists(Path.Combine(Application.dataPath, "..", validInitialPath))
            )
            {
                validInitialPath = DefaultRootPath;
            }
            NavigateTo(validInitialPath);
        }

        internal void NavigateTo(string path)
        {
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));

            if (!fullPath.StartsWith(_projectRootPath, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = _projectRootPath;
            }
            if (
                string.Equals(DefaultRootPath, "Assets", StringComparison.Ordinal)
                && !fullPath.StartsWith(_assetsFullPath, StringComparison.OrdinalIgnoreCase)
                && fullPath.StartsWith(_projectRootPath)
            )
            {
                fullPath = _assetsFullPath;
            }

            if (!Directory.Exists(fullPath))
            {
                Debug.LogWarning($"Directory does not exist: {fullPath}. Resetting to Assets.");
                _currentDirectory = _assetsFullPath;
            }
            else
            {
                _currentDirectory = fullPath;
            }

            if (_currentDirectory.Equals(_projectRootPath, StringComparison.OrdinalIgnoreCase))
            {
                _pathField.SetValueWithoutNotify("Project Root");
            }
            else if (
                _currentDirectory.StartsWith(_projectRootPath, StringComparison.OrdinalIgnoreCase)
            )
            {
                _pathField.SetValueWithoutNotify(
                    _currentDirectory.Substring(_projectRootPath.Length + 1).SanitizePath()
                );
            }
            else
            {
                _pathField.SetValueWithoutNotify(_currentDirectory.SanitizePath());
            }

            PopulateFileList();
            DirectoryInfo parentDirectory = Directory.GetParent(_currentDirectory);
            _upButton.SetEnabled(
                !_currentDirectory.Equals(_assetsFullPath, StringComparison.OrdinalIgnoreCase)
                    && parentDirectory != null
                    && _projectRootPath.Length <= parentDirectory.FullName.Length
            );
            BuildBreadcrumbs();
            // Persist relative path (scoped) if persistence is enabled
            string rel = _currentDirectory.StartsWith(
                _projectRootPath,
                StringComparison.OrdinalIgnoreCase
            )
                ? _currentDirectory.Substring(_projectRootPath.Length + 1)
                : _currentDirectory;
            string dirKey = ScopedKey(PrefKey_LastDir);
            if (!string.IsNullOrEmpty(rel) && !string.IsNullOrEmpty(dirKey))
            {
                PersistString(dirKey, rel.SanitizePath());
                UpdateLastUsedNow();
            }
        }

        private void NavigateUp()
        {
            if (string.IsNullOrEmpty(_currentDirectory))
            {
                return;
            }

            DirectoryInfo directoryParent = Directory.GetParent(_currentDirectory);
            if (directoryParent != null)
            {
                if (
                    directoryParent.FullName.Length < _projectRootPath.Length
                    || _currentDirectory.Equals(_assetsFullPath, StringComparison.OrdinalIgnoreCase)
                )
                {
                    Debug.Log("Cannot navigate above Assets folder.");
                    NavigateTo(DefaultRootPath);
                    return;
                }
                NavigateTo(directoryParent.FullName.Substring(_projectRootPath.Length + 1));
            }
        }

        private void PopulateFileList()
        {
            _items.Clear();
            try
            {
                // Collect and sort directories
                using (
                    Utils.PooledResource<List<string>> dirLease = Utils.Buffers<string>.List.Get(
                        out List<string> dirs
                    )
                )
                {
                    try
                    {
                        foreach (string d in Directory.EnumerateDirectories(_currentDirectory))
                        {
                            string name = Path.GetFileName(d);
                            if (
                                !string.IsNullOrEmpty(_searchText)
                                && name?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase)
                                    < 0
                            )
                            {
                                continue;
                            }
                            dirs.Add(d);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore individual access issues during enumeration
                    }
                    dirs.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (string d in dirs)
                    {
                        string name = Path.GetFileName(d);
                        _items.Add(new Item(true, d, name));
                    }
                }

                // Collect and sort files
                using (
                    Utils.PooledResource<List<string>> fileLease = Utils.Buffers<string>.List.Get(
                        out List<string> files
                    )
                )
                {
                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(_currentDirectory))
                        {
                            string ext = Path.GetExtension(f);
                            if (0 < _filterExtensions.Count && !_filterExtensions.Contains(ext))
                            {
                                continue;
                            }
                            string name = Path.GetFileName(f);
                            if (
                                !string.IsNullOrEmpty(_searchText)
                                && name?.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase)
                                    < 0
                            )
                            {
                                continue;
                            }
                            files.Add(f);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore individual access issues during enumeration
                    }
                    files.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        string name = Path.GetFileName(f);
                        _items.Add(new Item(false, f, name));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error accessing path {_currentDirectory}: {e.Message}");
            }

            _listView.RefreshItems();
            UpdateConfirmButtonText();
        }

        private void ConfirmSelection()
        {
            OnFilesSelectedReadOnly?.Invoke(_selectedSet);

            if (OnFilesSelected != null)
            {
                OnFilesSelected.Invoke(new List<string>(_selectedSet));
            }
        }

        /// <summary>
        /// Clears selection and navigates to a new starting directory.
        /// </summary>
        /// <param name="newInitialPath">Project-relative path (e.g., "Assets/...").</param>
        public void ResetAndShow(string newInitialPath)
        {
            _selectedSet.Clear();
            NavigateTo(newInitialPath);
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 1,
                },
            };

            Toggle toggle = new() { style = { marginRight = 5 } };
            Label label = new()
            {
                style = { unityTextAlign = TextAnchor.MiddleLeft, flexGrow = 1 },
            };

            row.Add(toggle);
            row.Add(label);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (index < 0 || _items.Count <= index)
            {
                return;
            }
            Item item = _items[index];
            Toggle toggle = row.ElementAt(0) as Toggle;
            Label label = row.ElementAt(1) as Label;

            if (item.isDirectory)
            {
                // Directory rows: no toggle, clickable to navigate
                toggle.SetEnabled(false);
                toggle.value = false;
                label.text = $"📁 {item.name}";
                label.style.opacity = 1f;
                label.tooltip = item.fullPath.SanitizePath();
                // Remove potential previous bindings
                row.UnregisterCallback<ClickEvent>(OnDirectoryClick);
                label.UnregisterCallback<PointerDownEvent>(OnLabelClick);
                label.userData = null;
                row.RegisterCallback<ClickEvent>(OnDirectoryClick, TrickleDown.NoTrickleDown);
                row.userData = item.fullPath;
            }
            else
            {
                // File rows: toggleable selection
                row.UnregisterCallback<ClickEvent>(OnDirectoryClick);
                row.userData = null;
                toggle.SetEnabled(true);
                bool isSelected = _selectedSet.Contains(item.fullPath);
                toggle.SetValueWithoutNotify(isSelected);
                toggle.tooltip = item.fullPath.SanitizePath();
                label.text = item.name;
                label.style.opacity = 1f;

                toggle.UnregisterValueChangedCallback(OnToggleChanged);
                toggle.RegisterValueChangedCallback(OnToggleChanged);
                toggle.userData = item.fullPath;
                // Clicking the label toggles selection
                label.UnregisterCallback<PointerDownEvent>(OnLabelClick);
                label.userData = toggle;
                label.RegisterCallback<PointerDownEvent>(OnLabelClick);
            }
        }

        private void OnDirectoryClick(ClickEvent evt)
        {
            if (evt.button != 0 || evt.clickCount < 2)
            {
                return;
            }
            string path = evt.currentTarget is VisualElement ve ? ve.userData as string : null;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Navigate on single or double click
            string relative = path;
            if (relative.StartsWith(_projectRootPath, StringComparison.OrdinalIgnoreCase))
            {
                relative = relative.Substring(_projectRootPath.Length + 1);
            }
            NavigateTo(relative);
        }

        private void OnToggleChanged(ChangeEvent<bool> evt)
        {
            string filePath = (evt.target as Toggle)?.userData as string;
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }
            if (evt.newValue)
            {
                _selectedSet.Add(filePath);
            }
            else
            {
                _selectedSet.Remove(filePath);
            }
            UpdateConfirmButtonText();
        }

        private void UpdateConfirmButtonText()
        {
            _confirmButton.text = $"Add Selected ({_selectedSet.Count})";
        }

        /// <summary>
        /// Returns the current visible entry names (folders and files) for diagnostics and tests.
        /// Folder names are returned without UI decorations.
        /// </summary>
        public IReadOnlyList<string> DebugGetVisibleEntryNames()
        {
            using Utils.PooledResource<List<string>> lease = Utils.Buffers<string>.List.Get(
                out List<string> list
            );
            foreach (Item item in _items)
            {
                list.Add(item.name);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Returns the currently selected file paths for diagnostics and tests.
        /// </summary>
        public IReadOnlyCollection<string> DebugGetSelectedFilePaths()
        {
            return _selectedSet;
        }

        private void BuildBreadcrumbs()
        {
            _breadcrumbBar.Clear();
            string display;
            string rel;
            if (_currentDirectory.StartsWith(_projectRootPath, StringComparison.OrdinalIgnoreCase))
            {
                rel = _currentDirectory.Substring(_projectRootPath.Length).TrimStart('/', '\\');
            }
            else
            {
                rel = _currentDirectory;
            }

            AddCrumb("Assets", "Assets");

            if (!string.IsNullOrEmpty(rel))
            {
                // If rel starts with Assets, strip it for subsequent segments
                display = rel.SanitizePath();
                if (display.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    display = display.Substring("Assets/".Length);
                }

                string cumulative = "Assets";
                string[] parts = display.Split(
                    new[] { '/' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (string part in parts)
                {
                    cumulative = cumulative + "/" + part;
                    AddCrumb(part, cumulative);
                }
            }

            // Remove trailing separator if exists
            if (
                0 < _breadcrumbBar.childCount
                && _breadcrumbBar.ElementAt(_breadcrumbBar.childCount - 1) is Label
            )
            {
                _breadcrumbBar.RemoveAt(_breadcrumbBar.childCount - 1);
            }

            return;

            // Start with root segment
            void AddCrumb(string title, string navigateTo)
            {
                Button b = new(() => NavigateTo(navigateTo))
                {
                    text = title,
                    style = { marginRight = 4 },
                };
                _breadcrumbBar.Add(b);
                Label sep = new("/") { style = { marginRight = 4 } };
                _breadcrumbBar.Add(sep);
            }
        }

        private void OnLabelClick(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            if ((evt.currentTarget as VisualElement)?.userData is Toggle toggle)
            {
                toggle.value = !toggle.value;
            }
        }

        internal void SelectAllInView()
        {
            foreach (Item it in _items)
            {
                if (!it.isDirectory)
                {
                    _selectedSet.Add(it.fullPath);
                }
            }
            _listView.RefreshItems();
            UpdateConfirmButtonText();
        }

        internal void ClearSelectionInView()
        {
            foreach (Item it in _items)
            {
                if (!it.isDirectory)
                {
                    _selectedSet.Remove(it.fullPath);
                }
            }
            _listView.RefreshItems();
            UpdateConfirmButtonText();
        }

        internal void InvertSelectionInView()
        {
            foreach (Item it in _items)
            {
                if (!it.isDirectory)
                {
                    if (_selectedSet.Contains(it.fullPath))
                    {
                        _selectedSet.Remove(it.fullPath);
                    }
                    else
                    {
                        _selectedSet.Add(it.fullPath);
                    }
                }
            }
            _listView.RefreshItems();
            UpdateConfirmButtonText();
        }

#if UNITY_EDITOR
        private void OpenInExplorer()
        {
            try
            {
                UnityEditor.EditorUtility.RevealInFinder(_currentDirectory);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to open in Explorer/Finder: {e.Message}");
            }
        }
#endif

        private static string LoadString(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(key))
            {
                return defaultValue;
            }
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetString(key, defaultValue);
#else
            return PlayerPrefs.GetString(key, defaultValue);
#endif
        }

        private static void PersistString(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString(key, value ?? string.Empty);
#else
            PlayerPrefs.SetString(key, value ?? string.Empty);
            PlayerPrefs.Save();
#endif
        }

        private static void RegisterScope(string scope)
        {
            if (string.IsNullOrEmpty(scope))
            {
                return;
            }

            string index = LoadString(PrefKey_ScopesIndex, string.Empty);
            if (string.IsNullOrEmpty(index))
            {
                PersistString(PrefKey_ScopesIndex, scope);
                return;
            }

            string[] parts = index.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (string.Equals(part, scope, StringComparison.Ordinal))
                {
                    return;
                }
            }
            PersistString(PrefKey_ScopesIndex, index + ";" + scope);
        }

        private void UpdateLastUsedNow()
        {
            string key = ScopedKey(PrefKey_LastUsed);
            if (!string.IsNullOrEmpty(key))
            {
                PersistString(key, DateTime.UtcNow.Ticks.ToString());
                RegisterScope(_prefsScope);
            }
        }

        /// <summary>
        /// Removes persisted entries for scopes that have not been used within the provided time window.
        /// Only affects entries written by this element and maintains a scope index for safe deletion.
        /// </summary>
        /// <param name="maxAge">Scopes with last-used older than now - maxAge are cleaned.</param>
        public static void CleanupStalePersistenceEntries(TimeSpan maxAge)
        {
            string index = LoadString(PrefKey_ScopesIndex, string.Empty);
            if (string.IsNullOrEmpty(index))
            {
                return;
            }

            string[] scopes = index.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            DateTime cutoff = DateTime.UtcNow - maxAge;
            bool changed = false;
            using Utils.PooledResource<List<string>> lease = Utils.Buffers<string>.List.Get(
                out List<string> survivors
            );

            foreach (string scope in scopes)
            {
                string lastUsedStr = LoadString(PrefKey_LastUsed + "." + scope, string.Empty);
                bool stale = true;
                if (
                    !string.IsNullOrEmpty(lastUsedStr) && long.TryParse(lastUsedStr, out long ticks)
                )
                {
                    DateTime last = new(ticks, DateTimeKind.Utc);
                    stale = last < cutoff;
                }

                if (stale)
                {
#if UNITY_EDITOR
                    UnityEditor.EditorPrefs.DeleteKey(PrefKey_LastUsed + "." + scope);
                    UnityEditor.EditorPrefs.DeleteKey(PrefKey_LastSearch + "." + scope);
                    UnityEditor.EditorPrefs.DeleteKey(PrefKey_LastDir + "." + scope);
#else
                    PlayerPrefs.DeleteKey(PrefKey_LastUsed + "." + scope);
                    PlayerPrefs.DeleteKey(PrefKey_LastSearch + "." + scope);
                    PlayerPrefs.DeleteKey(PrefKey_LastDir + "." + scope);
                    PlayerPrefs.Save();
#endif
                    changed = true;
                }
                else
                {
                    survivors.Add(scope);
                }
            }

            if (changed)
            {
                string rebuilt = string.Join(";", survivors);
                PersistString(PrefKey_ScopesIndex, rebuilt);
            }
        }

        private string ScopedKey(string baseKey)
        {
            if (string.IsNullOrEmpty(baseKey) || string.IsNullOrEmpty(_prefsScope))
            {
                return null;
            }
            return baseKey + "." + _prefsScope;
        }

        private static string BuildScope(string initialPath, HashSet<string> extensions, string key)
        {
            // Only enable persistence when an explicit key is provided by the caller.
            if (!string.IsNullOrWhiteSpace(key))
            {
                return SanitizeKey(key);
            }
            return null;
        }

        private static string SanitizeKey(string key)
        {
            char[] buffer = new char[key.Length];
            int n = 0;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                {
                    buffer[n++] = c;
                }
                else if (char.IsWhiteSpace(c) || c == '|' || c == '/' || c == '\\' || c == ',')
                {
                    buffer[n++] = '_';
                }
            }
            return new string(buffer, 0, n).Trim('_');
        }

        private readonly struct Item
        {
            public readonly bool isDirectory;
            public readonly string fullPath;
            public readonly string name;

            public Item(bool isDirectory, string fullPath, string name)
            {
                this.isDirectory = isDirectory;
                this.fullPath = fullPath;
                this.name = name;
            }
        }
    }
}
