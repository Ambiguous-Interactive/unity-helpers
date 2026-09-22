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
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Writes proto3 schemas for selected contracts without showing editor prompts.</summary>
    /// <remarks>File writes are Tier C: an error after an earlier write can leave partial output, and Unity Undo cannot restore overwritten files.</remarks>
    public static class ProtoSchemaExporter
    {
        private const string GlobalGroup = "Global";

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

        /// <summary>Finds the project's concrete proto contracts.</summary>
        public static bool TryDiscoverProjectContracts(
            out IReadOnlyList<Type> contracts,
            out string error
        )
        {
            try
            {
                contracts = TypeCache
                    .GetTypesWithAttribute<WProtoContractAttribute>()
                    .Where(type => !type.IsGenericTypeDefinition)
                    .OrderBy(type => type.Assembly.GetName().Name, StringComparer.Ordinal)
                    .ThenBy(type => DisplayName(type), StringComparer.Ordinal)
                    .ToArray();
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                contracts = Array.Empty<Type>();
                error = $"Could not discover proto contracts: {exception.Message}";
                return false;
            }
        }

        /// <summary>Finds proto surrogates declared by loaded assemblies.</summary>
        public static bool TryDiscoverProjectSurrogates(
            out IReadOnlyDictionary<Type, Type> discovered,
            out string error
        )
        {
            try
            {
                return TryDiscoverProjectSurrogatesCore(out discovered, out error);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                discovered = null;
                error = $"Could not discover proto surrogates: {exception.Message}";
                return false;
            }
        }

        /// <summary>Exports all discovered project contracts to a file or directory.</summary>
        public static ExportResult ExportProject(
            string destination,
            ExportLayout layout,
            string packageName = null
        )
        {
            if (!TryDiscoverProjectContracts(out IReadOnlyList<Type> contracts, out string error))
            {
                return new ExportResult(false, error, Array.Empty<string>(), Array.Empty<string>());
            }

            if (
                !TryDiscoverProjectSurrogates(
                    out IReadOnlyDictionary<Type, Type> surrogates,
                    out error
                )
            )
            {
                return new ExportResult(false, error, Array.Empty<string>(), Array.Empty<string>());
            }

            return Export(contracts, destination, layout, packageName, surrogates);
        }

        /// <summary>Exports selected contracts to a file or directory without opening dialogs.</summary>
        /// <remarks>A null surrogate map discovers loaded project surrogates; an explicit map replaces them.</remarks>
        public static ExportResult Export(
            IEnumerable<Type> contracts,
            string destination,
            ExportLayout layout,
            string packageName = null,
            IReadOnlyDictionary<Type, Type> surrogates = null
        )
        {
            if (
                !TryPlan(
                    contracts,
                    destination,
                    layout,
                    packageName,
                    out List<ExportFile> files,
                    out string error
                )
            )
            {
                return new ExportResult(false, error, Array.Empty<string>(), Array.Empty<string>());
            }

            if (surrogates == null && !TryDiscoverProjectSurrogates(out surrogates, out error))
            {
                return new ExportResult(false, error, Array.Empty<string>(), Array.Empty<string>());
            }

            List<string> diagnostics = new List<string>();
            List<RenderedFile> renderedFiles = new List<RenderedFile>(files.Count);
            string package = string.IsNullOrWhiteSpace(packageName) ? null : packageName.Trim();
            foreach (ExportFile file in files)
            {
                bool rendered;
                string schema;
                IReadOnlyList<string> messages;
                try
                {
                    rendered = WProtoSchemaText.TryWriteSchema(
                        file.Contracts,
                        package,
                        surrogates,
                        out schema,
                        out messages
                    );
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    string target = layout == ExportLayout.SingleFile ? destination : file.Group;
                    return new ExportResult(
                        false,
                        $"Could not render schema for {target}: {exception.Message}",
                        Array.Empty<string>(),
                        diagnostics
                    );
                }
                if (!rendered)
                {
                    diagnostics.Add(
                        layout == ExportLayout.SingleFile
                            ? "Nothing rendered; no [WProtoContract] type in this file."
                            : $"{file.Group}: nothing rendered; no [WProtoContract] type in this group."
                    );
                    continue;
                }

                foreach (string message in messages)
                {
                    diagnostics.Add(
                        layout == ExportLayout.SingleFile ? message : $"{file.Group}: {message}"
                    );
                }

                renderedFiles.Add(new RenderedFile(file.Path, schema));
            }

            if (renderedFiles.Count == 0)
            {
                return new ExportResult(
                    false,
                    "Nothing rendered: no [WProtoContract] types among the selection.",
                    Array.Empty<string>(),
                    diagnostics
                );
            }

            List<string> writtenPaths = new List<string>(renderedFiles.Count);
            try
            {
                foreach (RenderedFile renderedFile in renderedFiles)
                {
                    string parent = Path.GetDirectoryName(renderedFile.Path);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    if (
                        !DurableFile.TryWriteAllText(
                            renderedFile.Path,
                            renderedFile.Schema,
                            out Exception writeError
                        )
                    )
                    {
                        throw new IOException(writeError.Message, writeError);
                    }
                    writtenPaths.Add(renderedFile.Path);
                }
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is System.Security.SecurityException
                )
            {
                string action =
                    layout == ExportLayout.SingleFile
                        ? $"Could not write {destination}"
                        : $"Could not write schemas to {destination}";
                return new ExportResult(
                    false,
                    $"{action}: {exception.Message}",
                    writtenPaths,
                    diagnostics
                );
            }

            string status =
                layout == ExportLayout.SingleFile
                    ? $"Exported {files[0].Contracts.Count} contracts to {destination}."
                    : $"Exported {files.Sum(file => file.Contracts.Count)} contracts to {writtenPaths.Count} files in {destination}.";
            return new ExportResult(true, status, writtenPaths, diagnostics);
        }

        /// <summary>Gets the paths an export would write, for overwrite confirmation.</summary>
        public static bool TryGetOutputPaths(
            IEnumerable<Type> contracts,
            string destination,
            ExportLayout layout,
            string packageName,
            out IReadOnlyList<string> paths,
            out string error
        )
        {
            if (
                !TryPlan(
                    contracts,
                    destination,
                    layout,
                    packageName,
                    out List<ExportFile> files,
                    out error
                )
            )
            {
                paths = Array.Empty<string>();
                return false;
            }

            paths = files.Select(file => file.Path).ToArray();
            return true;
        }

        private static bool TryDiscoverProjectSurrogatesCore(
            out IReadOnlyDictionary<Type, Type> discovered,
            out string error
        )
        {
            Dictionary<Type, Type> surrogates = new Dictionary<Type, Type>();
#if UNITY_6000_6_OR_NEWER
            IReadOnlyList<Assembly> assemblies =
                UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            IReadOnlyList<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies();
#endif
            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    foreach (
                        WProtoSurrogateAttribute surrogate in assembly.GetCustomAttributes<WProtoSurrogateAttribute>()
                    )
                    {
                        if (surrogate.RealType != null && surrogate.SurrogateType != null)
                        {
                            surrogates[surrogate.RealType] = surrogate.SurrogateType;
                        }
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    discovered = null;
                    error =
                        $"Could not read proto surrogates from {assembly.GetName().Name}: {exception.Message}";
                    return false;
                }
            }

            discovered = surrogates;
            error = null;
            return true;
        }

        private static bool TryPlan(
            IEnumerable<Type> contracts,
            string destination,
            ExportLayout layout,
            string packageName,
            out List<ExportFile> files,
            out string error
        )
        {
            try
            {
                return TryPlanCore(
                    contracts,
                    destination,
                    layout,
                    packageName,
                    out files,
                    out error
                );
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                files = new List<ExportFile>();
                error = $"Could not prepare schema export: {exception.Message}";
                return false;
            }
        }

        private static bool TryPlanCore(
            IEnumerable<Type> contracts,
            string destination,
            ExportLayout layout,
            string packageName,
            out List<ExportFile> files,
            out string error
        )
        {
            if (contracts == null)
            {
                return FailPlan(out files, out error, "No contracts selected.");
            }

            List<Type> selected = new List<Type>();
            HashSet<Type> seen = new HashSet<Type>();
            foreach (Type contract in contracts)
            {
                if (contract == null || !contract.IsDefined(typeof(WProtoContractAttribute), false))
                {
                    return FailPlan(
                        out files,
                        out error,
                        "Every selected type must be a [WProtoContract] type."
                    );
                }

                if (seen.Add(contract))
                {
                    selected.Add(contract);
                }
            }
            if (selected.Count == 0)
            {
                return FailPlan(out files, out error, "No contracts selected.");
            }

            selected.Sort(
                (left, right) =>
                {
                    int assemblyOrder = StringComparer.Ordinal.Compare(
                        left.Assembly.GetName().Name,
                        right.Assembly.GetName().Name
                    );
                    return assemblyOrder != 0
                        ? assemblyOrder
                        : StringComparer.Ordinal.Compare(DisplayName(left), DisplayName(right));
                }
            );

            if (layout < ExportLayout.SingleFile || ExportLayout.OneFilePerContract < layout)
            {
                return FailPlan(out files, out error, "Choose a supported file layout.");
            }

            if (!IsValidPackage(packageName))
            {
                return FailPlan(
                    out files,
                    out error,
                    $"\"{packageName}\" is not a proto3 package: use dot-separated identifiers, or clear the field to omit the clause."
                );
            }

            if (!TryResolvePath(destination, out string absolutePath, out string pathError))
            {
                return FailPlan(out files, out error, pathError);
            }

            List<ExportFile> plannedFiles = new List<ExportFile>();
            if (layout == ExportLayout.SingleFile)
            {
                plannedFiles.Add(new ExportFile(string.Empty, absolutePath, selected));
                files = plannedFiles;
                error = null;
                return true;
            }

            IEnumerable<IGrouping<string, Type>> groups = layout switch
            {
                ExportLayout.OneFilePerNamespace => selected.GroupBy(
                    type => string.IsNullOrEmpty(type.Namespace) ? GlobalGroup : type.Namespace,
                    StringComparer.Ordinal
                ),
                ExportLayout.OneFilePerContract => selected.GroupBy(
                    DisplayName,
                    StringComparer.Ordinal
                ),
                _ => selected.GroupBy(type => type.Assembly.GetName().Name, StringComparer.Ordinal),
            };
            HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, Type> group in groups)
            {
                plannedFiles.Add(
                    new ExportFile(
                        group.Key,
                        Path.Combine(absolutePath, UniqueFileName(group.Key, usedNames)),
                        group.ToList()
                    )
                );
            }

            files = plannedFiles;
            error = null;
            return true;
        }

        private static bool FailPlan(out List<ExportFile> files, out string error, string message)
        {
            files = new List<ExportFile>();
            error = message;
            return false;
        }

        private static bool TryResolvePath(string destination, out string path, out string error)
        {
            if (string.IsNullOrWhiteSpace(destination))
            {
                path = null;
                error = "Choose an output path first.";
                return false;
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string resolvedPath = Path.GetFullPath(
                    Path.IsPathRooted(destination)
                        ? destination
                        : Path.Combine(projectRoot, destination)
                );
                if (
                    !Path.IsPathRooted(destination)
                    && !string.Equals(resolvedPath, projectRoot, StringComparison.OrdinalIgnoreCase)
                    && !resolvedPath.StartsWith(
                        projectRoot.TrimEnd(Path.DirectorySeparatorChar)
                            + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    path = null;
                    error = "A relative output path must stay inside the project.";
                    return false;
                }

                string root = Path.GetPathRoot(resolvedPath);
                string[] segments = resolvedPath
                    .Substring(root.Length)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (string segment in segments)
                {
                    foreach (char character in segment)
                    {
                        if (InvalidFileNameCharacters.Contains(character))
                        {
                            path = null;
                            error = "The output path contains an invalid file name.";
                            return false;
                        }
                    }
                }

                path = resolvedPath;
                error = null;
                return true;
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException
                    || exception is System.Security.SecurityException
                )
            {
                path = null;
                error = $"Invalid output path: {exception.Message}";
                return false;
            }
        }

        private static string DisplayName(Type type)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        private static string UniqueFileName(string group, HashSet<string> usedNames)
        {
            StringBuilder builder = new StringBuilder(group.Length);
            foreach (char character in group)
            {
                builder.Append(InvalidFileNameCharacters.Contains(character) ? '_' : character);
            }

            string stem = builder.Length == 0 ? GlobalGroup : builder.ToString();
            string name = stem + ".proto";
            int suffix = 2;
            while (!usedNames.Add(name))
            {
                name = $"{stem}-{suffix}.proto";
                suffix++;
            }

            return name;
        }

        private static bool IsValidPackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return true;
            }

            foreach (string segment in packageName.Trim().Split('.'))
            {
                if (segment.Length == 0 || !(segment[0] == '_' || IsAsciiLetter(segment[0])))
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

        /// <summary>How selected contracts are distributed across output files.</summary>
        public enum ExportLayout
        {
            [Obsolete("Choose an export layout.")]
            None = 0,
            SingleFile = 1,
            OneFilePerAssembly = 2,
            OneFilePerNamespace = 3,
            OneFilePerContract = 4,
        }

        /// <summary>The outcome, written paths, and schema diagnostics of an export.</summary>
        public sealed class ExportResult
        {
            /// <summary>Whether all planned files were written.</summary>
            public bool Success { get; }

            /// <summary>A readable summary or failure reason.</summary>
            public string Message { get; }

            /// <summary>Paths written before the export completed or failed.</summary>
            public IReadOnlyList<string> WrittenPaths { get; }

            /// <summary>Warnings produced while rendering schemas.</summary>
            public IReadOnlyList<string> Diagnostics { get; }

            internal ExportResult(
                bool success,
                string message,
                IReadOnlyList<string> writtenPaths,
                IReadOnlyList<string> diagnostics
            )
            {
                Success = success;
                Message = message;
                WrittenPaths = writtenPaths;
                Diagnostics = diagnostics;
            }
        }

        private sealed class ExportFile
        {
            internal string Group { get; }
            internal string Path { get; }
            internal List<Type> Contracts { get; }

            internal ExportFile(string group, string path, List<Type> contracts)
            {
                Group = group;
                Path = path;
                Contracts = contracts;
            }
        }

        private sealed class RenderedFile
        {
            internal string Path { get; }
            internal string Schema { get; }

            internal RenderedFile(string path, string schema)
            {
                Path = path;
                Schema = schema;
            }
        }
    }
}
