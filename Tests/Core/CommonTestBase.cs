// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS: This IS the CommonTestBase class
namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;
#if UNITY_EDITOR
    using System.Text.RegularExpressions;
    using UnityEditor.SceneManagement;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using AssetDatabaseBatchHelper = WallstopStudios.UnityHelpers.Editor.Utils.AssetDatabaseBatchHelper;
#endif

    /// <summary>
    /// Shared test base that tracks spawned Unity objects, disposable resources,
    /// and temporary scenes across both EditMode and PlayMode tests.
    /// </summary>
    public abstract class CommonTestBase
    {
        /// <summary>
        /// Upper bound (seconds) for any teardown-time async cleanup wait (tracked async
        /// disposals, tracked scene unloads). Generous enough never to trip on a healthy
        /// op (these complete in well under a frame) yet far below the CI no-output
        /// watchdog window, so a stuck cleanup fails its own test -- and the leg still
        /// writes results.xml -- instead of stalling the whole run.
        /// </summary>
        private const float TrackedDisposalTimeoutSeconds = 30f;
        private const int TrackedObjectDestroyMaxFrames = 30;

        private UnityMainThreadDispatcher.AutoCreationScope _dispatcherScope;

        protected readonly List<Object> _trackedObjects = new();
        protected readonly List<IDisposable> _trackedDisposables = new();
        protected readonly List<Scene> _trackedScenes = new();

        // Expected-error capture: the Unity Test Framework re-invokes completed test bodies on scene
        // ops in batchmode, re-emitting their EXPECTED logs into bystanders. Capturing+suppressing the
        // expected patterns via a custom log handler keeps them out of the global log entirely, so a
        // re-run cannot bleed them. Static so a re-run of one fixture's body during another still hits
        // the registry. PlayMode only (the re-run + frame bleed are PlayMode); EditMode falls back to
        // LogAssert.Expect.
        private static readonly System.Collections.Generic.List<(
            UnityEngine.LogType type,
            System.Text.RegularExpressions.Regex pattern
        )> _expectedErrors = new();
        private static readonly System.Collections.Generic.HashSet<System.Text.RegularExpressions.Regex> _matchedExpectedErrors =
            new();
        private static readonly object _expectedErrorLock = new();
        private static UnityEngine.ILogHandler _expectErrorInnerHandler;
        private static ExpectedErrorSuppressingHandler _expectErrorHandler;
        protected readonly List<Func<ValueTask>> _trackedAsyncDisposals = new();

        /// <summary>
        /// PlayMode cross-test leak guard. Captured at the start of every test (in
        /// <see cref="CommonUnitySetUp"/>): the loaded scenes and the object IDs of every ROOT
        /// GameObject that already existed in them. Any root alive at teardown, in one of those same
        /// scenes, whose ID is NOT in the baseline was created by this test; if it survived the
        /// targeted cleanup it is a leak that would pollute later tests, so the teardown sweep
        /// (<see cref="CollectLeakedRoots"/> + <see cref="DestroyLeakedRootsAndDescribe"/>) destroys
        /// it and fails THIS test (the producer). PlayMode only -- EditMode destroys synchronously
        /// with no frame-boundary bleed, so it never captures and never sweeps.
        ///
        /// SCOPE (deliberately narrow to avoid false positives):
        /// - Only the SCENES that existed at test start are swept. A scene the test LOADS itself
        ///   (e.g. via <see cref="SceneManager.LoadScene(int)"/>) owns its content; those roots are
        ///   not the test's leaks.
        /// - The DontDestroyOnLoad scene is NOT swept (<see cref="SceneManager.GetSceneAt"/> excludes
        ///   it): leaked RuntimeSingletons there are handled by the registry clear above, and
        ///   framework infrastructure (e.g. Zenject's pooled prefab parent) must be left alone.
        /// - Only ROOT GameObjects (not children re-parented under a baseline root) and only
        ///   GameObjects (not non-object leaks like a dangling sceneLoaded delegate, handled at their
        ///   source). Keys are
        ///   <see cref="WallstopStudios.UnityHelpers.Core.Extension.UnityObjectExtensions.GetUnityObjectId"/>
        ///   (stable per object; forward-compatible with Unity 6000.4 EntityId).
        /// </summary>
        private readonly HashSet<long> _testStartRootIds = new();
        private readonly HashSet<Scene> _testStartScenes = new();
        private bool _testStartRootsCaptured;

#if UNITY_EDITOR
        /// <summary>
        /// Tracks folders created by this test instance for cleanup.
        /// Stored in order of creation (deepest paths may come later).
        /// </summary>
        protected readonly List<string> _trackedFolders = new();

        /// <summary>
        /// Tracks asset paths created by this test instance for cleanup.
        /// </summary>
        protected readonly List<string> _trackedAssetPaths = new();

        /// <summary>
        /// When true, CleanupTrackedFoldersAndAssets() accumulates assets for batch cleanup
        /// in OneTimeTearDown instead of cleaning per-test. Set in subclass constructor or OneTimeSetUp.
        /// </summary>
        protected bool DeferAssetCleanupToOneTimeTearDown { get; set; }

        /// <summary>
        /// Accumulated asset paths when deferred cleanup is enabled.
        /// </summary>
        private readonly List<string> _deferredAssetPaths = new();

        /// <summary>
        /// Accumulated folder paths when deferred cleanup is enabled.
        /// </summary>
        private readonly List<string> _deferredFolderPaths = new();

        private bool _previousEditorUiSuppress;
#endif

        [SetUp]
        public virtual void BaseSetUp()
        {
#if UNITY_EDITOR
            CleanupPackageRootGeneratedArtifacts();
            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;

            // Proactively reset asset editing state to ensure clean state for each test
            // This handles cases where a previous test crashed without proper cleanup
            // All AssetDatabase batching now uses the unified Editor.Utils.AssetDatabaseBatchHelper
            try
            {
                // Only reset batch depth if not using fixture-level batching (BatchedEditorTestBase)
                // When DeferAssetCleanupToOneTimeTearDown is true, the fixture manages its own batch scope
                if (!DeferAssetCleanupToOneTimeTearDown)
                {
                    // Reset unified batch helper with Unity cleanup (handles both counters AND Unity state)
                    // This ensures any lingering batch state from a crashed test is properly cleaned up
                    Editor.Utils.AssetDatabaseBatchHelper.ResetBatchDepth();
                }
                // Reset legacy state in production code classes
                ScriptableObjectSingletonCreator.ResetAssetEditingScopeDepthForTesting();
                ScriptableObjectSingletonMetadataUtility.ResetAssetEditingDepthForTesting();
            }
            catch
            {
                // Best-effort cleanup - ignore exceptions during setup
            }
#endif
            InitializeDispatcherScope();
        }

        [UnitySetUp]
        public IEnumerator CommonUnitySetUp()
        {
            // PlayMode cross-test leak guard: snapshot the roots that exist before this test runs so
            // the teardown sweep can destroy + attribute anything this test leaks. EditMode is immune
            // (synchronous destroy, no frame-boundary log bleed), so it never captures and never sweeps.
            if (Application.isPlaying)
            {
                InstallExpectedErrorSuppression();
                CaptureLeakGuardBaseline();
            }

            yield break;
        }

        protected GameObject NewGameObject(string name = "GameObject")
        {
            return Track(new GameObject(name));
        }

        protected T CreateScriptableObject<T>()
            where T : ScriptableObject
        {
            return Track(ScriptableObject.CreateInstance<T>());
        }

        protected ScriptableObject CreateScriptableObject(Type type)
        {
            return Track(ScriptableObject.CreateInstance(type));
        }

        protected T Track<T>(T obj)
            where T : Object
        {
            if (obj != null)
            {
                _trackedObjects.Add(obj);
            }
            return obj;
        }

        protected GameObject Track(GameObject obj)
        {
            return Track<GameObject>(obj);
        }

        protected T TrackDisposable<T>(T disposable)
            where T : IDisposable
        {
            if (disposable != null)
            {
                _trackedDisposables.Add(disposable);
            }
            return disposable;
        }

        protected Func<ValueTask> TrackAsyncDisposal(Func<ValueTask> producer)
        {
            if (producer != null)
            {
                _trackedAsyncDisposals.Add(producer);
            }
            return producer;
        }

        protected Scene CreateTempScene(string name, bool setActive = true)
        {
            Scene scene;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else
#endif
            {
                scene = SceneManager.CreateScene(name);
            }

            if (setActive)
            {
                SceneManager.SetActiveScene(scene);
            }

            _trackedScenes.Add(scene);

            if (Application.isPlaying)
            {
                TrackAsyncDisposal(() => UnloadSceneAsync(scene));
            }

            return scene;
        }

        [TearDown]
        public virtual void TearDown()
        {
#if UNITY_EDITOR
            // Safety cleanup: ensure AssetDatabase is not stuck in batch mode
            // This handles tests that throw exceptions before properly disposing batch scopes
            // All AssetDatabase batching now uses the unified Editor.Utils.AssetDatabaseBatchHelper
            try
            {
                // Only reset batch depth if not using fixture-level batching (BatchedEditorTestBase)
                // When DeferAssetCleanupToOneTimeTearDown is true, the fixture manages its own batch scope
                if (!DeferAssetCleanupToOneTimeTearDown)
                {
                    // Reset unified batch helper (handles all AssetDatabase state cleanup)
                    Editor.Utils.AssetDatabaseBatchHelper.ResetBatchDepth();
                }
                // Reset legacy state in production code classes
                ScriptableObjectSingletonCreator.ResetAssetEditingScopeDepthForTesting();
                ScriptableObjectSingletonMetadataUtility.ResetAssetEditingDepthForTesting();
            }
            catch
            {
                // Best-effort cleanup - ignore exceptions during teardown
            }

            if (!Application.isPlaying && _trackedScenes.Count > 0)
            {
                CloseTrackedScenesInEditor();
            }

            EditorUi.Suppress = _previousEditorUiSuppress;
#endif

            if (_trackedDisposables.Count > 0)
            {
                for (int i = _trackedDisposables.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _trackedDisposables[i]?.Dispose();
                    }
                    catch
                    {
                        // best-effort teardown
                    }
                }
                _trackedDisposables.Clear();
            }

            if (!Application.isPlaying)
            {
                DestroyTrackedObjects();
            }

            DisposeDispatcherScope();
        }

        /// <summary>
        /// Destroys every currently tracked <see cref="Object"/> and clears the tracking list.
        /// </summary>
        /// <remarks>
        /// In the editor, an object backed by a persisted asset is removed with
        /// <see cref="UnityEditor.AssetDatabase.DeleteAsset"/> (which also removes its <c>.meta</c>);
        /// anything else is destroyed with <c>allowDestroyingAssets: true</c>. That overload is the
        /// asset-safe one: it can never trigger Unity's "Destroying assets is not permitted to avoid
        /// data loss" error -- a <see cref="LogType.Error"/> that fails the running test in teardown
        /// AND leaks the object -- even when an asset path can no longer be resolved (e.g. a subclass
        /// already deleted the asset, leaving an orphaned persistent wrapper, or
        /// <c>EditorUtility.IsPersistent</c> and <c>GetAssetPath</c> momentarily disagree). The flag
        /// is a harmless no-op for in-memory objects, which -- together with the path-based
        /// <c>DeleteAsset</c> -- covers every kind of object these fixtures track (in-memory objects
        /// and standalone assets).
        /// </remarks>
        protected void DestroyTrackedObjects()
        {
            if (_trackedObjects.Count == 0)
            {
                return;
            }

#if UNITY_EDITOR
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
#endif
            {
                Object[] snapshot = _trackedObjects.ToArray();
                foreach (Object obj in snapshot)
                {
                    if (obj == null)
                    {
                        continue;
                    }
#if UNITY_EDITOR
                    string assetPath = UnityEditor.AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                        continue;
                    }
                    Object.DestroyImmediate(obj, true); // UNH-SUPPRESS: asset-safe test cleanup
#else
                    Object.DestroyImmediate(obj); // UNH-SUPPRESS: Required for test cleanup
#endif
                }
                _trackedObjects.Clear();
            }
        }

        [UnityTearDown]
        public virtual IEnumerator UnityTearDown()
        {
            // Deferred so the rest of teardown (object destroy, dispatcher-scope dispose,
            // singleton clear) ALWAYS runs even if a disposal times out -- otherwise a stuck
            // disposal would leak state into the next test. Surfaced after cleanup, below.
            string disposalFailure = null;
            string trackedObjectFailure = null;
            if (_trackedAsyncDisposals.Count > 0)
            {
                // Bounded wait: an async disposal that never completes (e.g. a batchmode
                // scene op that never signals) MUST NOT hang the leg. A hang produces no
                // output, the CI watchdog tree-kills Unity, and results.xml is never
                // written -- so ~thousands of passing tests report as "tests did not run."
                // A SINGLE total deadline across all disposals bounds the whole teardown wait
                // (a per-disposal cap could sum past the watchdog window with many disposals).
                float disposalEndTime = Time.realtimeSinceStartup + TrackedDisposalTimeoutSeconds;
                foreach (Func<ValueTask> producer in _trackedAsyncDisposals.ToArray())
                {
                    if (producer == null)
                    {
                        continue;
                    }

                    ValueTask task = producer();
                    while (!task.IsCompleted)
                    {
                        if (Time.realtimeSinceStartup > disposalEndTime)
                        {
                            // Record + abandon the wait; do NOT throw here. The failure is
                            // surfaced after all cleanup runs so the next test starts clean.
                            disposalFailure =
                                "Tracked async disposal did not complete within "
                                + $"{TrackedDisposalTimeoutSeconds:0.###}s during teardown of "
                                + $"{TestContext.CurrentContext.Test.FullName}. A disposal that "
                                + "never completes hangs the whole PlayMode leg (no results.xml); "
                                + "ensure TrackAsyncDisposal targets complete in batchmode.";
                            break;
                        }
                        yield return null;
                    }

                    if (disposalFailure != null)
                    {
                        break;
                    }
                }
                _trackedAsyncDisposals.Clear();
            }

            if (_trackedObjects.Count > 0)
            {
                Object[] snapshot = _trackedObjects.ToArray();
                foreach (Object obj in snapshot)
                {
                    if (obj == null)
                    {
                        continue;
                    }

                    Object.Destroy(obj); // UNH-SUPPRESS: Required for PlayMode test cleanup
                }

                for (int i = 0; i < TrackedObjectDestroyMaxFrames; i++)
                {
                    bool hasLiveObject = false;
                    foreach (Object obj in snapshot)
                    {
                        if (obj != null)
                        {
                            hasLiveObject = true;
                            break;
                        }
                    }

                    if (!hasLiveObject)
                    {
                        break;
                    }

                    yield return null;
                }

                List<string> liveTrackedObjects = null;
                foreach (Object obj in snapshot)
                {
                    if (obj == null)
                    {
                        continue;
                    }

                    liveTrackedObjects ??= new List<string>();
                    liveTrackedObjects.Add(
                        $"{obj.name} ({obj.GetType().FullName}, instance {obj.GetUnityObjectId()})"
                    );
                }

                if (liveTrackedObjects is { Count: > 0 })
                {
                    trackedObjectFailure =
                        $"Tracked object cleanup left {liveTrackedObjects.Count} object(s) alive "
                        + $"after {TrackedObjectDestroyMaxFrames} frame(s) during teardown of "
                        + $"{TestContext.CurrentContext.Test.FullName}: "
                        + string.Join(", ", liveTrackedObjects);
                }

                _trackedObjects.Clear();
            }

#if UNITY_EDITOR
            // Safety cleanup: ensure AssetDatabase is not stuck in batch mode
            // All AssetDatabase batching now uses the unified Editor.Utils.AssetDatabaseBatchHelper
            try
            {
                // Only reset batch depth if not using fixture-level batching (BatchedEditorTestBase)
                // When DeferAssetCleanupToOneTimeTearDown is true, the fixture manages its own batch scope
                if (!DeferAssetCleanupToOneTimeTearDown)
                {
                    // Reset unified batch helper (handles all AssetDatabase state cleanup)
                    Editor.Utils.AssetDatabaseBatchHelper.ResetBatchDepth();
                }
                // Reset legacy state in production code classes
                ScriptableObjectSingletonCreator.ResetAssetEditingScopeDepthForTesting();
                ScriptableObjectSingletonMetadataUtility.ResetAssetEditingDepthForTesting();
            }
            catch
            {
                // Best-effort cleanup - ignore exceptions during teardown
            }

            EditorUi.Suppress = _previousEditorUiSuppress;
#endif
            string dispatcherFailure = null;
            if (Application.isPlaying)
            {
                dispatcherFailure = DrainUnityMainThreadDispatchersForTeardown();
                yield return null;
                string followUpDispatcherFailure = DrainUnityMainThreadDispatchersForTeardown();
                if (dispatcherFailure == null)
                {
                    dispatcherFailure = followUpDispatcherFailure;
                }
            }

            DisposeDispatcherScope();

            // Cross-test singleton-leak guard (PlayMode only). RuntimeSingleton<T> types
            // (UnityMainThreadDispatcher, CoroutineHandler, ...) clear their static _instance only
            // on domain reload / scene load -- NOT between PlayMode tests in the same domain -- so a
            // singleton created (directly or incidentally) by one test otherwise survives into the
            // next, which fails "no instance on first access" assertions and lets dispatcher
            // instances accumulate across the suite. Clearing here nulls every registered singleton's
            // cached reference so the next test starts clean (the dispatcher's GameObjects are also
            // destroyed by DisposeDispatcherScope above). EditMode destroys synchronously already, so
            // this is scoped to PlayMode to keep the green EditMode legs untouched.
            if (Application.isPlaying)
            {
                int dispatcherDestroyFrames = 10;
                while (
                    UnityMainThreadDispatcher.GetLiveDispatcherCount() > 0
                    && dispatcherDestroyFrames > 0
                )
                {
                    dispatcherDestroyFrames--;
                    yield return null;
                }

                string singletonLeaksBeforeClear =
                    RuntimeSingletonRegistry.DescribeLiveInstancesForTesting();
                RuntimeSingletonRegistry.ClearAllRegisteredInstances();
                int singletonDestroyFrames = TrackedObjectDestroyMaxFrames;
                string singletonLeaksAfterClear =
                    RuntimeSingletonRegistry.DescribeLiveInstancesForTesting();
                while (
                    !string.IsNullOrWhiteSpace(singletonLeaksAfterClear)
                    && singletonDestroyFrames > 0
                )
                {
                    singletonDestroyFrames--;
                    yield return null;
                    singletonLeaksAfterClear =
                        RuntimeSingletonRegistry.DescribeLiveInstancesForTesting();
                }

                if (!string.IsNullOrWhiteSpace(singletonLeaksAfterClear))
                {
                    dispatcherFailure ??=
                        "[uh-leak] RuntimeSingleton object(s) still resident after registry "
                        + "cleanup during teardown of "
                        + $"{TestContext.CurrentContext.Test.FullName}. Before cleanup: "
                        + $"{singletonLeaksBeforeClear}. After cleanup: {singletonLeaksAfterClear}";
                }

                dispatcherDestroyFrames = 10;
                while (
                    UnityMainThreadDispatcher.GetLiveDispatcherCount() > 0
                    && dispatcherDestroyFrames > 0
                )
                {
                    dispatcherDestroyFrames--;
                    yield return null;
                }

                // Leak diagnostic: a UnityMainThreadDispatcher still resident after the scope tore
                // down + the registry cleared means a leak the cleanup could not reach (an orphaned
                // duplicate). Surface it as one [uh-leak] line naming the just-finished test so a
                // regression self-identifies in unity.log and fails the producer test instead of a
                // later bystander.
                int residentDispatchers = UnityMainThreadDispatcher.GetLiveDispatcherCount();
                if (residentDispatchers > 0)
                {
                    dispatcherFailure ??=
                        $"[uh-leak] {residentDispatchers} UnityMainThreadDispatcher object(s) "
                        + "still resident after teardown of "
                        + $"{TestContext.CurrentContext.Test.FullName}. "
                        + UnityMainThreadDispatcher.DescribeLiveDispatchersForTesting();
                }
            }

            // FINAL safety net (PlayMode only): destroy any root GameObject this test created that
            // survived the targeted cleanup above (tracked-object destroy, dispatcher-scope dispose,
            // singleton-registry clear), regardless of whether it was Track()'d. This closes the gap
            // where an untracked / production-spawned / DI-spawned object outlives its test and
            // pollutes a later one -- the root cause of this suite's cross-test flakiness.
            //
            // Candidates are settle-rechecked first: a non-baseline root may simply be mid-deferred-
            // destroy from the targeted cleanup above. Object.Destroy and DontDestroyOnLoad singleton
            // teardown flush at frame end, and the registry's Resources.FindObjectsOfTypeAll poll can
            // report a singleton "gone" a frame before GetRootGameObjects stops returning it -- so an
            // immediate enumeration would false-flag a singleton the registry IS correctly destroying.
            // Only roots that survive the settle window are GENUINE leaks; those are destroyed and
            // reported. The failure is surfaced AFTER the log reconcile below so any OnDestroy logs
            // flush into THIS test.
            string sweepFailure = null;
            if (Application.isPlaying)
            {
                List<GameObject> leakedRoots = CollectLeakedRoots();
                int settleFrames = TrackedObjectDestroyMaxFrames;
                while (leakedRoots != null && settleFrames > 0)
                {
                    settleFrames--;
                    yield return null;
                    leakedRoots = CollectLeakedRoots();
                }

                if (leakedRoots != null)
                {
                    sweepFailure = DestroyLeakedRootsAndDescribe(leakedRoots);
                    for (int i = 0; i < TrackedObjectDestroyMaxFrames; i++)
                    {
                        yield return null;
                    }
                }

                _testStartRootsCaptured = false;
            }

            // Cross-test log-pollution guard (PlayMode only), run BEFORE any failure is surfaced. A
            // synchronous or late-flushed [Error] -- including OnDestroy logs from the tracked-object
            // destroy, the dispatcher/singleton clear, and the scorched-earth sweep above -- otherwise
            // bleeds across the frame boundary into the NEXT test's scope, so an innocent later test
            // fails for an error this fixture produced. Pump frames to flush any pending logs, then
            // reconcile so an UNEXPECTED [Error] fails THIS fixture (where a LogAssert.Expect can fix
            // it) instead of a bystander. Compliant tests that LogAssert.Expect their errors are
            // unaffected. EditMode reconciles synchronously at test end already (no frame bleed), so
            // this is scoped to PlayMode to keep the green EditMode legs untouched.
            string expectedErrorFailure = null;
            if (Application.isPlaying)
            {
                expectedErrorFailure = RestoreExpectedErrorSuppressionAndVerify();
            }
            if (Application.isPlaying)
            {
                DrainUnityMainThreadDispatchersForTeardown();
                yield return null;
                DrainUnityMainThreadDispatchersForTeardown();
                LogAssert.NoUnexpectedReceived();
            }

            // All state cleanup has now run (objects destroyed, dispatcher scope disposed, singletons
            // cleared, leaks swept) and logs are reconciled, so the next test starts clean regardless
            // of which failure fires. Surface them AFTER the reconcile so deferred OnDestroy logs from
            // the cleanups cannot bleed; order is root-cause priority (a hang/leak is more actionable
            // than the noise it may have produced).
            if (disposalFailure != null)
            {
                Assert.Fail(disposalFailure);
            }

            if (trackedObjectFailure != null)
            {
                Assert.Fail(trackedObjectFailure);
            }

            if (dispatcherFailure != null)
            {
                Assert.Fail(dispatcherFailure);
            }

            if (sweepFailure != null)
            {
                Assert.Fail(sweepFailure);
            }

            if (expectedErrorFailure != null)
            {
                Assert.Fail(expectedErrorFailure);
            }
        }

        /// <summary>
        /// Snapshots the leak-guard baseline: the loaded scenes and the IDs of their existing root
        /// GameObjects. <see cref="SceneManager.GetSceneAt"/> excludes DontDestroyOnLoad, so framework
        /// infrastructure and leaked singletons there are out of scope (the registry clear handles the
        /// latter). PlayMode only.
        /// </summary>
        private void CaptureLeakGuardBaseline()
        {
            _testStartRootIds.Clear();
            _testStartScenes.Clear();

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                _testStartScenes.Add(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root != null)
                    {
                        _testStartRootIds.Add(root.GetUnityObjectId());
                    }
                }
            }

            _testStartRootsCaptured = true;
        }

        /// <summary>
        /// Returns the root GameObjects alive now -- in a scene that existed at baseline -- whose ID
        /// was NOT in the baseline (i.e. created by this test), or <c>null</c> when there are none (the
        /// common fast path). Does NOT destroy anything -- the caller settle-rechecks to distinguish a
        /// genuine leak from a root that is merely mid-deferred-destroy. Scenes the test LOADED itself
        /// (absent from the baseline scene set) are skipped -- their content is not this test's leak.
        /// No-op unless a PlayMode baseline was captured.
        /// </summary>
        private List<GameObject> CollectLeakedRoots()
        {
            if (!_testStartRootsCaptured)
            {
                return null;
            }

            List<GameObject> leaked = null;
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !_testStartScenes.Contains(scene))
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null || _testStartRootIds.Contains(root.GetUnityObjectId()))
                    {
                        continue;
                    }

                    leaked ??= new List<GameObject>();
                    leaked.Add(root);
                }
            }

            return leaked;
        }

        /// <summary>
        /// Destroys the given leaked roots (deferred <see cref="Object.Destroy(Object)"/>) and returns
        /// a one-line <c>[uh-leak]</c> diagnostic naming them and the producing test, or <c>null</c>
        /// if the list held nothing live.
        /// </summary>
        private string DestroyLeakedRootsAndDescribe(List<GameObject> leaked)
        {
            if (leaked == null)
            {
                return null;
            }

            List<string> descriptions = new(leaked.Count);
            foreach (GameObject root in leaked)
            {
                if (root == null)
                {
                    continue;
                }

                descriptions.Add(DescribeRoot(root));
                Object.Destroy(root); // UNH-SUPPRESS: scorched-earth cross-test leak cleanup
            }

            if (descriptions.Count == 0)
            {
                return null;
            }

            return "[uh-leak] scorched-earth swept "
                + $"{descriptions.Count} untracked root GameObject(s) leaked by "
                + $"{TestContext.CurrentContext.Test.FullName}: {string.Join(", ", descriptions)}. "
                + "Every GameObject a PlayMode test creates must be destroyed before teardown "
                + "(Track(...) it, or destroy it explicitly); a survivor pollutes later tests.";
        }

        private static string DescribeRoot(GameObject root)
        {
            string componentType = "GameObject";
            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component is not Transform)
                {
                    componentType = component.GetType().Name;
                    break;
                }
            }

            return $"'{root.name}' ({componentType}, scene '{root.scene.name}', "
                + $"instance {root.GetUnityObjectId()}, active={root.activeInHierarchy})";
        }

        /// <summary>
        /// Test hook for the leak-guard self-test: re-captures the current roots as this test's
        /// baseline (mirrors <see cref="CommonUnitySetUp"/>), so a test can establish a known
        /// baseline after creating objects it expects the sweep to spare. PlayMode only.
        /// </summary>
        protected void CaptureLeakGuardBaselineForTests()
        {
            CaptureLeakGuardBaseline();
        }

        /// <summary>
        /// Test hook for the leak-guard self-test: runs the teardown leak sweep immediately and
        /// returns its diagnostic (null when nothing leaked), destroying any non-baseline root.
        /// </summary>
        protected string RunLeakGuardSweepForTests()
        {
            return DestroyLeakedRootsAndDescribe(CollectLeakedRoots());
        }

        /// <summary>
        /// Test hook for the leak-guard self-test: the number of root GameObjects captured in the
        /// current baseline. Non-zero in PlayMode proves <see cref="CommonUnitySetUp"/> actually
        /// snapshotted the runner infrastructure (so a silent capture regression can't pass the
        /// self-test).
        /// </summary>
        protected int LeakGuardBaselineCountForTests => _testStartRootIds.Count;

        /// <summary>
        /// Registers an EXPECTED error/warning log pattern that is captured + SUPPRESSED (kept out of
        /// the global Unity log) for the rest of the current PlayMode test, instead of asserted via
        /// LogAssert.Expect. This makes the assertion immune to the Unity Test Framework re-invoking a
        /// completed test body on a later scene op (which would otherwise re-emit the log into a
        /// bystander). The pattern must still be matched at least once by teardown, or the test fails
        /// (same guarantee as LogAssert.Expect's "expected log did not appear"). EditMode (where the
        /// suppressing handler is not installed) falls back to LogAssert.Expect.
        /// </summary>
        protected static void ExpectError(UnityEngine.LogType type, string pattern) =>
            ExpectError(type, new System.Text.RegularExpressions.Regex(pattern));

        protected static void ExpectError(
            UnityEngine.LogType type,
            System.Text.RegularExpressions.Regex pattern
        )
        {
            lock (_expectedErrorLock)
            {
                if (_expectErrorHandler != null)
                {
                    _expectedErrors.Add((type, pattern));
                    return;
                }
            }
            UnityEngine.TestTools.LogAssert.Expect(type, pattern);
        }

        // Custom log handler: for Error/Warning/Assert/Exception logs matching a registered expected
        // pattern (same LogType), record the match and SUPPRESS (do not forward to the inner handler,
        // which is what keeps it out of LogAssert/the console). Everything else forwards unchanged.
        private sealed class ExpectedErrorSuppressingHandler : UnityEngine.ILogHandler
        {
            private readonly UnityEngine.ILogHandler _inner;

            public ExpectedErrorSuppressingHandler(UnityEngine.ILogHandler inner)
            {
                _inner = inner;
            }

            public void LogFormat(
                UnityEngine.LogType logType,
                UnityEngine.Object context,
                string format,
                params object[] args
            )
            {
                if (
                    logType == UnityEngine.LogType.Error
                    || logType == UnityEngine.LogType.Warning
                    || logType == UnityEngine.LogType.Assert
                    || logType == UnityEngine.LogType.Exception
                )
                {
                    string message;
                    try
                    {
                        message =
                            args != null && args.Length > 0 ? string.Format(format, args) : format;
                    }
                    catch
                    {
                        message = format;
                    }

                    lock (_expectedErrorLock)
                    {
                        for (int i = 0; i < _expectedErrors.Count; i++)
                        {
                            if (
                                _expectedErrors[i].type == logType
                                && _expectedErrors[i].pattern.IsMatch(message)
                            )
                            {
                                _matchedExpectedErrors.Add(_expectedErrors[i].pattern);
                                return;
                            }
                        }
                    }
                }

                _inner.LogFormat(logType, context, format, args);
            }

            public void LogException(System.Exception exception, UnityEngine.Object context)
            {
                _inner.LogException(exception, context);
            }
        }

        private static void InstallExpectedErrorSuppression()
        {
            if (_expectErrorHandler != null)
            {
                return;
            }
            _expectErrorInnerHandler = UnityEngine.Debug.unityLogger.logHandler;
            _expectErrorHandler = new ExpectedErrorSuppressingHandler(_expectErrorInnerHandler);
            UnityEngine.Debug.unityLogger.logHandler = _expectErrorHandler;
        }

        // Restores the real handler and returns a failure string for any expected pattern never matched
        // (or null). Always clears the registry so the next test starts clean.
        private static string RestoreExpectedErrorSuppressionAndVerify()
        {
            lock (_expectedErrorLock)
            {
                if (
                    _expectErrorHandler != null
                    && ReferenceEquals(
                        UnityEngine.Debug.unityLogger.logHandler,
                        _expectErrorHandler
                    )
                )
                {
                    UnityEngine.Debug.unityLogger.logHandler = _expectErrorInnerHandler;
                }
                _expectErrorHandler = null;
                _expectErrorInnerHandler = null;

                string failure = null;
                foreach (
                    (
                        UnityEngine.LogType type,
                        System.Text.RegularExpressions.Regex pattern
                    ) in _expectedErrors
                )
                {
                    if (!_matchedExpectedErrors.Contains(pattern))
                    {
                        failure =
                            (failure ?? "Expected error log(s) never matched: ")
                            + $"[{type}] {pattern} ; ";
                    }
                }
                _expectedErrors.Clear();
                _matchedExpectedErrors.Clear();
                return failure;
            }
        }

        /// <summary>
        /// Registers a <see cref="LogAssert"/> expectation for the exact <c>[Error]</c> a relational
        /// component assignment logs when a REQUIRED field cannot be resolved. Centralizes the log
        /// FORMAT -- the <c>&lt;time&gt;|&lt;name&gt;[&lt;type&gt;]|message</c> shape produced by the
        /// package logger -- so the dozen-plus child/parent/sibling tests share ONE source of truth
        /// (mirroring the producer in <c>BaseRelationalComponentAttribute</c>) instead of hand-copied
        /// regexes that silently rot if the format changes. Caller-supplied values are regex-escaped,
        /// so pass plain display names (e.g. <c>"UnityEngine.SpriteRenderer[]"</c>).
        /// </summary>
        /// <param name="ownerName">GameObject name hosting the component (e.g. "Child-Missing").</param>
        /// <param name="ownerType">Owning component type name (e.g. "ChildMissingTester").</param>
        /// <param name="relationship">"child", "parent", or "sibling".</param>
        /// <param name="fieldType">Field type display name (e.g. "UnityEngine.SpriteRenderer").</param>
        /// <param name="fieldName">Field name (e.g. "requiredRenderer").</param>
        protected static void ExpectMissingRelationalComponentError(
            string ownerName,
            string ownerType,
            string relationship,
            string fieldType,
            string fieldName
        )
        {
            static string Escape(string value) =>
                System.Text.RegularExpressions.Regex.Escape(value);

            string pattern =
                $@"^\d+(\.\d+)?\|{Escape(ownerName)}\[{Escape(ownerType)}\]\|Unable to find "
                + $"{relationship} component of type {Escape(fieldType)} for field "
                + $"'{Escape(fieldName)}'$";

            ExpectError(LogType.Error, pattern);
        }

        /// <summary>
        /// Called once before any tests in the fixture run.
        /// Subclasses can override to create shared test assets using BeginBatch().
        /// </summary>
        [OneTimeSetUp]
        public virtual void CommonOneTimeSetUp()
        {
#if UNITY_EDITOR
            CleanupPackageRootGeneratedArtifacts();
            // Reset counters only (not Unity state) to handle domain reload scenarios.
            // After a domain reload, Unity's internal AssetDatabase state is reset to zero,
            // but our static counters may persist with stale values from previous sessions.
            // Using ResetCountersOnly() (not ResetBatchDepth()) ensures we don't call
            // StopAssetEditing/AllowAutoRefresh when Unity's counters are already at zero,
            // which would cause assertion failures.
            try
            {
                Editor.Utils.AssetDatabaseBatchHelper.ResetCountersOnly();
            }
            catch
            {
                // Best-effort cleanup - ignore exceptions during setup
            }
#endif
            // Subclasses can override to create shared test assets using BeginBatch()
        }

#if UNITY_EDITOR
        /// <summary>
        /// Registers an expectation for Unity's benign "No script asset for ScriptableObject"
        /// importer warning so a fixture that legitimately persists a raw base
        /// <see cref="ScriptableObject"/> (via <c>ScriptableObject.CreateInstance&lt;ScriptableObject&gt;()</c>
        /// plus <see cref="UnityEditor.AssetDatabase.CreateAsset"/>) does not fail under
        /// <see cref="LogAssert.NoUnexpectedReceived"/>.
        /// </summary>
        /// <remarks>
        /// A base <see cref="ScriptableObject"/> has no backing <c>MonoScript</c>, so Unity's importer
        /// emits this warning when the asset is imported, re-serialized, or deleted (most reliably during
        /// a clean CI import). The warning originates inside Unity's importer, not in production cleanup
        /// code, so the only thing to do is tolerate it.
        ///
        /// CI evidence (Unity 2021/2022/6000, clean editmode import) shows the warning fires
        /// consistently for these fixtures, so <see cref="LogAssert.Expect(LogType, Regex)"/> is the
        /// correct, tightly scoped choice: it consumes exactly this one message and nothing else.
        /// <see cref="LogAssert.ignoreFailingMessages"/> was rejected because it suppresses ALL failing
        /// messages for its scope, which would mask genuine regressions. Call this immediately before
        /// the asset is imported/deleted and before <see cref="LogAssert.NoUnexpectedReceived"/>.
        /// </remarks>
        protected static void ExpectNoScriptAssetForScriptableObjectWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("No script asset for "));
        }

        private static void CleanupPackageRootGeneratedArtifacts()
        {
            string packageRoot = GetPackageRoot();
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                CleanupPackageRootGeneratedArtifactWithMeta(
                    Path.Combine(packageRoot, "test-results")
                );
            }

            string packagesFolder = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Packages")
            );
            string packageFolder = Path.Combine(
                packagesFolder,
                "com.wallstop-studios.unity-helpers"
            );

            CleanupPackageRootGeneratedArtifactWithMeta(
                Path.Combine(packageFolder, "test-results")
            );
        }

        private static string GetPackageRoot()
        {
            try
            {
                UnityEditor.PackageManager.PackageInfo packageInfo =
                    UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                        typeof(CommonTestBase).Assembly
                    );
                if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                {
                    return string.Empty;
                }

                return packageInfo.resolvedPath;
            }
            catch (Exception ex)
            {
                TestContext.WriteLine(
                    $"Failed to resolve unity-helpers package root for generated artifact cleanup: {ex.Message}"
                );
                return string.Empty;
            }
        }

        private static void CleanupPackageRootGeneratedArtifactWithMeta(string absolutePath)
        {
            if (ShouldPreservePackageRootArtifact(absolutePath))
            {
                return;
            }

            CleanupPackageRootGeneratedArtifact(absolutePath);
            CleanupPackageRootGeneratedArtifact(absolutePath + ".meta");
        }

        private static bool ShouldPreservePackageRootArtifact(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return true;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(absolutePath);
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                return isDirectory && !isReparsePoint;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (Exception ex)
            {
                TestContext.WriteLine(
                    $"Failed to inspect generated package-root test artifact '{absolutePath}': {ex.Message}"
                );
                return true;
            }
        }

        private static void CleanupPackageRootGeneratedArtifact(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

            try
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(absolutePath);
                }
                catch (FileNotFoundException)
                {
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    return;
                }

                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                if (isDirectory && !isReparsePoint)
                {
                    return;
                }

                if (isDirectory)
                {
                    Directory.Delete(absolutePath);
                }
                else
                {
                    File.Delete(absolutePath);
                }

                TestContext.WriteLine(
                    $"Removed generated package-root test artifact: {absolutePath}"
                );
            }
            catch (Exception ex)
            {
                TestContext.WriteLine(
                    $"Failed to remove generated package-root test artifact '{absolutePath}': {ex.Message}"
                );
            }
        }
#endif

        [OneTimeTearDown]
        public virtual void OneTimeTearDown()
        {
#if UNITY_EDITOR
            // Safety cleanup: ensure AssetDatabase is not stuck in batch mode
            // Use force reset at OneTimeTearDown for maximum cleanup
            // All AssetDatabase batching now uses the unified Editor.Utils.AssetDatabaseBatchHelper
            try
            {
                // Reset unified batch helper (handles all AssetDatabase state cleanup)
                Editor.Utils.AssetDatabaseBatchHelper.ForceResetAssetDatabase();
                // Reset legacy state in production code classes
                ScriptableObjectSingletonCreator.ResetAssetEditingScopeDepthForTesting();
                ScriptableObjectSingletonMetadataUtility.ResetAssetEditingDepthForTesting();
            }
            catch
            {
                // Best-effort cleanup - ignore exceptions during teardown
            }

            if (_trackedScenes.Count > 0)
            {
                CloseTrackedScenesInEditor();
            }
#endif

            DestroyTrackedObjects();

#if UNITY_EDITOR
            // Asset deletions above can schedule AssetPostprocessor drains. Flush them
            // synchronously so a late-arriving drain cannot land in the next fixture's
            // setup and pollute its handler statics. Covers fixtures that inherit
            // directly from CommonTestBase (not BatchedEditorTestBase) and would
            // otherwise escape the OneTime-flush discipline.
            try
            {
                WallstopStudios.UnityHelpers.Editor.AssetProcessors.AssetPostprocessorDeferral.FlushForTesting();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Best-effort during teardown — surface via log so diagnostics survive
                // without aborting the remainder of cleanup.
                Debug.LogException(ex);
            }
#endif

            if (_trackedDisposables.Count > 0)
            {
                for (int i = _trackedDisposables.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _trackedDisposables[i]?.Dispose();
                    }
                    catch
                    {
                        // ignore final teardown errors
                    }
                }
                _trackedDisposables.Clear();
            }

            if (_trackedAsyncDisposals.Count > 0)
            {
                foreach (Func<ValueTask> producer in _trackedAsyncDisposals.ToArray())
                {
                    try
                    {
                        producer?.Invoke();
                    }
                    catch
                    {
                        // ignore
                    }
                }
                _trackedAsyncDisposals.Clear();
            }

            DisposeDispatcherScope();
        }

        private void InitializeDispatcherScope()
        {
            DisposeDispatcherScope();
            _dispatcherScope = UnityMainThreadDispatcher.CreateTestScope(
                destroyImmediate: !Application.isPlaying
            );
        }

        private void DisposeDispatcherScope()
        {
            if (_dispatcherScope == null)
            {
                return;
            }

            _dispatcherScope.Dispose();
            _dispatcherScope = null;
        }

        private static string DrainUnityMainThreadDispatchersForTeardown()
        {
            const int MaxDrainPasses = 8;
            for (int i = 0; i < MaxDrainPasses; i++)
            {
                int pendingActionCount =
                    UnityMainThreadDispatcher.GetPendingActionCountForTesting();
                if (pendingActionCount <= 0)
                {
                    return null;
                }

                UnityMainThreadDispatcher.DrainPendingActionsForTesting();
            }

            int remainingPendingActionCount =
                UnityMainThreadDispatcher.GetPendingActionCountForTesting();
            if (remainingPendingActionCount <= 0)
            {
                return null;
            }

            return $"[uh-leak] {remainingPendingActionCount} UnityMainThreadDispatcher action(s) "
                + "remained queued after teardown drain of "
                + $"{TestContext.CurrentContext.Test.FullName}. "
                + UnityMainThreadDispatcher.DescribeLiveDispatchersForTesting();
        }

        private static async ValueTask UnloadSceneAsync(Scene scene)
        {
            if (!scene.IsValid())
            {
                return;
            }

            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            if (unload == null)
            {
                return;
            }

            // Bounded wait: a scene unload that never reports done (a batchmode edge case)
            // must not hang teardown forever -- that stalls the leg and loses results.xml.
            // Give up after a generous cap and surface it; the domain/editor tears down
            // regardless, so a not-yet-unloaded scene at this point is harmless.
            float endTime = Time.realtimeSinceStartup + TrackedDisposalTimeoutSeconds;
            while (!unload.isDone)
            {
                if (Time.realtimeSinceStartup > endTime)
                {
                    Debug.LogWarning(
                        $"[uh-leak] Scene '{scene.name}' did not finish unloading within "
                            + $"{TrackedDisposalTimeoutSeconds:0.###}s; abandoning the wait to "
                            + "avoid hanging the run."
                    );
                    return;
                }
                await Task.Yield();
            }
        }

#if UNITY_EDITOR
        private void CloseTrackedScenesInEditor()
        {
            for (int i = _trackedScenes.Count - 1; i >= 0; i--)
            {
                Scene scene = _trackedScenes[i];
                if (!scene.IsValid())
                {
                    continue;
                }

                try
                {
                    if (SceneManager.GetActiveScene() == scene)
                    {
                        TryPromoteAnotherScene(scene);
                    }

                    EditorSceneManager.CloseScene(scene, true);
                }
                catch
                {
                    // ignore
                }
            }

            _trackedScenes.Clear();
        }

        private static void TryPromoteAnotherScene(Scene current)
        {
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene candidate = SceneManager.GetSceneAt(i);
                if (candidate.IsValid() && candidate.isLoaded && candidate != current)
                {
                    SceneManager.SetActiveScene(candidate);
                    return;
                }
            }
        }

        /// <summary>
        /// Copies an asset file without triggering Unity's internal modal dialogs.
        /// This uses file system operations followed by asset database import instead of
        /// AssetDatabase.CopyAsset which can show dialogs in certain scenarios.
        /// </summary>
        /// <param name="sourcePath">Source asset path (relative to project root, e.g., "Assets/...").</param>
        /// <param name="destinationPath">Destination asset path (relative to project root).</param>
        /// <returns>True if the copy succeeded, false otherwise.</returns>
        protected static bool TryCopyAssetSilent(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
            {
                return false;
            }

            string absoluteSource = System
                .IO.Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    sourcePath
                )
                .SanitizePath();

            string absoluteDest = System
                .IO.Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    destinationPath
                )
                .SanitizePath();

            if (!System.IO.File.Exists(absoluteSource))
            {
                return false;
            }

            try
            {
                string destDir = System.IO.Path.GetDirectoryName(absoluteDest);
                if (!string.IsNullOrEmpty(destDir) && !System.IO.Directory.Exists(destDir))
                {
                    System.IO.Directory.CreateDirectory(destDir);
                }

                if (System.IO.File.Exists(absoluteDest))
                {
                    System.IO.File.Delete(absoluteDest);
                }

                System.IO.File.Copy(absoluteSource, absoluteDest);

                UnityEditor.AssetDatabase.ImportAsset(
                    destinationPath,
                    UnityEditor.ImportAssetOptions.ForceSynchronousImport
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures a folder exists both on disk and in the AssetDatabase.
        /// This prevents Unity's internal "Moving file failed" modal dialog.
        /// Tracks created folders for automatic cleanup during TearDown.
        /// </summary>
        /// <param name="folderPath">Unity relative path (e.g., Assets/Resources/Test).</param>
        /// <returns>List of folder paths that were created (for external tracking if needed).</returns>
        protected List<string> EnsureFolder(string folderPath)
        {
            List<string> createdFolders = new();

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return createdFolders;
            }

            folderPath = folderPath.SanitizePath();
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

            // Process each path segment to handle case-insensitive folder matching
            string[] parts = folderPath.Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string desiredName = parts[i];
                string intendedNext = current + "/" + desiredName;

                // First, check if folder already exists in AssetDatabase (exact match)
                if (UnityEditor.AssetDatabase.IsValidFolder(intendedNext))
                {
                    current = intendedNext;
                    continue;
                }

                // Check for case-insensitive match on disk first
                string actualFolderName = FindExistingFolderCaseInsensitive(
                    projectRoot,
                    current,
                    desiredName
                );
                if (actualFolderName != null)
                {
                    // Folder exists on disk with potentially different casing
                    string actualPath = current + "/" + actualFolderName;

                    // Import it into AssetDatabase if not already there
                    if (!UnityEditor.AssetDatabase.IsValidFolder(actualPath))
                    {
                        UnityEditor.AssetDatabase.ImportAsset(
                            actualPath,
                            UnityEditor.ImportAssetOptions.ForceSynchronousImport
                        );
                    }

                    current = actualPath;
                    continue;
                }

                // Folder doesn't exist on disk or in AssetDatabase - create it
                // First create on disk
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    string absoluteDirectory = System.IO.Path.Combine(projectRoot, intendedNext);
                    try
                    {
                        if (!System.IO.Directory.Exists(absoluteDirectory))
                        {
                            System.IO.Directory.CreateDirectory(absoluteDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"CommonTestBase.EnsureFolder: Failed to create directory on disk '{absoluteDirectory}': {ex.Message}"
                        );
                        return createdFolders;
                    }

                    // Import the newly created folder
                    UnityEditor.AssetDatabase.ImportAsset(
                        intendedNext,
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }

                // If it's still not valid, create via AssetDatabase (fallback)
                if (!UnityEditor.AssetDatabase.IsValidFolder(intendedNext))
                {
                    UnityEditor.AssetDatabase.CreateFolder(current, desiredName);
                }

                // Track folders we actually created (not pre-existing ones)
                TrackFolder(intendedNext);
                createdFolders.Add(intendedNext);
                current = intendedNext;
            }

            return createdFolders;
        }

        /// <summary>
        /// Finds an existing folder on disk that matches the desired name case-insensitively.
        /// Returns the actual folder name as it exists on disk, or null if not found.
        /// </summary>
        private static string FindExistingFolderCaseInsensitive(
            string projectRoot,
            string parentUnityPath,
            string desiredName
        )
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                return null;
            }

            string parentAbsolutePath = System.IO.Path.Combine(projectRoot, parentUnityPath);
            if (!System.IO.Directory.Exists(parentAbsolutePath))
            {
                return null;
            }

            try
            {
                foreach (string dir in System.IO.Directory.GetDirectories(parentAbsolutePath))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (string.Equals(name, desiredName, StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors
            }

            return null;
        }

        /// <summary>
        /// Static version of EnsureFolder that does not track folders.
        /// Use the instance method EnsureFolder() when you need automatic cleanup.
        /// </summary>
        protected static void EnsureFolderStatic(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            folderPath = folderPath.SanitizePath();
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

            // Process each path segment to handle case-insensitive folder matching
            string[] parts = folderPath.Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string desiredName = parts[i];
                string intendedNext = current + "/" + desiredName;

                // First, check if folder already exists in AssetDatabase (exact match)
                if (UnityEditor.AssetDatabase.IsValidFolder(intendedNext))
                {
                    current = intendedNext;
                    continue;
                }

                // Check for case-insensitive match on disk first
                string actualFolderName = FindExistingFolderCaseInsensitive(
                    projectRoot,
                    current,
                    desiredName
                );
                if (actualFolderName != null)
                {
                    // Folder exists on disk with potentially different casing
                    string actualPath = current + "/" + actualFolderName;

                    // Import it into AssetDatabase if not already there
                    if (!UnityEditor.AssetDatabase.IsValidFolder(actualPath))
                    {
                        UnityEditor.AssetDatabase.ImportAsset(
                            actualPath,
                            UnityEditor.ImportAssetOptions.ForceSynchronousImport
                        );
                    }

                    current = actualPath;
                    continue;
                }

                // Folder doesn't exist on disk or in AssetDatabase - create it
                // First create on disk
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    string absoluteDirectory = System.IO.Path.Combine(projectRoot, intendedNext);
                    try
                    {
                        if (!System.IO.Directory.Exists(absoluteDirectory))
                        {
                            System.IO.Directory.CreateDirectory(absoluteDirectory);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"CommonTestBase.EnsureFolderStatic: Failed to create directory on disk '{absoluteDirectory}': {ex.Message}"
                        );
                        return;
                    }

                    // Import the newly created folder
                    UnityEditor.AssetDatabase.ImportAsset(
                        intendedNext,
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }

                // If it's still not valid, create via AssetDatabase (fallback)
                if (!UnityEditor.AssetDatabase.IsValidFolder(intendedNext))
                {
                    UnityEditor.AssetDatabase.CreateFolder(current, desiredName);
                }

                current = intendedNext;
            }
        }

        /// <summary>
        /// Tracks a folder path for cleanup during TearDown.
        /// Only tracked folders will be deleted - pre-existing user folders are safe.
        /// </summary>
        /// <param name="folderPath">The Unity-relative folder path (e.g., "Assets/Temp/MyTest")</param>
        protected void TrackFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            string normalized = folderPath.SanitizePath();
            if (!_trackedFolders.Contains(normalized))
            {
                _trackedFolders.Add(normalized);
            }
        }

        /// <summary>
        /// Tracks an asset path for cleanup during TearDown.
        /// Only tracked assets will be deleted - pre-existing user assets are safe.
        /// </summary>
        /// <param name="assetPath">The Unity-relative asset path (e.g., "Assets/Temp/MyAsset.asset")</param>
        protected void TrackAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            string normalized = assetPath.SanitizePath();
            if (!_trackedAssetPaths.Contains(normalized))
            {
                _trackedAssetPaths.Add(normalized);
            }
        }

        /// <summary>
        /// Cleans up all tracked folders and assets that were created by this test.
        /// Only deletes folders/assets that were explicitly tracked - user data is safe.
        /// Folders are deleted in reverse order (deepest first) to handle nested structures.
        /// When <see cref="DeferAssetCleanupToOneTimeTearDown"/> is true, assets are accumulated
        /// for batch cleanup in OneTimeTearDown instead of being deleted immediately.
        /// </summary>
        protected void CleanupTrackedFoldersAndAssets()
        {
#if UNITY_EDITOR
            if (DeferAssetCleanupToOneTimeTearDown)
            {
                // Accumulate for batch cleanup later - don't delete or refresh yet
                _deferredAssetPaths.AddRange(_trackedAssetPaths);
                _deferredFolderPaths.AddRange(_trackedFolders);
                _trackedAssetPaths.Clear();
                _trackedFolders.Clear();
                return;
            }

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                // First, delete tracked assets
                foreach (string assetPath in _trackedAssetPaths)
                {
                    if (
                        !string.IsNullOrEmpty(assetPath)
                        && UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null
                    )
                    {
                        UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                    }
                }
                _trackedAssetPaths.Clear();

                // Sort folders by depth (deepest first) to delete children before parents
                List<string> sortedFolders = new(_trackedFolders);
                sortedFolders.Sort((a, b) => b.Split('/').Length.CompareTo(a.Split('/').Length));

                foreach (string folderPath in sortedFolders)
                {
                    if (
                        !string.IsNullOrEmpty(folderPath)
                        && UnityEditor.AssetDatabase.IsValidFolder(folderPath)
                    )
                    {
                        // Only delete if the folder is empty or contains only items we created
                        // For safety, we'll delete the folder - if it has unexpected contents,
                        // Unity will fail the delete which is fine
                        UnityEditor.AssetDatabase.DeleteAsset(folderPath);
                    }
                }
                _trackedFolders.Clear();
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
#endif
        }

        /// <summary>
        /// Polls frames until a Unity object reports as destroyed (its overloaded <c>== null</c>
        /// becomes true), or <paramref name="maxFrames"/> elapses. <see cref="Object.Destroy(Object)"/>
        /// is ASYNCHRONOUS in PlayMode — the managed wrapper is not nulled until Unity processes
        /// the deferred destruction, and the exact frame lag varies by editor version and CI load,
        /// so the "Destroy then one <c>yield return null</c>, then assert null" pattern is flaky.
        /// Poll instead. Returns quietly on timeout so the caller's own assertion produces the
        /// test-specific failure message. For <c>[UnityTest]</c> fixtures. Runtime-safe (no editor
        /// API), so it is available to the standalone player build too.
        /// </summary>
        protected static IEnumerator WaitUntilDestroyed(Object obj, int maxFrames = 30)
        {
            if (obj == null)
            {
                yield break;
            }

            Type objectType = obj.GetType();
            string objectName = obj.name;
            long objectId = obj.GetUnityObjectId();

            for (int i = 0; i < maxFrames; i++)
            {
                if (obj == null)
                {
                    yield break;
                }
                yield return null;
            }

            int liveObjectCount = -1;
            try
            {
                liveObjectCount = Resources.FindObjectsOfTypeAll(objectType).Length;
            }
            catch (Exception ex)
            {
                TestContext.WriteLine(
                    $"WaitUntilDestroyed failed to count live {objectType.FullName} objects: {ex.Message}"
                );
            }

            TestContext.WriteLine(
                $"WaitUntilDestroyed timed out after {maxFrames} frame(s). "
                    + $"Object '{objectName}' ({objectType.FullName}, instance {objectId}) "
                    + $"still reports alive. Application.isPlaying={Application.isPlaying}, "
                    + $"live objects of same type={liveObjectCount}."
            );
        }

#if UNITY_EDITOR
        /// <summary>
        /// Executes an action with immediate asset import enabled by pausing any active batch scope.
        /// Use this for operations that require immediate asset processing, such as
        /// <see cref="UnityEditor.AssetImporter.SaveAndReimport"/> or texture operations that need
        /// the asset to be fully imported before continuing.
        /// </summary>
        /// <param name="action">The action to execute outside of batch mode.</param>
        /// <param name="refreshAfter">Whether to refresh the asset database after the action completes. Default is false.</param>
        /// <remarks>
        /// <para>
        /// This method:
        /// </para>
        /// <list type="bullet">
        /// <item>Pauses the current batch scope (if any)</item>
        /// <item>Refreshes the AssetDatabase to ensure all pending operations are complete</item>
        /// <item>Executes the provided action</item>
        /// <item>Optionally refreshes the AssetDatabase again after the action (if <paramref name="refreshAfter"/> is true)</item>
        /// <item>Resumes the batch scope</item>
        /// </list>
        /// <para>
        /// Use sparingly, as each pause/resume cycle incurs overhead. Group related
        /// immediate operations together when possible.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// ExecuteWithImmediateImport(() =>
        /// {
        ///     TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        ///     importer.textureType = TextureImporterType.Sprite;
        ///     importer.SaveAndReimport();
        /// }, refreshAfter: true);
        /// </code>
        /// </example>
        protected void ExecuteWithImmediateImport(Action action, bool refreshAfter = false)
        {
            if (action == null)
            {
                return;
            }

            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                UnityEditor.AssetDatabase.Refresh(
                    UnityEditor.ImportAssetOptions.ForceSynchronousImport
                );
                action();
                if (refreshAfter)
                {
                    UnityEditor.AssetDatabase.Refresh(
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }
            }
        }

        /// <summary>
        /// Executes a function with immediate asset import enabled by pausing any active batch scope
        /// and returns the function's result.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The function to execute that returns a value.</param>
        /// <param name="refreshAfter">
        /// If <c>true</c>, a second <see cref="UnityEditor.AssetDatabase.Refresh"/> is called after <paramref name="func"/>
        /// completes to ensure any newly created or modified assets are available. Default is <c>false</c>.
        /// </param>
        /// <returns>The result of the function, or <c>default(T)</c> if <paramref name="func"/> is <c>null</c>.</returns>
        /// <remarks>
        /// This method is useful when you need to perform asset operations that return values while
        /// ensuring proper AssetDatabase synchronization. For void operations, use
        /// <see cref="ExecuteWithImmediateImport(Action, bool)"/> instead.
        /// </remarks>
        /// <example>
        /// <code>
        /// TextureImporter importer = ExecuteWithImmediateImport(() =>
        /// {
        ///     return AssetImporter.GetAtPath(texturePath) as TextureImporter;
        /// });
        /// </code>
        /// </example>
        protected T ExecuteWithImmediateImport<T>(Func<T> func, bool refreshAfter = false)
        {
            if (func == null)
            {
                return default;
            }

            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                UnityEditor.AssetDatabase.Refresh(
                    UnityEditor.ImportAssetOptions.ForceSynchronousImport
                );
                T result = func();
                if (refreshAfter)
                {
                    UnityEditor.AssetDatabase.Refresh(
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }
                return result;
            }
        }

        /// <summary>
        /// Coroutine: force the AssetDatabase to reconcile, then yield frames until the
        /// asset at <paramref name="assetPath"/> is no longer loadable (or the timeout
        /// elapses). A raw <see cref="System.IO.File.Delete(string)"/> or a deferred
        /// <see cref="UnityEditor.AssetDatabase.DeleteAsset(string)"/> becomes visible to
        /// the AssetDatabase ASYNCHRONOUSLY, and the lag differs BY EDITOR VERSION
        /// (2021.3 / 6000 retain the in-memory object longer than 2022.3), so the classic
        /// "one Refresh + one frame, then assert null" pattern is version-flaky. Poll
        /// instead. On timeout this returns quietly so the caller's own assertion produces
        /// the test-specific failure message. Editor-only; for <c>[UnityTest]</c> fixtures.
        /// </summary>
        protected static IEnumerator WaitUntilAssetUnloaded(
            string assetPath,
            float timeoutSeconds = 5f
        )
        {
            float endTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (true)
            {
                using (AssetDatabaseBatchHelper.PauseBatch())
                {
                    UnityEditor.AssetDatabase.Refresh(
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(assetPath) == null)
                {
                    yield break;
                }
                if (Time.realtimeSinceStartup > endTime)
                {
                    TestContext.WriteLine(
                        $"WaitUntilAssetUnloaded timed out after {timeoutSeconds:0.###} second(s). "
                            + DescribeAssetDatabaseState(assetPath)
                    );
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Coroutine: force the AssetDatabase to reconcile, then yield frames until the
        /// asset at <paramref name="assetPath"/> is loadable (or the timeout elapses).
        /// Editor-only; for <c>[UnityTest]</c> fixtures.
        /// </summary>
        protected static IEnumerator WaitUntilAssetLoaded(
            string assetPath,
            float timeoutSeconds = 5f
        )
        {
            float endTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (true)
            {
                using (AssetDatabaseBatchHelper.PauseBatch())
                {
                    UnityEditor.AssetDatabase.Refresh(
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
                {
                    yield break;
                }
                if (Time.realtimeSinceStartup > endTime)
                {
                    TestContext.WriteLine(
                        $"WaitUntilAssetLoaded timed out after {timeoutSeconds:0.###} second(s). "
                            + DescribeAssetDatabaseState(assetPath)
                    );
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Synchronous counterpart of <see cref="WaitUntilAssetUnloaded"/> for non-coroutine
        /// (<c>[Test]</c>) fixtures: repeatedly forces a synchronous AssetDatabase refresh
        /// until the asset at <paramref name="assetPath"/> is gone or
        /// <paramref name="maxRefreshes"/> is reached. Uses no real-time sleep (editor
        /// refreshes are synchronous), so it does not trip the UNH010 wait-time lint.
        /// Editor-only.
        /// </summary>
        protected static void ForceAssetUnloaded(string assetPath, int maxRefreshes = 10)
        {
            for (int i = 0; i < maxRefreshes; i++)
            {
                using (AssetDatabaseBatchHelper.PauseBatch())
                {
                    UnityEditor.AssetDatabase.Refresh(
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(assetPath) == null)
                {
                    return;
                }
            }

            TestContext.WriteLine(
                $"ForceAssetUnloaded timed out after {maxRefreshes} refresh(es). "
                    + DescribeAssetDatabaseState(assetPath)
            );
        }

        /// <summary>
        /// Polls until the folder at <paramref name="folderPath"/> becomes a valid AssetDatabase
        /// folder, forcing a synchronous refresh each iteration, or <paramref name="maxRefreshes"/>
        /// is reached. <see cref="UnityEditor.AssetDatabase.CreateFolder"/> becomes visible to
        /// <see cref="UnityEditor.AssetDatabase.IsValidFolder"/> ASYNCHRONOUSLY, and the lag varies
        /// by scheduling (a SINGLE_THREADED leg exposed a "create then one refresh, then assert
        /// valid" race that the default leg did not), so poll instead of assuming one refresh
        /// settles it. Returns quietly on timeout so the caller's own assertion fails specifically.
        /// Uses no real-time sleep (refreshes are synchronous), so it does not trip UNH010.
        /// Editor-only; for <c>[UnityTest]</c> fixtures.
        /// </summary>
        protected static IEnumerator WaitUntilFolderValid(string folderPath, int maxRefreshes = 10)
        {
            for (int i = 0; i < maxRefreshes; i++)
            {
                if (UnityEditor.AssetDatabase.IsValidFolder(folderPath))
                {
                    yield break;
                }
                using (AssetDatabaseBatchHelper.PauseBatch())
                {
                    UnityEditor.AssetDatabase.Refresh(
                        UnityEditor.ImportAssetOptions.ForceSynchronousImport
                    );
                }
                yield return null;
            }

            TestContext.WriteLine(
                $"WaitUntilFolderValid timed out after {maxRefreshes} refresh(es). "
                    + $"folderPath='{folderPath}', "
                    + $"isValidFolder={UnityEditor.AssetDatabase.IsValidFolder(folderPath)}."
            );
        }

        private static string DescribeAssetDatabaseState(string assetPath)
        {
            string absolutePath = string.Empty;
            string metaPath = string.Empty;
            bool fileExists = false;
            bool metaExists = false;
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(projectRoot) && !string.IsNullOrEmpty(assetPath))
                {
                    absolutePath = Path.Combine(
                        projectRoot,
                        assetPath.Replace('/', Path.DirectorySeparatorChar)
                    );
                    metaPath = absolutePath + ".meta";
                    fileExists = File.Exists(absolutePath);
                    metaExists = File.Exists(metaPath);
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine(
                    $"Failed to inspect filesystem state for asset '{assetPath}': {ex.Message}"
                );
            }

            Object loadedAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            UnityEditor.AssetImporter importer = UnityEditor.AssetImporter.GetAtPath(assetPath);
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(assetPath);

            return $"assetPath='{assetPath}', absolutePath='{absolutePath}', "
                + $"fileExists={fileExists}, metaPath='{metaPath}', metaExists={metaExists}, "
                + $"guid='{guid}', loadedAsset={DescribeUnityObject(loadedAsset)}, "
                + $"importer={(importer == null ? "null" : importer.GetType().FullName)}.";
        }

        private static string DescribeUnityObject(Object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            return $"{obj.GetType().FullName}('{obj.name}', instance {obj.GetUnityObjectId()})";
        }
#endif

        /// <summary>
        /// Performs batch cleanup of all deferred assets and folders.
        /// Call this in OneTimeTearDown when <see cref="DeferAssetCleanupToOneTimeTearDown"/> is true.
        /// </summary>
        protected void CleanupDeferredAssetsAndFolders()
        {
#if UNITY_EDITOR
            if (_deferredAssetPaths.Count == 0 && _deferredFolderPaths.Count == 0)
            {
                return;
            }

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                // Delete all accumulated assets
                foreach (string assetPath in _deferredAssetPaths)
                {
                    if (
                        !string.IsNullOrEmpty(assetPath)
                        && UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null
                    )
                    {
                        UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                    }
                }
                _deferredAssetPaths.Clear();

                // Sort folders by depth (deepest first) and delete
                List<string> sortedFolders = new(_deferredFolderPaths);
                sortedFolders.Sort((a, b) => b.Split('/').Length.CompareTo(a.Split('/').Length));

                foreach (string folderPath in sortedFolders)
                {
                    if (
                        !string.IsNullOrEmpty(folderPath)
                        && UnityEditor.AssetDatabase.IsValidFolder(folderPath)
                    )
                    {
                        UnityEditor.AssetDatabase.DeleteAsset(folderPath);
                    }
                }
                _deferredFolderPaths.Clear();
            }

            // Single refresh at end of all cleanup
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
#endif
        }

        /// <summary>
        /// Cleans up all known test folders in Assets and Assets/Resources, including duplicates.
        /// This should be called in OneTimeSetUp and OneTimeTearDown to ensure clean test state.
        /// </summary>
        /// <remarks>
        /// Handles folders like:
        /// - Assets/Resources: CreatorTests, Deep, Lifecycle, Loose, Multi, etc.
        /// - Assets: Temp and its duplicates (Temp 1, Temp 2, etc.)
        /// - Assets/Resources: Wallstop Studios duplicates (Wallstop Studios 1, etc.)
        ///
        /// This method automatically batches its operations when not already inside a batch scope.
        /// If called from within an existing batch scope, it will respect that scope.
        /// </remarks>
        protected static void CleanupAllKnownTestFolders()
        {
            // Use batching if not already in a batch scope to improve performance
            // and ensure atomic cleanup operations
            bool shouldBatch = !AssetDatabaseBatchHelper.IsCurrentlyBatching;
            IDisposable batchScope = shouldBatch
                ? AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true)
                : null;

            try
            {
                CleanupAllKnownTestFoldersInternal();
            }
            finally
            {
                batchScope?.Dispose();
            }
        }

        /// <summary>
        /// Internal implementation of CleanupAllKnownTestFolders that performs the actual cleanup.
        /// This method assumes it's called either within a batch scope or that batching is not needed.
        /// </summary>
        private static void CleanupAllKnownTestFoldersInternal()
        {
            // List of test folder patterns to clean up (relative to Assets/Resources)
            // IMPORTANT: If you update this list, also update CleanupAllKnownTestFoldersTests.ResourcesTestFolderPatterns()
            string[] resourcesTestFolderPatterns = new[]
            {
                "CreatorTests",
                "Deep",
                "Lifecycle",
                "Loose",
                "Multi",
                "MultiNatural",
                "SingleLevel",
                "Tests",
                "DuplicateCleanupTests",
                "CaseTest",
                "cASEtest",
                "CASETEST",
                "casetest",
                "CaseTEST",
                "CustomPath",
                "Missing",
            };

            // List of test folder patterns to clean up (relative to Assets)
            // Note: "Temp" will also match "Temp 1", "Temp 2", etc. due to duplicate handling
            // IMPORTANT: If you update this list, also update CleanupAllKnownTestFoldersTests.AssetsTestFolderPatterns()
            string[] assetsTestFolderPatterns = new[]
            {
                "Temp",
                "TempCleanupIntegrationTests",
                "TempMultiFileSelectorTests",
                "TempSpriteApplierTests",
                "TempSpriteApplierAdditional",
                "TempSpriteHelpersTests",
                "TempObjectHelpersEditorTests",
                "TempHelpersPrefabs",
                "TempHelpersScriptables",
                "TempColorExtensionTests",
                "TempTestFolder",
                "TestFolder",
                "__LlmArtifactCleanerTests__",
                "__DetectAssetChangedTests__",
            };

            // Also clean up duplicate Wallstop Studios folders
            string[] wallstopDuplicatePatterns = new[] { "Wallstop Studios" };

            // Clean up duplicate "Unity Helpers" folders inside Wallstop Studios
            string[] unityHelpersDuplicatePatterns = new[] { "Unity Helpers" };

            // Clean up duplicate "Resources" folders (e.g., "Resources 1", "Resources 2", etc.)
            // These are created when parallel tests or failed cleanup leaves orphaned folders
            string[] resourcesDuplicatePatterns = new[] { "Resources" };

            string resourcesRoot = "Assets/Resources";
            string assetsRoot = "Assets";
            string wallstopStudiosRoot = "Assets/Resources/Wallstop Studios";

            // Clean up test folders in Assets/Resources and their duplicates
            foreach (string pattern in resourcesTestFolderPatterns)
            {
                CleanupFolderAndDuplicates(resourcesRoot, pattern);
            }

            // Clean up test folders in Assets and their duplicates
            foreach (string pattern in assetsTestFolderPatterns)
            {
                CleanupFolderAndDuplicates(assetsRoot, pattern);
            }

            // Clean up Wallstop Studios duplicates (not the main folder)
            foreach (string pattern in wallstopDuplicatePatterns)
            {
                CleanupDuplicateFoldersOnly(resourcesRoot, pattern);
            }

            // Clean up Unity Helpers duplicates inside Wallstop Studios folder (not the main folder)
            foreach (string pattern in unityHelpersDuplicatePatterns)
            {
                CleanupDuplicateFoldersOnly(wallstopStudiosRoot, pattern);
            }

            // Clean up Resources duplicates in Assets folder (e.g., "Resources 1", "Resources 2")
            foreach (string pattern in resourcesDuplicatePatterns)
            {
                CleanupDuplicateFoldersOnly(assetsRoot, pattern);
            }

            // Also clean up from disk to handle orphaned folders
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string resourcesOnDisk = System.IO.Path.Combine(projectRoot, "Assets", "Resources");
                if (System.IO.Directory.Exists(resourcesOnDisk))
                {
                    foreach (string pattern in resourcesTestFolderPatterns)
                    {
                        CleanupFolderAndDuplicatesOnDisk(resourcesOnDisk, pattern);
                    }

                    foreach (string pattern in wallstopDuplicatePatterns)
                    {
                        CleanupDuplicateFoldersOnlyOnDisk(resourcesOnDisk, pattern);
                    }

                    // Clean up Unity Helpers duplicates inside Wallstop Studios folder on disk
                    string wallstopOnDisk = System.IO.Path.Combine(
                        resourcesOnDisk,
                        "Wallstop Studios"
                    );
                    if (System.IO.Directory.Exists(wallstopOnDisk))
                    {
                        foreach (string pattern in unityHelpersDuplicatePatterns)
                        {
                            CleanupDuplicateFoldersOnlyOnDisk(wallstopOnDisk, pattern);
                        }
                    }
                }

                // Clean up Temp folders in Assets
                string assetsOnDisk = System.IO.Path.Combine(projectRoot, "Assets");
                if (System.IO.Directory.Exists(assetsOnDisk))
                {
                    foreach (string pattern in assetsTestFolderPatterns)
                    {
                        CleanupFolderAndDuplicatesOnDisk(assetsOnDisk, pattern);
                    }

                    // Clean up Resources duplicates on disk (e.g., "Resources 1", "Resources 2")
                    foreach (string pattern in resourcesDuplicatePatterns)
                    {
                        CleanupDuplicateFoldersOnlyOnDisk(assetsOnDisk, pattern);
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a folder and all its duplicates (e.g., "Folder", "Folder 1", "Folder 2").
        /// </summary>
        private static void CleanupFolderAndDuplicates(string parentPath, string folderName)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(parentPath))
            {
                return;
            }

            string[] subFolders = UnityEditor.AssetDatabase.GetSubFolders(parentPath);
            if (subFolders == null)
            {
                return;
            }

            foreach (string folder in subFolders)
            {
                string name = System.IO.Path.GetFileName(folder);
                if (name == null)
                {
                    continue;
                }

                // Check exact match or duplicate pattern (e.g., "Folder 1", "Folder 2")
                if (
                    string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase)
                    || IsDuplicateFolder(name, folderName)
                )
                {
                    DeleteFolderRecursivelyWithContents(folder);
                }
            }
        }

        /// <summary>
        /// Deletes only duplicate folders (e.g., "Folder 1", "Folder 2") but NOT the main folder.
        /// </summary>
        private static void CleanupDuplicateFoldersOnly(string parentPath, string folderName)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(parentPath))
            {
                return;
            }

            string[] subFolders = UnityEditor.AssetDatabase.GetSubFolders(parentPath);
            if (subFolders == null)
            {
                return;
            }

            foreach (string folder in subFolders)
            {
                string name = System.IO.Path.GetFileName(folder);
                if (name == null)
                {
                    continue;
                }

                // Only delete duplicates, not the main folder
                if (IsDuplicateFolder(name, folderName))
                {
                    DeleteFolderRecursivelyWithContents(folder);
                }
            }
        }

        /// <summary>
        /// Checks if a folder name matches the pattern "BaseName N" where N is a positive integer.
        /// Unity creates duplicate folders with names like "Folder 1", "Folder 2", etc.
        /// </summary>
        private static bool IsDuplicateFolder(string actualName, string baseName)
        {
            if (!actualName.StartsWith(baseName + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = actualName.Substring(baseName.Length + 1);

            // Reject if suffix starts with whitespace (handles double-space like "Folder  1")
            // int.TryParse would otherwise accept " 1" as valid since it trims whitespace
            if (suffix.Length == 0 || char.IsWhiteSpace(suffix[0]))
            {
                return false;
            }

            // Only positive integers are valid duplicates (Unity uses 1, 2, 3, etc.)
            return int.TryParse(suffix, out int number) && number > 0;
        }

        /// <summary>
        /// List of protected production folder paths that should NEVER be deleted by tests.
        /// These paths are case-insensitive.
        /// </summary>
        private static readonly string[] ProtectedFolders = new[]
        {
            "Assets/Resources/Wallstop Studios",
            "Assets/Plugins",
            "Assets/Editor Default Resources",
            "Assets/StreamingAssets",
        };

        /// <summary>
        /// Known folder base names whose numbered duplicates (e.g., "Unity Helpers 1") should be
        /// considered pollution and NOT protected. The main folders remain protected.
        /// </summary>
        private static readonly (
            string parentPath,
            string baseName
        )[] KnownDuplicateFolderPatterns = new[]
        {
            ("Assets/Resources/Wallstop Studios", "Unity Helpers"),
            ("Assets/Resources", "Wallstop Studios"),
            ("Assets", "Resources"), // "Resources 1", "Resources 2", etc. are pollution
            ("Assets", "Temp"), // "Temp 1", "Temp 2", etc. are pollution
        };

        /// <summary>
        /// Checks if a path represents a numbered duplicate folder that is pollution, not production.
        /// For example, "Assets/Resources/Wallstop Studios/Unity Helpers 1" is pollution.
        /// </summary>
        private static bool IsKnownDuplicatePollution(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = path.SanitizePath();
            foreach ((string parentPath, string baseName) in KnownDuplicateFolderPatterns)
            {
                string prefix = parentPath + "/" + baseName + " ";
                if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string remainder = normalizedPath.Substring(prefix.Length);
                    int slashIndex = remainder.IndexOf('/');
                    string folderSuffix =
                        slashIndex >= 0 ? remainder.Substring(0, slashIndex) : remainder;
                    if (int.TryParse(folderSuffix, out _))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a path is or is under a protected production folder.
        /// Returns false for known duplicate pollution folders (e.g., "Unity Helpers 1").
        /// </summary>
        private static bool IsProtectedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalizedPath = path.SanitizePath();

            if (IsKnownDuplicatePollution(normalizedPath))
            {
                return false;
            }

            foreach (string protectedFolder in ProtectedFolders)
            {
                if (
                    string.Equals(
                        normalizedPath,
                        protectedFolder,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || normalizedPath.StartsWith(
                        protectedFolder + "/",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Internal test hooks for verifying protection path logic.
        /// </summary>
        protected internal static class ProtectionTestHooks
        {
            /// <summary>
            /// Exposes IsProtectedPath for testing.
            /// </summary>
            public static bool TestIsProtectedPath(string path) => IsProtectedPath(path);

            /// <summary>
            /// Exposes IsKnownDuplicatePollution for testing.
            /// </summary>
            public static bool TestIsKnownDuplicatePollution(string path) =>
                IsKnownDuplicatePollution(path);

            /// <summary>
            /// Gets the list of protected folders for verification.
            /// </summary>
            public static string[] GetProtectedFolders() => ProtectedFolders;

            /// <summary>
            /// Gets the list of known duplicate folder patterns for verification.
            /// </summary>
            public static (
                string parentPath,
                string baseName
            )[] GetKnownDuplicateFolderPatterns() => KnownDuplicateFolderPatterns;
        }

        /// <summary>
        /// Deletes a folder and all its contents through AssetDatabase.
        /// IMPORTANT: Will NOT delete protected production folders.
        /// </summary>
        private static void DeleteFolderRecursivelyWithContents(string folderPath)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            // CRITICAL: Never delete protected production folders
            if (IsProtectedPath(folderPath))
            {
                Debug.LogWarning(
                    $"[CommonTestBase] Refusing to delete protected production folder: {folderPath}. "
                        + "This is a safety measure to prevent accidental deletion of production assets during tests."
                );
                return;
            }

            // First delete all assets in this folder (not recursively - subfolders will be handled)
            string[] assetGuids = UnityEditor.AssetDatabase.FindAssets(
                string.Empty,
                new[] { folderPath }
            );
            if (assetGuids != null)
            {
                foreach (string guid in assetGuids)
                {
                    string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (
                        !string.IsNullOrEmpty(assetPath)
                        && !UnityEditor.AssetDatabase.IsValidFolder(assetPath)
                    )
                    {
                        // Double-check this asset is not in a protected folder
                        if (IsProtectedPath(assetPath))
                        {
                            Debug.LogWarning(
                                $"[CommonTestBase] Refusing to delete protected asset: {assetPath}"
                            );
                            continue;
                        }

                        UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                    }
                }
            }

            // Then delete subfolders recursively
            string[] subFolders = UnityEditor.AssetDatabase.GetSubFolders(folderPath);
            if (subFolders != null)
            {
                foreach (string sub in subFolders)
                {
                    DeleteFolderRecursivelyWithContents(sub);
                }
            }

            // Finally delete the folder itself (only if not protected)
            if (!IsProtectedPath(folderPath))
            {
                UnityEditor.AssetDatabase.DeleteAsset(folderPath);
            }
        }

        /// <summary>
        /// Converts a disk path to a Unity relative path for protection checking.
        /// </summary>
        private static string DiskPathToUnityRelativePath(string diskPath)
        {
            if (string.IsNullOrEmpty(diskPath))
            {
                return string.Empty;
            }

            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return string.Empty;
            }

            string normalizedDiskPath = diskPath.SanitizePath();
            string normalizedProjectRoot = projectRoot.SanitizePath();

            if (
                normalizedDiskPath.StartsWith(
                    normalizedProjectRoot + "/",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return normalizedDiskPath.Substring(normalizedProjectRoot.Length + 1);
            }

            return string.Empty;
        }

        /// <summary>
        /// Cleans up folders on disk (handles orphaned folders not in AssetDatabase).
        /// IMPORTANT: Will NOT delete protected production folders.
        /// </summary>
        private static void CleanupFolderAndDuplicatesOnDisk(string parentPath, string folderName)
        {
            if (!System.IO.Directory.Exists(parentPath))
            {
                return;
            }

            try
            {
                foreach (string dir in System.IO.Directory.GetDirectories(parentPath))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (name == null)
                    {
                        continue;
                    }

                    if (
                        string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase)
                        || IsDuplicateFolder(name, folderName)
                    )
                    {
                        // Check if this would be a protected path
                        string unityPath = DiskPathToUnityRelativePath(dir);
                        if (!string.IsNullOrEmpty(unityPath) && IsProtectedPath(unityPath))
                        {
                            Debug.LogWarning(
                                $"[CommonTestBase] Refusing to delete protected folder on disk: {dir}"
                            );
                            continue;
                        }

                        try
                        {
                            System.IO.Directory.Delete(dir, recursive: true);
                        }
                        catch
                        {
                            // Ignore - folder may be locked
                        }
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors
            }
        }

        /// <summary>
        /// Cleans up duplicate folders on disk (not the main folder).
        /// IMPORTANT: Will NOT delete protected production folders.
        /// </summary>
        private static void CleanupDuplicateFoldersOnlyOnDisk(string parentPath, string folderName)
        {
            if (!System.IO.Directory.Exists(parentPath))
            {
                return;
            }

            try
            {
                foreach (string dir in System.IO.Directory.GetDirectories(parentPath))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (name == null)
                    {
                        continue;
                    }

                    if (IsDuplicateFolder(name, folderName))
                    {
                        // Check if this would be a protected path
                        string unityPath = DiskPathToUnityRelativePath(dir);
                        if (!string.IsNullOrEmpty(unityPath) && IsProtectedPath(unityPath))
                        {
                            Debug.LogWarning(
                                $"[CommonTestBase] Refusing to delete protected folder on disk: {dir}"
                            );
                            continue;
                        }

                        try
                        {
                            System.IO.Directory.Delete(dir, recursive: true);
                        }
                        catch
                        {
                            // Ignore - folder may be locked
                        }
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors
            }
        }
#endif
    }
}
