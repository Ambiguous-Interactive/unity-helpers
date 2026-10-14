// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEditor.Compilation;
    using WallstopStudios.UnityHelpers.Core.Extension;

    internal sealed class OdinMigrationCompilationPlan
    {
        internal static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        internal readonly List<Assembly> assemblies = new List<Assembly>();
        internal readonly Dictionary<string, OdinMigrationSourceProposal> proposals =
            new Dictionary<string, OdinMigrationSourceProposal>(PathComparer);
        internal readonly Dictionary<string, string> inputHashes = new Dictionary<string, string>(
            PathComparer
        );
        internal readonly Dictionary<string, string> sourceTextHashes = new Dictionary<
            string,
            string
        >(PathComparer);
        internal readonly Dictionary<string, string> assemblySignatures = new Dictionary<
            string,
            string
        >(StringComparer.Ordinal);
        private readonly string _projectSignature;

        internal OdinMigrationCompilationPlan(
            IReadOnlyDictionary<string, OdinMigrationSourceProposal> sources,
            Assembly[] projectAssemblies
        )
        {
            if (sources == null || sources.Count == 0 || projectAssemblies == null)
            {
                throw new InvalidDataException(
                    "Source proposals and the current editor assembly graph are required."
                );
            }
            _projectSignature = GraphSignature(projectAssemblies);
            foreach (
                KeyValuePair<
                    string,
                    OdinMigrationSourceProposal
                > entry in sources.AsValueEnumerableRequired()
            )
            {
                OdinMigrationSourceProposal proposal = entry.Value;
                if (
                    proposal == null
                    || string.IsNullOrWhiteSpace(proposal.SourcePath)
                    || proposal.OriginalSource == null
                    || proposal.ProposedSource == null
                    || OdinMigrationSourceBinder.MaximumSourceCharacters
                        < proposal.ProposedSource.Length
                )
                {
                    throw new InvalidDataException(
                        "A source proposal is incomplete or exceeds the compiler size limit."
                    );
                }
                string path = OdinMigrationSourceBinder.FullPath(proposal.SourcePath);
                if (!proposals.TryAdd(path, proposal))
                {
                    throw new InvalidDataException("A compiler source is selected more than once.");
                }
            }
            Dictionary<string, Assembly> byName = new Dictionary<string, Assembly>(
                StringComparer.Ordinal
            );
            HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> found = new HashSet<string>(PathComparer);
            foreach (Assembly assembly in projectAssemblies)
            {
                if (
                    assembly == null
                    || string.IsNullOrWhiteSpace(assembly.name)
                    || !byName.TryAdd(assembly.name, assembly)
                )
                {
                    throw new InvalidDataException(
                        "The editor assembly graph has missing or duplicate assembly identities."
                    );
                }
                foreach (string source in assembly.sourceFiles)
                {
                    string path = OdinMigrationSourceBinder.FullPath(source);
                    if (proposals.ContainsKey(path))
                    {
                        if (!found.Add(path))
                        {
                            throw new InvalidDataException(
                                "A selected source belongs to more than one editor assembly."
                            );
                        }
                        selected.Add(assembly.name);
                    }
                }
            }
            if (found.Count != proposals.Count)
            {
                throw new InvalidDataException(
                    "A selected source is absent from the current editor assembly graph."
                );
            }
            bool expanded;
            do
            {
                expanded = false;
                foreach (Assembly assembly in projectAssemblies)
                {
                    foreach (Assembly dependency in assembly.assemblyReferences)
                    {
                        if (selected.Contains(dependency.name) && selected.Add(assembly.name))
                        {
                            expanded = true;
                        }
                    }
                }
            } while (expanded);
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (Assembly assembly in projectAssemblies)
            {
                if (selected.Contains(assembly.name))
                {
                    Visit(assembly, selected, byName, visiting, visited);
                }
            }
            foreach (Assembly assembly in assemblies.AsValueEnumerableRequired())
            {
                ValidateOptions(assembly);
                assemblySignatures.Add(assembly.name, Signature(assembly));
                foreach (string source in assembly.sourceFiles)
                {
                    string path = OdinMigrationSourceBinder.FullPath(source);
                    CaptureInput(path);
                    if (!sourceTextHashes.ContainsKey(path))
                    {
                        sourceTextHashes.Add(
                            path,
                            OdinMigrationSourceBinder.Checksum(
                                OdinMigrationSourceBinder.ReadSource(path)
                            )
                        );
                    }
                }
                foreach (string reference in assembly.allReferences)
                {
                    CaptureInput(OdinMigrationSourceBinder.FullPath(reference));
                }
                OdinMigrationCompilerOptionsSnapshot options =
                    new OdinMigrationCompilerOptionsSnapshot(assembly.compilerOptions);
                foreach (string input in options.inputPaths)
                {
                    CaptureInput(OdinMigrationSourceBinder.FullPath(input));
                }
            }
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> proposal in proposals)
            {
                if (
                    !string.Equals(
                        OdinMigrationSourceBinder.ReadSource(proposal.Key),
                        proposal.Value.OriginalSource,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidDataException(
                        "A selected source changed after its preview was constructed."
                    );
                }
            }
            ValidateCurrent();
        }

        internal static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return Convert.ToBase64String(hash.ComputeHash(stream));
            }
        }

        private static void ValidateOptions(Assembly assembly)
        {
            if (
                assembly.compilerOptions.ResponseFiles != null
                && assembly.compilerOptions.ResponseFiles.Length != 0
            )
            {
                throw new InvalidDataException(
                    "Response-file compilation requires explicit migration compiler support: "
                        + assembly.name
                );
            }
            foreach (
                string argument in assembly.compilerOptions.AdditionalCompilerArguments
                    ?? Array.Empty<string>()
            )
            {
                if (
                    !argument.StartsWith("/nowarn:", StringComparison.OrdinalIgnoreCase)
                    && !argument.StartsWith("-nowarn:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    throw new InvalidDataException(
                        "An additional compiler argument requires explicit migration compiler support: "
                            + assembly.name
                    );
                }
            }
        }

        private static string Signature(Assembly assembly)
        {
            StringBuilder signature = new StringBuilder();
            signature.Append(assembly.name).Append('\n').Append(assembly.flags).Append('\n');
            foreach (string value in assembly.sourceFiles)
            {
                signature.Append(value).Append('\n');
            }
            foreach (string value in assembly.defines)
            {
                signature.Append(value).Append('\n');
            }
            foreach (string value in assembly.allReferences)
            {
                signature.Append(value).Append('\n');
            }
            signature.Append(
                new OdinMigrationCompilerOptionsSnapshot(assembly.compilerOptions).signature
            );
            return signature.ToString();
        }

        private static string GraphSignature(Assembly[] assemblies)
        {
            SortedDictionary<string, string> signatures = new SortedDictionary<string, string>(
                StringComparer.Ordinal
            );
            foreach (Assembly assembly in assemblies)
            {
                if (
                    assembly == null
                    || string.IsNullOrWhiteSpace(assembly.name)
                    || !signatures.TryAdd(assembly.name, Signature(assembly))
                )
                {
                    throw new InvalidDataException(
                        "The current editor assembly graph is incomplete or ambiguous."
                    );
                }
            }
            return string.Join("\n", signatures.Values);
        }

        internal void ValidateCurrent()
        {
            foreach (KeyValuePair<string, string> input in inputHashes)
            {
                if (!string.Equals(input.Value, HashFile(input.Key), StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "A compiler input changed during migration validation: " + input.Key
                    );
                }
            }
            Assembly[] current = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            if (
                !string.Equals(_projectSignature, GraphSignature(current), StringComparison.Ordinal)
            )
            {
                throw new InvalidDataException(
                    "The editor assembly graph changed during migration validation."
                );
            }
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            foreach (Assembly assembly in current)
            {
                if (assemblySignatures.TryGetValue(assembly.name, out string signature))
                {
                    if (
                        !found.Add(assembly.name)
                        || !string.Equals(signature, Signature(assembly), StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidDataException(
                            "The editor compiler configuration changed during migration validation."
                        );
                    }
                }
            }
            if (found.Count != assemblySignatures.Count)
            {
                throw new InvalidDataException(
                    "A selected editor assembly disappeared during migration validation."
                );
            }
        }

        private void CaptureInput(string path)
        {
            if (!inputHashes.ContainsKey(path))
            {
                inputHashes.Add(path, HashFile(path));
            }
        }

        private void Visit(
            Assembly assembly,
            HashSet<string> selected,
            Dictionary<string, Assembly> byName,
            HashSet<string> visiting,
            HashSet<string> visited
        )
        {
            if (visited.Contains(assembly.name))
            {
                return;
            }
            if (!visiting.Add(assembly.name))
            {
                throw new InvalidDataException(
                    "The selected editor assembly graph contains a cycle."
                );
            }
            foreach (Assembly dependency in assembly.assemblyReferences)
            {
                if (selected.Contains(dependency.name))
                {
                    if (!byName.TryGetValue(dependency.name, out Assembly owner))
                    {
                        throw new InvalidDataException(
                            "A selected assembly dependency is unavailable."
                        );
                    }
                    Visit(owner, selected, byName, visiting, visited);
                }
            }
            visiting.Remove(assembly.name);
            visited.Add(assembly.name);
            assemblies.Add(assembly);
        }
    }
}
