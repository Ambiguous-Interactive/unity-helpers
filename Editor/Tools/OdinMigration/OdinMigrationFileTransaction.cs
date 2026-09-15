// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class OdinMigrationEncodedSource
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true, true);
        private static readonly UnicodeEncoding Utf16LittleEndian = new UnicodeEncoding(
            false,
            true,
            true
        );
        private static readonly UnicodeEncoding Utf16BigEndian = new UnicodeEncoding(
            true,
            true,
            true
        );
        private static readonly UTF32Encoding Utf32LittleEndian = new UTF32Encoding(
            false,
            true,
            true
        );
        private static readonly UTF32Encoding Utf32BigEndian = new UTF32Encoding(true, true, true);

        internal static bool TryDecode(
            byte[] bytes,
            out OdinMigrationDecodedSource decoded,
            out string failure
        )
        {
            if (bytes == null)
            {
                decoded = default;
                failure = "Source bytes are null.";
                return false;
            }

            Encoding encoding = Utf8;
            int preambleLength = 0;
            if (StartsWith(bytes, Utf32LittleEndian.GetPreamble()))
            {
                encoding = Utf32LittleEndian;
                preambleLength = 4;
            }
            else if (StartsWith(bytes, Utf32BigEndian.GetPreamble()))
            {
                encoding = Utf32BigEndian;
                preambleLength = 4;
            }
            else if (StartsWith(bytes, Utf8Bom.GetPreamble()))
            {
                encoding = Utf8Bom;
                preambleLength = 3;
            }
            else if (StartsWith(bytes, Utf16LittleEndian.GetPreamble()))
            {
                encoding = Utf16LittleEndian;
                preambleLength = 2;
            }
            else if (StartsWith(bytes, Utf16BigEndian.GetPreamble()))
            {
                encoding = Utf16BigEndian;
                preambleLength = 2;
            }

            try
            {
                string source = encoding.GetString(
                    bytes,
                    preambleLength,
                    bytes.Length - preambleLength
                );
                decoded = new OdinMigrationDecodedSource(source, encoding, preambleLength);
                failure = null;
                return true;
            }
            catch (DecoderFallbackException exception)
            {
                decoded = default;
                failure = $"Unsupported or invalid source encoding: {exception.Message}";
                return false;
            }
        }

        internal static byte[] Encode(OdinMigrationDecodedSource decoded, string source)
        {
            byte[] content = decoded.Encoding.GetBytes(source);
            if (decoded.PreambleLength == 0)
            {
                return content;
            }

            byte[] preamble = decoded.Encoding.GetPreamble();
            byte[] result = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
            return result;
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (prefix.Length == 0 || bytes.Length < prefix.Length)
            {
                return false;
            }
            for (int index = 0; index < prefix.Length; index++)
            {
                if (bytes[index] != prefix[index])
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal static class OdinMigrationFileTransaction
    {
        internal static bool TryApply(
            IReadOnlyList<OdinMigrationFilePlan> plans,
            string backupRoot,
            IOdinMigrationFileStore store,
            out string failure
        )
        {
            List<OdinMigrationFilePlan> writesStarted = new List<OdinMigrationFilePlan>();
            try
            {
                foreach (OdinMigrationFilePlan plan in plans)
                {
                    byte[] current = store.ReadAllBytes(plan.FullPath);
                    if (!BytesEqual(current, plan.OriginalBytes))
                    {
                        throw new IOException(
                            $"'{plan.AssetPath}' changed after preview. No further files were written."
                        );
                    }

                    string backupPath = Path.Combine(
                        backupRoot,
                        plan.AssetPath.Replace('/', Path.DirectorySeparatorChar)
                    );
                    store.WriteBackup(backupPath, plan.OriginalBytes);
                    writesStarted.Add(plan);
                    store.WriteTarget(plan.FullPath, plan.UpgradedBytes);
                }
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                List<string> rollbackFailures = new List<string>();
                for (int index = writesStarted.Count - 1; 0 <= index; index--)
                {
                    OdinMigrationFilePlan plan = writesStarted[index];
                    try
                    {
                        byte[] current = store.ReadAllBytes(plan.FullPath);
                        if (BytesEqual(current, plan.OriginalBytes))
                        {
                            continue;
                        }
                        if (!BytesEqual(current, plan.UpgradedBytes))
                        {
                            throw new IOException(
                                $"Rollback refused because '{plan.AssetPath}' changed after it was written."
                            );
                        }
                        store.RestoreTarget(plan.FullPath, plan.OriginalBytes);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackFailures.Add($"{plan.AssetPath}: {rollbackException.Message}");
                    }
                }

                string failureMessage = exception.Message;
                if (0 < rollbackFailures.Count)
                {
                    failureMessage += $" Rollback failures: {string.Join("; ", rollbackFailures)}";
                }
                failure = failureMessage;
                return false;
            }
        }

        internal static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal sealed class PhysicalOdinMigrationFileStore : IOdinMigrationFileStore
    {
        private static void WriteDurably(string path, byte[] bytes)
        {
            if (!DurableFile.TryWriteAllBytes(path, bytes, out Exception error))
            {
                throw new IOException($"Could not write '{path}'.", error);
            }
        }

        public byte[] ReadAllBytes(string path)
        {
            return File.ReadAllBytes(path);
        }

        public void RestoreTarget(string path, byte[] bytes)
        {
            WriteDurably(path, bytes);
        }

        public void WriteBackup(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            WriteDurably(path, bytes);
        }

        public void WriteTarget(string path, byte[] bytes)
        {
            WriteDurably(path, bytes);
        }
    }

    internal interface IOdinMigrationFileStore
    {
        byte[] ReadAllBytes(string path);

        void RestoreTarget(string path, byte[] bytes);

        void WriteBackup(string path, byte[] bytes);

        void WriteTarget(string path, byte[] bytes);
    }

    internal readonly struct OdinMigrationDecodedSource
    {
        internal readonly string Source;
        internal readonly Encoding Encoding;
        internal readonly int PreambleLength;

        internal OdinMigrationDecodedSource(string source, Encoding encoding, int preambleLength)
        {
            Source = source;
            Encoding = encoding;
            PreambleLength = preambleLength;
        }
    }

    internal sealed class OdinMigrationFilePlan
    {
        internal string AssetPath { get; }

        internal string FullPath { get; }

        internal byte[] OriginalBytes { get; }

        internal byte[] UpgradedBytes { get; }

        internal OdinMigrationAnalysis Analysis { get; }

        internal OdinMigrationFilePlan(
            string assetPath,
            string fullPath,
            byte[] originalBytes,
            byte[] upgradedBytes,
            OdinMigrationAnalysis analysis
        )
        {
            AssetPath = assetPath;
            FullPath = fullPath;
            OriginalBytes = originalBytes;
            UpgradedBytes = upgradedBytes;
            Analysis = analysis;
        }
    }
}
