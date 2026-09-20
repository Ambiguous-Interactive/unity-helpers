// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationCopierWindowTests : CommonTestBase
    {
        private const string SrcRoot = "Assets/Temp/AnimationCopierTests/Src";
        private const string DstRoot = "Assets/Temp/AnimationCopierTests/Dst";
        private bool _prevPrompt;
        private bool _previousEditorUiSuppress;

        private static void ImportAssetIfExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (
                AssetDatabase.IsValidFolder(assetPath)
                || AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null
            )
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static string ToFull(string rel) =>
            Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    rel
                )
                .SanitizePath();

        private static void ModifyClip(string relPath)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(relPath);
            Assert.IsTrue(clip != null);
            clip.frameRate = clip.frameRate + 1f;
            EditorUtility.SetDirty(clip);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;
            EnsureFolder(SrcRoot);
            EnsureFolder(DstRoot);
            _prevPrompt = AnimationCopierWindow.SuppressUserPrompts;
            AnimationCopierWindow.SuppressUserPrompts = true;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            CleanupTrackedFoldersAndAssets();
            AnimationCopierWindow.SuppressUserPrompts = _prevPrompt;
            EditorUi.Suppress = _previousEditorUiSuppress;
        }

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            DeferAssetCleanupToOneTimeTearDown = true;
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            CleanupDeferredAssetsAndFolders();
            base.OneTimeTearDown();
        }

        [Test]
        public void AnalyzeDetectsNewChangedUnchangedAndOrphans()
        {
            string srcA = Path.Combine(SrcRoot, "A.anim").SanitizePath();
            CreateEmptyClip(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = SrcRoot;
            window.AnimationDestinationPathRelative = DstRoot;
            window.AnalyzeAnimations();

            int newCount = window.NewCount;
            int changedCount = window.ChangedCount;
            int unchangedCount = window.UnchangedCount;
            int orphansCount = window.OrphansCount;
            Assert.AreEqual(1, newCount);
            Assert.AreEqual(0, changedCount);
            Assert.AreEqual(0, unchangedCount);
            Assert.AreEqual(0, orphansCount);

            string dstA = Path.Combine(DstRoot, "A.anim").SanitizePath();
            Assert.IsTrue(AssetDatabase.CopyAsset(srcA, dstA));
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(dstA);

            ModifyClip(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);

            string dstB = Path.Combine(DstRoot, "B.anim").SanitizePath();
            CreateEmptyClip(dstB);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(dstB);

            window.AnalyzeAnimations();

            newCount = window.NewCount;
            changedCount = window.ChangedCount;
            unchangedCount = window.UnchangedCount;
            orphansCount = window.OrphansCount;

            Assert.AreEqual(0, newCount);
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(0, unchangedCount);
            Assert.AreEqual(1, orphansCount);
        }

        [Test]
        public void CopyChangedPreservesGuidAndOverwrites()
        {
            string srcA = Path.Combine(SrcRoot, "A.anim").SanitizePath();
            string dstA = Path.Combine(DstRoot, "A.anim").SanitizePath();
            CreateEmptyClip(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);
            Assert.IsTrue(AssetDatabase.CopyAsset(srcA, dstA));
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(dstA);

            string guidBefore = AssetDatabase.AssetPathToGUID(dstA);

            ModifyClip(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = SrcRoot;
            window.AnimationDestinationPathRelative = DstRoot;
            window.DryRun = false;

            window.AnalyzeAnimations();
            window.CopyChanged();

            string guidAfter = AssetDatabase.AssetPathToGUID(dstA);
            Assert.AreEqual(
                guidBefore,
                guidAfter,
                "Destination GUID should be preserved after overwrite."
            );
        }

        [Test]
        public void CopyAllIncludingUnchangedReplacesSelectedClipAndPreservesGuid()
        {
            string sourceFolder = Path.Combine(SrcRoot, "ForceReplace").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "ForceReplace").SanitizePath();
            EnsureFolder(sourceFolder);
            EnsureFolder(destinationFolder);
            string source = Path.Combine(sourceFolder, "ForceReplace.anim").SanitizePath();
            string destination = Path.Combine(destinationFolder, "ForceReplace.anim")
                .SanitizePath();
            CreateEmptyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);
            Assert.IsTrue(AssetDatabase.CopyAsset(source, destination));
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(destination);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = sourceFolder;
            window.AnimationDestinationPathRelative = destinationFolder;
            window.IncludeUnchangedInCopyAll = true;
            window.AnalyzeAnimations();
            Assert.AreEqual(1, window.UnchangedCount);

            string destinationGuid = AssetDatabase.AssetPathToGUID(destination);
            AnimationClip originalDestination = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                destination
            );
            float originalFrameRate = originalDestination.frameRate;
            ModifyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);

            window.CopyAll();
            ImportAssetIfExists(destination);

            AnimationClip updatedDestination = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                destination
            );
            Assert.AreEqual(originalFrameRate + 1f, updatedDestination.frameRate);
            Assert.AreEqual(destinationGuid, AssetDatabase.AssetPathToGUID(destination));
        }

        [Test]
        public void MirrorDeleteRemovesOrphansWhenNotDryRun()
        {
            string srcA = Path.Combine(SrcRoot, "A.anim").SanitizePath();
            string dstB = Path.Combine(DstRoot, "B.anim").SanitizePath();
            CreateEmptyClip(srcA);
            CreateEmptyClip(dstB);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);
            ImportAssetIfExists(dstB);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = SrcRoot;
            window.AnimationDestinationPathRelative = DstRoot;
            window.DryRun = false;

            window.AnalyzeAnimations();

            Assert.Greater(window.OrphansCount, 0);

            window.MirrorDeleteDestinationAnimations();
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(DstRoot);

            Assert.IsFalse(
                File.Exists(ToFull(dstB)) || AssetDatabase.LoadMainAssetAtPath(dstB) != null,
                "Orphan should be deleted"
            );
        }

        [Test]
        public void CopiedAnimationsAreDetectedAsUnchangedOnReanalysis()
        {
            // A unique subdirectory, so a deferred cleanup elsewhere cannot collide with this test.
            string testSrc = Path.Combine(SrcRoot, "ReanalysisTest").SanitizePath();
            string testDst = Path.Combine(DstRoot, "ReanalysisTest").SanitizePath();
            EnsureFolder(testSrc);
            EnsureFolder(testDst);

            string srcA = Path.Combine(testSrc, "A.anim").SanitizePath();
            CreateEmptyClip(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = testSrc;
            window.AnimationDestinationPathRelative = testDst;
            window.DryRun = false;

            window.AnalyzeAnimations();
            Assert.AreEqual(1, window.NewCount, "Should detect one new animation before copy");
            Assert.AreEqual(0, window.ChangedCount);
            Assert.AreEqual(0, window.UnchangedCount);

            window.CopyNew();
            AssetDatabase.SaveAssets();
            string dstA = Path.Combine(testDst, "A.anim").SanitizePath();
            ImportAssetIfExists(dstA);

            window.AnalyzeAnimations();
            Assert.AreEqual(0, window.NewCount, "Should not detect any new animations after copy");
            Assert.AreEqual(
                0,
                window.ChangedCount,
                "Copied animation should NOT be detected as changed"
            );
            Assert.AreEqual(
                1,
                window.UnchangedCount,
                "Copied animation should be detected as unchanged"
            );
        }

        [Test]
        public void CopiedAnimationsWithSpriteCurvesAreDetectedAsUnchanged()
        {
            // A unique subdirectory, so a deferred cleanup elsewhere cannot collide with this test.
            string testSrc = Path.Combine(SrcRoot, "SpriteCurveTest").SanitizePath();
            string testDst = Path.Combine(DstRoot, "SpriteCurveTest").SanitizePath();
            EnsureFolder(testSrc);
            EnsureFolder(testDst);

            string srcA = Path.Combine(testSrc, "SpriteAnim.anim").SanitizePath();
            CreateClipWithSpriteCurve(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = testSrc;
            window.AnimationDestinationPathRelative = testDst;
            window.DryRun = false;

            window.AnalyzeAnimations();
            Assert.AreEqual(1, window.NewCount, "Should detect one new animation before copy");

            window.CopyNew();
            AssetDatabase.SaveAssets();
            string dstA = Path.Combine(testDst, "SpriteAnim.anim").SanitizePath();
            ImportAssetIfExists(dstA);

            window.AnalyzeAnimations();
            Assert.AreEqual(
                0,
                window.ChangedCount,
                "Sprite animation should NOT be detected as changed after copy"
            );
            Assert.AreEqual(
                1,
                window.UnchangedCount,
                "Sprite animation should be detected as unchanged"
            );
        }

        [Test]
        public void CopyingMultipleAnimationsToNewNestedDirectoryDoesNotCreateDuplicateFolders()
        {
            // A unique subdirectory, so a deferred cleanup elsewhere cannot collide with this test.
            string testSrc = Path.Combine(SrcRoot, "NestedDirTest").SanitizePath();
            string testDst = Path.Combine(DstRoot, "NestedDirTest").SanitizePath();
            EnsureFolder(testSrc);
            EnsureFolder(testDst);

            string srcSubDir = Path.Combine(testSrc, "SubDir", "Nested").SanitizePath();
            EnsureFolder(srcSubDir);

            string srcA = Path.Combine(srcSubDir, "AnimA.anim").SanitizePath();
            string srcB = Path.Combine(srcSubDir, "AnimB.anim").SanitizePath();
            string srcC = Path.Combine(srcSubDir, "AnimC.anim").SanitizePath();
            CreateEmptyClip(srcA);
            CreateEmptyClip(srcB);
            CreateEmptyClip(srcC);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcSubDir);

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = testSrc;
            window.AnimationDestinationPathRelative = testDst;
            window.DryRun = false;

            window.AnalyzeAnimations();
            Assert.AreEqual(3, window.NewCount, "Should detect three new animations before copy");

            window.CopyNew();
            AssetDatabase.SaveAssets();

            string dstSubDir = Path.Combine(testDst, "SubDir", "Nested").SanitizePath();
            ImportAssetIfExists(dstSubDir);

            Assert.That(
                AssetDatabase.IsValidFolder(dstSubDir),
                "Destination subdirectory should exist"
            );

            string dstA = Path.Combine(dstSubDir, "AnimA.anim").SanitizePath();
            string dstB = Path.Combine(dstSubDir, "AnimB.anim").SanitizePath();
            string dstC = Path.Combine(dstSubDir, "AnimC.anim").SanitizePath();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<AnimationClip>(dstA),
                Is.Not.Null,
                "AnimA.anim should exist in destination"
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<AnimationClip>(dstB),
                Is.Not.Null,
                "AnimB.anim should exist in destination"
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<AnimationClip>(dstC),
                Is.Not.Null,
                "AnimC.anim should exist in destination"
            );

            string dstSubDirParent = Path.Combine(testDst, "SubDir").SanitizePath();
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absoluteParent = Path.Combine(projectRoot, dstSubDirParent);

            if (Directory.Exists(absoluteParent))
            {
                string[] subFolders = Directory.GetDirectories(absoluteParent);
                int nestedCount = 0;
                foreach (string folder in subFolders)
                {
                    string folderName = Path.GetFileName(folder);
                    if (folderName.StartsWith("Nested", System.StringComparison.OrdinalIgnoreCase))
                    {
                        nestedCount++;
                    }
                }

                Assert.AreEqual(
                    1,
                    nestedCount,
                    "Should have exactly one Nested folder, not duplicates like Nested 1, Nested 2"
                );
            }

            window.AnalyzeAnimations();
            Assert.AreEqual(0, window.NewCount, "Should not detect any new animations after copy");
            Assert.AreEqual(
                3,
                window.UnchangedCount,
                "All copied animations should be detected as unchanged"
            );
        }

        [Test]
        public void DryRunCopyNewDoesNotCreateNestedDestinationDirectory()
        {
            string sourceFolder = Path.Combine(SrcRoot, "DryRunNestedDirectoryTest").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "DryRunNestedDirectoryTest")
                .SanitizePath();
            EnsureFolder(sourceFolder);
            EnsureFolder(destinationFolder);

            string sourceNestedFolder = Path.Combine(sourceFolder, "Nested").SanitizePath();
            EnsureFolder(sourceNestedFolder);
            string sourceClip = Path.Combine(sourceNestedFolder, "Clip.anim").SanitizePath();
            CreateEmptyClip(sourceClip);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(sourceClip);

            string destinationNestedFolder = Path.Combine(destinationFolder, "Nested")
                .SanitizePath();
            string destinationClip = Path.Combine(destinationNestedFolder, "Clip.anim")
                .SanitizePath();
            Assert.IsFalse(Directory.Exists(ToFull(destinationNestedFolder)));

            AnimationCopierWindow window = CreateWindow();
            window.AnimationSourcePathRelative = sourceFolder;
            window.AnimationDestinationPathRelative = destinationFolder;
            window.DryRun = true;
            window.AnalyzeAnimations();
            Assert.AreEqual(1, window.NewCount);

            window.CopyNew();

            Assert.IsFalse(Directory.Exists(ToFull(destinationNestedFolder)));
            Assert.IsFalse(File.Exists(ToFull(destinationNestedFolder) + ".meta"));
            Assert.IsFalse(File.Exists(ToFull(destinationClip)));
            Assert.IsFalse(AssetDatabase.IsValidFolder(destinationNestedFolder));
        }

        [Test]
        public void ApiCopiesClipsWithoutWindowAndPreservesDestinationGuid()
        {
            string sourceFolder = Path.Combine(SrcRoot, "DirectApiCopy").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "DirectApiCopy").SanitizePath();
            string nestedSourceFolder = Path.Combine(sourceFolder, "Nested").SanitizePath();
            EnsureFolder(nestedSourceFolder);
            EnsureFolder(destinationFolder);
            string source = Path.Combine(nestedSourceFolder, "Clip.anim").SanitizePath();
            string destination = Path.Combine(destinationFolder, "Nested", "Clip.anim")
                .SanitizePath();
            CreateEmptyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);

            List<AnimationCopierAPI.Entry> entries = new();
            List<AnimationCopierAPI.Entry> orphans = new();
            Assert.IsTrue(
                AnimationCopierAPI.TryAnalyze(
                    sourceFolder,
                    destinationFolder,
                    entries,
                    orphans,
                    out string error
                ),
                error
            );
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(AnimationCopierAPI.Status.New, entries[0].Classification);
            Assert.AreEqual(destination, entries[0].DestinationPath);

            AnimationCopierAPI.Result preview = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyNew,
                false
            );
            Assert.IsTrue(preview.Succeeded, preview.Error);
            Assert.AreEqual(1, preview.ProcessedCount);
            Assert.IsFalse(File.Exists(ToFull(destination)));

            AnimationCopierAPI.Result cancelled = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyNew,
                true,
                cancelRequested: (path, current, total) => true
            );
            Assert.IsTrue(cancelled.Cancelled);
            Assert.IsFalse(File.Exists(ToFull(destination)));

            AnimationCopierAPI.Result copied = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyNew,
                true
            );
            Assert.IsTrue(copied.Succeeded, copied.Error);
            Assert.AreEqual(1, copied.ProcessedCount);
            Assert.IsTrue(File.Exists(ToFull(destination)));
            string destinationGuid = AssetDatabase.AssetPathToGUID(destination);
            Assert.IsFalse(string.IsNullOrWhiteSpace(destinationGuid));

            ModifyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);
            AnimationCopierAPI.Result replaced = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyChanged,
                true
            );
            Assert.IsTrue(replaced.Succeeded, replaced.Error);
            Assert.AreEqual(1, replaced.ProcessedCount);
            Assert.AreEqual(destinationGuid, AssetDatabase.AssetPathToGUID(destination));
            Assert.AreEqual(
                AssetDatabase.LoadAssetAtPath<AnimationClip>(source).frameRate,
                AssetDatabase.LoadAssetAtPath<AnimationClip>(destination).frameRate
            );
        }

        [Test]
        public void ApiAnalysisContinuesWhenDestinationAnimationCannotLoad()
        {
            string sourceFolder = Path.Combine(SrcRoot, "InvalidDestination").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "InvalidDestination").SanitizePath();
            EnsureFolder(sourceFolder);
            EnsureFolder(destinationFolder);
            string firstSource = Path.Combine(sourceFolder, "First.anim").SanitizePath();
            string secondSource = Path.Combine(sourceFolder, "Second.anim").SanitizePath();
            string invalidDestination = Path.Combine(destinationFolder, "First.anim")
                .SanitizePath();
            CreateEmptyClip(firstSource);
            CreateEmptyClip(secondSource);
            AssetDatabase.SaveAssets();
            File.WriteAllText(ToFull(invalidDestination), "invalid animation clip");
            try
            {
                Assert.IsTrue(
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(invalidDestination) == null
                );

                List<AnimationCopierAPI.Entry> entries = new();
                List<AnimationCopierAPI.Entry> orphans = new();
                Assert.IsTrue(
                    AnimationCopierAPI.TryAnalyze(
                        sourceFolder,
                        destinationFolder,
                        entries,
                        orphans,
                        out string error
                    ),
                    error
                );
                Assert.AreEqual(2, entries.Count);
                Assert.IsTrue(
                    entries.Exists(entry =>
                        string.Equals(
                            entry.SourcePath,
                            firstSource,
                            System.StringComparison.Ordinal
                        )
                        && entry.Classification == AnimationCopierAPI.Status.Changed
                    )
                );
                Assert.IsTrue(
                    entries.Exists(entry =>
                        string.Equals(
                            entry.SourcePath,
                            secondSource,
                            System.StringComparison.Ordinal
                        )
                        && entry.Classification == AnimationCopierAPI.Status.New
                    )
                );
            }
            finally
            {
                File.Delete(ToFull(invalidDestination));
            }
        }

        [Test]
        public void ApiRechecksCurrentStateBeforeDeletingClips()
        {
            string sourceFolder = Path.Combine(SrcRoot, "DirectApiDelete").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "DirectApiDelete").SanitizePath();
            EnsureFolder(sourceFolder);
            EnsureFolder(destinationFolder);
            string source = Path.Combine(sourceFolder, "Clip.anim").SanitizePath();
            string destination = Path.Combine(destinationFolder, "Clip.anim").SanitizePath();
            CreateEmptyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);
            Assert.IsTrue(AssetDatabase.CopyAsset(source, destination));
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(destination);

            List<AnimationCopierAPI.Entry> entries = new();
            List<AnimationCopierAPI.Entry> orphans = new();
            Assert.IsTrue(
                AnimationCopierAPI.TryAnalyze(
                    sourceFolder,
                    destinationFolder,
                    entries,
                    orphans,
                    out string error
                ),
                error
            );
            Assert.AreEqual(AnimationCopierAPI.Status.Unchanged, entries[0].Classification);
            ModifyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);

            AnimationCopierAPI.Result deleteSource = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.DeleteUnchangedSource,
                true
            );
            Assert.IsTrue(deleteSource.Succeeded, deleteSource.Error);
            Assert.AreEqual(1, deleteSource.SkippedCount);
            Assert.IsTrue(File.Exists(ToFull(source)));

            AnimationCopierAPI.Result deleteDestination = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { destination },
                AnimationCopierAPI.Operation.DeleteDestinationOrphans,
                true
            );
            Assert.IsTrue(deleteDestination.Succeeded, deleteDestination.Error);
            Assert.AreEqual(1, deleteDestination.SkippedCount);
            Assert.IsTrue(File.Exists(ToFull(destination)));

            AnimationCopierAPI.Result copied = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyChanged,
                true
            );
            Assert.IsTrue(copied.Succeeded, copied.Error);
            Assert.AreEqual(1, copied.ProcessedCount);
            AnimationCopierAPI.Result deletedSource = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.DeleteUnchangedSource,
                true
            );
            Assert.IsTrue(deletedSource.Succeeded, deletedSource.Error);
            Assert.AreEqual(1, deletedSource.ProcessedCount);
            Assert.IsFalse(File.Exists(ToFull(source)));

            AnimationCopierAPI.Result deletedOrphan = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { destination },
                AnimationCopierAPI.Operation.DeleteDestinationOrphans,
                true
            );
            Assert.IsTrue(deletedOrphan.Succeeded, deletedOrphan.Error);
            Assert.AreEqual(1, deletedOrphan.ProcessedCount);
            Assert.IsFalse(File.Exists(ToFull(destination)));
        }

        [Test]
        public void ApiRejectsOverlappingRoots()
        {
            string sourceFolder = Path.Combine(SrcRoot, "DirectApiOverlap").SanitizePath();
            EnsureFolder(sourceFolder);
            string destinationFolder = Path.Combine(sourceFolder, "Nested", "..").SanitizePath();
            List<AnimationCopierAPI.Entry> entries = new();
            List<AnimationCopierAPI.Entry> orphans = new();

            Assert.IsFalse(
                AnimationCopierAPI.TryAnalyze(
                    sourceFolder,
                    destinationFolder,
                    entries,
                    orphans,
                    out string error
                )
            );
            StringAssert.Contains("overlap", error);
            AnimationCopierAPI.Result result = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { Path.Combine(sourceFolder, "Clip.anim").SanitizePath() },
                AnimationCopierAPI.Operation.CopyAll,
                true
            );
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("overlap", result.Error);
        }

        [Test]
        public void ApiExcludesAnimationClipsStoredInNonAnimFiles()
        {
            string sourceFolder = Path.Combine(SrcRoot, "DirectApiNonAnim").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "DirectApiNonAnim").SanitizePath();
            EnsureFolder(sourceFolder);
            EnsureFolder(destinationFolder);
            string source = Path.Combine(sourceFolder, "Embedded.asset").SanitizePath();
            AnimationClip clip = new();
            Track(clip);
            AssetDatabase.CreateAsset(clip, source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);

            List<AnimationCopierAPI.Entry> entries = new();
            List<AnimationCopierAPI.Entry> orphans = new();
            Assert.IsTrue(
                AnimationCopierAPI.TryAnalyze(
                    sourceFolder,
                    destinationFolder,
                    entries,
                    orphans,
                    out string error
                ),
                error
            );
            Assert.AreEqual(0, entries.Count);

            AnimationCopierAPI.Result result = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyAll,
                true
            );
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.FailedCount);
            Assert.IsTrue(File.Exists(ToFull(source)));
            Assert.IsFalse(File.Exists(ToFull(Path.Combine(destinationFolder, "Embedded.asset"))));
        }

        [Test]
        public void ApiForceReplaceCopiesAnUnchangedClip()
        {
            string sourceFolder = Path.Combine(SrcRoot, "DirectApiForceReplace").SanitizePath();
            string destinationFolder = Path.Combine(DstRoot, "DirectApiForceReplace")
                .SanitizePath();
            EnsureFolder(sourceFolder);
            EnsureFolder(destinationFolder);
            string source = Path.Combine(sourceFolder, "Clip.anim").SanitizePath();
            string destination = Path.Combine(destinationFolder, "Clip.anim").SanitizePath();
            CreateEmptyClip(source);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(source);
            Assert.IsTrue(AssetDatabase.CopyAsset(source, destination));
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(destination);

            const string marker = "# Direct API force replacement marker";
            File.AppendAllText(ToFull(source), "\n" + marker + "\n");
            ImportAssetIfExists(source);
            Assert.IsFalse(File.ReadAllText(ToFull(destination)).Contains(marker));

            AnimationCopierAPI.Result skipped = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyAll,
                true
            );
            Assert.IsTrue(skipped.Succeeded, skipped.Error);
            Assert.AreEqual(1, skipped.SkippedCount);
            Assert.IsFalse(File.ReadAllText(ToFull(destination)).Contains(marker));

            AnimationCopierAPI.Result replaced = AnimationCopierAPI.Run(
                sourceFolder,
                destinationFolder,
                new[] { source },
                AnimationCopierAPI.Operation.CopyAll,
                true,
                forceReplaceUnchanged: true
            );
            Assert.IsTrue(replaced.Succeeded, replaced.Error);
            Assert.AreEqual(1, replaced.ProcessedCount);
            StringAssert.Contains(marker, File.ReadAllText(ToFull(destination)));
        }

        private void CreateEmptyClip(string relPath)
        {
            string dir = Path.GetDirectoryName(relPath).SanitizePath();
            EnsureFolder(dir);
            AnimationClip clip = new();
            AssetDatabase.CreateAsset(clip, relPath);
            TrackAssetPath(relPath);
        }

        private void CreateClipWithSpriteCurve(string relPath)
        {
            string dir = Path.GetDirectoryName(relPath).SanitizePath();
            EnsureFolder(dir);
            AnimationClip clip = new() { frameRate = 12f };
            ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[3];
            keyframes[0] = new ObjectReferenceKeyframe { time = 0f, value = null };
            keyframes[1] = new ObjectReferenceKeyframe { time = 0.1f, value = null };
            keyframes[2] = new ObjectReferenceKeyframe { time = 0.2f, value = null };
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
                "",
                typeof(SpriteRenderer),
                "m_Sprite"
            );
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, relPath);
            TrackAssetPath(relPath);
        }

        private AnimationCopierWindow CreateWindow()
        {
            return Track(ScriptableObject.CreateInstance<AnimationCopierWindow>());
        }
    }
#endif
}
