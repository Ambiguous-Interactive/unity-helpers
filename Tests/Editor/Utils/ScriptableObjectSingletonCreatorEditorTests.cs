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
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ScriptableObjectSingletonCreatorEditorTests : CommonTestBase
    {
        private const string ResourcesRoot = "Assets/Resources";
        private const string TargetFolder = ResourcesRoot + "/Tests/CreatorPath";
        private const string TargetAssetPath = TargetFolder + "/CreatorPathSingleton.asset";
        private const string NestedTargetFolder = ResourcesRoot + "/Tests/Nested/DeepPath";
        private const string NestedTargetAssetPath =
            NestedTargetFolder + "/NestedDiskSingleton.asset";
        private const string WrongFolder = ResourcesRoot + "/Tests/WrongPath";
        private const string WrongAssetPath = WrongFolder + "/CreatorPathSingleton.asset";
        private const string WrongFolderCaseVariant = ResourcesRoot + "/TestS/WrongPath";
        private const string WrongAssetPathCaseVariant =
            WrongFolderCaseVariant + "/CreatorPathSingleton.asset";
        private bool _previousEditorUiSuppress;
        private bool _previousIgnoreCompilationState;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();

            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
            // Allow explicit calls to EnsureSingletonAssets during tests
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
            // Bypass compilation state check - Unity may report isCompiling/isUpdating
            // as true during test runs after AssetDatabase operations
            _previousIgnoreCompilationState =
                ScriptableObjectSingletonCreator.IgnoreCompilationState;
            ScriptableObjectSingletonCreator.IgnoreCompilationState = true;
            ScriptableObjectSingletonCreator.TypeFilter = static type =>
                type == typeof(CreatorPathSingleton) || type == typeof(NestedDiskSingleton);
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureFolder("Assets/Resources/Wallstop Studios/Unity Helpers");
            DeleteAssetIfExists(TargetAssetPath);
            yield return null;
            DeleteAssetIfExists(WrongAssetPath);
            yield return null;
            DeleteAssetIfExists(WrongAssetPathCaseVariant);
            yield return null;
            DeleteAssetIfExists(NestedTargetAssetPath);
            yield return null;
            DeleteFolderHierarchy(TargetFolder);
            yield return null;
            DeleteFolderHierarchy(WrongFolder);
            ;
            yield return null;
            DeleteFolderHierarchy(WrongFolderCaseVariant);
            yield return null;
            DeleteFolderHierarchy(NestedTargetFolder);
            yield return null;
            AssetDatabase.SaveAssets();
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            yield return base.UnityTearDown();
            yield return null;
            DeleteAssetIfExists(TargetAssetPath);
            yield return null;
            DeleteAssetIfExists(WrongAssetPath);
            yield return null;
            DeleteAssetIfExists(WrongAssetPathCaseVariant);
            yield return null;
            DeleteAssetIfExists(NestedTargetAssetPath);
            yield return null;
            DeleteFolderHierarchy(TargetFolder);
            yield return null;
            DeleteFolderHierarchy(WrongFolder);
            yield return null;
            DeleteFolderHierarchy(WrongFolderCaseVariant);
            yield return null;
            DeleteFolderHierarchy(NestedTargetFolder);
            yield return null;
            TryDeleteEmptyFolderCaseInsensitive(ResourcesRoot + "/Tests");
            yield return null;
            TryDeleteEmptyFolderCaseInsensitive(ResourcesRoot + "/TestS");
            yield return null;
            TryDeleteEmptyFolder(ResourcesRoot);
            yield return null;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = false;
            ScriptableObjectSingletonCreator.TypeFilter = null;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = false;
            ScriptableObjectSingletonCreator.IgnoreCompilationState =
                _previousIgnoreCompilationState;
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();
            AssetDatabase.SaveAssets();
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            AssetPostprocessorDeferral.FlushForTesting();
            EditorUi.Suppress = _previousEditorUiSuppress;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreatesAssetAtAttributePath()
        {
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            CreatorPathSingleton asset = AssetDatabase.LoadAssetAtPath<CreatorPathSingleton>(
                TargetAssetPath
            );
            Assert.IsTrue(asset != null);
        }

        [UnityTest]
        public IEnumerator RelocatesExistingAssetToAttributePath()
        {
            EnsureFolder(ResourcesRoot);
            yield return null;
            EnsureFolder(WrongFolder);
            yield return null;
            CreatorPathSingleton instance = ScriptableObject.CreateInstance<CreatorPathSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by test cleanup
            AssetDatabase.CreateAsset(instance, WrongAssetPath);
            AssetDatabase.SaveAssets();
            yield return null;

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;
            CreatorPathSingleton relocated = AssetDatabase.LoadAssetAtPath<CreatorPathSingleton>(
                TargetAssetPath
            );
            Assert.IsTrue(relocated != null);
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<CreatorPathSingleton>(WrongAssetPath) == null
            );
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteFolderHierarchy(string folderPath)
        {
            string path = ResolveExistingFolderPath(folderPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            while (
                !string.IsNullOrEmpty(path)
                && !string.Equals(path, ResourcesRoot, StringComparison.OrdinalIgnoreCase)
            )
            {
                if (!AssetDatabase.IsValidFolder(path))
                {
                    path = Path.GetDirectoryName(path)?.SanitizePath();
                    continue;
                }

                if (!AssetDatabase.DeleteAsset(path))
                {
                    break;
                }

                path = Path.GetDirectoryName(path)?.SanitizePath();
            }
        }

        private static void TryDeleteEmptyFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
            if (subFolders != null && subFolders.Length > 0)
            {
                return;
            }

            string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            if (assets != null && assets.Length > 0)
            {
                return;
            }

            AssetDatabase.DeleteAsset(folderPath);
        }

        private static void TryDeleteEmptyFolderCaseInsensitive(string folderPath)
        {
            string resolved = ResolveExistingFolderPath(folderPath);
            if (!string.IsNullOrEmpty(resolved))
            {
                TryDeleteEmptyFolder(resolved);
            }
        }

        private static string ResolveExistingFolderPath(string intended)
        {
            if (string.IsNullOrWhiteSpace(intended))
            {
                return null;
            }

            intended = intended.SanitizePath();
            string[] parts = intended.Split('/');
            if (parts.Length == 0)
            {
                return null;
            }

            string current = parts[0];
            if (!string.Equals(current, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return intended;
            }

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
                    return intended;
                }

                string match = null;
                for (int s = 0; s < subs.Length; s++)
                {
                    string sub = subs[s];
                    int last = sub.LastIndexOf('/', sub.Length - 1);
                    string name = last >= 0 ? sub.Substring(last + 1) : sub;
                    if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        match = sub;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(match))
                {
                    return intended;
                }

                current = match;
            }

            return current;
        }

        [UnityTest]
        public IEnumerator RelocatesExistingAssetToAttributePathFromMismatchedParentCase()
        {
            EnsureFolder(ResourcesRoot);
            yield return null;
            EnsureFolder(WrongFolderCaseVariant);
            yield return null;
            CreatorPathSingleton instance = ScriptableObject.CreateInstance<CreatorPathSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by test cleanup
            AssetDatabase.CreateAsset(instance, WrongAssetPathCaseVariant);
            AssetDatabase.SaveAssets();
            yield return null;
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            CreatorPathSingleton relocated = AssetDatabase.LoadAssetAtPath<CreatorPathSingleton>(
                TargetAssetPath
            );
            Assert.IsTrue(relocated != null);
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<CreatorPathSingleton>(WrongAssetPathCaseVariant)
                    == null
            );
        }

        private static IEnumerable<DiskFolderScenario> DiskOnlyFolderScenarios()
        {
            yield return new DiskFolderScenario(
                "CreatorPath",
                TargetFolder,
                TargetAssetPath,
                typeof(CreatorPathSingleton)
            );

            yield return new DiskFolderScenario(
                "NestedDeepPath",
                NestedTargetFolder,
                NestedTargetAssetPath,
                typeof(NestedDiskSingleton)
            );
        }

        [UnityTest]
        public IEnumerator CreatesAssetWhenFolderOnlyExistsOnDisk(
            [ValueSource(nameof(DiskOnlyFolderScenarios))] DiskFolderScenario scenario
        )
        {
            EnsureFolder(ResourcesRoot);
            yield return null;
            DeleteAssetIfExists(scenario.AssetPath);
            yield return null;
            DeleteFolderHierarchy(scenario.FolderPath);
            yield return null;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absoluteTarget = Path.Combine(
                projectRoot,
                scenario.FolderPath.Replace('/', Path.DirectorySeparatorChar)
            );

            if (Directory.Exists(absoluteTarget))
            {
                Directory.Delete(absoluteTarget, true);
            }

            string metaPath = absoluteTarget + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            Directory.CreateDirectory(absoluteTarget);
            yield return null;

            Assert.IsFalse(AssetDatabase.IsValidFolder(scenario.FolderPath));

            Func<Type, bool> originalFilter = ScriptableObjectSingletonCreator.TypeFilter;
            ScriptableObjectSingletonCreator.TypeFilter = type => type == scenario.SingletonType;
            try
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
                yield return null;
            }
            finally
            {
                ScriptableObjectSingletonCreator.TypeFilter = originalFilter;
            }

            Assert.IsTrue(AssetDatabase.IsValidFolder(scenario.FolderPath));
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(scenario.AssetPath) != null);
            Assert.IsFalse(AssetDatabase.IsValidFolder(scenario.FolderPath + " 1"));
        }

        public sealed class DiskFolderScenario
        {
            public DiskFolderScenario(
                string name,
                string folderPath,
                string assetPath,
                Type singletonType
            )
            {
                Name = name;
                FolderPath = folderPath;
                AssetPath = assetPath;
                SingletonType = singletonType;
            }

            public string Name { get; }
            public string FolderPath { get; }
            public string AssetPath { get; }
            public Type SingletonType { get; }

            public override string ToString()
            {
                return string.IsNullOrEmpty(Name) ? base.ToString() : Name;
            }
        }

        [UnityTest]
        public IEnumerator SkipsCreationWhenAssetFileExistsButIsNotImported()
        {
            DeleteAssetIfExists(TargetAssetPath);
            yield return null;

            EnsureFolder(TargetFolder);
            DeleteFileIfExists(TargetAssetPath);
            DeleteFileIfExists(TargetAssetPath + ".meta");
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                ImportAssetOptions.ForceSynchronousImport
            );
            yield return null;

            string absolutePath = GetAbsolutePath(TargetAssetPath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write an invalid asset file - Unity will fail to load it
            File.WriteAllText(absolutePath, "pending import");

            // Capture pre-call state for diagnostics
            string existingGuid = AssetDatabase.AssetPathToGUID(TargetAssetPath);
            bool fileExistsBefore = File.Exists(absolutePath);

            // Unity may or may not log an error when trying to load an invalid asset file
            // (depends on Unity version and whether the file has been indexed).
            IgnoreVersionSpecificInvalidAssetImportLogs();

            // Expect the "on-disk asset" warning OR the "target path already occupied" warning
            // (depends on whether Unity has a GUID for the path)
            LogAssert.Expect(
                LogType.Warning,
                new Regex(
                    "(on-disk asset|target path already occupied).*CreatorPathSingleton",
                    RegexOptions.IgnoreCase
                )
            );
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(
                File.Exists(absolutePath),
                $"The invalid file should still exist. Pre-call state: existingGuid='{existingGuid}', fileExistsBefore={fileExistsBefore}"
            );

            Object loadedAsset;
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                IgnoreVersionSpecificInvalidAssetImportLogs();
                loadedAsset = AssetDatabase.LoadAssetAtPath<Object>(TargetAssetPath);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }

            Assert.IsTrue(
                loadedAsset == null,
                "No valid asset should be loaded from the invalid file"
            );

            // Clean up the invalid file and its meta
            File.Delete(absolutePath);
            DeleteFileIfExists(TargetAssetPath + ".meta");
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            yield return null;

            // Now EnsureSingletonAssets should create the real asset
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<Object>(TargetAssetPath) != null,
                "Valid singleton asset should be created after removing the invalid file"
            );
        }

        private static void IgnoreVersionSpecificInvalidAssetImportLogs()
        {
            // This test intentionally writes an invalid .asset file. Unity's importer logs
            // different internal errors across editor versions before production code emits
            // the stable warning asserted by the test.
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTest]
        public IEnumerator RecreatesAssetWhenGuidRemainsButFileIsMissing()
        {
            DeleteAssetIfExists(TargetAssetPath);
            yield return null;

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(TargetAssetPath) != null);

            string absoluteAsset = GetAbsolutePath(TargetAssetPath);
            if (File.Exists(absoluteAsset))
            {
                File.Delete(absoluteAsset);
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.AssetPathToGUID(TargetAssetPath).Length > 0,
                "Meta should still exist after deleting only the asset file."
            );
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(TargetAssetPath) == null);

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Object>(TargetAssetPath) != null);
        }

        [UnityTest]
        public IEnumerator EnsureSingletonAssetsCreatesFolderHierarchyWhenMissing()
        {
            string metadataFolder = "Assets/Resources/Wallstop Studios/Unity Helpers";

            DeleteFolderHierarchy(metadataFolder);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(metadataFolder),
                "Setup: Metadata folder should not exist before test"
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios"),
                "Wallstop Studios folder should be created"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(metadataFolder),
                "Metadata folder hierarchy should be created"
            );
        }

        [UnityTest]
        public IEnumerator EnsureSingletonAssetsDoesNotThrowWhenFoldersAlreadyExist()
        {
            string metadataFolder = "Assets/Resources/Wallstop Studios/Unity Helpers";

            EnsureFolder(metadataFolder);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(metadataFolder),
                "Setup: Metadata folder should exist before test"
            );

            Assert.DoesNotThrow(
                () => ScriptableObjectSingletonCreator.EnsureSingletonAssets(),
                "EnsureSingletonAssets should not throw when folders already exist"
            );
            yield return null;
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
