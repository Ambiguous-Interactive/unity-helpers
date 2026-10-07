// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public abstract class DispatcherLifecycleTestBase : CommonTestBase
    {
        private UnityMainThreadDispatcher _dispatcher;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            TrackDisposable(UnityMainThreadDispatcher.AutoCreationScope.Disabled());
            _dispatcher = Track(new GameObject(nameof(DispatcherLifecycleTestBase)))
                .AddComponent<UnityMainThreadDispatcher>();
        }

        [Test]
        public void DestructionCancelsQueuedTasksWithoutExecutingCallbacks()
        {
            int calls = 0;
            Task action = _dispatcher.RunAsync(() => ++calls);
            Task asynchronous = _dispatcher.RunAsync(token =>
            {
                ++calls;
                return Task.CompletedTask;
            });
            Task<int> value = _dispatcher.Post(() =>
            {
                ++calls;
                return 42;
            });
            _dispatcher.RunOnMainThread(() => ++calls);
            DestroyDispatcher();
            Assert.IsTrue(action.IsCanceled);
            Assert.IsTrue(asynchronous.IsCanceled);
            Assert.IsTrue(value.IsCanceled);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

        [Test]
        public void DestroyedDispatcherRejectsEverySubmissionWithoutOverflowWarnings()
        {
            DestroyDispatcher();
            int calls = 0;
            Assert.IsFalse(_dispatcher.TryRunOnMainThread(() => ++calls));
            _dispatcher.RunOnMainThread(() => ++calls);
            Assert.IsTrue(_dispatcher.RunAsync(() => ++calls).IsCanceled);
            Assert.IsTrue(
                _dispatcher
                    .RunAsync(token =>
                    {
                        ++calls;
                        return Task.CompletedTask;
                    })
                    .IsCanceled
            );
            Assert.IsTrue(
                _dispatcher
                    .Post(() =>
                    {
                        ++calls;
                        return 42;
                    })
                    .IsCanceled
            );
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DestructionPreservesCallerCancellationToken()
        {
            using CancellationTokenSource cancellation = new();
            Task task = _dispatcher.RunAsync(token => Task.CompletedTask, cancellation.Token);
            cancellation.Cancel();
            DestroyDispatcher();
            OperationCanceledException error = Assert.Catch<OperationCanceledException>(() =>
                task.GetAwaiter().GetResult()
            );
            Assert.AreEqual(cancellation.Token, error.CancellationToken);
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

        [TestCase(TaskStatus.RanToCompletion)]
        [TestCase(TaskStatus.Faulted)]
        [TestCase(TaskStatus.Canceled)]
        public void DestructionPreservesStartedAsyncCompletion(TaskStatus outcome)
        {
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource<bool> running = new();
            Task task = _dispatcher.RunAsync(token => running.Task);
            RuntimeStateTestUtilities.DrainPendingActions();
            DestroyDispatcher();
            Assert.IsFalse(task.IsCompleted);
            InvalidOperationException failure = new("delegate failure");
            switch (outcome)
            {
                case TaskStatus.RanToCompletion:
                    running.SetResult(true);
                    break;
                case TaskStatus.Faulted:
                    running.SetException(failure);
                    break;
                case TaskStatus.Canceled:
                    cancellation.Cancel();
                    Assert.IsTrue(running.TrySetCanceled(cancellation.Token));
                    break;
                default:
                    Assert.Fail("Unexpected completion outcome.");
                    break;
            }
            Assert.IsTrue(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(3)));
            Assert.AreEqual(outcome, task.Status);
            if (outcome == TaskStatus.Faulted)
            {
                Assert.AreSame(failure, task.Exception.InnerException);
            }
            else if (outcome == TaskStatus.Canceled)
            {
                OperationCanceledException error = Assert.Catch<OperationCanceledException>(() =>
                    task.GetAwaiter().GetResult()
                );
                Assert.AreEqual(cancellation.Token, error.CancellationToken);
            }
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

        [Test]
        public void ReentrantDestructionCompletesCurrentCallbackAndCancelsLaterWork()
        {
            Task<int> current = _dispatcher.Post(() =>
            {
                DestroyDispatcher();
                return 42;
            });
            int calls = 0;
            Task later = _dispatcher.RunAsync(() => ++calls);
            RuntimeStateTestUtilities.DrainPendingActions();
            Assert.AreEqual(TaskStatus.RanToCompletion, current.Status);
            Assert.AreEqual(42, current.Result);
            Assert.IsTrue(later.IsCanceled);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

        [Test]
        public void InternalDispatchReportsOverflowRatherThanSuccessfulDelivery()
        {
            Assert.AreSame(_dispatcher, UnityMainThreadDispatcher.Instance);
            _dispatcher.PendingActionLimit = 1;
            Assert.IsTrue(_dispatcher.TryRunOnMainThread(() => { }));
            bool accepted = UnityMainThreadDispatcher.TryDispatchToMainThread(() =>
                Assert.Fail("Rejected callback executed.")
            );
            Assert.IsFalse(accepted);
            DestroyDispatcher();
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

#if !SINGLE_THREADED && !UNITY_WEBGL
        [Test]
        public void ConcurrentProducerAndDestructionSettleEveryReturnedTask()
        {
            _dispatcher.PendingActionLimit = 0;
            using ManualResetEventSlim start = new(false);
            using ManualResetEventSlim submittedFirst = new(false);
            using ManualResetEventSlim race = new(false);
            Task[] submitted = new Task[256];
            int calls = 0;
            Task producer = Task.Run(() =>
            {
                start.Wait();
                for (int index = 0; index < submitted.Length; ++index)
                {
                    submitted[index] = (index % 3) switch
                    {
                        0 => _dispatcher.RunAsync(() => Interlocked.Increment(ref calls)),
                        1 => _dispatcher.RunAsync(token =>
                        {
                            Interlocked.Increment(ref calls);
                            return Task.CompletedTask;
                        }),
                        _ => _dispatcher.Post(() => Interlocked.Increment(ref calls)),
                    };
                    if (index == 0)
                    {
                        submittedFirst.Set();
                        race.Wait();
                    }
                }
            });
            start.Set();
            try
            {
                Assert.IsTrue(
                    submittedFirst.Wait(TimeSpan.FromSeconds(5)),
                    "Producer did not submit initial work."
                );
                Assert.IsFalse(submitted[0].IsCompleted);
                race.Set();
                DestroyDispatcher();
            }
            finally
            {
                race.Set();
                Assert.IsTrue(
                    producer.Wait(TimeSpan.FromSeconds(5)),
                    "Producer did not terminate."
                );
            }
            foreach (Task task in submitted)
            {
                Assert.IsTrue(task.IsCanceled);
            }
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }
#endif

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateUnrootedPayload()
        {
            return new WeakReference(new object());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (Task task, WeakReference payload) QueueCapturedAsync(
            UnityMainThreadDispatcher dispatcher,
            CancellationToken token
        )
        {
            object payload = new();
            Task task = dispatcher.RunAsync(
                caller =>
                {
                    GC.KeepAlive(payload);
                    return Task.CompletedTask;
                },
                token
            );
            return (task, new WeakReference(payload));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void QueueOverflowFaultsTaskApis(int operation)
        {
            _dispatcher.PendingActionLimit = 1;
            Assert.IsTrue(_dispatcher.TryRunOnMainThread(() => { }));
            LogAssert.Expect(
                LogType.Warning,
                new Regex($"{nameof(UnityMainThreadDispatcher)} queue overflow")
            );
            int calls = 0;
            Task task = operation switch
            {
                0 => _dispatcher.RunAsync(() => ++calls),
                1 => _dispatcher.RunAsync(token =>
                {
                    ++calls;
                    return Task.CompletedTask;
                }),
                _ => _dispatcher.Post(() =>
                {
                    ++calls;
                    return 42;
                }),
            };
            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(task.Exception.InnerException);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(1, _dispatcher.PendingActionCount);
            DestroyDispatcher();
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
            Assert.IsTrue(task.IsFaulted);
        }

        [Test]
        public void NeverActiveDispatcherRejectsWorkThenAcceptsAfterActivation()
        {
            DestroyDispatcher();
            GameObject inactive = Track(new GameObject("InactiveDispatcher"));
            inactive.SetActive(false);
            _dispatcher = inactive.AddComponent<UnityMainThreadDispatcher>();
            Assert.IsTrue(
                _dispatcher.RunAsync(() => Assert.Fail("Dormant callback executed.")).IsCanceled
            );
            Assert.IsFalse(_dispatcher.TryRunOnMainThread(() => { }));
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
            inactive.SetActive(true);
            Task<int> task = _dispatcher.Post(() => 42);
            RuntimeStateTestUtilities.DrainPendingActions();
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.AreEqual(42, task.Result);
        }

        [UnityTest]
        public IEnumerator DisabledDispatcherCancelsWorkOnDestruction()
        {
            _dispatcher.enabled = false;
            Task task = _dispatcher.RunAsync(() => Assert.Fail("Disabled callback executed."));
            Assert.IsFalse(task.IsCompleted);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_dispatcher.gameObject); // UNH-SUPPRESS: Verify deferred destruction settles queued work
                yield return null;
            }
            else
            {
                DestroyDispatcher();
            }
            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReentrantAsyncDestructionPreservesRunningTaskAndCallerToken(bool cancelCaller)
        {
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource<bool> running = new();
            Task task = _dispatcher.RunAsync(
                token =>
                {
                    DestroyDispatcher();
                    return running.Task;
                },
                cancellation.Token
            );
            RuntimeStateTestUtilities.DrainPendingActions();
            Assert.IsFalse(task.IsCompleted);
            if (cancelCaller)
            {
                cancellation.Cancel();
                Assert.IsTrue(task.IsCanceled);
                OperationCanceledException error = Assert.Catch<OperationCanceledException>(() =>
                    task.GetAwaiter().GetResult()
                );
                Assert.AreEqual(cancellation.Token, error.CancellationToken);
            }
            running.SetResult(true);
            Assert.IsTrue(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(3)));
            Assert.AreEqual(
                cancelCaller ? TaskStatus.Canceled : TaskStatus.RanToCompletion,
                task.Status
            );
            Assert.AreEqual(0, _dispatcher.PendingActionCount);
        }

        [Test]
        public void DestructionReleasesQueuedDelegateWhileCallerTokenRemainsAlive()
        {
            object rootedControl = new();
            WeakReference rooted = new(rootedControl);
            WeakReference released = CreateUnrootedPayload();
            for (int attempt = 0; attempt < 3 && released.IsAlive; ++attempt)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.IsTrue(rooted.IsAlive, "The rooted retention control was lost.");
            GC.KeepAlive(rootedControl);
            if (released.IsAlive)
            {
                Assert.Ignore(
                    "The collector cannot establish unrooted payload release on this runtime."
                );
            }
            using CancellationTokenSource cancellation = new();
            (Task task, WeakReference payload) = QueueCapturedAsync(
                _dispatcher,
                cancellation.Token
            );
            Assert.IsTrue(payload.IsAlive);
            DestroyDispatcher();
            Assert.IsTrue(task.IsCanceled);
            for (int attempt = 0; attempt < 3 && payload.IsAlive; ++attempt)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.IsFalse(
                payload.IsAlive,
                "Discarded delegate remains rooted through its caller token."
            );
            GC.KeepAlive(cancellation);
            GC.KeepAlive(task);
            GC.KeepAlive(_dispatcher);
        }

        private void DestroyDispatcher()
        {
            UnityEngine.Object.DestroyImmediate(_dispatcher.gameObject); // UNH-SUPPRESS: Verify task settlement on destruction
        }
    }
}
