// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
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
            // Clean up only tracked folders/assets that this test created
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

            // Create destination copy to become unchanged
            string dstA = Path.Combine(DstRoot, "A.anim").SanitizePath();
            Assert.IsTrue(AssetDatabase.CopyAsset(srcA, dstA));
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(dstA);

            // Modify source so it becomes changed vs. destination
            ModifyClip(srcA);
            AssetDatabase.SaveAssets();
            ImportAssetIfExists(srcA);

            // Add orphan in destination
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
            // Modify source to force change
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

            // Ensure orphan exists
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

            // Initial analysis - should detect as new
            window.AnalyzeAnimations();
            Assert.AreEqual(1, window.NewCount, "Should detect one new animation before copy");
            Assert.AreEqual(0, window.ChangedCount);
            Assert.AreEqual(0, window.UnchangedCount);

            // Copy the animation
            window.CopyNew();
            AssetDatabase.SaveAssets();
            string dstA = Path.Combine(testDst, "A.anim").SanitizePath();
            ImportAssetIfExists(dstA);

            // Re-analyze - copied animation should be detected as unchanged, not changed
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

            // Initial analysis - should detect as new
            window.AnalyzeAnimations();
            Assert.AreEqual(1, window.NewCount, "Should detect one new animation before copy");

            // Copy the animation
            window.CopyNew();
            AssetDatabase.SaveAssets();
            string dstA = Path.Combine(testDst, "SpriteAnim.anim").SanitizePath();
            ImportAssetIfExists(dstA);

            // Re-analyze - should be unchanged
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

            // Check that no duplicate folders were created
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

        // Helpers
        private static string ToFull(string rel) =>
            Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    rel
                )
                .SanitizePath();

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

        private static void ModifyClip(string relPath)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(relPath);
            Assert.IsTrue(clip != null);
            clip.frameRate = clip.frameRate + 1f;
            EditorUtility.SetDirty(clip);
        }

        private AnimationCopierWindow CreateWindow()
        {
            return Track(ScriptableObject.CreateInstance<AnimationCopierWindow>());
        }
    }
#endif
}
