// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.SceneManagement;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SceneHelperTests : CommonTestBase
    {
        private const string TestScenePath =
            "Packages/com.wallstop-studios.unity-helpers/Tests/Runtime/Scenes/Test1.unity";

        private static void EnsureTestSceneAvailable()
        {
#if UNITY_EDITOR
            Assert.AreEqual(
                typeof(SceneAsset),
                AssetDatabase.GetMainAssetTypeAtPath(TestScenePath),
                "The package test scene must resolve through Unity's asset database."
            );
#else
            Assert.IsTrue(
                Application.CanStreamedLevelBeLoaded(TestScenePath),
                "The package test scene must be baked into standalone tests."
            );
#endif
        }

        [TestCase(null, TestName = "LoadedScene.Null.ReturnsFalse")]
        [TestCase("", TestName = "LoadedScene.Empty.ReturnsFalse")]
        [TestCase("   ", TestName = "LoadedScene.Spaces.ReturnsFalse")]
        [TestCase("\t\r\n", TestName = "LoadedScene.ControlWhitespace.ReturnsFalse")]
        [TestCase("\u2003", TestName = "LoadedScene.UnicodeWhitespace.ReturnsFalse")]
        public void IsSceneLoadedRejectsBlankNamesAndPaths(string sceneNameOrPath)
        {
            Scene unsavedScene = CreateTempScene(
                nameof(IsSceneLoadedRejectsBlankNamesAndPaths),
                setActive: false
            );
            Assert.AreEqual(string.Empty, unsavedScene.path);
            Assert.IsTrue(SceneHelper.IsSceneLoaded(unsavedScene.name));
            Assert.IsFalse(SceneHelper.IsSceneLoaded(sceneNameOrPath));
        }

        [TestCase(null, TestName = "SceneScope.Null.DoesNothing")]
        [TestCase("", TestName = "SceneScope.Empty.DoesNothing")]
        [TestCase("   ", TestName = "SceneScope.Spaces.DoesNothing")]
        [TestCase("\t\r\n", TestName = "SceneScope.ControlWhitespace.DoesNothing")]
        [TestCase("\u2003", TestName = "SceneScope.UnicodeWhitespace.DoesNothing")]
        public void SceneLoadScopeWithBlankPathDoesNothing(string scenePath)
        {
            int initialSceneCount = SceneManager.sceneCount;
            bool callbackInvoked = false;
            SceneHelper.SceneLoadScope scope = new(
                scenePath,
                (scene, mode) => callbackInvoked = true
            );
            TrackAsyncDisposal(scope.DisposeAsync);
            Assert.IsFalse(callbackInvoked);
            Assert.AreEqual(initialSceneCount, SceneManager.sceneCount);
            ValueTask disposal = scope.DisposeAsync();
            Assert.IsTrue(disposal.IsCompletedSuccessfully);
            Assert.AreEqual(initialSceneCount, SceneManager.sceneCount);
        }

        [Test]
        public void GetScenesInBuild()
        {
            string[] scenes = SceneHelper.GetScenesInBuild();
            if (scenes.Length == 0)
            {
                // The ephemeral CI project has no Build Settings scenes to assert against.
                Assert.Inconclusive(
                    "No scenes in Build Settings; GetScenesInBuild correctness is covered when "
                        + "build scenes exist (a populated project)."
                );
            }
            Assert.That(scenes, Is.Not.Empty);
        }

        [Test]
        public void GetAllScenePaths()
        {
            string[] scenePaths = SceneHelper.GetAllScenePaths();
            if (scenePaths.Length == 0)
            {
                // Scene asset enumeration needs AssetDatabase; standalone players return empty by design.
                Assert.Inconclusive(
                    "GetAllScenePaths enumerates scene assets via the editor AssetDatabase; "
                        + "not available in a standalone player."
                );
            }
            Assert.That(scenePaths, Is.Not.Empty);
            Assert.IsTrue(
                scenePaths.Any(path => path.Contains("Test1")),
                string.Join(",", scenePaths)
            );
            Assert.IsTrue(
                scenePaths.Any(path => path.Contains("Test2")),
                string.Join(",", scenePaths)
            );
        }

        [UnityTest]
        public IEnumerator GetObjectOfTypeInScene()
        {
            EnsureTestSceneAvailable();

            ValueTask<DeferredDisposalResult<SpriteRenderer>> task =
                SceneHelper.GetObjectOfTypeInScene<SpriteRenderer>(TestScenePath);
            while (!task.IsCompleted)
            {
                yield return null;
            }
            Assert.IsTrue(task.IsCompletedSuccessfully);

            TrackAsyncDisposal(task.Result.DisposeAsync);
            SpriteRenderer found = task.Result.result;
            Assert.IsTrue(found != null);
        }

        [UnityTest]
        public IEnumerator GetAllObjectOfTypeInScene()
        {
            EnsureTestSceneAvailable();

            ValueTask<DeferredDisposalResult<SpriteRenderer[]>> task =
                SceneHelper.GetAllObjectsOfTypeInScene<SpriteRenderer>(TestScenePath);

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsCompletedSuccessfully);
            TrackAsyncDisposal(task.Result.DisposeAsync);
            SpriteRenderer[] found = task.Result.result;
            Assert.That(found, Has.Length.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator GetAllObjectsOfTypeInSceneReturnsEmptyWhenSceneMissing()
        {
            const string missingPath = "NonExistentScene/DoesNotExist.unity";
            ValueTask<DeferredDisposalResult<SpriteRenderer[]>> task =
                SceneHelper.GetAllObjectsOfTypeInScene<SpriteRenderer>(missingPath);

            Assert.IsTrue(task.IsCompleted);
            TrackAsyncDisposal(task.Result.DisposeAsync);
            Assert.IsEmpty(task.Result.result);
            yield break;
        }

        [UnityTest]
        public IEnumerator GetObjectOfTypeInSceneReturnsDefaultWhenSceneMissing()
        {
            ValueTask<DeferredDisposalResult<SpriteRenderer>> task =
                SceneHelper.GetObjectOfTypeInScene<SpriteRenderer>("MissingScene/Scene.unity");

            Assert.IsTrue(task.IsCompleted);
            DeferredDisposalResult<SpriteRenderer> result = task.Result;
            Assert.IsTrue(result.result == null);
            yield return result.DisposeAsync().AsTask();
        }

        [UnityTest]
        public IEnumerator SceneLoadScopeLoadsAndDisposesScene()
        {
            EnsureTestSceneAvailable();

            int initialSceneCount = SceneManager.sceneCount;
            bool callbackInvoked = false;

            SceneHelper.SceneLoadScope scope = new(
                TestScenePath,
                (scene, mode) =>
                {
                    if (string.Equals(scene.path, TestScenePath, System.StringComparison.Ordinal))
                    {
                        callbackInvoked = true;
                    }
                }
            );

            TrackAsyncDisposal(scope.DisposeAsync);
            float timeout = Time.time + 5f;
            while (!callbackInvoked && Time.time < timeout)
            {
                yield return null;
            }

            Assert.IsTrue(callbackInvoked, "SceneLoadScope never reported scene load.");

            Scene additiveScene = SceneManager.GetSceneByPath(TestScenePath);
            Assert.IsTrue(additiveScene.IsValid());
            Assert.IsTrue(additiveScene.isLoaded);

            ValueTask disposeTask = scope.DisposeAsync();
            while (!disposeTask.IsCompleted)
            {
                yield return null;
            }

            timeout = Time.time + 5f;
            while (true)
            {
                Scene maybeScene = SceneManager.GetSceneByPath(TestScenePath);
                if (!maybeScene.IsValid() || !maybeScene.isLoaded)
                {
                    break;
                }
                if (timeout <= Time.time)
                {
                    break;
                }
                yield return null;
            }

            Assert.AreEqual(initialSceneCount, SceneManager.sceneCount);
            Assert.IsFalse(SceneManager.GetSceneByPath(TestScenePath).isLoaded);
        }

        [UnityTest]
        public IEnumerator SceneLoadScopeDoesNotUnloadAlreadyActiveScene()
        {
            if (
                !SceneHelperTestsUtilities.TryEnsureSceneLoaded(
                    TestScenePath,
                    out Scene loadedScene
                )
            )
            {
                Assert.Inconclusive($"Scene '{TestScenePath}' must exist to run this test.");
                yield break;
            }
            TrackAsyncDisposal(() => SceneHelperTestsUtilities.DisposeSceneAsync(loadedScene));
            yield return null;
            Scene previousActive = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(loadedScene);

            bool callbackInvoked = false;
            SceneHelper.SceneLoadScope scope = new(
                TestScenePath,
                (scene, mode) =>
                {
                    if (string.Equals(scene.path, TestScenePath, System.StringComparison.Ordinal))
                    {
                        callbackInvoked = true;
                    }
                }
            );

            Assert.IsTrue(callbackInvoked, "Active scene should trigger immediate callback.");

            ValueTask disposeTask = scope.DisposeAsync();
            while (!disposeTask.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(SceneManager.GetSceneByPath(TestScenePath).isLoaded);

            if (previousActive.IsValid() && previousActive.isLoaded)
            {
                SceneManager.SetActiveScene(previousActive);
            }

            yield return SceneHelperTestsUtilities.UnloadSceneAsync(TestScenePath);
        }

        [UnityTest]
        public IEnumerator GetAllObjectsOfTypeInSceneReturnsEmptyWhenTypeMissing()
        {
            ValueTask<DeferredDisposalResult<MissingSceneComponent[]>> task =
                SceneHelper.GetAllObjectsOfTypeInScene<MissingSceneComponent>(TestScenePath);

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsCompletedSuccessfully);
            TrackAsyncDisposal(task.Result.DisposeAsync);
            Assert.IsEmpty(task.Result.result);
        }

        private static class SceneHelperTestsUtilities
        {
            public static bool TryEnsureSceneLoaded(string scenePath, out Scene scene)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Scene opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    bool valid = opened.IsValid();
                    scene = opened;
                    return valid;
                }
                Scene loading = EditorSceneManager.LoadSceneInPlayMode(
                    scenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive)
                );
#else
                if (!Application.CanStreamedLevelBeLoaded(scenePath))
                {
                    scene = default;
                    return false;
                }
                Scene loading = SceneManager.LoadScene(
                    scenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive)
                );
#endif
                bool started = loading.IsValid();
                scene = loading;
                return started;
            }

            public static async ValueTask DisposeSceneAsync(Scene scene)
            {
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return;
                }
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Assert.IsTrue(EditorSceneManager.CloseScene(scene, true));
                    return;
                }
#endif
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                Assert.IsTrue(unload != null);
                await unload;
            }

            public static IEnumerator UnloadSceneAsync(string scenePath)
            {
#if UNITY_EDITOR
                if (
                    !Application.isPlaying
                    && EditorSceneManager.CloseScene(SceneManager.GetSceneByPath(scenePath), true)
                )
                {
                    yield break;
                }
#endif
                Scene loaded = SceneManager.GetSceneByPath(scenePath);
                if (!loaded.IsValid() || !loaded.isLoaded)
                {
                    yield break;
                }
                AsyncOperation unload = SceneManager.UnloadSceneAsync(loaded);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
            }
        }
    }
}
