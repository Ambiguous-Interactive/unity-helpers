// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityMainThreadDispatcherTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            UnityMainThreadDispatcher.ClearInstance();
            if (!UnityMainThreadDispatcher.TryGetInstance(out UnityMainThreadDispatcher dispatcher))
            {
                dispatcher = UnityMainThreadDispatcher.Instance;
            }

            Track(dispatcher.gameObject);
        }

        [UnityTest]
        public IEnumerator RunOnMainThreadExecutesQueuedActions()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            bool executed = false;

            dispatcher.RunOnMainThread(() => executed = true);

            yield return null;
            yield return null;

            Assert.IsTrue(executed);
        }

        [UnityTest]
        public IEnumerator StartSceneOnlyOneInstance()
        {
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount == 0)
            {
                Assert.Fail("No scenes in build settings");
                yield break;
            }

            SceneManager.LoadScene(0);
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                // Spin-inspect initialize count to see if we're messing up by destroying ourselves.
                long mainThreadDispatcherCount = UnityMainThreadDispatcher.InitializeCount;
                Assert.IsTrue(
                    mainThreadDispatcherCount <= 1,
                    $"Expected 0 or 1 instances, got {mainThreadDispatcherCount} instances of UnityMainThreadDispatcher."
                );
                yield return null;
            } while (timer.Elapsed < TimeSpan.FromSeconds(1));
        }

        [Test]
        public void AutoCreationScopeDisabledRestoresPreviousState()
        {
            UnityMainThreadDispatcherTestHelper.EnableAutoCreation();
            bool initialState = UnityMainThreadDispatcher.AutoCreationEnabled;

            using (
                UnityMainThreadDispatcher.AutoCreationScope scope =
                    UnityMainThreadDispatcher.AutoCreationScope.Disabled(
                        destroyExistingInstanceOnEnter: false,
                        destroyInstancesOnDispose: false
                    )
            )
            {
                Assert.IsFalse(UnityMainThreadDispatcher.AutoCreationEnabled);
            }

            Assert.AreEqual(initialState, UnityMainThreadDispatcher.AutoCreationEnabled);
        }

        [UnityTest]
        public IEnumerator AutoCreationScopeDestroysInstancesWhenConfigured()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);
            UnityMainThreadDispatcherTestHelper.EnableAutoCreation();

            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Assert.IsTrue(dispatcher != null, "Dispatcher should be auto-created when enabled");
            Assert.IsTrue(UnityMainThreadDispatcher.HasInstance);

            using (
                UnityMainThreadDispatcher.AutoCreationScope scope =
                    UnityMainThreadDispatcher.AutoCreationScope.Disabled(
                        destroyExistingInstanceOnEnter: true,
                        destroyInstancesOnDispose: true,
                        destroyImmediate: true
                    )
            )
            {
                Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);
                Assert.IsFalse(UnityMainThreadDispatcher.AutoCreationEnabled);

                UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
                dispatcher = UnityMainThreadDispatcher.Instance;
                Assert.IsTrue(dispatcher != null, "Dispatcher should be re-created inside scope");
                Assert.IsTrue(UnityMainThreadDispatcher.HasInstance);
            }

            yield return null;

            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);
            Assert.IsTrue(UnityMainThreadDispatcher.AutoCreationEnabled);
        }

        [UnityTest]
        public IEnumerator InstanceDoesNotAutoCreateWhenDisabled()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);
            UnityMainThreadDispatcherTestHelper.EnableAutoCreation();

            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Assert.IsTrue(dispatcher != null, "Dispatcher should be auto-created when enabled");
            Track(dispatcher.gameObject);

            using (
                UnityMainThreadDispatcher.AutoCreationScope scope =
                    UnityMainThreadDispatcher.AutoCreationScope.Disabled(
                        destroyExistingInstanceOnEnter: true,
                        destroyInstancesOnDispose: true,
                        destroyImmediate: true
                    )
            )
            {
                Assert.IsFalse(UnityMainThreadDispatcher.AutoCreationEnabled);
                Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);
                Assert.IsTrue(
                    UnityMainThreadDispatcher.Instance == null,
                    "Instance should be null when auto-creation is disabled"
                );
            }

            UnityMainThreadDispatcher recreated = UnityMainThreadDispatcher.Instance;
            Assert.IsTrue(recreated != null, "Dispatcher should be re-created after scope exits");
            Track(recreated.gameObject);

            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoCreationScopeEnabledRestoresFlagAndCleansUp()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);
            UnityMainThreadDispatcher.SetAutoCreationEnabled(false);
            Assert.IsFalse(UnityMainThreadDispatcher.AutoCreationEnabled);
            Assert.IsTrue(
                UnityMainThreadDispatcher.Instance == null,
                "Instance should be null when auto-creation is disabled"
            );
            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);

            using (
                UnityMainThreadDispatcher.AutoCreationScope scope =
                    UnityMainThreadDispatcher.AutoCreationScope.Enabled(
                        destroyExistingInstanceOnEnter: false,
                        destroyInstancesOnDispose: true,
                        destroyImmediate: true
                    )
            )
            {
                Assert.IsTrue(UnityMainThreadDispatcher.AutoCreationEnabled);
                UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
                Assert.IsTrue(
                    dispatcher != null,
                    "Dispatcher should be auto-created inside enabled scope"
                );
                Track(dispatcher.gameObject);
                Assert.IsTrue(UnityMainThreadDispatcher.HasInstance);
            }

            Assert.IsFalse(UnityMainThreadDispatcher.AutoCreationEnabled);
            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);
            Assert.IsTrue(
                UnityMainThreadDispatcher.Instance == null,
                "Instance should be null after scope exits"
            );

            UnityMainThreadDispatcherTestHelper.EnableAutoCreation();

            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyExistingDispatcherUsesDestroy()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);
            UnityMainThreadDispatcherTestHelper.EnableAutoCreation();

            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Assert.IsTrue(dispatcher != null, "Dispatcher should be auto-created");
            Track(dispatcher.gameObject);

            bool destroyed = UnityMainThreadDispatcher.DestroyExistingDispatcher(immediate: false);
            Assert.IsTrue(destroyed);

            int guard = 10;
            while (UnityMainThreadDispatcher.HasInstance && guard-- > 0)
            {
                yield return null;
            }

            guard = 10;
            while (
                UnityObjectExtensions.FindObjectsOfTypeShim<UnityMainThreadDispatcher>(false).Length
                    > 0
                && guard-- > 0
            )
            {
                yield return null;
            }

            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);
            Assert.AreEqual(
                0,
                UnityObjectExtensions.FindObjectsOfTypeShim<UnityMainThreadDispatcher>(false).Length
            );
        }

        [UnityTest]
        public IEnumerator DestroyExistingDispatcherImmediateClearsInstanceWithoutErrors()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Track(dispatcher.gameObject);

            bool destroyed = UnityMainThreadDispatcher.DestroyExistingDispatcher(immediate: true);

            Assert.IsTrue(destroyed);
            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);

            yield return null;
            LogAssert.NoUnexpectedReceived();

            UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
            UnityMainThreadDispatcher recreated = UnityMainThreadDispatcher.Instance;
            Track(recreated.gameObject);
        }

        [Test]
        public void DestroyExistingDispatcherReturnsFalseWhenMissing()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);

            bool destroyed = UnityMainThreadDispatcher.DestroyExistingDispatcher(immediate: true);

            Assert.IsFalse(destroyed);

            UnityMainThreadDispatcherTestHelper.EnableAutoCreation();
        }

        [UnityTest]
        public IEnumerator DestroyExistingDispatcherReturnsFalseWhenMissingDeferred()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);

            bool destroyed = UnityMainThreadDispatcher.DestroyExistingDispatcher(immediate: false);
            Assert.IsFalse(destroyed);

            yield break;
        }

        [UnityTest]
        public IEnumerator CreateTestScopeReEnablesAutoCreationAndCleansUp()
        {
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(immediate: true);
            UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
            Assert.IsTrue(UnityMainThreadDispatcher.AutoCreationEnabled);

            UnityMainThreadDispatcher.AutoCreationScope scope =
                UnityMainThreadDispatcher.CreateTestScope(destroyImmediate: true);
            Assert.IsTrue(scope != null, "Test scope should not be null");
            Assert.IsTrue(UnityMainThreadDispatcher.AutoCreationEnabled);

            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Assert.IsTrue(dispatcher != null, "Dispatcher should be auto-created in test scope");
            Track(dispatcher.gameObject);
            Assert.IsTrue(UnityMainThreadDispatcher.HasInstance);

            scope.Dispose();

            Assert.IsTrue(UnityMainThreadDispatcher.AutoCreationEnabled);
            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);

            int guard = 5;
            while (UnityMainThreadDispatcher.HasInstance && guard-- > 0)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RunOnMainThreadLogsExceptions()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            LogAssert.Expect(
                LogType.Error,
                new Regex(".*UnityMainThreadDispatcher action threw an exception.*")
            );

            dispatcher.RunOnMainThread(() =>
                throw new InvalidOperationException("Dispatcher test failure")
            );

            yield return null;
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator QueueOverflowDropsExcessActions()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            dispatcher.PendingActionLimit = 1;

            bool firstExecuted = false;
            bool secondExecuted = false;

            dispatcher.RunOnMainThread(() => firstExecuted = true);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("UnityMainThreadDispatcher queue overflow.*")
            );
            dispatcher.RunOnMainThread(() => secondExecuted = true);

            yield return null;
            yield return null;

            Assert.IsTrue(firstExecuted);
            Assert.IsFalse(secondExecuted);
            Assert.AreEqual(0, dispatcher.PendingActionCount);
        }

        [UnityTest]
        public IEnumerator TryRunOnMainThreadReturnsFalseWhenQueueFull()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            dispatcher.PendingActionLimit = 1;

            dispatcher.RunOnMainThread(() => { });

            bool overflowActionExecuted = false;
            bool enqueued = dispatcher.TryRunOnMainThread(() => overflowActionExecuted = true);

            Assert.IsFalse(enqueued);
            Assert.IsFalse(overflowActionExecuted);

            yield return null;
            yield return null;

            Assert.AreEqual(0, dispatcher.PendingActionCount);
        }

        [UnityTest]
        public IEnumerator RunOnMainThreadNullThrows()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Assert.Throws<ArgumentNullException>(() => dispatcher.RunOnMainThread(null));
            yield break;
        }

        [UnityTest]
        public IEnumerator TryRunOnMainThreadNullThrows()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            Assert.Throws<ArgumentNullException>(() => dispatcher.TryRunOnMainThread(null));
            yield break;
        }

        [UnityTest]
        public IEnumerator PendingActionLimitZeroIsUnbounded()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            dispatcher.PendingActionLimit = 0;

            bool executed = false;
            dispatcher.RunOnMainThread(() => executed = true);

            yield return null;
            yield return null;

            Assert.IsTrue(executed);
            Assert.AreEqual(0, dispatcher.PendingActionCount);
        }

        [UnityTest]
        public IEnumerator OverflowWarningThrottledPerFrame()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            dispatcher.PendingActionLimit = 1;

            dispatcher.RunOnMainThread(() => { });

            LogAssert.Expect(
                LogType.Warning,
                new Regex("UnityMainThreadDispatcher queue overflow.*")
            );
            dispatcher.RunOnMainThread(() => { });

            // Second overflow same frame should not produce another warning
            dispatcher.RunOnMainThread(() => { });

            yield return null;
            yield return null;

            LogAssert.Expect(
                LogType.Warning,
                new Regex("UnityMainThreadDispatcher queue overflow.*")
            );
            dispatcher.RunOnMainThread(() => { });
            dispatcher.RunOnMainThread(() => { });

            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RunAsyncFuncThrowsSynchronous()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            Task task = dispatcher.RunAsync(() =>
                throw new InvalidOperationException("Sync throw")
            );

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(task.Exception?.GetBaseException());
        }

        [UnityTest]
        public IEnumerator RunAsyncFuncReturnsNull()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            Task task = dispatcher.RunAsync(token => null);

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsFaulted);
            StringAssert.Contains(
                "expected the delegate to return a Task",
                task.Exception?.GetBaseException().Message
            );
        }

        [UnityTest]
        public IEnumerator InstanceAccessibleFromWorkerThreadAfterBootstrap()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            Exception backgroundException = null;
            UnityMainThreadDispatcher backgroundInstance = null;

            using (ManualResetEventSlim completed = new(false))
            {
                Task.Run(() =>
                {
                    try
                    {
                        backgroundInstance = UnityMainThreadDispatcher.Instance;
                    }
                    catch (Exception ex)
                    {
                        backgroundException = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                });

                while (!completed.IsSet)
                {
                    yield return null;
                }
            }

            Assert.IsTrue(
                backgroundException == null,
                "Background thread should not throw exception when accessing bootstrapped instance"
            );
            Assert.AreSame(dispatcher, backgroundInstance);
        }

        [UnityTest]
        public IEnumerator BackgroundThreadCanScheduleWorkAfterBootstrap()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            Exception backgroundException = null;
            bool executed = false;

            using (ManualResetEventSlim completed = new(false))
            {
                Task.Run(() =>
                {
                    try
                    {
                        UnityMainThreadDispatcher.Instance.RunOnMainThread(() => executed = true);
                    }
                    catch (Exception ex)
                    {
                        backgroundException = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                });

                while (!completed.IsSet)
                {
                    yield return null;
                }
            }

            yield return null;
            yield return null;

            Assert.IsTrue(
                backgroundException == null,
                "Background thread should not throw exception when scheduling work"
            );
            Assert.IsTrue(executed);
        }

        [UnityTest]
        public IEnumerator RunAsyncCancellationTokenHonorsCancellation()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            Task task = null;
            using (CancellationTokenSource cancellationTokenSource = new())
            {
                task = dispatcher.RunAsync(
                    async token =>
                    {
                        await Task.Delay(1, token);
                    },
                    cancellationTokenSource.Token
                );

                cancellationTokenSource.Cancel();

                while (!task.IsCompleted)
                {
                    yield return null;
                }
            }

            Assert.IsTrue(task.IsCanceled);
        }

        [UnityTest]
        public IEnumerator RunAsyncDelegateCompletesAfterAwait()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            bool awaited = false;

            Task task = dispatcher.RunAsync(async token =>
            {
                await Task.Yield();
                awaited = true;
            });

            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(awaited);
            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(task.IsCanceled);
        }

        [UnityTest]
        public IEnumerator BootstrapEnsuresInstanceExists()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;

            yield return null;

            Assert.IsTrue(UnityMainThreadDispatcher.HasInstance);
        }

        [UnityTest]
        public IEnumerator TryDispatchToMainThreadReturnsFalseWhenInstanceMissing()
        {
            UnityMainThreadDispatcher existing =
                UnityObjectExtensions.FindObjectOfTypeShim<UnityMainThreadDispatcher>();
            if (existing != null)
            {
                Track(existing.gameObject);
                UnityEngine.Object.Destroy(existing.gameObject);
                while (UnityMainThreadDispatcher.HasInstance)
                {
                    yield return null;
                }
            }

            bool executed = false;
            bool dispatched = UnityMainThreadDispatcher.TryDispatchToMainThread(() =>
                executed = true
            );

            Assert.IsFalse(dispatched);
            Assert.IsFalse(executed);
            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);

            yield return null;
        }

        [UnityTest]
        public IEnumerator BackgroundThreadLoggingDoesNotRespawnDispatcher()
        {
            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            UnityEngine.Object.Destroy(dispatcher.gameObject); // UNH-SUPPRESS: Test verifies dispatcher respawn prevention
            while (UnityMainThreadDispatcher.HasInstance)
            {
                yield return null;
            }

            GameObject loggerOwner = Track(new GameObject("LoggerOwner"));
            Exception backgroundException = null;

            using (ManualResetEventSlim completed = new(false))
            {
                Task.Run(() =>
                {
                    try
                    {
                        loggerOwner.LogDebug(
                            FormattableStringFactory.Create("Background log {0}", 1)
                        );
                    }
                    catch (Exception ex)
                    {
                        backgroundException = ex;
                    }
                    finally
                    {
                        completed.Set();
                    }
                });

                while (!completed.IsSet)
                {
                    yield return null;
                }
            }

            Assert.IsTrue(
                backgroundException == null,
                "Background thread should not throw exception during cleanup test"
            );
            Assert.IsFalse(UnityMainThreadDispatcher.HasInstance);
            Assert.AreEqual(
                0,
                UnityObjectExtensions.FindObjectsOfTypeShim<UnityMainThreadDispatcher>(false).Length
            );
        }
    }
}
