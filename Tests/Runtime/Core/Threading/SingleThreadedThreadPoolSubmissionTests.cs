// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Threading
{
#if !SINGLE_THREADED
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Threading;

    [TestFixture]
    [Category("Fast")]
    public sealed class SingleThreadedThreadPoolSubmissionTests
    {
        private const int TimeoutMilliseconds = 10_000;

        [TestCase(false)]
        [TestCase(true)]
        public void AcceptedSubmissionsRunInOrderAcrossEveryDelegateShape(bool runInBackground)
        {
            using SingleThreadedThreadPool pool = new(runInBackground);
            int sequence = 0;
            Assert.IsTrue(pool.TryEnqueue((Action)(() => Assert.AreEqual(1, ++sequence))));
            Assert.IsTrue(
                pool.TryEnqueue(
                    (Func<Task>)(
                        () =>
                        {
                            Assert.AreEqual(2, ++sequence);
                            return Task.CompletedTask;
                        }
                    )
                )
            );
            Assert.IsTrue(
                pool.TryEnqueue(
                    (Func<ValueTask>)(
                        () =>
                        {
                            Assert.AreEqual(3, ++sequence);
                            return default;
                        }
                    )
                )
            );
            Task<bool> drain = pool.DrainAsync().AsTask();
            Assert.IsTrue(drain.Wait(TimeoutMilliseconds));
            Assert.IsTrue(drain.Result);
            Assert.AreEqual(3, sequence);
            Assert.IsEmpty(pool.Exceptions);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ClosedPoolRejectsEveryDelegateShape(bool runInBackground, bool dispose)
        {
            using SingleThreadedThreadPool pool = new(runInBackground);
            if (dispose)
            {
                pool.Dispose();
            }
            else
            {
                Task<bool> drain = pool.DrainAsync().AsTask();
                Assert.IsTrue(drain.Wait(TimeoutMilliseconds));
                Assert.IsTrue(drain.Result);
            }
            Assert.IsFalse(pool.TryEnqueue((Action)(() => Assert.Fail("Rejected action ran"))));
            Assert.IsFalse(
                pool.TryEnqueue(
                    (Func<Task>)(() => Task.FromException(new InvalidOperationException()))
                )
            );
            Assert.IsFalse(
                pool.TryEnqueue(
                    (Func<ValueTask>)(
                        () => new ValueTask(Task.FromException(new InvalidOperationException()))
                    )
                )
            );
            Assert.AreEqual(0, pool.Count);
            Assert.IsEmpty(pool.Exceptions);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullDelegatesAreRejectedWithoutClosingThePool(bool runInBackground)
        {
            using SingleThreadedThreadPool pool = new(runInBackground);
            Assert.IsFalse(pool.TryEnqueue((Action)null));
            Assert.IsFalse(pool.TryEnqueue((Func<Task>)null));
            Assert.IsFalse(pool.TryEnqueue((Func<ValueTask>)null));
            pool.Enqueue((Action)null);
            pool.Enqueue((Func<Task>)null);
            pool.Enqueue((Func<ValueTask>)null);
            Assert.AreEqual(0, pool.Count);
            Assert.IsTrue(pool.IsAcceptingWork);
            Assert.IsEmpty(pool.Exceptions);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SubmissionsRacingDrainMatchExecutedWork(bool runInBackground)
        {
            using SingleThreadedThreadPool pool = new(runInBackground);
            using ManualResetEventSlim start = new(false);
            using ManualResetEventSlim firstAccepted = new(false);
            int accepted = 0;
            int executed = 0;
            Task producer = Task.Run(() =>
            {
                start.Wait(TimeoutMilliseconds);
                for (int i = 0; i < 1000; ++i)
                {
                    if (pool.TryEnqueue((Action)(() => Interlocked.Increment(ref executed))))
                    {
                        ++accepted;
                        firstAccepted.Set();
                    }
                }
            });
            start.Set();
            Assert.IsTrue(firstAccepted.Wait(TimeoutMilliseconds));
            Task<bool> drain = pool.DrainAsync().AsTask();
            Assert.IsTrue(Task.WaitAll(new Task[] { producer, drain }, TimeoutMilliseconds));
            Assert.IsTrue(drain.Result);
            Assert.Less(0, accepted);
            Assert.AreEqual(accepted, executed);
            Assert.AreEqual(0, pool.Count);
            Assert.IsEmpty(pool.Exceptions);
        }
    }
#endif
}
