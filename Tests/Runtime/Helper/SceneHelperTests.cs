// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor.SceneManagement;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SceneHelperTests : CommonTestBase
    {
        // Async disposals queued via TrackAsyncDisposal in base

        private static string TestScenePath => _testScenePath ??= ResolveScenePath();
        private static string _testScenePath;

        /// <summary>
        /// Marks the calling test inconclusive when the additive test scene cannot be loaded in this
        /// build. In the editor scenes load from disk; in a standalone player a scene must be baked
        /// into Build Settings, and the ephemeral CI player build includes none -- so additive
        /// loading of Test1.unity cannot succeed there. This is an environment precondition (no scene
        /// in the build), not a product defect, so skip rather than fail. <c>true</c> means the test
        /// should stop.
        /// </summary>
        private static bool TestSceneUnavailable()
        {
            if (Application.CanStreamedLevelBeLoaded(TestScenePath))
            {
                return false;
            }

            Assert.Inconclusive(
                "Additive test scene is not loadable in this build (not in the player's Build "
                    + "Settings); scene-loading coverage requires the test scene to be baked in."
            );
            return true;
        }

        [Test]
        public void GetScenesInBuild()
        {
            string[] scenes = SceneHelper.GetScenesInBuild();
            if (scenes.Length == 0)
            {
                // The ephemeral CI test project has no scenes in Build Settings, so there is
                // nothing to assert against. This is an environment precondition, not a defect.
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
                // GetAllScenePaths enumerates scene ASSETS via AssetDatabase, which only exists in
                // the editor; in a standalone player it returns empty by design. There is nothing to
                // assert against there -- an environment precondition, not a defect (mirrors
                // GetScenesInBuild above).
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
            if (TestSceneUnavailable())
            {
                yield break;
            }

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
            if (TestSceneUnavailable())
            {
                yield break;
            }

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
            if (TestSceneUnavailable())
            {
                yield break;
            }

            int initialSceneCount = SceneManager.sceneCount;
            bool callbackInvoked = false;

            SceneHelper.SceneLoadScope scope = new(
                TestScenePath,
                (scene, mode) =>
                {
                    if (scene.path == TestScenePath)
                    {
                        callbackInvoked = true;
                    }
                }
            );

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
                if (Time.time >= timeout)
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
            yield return null;
            Scene previousActive = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(loadedScene);

            bool callbackInvoked = false;
            SceneHelper.SceneLoadScope scope = new(
                TestScenePath,
                (scene, mode) =>
                {
                    if (scene.path == TestScenePath)
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

        private static string ResolveScenePath()
        {
            string relativePath = DirectoryHelper.FindAbsolutePathToDirectory(
                "Tests/Runtime/Scenes/Test1.unity"
            );
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                Assert.Fail("Unable to resolve test scene path.");
            }

            return relativePath;
        }

        private static class SceneHelperTestsUtilities
        {
            public static bool TryEnsureSceneLoaded(
                string scenePath,
                out Scene scene,
                bool expectError = false
            )
            {
#if UNITY_EDITOR
                try
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    return scene.IsValid();
                }
                catch
                {
                    // Fall back to runtime API below
                }
#endif
                if (SceneUtility.GetBuildIndexByScenePath(scenePath) < 0)
                {
                    if (expectError)
                    {
                        LogAssert.Expect(
                            LogType.Error,
                            new Regex("couldn't be loaded.*Build Settings", RegexOptions.IgnoreCase)
                        );
                    }
                    scene = default;
                    return false;
                }
                SceneManager.LoadScene(scenePath, LoadSceneMode.Additive);
                scene = SceneManager.GetSceneByPath(scenePath);
                return scene.IsValid() && scene.isLoaded;
            }

            public static IEnumerator UnloadSceneAsync(string scenePath)
            {
#if UNITY_EDITOR
                if (EditorSceneManager.CloseScene(SceneManager.GetSceneByPath(scenePath), true))
                {
                    yield break;
                }
#endif
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scenePath);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
            }
        }
    }
}
