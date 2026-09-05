// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class LlmArtifactCleanerTests : CommonTestBase
    {
        private const string PackagePrefix = "Packages/com.wallstop-studios.unity-helpers/";
        private const string AssetsRoot = "Assets/__LlmArtifactCleanerTests__";

        [SetUp]
        public override void BaseSetUp()
        {
            // Check inherited handler pollution before base setup changes its attribution.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            LlmArtifactCleaner.ResetForTesting();
            base.BaseSetUp();
            EnsureFolder(AssetsRoot);
        }

        [TearDown]
        public override void TearDown()
        {
            CleanupTrackedFoldersAndAssets();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            base.TearDown();
            // Every asset op above scheduled a drain that would otherwise leak forward.
            AssetPostprocessorDeferral.FlushForTesting();
            LlmArtifactCleaner.ResetForTesting();
        }

        [Test]
        public void DeletesLlmArtifactsInsidePackage()
        {
            string assetPath = PackagePrefix + "_llm_cleaner_test.txt";
            string absolutePath = Path.Combine(Environment.CurrentDirectory, assetPath);
            File.WriteAllText(absolutePath, "temp");
            TrackAssetPath(assetPath);

            Assert.IsTrue(LlmArtifactCleaner.ShouldDelete(assetPath));

            // Manually invoke deletion logic since OnPostprocessAllAssets timing is unreliable in tests
            LlmArtifactCleaner.DeleteBlockedAssets(new[] { assetPath });
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );

            Assert.IsTrue(string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)));
            Assert.IsFalse(File.Exists(absolutePath));
            Assert.IsFalse(File.Exists(absolutePath + ".meta"));
        }

        [Test]
        public void LeavesAssetsOutsidePackageUntouched()
        {
            string folderPath = AssetsRoot + "/Keep";
            EnsureFolder(folderPath);
            string assetPath = folderPath + "/_llm_keep.txt";
            string absolutePath = Path.Combine(Environment.CurrentDirectory, assetPath);
            File.WriteAllText(absolutePath, "keep");
            TrackFolder(folderPath);
            TrackAssetPath(assetPath);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );

            Assert.IsFalse(LlmArtifactCleaner.ShouldDelete(assetPath));
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            Assert.IsFalse(string.IsNullOrEmpty(guid));
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            Assert.IsTrue(textAsset != null);
            Assert.AreEqual("keep", textAsset.text);
        }

        private static IEnumerable<TestCaseData> ShouldDeletePositiveCases()
        {
            yield return new TestCaseData(PackagePrefix + "_llm_artifact.cs").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedCsFile"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_test.txt").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedTxtFile"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_").SetName(
                "ShouldDelete.RootLevel.LlmPrefixOnly"
            );

            yield return new TestCaseData(PackagePrefix + "Runtime/_llm_generated.cs").SetName(
                "ShouldDelete.NestedOnce.LlmPrefixedFile"
            );
            yield return new TestCaseData(PackagePrefix + "Editor/Core/_llm_helper.cs").SetName(
                "ShouldDelete.NestedTwice.LlmPrefixedFile"
            );
            yield return new TestCaseData(
                PackagePrefix + "Runtime/Core/Utils/_llm_deep.txt"
            ).SetName("ShouldDelete.DeeplyNested.LlmPrefixedFile");

            yield return new TestCaseData(PackagePrefix + "_llm_folder/file.cs").SetName(
                "ShouldDelete.LlmFolder.RegularFile"
            );
            yield return new TestCaseData(
                PackagePrefix + "Runtime/_llm_generated/Helper.cs"
            ).SetName("ShouldDelete.NestedLlmFolder.RegularFile");
            yield return new TestCaseData(PackagePrefix + "_llm_dir/_llm_file.cs").SetName(
                "ShouldDelete.LlmFolderAndFile.BothPrefixed"
            );

            yield return new TestCaseData(PackagePrefix + "test_llm_artifact.cs").SetName(
                "ShouldDelete.RootLevel.LlmInMiddleOfFilename"
            );
            yield return new TestCaseData(PackagePrefix + "Runtime/my_llm_helper.cs").SetName(
                "ShouldDelete.Nested.LlmInMiddleOfFilename"
            );
            yield return new TestCaseData(PackagePrefix + "data_llm_folder/file.txt").SetName(
                "ShouldDelete.LlmInMiddleOfFolderName.RegularFile"
            );

            yield return new TestCaseData(PackagePrefix + "artifact_llm_.cs").SetName(
                "ShouldDelete.RootLevel.LlmAtEndOfFilename"
            );
            yield return new TestCaseData(PackagePrefix + "folder_llm_/test.txt").SetName(
                "ShouldDelete.LlmAtEndOfFolderName.RegularFile"
            );

            yield return new TestCaseData(PackagePrefix + "_llm_script.js").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedJsFile"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_config.json").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedJsonFile"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_shader.shader").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedShaderFile"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_prefab.prefab").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedPrefabFile"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_meta.meta").SetName(
                "ShouldDelete.RootLevel.LlmPrefixedMetaFile"
            );

            yield return new TestCaseData(PackagePrefix + "_llm_lowercase.cs").SetName(
                "ShouldDelete.CaseSensitivity.LowercaseLlmPrefix"
            );

            yield return new TestCaseData(PackagePrefix + "_llm_first_llm_second.cs").SetName(
                "ShouldDelete.MultipleLlmOccurrences.InFilename"
            );
            yield return new TestCaseData(
                PackagePrefix + "_llm_folder/_llm_nested/_llm_file.cs"
            ).SetName("ShouldDelete.MultipleLlmOccurrences.InPath");

            yield return new TestCaseData(PackagePrefix + "_llm_file-with-dashes.cs").SetName(
                "ShouldDelete.SpecialChars.DashesInFilename"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_file.multiple.dots.cs").SetName(
                "ShouldDelete.SpecialChars.MultipleDotsInFilename"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_file_with_underscores.cs").SetName(
                "ShouldDelete.SpecialChars.UnderscoresInFilename"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_ file with spaces.cs").SetName(
                "ShouldDelete.SpecialChars.SpacesInFilename"
            );

            yield return new TestCaseData(PackagePrefix + "folder_/_llm_file.cs").SetName(
                "ShouldDelete.AdjacentUnderscore.UnderscoreInPreviousSegment"
            );
            yield return new TestCaseData(PackagePrefix + "_llm_/_folder.cs").SetName(
                "ShouldDelete.AdjacentUnderscore.UnderscoreInNextSegment"
            );
            yield return new TestCaseData(PackagePrefix + "a_llm_b.cs").SetName(
                "ShouldDelete.InMiddle.SingleUnderscoreBoundary"
            );
            yield return new TestCaseData(PackagePrefix + "folder/a_llm_b/file.cs").SetName(
                "ShouldDelete.InFolderName.SingleUnderscoreBoundary"
            );
        }

        [Test]
        [TestCaseSource(nameof(ShouldDeletePositiveCases))]
        public void ShouldDeleteReturnsTrueForLlmArtifactsInsidePackage(string assetPath)
        {
            bool result = LlmArtifactCleaner.ShouldDelete(assetPath);
            Assert.IsTrue(result, $"Expected ShouldDelete to return true for path: {assetPath}");
        }

        private static IEnumerable<TestCaseData> ShouldDeleteNegativeCases()
        {
            yield return new TestCaseData("Assets/_llm_artifact.cs").SetName(
                "ShouldNotDelete.AssetsFolder.LlmPrefixedFile"
            );
            yield return new TestCaseData("Assets/Scripts/_llm_helper.cs").SetName(
                "ShouldNotDelete.AssetsNested.LlmPrefixedFile"
            );
            yield return new TestCaseData("Assets/_llm_folder/file.cs").SetName(
                "ShouldNotDelete.AssetsLlmFolder.RegularFile"
            );

            yield return new TestCaseData("Packages/com.other.package/_llm_test.cs").SetName(
                "ShouldNotDelete.OtherPackage.LlmPrefixedFile"
            );
            yield return new TestCaseData("Packages/com.unity.ugui/_llm_artifact.cs").SetName(
                "ShouldNotDelete.UnityPackage.LlmPrefixedFile"
            );
            yield return new TestCaseData(
                "Packages/com.wallstop-studios.other/_llm_test.cs"
            ).SetName("ShouldNotDelete.SimilarPackageName.LlmPrefixedFile");

            yield return new TestCaseData(
                "Packages/com.wallstop-studios.unity-helpers-extended/_llm_test.cs"
            ).SetName("ShouldNotDelete.ExtendedPackageName.LlmPrefixedFile");
            yield return new TestCaseData(
                "Assets/Packages/com.wallstop-studios.unity-helpers/_llm_test.cs"
            ).SetName("ShouldNotDelete.PackageInAssetsFolder.LlmPrefixedFile");

            yield return new TestCaseData(PackagePrefix + "RegularFile.cs").SetName(
                "ShouldNotDelete.InsidePackage.RegularFile"
            );
            yield return new TestCaseData(PackagePrefix + "Runtime/Helper.cs").SetName(
                "ShouldNotDelete.InsidePackage.NestedRegularFile"
            );
            yield return new TestCaseData(PackagePrefix + "Editor/Core/Utils/Tool.cs").SetName(
                "ShouldNotDelete.InsidePackage.DeeplyNestedFile"
            );

            yield return new TestCaseData(PackagePrefix + "_LLM_uppercase.cs").SetName(
                "ShouldNotDelete.CaseSensitivity.UppercaseLLM"
            );
            yield return new TestCaseData(PackagePrefix + "_Llm_mixedcase.cs").SetName(
                "ShouldNotDelete.CaseSensitivity.MixedCaseLlm"
            );
            yield return new TestCaseData(PackagePrefix + "_llmnotrailingunderscore.cs").SetName(
                "ShouldNotDelete.PartialMatch.NoTrailingUnderscore"
            );
            yield return new TestCaseData(PackagePrefix + "llm_noleadingunderscore.cs").SetName(
                "ShouldNotDelete.PartialMatch.NoLeadingUnderscore"
            );
            yield return new TestCaseData(PackagePrefix + "__llm__doubleunderscore.cs").SetName(
                "ShouldNotDelete.PartialMatch.DoubleUnderscores"
            );
            yield return new TestCaseData(PackagePrefix + "_l_l_m_separated.cs").SetName(
                "ShouldNotDelete.PartialMatch.SeparatedLetters"
            );

            yield return new TestCaseData(PackagePrefix + "___llm___tripleunderscore.cs").SetName(
                "ShouldNotDelete.PartialMatch.TripleUnderscores"
            );
            yield return new TestCaseData(PackagePrefix + "file__llm_.cs").SetName(
                "ShouldNotDelete.PartialMatch.DoubleUnderscoreBeforeLlm"
            );
            yield return new TestCaseData(PackagePrefix + "file_llm__.cs").SetName(
                "ShouldNotDelete.PartialMatch.DoubleUnderscoreAfterLlm"
            );
            yield return new TestCaseData(PackagePrefix + "__llm_/folder/file.cs").SetName(
                "ShouldNotDelete.PartialMatch.DoubleUnderscorePrefixFolder"
            );
            yield return new TestCaseData(PackagePrefix + "_llm__/folder/file.cs").SetName(
                "ShouldNotDelete.PartialMatch.DoubleUnderscoreSuffixFolder"
            );
            yield return new TestCaseData(PackagePrefix + "folder/__llm__/file.cs").SetName(
                "ShouldNotDelete.PartialMatch.DoubleUnderscoreFolder"
            );

            yield return new TestCaseData(PackagePrefix + "algorithm_helper.cs").SetName(
                "ShouldNotDelete.NaturalText.AlgorithmInName"
            );
            yield return new TestCaseData(PackagePrefix + "llm.cs").SetName(
                "ShouldNotDelete.NaturalText.JustLlm"
            );
            yield return new TestCaseData(PackagePrefix + "LlmHelper.cs").SetName(
                "ShouldNotDelete.NaturalText.LlmHelperClass"
            );

            yield return new TestCaseData("").SetName("ShouldNotDelete.EdgeCase.EmptyString");
            yield return new TestCaseData("   ").SetName("ShouldNotDelete.EdgeCase.WhitespaceOnly");
            yield return new TestCaseData(PackagePrefix).SetName(
                "ShouldNotDelete.EdgeCase.PackagePrefixOnly"
            );

            yield return new TestCaseData("_llm_test.cs").SetName(
                "ShouldNotDelete.RelativePath.LlmPrefixedFile"
            );
            yield return new TestCaseData("./_llm_test.cs").SetName(
                "ShouldNotDelete.DotRelativePath.LlmPrefixedFile"
            );
            yield return new TestCaseData("../_llm_test.cs").SetName(
                "ShouldNotDelete.ParentRelativePath.LlmPrefixedFile"
            );
        }

        [Test]
        [TestCaseSource(nameof(ShouldDeleteNegativeCases))]
        public void ShouldDeleteReturnsFalseForNonTargetedPaths(string assetPath)
        {
            bool result = LlmArtifactCleaner.ShouldDelete(assetPath);
            Assert.IsFalse(result, $"Expected ShouldDelete to return false for path: {assetPath}");
        }

        private static IEnumerable<TestCaseData> NullAndInvalidPathCases()
        {
            yield return new TestCaseData(null).SetName(
                "ShouldNotDelete.NullPath.ReturnsGracefully"
            );
        }

        [Test]
        [TestCaseSource(nameof(NullAndInvalidPathCases))]
        public void ShouldDeleteHandlesNullPathGracefully(string assetPath)
        {
            bool result = LlmArtifactCleaner.ShouldDelete(assetPath);
            Assert.IsFalse(result, "Expected ShouldDelete to return false for null path");
        }

        private static IEnumerable<TestCaseData> DeleteBlockedAssetsEdgeCases()
        {
            yield return new TestCaseData(new object[] { null }).SetName(
                "DeleteBlockedAssets.NullArray.DoesNotThrow"
            );
            yield return new TestCaseData(new object[] { Array.Empty<string>() }).SetName(
                "DeleteBlockedAssets.EmptyArray.DoesNotThrow"
            );
            yield return new TestCaseData(new object[] { new string[] { null } }).SetName(
                "DeleteBlockedAssets.ArrayWithNullElement.DoesNotThrow"
            );
            yield return new TestCaseData(new object[] { new[] { "" } }).SetName(
                "DeleteBlockedAssets.ArrayWithEmptyString.DoesNotThrow"
            );
            yield return new TestCaseData(new object[] { new[] { "   " } }).SetName(
                "DeleteBlockedAssets.ArrayWithWhitespace.DoesNotThrow"
            );
            yield return new TestCaseData(new object[] { new[] { null, "", "   " } }).SetName(
                "DeleteBlockedAssets.ArrayWithMixedInvalid.DoesNotThrow"
            );
        }

        [Test]
        [TestCaseSource(nameof(DeleteBlockedAssetsEdgeCases))]
        public void DeleteBlockedAssetsHandlesEdgeCasesGracefully(string[] assetPaths)
        {
            Assert.DoesNotThrow(
                () => LlmArtifactCleaner.DeleteBlockedAssets(assetPaths),
                "DeleteBlockedAssets should handle edge cases without throwing"
            );
        }

        [Test]
        public void DeleteBlockedAssetsDeduplicatesQueuedPaths()
        {
            string assetPath = PackagePrefix + "_llm_duplicate.txt";
            int deleteCount = 0;

            LlmArtifactCleaner.SetDeleteAssetOverrideForTesting(_ => deleteCount++);
            try
            {
                LlmArtifactCleaner.DeleteBlockedAssets(new[] { assetPath, assetPath, assetPath });
            }
            finally
            {
                LlmArtifactCleaner.ResetForTesting();
            }

            Assert.AreEqual(1, deleteCount, "Duplicate paths should be deleted only once.");
            Assert.AreEqual(
                0,
                LlmArtifactCleaner.PendingDeletionCountForTesting,
                "Pending queue should be empty after the deduplicated delete pass."
            );
        }

        [Test]
        public void DeleteBlockedAssetsReentrantQueueDrainsBeforeReturning()
        {
            string firstPath = PackagePrefix + "_llm_first.txt";
            string secondPath = PackagePrefix + "_llm_second.txt";
            List<string> deletedPaths = new();
            bool queuedSecondPath = false;

            LlmArtifactCleaner.SetDeleteAssetOverrideForTesting(assetPath =>
            {
                deletedPaths.Add(assetPath);
                if (!queuedSecondPath)
                {
                    queuedSecondPath = true;
                    LlmArtifactCleaner.DeleteBlockedAssets(new[] { secondPath, secondPath });
                }
            });

            try
            {
                LlmArtifactCleaner.DeleteBlockedAssets(new[] { firstPath });
            }
            finally
            {
                LlmArtifactCleaner.ResetForTesting();
            }

            CollectionAssert.AreEquivalent(
                new[] { firstPath, secondPath },
                deletedPaths,
                "Reentrant deletes should stay queued and drain before the outer delete pass returns."
            );
            Assert.AreEqual(
                0,
                LlmArtifactCleaner.PendingDeletionCountForTesting,
                "Pending queue should be empty after reentrant deletes drain."
            );
        }

        private static IEnumerable<TestCaseData> MovedAssetScenarioCases()
        {
            yield return new TestCaseData(
                "Assets/_llm_temp.cs",
                PackagePrefix + "_llm_temp.cs",
                true
            ).SetName("MovedAsset.IntoPackage.LlmPrefixed.ShouldDelete");

            yield return new TestCaseData(
                PackagePrefix + "_llm_temp.cs",
                "Assets/_llm_temp.cs",
                false
            ).SetName("MovedAsset.OutOfPackage.LlmPrefixed.ShouldNotDelete");

            yield return new TestCaseData(
                "Assets/Regular.cs",
                PackagePrefix + "Regular.cs",
                false
            ).SetName("MovedAsset.IntoPackage.RegularFile.ShouldNotDelete");

            yield return new TestCaseData(
                PackagePrefix + "Runtime/_llm_file.cs",
                PackagePrefix + "Editor/_llm_file.cs",
                true
            ).SetName("MovedAsset.WithinPackage.LlmPrefixed.ShouldDelete");

            yield return new TestCaseData(
                PackagePrefix + "Runtime/Regular.cs",
                PackagePrefix + "Editor/Regular.cs",
                false
            ).SetName("MovedAsset.WithinPackage.RegularFile.ShouldNotDelete");
        }

        [Test]
        [TestCaseSource(nameof(MovedAssetScenarioCases))]
        public void ShouldDeleteEvaluatesDestinationPathForMovedAssets(
            string sourcePath,
            string destinationPath,
            bool expectedShouldDelete
        )
        {
            bool result = LlmArtifactCleaner.ShouldDelete(destinationPath);
            Assert.AreEqual(
                expectedShouldDelete,
                result,
                $"Expected ShouldDelete({destinationPath}) to return {expectedShouldDelete} when moving from {sourcePath}"
            );
        }
    }
}
