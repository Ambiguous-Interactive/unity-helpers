// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using Object = UnityEngine.Object;
    using RuntimeAssembly = System.Reflection.Assembly;

    internal static class OdinMigrationApplySnapshot
    {
        internal static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        internal static OdinMigrationApplyRecord Capture(
            OdinMigrationDictionaryMigrationPreview preview,
            string backupRoot,
            string epoch,
            out List<OdinMigrationFilePlan> plans
        )
        {
            List<OdinMigrationFilePlan> captured = new List<OdinMigrationFilePlan>();
            List<OdinMigrationApplyRecord.FileSnapshot> files =
                new List<OdinMigrationApplyRecord.FileSnapshot>();
            HashSet<string> assemblyNames = new HashSet<string>(StringComparer.Ordinal);
            long batchBytes = 0;
            foreach (OdinMigrationSourceProposal source in preview.Sources.Values)
            {
                string path = AssetPath(source.SourcePath);
                AddFile(
                    path,
                    source.OriginalSource,
                    source.ProposedSource,
                    captured,
                    files,
                    ref batchBytes
                );
                string assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                if (string.IsNullOrWhiteSpace(assembly))
                {
                    throw new IOException(
                        "A rewritten source has no current Unity compilation assembly."
                    );
                }
                assemblyNames.Add(
                    assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        ? assembly.Substring(0, assembly.Length - 4)
                        : assembly
                );
            }
            foreach (OdinMigrationAssetProposal asset in preview.Assets.Values)
            {
                AddFile(
                    asset.AssetPath,
                    asset.OriginalSource,
                    asset.ProposedSource,
                    captured,
                    files,
                    ref batchBytes
                );
            }
            List<OdinMigrationApplyRecord.Document> documents =
                new List<OdinMigrationApplyRecord.Document>();
            int delivered = 0;
            long recordBytes = 0;
            foreach (OdinMigrationDictionaryTarget target in preview.Targets)
            {
                if (
                    !preview.Assets.TryGetValue(
                        target.AssetPath,
                        out OdinMigrationAssetProposal asset
                    )
                    || !OdinMigrationTool.TryReadSerializedNodes(
                        asset.OriginalSource,
                        out IReadOnlyList<OdinMigrationSerializedData> payloads,
                        out string readFailure
                    )
                )
                {
                    throw new IOException("A retained document has no complete original payload.");
                }
                OdinMigrationSerializedData selected = null;
                foreach (OdinMigrationSerializedData payload in payloads)
                {
                    if (payload.FileId == target.FileId)
                    {
                        if (selected != null)
                        {
                            throw new IOException("A retained document identifier is ambiguous.");
                        }
                        selected = payload;
                    }
                }
                if (
                    selected == null
                    || !OdinMigrationTool.TryBindSerializedDictionaryFields(
                        selected,
                        target.Fields,
                        out OdinMigrationCompiledBinding binding,
                        out string bindingFailure
                    )
                )
                {
                    throw new IOException(
                        "The original retained dictionary binding is missing or stale."
                    );
                }
                string guid = AssetDatabase.AssetPathToGUID(target.AssetPath);
                OdinMigrationObjectReferenceValidator references =
                    new OdinMigrationObjectReferenceValidator(
                        selected,
                        guid,
                        target.ReferencedObjects
                    );
                if (
                    !OdinMigrationDictionaryConverter.TryDecode(
                        selected,
                        target.Fields,
                        references,
                        out IReadOnlyList<OdinMigrationConvertedDictionary> decoded,
                        out string failure
                    )
                )
                {
                    throw new IOException(failure);
                }
                Dictionary<string, string> identities = new Dictionary<string, string>(
                    StringComparer.Ordinal
                );
                int referenceIndex = 0;
                foreach (string address in selected.ReferencedUnityObjects)
                {
                    Object reference = target.ReferencedObjects[referenceIndex];
                    string identity =
                        reference == null
                            ? string.Empty
                            : reference.GetType().AssemblyQualifiedName;
                    if (
                        identities.TryGetValue(address, out string previous)
                        && !string.Equals(identity, previous, StringComparison.Ordinal)
                    )
                    {
                        throw new IOException(
                            "A retained reference address has conflicting runtime types."
                        );
                    }
                    identities[address] = identity;
                    ++referenceIndex;
                }
                List<OdinMigrationApplyRecord.Field> fields =
                    new List<OdinMigrationApplyRecord.Field>();
                foreach (OdinMigrationConvertedDictionary dictionary in decoded)
                {
                    OdinMigrationApplyRecord.RequireItem(ref delivered);
                    List<OdinMigrationApplyRecord.Entry> entries =
                        new List<OdinMigrationApplyRecord.Entry>();
                    int entryCount = dictionary.Keys.Count;
                    for (int entry = 0; entry < entryCount; ++entry)
                    {
                        OdinMigrationApplyRecord.RequireItem(ref delivered);
                        List<OdinMigrationApplyRecord.Reference> values =
                            new List<OdinMigrationApplyRecord.Reference>();
                        foreach (string address in dictionary.Values[entry])
                        {
                            OdinMigrationApplyRecord.RequireItem(ref delivered);
                            if (!identities.TryGetValue(address, out string identity))
                            {
                                if (!AuthoredAssetYaml.IsNullObjectReference(address))
                                {
                                    throw new IOException(
                                        "A converted reference is absent from the retained table."
                                    );
                                }
                                identity = string.Empty;
                            }
                            recordBytes +=
                                Encoding.UTF8.GetByteCount(address)
                                + (long)Encoding.UTF8.GetByteCount(identity)
                                + 256;
                            if (OdinMigrationApplyRecord.MaximumBytes < recordBytes)
                            {
                                throw new IOException(
                                    "Captured dictionary values exceed the recovery record limit."
                                );
                            }
                            values.Add(
                                new OdinMigrationApplyRecord.Reference
                                {
                                    address = address,
                                    typeIdentity = identity,
                                }
                            );
                        }
                        entries.Add(
                            new OdinMigrationApplyRecord.Entry
                            {
                                key = dictionary.Keys[entry],
                                references = values.ToArray(),
                            }
                        );
                    }
                    fields.Add(
                        new OdinMigrationApplyRecord.Field
                        {
                            name = dictionary.FieldName,
                            entries = entries.ToArray(),
                        }
                    );
                }
                if (OdinMigrationApplyRecord.MaximumDocuments <= documents.Count)
                {
                    throw new IOException(
                        "The captured dictionary document count exceeds its limit."
                    );
                }
                documents.Add(
                    new OdinMigrationApplyRecord.Document
                    {
                        assetPath = target.AssetPath,
                        fileId = target.FileId,
                        owningGuid = guid,
                        scriptGuid = selected.ScriptGuid,
                        ownerTypeIdentity = binding.OriginalType.AssemblyQualifiedName,
                        fields = fields.ToArray(),
                    }
                );
            }
            List<OdinMigrationApplyRecord.AssemblySnapshot> assemblies =
                new List<OdinMigrationApplyRecord.AssemblySnapshot>();
            foreach (string name in assemblyNames)
            {
                assemblies.Add(
                    new OdinMigrationApplyRecord.AssemblySnapshot
                    {
                        name = name,
                        moduleVersion = ModuleVersion(name),
                    }
                );
            }
            plans = captured;
            return new OdinMigrationApplyRecord
            {
                backupName = Path.GetFileName(backupRoot),
                projectHash = OdinMigrationBackupReceipt.Hash(Encoding.UTF8.GetBytes(ProjectRoot)),
                state = OdinMigrationApplyState.Prepared,
                importEpoch = epoch,
                requiresReload = true,
                deadlineUtcTicks = DateTime.UtcNow.AddMinutes(15).Ticks,
                documents = documents.ToArray(),
                files = files.ToArray(),
                assemblies = assemblies.ToArray(),
                originallyLoadedScenes = Array.Empty<string>(),
                openedScenes = Array.Empty<string>(),
                originalActiveScene = SceneManager.GetActiveScene().path,
                temporaryScene = string.Empty,
            };
        }

        internal static string ModuleVersion(string name)
        {
            RuntimeAssembly found = null;
            foreach (RuntimeAssembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal))
                {
                    if (found != null)
                    {
                        throw new IOException(
                            "A migrated source assembly has more than one loaded identity."
                        );
                    }
                    found = assembly;
                }
            }
            if (found == null)
            {
                throw new IOException("A migrated source assembly is not loaded: " + name);
            }
            return found.ManifestModule.ModuleVersionId.ToString("D");
        }

        internal static void ValidateFiles(OdinMigrationApplyRecord record, bool original)
        {
            foreach (OdinMigrationApplyRecord.FileSnapshot file in record.files)
            {
                string path = OdinMigrationTool.GetValidatedAssetPath(file.assetPath);
                string hash = OdinMigrationBackupReceipt.Hash(
                    OdinMigrationBackupReceipt.ReadBounded(
                        path,
                        OdinMigrationBackupReceipt.MaximumFileBytes
                    )
                );
                string metadata = OdinMigrationBackupReceipt.Hash(
                    OdinMigrationBackupReceipt.ReadBounded(
                        OdinMigrationTool.GetValidatedAssetPath(file.assetPath + ".meta"),
                        OdinMigrationBackupReceipt.MaximumFileBytes
                    )
                );
                if (
                    !string.Equals(
                        hash,
                        original ? file.originalHash : file.proposedHash,
                        StringComparison.Ordinal
                    ) || !string.Equals(metadata, file.metadataHash, StringComparison.Ordinal)
                )
                {
                    throw new IOException(
                        "A migration file or its metadata differs from its captured bytes: "
                            + file.assetPath
                    );
                }
            }
        }

        private static void AddFile(
            string assetPath,
            string originalSource,
            string proposedSource,
            List<OdinMigrationFilePlan> plans,
            List<OdinMigrationApplyRecord.FileSnapshot> snapshots,
            ref long batchBytes
        )
        {
            string fullPath = OdinMigrationTool.GetValidatedAssetPath(assetPath);
            byte[] original = OdinMigrationBackupReceipt.ReadBounded(
                fullPath,
                OdinMigrationBackupReceipt.MaximumFileBytes
            );
            if (
                !OdinMigrationEncodedSource.TryDecode(
                    original,
                    out OdinMigrationDecodedSource decoded,
                    out string failure
                ) || !string.Equals(decoded.Source, originalSource, StringComparison.Ordinal)
            )
            {
                throw new IOException(
                    failure ?? "A migration file changed after the paired preview."
                );
            }
            string metaPath = OdinMigrationTool.GetValidatedAssetPath(assetPath + ".meta");
            byte[] metadata = OdinMigrationBackupReceipt.ReadBounded(
                metaPath,
                OdinMigrationBackupReceipt.MaximumFileBytes
            );
            string currentGuid = AssetDatabase.AssetPathToGUID(assetPath);
            using (
                StringReader lines = new StringReader(
                    new UTF8Encoding(false, true).GetString(metadata)
                )
            )
            {
                string guid = null;
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    if (line.StartsWith("guid:", StringComparison.Ordinal))
                    {
                        if (guid != null)
                        {
                            throw new IOException("An asset metadata GUID occurs more than once.");
                        }
                        guid = line.Substring(5).Trim();
                    }
                }
                if (
                    !Guid.TryParseExact(guid, "N", out Guid _)
                    || !string.Equals(guid, currentGuid, StringComparison.OrdinalIgnoreCase)
                )
                {
                    throw new IOException(
                        "An asset metadata GUID differs from the current Asset Database identity."
                    );
                }
            }
            byte[] proposed = OdinMigrationEncodedSource.Encode(decoded, proposedSource);
            batchBytes += original.Length + (long)proposed.Length + 2L * metadata.Length;
            if (
                OdinMigrationBackupReceipt.MaximumFileBytes < proposed.Length
                || OdinMigrationBackupReceipt.MaximumBatchBytes < batchBytes
            )
            {
                throw new IOException("Captured migration bytes exceed the file or batch limit.");
            }
            plans.Add(
                new OdinMigrationFilePlan(
                    assetPath,
                    Path.GetFullPath(fullPath),
                    original,
                    proposed,
                    null
                )
            );
            snapshots.Add(
                new OdinMigrationApplyRecord.FileSnapshot
                {
                    assetPath = assetPath,
                    originalHash = OdinMigrationBackupReceipt.Hash(original),
                    proposedHash = OdinMigrationBackupReceipt.Hash(proposed),
                    metadataHash = OdinMigrationBackupReceipt.Hash(metadata),
                }
            );
        }

        private static string AssetPath(string source)
        {
            string full = OdinMigrationSourceBinder.FullPath(source);
            string prefix =
                Path.GetFullPath(ProjectRoot).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (
                full.Length <= prefix.Length
                || !OdinMigrationCompilationPlan.PathComparer.Equals(
                    full.Substring(0, prefix.Length),
                    prefix
                )
            )
            {
                throw new IOException(
                    "Migration source apply requires writable project Assets scripts."
                );
            }
            string path = full.Substring(prefix.Length).Replace('\\', '/');
            OdinMigrationTool.GetValidatedAssetPath(path);
            return path;
        }
    }
}
