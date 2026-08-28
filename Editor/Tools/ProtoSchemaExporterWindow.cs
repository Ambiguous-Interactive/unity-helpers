// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Exports the proto3 schema of the project's <see cref="WProtoContractAttribute"/> types so a
    /// downstream consumer can decode the bytes with any protobuf toolchain.
    /// </summary>
    /// <remarks>
    /// Undo policy: Tier C. The export writes one schema file, or one file per assembly, namespace
    /// or contract; prior files at those paths are overwritten and are not recoverable through
    /// Unity's undo system. No asset is modified, so nothing else is touched.
    /// </remarks>
    public sealed class ProtoSchemaExporterWindow : EditorWindow
    {
        private const string DefaultOutputPath = "Assets/wallstop-schema.proto";
        private const string DefaultOutputDirectory = "Assets/WallstopSchemas";
        private const string GlobalNamespaceGroup = "Global";
        private const int MaximumDisplayedDiagnostics = 10;

        // Static rather than injected: the window is a root object, and tests drive the export
        // through it without touching the OS file dialog.
        internal static bool SuppressUserPrompts;

        private static readonly ExportLayout[] SelectableLayouts = new ExportLayout[]
        {
            ExportLayout.SingleFile,
            ExportLayout.OneFilePerAssembly,
            ExportLayout.OneFilePerNamespace,
            ExportLayout.OneFilePerContract,
        };

        // Fixed for the process, and consulted once per character of every group key.
        private static readonly HashSet<char> InvalidFileNameCharacters = new HashSet<char>(
            Path.GetInvalidFileNameChars()
        )
        {
            '<',
            '>',
            ':',
            '"',
            '/',
            '\\',
            '|',
            '?',
            '*',
        };

        private static readonly string[] LayoutLabels = new string[]
        {
            "Single File",
            "Per Assembly",
            "Per Namespace",
            "Per Type",
        };

        // Selection is stored as the set the user has turned OFF, so a contract discovered after a
        // recompile arrives selected instead of silently missing from the next export.
        [SerializeField]
        private List<string> _excludedContractKeys = new List<string>();

        [SerializeField]
        private List<string> _collapsedAssemblyNames = new List<string>();

        [SerializeField]
        private string _outputPath = DefaultOutputPath;

        [SerializeField]
        private string _outputDirectory = DefaultOutputDirectory;

        [SerializeField]
        private string _packageName = string.Empty;

        [SerializeField]
        private string _searchFilter = string.Empty;

        [SerializeField]
        private ExportLayout _exportLayout = ExportLayout.SingleFile;

        private readonly HashSet<string> _excludedKeys = new HashSet<string>(
            StringComparer.Ordinal
        );
        private readonly HashSet<string> _collapsedAssemblies = new HashSet<string>(
            StringComparer.Ordinal
        );
        private readonly List<Type> _contracts = new List<Type>();
        private readonly List<string> _assemblyNames = new List<string>();
        private readonly Dictionary<Type, Type> _surrogates = new Dictionary<Type, Type>();
        private readonly List<string> _lastDiagnostics = new List<string>();
        private HelpBox _summary;
        private ScrollView _contractList;
        private HelpBox _packageError;
        private bool _lastStatusIsFailure;
        private TextField _outputField;
        private Button _exportButton;
        private HelpBox _statusBox;
        private VisualElement _diagnosticsContainer;
        private string _lastStatus;

        /// <summary>
        /// Opens the exporter window and re-scans the project for contracts.
        /// </summary>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Proto Schema Exporter")]
        public static void ShowWindow()
        {
            ProtoSchemaExporterWindow window = GetWindow<ProtoSchemaExporterWindow>(
                "Proto Schema Exporter"
            );
            window.RefreshFromUserInterface();
        }

        private void OnEnable()
        {
            RestoreSelectionState();
            RefreshInventory();
        }

        private void OnDisable()
        {
            CaptureSelectionState();
        }

        private void CreateGUI()
        {
            minSize = new Vector2(460f, 340f);
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 4f;
            root.style.paddingRight = 4f;
            root.style.paddingTop = 2f;
            root.style.paddingBottom = 4f;

            root.Add(BuildToolbar());

            _summary = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            root.Add(_summary);

            _contractList = new ScrollView(ScrollViewMode.Vertical);
            _contractList.style.flexGrow = 1f;
            _contractList.style.marginTop = 4f;
            _contractList.style.marginBottom = 4f;
            root.Add(_contractList);

            root.Add(BuildOutputOptions());

            _statusBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            root.Add(_statusBox);

            _diagnosticsContainer = new VisualElement();
            root.Add(_diagnosticsContainer);

            RebuildContractList();
            RefreshSummary();
            RefreshOutputOptions();
            RefreshStatus();
        }

        private Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.Add(
                new ToolbarButton(RefreshFromUserInterface)
                {
                    text = "Refresh",
                    tooltip = "Re-scan the project for [WProtoContract] types.",
                }
            );
            toolbar.Add(
                new ToolbarButton(() => ApplyBulkSelection(true))
                {
                    text = "Select All",
                    tooltip = "Select every contract the search currently shows.",
                }
            );
            toolbar.Add(
                new ToolbarButton(() => ApplyBulkSelection(false))
                {
                    text = "Select None",
                    tooltip = "Deselect every contract the search currently shows.",
                }
            );

            ToolbarSearchField searchField = new ToolbarSearchField
            {
                tooltip = "Filter by type name, namespace or assembly.",
            };
            searchField.style.flexGrow = 1f;
            searchField.SetValueWithoutNotify(_searchFilter);
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            toolbar.Add(searchField);
            return toolbar;
        }

        private VisualElement BuildOutputOptions()
        {
            VisualElement container = new VisualElement();

            PopupField<string> layoutField = new PopupField<string>(
                "File Layout",
                LayoutLabels.ToList(),
                CurrentLayoutIndex()
            )
            {
                tooltip =
                    "How the selection is distributed. Every file is self-contained: one .proto "
                    + "for everything, or one per assembly, namespace or type.",
            };
            layoutField.RegisterValueChangedCallback(OnLayoutChanged);
            container.Add(layoutField);

            TextField packageField = new TextField("Proto Package")
            {
                value = _packageName,
                tooltip = "Optional proto3 package clause written into every generated file.",
            };
            packageField.RegisterValueChangedCallback(OnPackageNameChanged);
            container.Add(packageField);

            _packageError = new HelpBox(
                "A proto package is dot-separated identifiers, for example \"mygame.save\". "
                    + "Clear the field to omit the clause.",
                HelpBoxMessageType.Error
            );
            container.Add(_packageError);

            VisualElement pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.alignItems = Align.Center;
            _outputField = new TextField("Output File") { value = _outputPath };
            _outputField.style.flexGrow = 1f;
            _outputField.RegisterValueChangedCallback(OnOutputPathChanged);
            pathRow.Add(_outputField);
            pathRow.Add(new Button(Browse) { text = "Browse..." });
            container.Add(pathRow);

            _exportButton = new Button(ExportSchema) { text = "Export Schema" };
            container.Add(_exportButton);
            return container;
        }

        private void RebuildContractList()
        {
            if (_contractList == null)
            {
                return;
            }

            _contractList.Clear();
            if (_contracts.Count == 0)
            {
                _contractList.Add(
                    new HelpBox(
                        "No [WProtoContract] types found in the project.",
                        HelpBoxMessageType.Info
                    )
                );
                return;
            }

            bool searching = !string.IsNullOrWhiteSpace(_searchFilter);
            bool anyVisible = false;
            foreach (string assemblyName in _assemblyNames)
            {
                List<Type> visible = ContractsInAssembly(assemblyName).Where(IsVisible).ToList();
                if (visible.Count == 0)
                {
                    continue;
                }

                anyVisible = true;
                _contractList.Add(BuildAssemblyGroup(assemblyName, visible, searching));
            }

            if (!anyVisible)
            {
                _contractList.Add(
                    new HelpBox($"Nothing matches \"{_searchFilter}\".", HelpBoxMessageType.Info)
                );
            }
        }

        private VisualElement BuildAssemblyGroup(
            string assemblyName,
            List<Type> visible,
            bool searching
        )
        {
            Foldout foldout = new Foldout
            {
                text = AssemblyHeaderText(assemblyName, visible),
                value = searching || !_collapsedAssemblies.Contains(assemblyName),
            };
            if (!searching)
            {
                foldout.RegisterValueChangedCallback(changed =>
                {
                    if (changed.newValue)
                    {
                        _collapsedAssemblies.Remove(assemblyName);
                    }
                    else
                    {
                        _collapsedAssemblies.Add(assemblyName);
                    }
                });
            }

            List<Toggle> toggles = new List<Toggle>(visible.Count);
            VisualElement bulkRow = new VisualElement();
            bulkRow.style.flexDirection = FlexDirection.Row;
            bulkRow.Add(
                new Button(() => ApplyGroupSelection(assemblyName, visible, toggles, foldout, true))
                {
                    text = "All",
                }
            );
            bulkRow.Add(
                new Button(() =>
                    ApplyGroupSelection(assemblyName, visible, toggles, foldout, false)
                )
                {
                    text = "None",
                }
            );
            foldout.Add(bulkRow);

            foreach (Type contract in visible)
            {
                foldout.Add(BuildContractRow(contract, assemblyName, visible, toggles, foldout));
            }

            return foldout;
        }

        private VisualElement BuildContractRow(
            Type contract,
            string assemblyName,
            List<Type> visible,
            List<Toggle> toggles,
            Foldout foldout
        )
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            string tooltip = $"{ContractDisplayName(contract)}\nAssembly: {assemblyName}";
            Toggle toggle = new Toggle { value = IsSelected(contract), tooltip = tooltip };
            toggle.RegisterValueChangedCallback(changed =>
            {
                SetSelection(contract, changed.newValue);
                foldout.text = AssemblyHeaderText(assemblyName, visible);
                RefreshSummary();
                RefreshOutputOptions();
            });
            toggles.Add(toggle);
            row.Add(toggle);

            Label nameLabel = new Label(ShortDisplayName(contract)) { tooltip = tooltip };
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(nameLabel);

            Label namespaceLabel = new Label(NamespaceGroupOf(contract)) { tooltip = tooltip };
            namespaceLabel.style.marginLeft = 6f;
            namespaceLabel.style.opacity = 0.6f;
            namespaceLabel.style.flexGrow = 1f;
            row.Add(namespaceLabel);
            return row;
        }

        private void RefreshSummary()
        {
            if (_summary == null)
            {
                return;
            }

            if (_contracts.Count == 0)
            {
                _summary.text = "No [WProtoContract] types found in the project.";
                _summary.messageType = HelpBoxMessageType.Info;
                return;
            }

            int selectedCount = SelectedContractCount();
            string filterSuffix = string.IsNullOrWhiteSpace(_searchFilter)
                ? string.Empty
                : $"; {_contracts.Count(IsVisible)} match \"{_searchFilter}\"";
            _summary.text =
                $"{selectedCount} of {_contracts.Count} contracts selected across "
                + $"{CountSelectedAssemblies()} of {_assemblyNames.Count} assemblies"
                + filterSuffix
                + ".";
            _summary.messageType =
                selectedCount == 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
        }

        private void RefreshOutputOptions()
        {
            // The last element BuildOutputOptions assigns, so a non-null one proves the whole row.
            if (_exportButton == null)
            {
                return;
            }

            bool singleFile = _exportLayout == ExportLayout.SingleFile;
            bool packageIsUsable = HasUsablePackageName();
            _outputField.label = singleFile ? "Output File" : "Output Directory";
            _outputField.SetValueWithoutNotify(singleFile ? _outputPath : _outputDirectory);
            _packageError.style.display = packageIsUsable ? DisplayStyle.None : DisplayStyle.Flex;
            _exportButton.text = singleFile ? "Export Schema" : "Export Schemas";
            _exportButton.SetEnabled(packageIsUsable && 0 < SelectedContractCount());
        }

        private void RefreshStatus()
        {
            if (_diagnosticsContainer == null)
            {
                return;
            }

            bool hasStatus = !string.IsNullOrEmpty(_lastStatus);
            _statusBox.style.display = hasStatus ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasStatus)
            {
                _statusBox.text = _lastStatus;
                _statusBox.messageType = _lastStatusIsFailure
                    ? HelpBoxMessageType.Error
                    : HelpBoxMessageType.Info;
            }

            _diagnosticsContainer.Clear();
            int shown = Math.Min(_lastDiagnostics.Count, MaximumDisplayedDiagnostics);
            for (int index = 0; index < shown; index++)
            {
                _diagnosticsContainer.Add(
                    new HelpBox(_lastDiagnostics[index], HelpBoxMessageType.Warning)
                );
            }

            if (shown < _lastDiagnostics.Count)
            {
                _diagnosticsContainer.Add(
                    new HelpBox(
                        $"{_lastDiagnostics.Count - shown} more diagnostics are in the Console.",
                        HelpBoxMessageType.Warning
                    )
                );
            }
        }

        private void RefreshFromUserInterface()
        {
            RefreshInventory();
            RebuildContractList();
            RefreshSummary();
            RefreshOutputOptions();
            RefreshStatus();
        }

        private void ApplyBulkSelection(bool selected)
        {
            SetSelectionForVisibleContracts(selected);
            RebuildContractList();
            RefreshSummary();
            RefreshOutputOptions();
        }

        private void ApplyGroupSelection(
            string assemblyName,
            List<Type> visible,
            List<Toggle> toggles,
            Foldout foldout,
            bool selected
        )
        {
            SetSelection(visible, selected);
            foreach (Toggle toggle in toggles)
            {
                toggle.SetValueWithoutNotify(selected);
            }

            foldout.text = AssemblyHeaderText(assemblyName, visible);
            RefreshSummary();
            RefreshOutputOptions();
        }

        private void OnSearchChanged(ChangeEvent<string> changed)
        {
            _searchFilter = changed.newValue;
            RebuildContractList();
            RefreshSummary();
        }

        private void OnLayoutChanged(ChangeEvent<string> changed)
        {
            int index = Array.IndexOf(LayoutLabels, changed.newValue);
            if (0 <= index && index < SelectableLayouts.Length)
            {
                _exportLayout = SelectableLayouts[index];
            }

            RefreshOutputOptions();
        }

        private void OnPackageNameChanged(ChangeEvent<string> changed)
        {
            _packageName = changed.newValue;
            RefreshOutputOptions();
        }

        private void OnOutputPathChanged(ChangeEvent<string> changed)
        {
            if (_exportLayout == ExportLayout.SingleFile)
            {
                _outputPath = changed.newValue;
            }
            else
            {
                _outputDirectory = changed.newValue;
            }
        }

        private int CurrentLayoutIndex()
        {
            int index = Array.IndexOf(SelectableLayouts, _exportLayout);
            if (index < 0)
            {
                // Correcting it rather than only displaying index 0, so the popup cannot read
                // "Single File" while the export still runs a layout the list does not name.
                _exportLayout = SelectableLayouts[0];
                return 0;
            }

            return index;
        }

        private string AssemblyHeaderText(string assemblyName, List<Type> visible)
        {
            return $"{assemblyName}  ({visible.Count(IsSelected)}/{visible.Count})";
        }

        private void Browse()
        {
            string chosen;
            if (_exportLayout == ExportLayout.SingleFile)
            {
                string directory = string.IsNullOrEmpty(_outputPath)
                    ? "Assets"
                    : Path.GetDirectoryName(_outputPath);
                chosen = EditorUtility.SaveFilePanel(
                    "Export Proto Schema",
                    directory,
                    Path.GetFileNameWithoutExtension(_outputPath),
                    "proto"
                );
            }
            else
            {
                chosen = EditorUtility.OpenFolderPanel(
                    "Export Proto Schemas",
                    _outputDirectory,
                    string.Empty
                );
            }

            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            if (_exportLayout == ExportLayout.SingleFile)
            {
                _outputPath = ToProjectPathOrAbsolute(chosen);
            }
            else
            {
                _outputDirectory = ToProjectPathOrAbsolute(chosen);
            }

            RefreshOutputOptions();
        }

        internal void RefreshInventory()
        {
            _contracts.Clear();
            _assemblyNames.Clear();
            _surrogates.Clear();

            HashSet<string> assemblyNames = new HashSet<string>(StringComparer.Ordinal);
            List<Type> discovered = new List<Type>();
            foreach (Type contract in TypeCache.GetTypesWithAttribute<WProtoContractAttribute>())
            {
                if (contract.IsGenericTypeDefinition)
                {
                    continue;
                }

                assemblyNames.Add(AssemblyNameOf(contract));
                discovered.Add(contract);
            }

            _contracts.AddRange(
                discovered
                    .OrderBy(AssemblyNameOf, StringComparer.Ordinal)
                    .ThenBy(ContractDisplayName, StringComparer.Ordinal)
            );
            _assemblyNames.AddRange(assemblyNames.OrderBy(name => name, StringComparer.Ordinal));
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (
                    WProtoSurrogateAttribute surrogate in assembly.GetCustomAttributes<WProtoSurrogateAttribute>()
                )
                {
                    _surrogates[surrogate.RealType] = surrogate.SurrogateType;
                }
            }
        }

        internal void SetSelectedAssembliesForTest(IEnumerable<string> assemblyNames)
        {
            HashSet<string> selectedAssemblies = new HashSet<string>(
                assemblyNames ?? Array.Empty<string>(),
                StringComparer.Ordinal
            );
            SetSelection(_contracts, false);
            SetSelection(
                _contracts.Where(contract => selectedAssemblies.Contains(AssemblyNameOf(contract))),
                true
            );
        }

        internal void SetSelectedContractsForTest(IEnumerable<Type> contracts)
        {
            SetSelection(_contracts, false);
            SetSelection(contracts ?? Array.Empty<Type>(), true);
        }

        internal void CaptureSelectionState()
        {
            _excludedContractKeys = _excludedKeys.ToList();
            _collapsedAssemblyNames = _collapsedAssemblies.ToList();
        }

        internal void RestoreSelectionState()
        {
            _excludedKeys.Clear();
            if (_excludedContractKeys != null)
            {
                _excludedKeys.UnionWith(_excludedContractKeys);
            }

            _collapsedAssemblies.Clear();
            if (_collapsedAssemblyNames != null)
            {
                _collapsedAssemblies.UnionWith(_collapsedAssemblyNames);
            }
        }

        private void ReportSuccess(string message)
        {
            _lastStatus = message;
            _lastStatusIsFailure = false;
        }

        private void ReportFailure(string message)
        {
            _lastStatus = message;
            _lastStatusIsFailure = true;
        }

        internal string LastStatusForTest => _lastStatus;

        internal bool LastStatusIsFailureForTest => _lastStatusIsFailure;

        internal IReadOnlyList<string> PersistedExclusionsForTest => _excludedContractKeys;

        internal static string ContractKeyForTest(Type contract) => ContractKey(contract);

        internal static IReadOnlyList<ExportLayout> SelectableLayoutsForTest => SelectableLayouts;

        internal static IReadOnlyList<string> LayoutLabelsForTest => LayoutLabels;

        internal static string UniqueFileNameForTest(string groupKey, HashSet<string> usedFileNames)
        {
            return UniqueFileName(groupKey, usedFileNames);
        }

        internal IReadOnlyList<string> LastDiagnosticsForTest => _lastDiagnostics;

        internal IReadOnlyList<Type> SelectedContractsForTest => SelectedContracts();

        internal IReadOnlyList<Type> VisibleContractsForTest =>
            _contracts.Where(IsVisible).ToList();

        internal ExportLayout ExportLayoutForTest
        {
            get => _exportLayout;
            set => _exportLayout = value;
        }

        internal string PackageNameForTest
        {
            get => _packageName;
            set => _packageName = value;
        }

        internal string SearchFilterForTest
        {
            get => _searchFilter;
            set => _searchFilter = value;
        }

        internal bool HasUsablePackageNameForTest => HasUsablePackageName();

        internal bool ExportSchemaToPath(string outputPath)
        {
            _lastDiagnostics.Clear();

            List<Type> contracts = SelectedContracts();
            if (contracts.Count == 0)
            {
                ReportFailure("No contracts selected.");
                return false;
            }

            if (!HasUsablePackageName())
            {
                ReportFailure(
                    $"\"{_packageName}\" is not a proto3 package: use dot-separated identifiers, "
                        + "or clear the field to omit the clause."
                );
                return false;
            }

            bool rendered = WProtoSchemaText.TryWriteSchema(
                contracts,
                PackageClause(),
                _surrogates,
                out string schema,
                out IReadOnlyList<string> diagnostics
            );
            if (!rendered)
            {
                ReportFailure("Nothing rendered: no [WProtoContract] types among the selection.");
                return false;
            }

            _lastDiagnostics.AddRange(diagnostics);
            try
            {
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, schema, new UTF8Encoding(false));
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                )
            {
                ReportFailure($"Could not write {outputPath}: {exception.Message}");
                return false;
            }
            ReportSuccess($"Exported {contracts.Count} contracts to {outputPath}.");
            return true;
        }

        internal bool ExportSchemasToDirectory(string outputDirectory)
        {
            _lastDiagnostics.Clear();

            List<Type> contracts = SelectedContracts();
            if (contracts.Count == 0)
            {
                ReportFailure("No contracts selected.");
                return false;
            }

            if (!HasUsablePackageName())
            {
                ReportFailure(
                    $"\"{_packageName}\" is not a proto3 package: use dot-separated identifiers, "
                        + "or clear the field to omit the clause."
                );
                return false;
            }

            try
            {
                Directory.CreateDirectory(outputDirectory);
                HashSet<string> usedFileNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );
                int exportedFiles = 0;
                foreach (IGrouping<string, Type> group in GroupForLayout(contracts))
                {
                    // Named before the render check, because ConfirmOverwrite walks the same
                    // groups: a group that renders nothing must still consume its name, or the
                    // two walks disagree about which file a later group is renamed onto.
                    string fileName = UniqueFileName(group.Key, usedFileNames);
                    bool rendered = WProtoSchemaText.TryWriteSchema(
                        group,
                        PackageClause(),
                        _surrogates,
                        out string schema,
                        out IReadOnlyList<string> diagnostics
                    );
                    if (!rendered)
                    {
                        _lastDiagnostics.Add(
                            $"{group.Key}: nothing rendered; no [WProtoContract] type in this group."
                        );
                        continue;
                    }

                    foreach (string diagnostic in diagnostics)
                    {
                        _lastDiagnostics.Add($"{group.Key}: {diagnostic}");
                    }

                    File.WriteAllText(
                        Path.Combine(outputDirectory, fileName),
                        schema,
                        new UTF8Encoding(false)
                    );
                    exportedFiles++;
                }

                if (exportedFiles == 0)
                {
                    ReportFailure(
                        "Nothing rendered: no [WProtoContract] types among the selection."
                    );
                    return false;
                }

                ReportSuccess(
                    $"Exported {contracts.Count} contracts to {exportedFiles} files in "
                        + $"{outputDirectory}."
                );
                return true;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                )
            {
                ReportFailure($"Could not write schemas to {outputDirectory}: {exception.Message}");
                return false;
            }
        }

        private void ExportSchema()
        {
            RunExport();
            RefreshStatus();
        }

        private void RunExport()
        {
            bool singleFile = _exportLayout == ExportLayout.SingleFile;
            string outputPath = singleFile ? _outputPath : _outputDirectory;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                ReportFailure("Choose an output path first.");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.Combine(projectRoot, outputPath);
            }

            if (!SuppressUserPrompts && !ConfirmOverwrite(outputPath, singleFile))
            {
                return;
            }

            bool exported = singleFile
                ? ExportSchemaToPath(outputPath)
                : ExportSchemasToDirectory(outputPath);
            // Logged before the failure return: RefreshStatus tells the reader the rest are in the
            // Console, and a failed export used to leave that sentence pointing at nothing.
            foreach (string diagnostic in _lastDiagnostics)
            {
                Debug.LogWarning(diagnostic, this);
            }

            if (!exported)
            {
                return;
            }

            if (IsInsideProject(outputPath, projectRoot))
            {
                string projectPath = ToProjectPath(outputPath, projectRoot);
                AssetDatabase.Refresh();
                if (singleFile)
                {
                    AssetDatabase.ImportAsset(projectPath);
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    projectPath
                );
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                }
            }

            Debug.Log(_lastStatus, this);
        }

        private bool ConfirmOverwrite(string outputPath, bool singleFile)
        {
            if (singleFile)
            {
                if (!File.Exists(outputPath))
                {
                    return true;
                }

                return EditorUtility.DisplayDialog(
                    "Overwrite schema?",
                    $"{outputPath} already exists. Overwrite it?",
                    "Overwrite",
                    "Cancel"
                );
            }

            HashSet<string> usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int existingFileCount = GroupForLayout(SelectedContracts())
                .Count(group =>
                    File.Exists(Path.Combine(outputPath, UniqueFileName(group.Key, usedFileNames)))
                );
            if (existingFileCount == 0)
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Overwrite schemas?",
                $"{existingFileCount} schema files already exist in {outputPath}. Overwrite them?",
                "Overwrite",
                "Cancel"
            );
        }

        private IEnumerable<IGrouping<string, Type>> GroupForLayout(IEnumerable<Type> contracts)
        {
            switch (_exportLayout)
            {
                case ExportLayout.OneFilePerNamespace:
                    return contracts.GroupBy(NamespaceGroupOf, StringComparer.Ordinal);
                case ExportLayout.OneFilePerContract:
                    return contracts.GroupBy(ContractDisplayName, StringComparer.Ordinal);
                default:
                    return contracts.GroupBy(AssemblyNameOf, StringComparer.Ordinal);
            }
        }

        private string PackageClause()
        {
            return string.IsNullOrWhiteSpace(_packageName) ? null : _packageName.Trim();
        }

        // An omitted package is legal proto3; a malformed one silently produces a file protoc
        // refuses, so the export is blocked rather than written.
        private bool HasUsablePackageName()
        {
            string package = PackageClause();
            if (package == null)
            {
                return true;
            }

            foreach (string segment in package.Split('.'))
            {
                if (segment.Length == 0)
                {
                    return false;
                }

                if (segment[0] != '_' && !IsAsciiLetter(segment[0]))
                {
                    return false;
                }

                for (int index = 1; index < segment.Length; index++)
                {
                    char character = segment[index];
                    if (
                        character != '_'
                        && !IsAsciiLetter(character)
                        && !(character is >= '0' and <= '9')
                    )
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsAsciiLetter(char character)
        {
            return character is >= 'a' and <= 'z' || character is >= 'A' and <= 'Z';
        }

        private List<Type> SelectedContracts()
        {
            return _contracts.Where(IsSelected).ToList();
        }

        private int SelectedContractCount()
        {
            return _contracts.Count(IsSelected);
        }

        private bool IsSelected(Type contract)
        {
            return !_excludedKeys.Contains(ContractKey(contract));
        }

        // Both overloads record the mutation before returning, so persistence never depends on
        // OnDisable running first. The bulk one records once rather than once per contract, which
        // is the difference between O(n) and O(n squared) on a Select All.
        private void SetSelection(Type contract, bool selected)
        {
            Exclude(contract, selected);
            CaptureSelectionState();
        }

        private void SetSelection(IEnumerable<Type> contracts, bool selected)
        {
            foreach (Type contract in contracts)
            {
                Exclude(contract, selected);
            }

            CaptureSelectionState();
        }

        private void Exclude(Type contract, bool selected)
        {
            if (selected)
            {
                _excludedKeys.Remove(ContractKey(contract));
            }
            else
            {
                _excludedKeys.Add(ContractKey(contract));
            }
        }

        private void SetSelectionForVisibleContracts(bool selected)
        {
            SetSelection(_contracts.Where(IsVisible), selected);
        }

        private bool IsVisible(Type contract)
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                return true;
            }

            string filter = _searchFilter.Trim();
            return 0
                    <= ContractDisplayName(contract)
                        .IndexOf(filter, StringComparison.OrdinalIgnoreCase)
                || 0
                    <= AssemblyNameOf(contract).IndexOf(filter, StringComparison.OrdinalIgnoreCase);
        }

        private int CountSelectedAssemblies()
        {
            return _contracts
                .Where(IsSelected)
                .Select(AssemblyNameOf)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        private IEnumerable<Type> ContractsInAssembly(string assemblyName)
        {
            return _contracts.Where(contract =>
                string.Equals(AssemblyNameOf(contract), assemblyName, StringComparison.Ordinal)
            );
        }

        private static string AssemblyNameOf(Type contract)
        {
            return contract.Assembly.GetName().Name;
        }

        private static string NamespaceGroupOf(Type contract)
        {
            return string.IsNullOrEmpty(contract.Namespace)
                ? GlobalNamespaceGroup
                : contract.Namespace;
        }

        // The raw FullName, keeping its '+', so a nested Ns.Outer+Inner and a top-level
        // Ns.Outer.Inner in the same assembly are two keys rather than one.
        private static string ContractKey(Type contract)
        {
            return $"{AssemblyNameOf(contract)}::{contract.FullName ?? contract.Name}";
        }

        private static string ContractDisplayName(Type contract)
        {
            return (contract.FullName ?? contract.Name).Replace('+', '.');
        }

        // The row leads with the type, not the namespace: an assembly's contracts share a namespace
        // prefix, so a full name truncated by the window width shows only what they have in common.
        private static string ShortDisplayName(Type contract)
        {
            string fullName = ContractDisplayName(contract);
            string namespaceName = contract.Namespace;
            if (string.IsNullOrEmpty(namespaceName))
            {
                return fullName;
            }

            return fullName.Substring(namespaceName.Length + 1);
        }

        // Two distinct group keys can sanitize to the same file name, and a schema silently
        // overwriting another schema is the one failure this tool must not have.
        private static string UniqueFileName(string groupKey, HashSet<string> usedFileNames)
        {
            string baseName = PortableFileName(groupKey);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = GlobalNamespaceGroup;
            }

            string candidate = baseName + ".proto";
            int suffix = 2;
            while (!usedFileNames.Add(candidate))
            {
                candidate = $"{baseName}-{suffix}.proto";
                suffix++;
            }

            return candidate;
        }

        private static string PortableFileName(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(InvalidFileNameCharacters.Contains(character) ? '_' : character);
            }

            return builder.ToString();
        }

        private static string ToProjectPathOrAbsolute(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!IsInsideProject(absolutePath, projectRoot))
            {
                return absolutePath;
            }

            return ToProjectPath(absolutePath, projectRoot);
        }

        private static bool IsInsideProject(string absolutePath, string projectRoot)
        {
            string normalized = absolutePath.Replace('\\', '/');
            string root = projectRoot.Replace('\\', '/').TrimEnd('/');
            return normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProjectPath(string absolutePath, string projectRoot)
        {
            string normalized = absolutePath.Replace('\\', '/');
            string root = projectRoot.Replace('\\', '/').TrimEnd('/');
            return normalized.Substring(root.Length + 1);
        }

        /// <summary>
        /// How the selected contracts are distributed across output files.
        /// </summary>
        internal enum ExportLayout
        {
            [Obsolete("Use a specific ExportLayout value instead of None.")]
            None = 0,
            SingleFile = 1,
            OneFilePerAssembly = 2,
            OneFilePerNamespace = 3,
            OneFilePerContract = 4,
        }
    }
}
