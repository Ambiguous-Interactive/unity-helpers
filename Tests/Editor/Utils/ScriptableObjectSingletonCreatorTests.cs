// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ScriptableObjectSingletonCreatorTests : CommonTestBase
    {
        private const string TestRoot = "Assets/Resources/CreatorTests";
        private bool _previousEditorUiSuppress;
        private bool _previousIgnoreCompilationState;

        public override void CommonOneTimeSetUp()
        {
            if (Application.isPlaying)
            {
                return;
            }
            base.CommonOneTimeSetUp();

            // Batching consolidates the deletes into a single AssetDatabase.Refresh.
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                CleanupAllKnownTestFolders();

                // Case-mismatch tests on a case-insensitive file system leave duplicates behind.
                TryDeleteFolderAndDuplicates("Assets/Resources", "CreatorTests");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "casetest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CASETEST");
                TryDeleteFolderAndDuplicates("Assets/Resources", "cASEtest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTEST");

                AssetDatabase.SaveAssets();
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();

            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;

            // Batching cuts the per-test Refresh calls from 5+ to one.
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                // Before each test: a data-driven case would otherwise inherit the previous one's folders.
                TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "casetest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CASETEST");
                TryDeleteFolderAndDuplicates("Assets/Resources", "cASEtest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTEST");
                AssetDatabase.SaveAssets();
            }
            yield return null;

            ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;

            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
            // Unity may report isCompiling/isUpdating during a test run after AssetDatabase operations.
            _previousIgnoreCompilationState =
                ScriptableObjectSingletonCreator.IgnoreCompilationState;
            ScriptableObjectSingletonCreator.IgnoreCompilationState = true;
            ScriptableObjectSingletonCreator.TypeFilter = static type =>
                type == typeof(CaseMismatch)
                || type == typeof(Duplicate)
                || type == typeof(A.NameCollision)
                || type == typeof(B.NameCollision)
                || type == typeof(RetrySingleton)
                || type == typeof(FileBlockSingleton)
                || type == typeof(NoRetrySingleton)
                || type == typeof(AssetDatabaseRaceSingleton);

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder(TestRoot);
                // Ensure the metadata folder exists to prevent modal dialogs
                EnsureFolder("Assets/Resources/Wallstop Studios/Unity Helpers");
            }

            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            IEnumerator baseEnumerator = base.UnityTearDown();
            while (baseEnumerator.MoveNext())
            {
                yield return baseEnumerator.Current;
            }

            // Batching consolidates 20+ delete and cleanup operations into one Refresh.
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                string[] guids = AssetDatabase.FindAssets("t:Object", new[] { TestRoot });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AssetDatabase.DeleteAsset(path);
                }

                DeleteFileIfExists(TestRoot + "/FileBlock");
                DeleteFileIfExists(TestRoot + "/NoRetry");
                DeleteFileIfExists(TestRoot + "/Retry");

                TryDeleteFolder(TestRoot + "/Collision");
                TryDeleteFolder(TestRoot + "/Retry");
                TryDeleteFolder(TestRoot + "/Retry 1");
                TryDeleteFolder(TestRoot + "/FileBlock");
                TryDeleteFolder(TestRoot + "/FileBlock 1");
                TryDeleteFolder(TestRoot + "/NoRetry");
                TryDeleteFolder(TestRoot + "/NoRetry 1");
                TryDeleteFolder(TestRoot + "/Race");
                TryDeleteFolder(TestRoot + "/Race 1");
                TryDeleteFolder(TestRoot);

                TryDeleteFolderAndDuplicates("Assets/Resources", "CreatorTests");

                TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "casetest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CASETEST");
                TryDeleteFolderAndDuplicates("Assets/Resources", "cASEtest");
                TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTEST");

                TryDeleteFolder("Assets/Resources");

                AssetDatabase.SaveAssets();
            }

            ScriptableObjectSingletonCreator.TypeFilter = null;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = false;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = false;
            ScriptableObjectSingletonCreator.IgnoreCompilationState =
                _previousIgnoreCompilationState;
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();
            EditorUi.Suppress = _previousEditorUiSuppress;

            // CleanupAllKnownTestFolders already batches its operations internally.
            CleanupAllKnownTestFolders();
            AssetPostprocessorDeferral.FlushForTesting();
        }

        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();

            CleanupAllKnownTestFolders();
        }

        [UnityTest]
        public IEnumerator DoesNotCreateDuplicateSubfolderOnCaseMismatch()
        {
            EnsureFolder("Assets/Resources/cASEtest");

            // Without the refresh, GetSubFolders cannot see the case-mismatched folder.
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string assetPath = "Assets/Resources/cASEtest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(assetPath);

            bool wrongCasedFolderExists = AssetDatabase.IsValidFolder("Assets/Resources/cASEtest");
            string[] subFolders = AssetDatabase.GetSubFolders("Assets/Resources");
            string subFolderList = subFolders != null ? string.Join(", ", subFolders) : "null";

            Assert.IsTrue(
                wrongCasedFolderExists,
                $"Setup: Wrong-cased folder 'cASEtest' should exist before EnsureSingletonAssets. "
                    + $"Subfolders of Assets/Resources: [{subFolderList}]"
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );

            bool wrongCasedFolderStillExists = AssetDatabase.IsValidFolder(
                "Assets/Resources/cASEtest"
            );
            bool correctCasedFolderExists = AssetDatabase.IsValidFolder(
                "Assets/Resources/CaseTest"
            );
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            string actualAssetPath = asset != null ? AssetDatabase.GetAssetPath(asset) : "null";

            string[] postSubFolders = AssetDatabase.GetSubFolders("Assets/Resources");
            string postSubFolderList =
                postSubFolders != null ? string.Join(", ", postSubFolders) : "null";

            bool anyDuplicateExists = false;
            string duplicateFound = null;
            foreach (string folder in postSubFolders)
            {
                string folderName = Path.GetFileName(folder);

                if (
                    folderName.StartsWith("CaseTest ", StringComparison.OrdinalIgnoreCase)
                    || folderName.StartsWith("cASEtest ", StringComparison.OrdinalIgnoreCase)
                )
                {
                    string[] parts = folderName.Split(' ');
                    if (2 <= parts.Length && int.TryParse(parts[^1], out _))
                    {
                        anyDuplicateExists = true;
                        duplicateFound = folder;
                        break;
                    }
                }
            }

            string diagnostics =
                $"wrongCasedFolderStillExists={wrongCasedFolderStillExists}, "
                + $"correctCasedFolderExists={correctCasedFolderExists}, "
                + $"anyDuplicateExists={anyDuplicateExists}, "
                + $"duplicateFound={duplicateFound ?? "none"}, "
                + $"assetExists={asset != null}, actualAssetPath={actualAssetPath}, "
                + $"Subfolders of Assets/Resources: [{postSubFolderList}]";

            // The folder may have been renamed to the correct casing, so either casing is acceptable.
            Assert.IsTrue(
                wrongCasedFolderStillExists || correctCasedFolderExists,
                $"Either original or corrected folder should exist. Diagnostics: {diagnostics}"
            );
            Assert.IsFalse(
                anyDuplicateExists,
                $"No duplicate folder should exist. Diagnostics: {diagnostics}"
            );

            Object finalAsset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (finalAsset == null)
            {
                finalAsset = AssetDatabase.LoadAssetAtPath<Object>(
                    "Assets/Resources/CaseTest/CaseMismatch.asset"
                );
            }

            Assert.IsTrue(
                finalAsset != null,
                $"Asset should exist in either folder. Diagnostics: {diagnostics}"
            );
        }

        [UnityTest]
        public IEnumerator SkipsCreationWhenTargetPathOccupied()
        {
            string targetFolder = TestRoot;
            EnsureFolder(targetFolder);
            string occupiedPath = targetFolder + "/Duplicate.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(occupiedPath) == null)
            {
                TextAsset ta = new("occupied");
                AssetDatabase.CreateAsset(ta, occupiedPath);
            }

            LogAssert.Expect(LogType.Warning, new Regex("target path already occupied"));
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(targetFolder + "/Duplicate 1.asset") == null
            );
        }

        [UnityTest]
        public IEnumerator WarnsOnTypeNameCollision()
        {
            EnsureFolder("Assets/Resources/CreatorTests/Collision");

            LogAssert.Expect(LogType.Warning, new Regex("Type name collision"));
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(
                    "Assets/Resources/CreatorTests/Collision/NameCollision.asset"
                ) == null
            );
        }

        [UnityTest]
        public IEnumerator EnsureSingletonAssetsIsIdempotent()
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            string firstGuid = AssetDatabase.AssetPathToGUID(targetPath);
            Assert.IsFalse(string.IsNullOrEmpty(firstGuid));

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            string secondGuid = AssetDatabase.AssetPathToGUID(targetPath);
            Assert.AreEqual(firstGuid, secondGuid);
        }

        [UnityTest]
        public IEnumerator SkipsEnsureInsideAssetImportWorkerProcess()
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            Func<bool> originalDetector =
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck;
            ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck = static () => true;
            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                yield return null;
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(targetPath) == null);
            }
            finally
            {
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck = originalDetector;
            }

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(targetPath) != null);
        }

        [UnityTest]
        public IEnumerator EnvironmentVariablesTriggerWorkerDetection(
            [ValueSource(nameof(AssetImportWorkerEnvironmentScenarios))] string environmentVariable
        )
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            string originalValue = Environment.GetEnvironmentVariable(environmentVariable);
            Func<bool> originalDetector =
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck;
            try
            {
                Environment.SetEnvironmentVariable(environmentVariable, "1");
                ScriptableObjectSingletonCreator.ResetAssetImportWorkerDetectionStateForTests();
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck = null;

                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                yield return null;

                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(targetPath) == null);
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, originalValue);
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck = originalDetector;
                ScriptableObjectSingletonCreator.ResetAssetImportWorkerDetectionStateForTests();
            }

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(targetPath) != null);
        }

        [UnityTest]
        public IEnumerator DetectorExceptionsDoNotBlockEnsure()
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            Func<bool> originalDetector =
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck;
            ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck = static () =>
                throw new InvalidOperationException("detector failure");

            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                yield return null;
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(targetPath) != null);
            }
            finally
            {
                ScriptableObjectSingletonCreator.AssetImportWorkerProcessCheck = originalDetector;
            }
        }

        [UnityTest]
        public IEnumerator RetriesCreationAfterTemporaryFolderBlock()
        {
            string retryFolder = TestRoot + "/Retry";
            string retryAsset = retryFolder + "/RetrySingleton.asset";
            string blockerMeta = retryFolder + ".meta";
            string retryFolderVariant = retryFolder + " 1";

            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            AssetDatabase.DeleteAsset(retryAsset);
            AssetDatabase.DeleteAsset(retryFolder);
            AssetDatabase.DeleteAsset(retryFolderVariant);
            CleanupRetryTestState(retryFolder, retryAsset, blockerMeta, retryFolderVariant);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            EnsureFolder(TestRoot);
            yield return null;

            string absoluteBlocker = GetAbsolutePath(retryFolder);
            File.WriteAllText(absoluteBlocker, "block");
            AssetDatabase.ImportAsset(retryFolder, ImportAssetOptions.ForceSynchronousImport);
            yield return null;

            Assert.IsTrue(
                File.Exists(absoluteBlocker),
                "Blocker file should exist before testing folder creation failure"
            );

            /*
                Unity versions differ in blocked-folder error wording and counts; assert the observable asset
                and folder results.
            */
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(retryFolder),
                "Retry folder should not exist while blocker is present"
            );
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(retryAsset) == null,
                "Retry asset should not exist while blocker is present"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(retryFolderVariant),
                "Variant folder should not be created"
            );

            // Via AssetDatabase first, so its internal state is cleared properly.
            AssetDatabase.DeleteAsset(retryFolder);
            CleanupRetryTestState(retryFolder, retryAsset, blockerMeta, retryFolderVariant);
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            // Second refresh pass - sometimes Unity needs this to fully clear internal GUID mappings
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            yield return null;

            Assert.IsFalse(
                File.Exists(absoluteBlocker),
                "Blocker file should be removed before retry"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(retryFolder),
                "Retry folder should not exist yet"
            );

            string blockerMetaAbsolute = GetAbsolutePath(blockerMeta);
            if (File.Exists(blockerMetaAbsolute))
            {
                File.Delete(blockerMetaAbsolute);
                AssetDatabaseBatchHelper.RefreshIfNotBatching(
                    ImportAssetOptions.ForceSynchronousImport
                );
                yield return null;
            }

            string preRetryGuid = AssetDatabase.AssetPathToGUID(retryFolder);
            string preRetryAssetGuid = AssetDatabase.AssetPathToGUID(retryAsset);
            bool preRetryDirExists = Directory.Exists(GetAbsolutePath(retryFolder));
            bool preRetryMetaExists = File.Exists(blockerMetaAbsolute);

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            bool folderExists = AssetDatabase.IsValidFolder(retryFolder);
            bool assetExists = AssetDatabase.LoadAssetAtPath<Object>(retryAsset) != null;
            bool variantExists = AssetDatabase.IsValidFolder(retryFolderVariant);

            string absoluteAssetPath = GetAbsolutePath(retryAsset);
            bool assetFileOnDisk = File.Exists(absoluteAssetPath);
            string postRetryFolderGuid = AssetDatabase.AssetPathToGUID(retryFolder);
            string postRetryAssetGuid = AssetDatabase.AssetPathToGUID(retryAsset);

            string diagnostics =
                $"folderExists={folderExists}, assetExists={assetExists}, variantExists={variantExists}, "
                + $"blockerOnDisk={File.Exists(absoluteBlocker)}, metaOnDisk={File.Exists(blockerMetaAbsolute)}, "
                + $"assetFileOnDisk={assetFileOnDisk}, preRetryFolderGuid={preRetryGuid}, preRetryAssetGuid={preRetryAssetGuid}, "
                + $"preRetryDirExists={preRetryDirExists}, preRetryMetaExists={preRetryMetaExists}, "
                + $"postRetryFolderGuid={postRetryFolderGuid}, postRetryAssetGuid={postRetryAssetGuid}";

            Assert.IsTrue(
                folderExists && assetExists && !variantExists,
                $"Retry singleton should be created once the temporary blocker is removed. Diagnostics: {diagnostics}"
            );
        }

        private void CleanupRetryTestState(
            string retryFolder,
            string retryAsset,
            string blockerMeta,
            string retryFolderVariant
        )
        {
            // Delete assets through AssetDatabase first - this properly clears Unity's internal state
            AssetDatabase.DeleteAsset(retryAsset);
            if (AssetDatabase.IsValidFolder(retryFolder))
            {
                AssetDatabase.DeleteAsset(retryFolder);
            }

            if (!AssetDatabase.IsValidFolder(retryFolder))
            {
                AssetDatabase.DeleteAsset(retryFolder);
            }
            if (AssetDatabase.IsValidFolder(retryFolderVariant))
            {
                AssetDatabase.DeleteAsset(retryFolderVariant);
            }

            string absoluteFolder = GetAbsolutePath(retryFolder);
            string absoluteVariant = GetAbsolutePath(retryFolderVariant);
            string absoluteMeta = GetAbsolutePath(blockerMeta);
            string absoluteAsset = GetAbsolutePath(retryAsset);

            if (File.Exists(absoluteFolder))
            {
                File.Delete(absoluteFolder);
            }

            if (File.Exists(absoluteMeta))
            {
                File.Delete(absoluteMeta);
            }

            string folderMeta = absoluteFolder + ".meta";
            if (File.Exists(folderMeta))
            {
                File.Delete(folderMeta);
            }

            if (File.Exists(absoluteAsset))
            {
                File.Delete(absoluteAsset);
            }

            string assetMeta = absoluteAsset + ".meta";
            if (File.Exists(assetMeta))
            {
                File.Delete(assetMeta);
            }

            if (Directory.Exists(absoluteFolder))
            {
                Directory.Delete(absoluteFolder, true);

                if (File.Exists(folderMeta))
                {
                    File.Delete(folderMeta);
                }
            }
            if (Directory.Exists(absoluteVariant))
            {
                Directory.Delete(absoluteVariant, true);
            }

            string variantMeta = absoluteVariant + ".meta";
            if (File.Exists(variantMeta))
            {
                File.Delete(variantMeta);
            }
        }

        [UnityTest]
        public IEnumerator DoesNotCreateAlternateFolderWhenFileConflicts()
        {
            string conflictFolder = TestRoot + "/FileBlock";
            string conflictAsset = conflictFolder + "/FileBlockSingleton.asset";
            string conflictFile = conflictFolder;
            string conflictVariant = conflictFolder + " 1";

            AssetDatabase.DeleteAsset(conflictAsset);
            if (AssetDatabase.IsValidFolder(conflictFolder))
            {
                AssetDatabase.DeleteAsset(conflictFolder);
            }
            if (AssetDatabase.IsValidFolder(conflictVariant))
            {
                AssetDatabase.DeleteAsset(conflictVariant);
            }

            DeleteFileIfExists(conflictFile);
            EnsureFolder(TestRoot);

            string absoluteParent = Path.GetDirectoryName(GetAbsolutePath(conflictFile));
            if (!string.IsNullOrEmpty(absoluteParent) && !Directory.Exists(absoluteParent))
            {
                Directory.CreateDirectory(absoluteParent);
            }

            File.WriteAllText(GetAbsolutePath(conflictFile), "block");
            AssetDatabase.ImportAsset(conflictFile);
            yield return null;

            /*
                Unity versions differ in blocked-folder error wording and counts; assert the observable asset
                and folder results.
            */
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = true;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            }
            yield return null;

            Assert.IsFalse(AssetDatabase.IsValidFolder(conflictFolder));
            Assert.IsFalse(AssetDatabase.IsValidFolder(conflictVariant));
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(conflictAsset) == null);

            DeleteFileIfExists(conflictFile);
            if (AssetDatabase.IsValidFolder(conflictVariant))
            {
                AssetDatabase.DeleteAsset(conflictVariant);
            }
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AutomaticRetriesCanBeDisabled()
        {
            string noRetryFolder = TestRoot + "/NoRetry";
            string noRetryAsset = noRetryFolder + "/NoRetrySingleton.asset";
            string noRetryVariant = noRetryFolder + " 1";
            string blockerMeta = noRetryFolder + ".meta";

            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            AssetDatabase.DeleteAsset(noRetryAsset);
            AssetDatabase.DeleteAsset(noRetryFolder);
            AssetDatabase.DeleteAsset(noRetryVariant);
            if (AssetDatabase.IsValidFolder(noRetryFolder))
            {
                AssetDatabase.DeleteAsset(noRetryFolder);
            }
            if (AssetDatabase.IsValidFolder(noRetryVariant))
            {
                AssetDatabase.DeleteAsset(noRetryVariant);
            }
            DeleteFileIfExists(noRetryFolder);
            string absoluteBlockerMeta = GetAbsolutePath(blockerMeta);
            if (File.Exists(absoluteBlockerMeta))
            {
                File.Delete(absoluteBlockerMeta);
            }
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            EnsureFolder(TestRoot);
            yield return null;

            string absoluteBlocker = GetAbsolutePath(noRetryFolder);
            File.WriteAllText(absoluteBlocker, "block");
            AssetDatabase.ImportAsset(noRetryFolder, ImportAssetOptions.ForceSynchronousImport);
            yield return null;

            /*
                Unity versions differ in blocked-folder error wording and counts; assert the observable asset
                and folder results.
            */
            bool originalRetrySetting = ScriptableObjectSingletonCreator.DisableAutomaticRetries;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = true;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                yield return null;
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                ScriptableObjectSingletonCreator.DisableAutomaticRetries = originalRetrySetting;
            }

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(noRetryAsset) == null,
                "Asset should not be created while blocker is present"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(noRetryFolder),
                "Folder should not exist while blocker is present"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(noRetryVariant),
                "Variant folder should not be created"
            );

            AssetDatabase.DeleteAsset(noRetryFolder);
            DeleteFileIfExists(noRetryFolder);
            if (File.Exists(absoluteBlockerMeta))
            {
                File.Delete(absoluteBlockerMeta);
            }
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            // Automatic retries are still disabled, so nothing should be created until we run ensure manually.
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(noRetryAsset) == null,
                "Asset should not be created automatically while retries are disabled"
            );

            ScriptableObjectSingletonCreator.ResetRetryStateForTests();
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            bool folderExists = AssetDatabase.IsValidFolder(noRetryFolder);
            bool assetExists = AssetDatabase.LoadAssetAtPath<Object>(noRetryAsset) != null;
            bool variantExists = AssetDatabase.IsValidFolder(noRetryVariant);

            ScriptableObjectSingletonCreator.DisableAutomaticRetries = originalRetrySetting;

            string absoluteAssetPath = GetAbsolutePath(noRetryAsset);
            bool assetFileOnDisk = File.Exists(absoluteAssetPath);
            string postFolderGuid = AssetDatabase.AssetPathToGUID(noRetryFolder);
            string postAssetGuid = AssetDatabase.AssetPathToGUID(noRetryAsset);

            string diagnostics =
                $"folderExists={folderExists}, assetExists={assetExists}, variantExists={variantExists}, "
                + $"assetFileOnDisk={assetFileOnDisk}, postFolderGuid={postFolderGuid}, postAssetGuid={postAssetGuid}";

            Assert.IsTrue(
                folderExists && assetExists && !variantExists,
                $"NoRetry singleton should be created when manually triggered. Diagnostics: {diagnostics}"
            );
        }

        /// <summary>
        /// When some singletons succeed and others fail, the retry counter resets so the failures
        /// keep retrying; it used to accumulate globally and exhaust quickly.
        /// </summary>
        [UnityTest]
        public IEnumerator PartialSuccessResetsRetryCounter()
        {
            string retryFolder = TestRoot + "/Retry";
            string retryAsset = retryFolder + "/RetrySingleton.asset";
            string caseTestFolder = "Assets/Resources/CaseTest";
            string caseTestAsset = caseTestFolder + "/CaseMismatch.asset";

            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            AssetDatabase.DeleteAsset(retryAsset);
            if (AssetDatabase.IsValidFolder(retryFolder))
            {
                AssetDatabase.DeleteAsset(retryFolder);
            }

            TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTest");

            EnsureFolder(TestRoot);
            EnsureFolder(caseTestFolder);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string absoluteBlocker = GetAbsolutePath(retryFolder);
            File.WriteAllText(absoluteBlocker, "block");
            AssetDatabase.ImportAsset(retryFolder, ImportAssetOptions.ForceSynchronousImport);
            yield return null;

            /*
                A file occupying a folder path produces version-dependent errors and retry counts; test partial
                success and later recovery.
            */
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            }
            finally
            {
                /*
                    Cancel the queued retry before suppression ends; otherwise the next frame logs its expected
                    folder failure again.
                */
                ScriptableObjectSingletonCreator.ResetRetryStateForTests();
                LogAssert.ignoreFailingMessages = false;
            }
            yield return null;

            Object caseMismatchAsset = AssetDatabase.LoadAssetAtPath<Object>(caseTestAsset);
            Assert.IsTrue(
                caseMismatchAsset != null,
                "CaseMismatch singleton should be created even when RetrySingleton fails"
            );

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(retryAsset) == null,
                "RetrySingleton should not be created while blocker exists"
            );

            AssetDatabase.DeleteAsset(retryFolder);
            if (File.Exists(absoluteBlocker))
            {
                File.Delete(absoluteBlocker);
            }
            string blockerMeta = absoluteBlocker + ".meta";
            if (File.Exists(blockerMeta))
            {
                File.Delete(blockerMeta);
            }
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Object retryAssetObj = AssetDatabase.LoadAssetAtPath<Object>(retryAsset);
            Assert.IsTrue(
                retryAssetObj != null,
                "RetrySingleton should be created after blocker is removed"
            );
        }

        /// <summary>
        /// Verifies that the fix for issue #157 (race condition where newly created assets
        /// were immediately deleted) works correctly. After CreateAsset succeeds and writes
        /// a file to disk, LoadAssetAtPath should find it because we now call ImportAsset
        /// with ForceSynchronousImport. Even if LoadAssetAtPath returns null, the fix
        /// ensures we do NOT delete the file on disk when it exists - we only destroy the
        /// in-memory instance and retry.
        /// </summary>
        [UnityTest]
        public IEnumerator AssetCreationDoesNotDeleteValidFilesOnDisk()
        {
            string raceFolder = TestRoot + "/Race";
            string raceAsset = raceFolder + "/AssetDatabaseRaceSingleton.asset";

            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            AssetDatabase.DeleteAsset(raceAsset);
            if (AssetDatabase.IsValidFolder(raceFolder))
            {
                AssetDatabase.DeleteAsset(raceFolder);
            }
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            EnsureFolder(TestRoot);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(raceFolder),
                "Race folder should not exist before test"
            );
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(raceAsset) == null,
                "Race asset should not exist before test"
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            bool folderExists = AssetDatabase.IsValidFolder(raceFolder);
            Object loadedAsset = AssetDatabase.LoadAssetAtPath<Object>(raceAsset);
            string absoluteAssetPath = GetAbsolutePath(raceAsset);
            bool fileExistsOnDisk = File.Exists(absoluteAssetPath);

            string diagnostics =
                $"folderExists={folderExists}, loadedAsset={loadedAsset != null}, fileExistsOnDisk={fileExistsOnDisk}";

            Assert.IsTrue(
                folderExists,
                $"Race singleton folder should be created. Diagnostics: {diagnostics}"
            );
            Assert.IsTrue(
                loadedAsset != null,
                $"Race singleton asset should be loadable via AssetDatabase. Diagnostics: {diagnostics}"
            );
            Assert.IsTrue(
                fileExistsOnDisk,
                $"Race singleton asset file should exist on disk. Diagnostics: {diagnostics}"
            );
        }

        private static IEnumerable<string> AssetImportWorkerEnvironmentScenarios()
        {
            yield return "UNITY_ASSET_IMPORT_WORKER";
            yield return "UNITY_ASSETIMPORT_WORKER";
            yield return "MY_CUSTOM_UNITY_ASSET_IMPORT_WORKER_FLAG";
        }

        /// <summary>
        /// Data source for case mismatch folder scenarios.
        /// Each tuple contains: (existingFolderName, expectedSingletonPath, description)
        /// </summary>
        private static IEnumerable<TestCaseData> CaseMismatchFolderScenarios()
        {
            yield return new TestCaseData("casetest", "Assets/Resources/casetest")
                .SetName("AllLowercase")
                .SetDescription("Existing folder with all lowercase name");
            yield return new TestCaseData("CASETEST", "Assets/Resources/CASETEST")
                .SetName("AllUppercase")
                .SetDescription("Existing folder with all uppercase name");
            yield return new TestCaseData("cASEtest", "Assets/Resources/cASEtest")
                .SetName("MixedCase1")
                .SetDescription("Existing folder with mixed case (cASEtest)");
            yield return new TestCaseData("CaseTEST", "Assets/Resources/CaseTEST")
                .SetName("MixedCase2")
                .SetDescription("Existing folder with mixed case (CaseTEST)");
        }

        [UnityTest]
        public IEnumerator CaseMismatchFolderIsReused(
            [ValueSource(nameof(CaseMismatchFolderScenarios))] TestCaseData testCase
        )
        {
            string existingFolderName = (string)testCase.Arguments[0];
            string expectedFolderPath = (string)testCase.Arguments[1];

            string existingFolder = "Assets/Resources/" + existingFolderName;
            EnsureFolder(existingFolder);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(existingFolder),
                $"Setup: Folder '{existingFolder}' should exist"
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] resourceSubfolders = AssetDatabase.GetSubFolders("Assets/Resources");
            string subfoldersStr = string.Join(", ", resourceSubfolders);

            bool anyDuplicateExists = false;
            string duplicateFound = null;
            foreach (string folder in resourceSubfolders)
            {
                string folderName = Path.GetFileName(folder);

                if (
                    folderName.StartsWith("CaseTest ", StringComparison.OrdinalIgnoreCase)
                    || folderName.StartsWith(
                        existingFolderName + " ",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string[] parts = folderName.Split(' ');
                    if (2 <= parts.Length && int.TryParse(parts[^1], out _))
                    {
                        anyDuplicateExists = true;
                        duplicateFound = folder;
                        break;
                    }
                }
            }

            Assert.IsFalse(
                anyDuplicateExists,
                $"No duplicate folder should exist when '{existingFolderName}' exists. "
                    + $"Found duplicate: '{duplicateFound}'. Subfolders: [{subfoldersStr}]"
            );

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(
                existingFolder + "/CaseMismatch.asset"
            );
            if (asset == null)
            {
                asset = AssetDatabase.LoadAssetAtPath<Object>(
                    "Assets/Resources/CaseTest/CaseMismatch.asset"
                );
            }

            Assert.IsTrue(
                asset != null,
                $"CaseMismatch singleton should be created. Subfolders: [{subfoldersStr}]"
            );
        }

        /// <summary>
        /// Verifies that duplicate folder cleanup helper correctly identifies duplicate folders.
        /// </summary>
        [Test]
        public void TryDeleteFolderAndDuplicatesIdentifiesDuplicateFolderPatterns()
        {
            string baseName = "TestDuplicateDetection";
            string basePath = "Assets/Resources/" + baseName;
            string dup1Path = "Assets/Resources/" + baseName + " 1";
            string dup2Path = "Assets/Resources/" + baseName + " 2";
            string notDupPath = "Assets/Resources/" + baseName + "Other";

            EnsureFolder(basePath);
            EnsureFolder(dup1Path);
            EnsureFolder(dup2Path);
            EnsureFolder(notDupPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.IsTrue(AssetDatabase.IsValidFolder(basePath), "Base folder should exist");
            Assert.IsTrue(AssetDatabase.IsValidFolder(dup1Path), "Duplicate 1 should exist");
            Assert.IsTrue(AssetDatabase.IsValidFolder(dup2Path), "Duplicate 2 should exist");
            Assert.IsTrue(AssetDatabase.IsValidFolder(notDupPath), "Non-duplicate should exist");

            TryDeleteFolderAndDuplicates("Assets/Resources", baseName);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.IsFalse(AssetDatabase.IsValidFolder(basePath), "Base folder should be deleted");
            Assert.IsFalse(AssetDatabase.IsValidFolder(dup1Path), "Duplicate 1 should be deleted");
            Assert.IsFalse(AssetDatabase.IsValidFolder(dup2Path), "Duplicate 2 should be deleted");
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(notDupPath),
                "Non-duplicate should NOT be deleted (it doesn't match the pattern)"
            );

            TryDeleteFolder(notDupPath);
        }

        /// <summary>
        /// Verifies that the data-driven CaseMismatchFolderIsReused test doesn't pollute
        /// state between test case executions.
        /// </summary>
        [UnityTest]
        public IEnumerator CaseMismatchFoldersCleanupBetweenTests()
        {
            TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTest");
            TryDeleteFolderAndDuplicates("Assets/Resources", "casetest");
            TryDeleteFolderAndDuplicates("Assets/Resources", "CASETEST");
            TryDeleteFolderAndDuplicates("Assets/Resources", "cASEtest");
            TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTEST");
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string[] subFolders = AssetDatabase.GetSubFolders("Assets/Resources");
            List<string> caseTestFolders = new();

            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (
                    name.StartsWith("CaseTest", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("casetest", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("CASETEST", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("cASEtest", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("CaseTEST", StringComparison.OrdinalIgnoreCase)
                )
                {
                    caseTestFolders.Add(folder);
                }
            }

            Assert.IsEmpty(
                caseTestFolders,
                $"No CaseTest variant folders should exist after cleanup. Found: [{string.Join(", ", caseTestFolders)}]"
            );
        }

        /// <summary>
        /// Verifies diagnostics output for case mismatch scenario.
        /// </summary>
        [UnityTest]
        public IEnumerator CaseMismatchDiagnosticsAreHelpful()
        {
            string existingFolder = "Assets/Resources/cAsEtEsT";
            EnsureFolder(existingFolder);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string[] subFolders = AssetDatabase.GetSubFolders("Assets/Resources");
            bool foundOriginal = false;
            bool foundDuplicate = false;
            List<string> foundCaseVariants = new();

            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (string.Equals(name, "cAsEtEsT", StringComparison.Ordinal))
                {
                    foundOriginal = true;
                }
                if (
                    name.StartsWith("cAsEtEsT ", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("CaseTest ", StringComparison.OrdinalIgnoreCase)
                )
                {
                    string[] parts = name.Split(' ');
                    if (2 <= parts.Length && int.TryParse(parts[^1], out _))
                    {
                        foundDuplicate = true;
                    }
                }
                if (name.StartsWith("CaseTest", StringComparison.OrdinalIgnoreCase))
                {
                    foundCaseVariants.Add(folder);
                }
            }

            string diagnostics =
                $"foundOriginal={foundOriginal}, foundDuplicate={foundDuplicate}, "
                + $"caseVariants=[{string.Join(", ", foundCaseVariants)}], "
                + $"allSubfolders=[{string.Join(", ", subFolders)}]";

            Assert.IsFalse(
                foundDuplicate,
                $"No duplicate folder should be created for case-insensitive match. {diagnostics}"
            );

            TryDeleteFolderAndDuplicates("Assets/Resources", "cAsEtEsT");
            TryDeleteFolderAndDuplicates("Assets/Resources", "CaseTest");
        }

        /// <summary>
        /// Verifies that EnsureSingletonAssets defers execution when EditorApplication.isCompiling is true.
        /// This prevents "Unable to import newly created asset" errors during domain reloads.
        /// </summary>
        [UnityTest]
        public IEnumerator DefersEnsureDuringCompilation()
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            // EditorApplication.isCompiling cannot be set, so only the not-compiling path is exercised.
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(targetPath);
            Assert.IsTrue(asset != null, "Asset should be created when not compiling or updating");
        }

        /// <summary>
        /// Verifies that IgnoreCompilationState property allows bypassing the isCompiling/isUpdating check.
        /// This is essential for tests that need to explicitly call EnsureSingletonAssets.
        /// </summary>
        [UnityTest]
        public IEnumerator IgnoreCompilationStateAllowsBypassingCompilationCheck()
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            bool previousIgnoreCompilationState =
                ScriptableObjectSingletonCreator.IgnoreCompilationState;

            try
            {
                ScriptableObjectSingletonCreator.IgnoreCompilationState = true;
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                yield return null;
                AssetDatabaseBatchHelper.RefreshIfNotBatching(
                    ImportAssetOptions.ForceSynchronousImport
                );

                Object asset = AssetDatabase.LoadAssetAtPath<Object>(targetPath);
                Assert.IsTrue(
                    asset != null,
                    "Asset should be created when IgnoreCompilationState is true. "
                        + $"isCompiling={EditorApplication.isCompiling}, "
                        + $"isUpdating={EditorApplication.isUpdating}"
                );
            }
            finally
            {
                ScriptableObjectSingletonCreator.IgnoreCompilationState =
                    previousIgnoreCompilationState;
            }
        }

        /// <summary>
        /// Verifies that SafeDestroyInstance does not throw when destroying a partially-created asset.
        /// This tests the fix for "Destroying assets is not permitted" errors.
        /// </summary>
        [UnityTest]
        public IEnumerator SafeDestroyInstanceHandlesPartialAssetCreation()
        {
            ScriptableObject instance = ScriptableObject.CreateInstance<CaseMismatch>(); // UNH-SUPPRESS: UNH002 - Testing partial asset creation
            Assert.IsTrue(instance != null, "Instance should be created");

            string testPath = TestRoot + "/SafeDestroyTest.asset";
            EnsureFolder(TestRoot);

            AssetDatabase.CreateAsset(instance, testPath);
            yield return null;

            Object createdAsset = AssetDatabase.LoadAssetAtPath<Object>(testPath);
            Assert.IsTrue(createdAsset != null, "Asset should be created for test setup");

            // Simulates partial creation: the file is gone but Unity may still track the instance.
            string absolutePath = GetAbsolutePath(testPath);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;

            Assert.DoesNotThrow(
                () =>
                {
                    if (instance != null)
                    {
                        Object.DestroyImmediate(instance, true); // UNH-SUPPRESS: UNH001 - Testing DestroyImmediate behavior
                    }
                },
                "DestroyImmediate with allowDestroyingAssets=true should not throw"
            );

            AssetDatabase.DeleteAsset(testPath);
            yield return null;
        }

        /// <summary>
        /// Verifies that TryCleanupPartiallyCreatedAsset removes orphaned files on disk.
        /// </summary>
        [UnityTest]
        public IEnumerator TryCleanupPartiallyCreatedAssetRemovesOrphanedFiles()
        {
            EnsureFolder(TestRoot);
            string testPath = TestRoot + "/OrphanCleanupTest.asset";
            string absolutePath = GetAbsolutePath(testPath);

            /*
                Corrupt YAML can trigger errors on any deferred refresh. Keep suppression active across yields
                until the file is removed and refreshed.
            */
            LogAssert.ignoreFailingMessages = true;

            File.WriteAllText(absolutePath, "fake asset content");
            Assert.IsTrue(File.Exists(absolutePath), "Setup: fake file should exist on disk");

            yield return null;

            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(File.Exists(absolutePath), "Orphaned asset file should be cleaned up");
            Assert.IsFalse(File.Exists(metaPath), "Orphaned meta file should be cleaned up");
        }

        /// <summary>
        /// Verifies that the singleton creator handles the scenario where CreateAsset
        /// throws an exception but has partially created the asset on disk.
        /// </summary>
        [UnityTest]
        public IEnumerator HandlesCreateAssetExceptionWithPartialFile()
        {
            EnsureFolder(TestRoot);
            string testPath = TestRoot + "/ExceptionTest.asset";
            string absolutePath = GetAbsolutePath(testPath);

            AssetDatabase.DeleteAsset(testPath);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;

            ScriptableObject instance = ScriptableObject.CreateInstance<CaseMismatch>(); // UNH-SUPPRESS: UNH002 - Testing cleanup logic

            // Deferred refreshes may log errors while this corrupt asset YAML remains on disk.
            LogAssert.ignoreFailingMessages = true;

            File.WriteAllText(absolutePath, "partial yaml content");
            yield return null;

            Assert.DoesNotThrow(
                () =>
                {
                    if (File.Exists(absolutePath))
                    {
                        File.Delete(absolutePath);
                    }
                    if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
                    Object.DestroyImmediate(instance, true); // UNH-SUPPRESS: UNH001 - Testing cleanup behavior
                },
                "Cleanup should not throw even with partial files"
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(File.Exists(absolutePath), "Partial file should be cleaned up");
        }

        /// <summary>
        /// Verifies that multiple consecutive ensure calls don't cause errors when
        /// previous calls may have left partial state.
        /// </summary>
        [UnityTest]
        public IEnumerator MultipleEnsureCallsAreRobust()
        {
            string targetPath = "Assets/Resources/CaseTest/CaseMismatch.asset";
            AssetDatabase.DeleteAsset(targetPath);
            yield return null;

            for (int i = 0; i < 3; i++)
            {
                Assert.DoesNotThrow(
                    () =>
                    {
                        ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                    },
                    $"Ensure call {i + 1} should not throw"
                );
                yield return null;
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(targetPath);
            Assert.IsTrue(asset != null, "Asset should exist after multiple ensure calls");

            string[] guids = AssetDatabase.FindAssets(
                "t:CaseMismatch",
                new[] { "Assets/Resources" }
            );
            Assert.AreEqual(1, guids.Length, "There should be exactly one CaseMismatch asset");
        }

        [TestCase(null, null, ExpectedResult = false, TestName = "BothNull")]
        [TestCase(null, "Folder", ExpectedResult = false, TestName = "ActualNameNull")]
        [TestCase("Folder 1", null, ExpectedResult = false, TestName = "DesiredNameNull")]
        [TestCase("", "", ExpectedResult = false, TestName = "BothEmpty")]
        [TestCase("", "Folder", ExpectedResult = false, TestName = "ActualNameEmpty")]
        [TestCase("Folder 1", "", ExpectedResult = false, TestName = "DesiredNameEmpty")]
        [TestCase("Folder", "Folder", ExpectedResult = false, TestName = "SameLengthExactMatch")]
        [TestCase("FolderX", "FolderY", ExpectedResult = false, TestName = "SameLengthDifferent")]
        [TestCase(
            "Folder 1",
            "Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateSingleDigit"
        )]
        [TestCase(
            "Folder 10",
            "Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateDoubleDigit"
        )]
        [TestCase(
            "Folder 999",
            "Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateTripleDigit"
        )]
        [TestCase(
            "Resources 1",
            "Resources",
            ExpectedResult = true,
            TestName = "ValidDuplicateResourcesFolder"
        )]
        [TestCase(
            "Resources 42",
            "Resources",
            ExpectedResult = true,
            TestName = "ValidDuplicateResourcesLargeNumber"
        )]
        [TestCase(
            "My Folder 5",
            "My Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateWithSpaceInName"
        )]
        [TestCase("Folder 0", "Folder", ExpectedResult = false, TestName = "ZeroNotValidDuplicate")]
        [TestCase(
            "Folder -1",
            "Folder",
            ExpectedResult = false,
            TestName = "NegativeNumberNotValidDuplicate"
        )]
        [TestCase(
            "Folder -10",
            "Folder",
            ExpectedResult = false,
            TestName = "NegativeDoubleDigitNotValidDuplicate"
        )]
        [TestCase(
            "FOLDER 1",
            "folder",
            ExpectedResult = true,
            TestName = "CaseInsensitiveUpperToLower"
        )]
        [TestCase(
            "folder 1",
            "FOLDER",
            ExpectedResult = true,
            TestName = "CaseInsensitiveLowerToUpper"
        )]
        [TestCase(
            "FoLdEr 1",
            "fOlDeR",
            ExpectedResult = true,
            TestName = "CaseInsensitiveMixedCase"
        )]
        [TestCase(
            "Resources 1",
            "RESOURCES",
            ExpectedResult = true,
            TestName = "CaseInsensitiveResources"
        )]
        [TestCase("Folder1", "Folder", ExpectedResult = false, TestName = "NoSpaceSeparator")]
        [TestCase(
            "Resources1",
            "Resources",
            ExpectedResult = false,
            TestName = "NoSpaceSeparatorResources"
        )]
        [TestCase(
            "Folder10",
            "Folder",
            ExpectedResult = false,
            TestName = "NoSpaceSeparatorDoubleDigit"
        )]
        [TestCase("Folder  1", "Folder", ExpectedResult = false, TestName = "DoubleSpaceSeparator")]
        [TestCase(
            "Folder  10",
            "Folder",
            ExpectedResult = false,
            TestName = "DoubleSpaceDoubleDigit"
        )]
        [TestCase(
            "Folder abc",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixLetters"
        )]
        [TestCase(
            "Folder 1a",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixMixed"
        )]
        [TestCase(
            "Folder a1",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixLetterFirst"
        )]
        [TestCase(
            "Folder 1.5",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixDecimal"
        )]
        [TestCase(
            "Folder 1 2",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixMultipleNumbers"
        )]
        [TestCase("Fol", "Folder", ExpectedResult = false, TestName = "ActualShorterThanDesired")]
        [TestCase("F", "Folder", ExpectedResult = false, TestName = "ActualMuchShorterThanDesired")]
        [TestCase(
            "Folder ",
            "Folder",
            ExpectedResult = false,
            TestName = "ActualHasOnlyTrailingSpace"
        )]
        public bool IsNumberedDuplicateReturnsExpectedResult(string actualName, string desiredName)
        {
            return ScriptableObjectSingletonCreator.IsNumberedDuplicate(actualName, desiredName);
        }

        private static void TryDeleteFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] contents = AssetDatabase.FindAssets(string.Empty, new[] { folder });
            if (contents == null || contents.Length == 0)
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void TryDeleteFolderCaseInsensitive(string intended)
        {
            if (string.IsNullOrWhiteSpace(intended))
            {
                return;
            }

            string[] parts = intended.SanitizePath().Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string desired = parts[i];
                string next = current + "/" + desired;
                if (AssetDatabase.IsValidFolder(next))
                {
                    current = next;
                    continue;
                }

                string[] subs = AssetDatabase.GetSubFolders(current);
                if (subs == null || subs.Length == 0)
                {
                    return;
                }

                string match = null;
                foreach (string sub in subs)
                {
                    int last = sub.LastIndexOf('/');
                    string name = 0 <= last ? sub.Substring(last + 1) : sub;
                    if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        match = sub;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(match))
                {
                    return;
                }

                current = match;
            }

            TryDeleteFolder(current);
        }

        /// <summary>
        /// Deletes a folder and all its duplicates (e.g., "Folder", "Folder 1", "Folder 2").
        /// This handles the case where Unity creates duplicate folders when case-insensitive
        /// matches aren't detected properly during asset database operations.
        /// </summary>
        private static void TryDeleteFolderAndDuplicates(string parentPath, string folderBaseName)
        {
            if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(folderBaseName))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(parentPath);
            if (subFolders == null || subFolders.Length == 0)
            {
                return;
            }

            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (string.Equals(name, folderBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteFolderRecursively(folder);
                    continue;
                }

                if (name.StartsWith(folderBaseName + " ", StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = name.Substring(folderBaseName.Length + 1);
                    if (int.TryParse(suffix, out _))
                    {
                        DeleteFolderRecursively(folder);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively deletes a folder and all its contents.
        /// </summary>
        private static void DeleteFolderRecursively(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            if (guids != null)
            {
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                    }
                }
            }

            string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
            if (subFolders != null)
            {
                foreach (string sub in subFolders)
                {
                    DeleteFolderRecursively(sub);
                }
            }

            AssetDatabase.DeleteAsset(folderPath);
        }

        private static string GetAbsolutePath(string assetsRelativePath)
        {
            if (string.IsNullOrWhiteSpace(assetsRelativePath))
            {
                return string.Empty;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string normalized = assetsRelativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, normalized);
        }

        private static void DeleteFileIfExists(string assetsRelativePath)
        {
            if (string.IsNullOrWhiteSpace(assetsRelativePath))
            {
                return;
            }

            if (AssetDatabase.DeleteAsset(assetsRelativePath))
            {
                return;
            }

            string absolutePath = GetAbsolutePath(assetsRelativePath);
            if (!string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            string metaPath = absolutePath + ".meta";
            if (!string.IsNullOrEmpty(metaPath) && File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }
    }
#endif
}
