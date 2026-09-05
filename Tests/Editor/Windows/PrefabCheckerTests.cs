// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Windows
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class PrefabCheckerTests : BatchedEditorTestBase
    {
        private const string Root = "Assets/Temp/PrefabCheckerTests";

        private static readonly Regex NoAssetPathsErrorPattern = new(
            @"\[PrefabChecker\].*No asset paths specified",
            RegexOptions.Compiled
        );

        private static readonly Regex InvalidPathsErrorPattern = new(
            @"\[PrefabChecker\].*None of the specified paths are valid folders",
            RegexOptions.Compiled
        );

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            // Always reset ignoreFailingMessages to prevent test pollution
            LogAssert.ignoreFailingMessages = false;
            base.TearDown();

            CleanupTrackedFoldersAndAssets();
        }

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();
        }

        [Test]
        public void DataPathConvertsToAssets()
        {
            string dataPath = Application.dataPath;
            string rel = DirectoryHelper.AbsoluteToUnityRelativePath(dataPath);
            Assert.IsTrue(rel != null, "Relative path should not be null for valid data path");
            Assert.IsNotEmpty(rel);
            Assert.AreEqual("Assets", rel, "Root Assets conversion should be exactly 'Assets'.");
        }

        [Test]
        public void RunChecksAcceptsAssetsRoot()
        {
            // ExecuteWithImmediateImport pauses batch mode so AssetDatabase.IsValidFolder sees our folders
            ExecuteWithImmediateImport(() =>
            {
                string prefabPath = Path.Combine(Root, "Dummy.prefab").SanitizePath();
                EnsureFolder(Path.GetDirectoryName(prefabPath).SanitizePath());

                GameObject go = Track(new GameObject("DummyPrefab"));
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                TrackAssetPath(prefabPath);
                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

                // Scan only the test folder: other project prefabs (SpriteCache with missing sprites) log errors.
                List<string> list = new() { Root };
                checker._assetPaths = list;

                Assert.DoesNotThrow(() => checker.RunChecksImproved());
            });
        }

        [Test]
        public void RunChecksOnEmptyFolderCompletesWithoutError()
        {
            ExecuteWithImmediateImport(() =>
            {
                string emptySubFolder = Path.Combine(Root, "EmptyFolder").SanitizePath();
                EnsureFolder(emptySubFolder);
                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
                checker._assetPaths = new List<string> { emptySubFolder };

                Assert.DoesNotThrow(() => checker.RunChecksImproved());
            });
        }

        [Test]
        [TestCase("")]
        [TestCase("   ")]
        public void DataPathConversionHandlesInvalidInputs(string invalidPath)
        {
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(invalidPath);

            Assert.IsTrue(
                string.IsNullOrEmpty(result),
                $"Expected null or empty for invalid path '{invalidPath}', got '{result}'"
            );
        }

        [Test]
        public void RunChecksWithNullAssetPathsLogsErrorAndDoesNotThrow()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            checker._assetPaths = null;

            LogAssert.Expect(LogType.Error, NoAssetPathsErrorPattern);

            Assert.DoesNotThrow(
                () => checker.RunChecksImproved(),
                "RunChecksImproved() should not throw when asset paths are null"
            );
        }

        [Test]
        public void RunChecksWithEmptyAssetPathsListLogsErrorAndDoesNotThrow()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            checker._assetPaths = new List<string>();

            LogAssert.Expect(LogType.Error, NoAssetPathsErrorPattern);

            Assert.DoesNotThrow(
                () => checker.RunChecksImproved(),
                "RunChecksImproved() should not throw when asset paths list is empty"
            );
        }

        [Test]
        public void RunChecksOnSingleValidPrefabCompletesSuccessfully()
        {
            ExecuteWithImmediateImport(() =>
            {
                string prefabPath = Path.Combine(Root, "SingleValid.prefab").SanitizePath();
                EnsureFolder(Path.GetDirectoryName(prefabPath).SanitizePath());

                GameObject go = Track(new GameObject("SingleValidPrefab"));
                go.AddComponent<BoxCollider>();
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                TrackAssetPath(prefabPath);
                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
                checker._assetPaths = new List<string> { Root };

                Assert.DoesNotThrow(() => checker.RunChecksImproved());
            });
        }

        [Test]
        public void RunChecksOnNonExistentPathLogsErrorAndDoesNotThrow()
        {
            const string nonExistentPath = "Assets/NonExistent/Path/That/DoesNotExist";
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            checker._assetPaths = new List<string> { nonExistentPath };

            LogAssert.Expect(LogType.Error, InvalidPathsErrorPattern);

            Assert.DoesNotThrow(
                () => checker.RunChecksImproved(),
                $"RunChecksImproved() should not throw when path '{nonExistentPath}' does not exist"
            );
        }

        [Test]
        [TestCase(null, TestName = "NullPathInList")]
        [TestCase("", TestName = "EmptyStringPathInList")]
        [TestCase("   ", TestName = "WhitespacePathInList")]
        public void RunChecksWithInvalidPathEntriesLogsErrorAndDoesNotThrow(string invalidPath)
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            checker._assetPaths = new List<string> { invalidPath };

            LogAssert.Expect(LogType.Error, InvalidPathsErrorPattern);

            Assert.DoesNotThrow(
                () => checker.RunChecksImproved(),
                $"RunChecksImproved() should not throw when list contains invalid path: '{invalidPath ?? "null"}'"
            );
        }

        [Test]
        public void RunChecksWithMixedValidAndInvalidPathsProcessesValidOnes()
        {
            ExecuteWithImmediateImport(() =>
            {
                string prefabPath = Path.Combine(Root, "MixedTest.prefab").SanitizePath();
                EnsureFolder(Path.GetDirectoryName(prefabPath).SanitizePath());

                GameObject go = Track(new GameObject("MixedTestPrefab"));
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                TrackAssetPath(prefabPath);
                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

                checker._assetPaths = new List<string>
                {
                    "Assets/NonExistent/Invalid/Path",
                    Root,
                    "",
                    null,
                    "   ",
                };

                Assert.DoesNotThrow(
                    () => checker.RunChecksImproved(),
                    "RunChecksImproved() should process valid paths even when list contains invalid entries"
                );
            });
        }

        [Test]
        public void RunChecksWithMultipleNonExistentPathsLogsAllInError()
        {
            const string path1 = "Assets/NonExistent/Path1";
            const string path2 = "Assets/NonExistent/Path2";
            const string path3 = "Assets/Another/Missing/Path";

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            checker._assetPaths = new List<string> { path1, path2, path3 };

            LogAssert.Expect(LogType.Error, InvalidPathsErrorPattern);

            Assert.DoesNotThrow(
                () => checker.RunChecksImproved(),
                "RunChecksImproved() should not throw when multiple paths are non-existent"
            );
        }

        [Test]
        public void RunChecksWithMultipleValidFoldersCompletesWithoutError()
        {
            ExecuteWithImmediateImport(() =>
            {
                string subFolder1 = Path.Combine(Root, "SubFolder1").SanitizePath();
                string subFolder2 = Path.Combine(Root, "SubFolder2").SanitizePath();
                string subFolder3 = Path.Combine(Root, "SubFolder3").SanitizePath();

                EnsureFolder(subFolder1);
                EnsureFolder(subFolder2);
                EnsureFolder(subFolder3);

                string prefabPath1 = Path.Combine(subFolder1, "Prefab1.prefab").SanitizePath();
                string prefabPath2 = Path.Combine(subFolder2, "Prefab2.prefab").SanitizePath();
                string prefabPath3 = Path.Combine(subFolder3, "Prefab3.prefab").SanitizePath();

                GameObject go1 = Track(new GameObject("Prefab1"));
                GameObject go2 = Track(new GameObject("Prefab2"));
                GameObject go3 = Track(new GameObject("Prefab3"));

                PrefabUtility.SaveAsPrefabAsset(go1, prefabPath1);
                PrefabUtility.SaveAsPrefabAsset(go2, prefabPath2);
                PrefabUtility.SaveAsPrefabAsset(go3, prefabPath3);

                TrackAssetPath(prefabPath1);
                TrackAssetPath(prefabPath2);
                TrackAssetPath(prefabPath3);

                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
                checker._assetPaths = new List<string> { subFolder1, subFolder2, subFolder3 };

                Assert.DoesNotThrow(
                    () => checker.RunChecksImproved(),
                    "RunChecksImproved() should complete without error when scanning multiple valid folders"
                );
            });
        }

        [Test]
        [TestCase(1, TestName = "SingleFolder")]
        [TestCase(2, TestName = "TwoFolders")]
        [TestCase(5, TestName = "FiveFolders")]
        [TestCase(10, TestName = "TenFolders")]
        public void RunChecksWithVariousFolderCountsCompletesWithoutError(int folderCount)
        {
            ExecuteWithImmediateImport(() =>
            {
                // Varying folder counts catches array pooling and sizing defects such as the SystemArrayPool bug.
                List<string> folders = new();

                for (int i = 0; i < folderCount; i++)
                {
                    string folder = Path.Combine(Root, $"TestFolder{i}").SanitizePath();
                    EnsureFolder(folder);
                    folders.Add(folder);

                    string prefabPath = Path.Combine(folder, $"TestPrefab{i}.prefab")
                        .SanitizePath();
                    GameObject go = Track(new GameObject($"TestPrefab{i}"));
                    PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                    TrackAssetPath(prefabPath);
                }

                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
                checker._assetPaths = folders;

                Assert.DoesNotThrow(
                    () => checker.RunChecksImproved(),
                    $"RunChecksImproved() should complete without error when scanning {folderCount} folder(s)"
                );
            });
        }

        [Test]
        public void RunChecksWithDuplicateFoldersCompletesWithoutError()
        {
            ExecuteWithImmediateImport(() =>
            {
                string prefabPath = Path.Combine(Root, "DuplicateTest.prefab").SanitizePath();
                EnsureFolder(Path.GetDirectoryName(prefabPath).SanitizePath());

                GameObject go = Track(new GameObject("DuplicateTestPrefab"));
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                TrackAssetPath(prefabPath);
                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

                checker._assetPaths = new List<string> { Root, Root, Root };

                Assert.DoesNotThrow(
                    () => checker.RunChecksImproved(),
                    "RunChecksImproved() should handle duplicate folder paths gracefully"
                );
            });
        }

        [Test]
        public void RunChecksWithNestedFoldersCompletesWithoutError()
        {
            ExecuteWithImmediateImport(() =>
            {
                string parentFolder = Path.Combine(Root, "Parent").SanitizePath();
                string childFolder = Path.Combine(parentFolder, "Child").SanitizePath();

                EnsureFolder(childFolder);

                string parentPrefabPath = Path.Combine(parentFolder, "ParentPrefab.prefab")
                    .SanitizePath();
                string childPrefabPath = Path.Combine(childFolder, "ChildPrefab.prefab")
                    .SanitizePath();

                GameObject parentGo = Track(new GameObject("ParentPrefab"));
                GameObject childGo = Track(new GameObject("ChildPrefab"));

                PrefabUtility.SaveAsPrefabAsset(parentGo, parentPrefabPath);
                PrefabUtility.SaveAsPrefabAsset(childGo, childPrefabPath);

                TrackAssetPath(parentPrefabPath);
                TrackAssetPath(childPrefabPath);

                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

                checker._assetPaths = new List<string> { parentFolder, childFolder };

                Assert.DoesNotThrow(
                    () => checker.RunChecksImproved(),
                    "RunChecksImproved() should handle nested folder paths gracefully"
                );
            });
        }
    }
#endif
}
