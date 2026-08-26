// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SingletonAssetPollutionPreventionTests : CommonTestBase
    {
        private const string ResourcesRoot = "Assets/Resources";

        private static readonly Type[] AllTestSingletonTypesWithExclusion = new[]
        {
            typeof(TestSingleton),
            typeof(EmptyPathSingleton),
            typeof(CustomPathSingleton),
            typeof(MultipleInstancesSingleton),
            typeof(ResourceBackedSingleton),
            typeof(DeepPathResourceSingleton),
            typeof(WrongPathFallbackSingleton),
            typeof(MultiAssetScriptableSingleton),
            typeof(LifecycleScriptableSingleton),
            typeof(MissingResourceSingleton),
            typeof(AutoScriptableSingleton),
            typeof(ScriptableMismatchSingleton),
            typeof(SingleLevelPathSingleton),
        };

        private static readonly Type[] TestSingletonTypesWithoutExclusion = new[]
        {
            typeof(CreatorPathSingleton),
            typeof(NestedDiskSingleton),
            typeof(CaseMismatch),
            typeof(Duplicate),
        };

        private bool _previousIncludeTestAssemblies;
        private Func<Type, bool> _previousTypeFilter;
        private bool _previousEditorUiSuppress;
        private readonly List<string> _createdAssets = new();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Clean up any leftover test folders from previous test runs
            CleanupAllKnownTestFolders();
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();

            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;
            _previousIncludeTestAssemblies = ScriptableObjectSingletonCreator.IncludeTestAssemblies;
            _previousTypeFilter = ScriptableObjectSingletonCreator.TypeFilter;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = false;
            ScriptableObjectSingletonCreator.TypeFilter = null;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = true;
            yield return CleanupExistingTestSingletonAssets();
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            yield return base.UnityTearDown();
            foreach (string path in _createdAssets)
            {
                DeleteAssetIfExists(path);
                yield return null;
            }
            _createdAssets.Clear();
            yield return CleanupExistingTestSingletonAssets();
            yield return CleanupTestFolders();
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = _previousIncludeTestAssemblies;
            ScriptableObjectSingletonCreator.TypeFilter = _previousTypeFilter;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = false;
            EditorUi.Suppress = _previousEditorUiSuppress;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            // Clean up all known test folders including duplicates
            CleanupAllKnownTestFolders();
            AssetPostprocessorDeferral.FlushForTesting();
            yield return null;
        }

        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();
            // Final cleanup of all test folders
            CleanupAllKnownTestFolders();
        }

        [Test]
        public void AllTestSingletonTypesWithExclusionHaveTheAttribute()
        {
            List<string> missingAttribute = new();
            foreach (Type type in AllTestSingletonTypesWithExclusion)
            {
                if (
                    !ReflectionHelpers.TryGetAttributeSafe<ExcludeFromSingletonCreationAttribute>(
                        type,
                        out _,
                        inherit: false
                    )
                )
                {
                    missingAttribute.Add(type.FullName);
                }
            }

            Assert.IsEmpty(
                missingAttribute,
                $"The following test singleton types are missing [ExcludeFromSingletonCreation] attribute:\n{string.Join("\n", missingAttribute)}"
            );
        }

        [Test]
        public void CreatorTestTypesDoNotHaveExclusionAttribute()
        {
            List<string> hasAttribute = new();
            foreach (Type type in TestSingletonTypesWithoutExclusion)
            {
                if (
                    ReflectionHelpers.TryGetAttributeSafe<ExcludeFromSingletonCreationAttribute>(
                        type,
                        out _,
                        inherit: false
                    )
                )
                {
                    hasAttribute.Add(type.FullName);
                }
            }

            Assert.IsEmpty(
                hasAttribute,
                $"The following creator test types should NOT have [ExcludeFromSingletonCreation] attribute as they are used to test creation logic:\n{string.Join("\n", hasAttribute)}"
            );
        }

        private static IEnumerable<ProtectedPathTestCase> ProtectedPathTestCases()
        {
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios",
                true,
                false,
                "Main Wallstop Studios folder should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers",
                true,
                false,
                "Main Unity Helpers folder should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers/SomeAsset.asset",
                true,
                false,
                "Assets inside Unity Helpers should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios 1",
                false,
                true,
                "Wallstop Studios 1 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios 2",
                false,
                true,
                "Wallstop Studios 2 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 1",
                false,
                true,
                "Unity Helpers 1 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 15",
                false,
                true,
                "Unity Helpers 15 is a duplicate and should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 1/SomeAsset.asset",
                false,
                true,
                "Assets inside duplicate Unity Helpers should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Wallstop Studios/Unity Helpers 99/Nested/File.asset",
                false,
                true,
                "Nested assets inside duplicate Unity Helpers should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Plugins",
                true,
                false,
                "Plugins folder should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Plugins/SomePlugin/Code.cs",
                true,
                false,
                "Assets inside Plugins should be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/TempTestFolder",
                false,
                false,
                "Temp test folders should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "Assets/Resources/Tests",
                false,
                false,
                "Test folders in Resources should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                "",
                false,
                false,
                "Empty path should NOT be protected"
            );
            yield return new ProtectedPathTestCase(
                null,
                false,
                false,
                "Null path should NOT be protected"
            );
        }

        [Test]
        public void IsProtectedPathReturnsExpectedValue(
            [ValueSource(nameof(ProtectedPathTestCases))] ProtectedPathTestCase testCase
        )
        {
            bool actual = ProtectionTestHooks.TestIsProtectedPath(testCase.Path);
            Assert.That(
                actual,
                Is.EqualTo(testCase.ExpectedIsProtected),
                $"IsProtectedPath(\"{testCase.Path ?? "(null)"}\") returned {actual}, expected {testCase.ExpectedIsProtected}. "
                    + $"Description: {testCase.Description}"
            );
        }

        [Test]
        public void IsKnownDuplicatePollutionReturnsExpectedValue(
            [ValueSource(nameof(ProtectedPathTestCases))] ProtectedPathTestCase testCase
        )
        {
            bool actual = ProtectionTestHooks.TestIsKnownDuplicatePollution(testCase.Path);
            Assert.That(
                actual,
                Is.EqualTo(testCase.ExpectedIsDuplicatePollution),
                $"IsKnownDuplicatePollution(\"{testCase.Path ?? "(null)"}\") returned {actual}, expected {testCase.ExpectedIsDuplicatePollution}. "
                    + $"Description: {testCase.Description}"
            );
        }

        [Test]
        public void DuplicatePollutionFoldersAreNotProtected()
        {
            string[] duplicatePaths = new[]
            {
                "Assets/Resources/Wallstop Studios/Unity Helpers 1",
                "Assets/Resources/Wallstop Studios/Unity Helpers 2",
                "Assets/Resources/Wallstop Studios/Unity Helpers 10",
                "Assets/Resources/Wallstop Studios/Unity Helpers 99",
                "Assets/Resources/Wallstop Studios 1",
                "Assets/Resources/Wallstop Studios 2",
            };

            List<string> incorrectlyProtected = new();
            foreach (string path in duplicatePaths)
            {
                if (ProtectionTestHooks.TestIsProtectedPath(path))
                {
                    incorrectlyProtected.Add(path);
                }
            }

            Assert.IsEmpty(
                incorrectlyProtected,
                $"The following duplicate pollution folders are incorrectly marked as protected:\n"
                    + $"{string.Join("\n", incorrectlyProtected)}\n"
                    + "These should be deletable during test cleanup."
            );
        }

        [Test]
        public void MainProductionFoldersAreProtected()
        {
            string[] mainFolders = new[]
            {
                "Assets/Resources/Wallstop Studios",
                "Assets/Resources/Wallstop Studios/Unity Helpers",
                "Assets/Plugins",
                "Assets/Editor Default Resources",
                "Assets/StreamingAssets",
            };

            List<string> unprotected = new();
            foreach (string path in mainFolders)
            {
                if (!ProtectionTestHooks.TestIsProtectedPath(path))
                {
                    unprotected.Add(path);
                }
            }

            Assert.IsEmpty(
                unprotected,
                $"The following main production folders are NOT protected:\n"
                    + $"{string.Join("\n", unprotected)}\n"
                    + "These should be protected from test cleanup."
            );
        }

        [UnityTest]
        public IEnumerator EnsureSingletonAssetsDoesNotCreateAssetsForExcludedTypes()
        {
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
            ScriptableObjectSingletonCreator.TypeFilter = null;
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureMetadataFolder();
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;

            List<string> beforePaths = new();
            foreach (Type type in AllTestSingletonTypesWithExclusion)
            {
                string path = GetExpectedAssetPath(type);
                if (
                    !string.IsNullOrEmpty(path)
                    && AssetDatabase.LoadAssetAtPath<Object>(path) != null
                )
                {
                    beforePaths.Add(path);
                }
            }

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            List<string> unexpectedlyCreated = new();
            foreach (Type type in AllTestSingletonTypesWithExclusion)
            {
                string path = GetExpectedAssetPath(type);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (beforePaths.Contains(path))
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    unexpectedlyCreated.Add($"{type.Name} at {path}");
                    _createdAssets.Add(path);
                }
            }

            Assert.IsEmpty(
                unexpectedlyCreated,
                $"EnsureSingletonAssets unexpectedly created assets for excluded types:\n{string.Join("\n", unexpectedlyCreated)}"
            );
        }

        [UnityTest]
        public IEnumerator EnsureSingletonAssetsRespectsExclusionEvenWithIncludeTestAssembliesTrue()
        {
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
            ScriptableObjectSingletonCreator.TypeFilter = null;
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureMetadataFolder();
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
            yield return CleanupExistingTestSingletonAssets();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            List<string> createdAssets = new();
            foreach (Type type in AllTestSingletonTypesWithExclusion)
            {
                string path = GetExpectedAssetPath(type);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    createdAssets.Add($"{type.Name} at {path}");
                    _createdAssets.Add(path);
                }
            }

            Assert.IsEmpty(
                createdAssets,
                $"Excluded singleton types should never have assets created, even with IncludeTestAssemblies=true:\n{string.Join("\n", createdAssets)}"
            );
        }

        [UnityTest]
        public IEnumerator TypeFilterBypassDoesNotOverrideExclusionAttribute()
        {
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
            ScriptableObjectSingletonCreator.TypeFilter = _ => true;
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureMetadataFolder();
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
            yield return CleanupExistingTestSingletonAssets();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            List<string> createdAssets = new();
            foreach (Type type in AllTestSingletonTypesWithExclusion)
            {
                string path = GetExpectedAssetPath(type);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    createdAssets.Add($"{type.Name} at {path}");
                    _createdAssets.Add(path);
                }
            }

            Assert.IsEmpty(
                createdAssets,
                $"Excluded singleton types should never be created even when TypeFilter returns true:\n{string.Join("\n", createdAssets)}"
            );
        }

        [UnityTest]
        public IEnumerator ResourcesFolderRemainsCleanAfterMultipleEnsureCalls()
        {
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureMetadataFolder();
            yield return CleanupExistingTestSingletonAssets();

            HashSet<string> initialAssetGuids = new(
                AssetDatabase.FindAssets("t:ScriptableObject", new[] { ResourcesRoot })
            );

            for (int i = 0; i < 3; i++)
            {
                ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
                ScriptableObjectSingletonCreator.TypeFilter = null;
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                yield return null;
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
                yield return null;
            }

            string[] finalGuids = AssetDatabase.FindAssets(
                "t:ScriptableObject",
                new[] { ResourcesRoot }
            );
            List<string> newAssets = new();
            foreach (string guid in finalGuids)
            {
                if (!initialAssetGuids.Contains(guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    foreach (Type type in AllTestSingletonTypesWithExclusion)
                    {
                        if (path.Contains(type.Name))
                        {
                            newAssets.Add(path);
                            _createdAssets.Add(path);
                            break;
                        }
                    }
                }
            }

            Assert.IsEmpty(
                newAssets,
                $"Test singleton assets were unexpectedly created in Resources:\n{string.Join("\n", newAssets)}"
            );
        }

        [Test]
        public void ExcludeFromSingletonCreationAttributeIsDefinedCorrectly()
        {
            Type attributeType = typeof(ExcludeFromSingletonCreationAttribute);
            Assert.IsTrue(
                attributeType.IsSealed,
                "ExcludeFromSingletonCreationAttribute should be sealed"
            );

            AttributeUsageAttribute usage =
                attributeType.GetCustomAttribute<AttributeUsageAttribute>();
            Assert.IsTrue(
                usage != null,
                "ExcludeFromSingletonCreationAttribute should have AttributeUsage"
            );
            Assert.AreEqual(
                AttributeTargets.Class,
                usage.ValidOn,
                "ExcludeFromSingletonCreationAttribute should target classes"
            );
            Assert.IsFalse(
                usage.Inherited,
                "ExcludeFromSingletonCreationAttribute should not be inherited"
            );
            Assert.IsFalse(
                usage.AllowMultiple,
                "ExcludeFromSingletonCreationAttribute should not allow multiple"
            );
        }

        [UnityTest]
        public IEnumerator TestTypesWithoutExclusionNotCreatedWhenIncludeTestAssembliesIsFalse()
        {
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = false;
            ScriptableObjectSingletonCreator.TypeFilter = null;
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureMetadataFolder();
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
            yield return CleanupExistingTestSingletonAssets();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            List<string> createdAssets = new();
            foreach (Type type in TestSingletonTypesWithoutExclusion)
            {
                string path = GetExpectedAssetPath(type);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    createdAssets.Add($"{type.Name} at {path}");
                    _createdAssets.Add(path);
                }
            }

            Assert.IsEmpty(
                createdAssets,
                $"Test types should not be created when IncludeTestAssemblies=false (protected by IsTestType):\n{string.Join("\n", createdAssets)}"
            );
        }

        [Test]
        public void AllTestTypesAreDetectedByIsTestTypeHelper()
        {
            List<string> notDetected = new();
            foreach (Type type in AllTestSingletonTypes)
            {
                if (!TestAssemblyHelper.IsTestType(type))
                {
                    notDetected.Add(type.FullName);
                }
            }

            Assert.IsEmpty(
                notDetected,
                $"The following test singleton types are not detected by TestAssemblyHelper.IsTestType:\n{string.Join("\n", notDetected)}"
            );
        }

        /// <summary>
        /// Data source for duplicate folder detection tests.
        /// Each entry contains: parentPath, folderBaseName
        /// </summary>
        private static IEnumerable<DuplicateFolderTestCase> DuplicateFolderTestCases()
        {
            // Wallstop Studios duplicates in Assets/Resources
            yield return new DuplicateFolderTestCase(
                "Assets/Resources",
                "Wallstop Studios",
                "Duplicate Wallstop Studios folders may be created during test runs"
            );

            // Unity Helpers duplicates inside Wallstop Studios
            yield return new DuplicateFolderTestCase(
                "Assets/Resources/Wallstop Studios",
                "Unity Helpers",
                "Duplicate Unity Helpers folders may be created during concurrent test execution"
            );
        }

        [Test]
        public void NoDuplicateFoldersExist(
            [ValueSource(nameof(DuplicateFolderTestCases))] DuplicateFolderTestCase testCase
        )
        {
            // Check for duplicate folders matching the pattern "FolderName N" where N is a number
            if (!AssetDatabase.IsValidFolder(testCase.ParentPath))
            {
                Assert.Pass(
                    $"Parent folder '{testCase.ParentPath}' doesn't exist - no duplicates possible."
                );
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(testCase.ParentPath);
            List<string> duplicates = new();
            string prefix = testCase.FolderBaseName + " ";

            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (
                    name != null
                    && name.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(name.Substring(prefix.Length), out _)
                )
                {
                    duplicates.Add(folder);
                }
            }

            if (0 < duplicates.Count)
            {
                Debug.LogWarning(
                    $"[SingletonAssetPollutionPreventionTests] Found {duplicates.Count} duplicate folder(s) in {testCase.ParentPath}. "
                        + $"Attempting cleanup before asserting."
                );

                foreach (string duplicate in duplicates)
                {
                    bool isDuplicatePollution = ProtectionTestHooks.TestIsKnownDuplicatePollution(
                        duplicate
                    );
                    bool isProtected = ProtectionTestHooks.TestIsProtectedPath(duplicate);
                    Debug.Log(
                        $"[SingletonAssetPollutionPreventionTests] Duplicate folder: {duplicate}, "
                            + $"IsDuplicatePollution={isDuplicatePollution}, IsProtected={isProtected}"
                    );
                }

                CleanupAllKnownTestFolders();
                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                subFolders = AssetDatabase.GetSubFolders(testCase.ParentPath);
                duplicates.Clear();
                foreach (string folder in subFolders)
                {
                    string name = Path.GetFileName(folder);
                    if (
                        name != null
                        && name.StartsWith(prefix, StringComparison.Ordinal)
                        && int.TryParse(name.Substring(prefix.Length), out _)
                    )
                    {
                        duplicates.Add(folder);
                    }
                }
            }

            Assert.IsEmpty(
                duplicates,
                $"Found duplicate '{testCase.FolderBaseName}' folders in {testCase.ParentPath} that could not be cleaned up:\n"
                    + $"{string.Join("\n", duplicates)}\n\n"
                    + $"Description: {testCase.Description}\n"
                    + "This indicates a race condition or improper folder creation. "
                    + "Duplicates should have been cleaned by CleanupAllKnownTestFolders()."
            );
        }

        private static string GetExpectedAssetPath(Type type)
        {
            if (
                ReflectionHelpers.TryGetAttributeSafe<ScriptableSingletonPathAttribute>(
                    type,
                    out ScriptableSingletonPathAttribute pathAttr,
                    inherit: false
                ) && !string.IsNullOrWhiteSpace(pathAttr.resourcesPath)
            )
            {
                return $"{ResourcesRoot}/{pathAttr.resourcesPath}/{type.Name}.asset";
            }

            return $"{ResourcesRoot}/{type.Name}.asset";
        }

        private static Type[] AllTestSingletonTypes =>
            new List<Type>(AllTestSingletonTypesWithExclusion)
                .Concat(TestSingletonTypesWithoutExclusion)
                .ToArray();

        private IEnumerator CleanupExistingTestSingletonAssets()
        {
            foreach (Type type in AllTestSingletonTypes)
            {
                string path = GetExpectedAssetPath(type);
                if (!string.IsNullOrEmpty(path))
                {
                    DeleteAssetIfExists(path);
                    yield return null;
                }
            }

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            Object existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (existing != null || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private IEnumerator CleanupTestFolders()
        {
            string[] testFolders = new[]
            {
                ResourcesRoot + "/Tests/Nested/DeepPath",
                ResourcesRoot + "/Tests/Nested",
                ResourcesRoot + "/Tests/CreatorPath",
                ResourcesRoot + "/Tests",
                ResourcesRoot + "/CreatorTests/Collision",
                ResourcesRoot + "/CreatorTests/Retry",
                ResourcesRoot + "/CreatorTests/FileBlock",
                ResourcesRoot + "/CreatorTests/NoRetry",
                ResourcesRoot + "/CreatorTests",
                ResourcesRoot + "/CaseTest",
                ResourcesRoot + "/CustomPath",
                ResourcesRoot + "/SingleLevel",
                ResourcesRoot + "/Deep/Nested/Singletons",
                ResourcesRoot + "/Deep/Nested",
                ResourcesRoot + "/Deep",
                ResourcesRoot + "/Missing/Subfolder",
                ResourcesRoot + "/Missing",
            };

            foreach (string folder in testFolders)
            {
                TryDeleteEmptyFolder(folder);
                yield return null;
            }

            TryDeleteEmptyFolder(ResourcesRoot);
            yield return null;
        }

        private static void EnsureMetadataFolder()
        {
            string metadataFolder = "Assets/Resources/Wallstop Studios/Unity Helpers";
            // First, ensure the folder exists on disk to prevent Unity's internal
            // "Moving file failed" modal dialog
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absoluteDirectory = Path.Combine(projectRoot, metadataFolder);
                if (!Directory.Exists(absoluteDirectory))
                {
                    Directory.CreateDirectory(absoluteDirectory);
                }
            }

            // Also register in AssetDatabase
            string[] parts = metadataFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
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

        public sealed class ProtectedPathTestCase
        {
            public string Path { get; }
            public bool ExpectedIsProtected { get; }
            public bool ExpectedIsDuplicatePollution { get; }
            public string Description { get; }

            public ProtectedPathTestCase(
                string path,
                bool expectedIsProtected,
                bool expectedIsDuplicatePollution,
                string description
            )
            {
                Path = path;
                ExpectedIsProtected = expectedIsProtected;
                ExpectedIsDuplicatePollution = expectedIsDuplicatePollution;
                Description = description;
            }

            public override string ToString() => $"{(Path ?? "(null)")} - {Description}";
        }

        public sealed class DuplicateFolderTestCase
        {
            public string ParentPath { get; }
            public string FolderBaseName { get; }
            public string Description { get; }

            public DuplicateFolderTestCase(
                string parentPath,
                string folderBaseName,
                string description
            )
            {
                ParentPath = parentPath;
                FolderBaseName = folderBaseName;
                Description = description;
            }

            public override string ToString() => $"{FolderBaseName} in {ParentPath}";
        }
    }
#endif
}
