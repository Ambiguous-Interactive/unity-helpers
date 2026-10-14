// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [Serializable]
    internal sealed class OdinMigrationApplyRecord
    {
        internal const string FileName = "dictionary-apply.txt";
        internal const int MaximumBytes = 16 * 1024 * 1024;
        internal const int MaximumDocuments = 4096;
        internal const int MaximumValues = 65536;
        private const string Header = "WallstopOdinDictionaryApply:2";
        private const string LegacyHeader = "WallstopOdinDictionaryApply:1";

        private static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);

        [SerializeField]
        internal int version = 2;

        [SerializeField]
        internal string backupName;

        [SerializeField]
        internal string projectHash;

        [SerializeField]
        internal string importEpoch;

        [SerializeField]
        internal bool requiresReload;

        [SerializeField]
        internal FileSnapshot[] files;

        [SerializeField]
        internal string[] affectedPaths;

        [SerializeField]
        internal OdinMigrationApplyState state;

        [SerializeField]
        internal long deadlineUtcTicks;

        [SerializeField]
        internal bool compilationFailed;

        [SerializeField]
        internal string failure;

        [SerializeField]
        internal Document[] documents;

        [SerializeField]
        internal AssemblySnapshot[] assemblies;

        [SerializeField]
        internal string[] originallyLoadedScenes;

        [SerializeField]
        internal string[] openedScenes;

        [SerializeField]
        internal string originalActiveScene;

        [SerializeField]
        internal string temporaryScene;

        private string _persistedHash;

        internal static bool TryRead(
            string backupRoot,
            out OdinMigrationApplyRecord record,
            out string failure
        )
        {
            try
            {
                UnityMainThreadGuard.EnsureMainThread();
                string root = OdinMigrationTool.GetValidatedBackupRoot(backupRoot);
                string path = Path.Combine(root, FileName);
                OdinMigrationTool.EnsureNoReparsePoints(
                    Directory.GetParent(Application.dataPath).FullName,
                    path
                );
                string text = Encoding.GetString(
                    OdinMigrationBackupReceipt.ReadBounded(path, MaximumBytes)
                );
                int first = text.IndexOf('\n');
                int second = first < 0 ? -1 : text.IndexOf('\n', first + 1);
                if (
                    first < 0
                    || second < 0
                    || (
                        !string.Equals(text.Substring(0, first), Header, StringComparison.Ordinal)
                        && !string.Equals(
                            text.Substring(0, first),
                            LegacyHeader,
                            StringComparison.Ordinal
                        )
                    )
                )
                {
                    throw new IOException(
                        "The dictionary apply recovery record has an invalid version."
                    );
                }
                string json = text.Substring(second + 1);
                if (
                    !string.Equals(
                        text.Substring(first + 1, second - first - 1),
                        OdinMigrationBackupReceipt.Hash(Encoding.GetBytes(json)),
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new IOException(
                        "The dictionary apply recovery record changed or is incomplete."
                    );
                }
                ValidateDepth(json);
                OdinMigrationApplyRecord result = JsonUtility.FromJson<OdinMigrationApplyRecord>(
                    json
                );
                Validate(result, Path.GetFileName(root));
                if (
                    !string.Equals(
                        text.Substring(0, first),
                        result.version == 1 ? LegacyHeader : Header,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new IOException(
                        "The dictionary recovery record version differs from its header."
                    );
                }
                result._persistedHash = OdinMigrationBackupReceipt.Hash(Encoding.GetBytes(text));
                record = result;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                record = null;
                failure = exception.Message;
                return false;
            }
        }

        internal static void Validate(OdinMigrationApplyRecord record, string expectedName)
        {
            int documentCount =
                record == null || record.documents == null ? 0 : record.documents.Length;
            if (
                record == null
                || (record.version != 1 && record.version != 2)
                || !string.Equals(record.backupName, expectedName, StringComparison.Ordinal)
                || (int)record.state < 1
                || 7 < (int)record.state
                || record.deadlineUtcTicks <= 0
                || documentCount == 0
                || MaximumDocuments < documentCount
                || record.assemblies == null
                || record.assemblies.Length == 0
                || record.originallyLoadedScenes == null
                || record.openedScenes == null
                || record.affectedPaths == null
                || record.files == null
                || record.files.Length == 0
                || !Guid.TryParseExact(record.importEpoch, "N", out Guid _)
                || !string.Equals(
                    record.projectHash,
                    OdinMigrationBackupReceipt.Hash(
                        Encoding.GetBytes(OdinMigrationApplySnapshot.ProjectRoot)
                    ),
                    StringComparison.Ordinal
                )
            )
            {
                throw new IOException("The dictionary apply recovery record is incomplete.");
            }
            int values = 0;
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (FileSnapshot file in record.files)
            {
                OdinMigrationTool.GetValidatedAssetPath(file.assetPath);
                if (
                    !paths.Add(file.assetPath)
                    || !IsHash(file.originalHash)
                    || !IsHash(file.proposedHash)
                    || !IsHash(file.metadataHash)
                )
                {
                    throw new IOException("A dictionary apply file snapshot is incomplete.");
                }
            }
            foreach (string path in record.affectedPaths)
            {
                OdinMigrationTool.GetValidatedAssetPath(path);
            }
            if (!string.IsNullOrWhiteSpace(record.originalActiveScene))
            {
                ValidateScene(record.originalActiveScene);
            }
            foreach (string path in record.originallyLoadedScenes)
            {
                ValidateScene(path);
            }
            foreach (string path in record.openedScenes)
            {
                ValidateScene(path);
            }
            foreach (AssemblySnapshot assembly in record.assemblies)
            {
                if (
                    string.IsNullOrWhiteSpace(assembly.name)
                    || !Guid.TryParseExact(assembly.moduleVersion, "D", out Guid _)
                )
                {
                    throw new IOException("A dictionary apply assembly snapshot is incomplete.");
                }
            }
            if (
                !string.IsNullOrWhiteSpace(record.temporaryScene)
                && !string.Equals(
                    record.temporaryScene,
                    "OdinMigration-" + expectedName,
                    StringComparison.Ordinal
                )
            )
            {
                throw new IOException(
                    "A dictionary apply temporary scene has an invalid identity."
                );
            }
            HashSet<string> owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (Document document in record.documents)
            {
                if (record.version == 1 && document.liveCaptured)
                {
                    throw new IOException(
                        "A legacy dictionary recovery record cannot restore live captures."
                    );
                }
                OdinMigrationTool.GetValidatedAssetPath(document.assetPath);
                if (
                    !Guid.TryParseExact(document.owningGuid, "N", out Guid _)
                    || !Guid.TryParseExact(document.scriptGuid, "N", out _)
                    || document.fileId == 0
                    || !paths.Contains(document.assetPath)
                    || !owners.Add(
                        document.assetPath
                            + ":"
                            + document.fileId.ToString(CultureInfo.InvariantCulture)
                    )
                    || string.IsNullOrWhiteSpace(document.ownerTypeIdentity)
                    || 2048 < document.ownerTypeIdentity.Length
                    || document.fields == null
                    || document.fields.Length == 0
                )
                {
                    throw new IOException("A dictionary apply document has no identity or fields.");
                }
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                foreach (Field field in document.fields)
                {
                    RequireItem(ref values);
                    if (
                        string.IsNullOrWhiteSpace(field.name)
                        || !names.Add(field.name)
                        || field.entries == null
                    )
                    {
                        throw new IOException("A dictionary apply field is incomplete.");
                    }
                    HashSet<int> keys = new HashSet<int>();
                    foreach (Entry entry in field.entries)
                    {
                        RequireItem(ref values);
                        if (!keys.Add(entry.key) || entry.references == null)
                        {
                            throw new IOException("A dictionary apply entry is incomplete.");
                        }
                        foreach (Reference reference in entry.references)
                        {
                            if (
                                string.IsNullOrWhiteSpace(reference.address)
                                || MaximumValues <= values
                                || reference.typeIdentity == null
                                || 2048 < reference.typeIdentity.Length
                            )
                            {
                                throw new IOException(
                                    "Dictionary apply references are invalid or exceed their limit."
                                );
                            }
                            ++values;
                        }
                    }
                }
            }
        }

        internal static void RequireItem(ref int count)
        {
            if (MaximumValues <= count)
            {
                throw new IOException("Dictionary apply values exceed their record limit.");
            }
            ++count;
        }

        private static bool IsHash(string text)
        {
            if (text == null || text.Length != 64)
            {
                return false;
            }
            foreach (char character in text)
            {
                if (
                    !('0' <= character && character <= '9')
                    && !('a' <= character && character <= 'f')
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateScene(string path)
        {
            OdinMigrationTool.GetValidatedAssetPath(path);
            if (!path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("A dictionary apply scene path must identify a saved scene.");
            }
        }

        private static void ValidateDepth(string json)
        {
            int depth = 0;
            bool quoted = false;
            bool escaped = false;
            foreach (char character in json)
            {
                if (quoted)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        quoted = false;
                    }
                }
                else if (character == '"')
                {
                    quoted = true;
                }
                else if (character == '{' || character == '[')
                {
                    ++depth;
                    if (16 < depth)
                    {
                        throw new IOException(
                            "The dictionary apply recovery record exceeds its nesting limit."
                        );
                    }
                }
                else if (character == '}' || character == ']')
                {
                    --depth;
                    if (depth < 0)
                    {
                        throw new IOException(
                            "The dictionary apply recovery record has invalid nesting."
                        );
                    }
                }
            }
            if (quoted || depth != 0)
            {
                throw new IOException("The dictionary apply recovery record is truncated.");
            }
        }

        internal void Write(string backupRoot)
        {
            UnityMainThreadGuard.EnsureMainThread();
            string root = OdinMigrationTool.GetValidatedBackupRoot(backupRoot);
            Validate(this, Path.GetFileName(root));
            string json = JsonUtility.ToJson(this);
            string text =
                (version == 1 ? LegacyHeader : Header)
                + "\n"
                + OdinMigrationBackupReceipt.Hash(Encoding.GetBytes(json))
                + "\n"
                + json;
            byte[] bytes = Encoding.GetBytes(text);
            if (MaximumBytes < bytes.Length)
            {
                throw new IOException(
                    "The dictionary apply recovery record exceeds its size limit."
                );
            }
            string path = Path.Combine(root, FileName);
            OdinMigrationTool.EnsureNoReparsePoints(
                Directory.GetParent(Application.dataPath).FullName,
                path
            );
            ValidateCurrent(root);
            if (!DurableFile.TryWriteAllBytes(path, bytes, out Exception error))
            {
                throw new IOException(
                    "Could not retain the dictionary apply recovery record.",
                    error
                );
            }
            _persistedHash = OdinMigrationBackupReceipt.Hash(bytes);
        }

        internal void ValidateCurrent(string backupRoot)
        {
            string path = Path.Combine(
                OdinMigrationTool.GetValidatedBackupRoot(backupRoot),
                FileName
            );
            OdinMigrationTool.EnsureNoReparsePoints(OdinMigrationApplySnapshot.ProjectRoot, path);
            if (_persistedHash == null)
            {
                if (File.Exists(path))
                {
                    throw new IOException(
                        "A dictionary apply record already occupies the recovery path."
                    );
                }
                return;
            }
            if (
                !string.Equals(
                    _persistedHash,
                    OdinMigrationBackupReceipt.Hash(
                        OdinMigrationBackupReceipt.ReadBounded(path, MaximumBytes)
                    ),
                    StringComparison.Ordinal
                )
            )
            {
                throw new IOException("The dictionary apply recovery record changed externally.");
            }
        }

        [Serializable]
        internal struct Document
        {
            [SerializeField]
            internal string assetPath;

            [SerializeField]
            internal long fileId;

            [SerializeField]
            internal string owningGuid;

            [SerializeField]
            internal string scriptGuid;

            [SerializeField]
            internal string ownerTypeIdentity;

            [SerializeField]
            internal bool liveCaptured;

            [SerializeField]
            internal Field[] fields;
        }

        [Serializable]
        internal struct Field
        {
            [SerializeField]
            internal string name;

            [SerializeField]
            internal Entry[] entries;
        }

        [Serializable]
        internal struct Entry
        {
            [SerializeField]
            internal int key;

            [SerializeField]
            internal Reference[] references;
        }

        [Serializable]
        internal struct Reference
        {
            [SerializeField]
            internal string address;

            [SerializeField]
            internal string typeIdentity;
        }

        [Serializable]
        internal struct AssemblySnapshot
        {
            [SerializeField]
            internal string name;

            [SerializeField]
            internal string moduleVersion;
        }

        [Serializable]
        internal struct FileSnapshot
        {
            [SerializeField]
            internal string assetPath;

            [SerializeField]
            internal string originalHash;

            [SerializeField]
            internal string proposedHash;

            [SerializeField]
            internal string metadataHash;
        }
    }
}
