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
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Exports the proto3 schema of the project's <see cref="WProtoContractAttribute"/> types so a
    /// downstream consumer can decode the bytes with any protobuf toolchain.
    /// </summary>
    /// <remarks>
    /// Undo policy: Tier C. The export writes one schema file; a prior file at the chosen path is
    /// overwritten and is not recoverable through Unity's undo system. No asset is modified, so
    /// nothing else is touched.
    /// </remarks>
    public sealed class ProtoSchemaExporterWindow : EditorWindow
    {
        private const string DefaultOutputPath = "Assets/wallstop-schema.proto";

        // Static rather than injected: the window is a root object, and tests drive the export
        // through it without touching the OS file dialog.
        internal static bool SuppressUserPrompts;

        private List<string> _assemblyNames = new List<string>();
        private HashSet<string> _selectedAssemblies = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<Type, Type> _surrogates = new Dictionary<Type, Type>();
        private string _outputPath = DefaultOutputPath;
        private Vector2 _scrollPosition;
        private readonly List<string> _lastDiagnostics = new List<string>();
        private string _lastStatus;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Proto Schema Exporter")]
        public static void ShowWindow()
        {
            ProtoSchemaExporterWindow window = GetWindow<ProtoSchemaExporterWindow>(
                "Proto Schema Exporter"
            );
            window.RefreshInventory();
        }

        private void OnEnable()
        {
            RefreshInventory();
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Refresh Contracts"))
            {
                RefreshInventory();
            }

            EditorGUILayout.HelpBox(
                _assemblyNames.Count == 0
                    ? "No [WProtoContract] types found in the project."
                    : $"{CountSelectedContracts()} contracts across {_selectedAssemblies.Count} assemblies.",
                MessageType.Info
            );

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (string assemblyName in _assemblyNames)
            {
                bool selected = _selectedAssemblies.Contains(assemblyName);
                bool nowSelected = EditorGUILayout.Toggle(assemblyName, selected);
                if (nowSelected)
                {
                    _selectedAssemblies.Add(assemblyName);
                }
                else
                {
                    _selectedAssemblies.Remove(assemblyName);
                }
            }

            EditorGUILayout.EndScrollView();

            _outputPath = EditorGUILayout.TextField("Output Path", _outputPath);

            EditorGUI.BeginDisabledGroup(_selectedAssemblies.Count == 0);
            if (GUILayout.Button("Export Schema"))
            {
                ExportSchema();
            }

            EditorGUI.EndDisabledGroup();

            if (!SuppressUserPrompts && GUILayout.Button("Browse..."))
            {
                string directory = string.IsNullOrEmpty(_outputPath)
                    ? "Assets"
                    : Path.GetDirectoryName(_outputPath);
                string chosen = EditorUtility.SaveFilePanel(
                    "Export Proto Schema",
                    directory,
                    Path.GetFileNameWithoutExtension(_outputPath),
                    "proto"
                );
                if (!string.IsNullOrEmpty(chosen))
                {
                    _outputPath = ToProjectPathOrAbsolute(chosen);
                }
            }

            if (!string.IsNullOrEmpty(_lastStatus))
            {
                EditorGUILayout.HelpBox(_lastStatus, MessageType.Info);
            }

            foreach (string diagnostic in _lastDiagnostics)
            {
                EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
            }
        }

        internal void RefreshInventory()
        {
            _assemblyNames.Clear();
            _selectedAssemblies.Clear();
            _surrogates.Clear();

            HashSet<string> assemblyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type contract in TypeCache.GetTypesWithAttribute<WProtoContractAttribute>())
            {
                if (contract.IsGenericTypeDefinition)
                {
                    continue;
                }

                assemblyNames.Add(contract.Assembly.GetName().Name);
            }

            _assemblyNames = assemblyNames.OrderBy(name => name, StringComparer.Ordinal).ToList();
            foreach (string assemblyName in _assemblyNames)
            {
                _selectedAssemblies.Add(assemblyName);
            }

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
            _selectedAssemblies = new HashSet<string>(
                assemblyNames ?? Array.Empty<string>(),
                StringComparer.Ordinal
            );
        }

        internal string LastStatusForTest => _lastStatus;

        internal bool ExportSchemaToPath(string outputPath)
        {
            _lastDiagnostics.Clear();

            List<Type> contracts = SelectedContracts();
            if (contracts.Count == 0)
            {
                _lastStatus = "No contracts selected.";
                return false;
            }

            bool rendered = WProtoSchemaText.TryWriteSchema(
                contracts,
                null,
                _surrogates,
                out string schema,
                out IReadOnlyList<string> diagnostics
            );
            if (!rendered)
            {
                _lastStatus = "Nothing rendered: no [WProtoContract] types among the selection.";
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
                _lastStatus = $"Could not write {outputPath}: {exception.Message}";
                return false;
            }
            _lastStatus = $"Exported {contracts.Count} contracts to {outputPath}.";
            return true;
        }

        private void ExportSchema()
        {
            string outputPath = _outputPath;
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                _lastStatus = "Choose an output path first.";
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.Combine(projectRoot, outputPath);
            }

            if (File.Exists(outputPath) && !SuppressUserPrompts)
            {
                if (
                    !EditorUtility.DisplayDialog(
                        "Overwrite schema?",
                        $"{outputPath} already exists. Overwrite it?",
                        "Overwrite",
                        "Cancel"
                    )
                )
                {
                    return;
                }
            }

            if (ExportSchemaToPath(outputPath))
            {
                string projectPath = ToProjectPath(outputPath, projectRoot);
                if (IsInsideProject(outputPath, projectRoot))
                {
                    AssetDatabase.ImportAsset(projectPath);
                    AssetDatabase.Refresh();
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
        }

        private List<Type> SelectedContracts()
        {
            List<Type> contracts = new List<Type>();
            foreach (Type contract in TypeCache.GetTypesWithAttribute<WProtoContractAttribute>())
            {
                if (contract.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (_selectedAssemblies.Contains(contract.Assembly.GetName().Name))
                {
                    contracts.Add(contract);
                }
            }

            contracts.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return contracts;
        }

        private int CountSelectedContracts()
        {
            return SelectedContracts().Count;
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
    }
}
