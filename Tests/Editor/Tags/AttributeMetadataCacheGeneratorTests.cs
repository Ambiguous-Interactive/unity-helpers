// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Tags;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Attribute = System.Attribute;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AttributeMetadataCacheGeneratorTests : CommonTestBase
    {
        private const string CacheAssetPath =
            "Assets/Resources/Wallstop Studios/Unity Helpers/AttributeMetadataCache.asset";
        private const string CacheFolder = "Assets/Resources/Wallstop Studios/Unity Helpers";

        private bool _assetExistedBefore;
        private string _backupPath;
        private bool _previousAllowAssetCreationDuringSuppression;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ScriptableObjectSingletonMetadataUtility.ResetAssetEditingDepthForTesting();
            ScriptableObjectSingleton<AttributeMetadataCache>.ClearInstance();

            _previousAllowAssetCreationDuringSuppression =
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            _assetExistedBefore =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null;

            if (_assetExistedBefore)
            {
                _backupPath = "Assets/Temp/AttributeMetadataCache_Backup.asset";
                if (!AssetDatabase.IsValidFolder("Assets/Temp"))
                {
                    AssetDatabase.CreateFolder("Assets", "Temp");
                }

                bool copySuccess = TryCopyAssetSilent(CacheAssetPath, _backupPath);
                if (!copySuccess)
                {
                    Debug.LogWarning(
                        $"[{nameof(AttributeMetadataCacheGeneratorTests)}] Failed to backup {CacheAssetPath} to {_backupPath}. Test may not properly restore state."
                    );
                }
            }

            yield return null;
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            yield return base.UnityTearDown();

            if (_assetExistedBefore && !string.IsNullOrEmpty(_backupPath))
            {
                if (AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(_backupPath) != null)
                {
                    AssetDatabase.DeleteAsset(CacheAssetPath);
                    string moveError = AssetDatabase.MoveAsset(_backupPath, CacheAssetPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        Debug.LogWarning(
                            $"[{nameof(AttributeMetadataCacheGeneratorTests)}] Failed to restore backup: {moveError}"
                        );
                    }
                }
            }

            AssetDatabase.SaveAssets();
            ImportAssetIfExists(CacheAssetPath);
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression =
                _previousAllowAssetCreationDuringSuppression;
            ScriptableObjectSingleton<AttributeMetadataCache>.ClearInstance();
            DeleteFolderIfEmpty("Assets/Temp");
            yield return null;
        }

        private static void ImportAssetIfExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absolutePath = Path.Combine(projectRoot, assetPath);
                if (File.Exists(absolutePath))
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
            }
        }

        [UnityTest]
        public IEnumerator GenerateCacheCreatesAssetWhenMissing()
        {
            bool folderExistedBefore = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"Initial state: AssetExistedBefore={_assetExistedBefore}, "
                    + $"FolderExistedBefore={folderExistedBefore}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            if (_assetExistedBefore)
            {
                AssetDatabase.DeleteAsset(CacheAssetPath);
                yield return null;
            }

            bool cacheExistsAfterDelete =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null;
            bool folderExistsAfterDelete = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"After delete: CacheExists={cacheExistsAfterDelete}, FolderExists={folderExistsAfterDelete}"
            );

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) == null,
                $"Setup: Cache asset should not exist before test. "
                    + $"Path: '{CacheAssetPath}', FolderExists: {AssetDatabase.IsValidFolder(CacheFolder)}"
            );

            AttributeMetadataCacheGenerator.GenerateCache();
            yield return null;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportAssetIfExists(CacheAssetPath);
            yield return null;

            bool fileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);
            bool folderExists = AssetDatabase.IsValidFolder(CacheFolder);
            AttributeMetadataCache cache = AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(
                CacheAssetPath
            );
            TestContext.WriteLine(
                $"After GenerateCache: CacheExists={cache != null}, FolderExists={folderExists}, "
                    + $"FileExistsOnDisk={fileExistsOnDisk}"
            );

            Assert.IsTrue(
                cache != null,
                $"GenerateCache should create the cache asset when missing. "
                    + $"Path: '{CacheAssetPath}', FolderExists={folderExists}, "
                    + $"FileExistsOnDisk={fileExistsOnDisk}, EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );
        }

        [UnityTest]
        public IEnumerator GenerateCacheCreatesFolderHierarchyWhenMissing()
        {
            bool folderExistedBefore = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"Initial state: AssetExistedBefore={_assetExistedBefore}, "
                    + $"FolderExistedBefore={folderExistedBefore}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            if (_assetExistedBefore)
            {
                AssetDatabase.DeleteAsset(CacheAssetPath);
                yield return null;
            }

            DeleteFolderIfEmpty(CacheFolder);
            yield return null;

            bool folderExistsAfterDelete = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"After delete and folder cleanup: FolderExists={folderExistsAfterDelete}"
            );

            AssetDatabase.SaveAssets();
            yield return null;

            AttributeMetadataCacheGenerator.GenerateCache();
            yield return null;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportAssetIfExists(CacheAssetPath);
            yield return null;

            bool fileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);
            bool folderExistsAfterGenerate = AssetDatabase.IsValidFolder(CacheFolder);
            AttributeMetadataCache cache = AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(
                CacheAssetPath
            );
            TestContext.WriteLine(
                $"After GenerateCache: CacheExists={cache != null}, FolderExists={folderExistsAfterGenerate}, "
                    + $"FileExistsOnDisk={fileExistsOnDisk}"
            );

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(CacheFolder),
                $"GenerateCache should create the folder hierarchy. "
                    + $"FileExistsOnDisk={fileExistsOnDisk}, EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            Assert.IsTrue(
                cache != null,
                $"GenerateCache should create the cache asset. "
                    + $"Path: '{CacheAssetPath}', FolderExists: {AssetDatabase.IsValidFolder(CacheFolder)}, "
                    + $"FileExistsOnDisk={fileExistsOnDisk}, EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );
        }

        [UnityTest]
        public IEnumerator GenerateCacheDoesNotThrowWhenAssetAlreadyExists()
        {
            bool folderExistedBefore = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"Initial state: AssetExistedBefore={_assetExistedBefore}, "
                    + $"FolderExistedBefore={folderExistedBefore}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            AttributeMetadataCacheGenerator.GenerateCache();
            yield return null;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportAssetIfExists(CacheAssetPath);
            yield return null;

            bool firstFileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);

            AttributeMetadataCache firstCache =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath);
            bool folderExistsAfterFirst = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"After first GenerateCache: CacheExists={firstCache != null}, FolderExists={folderExistsAfterFirst}, "
                    + $"FileExistsOnDisk={firstFileExistsOnDisk}"
            );

            Assert.IsTrue(
                firstCache != null,
                $"First GenerateCache should create the asset. "
                    + $"FolderExists={AssetDatabase.IsValidFolder(CacheFolder)}, "
                    + $"FileExistsOnDisk={firstFileExistsOnDisk}, EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            Assert.DoesNotThrow(
                () => AttributeMetadataCacheGenerator.GenerateCache(),
                "GenerateCache should not throw when cache already exists"
            );
            yield return null;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            bool secondFileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);

            AttributeMetadataCache secondCache =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath);
            bool folderExistsAfterSecond = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"After second GenerateCache: CacheExists={secondCache != null}, FolderExists={folderExistsAfterSecond}, "
                    + $"FileExistsOnDisk={secondFileExistsOnDisk}"
            );

            Assert.IsTrue(
                secondCache != null,
                $"Cache should still exist after second GenerateCache call. "
                    + $"Path: '{CacheAssetPath}', FolderExists: {AssetDatabase.IsValidFolder(CacheFolder)}, "
                    + $"FileExistsOnDisk={secondFileExistsOnDisk}"
            );
        }

        [UnityTest]
        public IEnumerator GenerateCacheIsIdempotent()
        {
            TestContext.WriteLine(
                $"Initial state: AssetExistedBefore={_assetExistedBefore}, "
                    + $"FolderExists={AssetDatabase.IsValidFolder(CacheFolder)}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            for (int i = 0; i < 3; i++)
            {
                AttributeMetadataCacheGenerator.GenerateCache();
                yield return null;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ImportAssetIfExists(CacheAssetPath);
                yield return null;

                bool fileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);
                bool folderExists = AssetDatabase.IsValidFolder(CacheFolder);
                AttributeMetadataCache cache =
                    AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath);
                TestContext.WriteLine(
                    $"Iteration {i + 1}: CacheExists={cache != null}, FolderExists={folderExists}, "
                        + $"FileExistsOnDisk={fileExistsOnDisk}"
                );

                Assert.IsTrue(
                    cache != null,
                    $"Cache should exist after iteration {i + 1}. "
                        + $"Path: '{CacheAssetPath}', FolderExists={folderExists}, "
                        + $"FileExistsOnDisk={fileExistsOnDisk}, EditorUiSuppress={EditorUi.Suppress}, "
                        + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
                );
            }
        }

        [UnityTest]
        public IEnumerator GenerateCachePreservesWallstopStudiosFolder()
        {
            bool folderExistedBefore = AssetDatabase.IsValidFolder(CacheFolder);
            bool cacheExistedBefore =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null;
            TestContext.WriteLine(
                $"Initial state: AssetExistedBefore={_assetExistedBefore}, "
                    + $"CacheExistedBefore={cacheExistedBefore}, FolderExistedBefore={folderExistedBefore}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            AttributeMetadataCacheGenerator.GenerateCache();
            yield return null;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportAssetIfExists(CacheAssetPath);
            yield return null;

            bool fileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);
            bool folderExistsAfterGenerate = AssetDatabase.IsValidFolder(CacheFolder);
            bool cacheExistsAfterGenerate =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null;
            TestContext.WriteLine(
                $"After GenerateCache: CacheExists={cacheExistsAfterGenerate}, FolderExists={folderExistsAfterGenerate}, "
                    + $"FileExistsOnDisk={fileExistsOnDisk}"
            );

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(CacheFolder),
                $"Wallstop Studios folder should be preserved after cache generation. "
                    + $"FileExistsOnDisk={fileExistsOnDisk}, EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );
        }

        [UnityTest]
        public IEnumerator GenerateCacheCreatesAtCorrectPathWhenStaleInstanceExists()
        {
            const string decoyPath = "Assets/Temp/DecoyCache.asset";

            bool folderExistedBefore = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"Initial state: AssetExistedBefore={_assetExistedBefore}, "
                    + $"FolderExistedBefore={folderExistedBefore}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }

            AttributeMetadataCache decoy =
                ScriptableObject.CreateInstance<AttributeMetadataCache>();
            Track(decoy);
            AssetDatabase.CreateAsset(decoy, decoyPath);
            TrackAssetPath(decoyPath);
            AssetDatabase.SaveAssets();
            yield return null;

            AttributeMetadataCache loadedDecoy =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(decoyPath);
            TestContext.WriteLine(
                $"Decoy created: DecoyExists={loadedDecoy != null}, DecoyPath='{decoyPath}'"
            );
            Assert.IsTrue(loadedDecoy != null, $"Setup: Decoy cache should exist at '{decoyPath}'");

            if (AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(CacheAssetPath);
                yield return null;
            }

            bool cacheExistsAfterDelete =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null;
            TestContext.WriteLine(
                $"After deleting real cache: CacheExists={cacheExistsAfterDelete}"
            );
            Assert.IsTrue(
                cacheExistsAfterDelete == false,
                $"Setup: Real cache should not exist at '{CacheAssetPath}' before calling GenerateCache"
            );

            ScriptableObjectSingleton<AttributeMetadataCache>.ClearInstance();
            yield return null;

            AttributeMetadataCacheGenerator.GenerateCache();
            yield return null;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ImportAssetIfExists(CacheAssetPath);
            yield return null;

            bool fileExistsOnDisk = FileExistsOnDisk(CacheAssetPath);
            bool folderExists = AssetDatabase.IsValidFolder(CacheFolder);
            AttributeMetadataCache cache = AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(
                CacheAssetPath
            );
            TestContext.WriteLine(
                $"After GenerateCache: CacheExists={cache != null}, FolderExists={folderExists}, "
                    + $"FileExistsOnDisk={fileExistsOnDisk}, DecoyStillExists={AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(decoyPath) != null}"
            );

            Assert.IsTrue(
                cache != null,
                $"GenerateCache should create cache at the correct path even when a stale instance exists elsewhere. "
                    + $"ExpectedPath: '{CacheAssetPath}', DecoyPath: '{decoyPath}', "
                    + $"FolderExists={folderExists}, FileExistsOnDisk={fileExistsOnDisk}, "
                    + $"EditorUiSuppress={EditorUi.Suppress}, "
                    + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
            );

            string cachePath = AssetDatabase.GetAssetPath(cache);
            Assert.AreEqual(
                CacheAssetPath,
                cachePath,
                $"Cache asset should be at the expected path, not at a stale instance path. "
                    + $"ActualPath: '{cachePath}', ExpectedPath: '{CacheAssetPath}', DecoyPath: '{decoyPath}'"
            );
        }

        [Test]
        public void CachePathsMatchScriptableSingletonPathAttribute()
        {
            Attribute[] attributes = Attribute.GetCustomAttributes(
                typeof(AttributeMetadataCache),
                typeof(ScriptableSingletonPathAttribute)
            );
            Assert.AreEqual(
                1,
                attributes.Length,
                "AttributeMetadataCache should have exactly one ScriptableSingletonPathAttribute"
            );

            ScriptableSingletonPathAttribute pathAttribute = (ScriptableSingletonPathAttribute)
                attributes[0];
            string resourcesPath = pathAttribute.resourcesPath;

            string expectedAssetPath =
                $"Assets/Resources/{resourcesPath}/{nameof(AttributeMetadataCache)}.asset";
            string expectedFolder = $"Assets/Resources/{resourcesPath}";

            Assert.AreEqual(
                expectedAssetPath,
                CacheAssetPath,
                $"CacheAssetPath should match path derived from ScriptableSingletonPathAttribute. "
                    + $"Attribute resourcesPath: '{resourcesPath}'"
            );
            Assert.AreEqual(
                expectedFolder,
                CacheFolder,
                $"CacheFolder should match folder derived from ScriptableSingletonPathAttribute. "
                    + $"Attribute resourcesPath: '{resourcesPath}'"
            );
        }

        private static IEnumerable<TestCaseData> SuppressionFlagTestCases()
        {
            yield return new TestCaseData(true, false)
                .Returns(null)
                .SetName("Suppression.Enabled.AllowFalse");
            yield return new TestCaseData(true, true)
                .Returns(null)
                .SetName("Suppression.Enabled.AllowTrue");
            yield return new TestCaseData(false, false)
                .Returns(null)
                .SetName("Suppression.Disabled.AllowFalse");
            yield return new TestCaseData(false, true)
                .Returns(null)
                .SetName("Suppression.Disabled.AllowTrue");
        }

        [UnityTest]
        [TestCaseSource(nameof(SuppressionFlagTestCases))]
        public IEnumerator GenerateCacheRespectsSuppressionFlags(
            bool suppressEditorUi,
            bool allowDuringSuppression
        )
        {
            TestContext.WriteLine(
                $"Test parameters: SuppressEditorUi={suppressEditorUi}, "
                    + $"AllowDuringSuppression={allowDuringSuppression}"
            );

            bool cacheExistedAtStart =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null;
            bool folderExistedAtStart = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"Initial state: CacheExistedAtStart={cacheExistedAtStart}, "
                    + $"FolderExistedAtStart={folderExistedAtStart}, "
                    + $"AssetExistedBefore={_assetExistedBefore}"
            );

            if (AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(CacheAssetPath);
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
                yield return null;
            }

            AttributeMetadataCache cacheCheck =
                AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath);
            bool folderExistsBeforeTest = AssetDatabase.IsValidFolder(CacheFolder);
            TestContext.WriteLine(
                $"After cleanup: CacheExists={cacheCheck != null}, FolderExists={folderExistsBeforeTest}"
            );

            Assert.IsTrue(
                cacheCheck == null,
                $"Setup: Cache asset should not exist before test. Path: '{CacheAssetPath}', "
                    + $"Exists: {cacheCheck != null}, AssetExistedBefore: {_assetExistedBefore}, "
                    + $"FolderExists: {folderExistsBeforeTest}"
            );

            bool originalSuppress = EditorUi.Suppress;
            bool originalAllow =
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression;
            TestContext.WriteLine(
                $"Original flag values: EditorUi.Suppress={originalSuppress}, "
                    + $"AllowAssetCreationDuringSuppression={originalAllow}"
            );

            try
            {
                EditorUi.Suppress = suppressEditorUi;
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression =
                    allowDuringSuppression;
                ScriptableObjectSingleton<AttributeMetadataCache>.ClearInstance();

                // Automatic UI suppression also depends on batch mode, CI, and the test runner.
                bool effectiveSuppress = EditorUi.Suppress;
                bool expectCacheCreated = !effectiveSuppress || allowDuringSuppression;

                TestContext.WriteLine(
                    $"Flags set for test: EditorUi.Suppress={EditorUi.Suppress}, "
                        + $"EffectiveSuppress={effectiveSuppress}, "
                        + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}, "
                        + $"ExpectCacheCreated={expectCacheCreated}"
                );

                AttributeMetadataCacheGenerator.GenerateCache();
                yield return null;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ImportAssetIfExists(CacheAssetPath);
                yield return null;

                bool folderExistsAfterGenerate = AssetDatabase.IsValidFolder(CacheFolder);
                AttributeMetadataCache cache =
                    AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(CacheAssetPath);
                bool cacheExists = cache != null;
                TestContext.WriteLine(
                    $"After GenerateCache: CacheExists={cacheExists}, "
                        + $"FolderExists={folderExistsAfterGenerate}, "
                        + $"ExpectedCacheCreated={expectCacheCreated}"
                );

                Assert.AreEqual(
                    expectCacheCreated,
                    cacheExists,
                    $"GenerateCache with Suppress={suppressEditorUi}, AllowDuringSuppression={allowDuringSuppression} "
                        + $"should {(expectCacheCreated ? "" : "NOT ")}create cache. "
                        + $"Path: '{CacheAssetPath}', FolderExists: {folderExistsAfterGenerate}"
                );
            }
            finally
            {
                EditorUi.Suppress = originalSuppress;
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression =
                    originalAllow;
                TestContext.WriteLine(
                    $"Flags restored: EditorUi.Suppress={EditorUi.Suppress}, "
                        + $"AllowAssetCreationDuringSuppression={ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression}"
                );
            }
        }

        private static bool FileExistsOnDisk(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return false;
            }

            string absolutePath = Path.Combine(projectRoot, assetPath);
            return File.Exists(absolutePath);
        }

        private static void DeleteFolderIfEmpty(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
            if (subFolders != null && 0 < subFolders.Length)
            {
                return;
            }

            string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            if (assets != null && 0 < assets.Length)
            {
                return;
            }

            AssetDatabase.DeleteAsset(folderPath);
        }
    }
#endif
}
