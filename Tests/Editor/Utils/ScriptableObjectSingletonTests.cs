// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ScriptableObjectSingletonTests : CommonTestBase
    {
        /// <summary>
        /// Tracks asset paths created during tests for cleanup in TearDown.
        /// Thread-safe in practice because NUnit runs tests sequentially within a fixture,
        /// and the list is cleared at the start of each test in SetUp.
        /// </summary>
        private static readonly System.Collections.Generic.List<string> CreatedAssetPaths = new();

        /// <summary>
        /// Tracks in-memory ScriptableObject instances created during tests for cleanup in TearDown.
        /// Thread-safe in practice because NUnit runs tests sequentially within a fixture,
        /// and the list is cleared at the start of each test in SetUp.
        /// </summary>
        private static readonly System.Collections.Generic.List<ScriptableObject> InMemoryInstances =
            new();
        private const string ResourcesRoot = "Assets/Resources";
        private bool _previousEditorUiSuppress;

        public override void CommonOneTimeSetUp()
        {
            if (Application.isPlaying)
            {
                return;
            }
            base.CommonOneTimeSetUp();

            // Batch all cleanup operations to minimize AssetDatabase.Refresh calls
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                // Clean up any leftover test folders from previous test runs
                CleanupAllKnownTestFolders();
            }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;

            // Clear all singleton instances (these are in-memory operations, no batching needed)
            TestSingleton.ClearInstance();
            EmptyPathSingleton.ClearInstance();
            CustomPathSingleton.ClearInstance();
            MultipleInstancesSingleton.ClearInstance();
            ResourceBackedSingleton.ClearInstance();
            DeepPathResourceSingleton.ClearInstance();
            WrongPathFallbackSingleton.ClearInstance();
            MultiAssetScriptableSingleton.ClearInstance();
            LifecycleScriptableSingleton.ClearInstance();
            MissingResourceSingleton.ClearInstance();
            SingleLevelPathSingleton.ClearInstance();

            // Batch all asset cleanup operations to minimize AssetDatabase.Refresh calls
            // This reduces 20+ individual Refresh calls down to 1 at the end of the batch
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                // Clean up any leftover assets from previous runs to avoid broken nested-class assets
                EnsureFolder(ResourcesRoot);
                DeleteAssetIfExists("Assets/Resources/TestSingleton.asset");
                DeleteAssetIfExists("Assets/Resources/EmptyPathSingleton.asset");
                DeleteAssetIfExists("Assets/Resources/CustomPath/CustomPathSingleton.asset");
                DeleteAssetIfExists(ToFullResourcePath("ResourceBackedSingleton.asset"));
                DeleteAssetIfExists(
                    ToFullResourcePath("Deep/Nested/Singletons/DeepPathResourceSingleton.asset")
                );
                DeleteAssetIfExists(ToFullResourcePath("Loose/WrongPathInstance.asset"));
                DeleteAssetIfExists(ToFullResourcePath("Multi/Primary.asset"));
                DeleteAssetIfExists(ToFullResourcePath("Multi/Secondary.asset"));
                DeleteAssetIfExists(
                    ToFullResourcePath("Lifecycle/LifecycleScriptableSingleton.asset")
                );
                DeleteAssetIfExists(ToFullResourcePath("MultiNatural/Entry2.asset"));
                DeleteAssetIfExists(ToFullResourcePath("MultiNatural/Entry10.asset"));
                DeleteAssetIfExists(ToFullResourcePath("MultiNatural/Entry11.asset"));
                DeleteAssetIfExists(ScriptableObjectSingletonMetadata.AssetPath);
                DeleteAssetIfExists(ToFullResourcePath("SingleLevel/EmptyPathSingleton.asset"));
                DeleteAssetIfExists(
                    ToFullResourcePath("SingleLevel/SingleLevelPathSingleton.asset")
                );
                DeleteFolderIfEmpty("Assets/Resources/CustomPath");
                DeleteFolderIfEmpty("Assets/Resources/SingleLevel");
            }

            yield return null;

            // For nested test types, Unity cannot create valid .asset files (no script file).
            // Instead, create in-memory instances so the singleton loader can discover them via FindObjectsOfTypeAll.
            CreateInMemoryInstance<TestSingleton>();
            CreateInMemoryInstance<EmptyPathSingleton>();
            CreateInMemoryInstance<CustomPathSingleton>();
            yield return null;
        }

        /// <summary>
        /// Deletes an asset if it exists. When called inside a batch scope,
        /// defers Refresh() until the batch completes.
        /// </summary>
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
                // Only refresh if not batching - batched operations defer refresh until batch completes
                AssetDatabaseBatchHelper.RefreshIfNotBatching();
                PruneResourceFoldersForPath(assetPath);
            }
        }

        private static void DeleteFolderIfEmpty(string folderPath)
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
            if (subFolders is { Length: > 0 })
            {
                return;
            }

            // Re-check folder validity immediately before FindAssets to minimize race window
            // FindAssets emits a warning if the folder doesn't exist
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] assetGuids;
            try
            {
                assetGuids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            }
            catch
            {
                // Folder may have been deleted between check and FindAssets
                return;
            }

            // Final validity check - folder may have been deleted during FindAssets
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            foreach (string guid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (
                    !string.IsNullOrEmpty(assetPath)
                    && !string.Equals(assetPath, folderPath, StringComparison.Ordinal)
                )
                {
                    return;
                }
            }

            AssetDatabase.DeleteAsset(folderPath);
            // Only refresh if not batching - batched operations defer refresh until batch completes
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
        }

        private static string ToFullResourcePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("relativePath cannot be empty", nameof(relativePath));
            }

            string sanitized = relativePath.Replace("\\", "/").TrimStart('/');
            if (string.IsNullOrEmpty(sanitized))
            {
                throw new ArgumentException("relativePath cannot be empty", nameof(relativePath));
            }

            return $"{ResourcesRoot}/{sanitized}".Replace("//", "/");
        }

        private static TType CreateResourceAsset<TType>(
            string relativePath,
            Action<TType> configure = null
        )
            where TType : ScriptableObject
        {
            string fullPath = ToFullResourcePath(relativePath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                directory = directory.Replace("\\", "/");
                EnsureFolderStatic(directory);
            }

            DeleteAssetIfExists(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                EnsureFolderStatic(directory);
            }

            TType instance = ScriptableObject.CreateInstance<TType>(); // UNH-SUPPRESS: UNH002 - Asset managed by test cleanup
            instance.name = Path.GetFileNameWithoutExtension(fullPath);
            configure?.Invoke(instance);

            AssetDatabase.CreateAsset(instance, fullPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            CreatedAssetPaths.Add(fullPath);
            return instance;
        }

        private static void PruneResourceFoldersForPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(assetPath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                directory = directory.Replace("\\", "/");
                if (!directory.StartsWith(ResourcesRoot, StringComparison.Ordinal))
                {
                    break;
                }

                if (!string.Equals(directory, ResourcesRoot, StringComparison.Ordinal))
                {
                    DeleteFolderIfEmpty(directory);
                }

                directory = Path.GetDirectoryName(directory);
            }
        }

        private static TType CreateInMemoryInstance<TType>()
            where TType : ScriptableObject
        {
            TType instance = ScriptableObject.CreateInstance<TType>(); // UNH-SUPPRESS: UNH002 - In-memory instance managed by InMemoryInstances
            instance.name = typeof(TType).Name;
            instance.hideFlags = HideFlags.DontSave;
            InMemoryInstances.Add(instance);
            return instance;
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            yield return base.UnityTearDown();

            // Batch all asset cleanup operations to minimize AssetDatabase.Refresh calls
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                // Delete any assets created during SetUp
                foreach (string path in CreatedAssetPaths)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        DeleteAssetIfExists(path);
                    }
                }
                CreatedAssetPaths.Clear();

                DeleteFolderIfEmpty("Assets/Resources/CustomPath");
                AssetDatabase.SaveAssets();
            }

            yield return null;

            // Destroy any in-memory instances created as a fallback
            // These are in-memory operations, no batching needed
            foreach (ScriptableObject obj in InMemoryInstances)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj); // UNH-SUPPRESS: UNH001 - Test cleanup for in-memory instances
                }
            }
            InMemoryInstances.Clear();

            // Prefer public API surface over reflection to clean up the cached instance
            if (TestSingleton.HasInstance)
            {
                TestSingleton.Instance.Destroy();
                TestSingleton.ClearInstance();
            }

            if (ResourceBackedSingleton.HasInstance)
            {
                ResourceBackedSingleton.Instance.Destroy();
                ResourceBackedSingleton.ClearInstance();
            }

            if (DeepPathResourceSingleton.HasInstance)
            {
                DeepPathResourceSingleton.Instance.Destroy();
                DeepPathResourceSingleton.ClearInstance();
            }

            if (WrongPathFallbackSingleton.HasInstance)
            {
                WrongPathFallbackSingleton.Instance.Destroy();
                WrongPathFallbackSingleton.ClearInstance();
            }

            if (MultiAssetScriptableSingleton.HasInstance)
            {
                MultiAssetScriptableSingleton.Instance.Destroy();
                MultiAssetScriptableSingleton.ClearInstance();
            }

            if (LifecycleScriptableSingleton.HasInstance)
            {
                LifecycleScriptableSingleton.Instance.Destroy();
                LifecycleScriptableSingleton.ClearInstance();
            }

            if (SingleLevelPathSingleton.HasInstance)
            {
                SingleLevelPathSingleton.Instance.Destroy();
                SingleLevelPathSingleton.ClearInstance();
            }

            yield return null;

            // Destroy all singleton instances found via FindObjectsOfTypeAll
            // These are in-memory operations, no batching needed
            TestSingleton[] allTestSingletons = Resources.FindObjectsOfTypeAll<TestSingleton>();
            foreach (TestSingleton singleton in allTestSingletons)
            {
                singleton.Destroy();
            }

            CustomPathSingleton[] allCustomPathSingletons =
                Resources.FindObjectsOfTypeAll<CustomPathSingleton>();
            foreach (CustomPathSingleton singleton in allCustomPathSingletons)
            {
                singleton.Destroy();
            }

            EmptyPathSingleton[] allEmptyPathSingletons =
                Resources.FindObjectsOfTypeAll<EmptyPathSingleton>();
            foreach (EmptyPathSingleton singleton in allEmptyPathSingletons)
            {
                singleton.Destroy();
            }

            MultipleInstancesSingleton[] allMultipleSingletons =
                Resources.FindObjectsOfTypeAll<MultipleInstancesSingleton>();
            foreach (MultipleInstancesSingleton singleton in allMultipleSingletons)
            {
                singleton.Destroy();
            }

            ResourceBackedSingleton[] resourceBacked =
                Resources.FindObjectsOfTypeAll<ResourceBackedSingleton>();
            foreach (ResourceBackedSingleton singleton in resourceBacked)
            {
                singleton.Destroy();
            }

            DeepPathResourceSingleton[] deepPath =
                Resources.FindObjectsOfTypeAll<DeepPathResourceSingleton>();
            foreach (DeepPathResourceSingleton singleton in deepPath)
            {
                singleton.Destroy();
            }

            WrongPathFallbackSingleton[] wrongPath =
                Resources.FindObjectsOfTypeAll<WrongPathFallbackSingleton>();
            foreach (WrongPathFallbackSingleton singleton in wrongPath)
            {
                singleton.Destroy();
            }

            MultiAssetScriptableSingleton[] multiAsset =
                Resources.FindObjectsOfTypeAll<MultiAssetScriptableSingleton>();
            foreach (MultiAssetScriptableSingleton singleton in multiAsset)
            {
                singleton.Destroy();
            }

            LifecycleScriptableSingleton[] lifecycle =
                Resources.FindObjectsOfTypeAll<LifecycleScriptableSingleton>();
            foreach (LifecycleScriptableSingleton singleton in lifecycle)
            {
                singleton.Destroy();
            }
            LifecycleScriptableSingleton.ClearedCount = 0;

            MissingResourceSingleton[] missing =
                Resources.FindObjectsOfTypeAll<MissingResourceSingleton>();
            foreach (MissingResourceSingleton singleton in missing)
            {
                singleton.Destroy();
            }

            SingleLevelPathSingleton[] singleLevel =
                Resources.FindObjectsOfTypeAll<SingleLevelPathSingleton>();
            foreach (SingleLevelPathSingleton singleton in singleLevel)
            {
                singleton.Destroy();
            }

            yield return null;
            yield return CleanupTestFolders();
            EditorUi.Suppress = _previousEditorUiSuppress;
        }

        private IEnumerator CleanupTestFolders()
        {
            // Batch all folder cleanup operations to minimize AssetDatabase.Refresh calls
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                string[] testFolders = new[]
                {
                    ResourcesRoot + "/Deep/Nested/Singletons",
                    ResourcesRoot + "/Deep/Nested",
                    ResourcesRoot + "/Deep",
                    ResourcesRoot + "/Missing/Subfolder",
                    ResourcesRoot + "/Missing",
                    ResourcesRoot + "/Loose",
                    ResourcesRoot + "/Multi",
                    ResourcesRoot + "/Lifecycle",
                    ResourcesRoot + "/MultiNatural",
                    ResourcesRoot + "/SingleLevel",
                };

                foreach (string folder in testFolders)
                {
                    DeleteFolderIfEmpty(folder);
                }

                // Also clean up duplicates that may have been created
                // Note: CleanupAllKnownTestFolders already batches internally when not in a batch
                CleanupAllKnownTestFolders();

                DeleteFolderIfEmpty(ResourcesRoot);
            }

            yield return null;
        }

        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();
            // Final cleanup of all test folders
            CleanupAllKnownTestFolders();
        }

        [UnityTest]
        public IEnumerator HasInstanceReturnsFalseBeforeAccess()
        {
            Assert.IsFalse(TestSingleton.HasInstance);
            yield break;
        }

        [UnityTest]
        public IEnumerator HasInstanceReturnsTrueAfterAccess()
        {
            TestSingleton instance = TestSingleton.Instance;

            Assert.IsTrue(TestSingleton.HasInstance);
            Assert.IsTrue(instance != null);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceReturnsNonNull()
        {
            TestSingleton instance = TestSingleton.Instance;
            Assert.IsTrue(instance != null);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceReturnsSameObjectOnMultipleAccesses()
        {
            TestSingleton instance1 = TestSingleton.Instance;
            TestSingleton instance2 = TestSingleton.Instance;

            Assert.AreSame(instance1, instance2);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceIsScriptableObject()
        {
            TestSingleton instance = TestSingleton.Instance;

            Assert.IsInstanceOf<ScriptableObject>(instance);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstancePreservesData()
        {
            TestSingleton instance = TestSingleton.Instance;

            Assert.AreEqual(42, instance.testValue);
            yield break;
        }

        [UnityTest]
        public IEnumerator LazyInstanceIsLazy()
        {
            Assert.IsFalse(TestSingleton._lazyInstance.IsValueCreated);

            TestSingleton instance = TestSingleton.Instance;

            Assert.IsTrue(TestSingleton._lazyInstance.IsValueCreated);
            Assert.IsTrue(instance != null);
            yield break;
        }

        [UnityTest]
        public IEnumerator CustomPathAttributeIsRespected()
        {
            CustomPathSingleton instance = CustomPathSingleton.Instance;

            Assert.IsTrue(instance != null);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmptyPathFallsBackToTypeName()
        {
            EmptyPathSingleton instance = EmptyPathSingleton.Instance;
            Assert.IsTrue(instance != null);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceCanAccessPublicFields()
        {
            TestSingleton instance = TestSingleton.Instance;
            instance.testValue = 99;

            Assert.AreEqual(99, instance.testValue);
            yield break;
        }

        [UnityTest]
        public IEnumerator MultipleTypesHaveIndependentInstances()
        {
            TestSingleton instance1 = TestSingleton.Instance;
            CustomPathSingleton instance2 = CustomPathSingleton.Instance;

            Assert.IsTrue(instance1 != null);
            Assert.IsTrue(instance2 != null);
            Assert.AreNotSame(instance1, instance2);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstancePersistsAcrossAccesses()
        {
            TestSingleton instance = TestSingleton.Instance;
            instance.testValue = 123;

            TestSingleton sameInstance = TestSingleton.Instance;

            Assert.AreEqual(123, sameInstance.testValue);
            yield break;
        }

        [UnityTest]
        public IEnumerator HasInstanceDoesNotTriggerCreation()
        {
            bool hasInstance = TestSingleton.HasInstance;

            Assert.IsFalse(hasInstance);
            Assert.IsFalse(TestSingleton._lazyInstance.IsValueCreated);
            yield break;
        }

        [UnityTest]
        public IEnumerator LazyInstanceValueMatchesInstance()
        {
            TestSingleton instance = TestSingleton.Instance;
            TestSingleton lazyValue = TestSingleton._lazyInstance.Value;

            Assert.AreSame(instance, lazyValue);
            yield break;
        }

        [UnityTest]
        public IEnumerator ScriptableSingletonPathAttributeCanBeNull()
        {
            EmptyPathSingleton instance = EmptyPathSingleton.Instance;

            Assert.IsTrue(instance != null);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceCanBeUsedInCollections()
        {
            TestSingleton instance = TestSingleton.Instance;
            System.Collections.Generic.List<TestSingleton> list = new() { instance };

            Assert.AreEqual(1, list.Count);
            Assert.Contains(instance, list);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceHasCorrectTypeName()
        {
            TestSingleton instance = TestSingleton.Instance;

            Assert.AreEqual(typeof(TestSingleton), instance.GetType());
            yield break;
        }

        [UnityTest]
        public IEnumerator MultipleAccessesDoNotCreateMultipleInstances()
        {
            TestSingleton instance1 = TestSingleton.Instance;
            TestSingleton instance2 = TestSingleton.Instance;
            TestSingleton instance3 = TestSingleton.Instance;

            Assert.AreSame(instance1, instance2);
            Assert.AreSame(instance2, instance3);
            yield break;
        }

        [UnityTest]
        public IEnumerator CanAccessInstanceAfterHasInstanceCheck()
        {
            yield return null;
            bool hasInstance = TestSingleton.HasInstance;
            Assert.IsFalse(hasInstance);

            yield return null;
            _ = TestSingleton.Instance;
            Assert.IsTrue(TestSingleton.HasInstance);
            yield return null;
            Assert.IsTrue(TestSingleton.Instance != null);

            yield return null;
            hasInstance = TestSingleton.HasInstance;
            Assert.IsTrue(hasInstance);
        }

        [UnityTest]
        public IEnumerator InstanceWorksWithInheritance()
        {
            TestSingleton instance = TestSingleton.Instance;

            Assert.IsInstanceOf<ScriptableObjectSingleton<TestSingleton>>(instance);
            Assert.IsInstanceOf<ScriptableObject>(instance);
            yield break;
        }

        [UnityTest]
        public IEnumerator LazyInstanceDoesNotChangeAfterCreation()
        {
            Lazy<TestSingleton> lazy1 = TestSingleton._lazyInstance;
            TestSingleton instance = TestSingleton.Instance;
            Lazy<TestSingleton> lazy2 = TestSingleton._lazyInstance;

            Assert.AreSame(lazy1, lazy2);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceCanBeCompared()
        {
            TestSingleton instance1 = TestSingleton.Instance;
            TestSingleton instance2 = TestSingleton.Instance;

            Assert.IsTrue(instance1 == instance2);
            Assert.IsFalse(instance1 != instance2);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceHasConsistentHashCode()
        {
            TestSingleton instance1 = TestSingleton.Instance;
            int hash1 = instance1.GetHashCode();

            TestSingleton instance2 = TestSingleton.Instance;
            int hash2 = instance2.GetHashCode();

            Assert.AreEqual(hash1, hash2);
            yield break;
        }

        [UnityTest]
        public IEnumerator InstanceToStringReturnsTypeName()
        {
            TestSingleton instance = TestSingleton.Instance;
            string result = instance.ToString();

            Assert.IsTrue(result != null, "ToString result should not be null");
            Assert.IsTrue(result.Contains("TestSingleton") || result.Length > 0);
            yield break;
        }

        [UnityTest]
        public IEnumerator MissingAssetInstanceReturnsNull()
        {
            MissingResourceSingleton.ClearInstance();
            yield return null;

            MissingResourceSingleton instance = MissingResourceSingleton.Instance;

            Assert.IsTrue(instance == null, "Instance should be null when resource is missing");
            Assert.IsFalse(MissingResourceSingleton.HasInstance);
            Assert.IsTrue(MissingResourceSingleton._lazyInstance.IsValueCreated);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearInstanceHandlesMissingAsset()
        {
            MissingResourceSingleton.ClearInstance();
            yield return null;

            _ = MissingResourceSingleton.Instance;
            yield return null;

            Assert.DoesNotThrow(() => MissingResourceSingleton.ClearInstance());
            yield return null;

            Assert.IsFalse(MissingResourceSingleton.HasInstance);
            Assert.IsFalse(MissingResourceSingleton._lazyInstance.IsValueCreated);
            yield break;
        }

        [UnityTest]
        public IEnumerator LoadsInstanceFromResourcesAsset()
        {
            CreateResourceAsset<ResourceBackedSingleton>(
                "ResourceBackedSingleton.asset",
                asset => asset.payload = "from-resource"
            );
            ResourceBackedSingleton.ClearInstance();
            yield return null;

            ResourceBackedSingleton instance = ResourceBackedSingleton.Instance;

            Assert.IsTrue(instance != null);
            Assert.AreEqual("from-resource", instance.payload);
            yield break;
        }

        [UnityTest]
        public IEnumerator CustomPathResourcesAssetIsLoaded()
        {
            CreateResourceAsset<DeepPathResourceSingleton>(
                "Deep/Nested/Singletons/DeepPathResourceSingleton.asset",
                asset => asset.payload = "deep-path"
            );
            DeepPathResourceSingleton.ClearInstance();
            yield return null;

            DeepPathResourceSingleton instance = DeepPathResourceSingleton.Instance;

            Assert.IsTrue(instance != null);
            Assert.AreEqual("deep-path", instance.payload);
            StringAssert.Contains("Deep/Nested/Singletons", AssetDatabase.GetAssetPath(instance));
            yield break;
        }

        [UnityTest]
        public IEnumerator AttributePathFallbacksWhenAssetStoredElsewhere()
        {
            CreateResourceAsset<WrongPathFallbackSingleton>(
                "Loose/WrongPathInstance.asset",
                asset =>
                {
                    asset.payload = "relocated";
                    asset.name = "RenamedWrongPath";
                }
            );
            WrongPathFallbackSingleton.ClearInstance();
            yield return null;

            WrongPathFallbackSingleton instance = WrongPathFallbackSingleton.Instance;

            Assert.IsTrue(instance != null);
            Assert.AreEqual("relocated", instance.payload);
            StringAssert.Contains(
                "Loose/WrongPathInstance.asset",
                AssetDatabase.GetAssetPath(instance)
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator MetadataAssetTracksSingletonEntries(
            [ValueSource(nameof(MetadataEntryScenarios))] MetadataScenario scenario
        )
        {
            scenario.CreateAsset();
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            using (
                SingletonCreatorTestScope scope = SingletonCreatorTestScope.RestrictTo(
                    scenario.SingletonType
                )
            )
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            }
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            ScriptableObjectSingletonMetadata metadata =
                AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                    ScriptableObjectSingletonMetadata.AssetPath
                );

            bool metadataFileExists = File.Exists(ScriptableObjectSingletonMetadata.AssetPath);
            string diagnosticInfo =
                $"Scenario: {scenario.Description}, "
                + $"Type: {scenario.SingletonType.FullName}, "
                + $"ExpectedPath: {scenario.ExpectedLoadPath}, "
                + $"MetadataPath: {ScriptableObjectSingletonMetadata.AssetPath}, "
                + $"FileExists: {metadataFileExists}";

            Assert.IsTrue(
                metadata != null,
                $"Metadata asset missing for {scenario.Description}. Diagnostics: {diagnosticInfo}"
            );

            Assert.IsTrue(
                metadata.TryGetEntry(
                    scenario.SingletonType,
                    out ScriptableObjectSingletonMetadata.Entry entry
                ),
                $"Metadata entry missing for {scenario.SingletonType.Name} ({scenario.Description}). Diagnostics: {diagnosticInfo}"
            );

            StringAssert.AreEqualIgnoringCase(
                scenario.ExpectedLoadPath,
                entry.resourcesLoadPath,
                $"resourcesLoadPath mismatch for {scenario.Description}. {diagnosticInfo}. "
                    + $"Actual resourcesLoadPath: '{entry.resourcesLoadPath}', "
                    + $"Actual resourcesPath: '{entry.resourcesPath}'"
            );

            string expectedFolder = scenario.ExpectedFolder?.Replace("\\", "/") ?? string.Empty;
            string actualFolder = (entry.resourcesPath ?? string.Empty).Replace("\\", "/");
            if (string.IsNullOrEmpty(expectedFolder))
            {
                Assert.IsTrue(
                    string.IsNullOrEmpty(actualFolder),
                    $"Expected metadata to store empty folder for {scenario.Description}"
                );
            }
            else
            {
                Assert.IsFalse(
                    string.IsNullOrEmpty(actualFolder),
                    $"Metadata missing folder for {scenario.Description}"
                );
                StringAssert.AreEqualIgnoringCase(
                    expectedFolder,
                    actualFolder,
                    $"resourcesPath mismatch for {scenario.Description}"
                );
            }
        }

        [Test]
        public void MetadataEntryUpdateReturnsFalseWhenEntryIsUnchanged()
        {
            ScriptableObjectSingletonMetadata metadata = Track(
                ScriptableObject.CreateInstance<ScriptableObjectSingletonMetadata>()
            );
            ScriptableObjectSingletonMetadata.Entry entry = new()
            {
                assemblyQualifiedTypeName = typeof(TestSingleton).AssemblyQualifiedName,
                resourcesLoadPath = "TestSingleton",
                resourcesPath = string.Empty,
                assetGuid = "guid-a",
            };
            ScriptableObjectSingletonMetadata.Entry updatedEntry = new()
            {
                assemblyQualifiedTypeName = typeof(TestSingleton).AssemblyQualifiedName,
                resourcesLoadPath = "Other/TestSingleton",
                resourcesPath = "Other",
                assetGuid = "guid-b",
            };

            bool firstChanged = metadata.SetOrUpdateEntry(entry);
            bool secondChanged = metadata.SetOrUpdateEntry(entry);
            bool thirdChanged = metadata.SetOrUpdateEntry(updatedEntry);

            Assert.IsTrue(firstChanged);
            Assert.IsFalse(secondChanged);
            Assert.IsTrue(thirdChanged);
            Assert.IsTrue(metadata.TryGetEntry(typeof(TestSingleton), out _));
        }

        [UnityTest]
        public IEnumerator MultipleAssetsLogWarningAndSelectDeterministically()
        {
            CreateResourceAsset<MultiAssetScriptableSingleton>(
                "Multi/Primary.asset",
                asset =>
                {
                    asset.name = "01_Primary";
                    asset.id = "primary";
                }
            );
            CreateResourceAsset<MultiAssetScriptableSingleton>(
                "Multi/Secondary.asset",
                asset =>
                {
                    asset.name = "02_Secondary";
                    asset.id = "secondary";
                }
            );
            MultiAssetScriptableSingleton.ClearInstance();
            yield return null;

            LogAssert.Expect(
                LogType.Warning,
                new Regex("Found multiple ScriptableSingletons", RegexOptions.IgnoreCase)
            );

            MultiAssetScriptableSingleton instance = MultiAssetScriptableSingleton.Instance;

            Assert.AreEqual("primary", instance.id);
            yield break;
        }

        [UnityTest]
        public IEnumerator MultipleAssetsUseNaturalNumericOrdering()
        {
            CreateResourceAsset<MultiAssetScriptableSingleton>(
                "MultiNatural/Entry10.asset",
                asset =>
                {
                    asset.name = "Entry10";
                    asset.id = "ten";
                }
            );
            CreateResourceAsset<MultiAssetScriptableSingleton>(
                "MultiNatural/Entry2.asset",
                asset =>
                {
                    asset.name = "Entry2";
                    asset.id = "two";
                }
            );
            CreateResourceAsset<MultiAssetScriptableSingleton>(
                "MultiNatural/Entry11.asset",
                asset =>
                {
                    asset.name = "Entry11";
                    asset.id = "eleven";
                }
            );
            MultiAssetScriptableSingleton.ClearInstance();
            yield return null;

            LogAssert.Expect(
                LogType.Warning,
                new Regex("Found multiple ScriptableSingletons", RegexOptions.IgnoreCase)
            );

            MultiAssetScriptableSingleton instance = MultiAssetScriptableSingleton.Instance;

            Assert.AreEqual("two", instance.id);
            yield break;
        }

        [UnityTest]
        public IEnumerator MetadataLoadPathChoosesCanonicalAssetWhenFolderContainsDuplicates()
        {
            CreateResourceAsset<DeepPathResourceSingleton>(
                "Deep/Nested/Singletons/DeepPathResourceSingleton.asset",
                asset => asset.payload = "canonical"
            );
            CreateResourceAsset<DeepPathResourceSingleton>(
                "Deep/Nested/Singletons/00_Duplicate.asset",
                asset => asset.payload = "duplicate"
            );

            using (SingletonCreatorTestScope.RestrictTo(typeof(DeepPathResourceSingleton)))
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            }

            DeepPathResourceSingleton.ClearInstance();
            yield return null;

            DeepPathResourceSingleton instance = DeepPathResourceSingleton.Instance;

            Assert.AreEqual("canonical", instance.payload);
            StringAssert.Contains(
                "Deep/Nested/Singletons/DeepPathResourceSingleton.asset",
                AssetDatabase.GetAssetPath(instance)
            );
        }

        public readonly struct MetadataScenario
        {
            private readonly Action _createAsset;

            public MetadataScenario(
                string description,
                Action createAsset,
                Type singletonType,
                string expectedLoadPath,
                string expectedFolder
            )
            {
                if (createAsset == null)
                {
                    throw new ArgumentNullException(nameof(createAsset));
                }

                if (singletonType == null)
                {
                    throw new ArgumentNullException(nameof(singletonType));
                }

                Description = description ?? string.Empty;
                _createAsset = createAsset;
                SingletonType = singletonType;
                ExpectedLoadPath = expectedLoadPath ?? string.Empty;
                ExpectedFolder = expectedFolder ?? string.Empty;
            }

            public string Description { get; }
            public Type SingletonType { get; }
            public string ExpectedLoadPath { get; }
            public string ExpectedFolder { get; }

            public void CreateAsset()
            {
                _createAsset();
            }
        }

        private static System.Collections.Generic.IEnumerable<MetadataScenario> MetadataEntryScenarios()
        {
            yield return new MetadataScenario(
                "default resources asset uses type name",
                () =>
                    CreateResourceAsset<TestSingleton>(
                        "TestSingleton.asset",
                        asset => asset.payload = "metadata"
                    ),
                typeof(TestSingleton),
                "TestSingleton",
                string.Empty
            );

            yield return new MetadataScenario(
                "custom resources folder is stored",
                () =>
                    CreateResourceAsset<DeepPathResourceSingleton>(
                        "Deep/Nested/Singletons/DeepPathResourceSingleton.asset",
                        asset => asset.payload = "deep"
                    ),
                typeof(DeepPathResourceSingleton),
                "Deep/Nested/Singletons/DeepPathResourceSingleton",
                "Deep/Nested/Singletons"
            );

            yield return new MetadataScenario(
                "singleton at root resources folder",
                () =>
                    CreateResourceAsset<ResourceBackedSingleton>(
                        "ResourceBackedSingleton.asset",
                        asset => asset.payload = "root-level"
                    ),
                typeof(ResourceBackedSingleton),
                "ResourceBackedSingleton",
                string.Empty
            );

            yield return new MetadataScenario(
                "singleton with single subfolder",
                () =>
                    CreateResourceAsset<SingleLevelPathSingleton>(
                        "SingleLevel/SingleLevelPathSingleton.asset",
                        asset => asset.flag = true
                    ),
                typeof(SingleLevelPathSingleton),
                "SingleLevel/SingleLevelPathSingleton",
                "SingleLevel"
            );

            yield return new MetadataScenario(
                "singleton with custom path attribute",
                () =>
                    CreateResourceAsset<CustomPathSingleton>(
                        "CustomPath/CustomPathSingleton.asset",
                        asset => asset.customData = "metadata-test"
                    ),
                typeof(CustomPathSingleton),
                "CustomPath/CustomPathSingleton",
                "CustomPath"
            );
        }

        private sealed class SingletonCreatorTestScope : IDisposable
        {
            private readonly bool _previousIncludeTests;
            private readonly bool _previousIgnoreExclusion;
            private readonly bool _previousAllowAssetCreation;
            private readonly bool _previousIgnoreCompilationState;
            private readonly Func<Type, bool> _previousFilter;

            private SingletonCreatorTestScope(Type[] allowedTypes)
            {
                if (allowedTypes == null || allowedTypes.Length == 0)
                {
                    throw new ArgumentException(
                        "allowedTypes must contain at least one type.",
                        nameof(allowedTypes)
                    );
                }

                // Ensure the metadata folder exists to prevent modal dialogs
                EnsureMetadataFolder();

                System.Collections.Generic.HashSet<Type> allowed = new(allowedTypes);
                _previousIncludeTests = ScriptableObjectSingletonCreator.IncludeTestAssemblies;
                _previousIgnoreExclusion =
                    ScriptableObjectSingletonCreator.IgnoreExclusionAttribute;
                _previousAllowAssetCreation =
                    ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression;
                _previousIgnoreCompilationState =
                    ScriptableObjectSingletonCreator.IgnoreCompilationState;
                _previousFilter = ScriptableObjectSingletonCreator.TypeFilter;
                ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
                ScriptableObjectSingletonCreator.IgnoreExclusionAttribute = true;
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
                // Bypass compilation state check - Unity may report isCompiling/isUpdating
                // as true during test runs after AssetDatabase operations
                ScriptableObjectSingletonCreator.IgnoreCompilationState = true;
                ScriptableObjectSingletonCreator.TypeFilter = type =>
                {
                    if (!allowed.Contains(type))
                    {
                        return false;
                    }

                    return _previousFilter == null || _previousFilter(type);
                };
            }

            public static SingletonCreatorTestScope RestrictTo(params Type[] allowedTypes)
            {
                return new SingletonCreatorTestScope(allowedTypes);
            }

            public void Dispose()
            {
                ScriptableObjectSingletonCreator.TypeFilter = _previousFilter;
                ScriptableObjectSingletonCreator.IncludeTestAssemblies = _previousIncludeTests;
                ScriptableObjectSingletonCreator.IgnoreExclusionAttribute =
                    _previousIgnoreExclusion;
                ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression =
                    _previousAllowAssetCreation;
                ScriptableObjectSingletonCreator.IgnoreCompilationState =
                    _previousIgnoreCompilationState;
            }

            private static void EnsureMetadataFolder()
            {
                const string folderPath = "Assets/Resources/Wallstop Studios/Unity Helpers";
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    string absoluteDirectory = Path.Combine(projectRoot, folderPath);
                    if (!Directory.Exists(absoluteDirectory))
                    {
                        Directory.CreateDirectory(absoluteDirectory);
                    }
                }

                if (AssetDatabase.IsValidFolder(folderPath))
                {
                    return;
                }

                string[] parts = folderPath.Split('/');
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
        }

        [UnityTest]
        public IEnumerator ClearInstanceReloadsLatestAssetState()
        {
            CreateResourceAsset<ResourceBackedSingleton>(
                "ResourceBackedSingleton.asset",
                asset => asset.payload = "first"
            );
            ResourceBackedSingleton.ClearInstance();
            yield return null;

            ResourceBackedSingleton first = ResourceBackedSingleton.Instance;
            Assert.AreEqual("first", first.payload);

            DeleteAssetIfExists(ToFullResourcePath("ResourceBackedSingleton.asset"));
            yield return null;

            CreateResourceAsset<ResourceBackedSingleton>(
                "ResourceBackedSingleton.asset",
                asset => asset.payload = "second"
            );
            ResourceBackedSingleton.ClearInstance();
            yield return null;

            ResourceBackedSingleton second = ResourceBackedSingleton.Instance;
            Assert.AreEqual("second", second.payload);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearInstanceDestroysLoadedAsset()
        {
            LifecycleScriptableSingleton.ClearedCount = 0;
            CreateResourceAsset<LifecycleScriptableSingleton>(
                "Lifecycle/LifecycleScriptableSingleton.asset"
            );
            LifecycleScriptableSingleton.ClearInstance();
            yield return null;

            LifecycleScriptableSingleton instance = LifecycleScriptableSingleton.Instance;

            Assert.IsTrue(instance != null);
            Assert.AreEqual(0, LifecycleScriptableSingleton.ClearedCount);

            LifecycleScriptableSingleton.ClearInstance();
            yield return null;

            Assert.IsFalse(LifecycleScriptableSingleton.HasInstance);
            Assert.IsFalse(LifecycleScriptableSingleton._lazyInstance.IsValueCreated);
            Assert.GreaterOrEqual(LifecycleScriptableSingleton.ClearedCount, 1);
            yield break;
        }

        [UnityTest]
        public IEnumerator LoadsInstanceFromInMemoryWhenAssetMissing()
        {
            string path = "Assets/Resources/TestSingleton.asset";
            Object existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            }

            TestSingleton.ClearInstance();

            TestSingleton created = ScriptableObject.CreateInstance<TestSingleton>(); // UNH-SUPPRESS: UNH002 - In-memory instance managed by InMemoryInstances
            created.hideFlags = HideFlags.DontSave;
            InMemoryInstances.Add(created);

            TestSingleton resolved = TestSingleton.Instance;

            Assert.IsTrue(resolved != null);
            Assert.AreSame(created, resolved);
            yield break;
        }

        [UnityTest]
        public IEnumerator OffThreadCreationThrowsDescriptiveException()
        {
            TestSingleton.ClearInstance();
            yield return null;

            Task task = Task.Run(() =>
            {
                _ = TestSingleton.Instance;
            });

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsFaulted);
            AggregateException aggregate = task.Exception;
            Assert.IsTrue(aggregate != null, "Task exception should not be null for faulted task");
            AggregateException flattened = aggregate.Flatten();
            Assert.IsTrue(flattened.InnerExceptions.Count > 0);
            InvalidOperationException exception =
                flattened.InnerExceptions[0] as InvalidOperationException;
            Assert.IsTrue(exception != null, "Inner exception should be InvalidOperationException");
            StringAssert.Contains("main thread", exception.Message);
            Assert.IsFalse(TestSingleton.HasInstance);
        }

        [UnityTest]
        public IEnumerator BackgroundThreadCanReadInstanceAfterCreation()
        {
            TestSingleton instance = TestSingleton.Instance;

            Task<TestSingleton> task = Task.Run(() => TestSingleton.Instance);

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsFalse(task.IsFaulted);
            Assert.AreSame(instance, task.Result);
        }
    }
#endif
}
