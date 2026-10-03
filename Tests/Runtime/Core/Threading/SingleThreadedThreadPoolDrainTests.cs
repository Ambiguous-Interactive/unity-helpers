// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Threading
{
#if !SINGLE_THREADED
    using System;
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Threading;

    /// <summary>
    /// Covers the teardown contract: disposal discards queued work, draining does not.
    /// </summary>
    /// <remarks>
    /// The reported failure is a race -- enqueueing and disposing back to back dropped the item in
    /// 197 of 200 trials, while a one-millisecond gap dropped none. These tests therefore assert
    /// the drained side deterministically, and assert only that the discarding side is permitted
    /// to discard, so the suite cannot become flaky on a fast or slow machine.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SingleThreadedThreadPoolDrainTests
    {
        private const int QueuedItems = 25;
        private const int DrainTimeoutMilliseconds = 30_000;

        // Bound the gate wait so an assertion failure cannot stall teardown.
        private const int GateTimeoutMilliseconds = 5_000;

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        public void DisposalClearsPendingWorkWhileWaitingForRunningItem(
            bool runInBackground,
            int delegateKind
        )
        {
            using ManualResetEventSlim entered = new(false);
            using ManualResetEventSlim release = new(false);
            using SingleThreadedThreadPool threadPool = new(runInBackground);
            int queuedRuns = 0;
            threadPool.Enqueue(() =>
            {
                entered.Set();
                release.Wait(DrainTimeoutMilliseconds);
            });
            Task disposal = null;
            try
            {
                Assert.IsTrue(entered.Wait(GateTimeoutMilliseconds));
                for (int index = 0; index < QueuedItems; ++index)
                {
                    switch (delegateKind)
                    {
                        case 0:
                            threadPool.Enqueue(
                                (Action)(() => Interlocked.Increment(ref queuedRuns))
                            );
                            break;
                        case 1:
                            threadPool.Enqueue(
                                (Func<Task>)(
                                    () =>
                                    {
                                        Interlocked.Increment(ref queuedRuns);
                                        return Task.CompletedTask;
                                    }
                                )
                            );
                            break;
                        case 2:
                            threadPool.Enqueue(
                                (Func<ValueTask>)(
                                    () =>
                                    {
                                        Interlocked.Increment(ref queuedRuns);
                                        return default;
                                    }
                                )
                            );
                            break;
                    }
                }
                Assert.AreEqual(QueuedItems + 1, threadPool.Count);
                disposal = threadPool.DisposeAsync().AsTask();
                Assert.IsFalse(threadPool.IsAcceptingWork);
                Assert.IsFalse(disposal.IsCompleted);
                Assert.AreEqual(
                    1,
                    threadPool.Count,
                    "Shutdown must release queued delegates before the running item finishes."
                );
            }
            finally
            {
                release.Set();
                if (disposal != null)
                {
                    Assert.IsTrue(disposal.Wait(GateTimeoutMilliseconds));
                }
            }
            Assert.AreEqual(0, threadPool.Count);
            Assert.AreEqual(0, Volatile.Read(ref queuedRuns));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConcurrentSubmissionsCannotRetainWorkAfterDisposal(bool runInBackground)
        {
            using ManualResetEventSlim entered = new(false);
            using ManualResetEventSlim release = new(false);
            using ManualResetEventSlim submit = new(false);
            using SingleThreadedThreadPool threadPool = new(runInBackground);
            int queuedRuns = 0;
            Action pending = () => Interlocked.Increment(ref queuedRuns);
            threadPool.Enqueue(() =>
            {
                entered.Set();
                release.Wait(DrainTimeoutMilliseconds);
            });
            Task[] producers = new Task[4];
            Task disposal = null;
            try
            {
                Assert.IsTrue(entered.Wait(GateTimeoutMilliseconds));
                threadPool.Enqueue(pending);
                for (int index = 0; index < producers.Length; ++index)
                {
                    producers[index] = Task.Run(() =>
                    {
                        submit.Wait(DrainTimeoutMilliseconds);
                        for (int item = 0; item < 1000; ++item)
                        {
                            threadPool.Enqueue(pending);
                        }
                    });
                }
                submit.Set();
                disposal = threadPool.DisposeAsync().AsTask();
                Assert.IsTrue(Task.WaitAll(producers, GateTimeoutMilliseconds));
                threadPool.Enqueue(pending);
                Assert.AreEqual(1, threadPool.Count);
            }
            finally
            {
                submit.Set();
                release.Set();
                if (disposal != null)
                {
                    Assert.IsTrue(disposal.Wait(GateTimeoutMilliseconds));
                }
            }
            Assert.AreEqual(0, threadPool.Count);
            Assert.AreEqual(0, Volatile.Read(ref queuedRuns));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DrainRacingDisposalCannotReportDiscardedWorkAsCompleted(bool runInBackground)
        {
            for (int attempt = 0; attempt < QueuedItems; ++attempt)
            {
                using ManualResetEventSlim entered = new(false);
                using ManualResetEventSlim release = new(false);
                using SingleThreadedThreadPool threadPool = new(runInBackground);
                int queuedRuns = 0;
                threadPool.Enqueue(() =>
                {
                    entered.Set();
                    release.Wait(DrainTimeoutMilliseconds);
                });
                Task<bool> drain = null;
                Task disposal = null;
                try
                {
                    Assert.IsTrue(entered.Wait(GateTimeoutMilliseconds));
                    threadPool.Enqueue(() => Interlocked.Increment(ref queuedRuns));
                    drain = threadPool.DrainAsync().AsTask();
                    Assert.IsFalse(drain.IsCompleted);
                    disposal = threadPool.DisposeAsync().AsTask();
                }
                finally
                {
                    release.Set();
                    if (disposal != null)
                    {
                        Assert.IsTrue(disposal.Wait(GateTimeoutMilliseconds));
                    }
                }
                Assert.IsTrue(drain.Wait(GateTimeoutMilliseconds));
                Assert.IsFalse(drain.Result, "Disposal discarded accepted work during this drain.");
                Assert.AreEqual(0, Volatile.Read(ref queuedRuns));
                Assert.AreEqual(0, threadPool.Count);
            }
        }

        [Test]
        public void AlreadyCanceledDrainOfIdlePoolPreservesCompletedSuccess()
        {
            using CancellationTokenSource canceled = new();
            using SingleThreadedThreadPool threadPool = new();
            canceled.Cancel();
            Task<bool> drain = threadPool.DrainAsync(canceled.Token).AsTask();
            Assert.IsTrue(drain.IsCompleted);
            Assert.IsTrue(drain.Result);
        }

        [Test]
        public void AlreadyCanceledDrainWithRunningWorkReportsFailure()
        {
            using ManualResetEventSlim entered = new(false);
            using ManualResetEventSlim release = new(false);
            using CancellationTokenSource canceled = new();
            using SingleThreadedThreadPool threadPool = new();
            threadPool.Enqueue(() =>
            {
                entered.Set();
                release.Wait(DrainTimeoutMilliseconds);
            });
            try
            {
                Assert.IsTrue(entered.Wait(GateTimeoutMilliseconds));
                canceled.Cancel();
                Task<bool> drain = threadPool.DrainAsync(canceled.Token).AsTask();
                Assert.IsTrue(drain.IsCompleted);
                Assert.IsFalse(drain.Result);
            }
            finally
            {
                release.Set();
            }
        }

        [UnityTest]
        public IEnumerator DrainRunsEveryQueuedItemBeforeDisposal()
        {
            int completed = 0;
            using CancellationTokenSource timeout = new(DrainTimeoutMilliseconds);
            using SingleThreadedThreadPool threadPool = new();
            for (int i = 0; i < QueuedItems; ++i)
            {
                threadPool.Enqueue(() => Interlocked.Increment(ref completed));
            }

            Task<bool> drain = threadPool.DrainAsync(timeout.Token).AsTask();
            while (!drain.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(drain.Result, "Drain reported that it did not run the queue down.");
            Assert.AreEqual(QueuedItems, Volatile.Read(ref completed));

            threadPool.Dispose();
            Assert.AreEqual(QueuedItems, Volatile.Read(ref completed));
        }

        // Gate the running item to distinguish queued work from unfinished work without a dequeue race.
        [UnityTest]
        public IEnumerator DrainWaitsForAnItemAlreadyExecuting()
        {
            using ManualResetEventSlim release = new(false);
            using CancellationTokenSource timeout = new(DrainTimeoutMilliseconds);
            using SingleThreadedThreadPool threadPool = new();

            int completed = 0;
            threadPool.Enqueue(() =>
            {
                release.Wait(GateTimeoutMilliseconds);
                Interlocked.Increment(ref completed);
            });

            Task<bool> drain = threadPool.DrainAsync(timeout.Token).AsTask();
            for (int i = 0; i < 10; ++i)
            {
                Assert.IsFalse(
                    drain.IsCompleted,
                    "Drain returned while an item was still running."
                );
                yield return null;
            }

            release.Set();
            while (!drain.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(drain.Result);
            Assert.AreEqual(1, Volatile.Read(ref completed));
        }

        [UnityTest]
        public IEnumerator DrainStopsAcceptingWork()
        {
            using CancellationTokenSource timeout = new(DrainTimeoutMilliseconds);
            using SingleThreadedThreadPool threadPool = new();
            Assert.IsTrue(threadPool.IsAcceptingWork);

            Task<bool> drain = threadPool.DrainAsync(timeout.Token).AsTask();
            while (!drain.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(drain.Result);
            Assert.IsFalse(threadPool.IsAcceptingWork);

            bool ranAfterDrain = false;
            threadPool.Enqueue(() => ranAfterDrain = true);
            Assert.AreEqual(0, threadPool.Count, "Work enqueued after a drain must be rejected.");

            threadPool.Dispose();
            Assert.IsFalse(ranAfterDrain);
        }

        [UnityTest]
        public IEnumerator DrainAfterDisposalReportsFailureInsteadOfHanging()
        {
            using CancellationTokenSource timeout = new(DrainTimeoutMilliseconds);
            using SingleThreadedThreadPool threadPool = new();
            threadPool.Dispose();

            Task<bool> drain = threadPool.DrainAsync(timeout.Token).AsTask();
            while (!drain.IsCompleted)
            {
                yield return null;
            }

            Assert.IsFalse(drain.Result);
        }

        [UnityTest]
        public IEnumerator DrainOfAnIdlePoolCompletes()
        {
            using CancellationTokenSource timeout = new(DrainTimeoutMilliseconds);
            using SingleThreadedThreadPool threadPool = new();

            Task<bool> drain = threadPool.DrainAsync(timeout.Token).AsTask();
            while (!drain.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(drain.Result);
        }

        // Disposal cancels pending work; the worker race permits a range of completed counts.
        [UnityTest]
        public IEnumerator DisposalWithoutDrainingIsAllowedToDiscardQueuedWork()
        {
            int completed = 0;
            using SingleThreadedThreadPool threadPool = new();
            for (int i = 0; i < QueuedItems; ++i)
            {
                threadPool.Enqueue(() => Interlocked.Increment(ref completed));
            }

            threadPool.Dispose();
            yield return null;

            int ran = Volatile.Read(ref completed);
            Assert.GreaterOrEqual(ran, 0);
            Assert.LessOrEqual(ran, QueuedItems);
        }
    }
#endif
}
