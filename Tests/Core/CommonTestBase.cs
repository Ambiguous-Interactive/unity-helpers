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
        private UnityMainThreadDispatcher.AutoCreationScope _dispatcherScope;

        protected readonly List<Object> _trackedObjects = new();
        protected readonly List<IDisposable> _trackedDisposables = new();
        protected readonly List<Scene> _trackedScenes = new();
        protected readonly List<Func<ValueTask>> _trackedAsyncDisposals = new();

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
            if (_trackedAsyncDisposals.Count > 0)
            {
                foreach (Func<ValueTask> producer in _trackedAsyncDisposals.ToArray())
                {
                    if (producer == null)
                    {
                        continue;
                    }

                    ValueTask task = producer();
                    while (!task.IsCompleted)
                    {
                        yield return null;
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
                    yield return null;
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
                    Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>().Length > 0
                    && dispatcherDestroyFrames > 0
                )
                {
                    dispatcherDestroyFrames--;
                    yield return null;
                }

                RuntimeSingletonRegistry.ClearAllRegisteredInstances();

                // Leak diagnostic: a UnityMainThreadDispatcher still resident after the scope tore
                // down + the registry cleared means a leak the cleanup could not reach (an orphaned
                // duplicate). Surface it as one [uh-leak] line naming the just-finished test so a
                // regression self-identifies in unity.log (like the test streamer does for [Error]s)
                // without failing the run. FindObjectsOfTypeAll over this one type is cheap.
                int residentDispatchers = Resources
                    .FindObjectsOfTypeAll<UnityMainThreadDispatcher>()
                    .Length;
                if (residentDispatchers > 0)
                {
                    Debug.LogWarning(
                        $"[uh-leak] {residentDispatchers} UnityMainThreadDispatcher object(s) "
                            + $"still resident after teardown of "
                            + $"{TestContext.CurrentContext.Test.FullName}."
                    );
                }
            }

            // Cross-test log-pollution guard (PlayMode only). A synchronous or late-flushed
            // [Error] otherwise bleeds across the frame boundary into the NEXT test's scope, so
            // an innocent later test fails for an error this (or an earlier) fixture produced.
            // Pump one frame to flush any pending logs, then reconcile so an UNEXPECTED [Error]
            // fails THIS fixture -- where it can be fixed with a LogAssert.Expect -- instead of a
            // bystander. Compliant tests that LogAssert.Expect their errors are unaffected.
            // EditMode tests reconcile synchronously at test end already (no frame bleed), so this
            // is scoped to PlayMode to keep the green EditMode legs untouched.
            if (Application.isPlaying)
            {
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
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
            UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
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

            while (!unload.isDone)
            {
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
