// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEngine;

    internal static class OdinMigrationBackupReceipt
    {
        internal const string ReceiptName = "recovery.txt";
        internal const int MaximumFileBytes = 16 * 1024 * 1024;
        internal const long MaximumBatchBytes = 256L * 1024 * 1024;
        private const int MaximumReceiptBytes = 4 * 1024 * 1024;
        private const int MaximumEntries = 10000;
        private const string Header = "WallstopOdinMigrationBackup:1";

        private static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);

        internal static bool TryApply(
            IReadOnlyList<OdinMigrationFilePlan> plans,
            string backupRoot,
            out string failure,
            Action receiptCompleted = null
        )
        {
            try
            {
                WallstopStudios.UnityHelpers.Core.Helper.UnityMainThreadGuard.EnsureMainThread();
                string root = OdinMigrationTool.GetValidatedBackupRoot(backupRoot);
                if (Directory.Exists(root) || File.Exists(root))
                {
                    throw new IOException("A migration backup location must be new.");
                }
                int planCount = plans == null ? 0 : plans.Count;
                if (planCount == 0 || MaximumEntries / 2 < planCount)
                {
                    throw new IOException("A bounded, nonempty migration batch is required.");
                }
                List<OdinMigrationFilePlan> captured = new List<OdinMigrationFilePlan>();
                HashSet<string> paths = new HashSet<string>(
                    OdinMigrationCompilationPlan.PathComparer
                );
                long bytes = 0;
                foreach (OdinMigrationFilePlan plan in plans)
                {
                    if (
                        plan == null
                        || plan.AssetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        throw new IOException(
                            "Migration targets must be assets with separate metadata."
                        );
                    }
                    ValidateTarget(plan.AssetPath);
                    string fullPath = OdinMigrationTool.GetValidatedAssetPath(plan.AssetPath);
                    if (
                        !OdinMigrationCompilationPlan.PathComparer.Equals(
                            Path.GetFullPath(fullPath),
                            Path.GetFullPath(plan.FullPath)
                        )
                    )
                    {
                        throw new IOException(
                            "The migration target path differs from its project path."
                        );
                    }
                    AddCaptured(plan, captured, paths, ref bytes);
                    string metaPath = plan.AssetPath + ".meta";
                    string metaFullPath = OdinMigrationTool.GetValidatedAssetPath(metaPath);
                    byte[] metadata = ReadBounded(metaFullPath, MaximumFileBytes);
                    AddCaptured(
                        new OdinMigrationFilePlan(metaPath, metaFullPath, metadata, metadata, null),
                        captured,
                        paths,
                        ref bytes
                    );
                }
                byte[] receipt = CreateReceipt(captured);
                PhysicalOdinMigrationFileStore store = new PhysicalOdinMigrationFileStore();
                return OdinMigrationFileTransaction.TryApply(
                    captured,
                    root,
                    store,
                    out failure,
                    () =>
                    {
                        store.WriteBackup(Path.Combine(root, ReceiptName), receipt);
                        receiptCompleted?.Invoke();
                    }
                );
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        internal static bool TryRestore(string backupRoot, out string failure)
        {
            try
            {
                WallstopStudios.UnityHelpers.Core.Helper.UnityMainThreadGuard.EnsureMainThread();
                string root = OdinMigrationTool.GetValidatedBackupRoot(backupRoot);
                string receiptPath = Path.Combine(root, ReceiptName);
                OdinMigrationTool.EnsureNoReparsePoints(ProjectRoot(), receiptPath);
                string[] lines = Encoding
                    .GetString(ReadBounded(receiptPath, MaximumReceiptBytes))
                    .Split('\n');
                int lineCount = lines.Length;
                if (
                    lineCount < 6
                    || MaximumEntries + 4 < lineCount
                    || !string.Equals(lines[0], Header, StringComparison.Ordinal)
                    || !string.Equals(
                        lines[1],
                        Hash(Encoding.GetBytes(ProjectRoot())),
                        StringComparison.Ordinal
                    )
                    || lines[lineCount - 1].Length != 0
                )
                {
                    throw new IOException(
                        "The migration recovery record is invalid or belongs to another project."
                    );
                }
                string content = string.Join("\n", lines, 0, lineCount - 2) + "\n";
                if (
                    !string.Equals(
                        lines[lineCount - 2],
                        "sha256\t" + Hash(Encoding.GetBytes(content)),
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new IOException(
                        "The migration recovery record changed or is incomplete."
                    );
                }
                List<OdinMigrationFilePlan> restore = new List<OdinMigrationFilePlan>();
                HashSet<string> paths = new HashSet<string>(
                    OdinMigrationCompilationPlan.PathComparer
                );
                Dictionary<string, string> metadata = new Dictionary<string, string>(
                    OdinMigrationCompilationPlan.PathComparer
                );
                long bytes = 0;
                for (int index = 2; index < lineCount - 2; ++index)
                {
                    string[] fields = lines[index].Split('\t');
                    if (fields.Length != 3 || !IsHash(fields[1]) || !IsHash(fields[2]))
                    {
                        throw new IOException("The migration recovery entry is invalid.");
                    }
                    string assetPath = fields[0];
                    ValidateTarget(assetPath);
                    string fullPath = OdinMigrationTool.GetValidatedAssetPath(assetPath);
                    if (!paths.Add(fullPath))
                    {
                        throw new IOException(
                            "The migration recovery record contains duplicate paths."
                        );
                    }
                    string backupPath = Path.Combine(root, assetPath);
                    OdinMigrationTool.EnsureNoReparsePoints(ProjectRoot(), backupPath);
                    byte[] original = ReadBounded(backupPath, MaximumFileBytes);
                    byte[] current = ReadBounded(fullPath, MaximumFileBytes);
                    if (OdinMigrationFileTransaction.BytesEqual(original, current))
                    {
                        current = original;
                    }
                    bytes +=
                        original.Length
                        + (ReferenceEquals(original, current) ? 0L : current.Length);
                    if (MaximumBatchBytes < bytes)
                    {
                        throw new IOException(
                            "The migration recovery batch exceeds its size limit."
                        );
                    }
                    if (!string.Equals(Hash(original), fields[1], StringComparison.Ordinal))
                    {
                        throw new IOException("A migration backup changed: " + assetPath);
                    }
                    string currentHash = Hash(current);
                    if (
                        !string.Equals(currentHash, fields[1], StringComparison.Ordinal)
                        && !string.Equals(currentHash, fields[2], StringComparison.Ordinal)
                    )
                    {
                        throw new IOException(
                            "Recovery refused because a target changed after migration: "
                                + assetPath
                        );
                    }
                    if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(fields[1], fields[2], StringComparison.Ordinal))
                        {
                            throw new IOException(
                                "Migration recovery must preserve asset metadata."
                            );
                        }
                        metadata.Add(assetPath, currentHash);
                    }
                    restore.Add(
                        new OdinMigrationFilePlan(assetPath, fullPath, current, original, null)
                    );
                }
                int restoreCount = restore.Count;
                if (restoreCount == 0 || restoreCount != metadata.Count * 2)
                {
                    throw new IOException(
                        "The migration recovery record lacks complete asset and metadata pairs."
                    );
                }
                foreach (OdinMigrationFilePlan plan in restore)
                {
                    if (
                        !plan.AssetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                        && !metadata.ContainsKey(plan.AssetPath + ".meta")
                    )
                    {
                        throw new IOException(
                            "The migration recovery record lacks asset metadata."
                        );
                    }
                }
                restore.Reverse();
                PhysicalOdinMigrationFileStore store = new PhysicalOdinMigrationFileStore();
                List<string> failures = new List<string>();
                foreach (OdinMigrationFilePlan plan in restore)
                {
                    try
                    {
                        OdinMigrationTool.GetValidatedAssetPath(plan.AssetPath);
                        if (!plan.AssetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            string metaPath = plan.AssetPath + ".meta";
                            if (
                                !metadata.TryGetValue(metaPath, out string expectedMetadata)
                                || !string.Equals(
                                    Hash(
                                        ReadBounded(
                                            OdinMigrationTool.GetValidatedAssetPath(metaPath),
                                            MaximumFileBytes
                                        )
                                    ),
                                    expectedMetadata,
                                    StringComparison.Ordinal
                                )
                            )
                            {
                                throw new IOException(
                                    "Recovery refused because asset metadata changed during recovery."
                                );
                            }
                        }
                        byte[] current = ReadBounded(plan.FullPath, MaximumFileBytes);
                        if (!OdinMigrationFileTransaction.BytesEqual(current, plan.OriginalBytes))
                        {
                            throw new IOException(
                                "Recovery refused because a target changed during recovery."
                            );
                        }
                        if (!OdinMigrationFileTransaction.BytesEqual(current, plan.UpgradedBytes))
                        {
                            store.RestoreTarget(plan.FullPath, plan.UpgradedBytes);
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Add(plan.AssetPath + ": " + exception.Message);
                    }
                }
                if (0 < failures.Count)
                {
                    throw new IOException("Recovery is incomplete: " + string.Join("; ", failures));
                }
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return BitConverter
                    .ToString(hash.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        internal static byte[] ReadBounded(string path, int limit)
        {
            using (
                FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                )
            )
            {
                if (limit < stream.Length)
                {
                    throw new IOException("A migration recovery file exceeds its size limit.");
                }
                byte[] bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0)
                    {
                        throw new IOException("A migration recovery file changed while reading.");
                    }
                    offset += read;
                }
                if (stream.ReadByte() != -1)
                {
                    throw new IOException("A migration recovery file changed while reading.");
                }
                return bytes;
            }
        }

        private static void AddCaptured(
            OdinMigrationFilePlan plan,
            List<OdinMigrationFilePlan> captured,
            HashSet<string> paths,
            ref long bytes
        )
        {
            if (MaximumEntries <= captured.Count)
            {
                throw new IOException("The migration batch exceeds its entry limit.");
            }
            if (
                plan.OriginalBytes == null
                || plan.UpgradedBytes == null
                || MaximumFileBytes < plan.OriginalBytes.Length
                || MaximumFileBytes < plan.UpgradedBytes.Length
            )
            {
                throw new IOException("A migration file exceeds its size limit or has no bytes.");
            }
            string fullPath = Path.GetFullPath(plan.FullPath);
            if (!paths.Add(fullPath))
            {
                throw new IOException("The migration batch contains duplicate paths.");
            }
            bytes += plan.OriginalBytes.Length + (long)plan.UpgradedBytes.Length;
            if (MaximumBatchBytes < bytes)
            {
                throw new IOException("The migration batch exceeds its size limit.");
            }
            captured.Add(
                new OdinMigrationFilePlan(
                    plan.AssetPath,
                    fullPath,
                    (byte[])plan.OriginalBytes.Clone(),
                    (byte[])plan.UpgradedBytes.Clone(),
                    plan.Analysis
                )
            );
        }

        private static byte[] CreateReceipt(IReadOnlyList<OdinMigrationFilePlan> plans)
        {
            StringBuilder text = new StringBuilder();
            text.Append(Header)
                .Append('\n')
                .Append(Hash(Encoding.GetBytes(ProjectRoot())))
                .Append('\n');
            foreach (OdinMigrationFilePlan plan in plans)
            {
                text.Append(plan.AssetPath)
                    .Append('\t')
                    .Append(Hash(plan.OriginalBytes))
                    .Append('\t')
                    .Append(Hash(plan.UpgradedBytes))
                    .Append('\n');
            }
            string content = text.ToString();
            text.Append("sha256\t").Append(Hash(Encoding.GetBytes(content))).Append('\n');
            byte[] receipt = Encoding.GetBytes(text.ToString());
            if (MaximumReceiptBytes < receipt.Length)
            {
                throw new IOException("The migration recovery record exceeds its size limit.");
            }
            return receipt;
        }

        private static bool IsHash(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }
            foreach (char character in value)
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

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static void ValidateTarget(string assetPath)
        {
            OdinMigrationTool.GetValidatedAssetPath(assetPath);
            foreach (char character in assetPath)
            {
                if (char.IsControl(character))
                {
                    throw new IOException("Migration paths cannot contain control characters.");
                }
            }
            string target = assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? assetPath.Substring(0, assetPath.Length - 5)
                : assetPath;
            if (
                !target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !target.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                && !target.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                && !target.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new IOException(
                    "Migration recovery supports only source and Unity serialized assets."
                );
            }
        }
    }
}
