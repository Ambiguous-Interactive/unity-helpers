// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    [TestFixture]
    [Category("Fast")]
    public sealed class SceneHelperLifetimeTests : CommonTestBase
    {
        private const string PackedScenePath =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Runtime/Scenes/Test1.unity";
        private static bool[] ActiveStates { get; } = { false, true };
        private static int[] ObjectCounts { get; } = { 0, 1, 7 };
        private static string[] ScenePaths { get; } =
            {
                PackedScenePath,
                PackedScenePath.Replace('/', '\\'),
                "Test1",
                Path.ChangeExtension(PackedScenePath, null),
                "Tests/Runtime/Scenes/Test1",
            };

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (
            SceneHelper.SceneLoadScope scope,
            WeakReference payload
        ) CreateCapturedScope()
        {
            object payload = new object();
            SceneHelper.SceneLoadScope scope = new(
                PackedScenePath,
                (scene, mode) => GC.KeepAlive(payload)
            );
            return (scope, new WeakReference(payload));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnrootedPayload()
        {
            return new WeakReference(new object());
        }

        private static IEnumerator WaitForCompletion(Task task)
        {
            float started = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - started < 15f)
            {
                yield return null;
            }
            Assert.IsTrue(
                task.IsCompleted,
                "The scene operation did not settle within 15 seconds."
            );
        }

        [SetUp]
        public void ProvisionBuildSceneAliases()
        {
#if UNITY_EDITOR
            EditorBuildSettingsScene[] originalBuildScenes = EditorBuildSettings.scenes;
            TrackAsyncDisposal(() =>
            {
                EditorBuildSettings.scenes = originalBuildScenes;
                return new ValueTask();
            });
            EditorBuildSettingsScene[] provisioned = new EditorBuildSettingsScene[
                originalBuildScenes.Length + 1
            ];
            Array.Copy(originalBuildScenes, provisioned, originalBuildScenes.Length);
            provisioned[originalBuildScenes.Length] = new EditorBuildSettingsScene(
                PackedScenePath,
                true
            );
            EditorBuildSettings.scenes = provisioned;
#endif
        }

        [UnityTest]
        public IEnumerator ScopeReleasesCallbackCaptureAfterLoadingOrPendingDisposal(
            [ValueSource(nameof(ActiveStates))] bool disposeBeforeLoading
        )
        {
            object rootedControl = new object();
            WeakReference rooted = new(rootedControl);
            WeakReference released = CreateUnrootedPayload();
            for (int iteration = 0; iteration < 3; ++iteration)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                yield return null;
            }
            Assert.IsTrue(rooted.IsAlive, "The rooted collector control must survive.");
            Assert.IsFalse(released.IsAlive, "The collector must release the unrooted control.");
            (SceneHelper.SceneLoadScope scope, WeakReference payload) = CreateCapturedScope();
            TrackAsyncDisposal(scope.DisposeAsync);
            Task disposal = disposeBeforeLoading ? scope.DisposeAsync().AsTask() : null;
            yield return WaitForCompletion(scope.LoadTask);
            Assert.IsTrue(scope.LoadTask.IsCompletedSuccessfully);
            if (disposal != null)
            {
                yield return WaitForCompletion(disposal);
                Assert.IsTrue(disposal.IsCompletedSuccessfully);
            }
            for (int iteration = 0; iteration < 3; ++iteration)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                yield return null;
            }
            Assert.IsFalse(
                payload.IsAlive,
                "The retained scope must release its completed/discarded callback capture."
            );
            GC.KeepAlive(scope);
            GC.KeepAlive(rootedControl);
        }

        [UnityTest]
        public IEnumerator LoadedSceneRetrievalBorrowsActiveAndNonactiveScenes(
            [ValueSource(nameof(ActiveStates))] bool active,
            [ValueSource(nameof(ObjectCounts))] int count
        )
        {
            Assert.IsTrue(
                Application.isPlaying,
                "This fixture requires the runtime scene lifecycle."
            );
            Scene previous = SceneManager.GetActiveScene();
            Scene target = CreateTempScene(
                nameof(LoadedSceneRetrievalBorrowsActiveAndNonactiveScenes),
                setActive: false
            );
            SpriteRenderer[] expected = new SpriteRenderer[count];
            for (int index = 0; index < count; ++index)
            {
                GameObject owned = Track(
                    new GameObject(nameof(LoadedSceneRetrievalBorrowsActiveAndNonactiveScenes))
                );
                SceneManager.MoveGameObjectToScene(owned, target);
                expected[index] = owned.AddComponent<SpriteRenderer>();
                owned.SetActive(index != 0);
            }
            GameObject outside = Track(new GameObject("OutsideScene"));
            SceneManager.MoveGameObjectToScene(outside, previous);
            outside.AddComponent<SpriteRenderer>();
            if (active)
            {
                SceneManager.SetActiveScene(target);
            }
            Scene activeBefore = SceneManager.GetActiveScene();
            int scenesBefore = SceneManager.sceneCount;
            Scene callbackScene = default;
            LoadSceneMode callbackMode = default;
            int callbacks = 0;
            SceneHelper.SceneLoadScope borrowedScope = new(
                target.name,
                (scene, mode) =>
                {
                    callbackScene = scene;
                    callbackMode = mode;
                    ++callbacks;
                }
            );
            TrackAsyncDisposal(borrowedScope.DisposeAsync);
            Assert.IsTrue(borrowedScope.LoadTask.IsCompletedSuccessfully);
            Assert.AreEqual(target, callbackScene);
            Assert.AreEqual(active ? LoadSceneMode.Single : LoadSceneMode.Additive, callbackMode);
            Assert.AreEqual(1, callbacks);
            Assert.IsTrue(borrowedScope.DisposeAsync().IsCompletedSuccessfully);

            ValueTask<DeferredDisposalResult<SpriteRenderer[]>> all =
                SceneHelper.GetAllObjectsOfTypeInScene<SpriteRenderer>(target.name);
            Assert.IsTrue(all.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer[]> allResult = all.Result;
            TrackAsyncDisposal(allResult.DisposeAsync);
            CollectionAssert.AreEquivalent(expected, allResult.result);
            ValueTask<DeferredDisposalResult<SpriteRenderer>> single =
                SceneHelper.GetObjectOfTypeInScene<SpriteRenderer>(target.name);
            Assert.IsTrue(single.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer> singleResult = single.Result;
            TrackAsyncDisposal(singleResult.DisposeAsync);
            if (count == 0)
            {
                Assert.IsTrue(singleResult.result == null);
            }
            else
            {
                CollectionAssert.Contains(expected, singleResult.result);
            }
            Task disposeAll = allResult.DisposeAsync().AsTask();
            Task disposeSingle = singleResult.DisposeAsync().AsTask();
            yield return WaitForCompletion(disposeAll);
            yield return WaitForCompletion(disposeSingle);
            Assert.IsTrue(disposeAll.IsCompletedSuccessfully);
            Assert.IsTrue(disposeSingle.IsCompletedSuccessfully);
            Assert.IsTrue(target.isLoaded);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
            Assert.AreEqual(activeBefore, SceneManager.GetActiveScene());
        }

        [UnityTest]
        public IEnumerator LoadedNameWinsOverBuildSceneAlias()
        {
            Scene borrowed = CreateTempScene("Test1", setActive: false);
            GameObject owned = Track(new GameObject(nameof(LoadedNameWinsOverBuildSceneAlias)));
            SceneManager.MoveGameObjectToScene(owned, borrowed);
            SpriteRenderer expected = owned.AddComponent<SpriteRenderer>();
            int scenesBefore = SceneManager.sceneCount;
            Task<DeferredDisposalResult<SpriteRenderer[]>> task = SceneHelper
                .GetAllObjectsOfTypeInScene<SpriteRenderer>(borrowed.name)
                .AsTask();
            yield return WaitForCompletion(task);
            Assert.IsTrue(task.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer[]> result = task.Result;
            TrackAsyncDisposal(result.DisposeAsync);
            CollectionAssert.AreEqual(new[] { expected }, result.result);
            Task disposal = result.DisposeAsync().AsTask();
            yield return WaitForCompletion(disposal);
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
            Assert.IsTrue(borrowed.isLoaded);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
        }

        [UnityTest]
        public IEnumerator RetrievalDisposalSurvivesDispatcherTeardown(
            [ValueSource(nameof(ActiveStates))] bool fromBackground
        )
        {
            Task<DeferredDisposalResult<SpriteRenderer[]>> task = SceneHelper
                .GetAllObjectsOfTypeInScene<SpriteRenderer>(PackedScenePath)
                .AsTask();
            yield return WaitForCompletion(task);
            Assert.IsTrue(task.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer[]> result = task.Result;
            TrackAsyncDisposal(result.DisposeAsync);
            Scene loaded = result.result[0].gameObject.scene;
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Track(dispatcher.gameObject);
            dispatcher.enabled = false;
            Task disposal = fromBackground
                ? Task.Run(async () => await result.DisposeAsync())
                : result.DisposeAsync().AsTask();
            UnityEngine.Object.Destroy(dispatcher.gameObject);
            yield return WaitForCompletion(disposal);
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
            Assert.IsFalse(loaded.isLoaded);
        }

        [UnityTest]
        public IEnumerator PackedSceneRetrievalOwnsOnlyItsLoadedScene(
            [ValueSource(nameof(ScenePaths))] string scenePath
        )
        {
            Assert.IsTrue(Application.isPlaying);
#if !UNITY_EDITOR
            Assert.IsTrue(
                Application.CanStreamedLevelBeLoaded(PackedScenePath),
                "The test scene must be baked into the player."
            );
            Assert.IsFalse(
                File.Exists(PackedScenePath),
                "This control must use a packed scene without its source asset."
            );
            Assert.IsFalse(
                File.Exists(
                    Path.Combine(Path.GetDirectoryName(Application.dataPath), PackedScenePath)
                )
            );
#endif
            Scene original = SceneManager.GetActiveScene();
            int scenesBefore = SceneManager.sceneCount;
            Task<DeferredDisposalResult<SpriteRenderer[]>> allTask = SceneHelper
                .GetAllObjectsOfTypeInScene<SpriteRenderer>(scenePath)
                .AsTask();
            yield return WaitForCompletion(allTask);
            Assert.IsTrue(allTask.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer[]> all = allTask.Result;
            TrackAsyncDisposal(all.DisposeAsync);
            Assert.AreEqual(7, all.result.Length);
            Scene loaded = all.result[0].gameObject.scene;
            Assert.IsTrue(loaded.isLoaded);
            Assert.AreEqual(original, SceneManager.GetActiveScene());
            Assert.AreEqual(scenesBefore + 1, SceneManager.sceneCount);
            foreach (SpriteRenderer renderer in all.result)
            {
                if (renderer == null)
                {
                    Assert.Fail("Every result must be a live sprite renderer.");
                    continue;
                }
                Assert.AreEqual(loaded, renderer.gameObject.scene);
            }
            Task<DeferredDisposalResult<SpriteRenderer>> singleTask = SceneHelper
                .GetObjectOfTypeInScene<SpriteRenderer>(scenePath)
                .AsTask();
            yield return WaitForCompletion(singleTask);
            Assert.IsTrue(singleTask.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer> single = singleTask.Result;
            TrackAsyncDisposal(single.DisposeAsync);
            CollectionAssert.Contains(all.result, single.result);
            Task borrowedDisposal = single.DisposeAsync().AsTask();
            yield return WaitForCompletion(borrowedDisposal);
            Assert.IsTrue(borrowedDisposal.IsCompletedSuccessfully);
            Assert.IsTrue(loaded.isLoaded);
            Task ownedDisposal = all.DisposeAsync().AsTask();
            yield return WaitForCompletion(ownedDisposal);
            Assert.IsTrue(ownedDisposal.IsCompletedSuccessfully);
            Assert.IsFalse(loaded.isLoaded);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
            Assert.AreEqual(original, SceneManager.GetActiveScene());
        }

        [UnityTest]
        public IEnumerator SingleObjectRetrievalUnloadsItsOwnedScene()
        {
            int scenesBefore = SceneManager.sceneCount;
            Task<DeferredDisposalResult<SpriteRenderer>> task = SceneHelper
                .GetObjectOfTypeInScene<SpriteRenderer>(PackedScenePath)
                .AsTask();
            yield return WaitForCompletion(task);
            Assert.IsTrue(task.IsCompletedSuccessfully);
            DeferredDisposalResult<SpriteRenderer> result = task.Result;
            TrackAsyncDisposal(result.DisposeAsync);
            Assert.IsTrue(result.result != null);
            Scene owned = result.result.gameObject.scene;
            Assert.IsTrue(owned.isLoaded);
            Task disposal = result.DisposeAsync().AsTask();
            yield return WaitForCompletion(disposal);
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
            Assert.IsFalse(owned.isLoaded);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
        }

        [UnityTest]
        public IEnumerator DisposalFromLoadedCallbackSharesCleanup()
        {
            SceneHelper.SceneLoadScope scope = null;
            Task disposal = null;
            int callbacks = 0;
            int scenesBefore = SceneManager.sceneCount;
            scope = new SceneHelper.SceneLoadScope(
                PackedScenePath,
                (scene, mode) =>
                {
                    ++callbacks;
                    disposal = scope.DisposeAsync().AsTask();
                }
            );
            TrackAsyncDisposal(scope.DisposeAsync);
            yield return WaitForCompletion(scope.LoadTask);
            Assert.IsTrue(scope.LoadTask.IsCompletedSuccessfully);
            Assert.IsTrue(disposal != null);
            Assert.AreSame(disposal, scope.DisposeAsync().AsTask());
            yield return WaitForCompletion(disposal);
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
            Assert.IsFalse(scope.LoadTask.Result.isLoaded);
            Assert.AreEqual(1, callbacks);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
        }

        [UnityTest]
        public IEnumerator DisposalBeforeLoadingWaitsAndSuppressesCallback()
        {
            int callbacks = 0;
            int scenesBefore = SceneManager.sceneCount;
            SceneHelper.SceneLoadScope scope = new(PackedScenePath, (scene, mode) => ++callbacks);
            TrackAsyncDisposal(scope.DisposeAsync);
            Assert.IsFalse(
                scope.LoadTask.IsCompleted,
                "This control requires a pending native load."
            );
            Task first = scope.DisposeAsync().AsTask();
            Task second = scope.DisposeAsync().AsTask();
            Assert.AreSame(first, second);
            yield return WaitForCompletion(first);
            Assert.IsTrue(first.IsCompletedSuccessfully);
            Assert.IsTrue(scope.LoadTask.IsCompletedSuccessfully);
            Assert.IsFalse(scope.LoadTask.Result.isLoaded);
            Assert.AreEqual(0, callbacks);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
        }

        [UnityTest]
        public IEnumerator SimultaneousSamePathLoadsRetainDistinctOwnership(
            [ValueSource(nameof(ActiveStates))] bool reverseDisposal
        )
        {
            int scenesBefore = SceneManager.sceneCount;
            Scene firstCallback = default;
            Scene secondCallback = default;
            int firstCallbacks = 0;
            int secondCallbacks = 0;
            SceneHelper.SceneLoadScope first = new(
                PackedScenePath,
                (scene, mode) =>
                {
                    firstCallback = scene;
                    ++firstCallbacks;
                }
            );
            TrackAsyncDisposal(first.DisposeAsync);
            Assert.IsFalse(first.LoadTask.IsCompleted);
            SceneHelper.SceneLoadScope second = new(
                PackedScenePath,
                (scene, mode) =>
                {
                    secondCallback = scene;
                    ++secondCallbacks;
                }
            );
            TrackAsyncDisposal(second.DisposeAsync);
            yield return WaitForCompletion(first.LoadTask);
            yield return WaitForCompletion(second.LoadTask);
            Assert.IsTrue(first.LoadTask.IsCompletedSuccessfully);
            Assert.IsTrue(second.LoadTask.IsCompletedSuccessfully);
            Assert.AreNotEqual(first.LoadTask.Result, second.LoadTask.Result);
            Assert.AreEqual(first.LoadTask.Result, firstCallback);
            Assert.AreEqual(second.LoadTask.Result, secondCallback);
            Assert.AreEqual(1, firstCallbacks);
            Assert.AreEqual(1, secondCallbacks);
            Assert.AreEqual(scenesBefore + 2, SceneManager.sceneCount);
            SceneHelper.SceneLoadScope early = reverseDisposal ? second : first;
            SceneHelper.SceneLoadScope late = reverseDisposal ? first : second;
            Task earlyDisposal = early.DisposeAsync().AsTask();
            yield return WaitForCompletion(earlyDisposal);
            Assert.IsTrue(earlyDisposal.IsCompletedSuccessfully);
            Assert.IsFalse(early.LoadTask.Result.isLoaded);
            Assert.IsTrue(late.LoadTask.Result.isLoaded);
            Assert.AreEqual(scenesBefore + 1, SceneManager.sceneCount);
            Task lateDisposal = late.DisposeAsync().AsTask();
            yield return WaitForCompletion(lateDisposal);
            Assert.IsTrue(lateDisposal.IsCompletedSuccessfully);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
            Assert.AreEqual(1, firstCallbacks);
            Assert.AreEqual(1, secondCallbacks);
        }

        [UnityTest]
        public IEnumerator CallbackFailureRemainsObservableAndOwnedSceneUnloads()
        {
            InvalidOperationException failure = new(
                nameof(CallbackFailureRemainsObservableAndOwnedSceneUnloads)
            );
            Scene owned = default;
            int scenesBefore = SceneManager.sceneCount;
            SceneHelper.SceneLoadScope scope = new(
                PackedScenePath,
                (scene, mode) =>
                {
                    owned = scene;
                    throw failure;
                }
            );
            TrackAsyncDisposal(async () =>
            {
                try
                {
                    await scope.DisposeAsync();
                }
                catch (InvalidOperationException exception)
                    when (ReferenceEquals(exception, failure)) { }
            });
            yield return WaitForCompletion(scope.LoadTask);
            Assert.IsTrue(scope.LoadTask.IsFaulted);
            Assert.AreSame(
                failure,
                Assert.Throws<InvalidOperationException>(() =>
                    scope.LoadTask.GetAwaiter().GetResult()
                )
            );
            Assert.IsTrue(owned.isLoaded);
            Task disposal = scope.DisposeAsync().AsTask();
            yield return WaitForCompletion(disposal);
            Assert.IsTrue(disposal.IsFaulted);
            Assert.AreSame(
                failure,
                Assert.Throws<InvalidOperationException>(() => disposal.GetAwaiter().GetResult())
            );
            Assert.IsFalse(owned.isLoaded);
            Assert.AreEqual(scenesBefore, SceneManager.sceneCount);
            Assert.AreSame(disposal, scope.DisposeAsync().AsTask());
        }
    }
}
