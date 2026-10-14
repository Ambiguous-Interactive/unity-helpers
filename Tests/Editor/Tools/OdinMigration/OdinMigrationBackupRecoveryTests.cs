// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("MigrationIsolation")]
    [Category("Fast")]
    public sealed class OdinMigrationBackupRecoveryTests
    {
        private string _assetFolder;
        private string _fullFolder;
        private string _backup;
        private OdinMigrationFilePlan[] _plans;

        private static void RewriteChecksum(string record)
        {
            string[] lines = File.ReadAllText(record).Split('\n');
            string content = string.Join("\n", lines, 0, lines.Length - 2) + "\n";
            using (SHA256 hash = SHA256.Create())
            {
                lines[lines.Length - 2] =
                    "sha256\t"
                    + BitConverter
                        .ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content)))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
            }
            File.WriteAllText(record, string.Join("\n", lines), new UTF8Encoding(false));
        }

        [SetUp]
        public void CreateSourceAndSavedData()
        {
            string name = Guid.NewGuid().ToString("N");
            _assetFolder = "Assets/.OdinMigrationRecovery-" + name;
            _fullFolder = OdinMigrationTool.GetValidatedAssetPath(_assetFolder);
            Directory.CreateDirectory(_fullFolder);
            _backup = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Library",
                "WallstopOdinMigrationBackups",
                "recovery-" + name
            );
            _plans = new[] { CreatePlan("source.cs"), CreatePlan("data.asset") };
        }

        [UnitySetUp]
        public IEnumerator WaitForEditorReady()
        {
            yield return null;
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 60;
            while (
                (
                    UnityEditor.EditorApplication.isCompiling
                    || UnityEditor.EditorApplication.isUpdating
                )
                && UnityEditor.EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }
            Assert.IsFalse(UnityEditor.EditorApplication.isCompiling);
            Assert.IsFalse(UnityEditor.EditorApplication.isUpdating);
        }

        [TearDown]
        public void DeleteRecoveryFiles()
        {
            if (Directory.Exists(_fullFolder))
            {
                Directory.Delete(_fullFolder, true);
            }
            if (Directory.Exists(_backup))
            {
                Directory.Delete(_backup, true);
            }
        }

        [Test]
        public void RecoveryReadsDurableRecordAndPreservesMetadataAndEncoding()
        {
            byte[] metadata = File.ReadAllBytes(_plans[0].FullPath + ".meta");
            Assert.IsTrue(
                OdinMigrationBackupReceipt.TryApply(_plans, _backup, out string failure),
                failure
            );
            foreach (OdinMigrationFilePlan plan in _plans)
            {
                CollectionAssert.AreEqual(plan.UpgradedBytes, File.ReadAllBytes(plan.FullPath));
                CollectionAssert.AreEqual(
                    plan.OriginalBytes,
                    File.ReadAllBytes(Path.Combine(_backup, plan.AssetPath))
                );
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(plan.FullPath + ".meta"),
                    File.ReadAllBytes(Path.Combine(_backup, plan.AssetPath + ".meta"))
                );
            }
            Assert.IsTrue(OdinMigrationTool.TryRestoreBackup(_backup, out failure), failure);
            foreach (OdinMigrationFilePlan plan in _plans)
            {
                CollectionAssert.AreEqual(plan.OriginalBytes, File.ReadAllBytes(plan.FullPath));
            }
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(_plans[0].FullPath + ".meta"));
            Assert.IsTrue(OdinMigrationTool.TryRestoreBackup(_backup, out failure), failure);
            Assert.IsTrue(
                File.Exists(Path.Combine(_backup, OdinMigrationBackupReceipt.ReceiptName))
            );
        }

        [TestCase(0, TestName = "Recovery.ChangedTarget.RefusesWholeBatch")]
        [TestCase(1, TestName = "Recovery.ChangedMetadata.RefusesWholeBatch")]
        [TestCase(2, TestName = "Recovery.ChangedBackup.RefusesWholeBatch")]
        [TestCase(3, TestName = "Recovery.MissingBackup.RefusesWholeBatch")]
        [TestCase(4, TestName = "Recovery.TruncatedRecord.RefusesWholeBatch")]
        [TestCase(5, TestName = "Recovery.TraversalRecord.RefusesWholeBatch")]
        [TestCase(6, TestName = "Recovery.DuplicateRecord.RefusesWholeBatch")]
        [TestCase(7, TestName = "Recovery.MissingMetadataRecord.RefusesWholeBatch")]
        [TestCase(8, TestName = "Recovery.ForeignProjectRecord.RefusesWholeBatch")]
        [TestCase(9, TestName = "Recovery.InvalidVersion.RefusesWholeBatch")]
        [TestCase(10, TestName = "Recovery.InvalidHash.RefusesWholeBatch")]
        [TestCase(11, TestName = "Recovery.MissingRecord.RefusesWholeBatch")]
        [TestCase(12, TestName = "Recovery.EmptyAssetSelection.RefusesWholeBatch")]
        [TestCase(13, TestName = "Recovery.OversizedRecord.RefusesWholeBatch")]
        public void InvalidRecoveryPreservesEveryCurrentFile(int damage)
        {
            Assert.IsTrue(
                OdinMigrationBackupReceipt.TryApply(_plans, _backup, out string failure),
                failure
            );
            string record = Path.Combine(_backup, OdinMigrationBackupReceipt.ReceiptName);
            string[] lines = File.ReadAllText(record).Split('\n');
            switch (damage)
            {
                case 0:
                    File.WriteAllText(_plans[1].FullPath, "concurrent edit");
                    break;
                case 1:
                    File.WriteAllText(_plans[1].FullPath + ".meta", "changed identity");
                    break;
                case 2:
                    File.WriteAllText(Path.Combine(_backup, _plans[1].AssetPath), "changed backup");
                    break;
                case 3:
                    File.Delete(Path.Combine(_backup, _plans[1].AssetPath));
                    break;
                case 4:
                    File.WriteAllText(record, File.ReadAllText(record).TrimEnd('\n'));
                    break;
                case 5:
                    lines[2] = lines[2].Replace(_plans[0].AssetPath, "Assets/../escaped.cs");
                    File.WriteAllText(record, string.Join("\n", lines));
                    break;
                case 6:
                    lines[4] = lines[2];
                    File.WriteAllText(record, string.Join("\n", lines));
                    break;
                case 7:
                    List<string> missing = new List<string>(lines);
                    missing.RemoveAt(3);
                    File.WriteAllText(record, string.Join("\n", missing));
                    break;
                case 8:
                    lines[1] = new string('0', 64);
                    File.WriteAllText(record, string.Join("\n", lines));
                    break;
                case 9:
                    lines[0] = "WallstopOdinMigrationBackup:999";
                    File.WriteAllText(record, string.Join("\n", lines));
                    break;
                case 10:
                    lines[2] = lines[2].Replace(lines[2].Split('\t')[1], new string('g', 64));
                    File.WriteAllText(record, string.Join("\n", lines));
                    break;
                case 11:
                    File.Delete(record);
                    break;
                case 12:
                    File.WriteAllText(
                        record,
                        string.Join(
                            "\n",
                            new[] { lines[0], lines[1], lines[3], lines[5], lines[6], "" }
                        )
                    );
                    break;
                case 13:
                    using (
                        FileStream stream = new FileStream(record, FileMode.Open, FileAccess.Write)
                    )
                    {
                        stream.SetLength(4 * 1024 * 1024 + 1);
                    }
                    break;
            }
            if (5 <= damage && damage <= 10 || damage == 12)
            {
                RewriteChecksum(record);
            }
            List<byte[]> current = new List<byte[]>();
            foreach (OdinMigrationFilePlan plan in _plans)
            {
                current.Add(File.ReadAllBytes(plan.FullPath));
                current.Add(File.ReadAllBytes(plan.FullPath + ".meta"));
            }
            Assert.IsFalse(OdinMigrationTool.TryRestoreBackup(_backup, out failure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            int index = 0;
            foreach (OdinMigrationFilePlan plan in _plans)
            {
                CollectionAssert.AreEqual(current[index], File.ReadAllBytes(plan.FullPath));
                ++index;
                CollectionAssert.AreEqual(
                    current[index],
                    File.ReadAllBytes(plan.FullPath + ".meta")
                );
                ++index;
            }
        }

        [TestCase(0, TestName = "Backup.MissingMetadata.WritesNoTargets")]
        [TestCase(1, TestName = "Backup.DuplicateTarget.WritesNoTargets")]
        [TestCase(2, TestName = "Backup.StaleTarget.WritesNoTargets")]
        [TestCase(3, TestName = "Backup.ExistingDestination.WritesNoTargets")]
        [TestCase(4, TestName = "Backup.InvalidFullPath.WritesNoTargets")]
        [TestCase(5, TestName = "Backup.OversizedTarget.WritesNoTargets")]
        [TestCase(6, TestName = "Backup.CanonicalPathAlias.WritesNoTargets")]
        public void InvalidBatchDoesNotStartWritingTargets(int invalid)
        {
            IReadOnlyList<OdinMigrationFilePlan> plans = _plans;
            switch (invalid)
            {
                case 0:
                    File.Delete(_plans[1].FullPath + ".meta");
                    break;
                case 1:
                    plans = new[] { _plans[0], _plans[0] };
                    break;
                case 2:
                    File.WriteAllText(_plans[1].FullPath, "concurrent edit");
                    break;
                case 3:
                    Directory.CreateDirectory(_backup);
                    break;
                case 4:
                    plans = new[]
                    {
                        new OdinMigrationFilePlan(
                            _plans[0].AssetPath,
                            _plans[1].FullPath,
                            _plans[0].OriginalBytes,
                            _plans[0].UpgradedBytes,
                            null
                        ),
                    };
                    break;
                case 5:
                    plans = new[]
                    {
                        new OdinMigrationFilePlan(
                            _plans[0].AssetPath,
                            _plans[0].FullPath,
                            _plans[0].OriginalBytes,
                            new byte[OdinMigrationBackupReceipt.MaximumFileBytes + 1],
                            null
                        ),
                    };
                    break;
                case 6:
                    plans = new[]
                    {
                        _plans[0],
                        new OdinMigrationFilePlan(
                            _plans[0].AssetPath,
                            Path.GetFullPath(_plans[0].FullPath),
                            _plans[0].OriginalBytes,
                            _plans[0].UpgradedBytes,
                            null
                        ),
                    };
                    break;
            }
            byte[] first = File.ReadAllBytes(_plans[0].FullPath);
            byte[] second = File.ReadAllBytes(_plans[1].FullPath);
            Assert.IsFalse(OdinMigrationBackupReceipt.TryApply(plans, _backup, out string failure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            CollectionAssert.AreEqual(first, File.ReadAllBytes(_plans[0].FullPath));
            CollectionAssert.AreEqual(second, File.ReadAllBytes(_plans[1].FullPath));
            Assert.IsFalse(
                File.Exists(Path.Combine(_backup, OdinMigrationBackupReceipt.ReceiptName))
            );
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("Library/WallstopOdinMigrationBackups/relative")]
        [TestCase("Assets/../escaped")]
        public void InvalidBackupPathsFailWithoutWrites(string backup)
        {
            Assert.IsFalse(OdinMigrationTool.TryRestoreBackup(backup, out string failure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            Assert.IsFalse(OdinMigrationBackupReceipt.TryApply(_plans, backup, out failure));
            CollectionAssert.AreEqual(
                _plans[0].OriginalBytes,
                File.ReadAllBytes(_plans[0].FullPath)
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AbsolutePathsOutsideMigrationBackupChildrenAreRefused(int location)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string path =
                location == 0 ? Path.Combine(root, "Assets", "backup")
                : location == 1 ? Path.GetDirectoryName(_backup)
                : Path.Combine(_backup, "nested");
            Assert.IsFalse(OdinMigrationTool.TryRestoreBackup(path, out string failure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            Assert.IsFalse(OdinMigrationBackupReceipt.TryApply(_plans, path, out failure));
            CollectionAssert.AreEqual(
                _plans[0].OriginalBytes,
                File.ReadAllBytes(_plans[0].FullPath)
            );
        }

        private OdinMigrationFilePlan CreatePlan(string name)
        {
            string assetPath = _assetFolder + "/" + name;
            string fullPath = OdinMigrationTool.GetValidatedAssetPath(assetPath);
            byte[] original = { 0xFF, 0xFE, 65, 0, 13, 0, 10, 0 };
            byte[] proposed = { 0xFF, 0xFE, 66, 0, 13, 0, 10, 0 };
            File.WriteAllBytes(fullPath, original);
            File.WriteAllText(
                fullPath + ".meta",
                "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\n"
            );
            return new OdinMigrationFilePlan(assetPath, fullPath, original, proposed, null);
        }
    }
}
