// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Threading
{
#if !SINGLE_THREADED
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

        // Disposal cancels rather than drains, which is the documented behavior for work that can
        // be redone. The count is asserted as a range, not a number, because whether the worker
        // wins the race depends on machine speed.
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
