// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationApiTests
    {
        private const string Source =
            "class MigrationApiSubject { [global::Sirenix.OdinInspector.ReadOnly] public int value; }\r\n";
        private string _assetFolder;
        private string _fullFolder;
        private readonly List<string> _backupRoots = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _assetFolder = "Assets/OdinMigrationApi" + Guid.NewGuid().ToString("N");
            _fullFolder = Path.Combine(Application.dataPath, Path.GetFileName(_assetFolder));
            Directory.CreateDirectory(_fullFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_fullFolder))
            {
                Directory.Delete(_fullFolder, true);
            }
            foreach (string backup in _backupRoots)
            {
                if (Directory.Exists(backup))
                {
                    Directory.Delete(backup, true);
                }
            }
            _backupRoots.Clear();
            if (File.Exists(_fullFolder + ".meta"))
            {
                File.Delete(_fullFolder + ".meta");
            }
        }

        [Test]
        public void SourceAnalysisExposesReadOnlyChangesAndFindings()
        {
            OdinMigrationAnalysis result = OdinMigrationTool.AnalyzeSource(Source);
            Assert.IsTrue(result.IsValid, result.Failure);
            Assert.AreEqual(1, result.ReplacementCount);
            Assert.AreEqual(1, result.Changes[0].Line);
            Assert.AreEqual("global::Sirenix.OdinInspector.ReadOnly", result.Changes[0].From);
            StringAssert.Contains("WReadOnly", result.Changes[0].To);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OdinMigrationChange>)result.Changes).Clear()
            );
            OdinMigrationAnalysis blocked = OdinMigrationTool.AnalyzeSource(
                "class Target : SerializedMonoBehaviour { [OdinSerialize] public int value; }"
            );
            Assert.IsTrue(0 < blocked.Blockers.Count);
            Assert.AreEqual(0, blocked.ReplacementCount);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OdinMigrationFinding>)blocked.Blockers).Clear()
            );
        }

        [Test]
        public void NullSourceReturnsAnInvalidAnalysis()
        {
            OdinMigrationAnalysis result = OdinMigrationTool.AnalyzeSource(null);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(result.Failure));
            Assert.AreEqual(0, result.ReplacementCount);
        }

        [TestCase(null, TestName = "PreviewPath.Null.Refused")]
        [TestCase("", TestName = "PreviewPath.Empty.Refused")]
        [TestCase(" ", TestName = "PreviewPath.Whitespace.Refused")]
        [TestCase("Packages/Target.cs", TestName = "PreviewPath.Package.Refused")]
        [TestCase("/Assets/Target.cs", TestName = "PreviewPath.Rooted.Refused")]
        [TestCase("Assets/../Target.cs", TestName = "PreviewPath.Traversal.Refused")]
        [TestCase("Assets/./Target.cs", TestName = "PreviewPath.Dot.Refused")]
        [TestCase("Assets//Target.cs", TestName = "PreviewPath.EmptySegment.Refused")]
        [TestCase("Assets\\Target.cs", TestName = "PreviewPath.Backslash.Refused")]
        [TestCase("Assets/Target.cs:stream", TestName = "PreviewPath.Stream.Refused")]
        [TestCase("Assets/Target.prefab", TestName = "PreviewPath.NotSource.Refused")]
        public void InvalidSourcePathsCannotCreateAPreview(string path)
        {
            Assert.IsFalse(
                OdinMigrationTool.TryPreview(
                    new[] { path },
                    out OdinMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(failure));
        }

        [Test]
        public void NullPathCollectionIsRefused()
        {
            Assert.IsFalse(
                OdinMigrationTool.TryPreview(
                    null,
                    out OdinMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(failure));
        }

        [Test]
        public void PreviewDeduplicatesScriptsAndDoesNotWrite()
        {
            string path = WriteSource("Target.cs", Source);
            byte[] before = File.ReadAllBytes(FullPath(path));
            Assert.IsTrue(
                OdinMigrationTool.TryPreview(
                    new[] { path, path },
                    out OdinMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(preview.CanApply);
            Assert.AreEqual(1, preview.AnalyzedFiles);
            Assert.AreEqual(1, preview.ChangedFileCount);
            Assert.AreEqual(1, preview.ReplacementCount);
            Assert.IsTrue(
                preview.SourceAnalyses.TryGetValue(path, out OdinMigrationAnalysis analysis)
            );
            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains(path, preview.Report);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(FullPath(path)));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, OdinMigrationAnalysis>)preview.SourceAnalyses).Clear()
            );
        }

        [Test]
        public void ApplyPreservesEncodingAndCreatesExactBackup()
        {
            string path = WriteSource("Target.cs", Source, new UnicodeEncoding(false, true, true));
            byte[] before = File.ReadAllBytes(FullPath(path));
            Assert.IsTrue(
                OdinMigrationTool.TryPreview(
                    new[] { path },
                    out OdinMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(
                OdinMigrationTool.TryApply(preview, out string backupRoot, out failure),
                failure
            );
            _backupRoots.Add(backupRoot);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(backupRoot, path)));
            byte[] after = File.ReadAllBytes(FullPath(path));
            Assert.AreEqual(before[0], after[0]);
            Assert.AreEqual(before[1], after[1]);
            StringAssert.Contains("WReadOnly", File.ReadAllText(FullPath(path)));
            StringAssert.EndsWith("\r\n", File.ReadAllText(FullPath(path)));
            Assert.IsFalse(preview.CanApply);
            Assert.IsFalse(
                OdinMigrationTool.TryApply(preview, out string repeatedBackup, out failure)
            );
            Assert.IsTrue(repeatedBackup == null);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(failure));
        }

        [Test]
        public void ChangedSecondFileRollsBackFirstAndRetainsConcurrentEdit()
        {
            string first = WriteSource("First.cs", Source);
            string second = WriteSource("Second.cs", Source);
            Assert.IsTrue(
                OdinMigrationTool.TryPreview(
                    new[] { first, second },
                    out OdinMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            const string ExternalEdit = "class ExternalEdit { }";
            File.WriteAllText(FullPath(second), ExternalEdit);
            Assert.IsFalse(OdinMigrationTool.TryApply(preview, out string backupRoot, out failure));
            _backupRoots.Add(backupRoot);
            StringAssert.Contains("changed after preview", failure);
            Assert.AreEqual(Source, File.ReadAllText(FullPath(first)));
            Assert.AreEqual(ExternalEdit, File.ReadAllText(FullPath(second)));
            CollectionAssert.AreEqual(
                File.ReadAllBytes(FullPath(first)),
                File.ReadAllBytes(Path.Combine(backupRoot, first))
            );
        }

        [Test]
        public void MissingSourceMakesTheEntirePreviewUnapplicable()
        {
            string path = WriteSource("Target.cs", Source);
            Assert.IsFalse(
                OdinMigrationTool.TryPreview(
                    new[] { path, _assetFolder + "/Missing.cs" },
                    out OdinMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview != null);
            Assert.IsFalse(preview.CanApply);
            Assert.AreEqual(1, preview.Failures.Count);
            Assert.IsFalse(OdinMigrationTool.TryApply(preview, out string backupRoot, out failure));
            Assert.IsTrue(backupRoot == null);
            Assert.AreEqual(Source, File.ReadAllText(FullPath(path)));
        }

        [Test]
        public void CancellationRetainsFindingsAndPreventsApply()
        {
            string first = WriteSource("First.cs", Source);
            string second = WriteSource("Second.cs", Source);
            Assert.IsFalse(
                OdinMigrationTool.TryPreview(
                    new[] { first, second },
                    out OdinMigrationPreview preview,
                    out string failure,
                    (path, index, count) => index == 1
                )
            );
            Assert.IsTrue(preview.Cancelled);
            Assert.IsFalse(preview.CanApply);
            Assert.AreEqual(1, preview.AnalyzedFiles);
            Assert.AreEqual(Source, File.ReadAllText(FullPath(first)));
            Assert.AreEqual(Source, File.ReadAllText(FullPath(second)));
        }

        [Test]
        public void CallbackFailureDoesNotPublishAPartialPreview()
        {
            string path = WriteSource("Target.cs", Source);
            Assert.IsFalse(
                OdinMigrationTool.TryPreview(
                    new[] { path },
                    out OdinMigrationPreview preview,
                    out string failure,
                    (asset, index, count) => throw new IOException("Callback failed")
                )
            );
            Assert.IsTrue(preview == null);
            Assert.AreEqual("Callback failed", failure);
            Assert.AreEqual(Source, File.ReadAllText(FullPath(path)));
        }

        [Test]
        public void SerializedFindingsAndFailuresRemainSourceOnly()
        {
            string path = WriteSource("Target.cs", Source);
            string asset = WriteSource(
                "Target.asset",
                "%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n  serializationData:\n    SerializedBytes: AQ==\n"
            );
            string invalid = WriteSource("Binary.asset", "not Unity YAML");
            byte[] data = File.ReadAllBytes(FullPath(asset));
            Assert.IsTrue(
                OdinMigrationTool.TryPreview(
                    new[] { path },
                    out OdinMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(
                preview.SerializedAnalyses.TryGetValue(
                    asset,
                    out IReadOnlyList<OdinMigrationFinding> findings
                )
            );
            Assert.IsTrue(0 < findings.Count);
            Assert.IsTrue(0 < preview.SerializedScanFailures.Count);
            StringAssert.Contains(invalid, string.Join("; ", preview.SerializedScanFailures));
            Assert.IsTrue(preview.CanApply);
            Assert.IsTrue(
                OdinMigrationTool.TryApply(preview, out string backupRoot, out failure),
                failure
            );
            _backupRoots.Add(backupRoot);
            CollectionAssert.AreEqual(data, File.ReadAllBytes(FullPath(asset)));
            Assert.AreEqual("not Unity YAML", File.ReadAllText(FullPath(invalid)));
        }

        [Test]
        public void EmptyAndUnchangedPreviewsCannotBeApplied()
        {
            Assert.IsTrue(
                OdinMigrationTool.TryPreview(
                    Array.Empty<string>(),
                    out OdinMigrationPreview empty,
                    out string failure
                ),
                failure
            );
            Assert.IsFalse(empty.CanApply);
            string path = WriteSource("Unchanged.cs", "class Unchanged { public int value; }");
            Assert.IsTrue(
                OdinMigrationTool.TryPreview(
                    new[] { path },
                    out OdinMigrationPreview unchanged,
                    out failure
                ),
                failure
            );
            Assert.IsFalse(unchanged.CanApply);
            Assert.IsFalse(
                OdinMigrationTool.TryApply(unchanged, out string backupRoot, out failure)
            );
            Assert.IsTrue(backupRoot == null);
            Assert.IsFalse(OdinMigrationTool.TryApply(null, out backupRoot, out failure));
            Assert.IsTrue(backupRoot == null);
        }

        private static string FullPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }

        private string WriteSource(string name, string source, Encoding encoding = null)
        {
            string path = _assetFolder + "/" + name;
            File.WriteAllText(FullPath(path), source, encoding ?? new UTF8Encoding(false));
            return path;
        }
    }
}
