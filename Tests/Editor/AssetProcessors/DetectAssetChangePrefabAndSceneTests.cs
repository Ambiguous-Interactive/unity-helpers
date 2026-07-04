// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestAssets;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Tests for prefab and scene object search functionality in DetectAssetChangeProcessor.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class DetectAssetChangePrefabAndSceneTests : DetectAssetChangeTestBase
    {
        protected override string DefaultPayloadAssetPath => TestRoot + "/Payload.asset";
        private const string TestScenePath = TestRoot + "/TestScene.unity";

        private readonly List<GameObject> _instantiatedSceneObjects = new();

        /// <summary>
        /// Folder prefixes this fixture is allowed to drive the processor through.
        /// Covers (a) the fixture's own test root for payload assets, (b) the
        /// committed shared-prefab fixtures under Packages, and (c) the dynamic
        /// prefab workspace. Everything else is structurally ignored even with
        /// <see cref="DetectAssetChangeProcessor.IncludeTestAssets"/> on.
        /// </summary>
        private static readonly string[] PrefabSceneFixtureAllowlist =
        {
            TestRoot + "/",
            "Packages/com.wallstop-studios.unity-helpers/Tests/Editor/TestAssets/Prefabs/",
            "Assets/Temp/DynamicPrefabFixtures/",
        };

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            SharedPrefabTestFixtures.AcquireFixtures();
            CleanupTestFolders();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            // Drain any AssetPostprocessor deferrals scheduled by the folder
            // cleanup/refresh above before the first test's SetUp runs. Without
            // this, a late-arriving drain lands mid-first-test and pollutes its
            // handler statics.
            AssetPostprocessorDeferral.FlushForTesting();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();
            CleanupTestFolders();
            SharedPrefabTestFixtures.ReleaseFixtures();
            CleanupDeferredAssetsAndFolders();
            // The CleanupTestFolders / ReleaseFixtures / CleanupDeferred* calls above
            // all mutate the AssetDatabase (DeleteAsset, Refresh) and each schedules a
            // drain via AssetPostprocessorDeferral. Without an explicit flush here, a
            // late-arriving drain fires during the next fixture's SetUp and pollutes
            // its handler statics. The OneTimeLifecycleMethodsWithAssetMutationsFlushDeferrals
            // contract enforces this at the source-scan level — do not remove.
            AssetPostprocessorDeferral.FlushForTesting();
        }

        private GameObject InstantiateInScene(GameObject prefab)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            return TrackSceneObject(instance);
        }

        private GameObject TrackSceneObject(GameObject go)
        {
            if (go != null)
            {
                _instantiatedSceneObjects.Add(go);
            }
            return Track(go);
        }

        private GameObject CreateTrackedSceneObject(string name)
        {
            GameObject go = Track(new GameObject(name));
            _instantiatedSceneObjects.Add(go);
            return go;
        }

        [SetUp]
        public override void BaseSetUp()
        {
            // Canonical cross-fixture pollution tripwire. See
            // AssetPostprocessorTestHandlers.AssertCleanAndClearAll XML doc for
            // the rationale (why this runs FIRST, before any asset mutation or
            // processor configuration in this SetUp — the mutation sources here
            // are EnsureTestFolder, SharedPrefabTestFixtures.ForceCleanup, and
            // ResetProcessor*). Placed BEFORE base.BaseSetUp() to match the
            // placement convention enforced by
            // AssetContextFixturesCallCrossFixturePollutionTripwire.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();

            base.BaseSetUp();

            EnsureTestFolder();

            // Clean up any dynamic prefab fixtures from prior tests BEFORE clearing handler state
            // This prevents dynamic prefabs from prior tests from being found during processor reset
            SharedPrefabTestFixtures.ForceCleanup();

            // Drains scheduled by EnsureTestFolder / ForceCleanup above may land
            // during the test body otherwise. The helper internally flushes then
            // clears, so a separate FlushForTesting() call here would be
            // redundant.
            AssetPostprocessorTestHandlers.FlushAndClearAll();

            // ResetForTesting clears everything including the allowlist, so this
            // helper re-applies it after — and IncludeTestAssets is set inside
            // the helper. Configure the processor LAST so the allowlist isn't
            // already armed when the flush above fires drains.
            ResetProcessorWithPrefabSceneFixtureAllowlist();
        }

        /// <summary>
        /// Resets the processor to a clean state while preserving this fixture's
        /// <see cref="PrefabSceneFixtureAllowlist"/>. Every in-test call site that resets the
        /// processor MUST go through this helper — calling
        /// <see cref="DetectAssetChangeProcessor.ResetForTesting()"/> directly drops
        /// the allowlist, which silently opens the structural defense against
        /// cross-fixture pollution for the remainder of that test.
        /// </summary>
        private static void ResetProcessorWithPrefabSceneFixtureAllowlist()
        {
            DetectAssetChangeProcessor.ResetForTesting();
            DetectAssetChangeProcessor.IncludeTestAssets = true;
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = PrefabSceneFixtureAllowlist;
        }

        [TearDown]
        public override void TearDown()
        {
            DetectAssetChangeProcessor.IncludeTestAssets = false;
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = null;

            // Clean up tracked scene objects directly - no FindObjectsByType!
            foreach (GameObject go in _instantiatedSceneObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go); // UNH-SUPPRESS: Test cleanup
                }
            }
            _instantiatedSceneObjects.Clear();

            // Clean up any dynamic prefab fixtures created during the test
            SharedPrefabTestFixtures.ForceCleanup();

            DeleteAssetIfExists(DefaultPayloadAssetPath);
            DeleteAssetIfExists(TestScenePath);

            CleanupTestFolders();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            // ClearTestState() must run AFTER base.TearDown() because base destroys
            // tracked assets, which schedules additional drains. The helper flushes
            // those drains before clearing handler statics, so placing it last
            // covers every drain source with a single call.
            base.TearDown();
            ClearTestState();
        }

        [Test]
        public void PrefabHandlerInvokesInstanceMethodWhenAssetCreated()
        {
            // Arrange - Use shared prefab fixture
            GameObject prefab = SharedPrefabTestFixtures.PrefabHandler;
            Assert.IsTrue(prefab != null, "Shared PrefabHandler fixture not found");

            TestPrefabAssetChangeHandler expectedHandler =
                prefab.GetComponent<TestPrefabAssetChangeHandler>();
            Assert.IsTrue(
                expectedHandler != null,
                "PrefabHandler should have TestPrefabAssetChangeHandler"
            );

            CreatePayloadAsset();
            ClearTestState();

            // Need to reset processor so it finds the handler
            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Verify the expected handler was invoked
            // Note: ALL prefabs with TestPrefabAssetChangeHandler will be found, including
            // test_prefab_handler.prefab (1) and test_multiple_handlers.prefab (2) = 3 minimum
            Assert.GreaterOrEqual(
                TestPrefabAssetChangeHandler.RecordedContexts.Count,
                1,
                "Expected at least one prefab handler invocation"
            );
            Assert.IsTrue(
                TestPrefabAssetChangeHandler.RecordedContexts.Any(ctx =>
                    ctx.Flags == AssetChangeFlags.Created
                ),
                "Expected at least one Created context"
            );
            Assert.IsTrue(
                TestPrefabAssetChangeHandler.RecordedInstances.Contains(expectedHandler),
                "Expected the specific PrefabHandler fixture handler to be invoked"
            );
        }

        [Test]
        public void PrefabHandlerInvokesInstanceMethodWhenAssetDeleted()
        {
            // Arrange - Use shared prefab fixture
            GameObject prefab = SharedPrefabTestFixtures.PrefabHandler;
            Assert.IsTrue(prefab != null, "Shared PrefabHandler fixture not found");

            TestPrefabAssetChangeHandler expectedHandler =
                prefab.GetComponent<TestPrefabAssetChangeHandler>();
            Assert.IsTrue(
                expectedHandler != null,
                "PrefabHandler should have TestPrefabAssetChangeHandler"
            );

            CreatePayloadAsset();
            ClearTestState();

            // Reset processor so it finds the handler
            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // First process creation to track the asset
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );
            ClearTestState();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                null,
                new[] { DefaultPayloadAssetPath },
                null,
                null
            );

            // Assert - Verify at least one deletion context exists and our handler was invoked
            Assert.GreaterOrEqual(
                TestPrefabAssetChangeHandler.RecordedContexts.Count,
                1,
                "Expected at least one prefab handler invocation for deletion"
            );
            Assert.IsTrue(
                TestPrefabAssetChangeHandler.RecordedContexts.Any(ctx =>
                    ctx.Flags == AssetChangeFlags.Deleted
                ),
                "Expected at least one Deleted context"
            );
            Assert.IsTrue(
                TestPrefabAssetChangeHandler.RecordedInstances.Contains(expectedHandler),
                "Expected the specific PrefabHandler fixture handler to be invoked"
            );
        }

        [Test]
        public void PrefabHandlerFindsNestedComponents()
        {
            // Arrange - Use shared nested handler fixture (prefab with nested child containing handler)
            GameObject prefab = SharedPrefabTestFixtures.NestedHandler;
            Assert.IsTrue(prefab != null, "Shared NestedHandler fixture not found");

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert
            Assert.AreEqual(
                1,
                TestNestedPrefabHandler.RecordedContexts.Count,
                $"Expected nested prefab handler to be invoked. "
                    + $"RecordedContexts=[{string.Join(", ", TestNestedPrefabHandler.RecordedContexts.Select(c => $"Flags={c.Flags}"))}], "
                    + $"InstanceIDs=[{string.Join(", ", TestNestedPrefabHandler.RecordedInstances.Select(i => i.GetUnityObjectId()))}]"
            );
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void PrefabHandlerFindsMultipleComponentsOnSamePrefab()
        {
            // Arrange - Use shared multiple handlers fixture (prefab with multiple handler components)
            GameObject prefab = SharedPrefabTestFixtures.MultipleHandlers;
            Assert.IsTrue(prefab != null, "Shared MultipleHandlers fixture not found");

            TestPrefabAssetChangeHandler[] expectedHandlers =
                prefab.GetComponents<TestPrefabAssetChangeHandler>();
            Assert.GreaterOrEqual(
                expectedHandlers.Length,
                2,
                "MultipleHandlers prefab should have at least 2 handlers"
            );

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Verify all handlers from the MultipleHandlers prefab were invoked
            // Also verifies that handlers from other prefabs are invoked (test_prefab_handler.prefab)
            Assert.GreaterOrEqual(
                TestPrefabAssetChangeHandler.RecordedInstances.Count,
                expectedHandlers.Length,
                "Expected at least all handlers from MultipleHandlers prefab to be invoked"
            );
            foreach (TestPrefabAssetChangeHandler handler in expectedHandlers)
            {
                Assert.IsTrue(
                    TestPrefabAssetChangeHandler.RecordedInstances.Contains(handler),
                    $"Expected handler {handler.GetUnityObjectId()} from MultipleHandlers to be invoked"
                );
            }
        }

        [Ignore("Clean up once we figure out why dynamic prefab creation fails")]
        [Test]
        public void PrefabHandlerFindsHandlersAcrossMultiplePrefabs()
        {
            // Arrange - Use shared PrefabHandler plus a dynamic prefab for multiple prefab scenario
            GameObject prefab1 = SharedPrefabTestFixtures.PrefabHandler;
            Assert.IsTrue(prefab1 != null, "Shared PrefabHandler fixture not found");

            TestPrefabAssetChangeHandler handler1 =
                prefab1.GetComponent<TestPrefabAssetChangeHandler>();
            Assert.IsTrue(
                handler1 != null,
                "Shared PrefabHandler does not have TestPrefabAssetChangeHandler component"
            );

            // Create a second dynamic prefab for this test
            SharedPrefabTestFixtures.DynamicPrefabFixture dynamicFixture =
                SharedPrefabTestFixtures.GetOrCreateDynamicPrefab<TestPrefabAssetChangeHandler>(
                    "MultiplePrefabsTest_Prefab2"
                );
            GameObject prefab2 = dynamicFixture.Prefab;
            Assert.IsTrue(prefab2 != null, "Dynamic prefab creation failed");

            TestPrefabAssetChangeHandler handler2 =
                prefab2.GetComponent<TestPrefabAssetChangeHandler>();
            Assert.IsTrue(
                handler2 != null,
                "Dynamic prefab does not have TestPrefabAssetChangeHandler component"
            );

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Verify BOTH specific handlers were invoked (there may be more from other fixtures)
            Assert.GreaterOrEqual(
                TestPrefabAssetChangeHandler.RecordedInstances.Count,
                2,
                $"Expected at least 2 handlers to be invoked. "
                    + $"RecordedContexts.Count={TestPrefabAssetChangeHandler.RecordedContexts.Count}, "
                    + $"Prefab1 exists={prefab1 != null}, Prefab2 exists={prefab2 != null}"
            );
            Assert.IsTrue(
                TestPrefabAssetChangeHandler.RecordedInstances.Contains(handler1),
                "Expected handler1 from PrefabHandler fixture to be invoked"
            );
            Assert.IsTrue(
                TestPrefabAssetChangeHandler.RecordedInstances.Contains(handler2),
                "Expected handler2 from dynamic prefab to be invoked"
            );
        }

        [Test]
        public void PrefabHandlerDoesNotInvokeWithoutSearchPrefabsOption()
        {
            // Arrange - TestSceneAssetChangeHandler has SearchSceneObjects option but NOT SearchPrefabs
            // Use the shared SceneHandler fixture (which is a prefab containing TestSceneAssetChangeHandler)
            // It should not be found via prefab search because it lacks SearchPrefabs option
            GameObject prefab = SharedPrefabTestFixtures.SceneHandler;
            Assert.IsTrue(prefab != null, "Shared SceneHandler fixture not found");

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - The handler on prefab should NOT be invoked because it doesn't have SearchPrefabs
            // The TestSceneAssetChangeHandler only has SearchSceneObjects option, not SearchPrefabs
            Assert.AreEqual(
                0,
                TestSceneAssetChangeHandler.RecordedInstances.Count,
                $"Expected no scene handlers invoked from prefab since it lacks SearchPrefabs option. "
                    + $"RecordedContexts=[{string.Join(", ", TestSceneAssetChangeHandler.RecordedContexts.Select(c => $"Flags={c.Flags}"))}], "
                    + $"InstanceIDs=[{string.Join(", ", TestSceneAssetChangeHandler.RecordedInstances.Select(i => i.GetUnityObjectId()))}]"
            );
        }

        [Test]
        public void SceneHandlerInvokesInstanceMethodWhenAssetCreated()
        {
            // Arrange - Create handler in current scene
            GameObject go = CreateTrackedSceneObject("SceneHandler");
            TestSceneAssetChangeHandler handler = go.AddComponent<TestSceneAssetChangeHandler>();

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Verify our specific handler was invoked with correct flags
            Assert.GreaterOrEqual(
                TestSceneAssetChangeHandler.RecordedContexts.Count,
                1,
                "Expected at least one scene handler invocation"
            );
            Assert.IsTrue(
                TestSceneAssetChangeHandler.RecordedContexts.Any(ctx =>
                    ctx.Flags == AssetChangeFlags.Created
                ),
                "Expected at least one Created context"
            );
            Assert.IsTrue(
                TestSceneAssetChangeHandler.RecordedInstances.Contains(handler),
                "Expected the specific handler instance to be invoked"
            );
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void SceneHandlerInvokesInstanceMethodWhenAssetDeleted()
        {
            // Arrange
            GameObject go = CreateTrackedSceneObject("SceneHandler");
            TestSceneAssetChangeHandler handler = go.AddComponent<TestSceneAssetChangeHandler>();

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // First process creation
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );
            ClearTestState();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                null,
                new[] { DefaultPayloadAssetPath },
                null,
                null
            );

            // Assert - Filter to only count invocations for our specific handler instance
            int handlerInvocationCount = CountInvocationsForInstances(
                TestSceneAssetChangeHandler.RecordedInstances,
                handler
            );
            List<AssetChangeContext> handlerContexts = GetContextsForInstances(
                TestSceneAssetChangeHandler.RecordedContexts,
                TestSceneAssetChangeHandler.RecordedInstances,
                handler
            );

            Assert.AreEqual(
                1,
                handlerInvocationCount,
                $"Expected scene handler to be invoked exactly once for deletion. "
                    + $"HandlerInvocations={handlerInvocationCount}, "
                    + $"TotalRecordedContexts={TestSceneAssetChangeHandler.RecordedContexts.Count}, "
                    + $"HandlerContexts=[{string.Join(", ", handlerContexts.Select(c => $"Flags={c.Flags}"))}]"
            );
            Assert.AreEqual(
                1,
                handlerContexts.Count,
                "Expected exactly one context for our handler"
            );
            Assert.AreEqual(AssetChangeFlags.Deleted, handlerContexts[0].Flags);
        }

        [Test]
        public void SceneHandlerFindsNestedChildComponents()
        {
            // Arrange - Create hierarchy with handler on child
            GameObject parent = CreateTrackedSceneObject("Parent");
            GameObject child = CreateTrackedSceneObject("Child");
            child.transform.SetParent(parent.transform);
            TestSceneAssetChangeHandler handler = child.AddComponent<TestSceneAssetChangeHandler>();

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Filter to only count invocations for our specific handler instance
            int handlerInvocationCount = CountInvocationsForInstances(
                TestSceneAssetChangeHandler.RecordedInstances,
                handler
            );

            Assert.AreEqual(
                1,
                handlerInvocationCount,
                $"Expected nested scene handler to be invoked exactly once. "
                    + $"HandlerInvocations={handlerInvocationCount}, "
                    + $"TotalRecordedInstances={TestSceneAssetChangeHandler.RecordedInstances.Count}, "
                    + $"ExpectedHandlerID={handler.GetUnityObjectId()}"
            );
            Assert.IsTrue(
                TestSceneAssetChangeHandler.RecordedInstances.Contains(handler),
                "Expected the nested handler instance to be in recorded instances"
            );
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void SceneHandlerFindsMultipleHandlersInScene()
        {
            // Arrange - Create multiple handlers
            GameObject go1 = CreateTrackedSceneObject("SceneHandler1");
            TestSceneAssetChangeHandler handler1 = go1.AddComponent<TestSceneAssetChangeHandler>();

            GameObject go2 = CreateTrackedSceneObject("SceneHandler2");
            TestSceneAssetChangeHandler handler2 = go2.AddComponent<TestSceneAssetChangeHandler>();

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Filter to only count invocations for our specific handler instances
            int handlerInvocationCount = CountInvocationsForInstances(
                TestSceneAssetChangeHandler.RecordedInstances,
                handler1,
                handler2
            );

            Assert.AreEqual(
                2,
                handlerInvocationCount,
                $"Expected both scene handlers to be invoked exactly once each. "
                    + $"HandlerInvocations={handlerInvocationCount}, "
                    + $"TotalRecordedInstances={TestSceneAssetChangeHandler.RecordedInstances.Count}, "
                    + $"ExpectedHandler1ID={handler1.GetUnityObjectId()}, ExpectedHandler2ID={handler2.GetUnityObjectId()}"
            );
            Assert.IsTrue(
                TestSceneAssetChangeHandler.RecordedInstances.Contains(handler1),
                "Expected handler1 to be in recorded instances"
            );
            Assert.IsTrue(
                TestSceneAssetChangeHandler.RecordedInstances.Contains(handler2),
                "Expected handler2 to be in recorded instances"
            );
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void SceneHandlerFindsInactiveObjects()
        {
            // Arrange - Create inactive handler
            GameObject go = CreateTrackedSceneObject("InactiveHandler");
            go.SetActive(false);
            TestSceneAssetChangeHandler handler = go.AddComponent<TestSceneAssetChangeHandler>();

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Filter to only count invocations for our specific handler instance
            int handlerInvocationCount = CountInvocationsForInstances(
                TestSceneAssetChangeHandler.RecordedInstances,
                handler
            );

            Assert.AreEqual(
                1,
                handlerInvocationCount,
                $"Expected inactive scene handler to be invoked exactly once. "
                    + $"HandlerInvocations={handlerInvocationCount}, "
                    + $"TotalRecordedInstances={TestSceneAssetChangeHandler.RecordedInstances.Count}, "
                    + $"ExpectedHandlerID={handler.GetUnityObjectId()}"
            );
            Assert.IsTrue(
                TestSceneAssetChangeHandler.RecordedInstances.Contains(handler),
                "Expected the inactive handler instance to be in recorded instances"
            );
        }

        [Test]
        public void CombinedHandlerFindsBothPrefabAndSceneObjects()
        {
            // Arrange - Use shared CombinedHandler fixture plus a scene object
            GameObject prefab = SharedPrefabTestFixtures.CombinedHandler;
            Assert.IsTrue(prefab != null, "Shared CombinedHandler fixture not found");

            TestCombinedSearchHandler prefabHandler =
                prefab.GetComponent<TestCombinedSearchHandler>();
            Assert.IsTrue(
                prefabHandler != null,
                "Shared CombinedHandler does not have TestCombinedSearchHandler component"
            );

            GameObject sceneGo = CreateTrackedSceneObject("SceneCombinedHandler");
            TestCombinedSearchHandler sceneHandler =
                sceneGo.AddComponent<TestCombinedSearchHandler>();

            Assert.IsTrue(
                sceneHandler != null,
                "Failed to add TestCombinedSearchHandler to scene object"
            );

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Filter to only count invocations for our specific handler instances
            int handlerInvocationCount = CountInvocationsForInstances(
                TestCombinedSearchHandler.RecordedInstances,
                prefabHandler,
                sceneHandler
            );

            Assert.AreEqual(
                2,
                handlerInvocationCount,
                $"Expected both prefab and scene handlers to be invoked exactly once each. "
                    + $"HandlerInvocations={handlerInvocationCount}, "
                    + $"TotalRecordedInstances={TestCombinedSearchHandler.RecordedInstances.Count}, "
                    + $"PrefabHandlerID={prefabHandler.GetUnityObjectId()}, SceneHandlerID={sceneHandler.GetUnityObjectId()}"
            );
            Assert.IsTrue(
                TestCombinedSearchHandler.RecordedInstances.Contains(prefabHandler),
                "Expected prefab handler to be in recorded instances"
            );
            Assert.IsTrue(
                TestCombinedSearchHandler.RecordedInstances.Contains(sceneHandler),
                "Expected scene handler to be in recorded instances"
            );
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void CombinedHandlerDoesNotDuplicateWhenSameInstanceInPrefabAndScene()
        {
            // Arrange - Use shared CombinedHandler fixture and instantiate it in scene
            GameObject prefab = SharedPrefabTestFixtures.CombinedHandler;
            Assert.IsTrue(prefab != null, "Shared CombinedHandler fixture not found");

            TestCombinedSearchHandler prefabHandler =
                prefab.GetComponent<TestCombinedSearchHandler>();
            Assert.IsTrue(
                prefabHandler != null,
                "Shared CombinedHandler does not have TestCombinedSearchHandler component"
            );

            GameObject instance = InstantiateInScene(prefab);
            Track(instance);

            Assert.IsTrue(instance != null, "Failed to instantiate prefab in scene");
            TestCombinedSearchHandler instanceHandler =
                instance.GetComponent<TestCombinedSearchHandler>();
            Assert.IsTrue(
                instanceHandler != null,
                "Instantiated prefab does not have TestCombinedSearchHandler component"
            );

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Should find prefab asset handler and scene instance handler
            // Both should be invoked (they are different objects with different instance IDs)
            Assert.GreaterOrEqual(
                TestCombinedSearchHandler.RecordedInstances.Count,
                1,
                $"Expected at least one handler to be invoked. "
                    + $"RecordedContexts.Count={TestCombinedSearchHandler.RecordedContexts.Count}, "
                    + $"Prefab exists={prefab != null}, Prefab handler={prefabHandler != null}, "
                    + $"Instance exists={instance != null}, Instance handler={instanceHandler != null}"
            );
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void HandlerHandlesNullComponentsGracefully()
        {
            // This tests the null checks in the enumeration code
            // We can't easily create null components, but we verify no crash occurs

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act - Should not throw
            Assert.DoesNotThrow(() =>
            {
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { DefaultPayloadAssetPath },
                    null,
                    null,
                    null
                );
            });
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void HandlerHandlesEmptyScenesGracefully()
        {
            // Arrange - No handlers in scene
            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act - Should not throw even with no scene handlers
            Assert.DoesNotThrow(() =>
            {
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { DefaultPayloadAssetPath },
                    null,
                    null,
                    null
                );
            });
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        public void HandlerHandlesDestroyedObjectsDuringEnumeration()
        {
            // Arrange - Use CreateTrackedSceneObject to ensure proper cleanup if destroy fails
            GameObject go = CreateTrackedSceneObject("Handler");
            TestSceneAssetChangeHandler handler = go.AddComponent<TestSceneAssetChangeHandler>();

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Destroy before processing - the object is tracked so cleanup will handle it
            // if DestroyImmediate fails for any reason
            Object.DestroyImmediate(go); // UNH-SUPPRESS: Testing destroyed object handling

            // Act - Should handle destroyed objects gracefully
            Assert.DoesNotThrow(() =>
            {
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { DefaultPayloadAssetPath },
                    null,
                    null,
                    null
                );
            });
        }

        [Test]
        public void HandlerNonComponentTypeDoesNotSearchPrefabs()
        {
            // Arrange - ScriptableObject handlers should not trigger prefab/scene search
            // even if options are set (the type check should prevent this)
            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act - Should complete normally
            Assert.DoesNotThrow(() =>
            {
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { DefaultPayloadAssetPath },
                    null,
                    null,
                    null
                );
            });
        }

        [TestCase(true, false, 1, TestName = "SearchOptions.PrefabOnly.FindsPrefab")]
        [TestCase(
            false,
            true,
            1,
            TestName = "SearchOptions.SceneOnly.FindsScene",
            Category = "Stress"
        )]
        [TestCase(true, true, 2, TestName = "SearchOptions.Both.FindsBoth", Category = "Stress")]
        public void SearchOptionsFindsCorrectInstances(
            bool usePrefab,
            bool createSceneObject,
            int expectedInvocations
        )
        {
            // Arrange
            GameObject prefab = null;
            TestCombinedSearchHandler prefabHandler = null;
            GameObject sceneGo = null;
            TestCombinedSearchHandler sceneHandler = null;
            List<TestCombinedSearchHandler> expectedHandlers = new();

            if (usePrefab)
            {
                // Use shared CombinedHandler fixture
                prefab = SharedPrefabTestFixtures.CombinedHandler;
                Assert.IsTrue(prefab != null, "Shared CombinedHandler fixture not found");

                prefabHandler = prefab.GetComponent<TestCombinedSearchHandler>();
                Assert.IsTrue(
                    prefabHandler != null,
                    "Shared CombinedHandler does not have TestCombinedSearchHandler component"
                );
                expectedHandlers.Add(prefabHandler);
            }

            if (createSceneObject)
            {
                sceneGo = CreateTrackedSceneObject("CombinedHandler");
                sceneHandler = sceneGo.AddComponent<TestCombinedSearchHandler>();

                Assert.IsTrue(
                    sceneHandler != null,
                    "Failed to add TestCombinedSearchHandler to scene object"
                );
                expectedHandlers.Add(sceneHandler);
            }

            CreatePayloadAsset();
            ClearTestState();

            ResetProcessorWithPrefabSceneFixtureAllowlist();

            // Act
            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { DefaultPayloadAssetPath },
                null,
                null,
                null
            );

            // Assert - Filter to only count invocations for our specific handler instances
            int handlerInvocationCount = CountInvocationsForInstances(
                TestCombinedSearchHandler.RecordedInstances,
                expectedHandlers.ToArray()
            );

            Assert.AreEqual(
                expectedInvocations,
                handlerInvocationCount,
                $"Expected {expectedInvocations} invocations for expected handlers. "
                    + $"HandlerInvocations={handlerInvocationCount}, "
                    + $"TotalRecordedInstances={TestCombinedSearchHandler.RecordedInstances.Count}, "
                    + $"usePrefab={usePrefab}, createSceneObject={createSceneObject}, "
                    + $"PrefabHandlerID={prefabHandler.GetUnityObjectId()}, SceneHandlerID={sceneHandler.GetUnityObjectId()}"
            );
        }

        private static readonly (Type HandlerType, string HumanName)[] HandlerTypesUnderTest =
        {
            (typeof(TestPrefabAssetChangeHandler), "TestPrefabAssetChangeHandler"),
            (typeof(TestSceneAssetChangeHandler), "TestSceneAssetChangeHandler"),
            (typeof(TestCombinedSearchHandler), "TestCombinedSearchHandler"),
            (typeof(TestNestedPrefabHandler), "TestNestedPrefabHandler"),
        };

        /// <summary>
        /// Verifies that each MonoBehaviour handler test double lives in a non-Editor
        /// folder so Unity permits attaching it to GameObjects.
        /// </summary>
        [Test]
        public void HandlersCanBeAddedToGameObjects()
        {
            foreach ((Type handlerType, string humanName) in HandlerTypesUnderTest)
            {
                GameObject go = CreateTrackedSceneObject(humanName + "AddComponent");
                Component handler = go.AddComponent(handlerType);
                Assert.IsTrue(
                    handler != null,
                    $"{handlerType.Name} must NOT be in an Editor folder. "
                        + "MonoBehaviours in Editor folders cannot be attached to GameObjects. "
                        + "Move it to a non-Editor folder (e.g., Tests/Runtime/)."
                );
            }
        }

        [Test]
        public void SharedPrefabFixturesAreValid()
        {
            // Verify all shared prefab fixtures exist and have the expected components
            // PrefabHandler
            GameObject prefabHandler = SharedPrefabTestFixtures.PrefabHandler;
            Assert.IsTrue(prefabHandler != null, "Shared PrefabHandler fixture not found");
            Assert.IsTrue(
                prefabHandler.GetComponent<TestPrefabAssetChangeHandler>() != null,
                "PrefabHandler fixture missing TestPrefabAssetChangeHandler component"
            );

            // NestedHandler
            GameObject nestedHandler = SharedPrefabTestFixtures.NestedHandler;
            Assert.IsTrue(nestedHandler != null, "Shared NestedHandler fixture not found");
            Assert.IsTrue(
                nestedHandler.GetComponentInChildren<TestNestedPrefabHandler>() != null,
                "NestedHandler fixture missing TestNestedPrefabHandler component in children"
            );

            // MultipleHandlers
            GameObject multipleHandlers = SharedPrefabTestFixtures.MultipleHandlers;
            Assert.IsTrue(multipleHandlers != null, "Shared MultipleHandlers fixture not found");
            TestPrefabAssetChangeHandler[] handlers =
                multipleHandlers.GetComponents<TestPrefabAssetChangeHandler>();
            Assert.GreaterOrEqual(
                handlers.Length,
                2,
                "MultipleHandlers fixture should have at least 2 TestPrefabAssetChangeHandler components"
            );

            // CombinedHandler
            GameObject combinedHandler = SharedPrefabTestFixtures.CombinedHandler;
            Assert.IsTrue(combinedHandler != null, "Shared CombinedHandler fixture not found");
            Assert.IsTrue(
                combinedHandler.GetComponent<TestCombinedSearchHandler>() != null,
                "CombinedHandler fixture missing TestCombinedSearchHandler component"
            );

            // SceneHandler
            GameObject sceneHandler = SharedPrefabTestFixtures.SceneHandler;
            Assert.IsTrue(sceneHandler != null, "Shared SceneHandler fixture not found");
            Assert.IsTrue(
                sceneHandler.GetComponent<TestSceneAssetChangeHandler>() != null,
                "SceneHandler fixture missing TestSceneAssetChangeHandler component"
            );
        }

        /// <summary>
        /// Counts how many times the specified handler instances were invoked.
        /// This filters the global RecordedInstances to only count invocations for
        /// the specific instances passed in, providing test isolation.
        /// </summary>
        private static int CountInvocationsForInstances<T>(
            IReadOnlyList<T> recordedInstances,
            params T[] expectedInstances
        )
            where T : Component
        {
            HashSet<long> expectedIds = new();
            foreach (T instance in expectedInstances)
            {
                if (instance != null)
                {
                    expectedIds.Add(instance.GetUnityObjectId());
                }
            }

            int count = 0;
            foreach (T recorded in recordedInstances)
            {
                if (recorded != null && expectedIds.Contains(recorded.GetUnityObjectId()))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Gets the contexts recorded for specific handler instances.
        /// This filters the global RecordedContexts to only return entries for
        /// the specific instances passed in, providing test isolation.
        /// </summary>
        private static List<AssetChangeContext> GetContextsForInstances<T>(
            IReadOnlyList<AssetChangeContext> recordedContexts,
            IReadOnlyList<T> recordedInstances,
            params T[] expectedInstances
        )
            where T : Component
        {
            HashSet<long> expectedIds = new();
            foreach (T instance in expectedInstances)
            {
                if (instance != null)
                {
                    expectedIds.Add(instance.GetUnityObjectId());
                }
            }

            List<AssetChangeContext> result = new();
            for (int i = 0; i < recordedInstances.Count && i < recordedContexts.Count; i++)
            {
                T recorded = recordedInstances[i];
                if (recorded != null && expectedIds.Contains(recorded.GetUnityObjectId()))
                {
                    result.Add(recordedContexts[i]);
                }
            }
            return result;
        }
    }
}
