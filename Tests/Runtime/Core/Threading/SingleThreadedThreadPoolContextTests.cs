// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Threading
{
#if SINGLE_THREADED || (UNITY_WEBGL && !UNITY_EDITOR)
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Threading;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SingleThreadedThreadPoolContextTests
    {
        private static IEnumerator AwaitCompletion(Task task)
        {
            for (int frame = 0; frame < 120 && !task.IsCompleted; ++frame)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Task did not complete after 120 frames.");
        }

        [UnityTest]
        public IEnumerator WorkRunsInOrderOnUnityContextAndReportsFailures()
        {
            int ownerThread = Environment.CurrentManagedThreadId;
            List<int> order = new();
            List<int> threads = new();
            InvalidOperationException expected = new InvalidOperationException("work failure");
            List<Exception> reported = new();
            using SingleThreadedThreadPool pool = new(
                false,
                Timeout.InfiniteTimeSpan,
                reported.Add
            );
            TaskCompletionSource<bool> release = new();
            bool started = false;
            pool.Enqueue(() =>
            {
                order.Add(1);
                threads.Add(Environment.CurrentManagedThreadId);
            });
            pool.Enqueue(
                (Func<Task>)(
                    async () =>
                    {
                        started = true;
                        await release.Task;
                        order.Add(2);
                        threads.Add(Environment.CurrentManagedThreadId);
                        throw expected;
                    }
                )
            );
            pool.Enqueue(() =>
            {
                order.Add(3);
                threads.Add(Environment.CurrentManagedThreadId);
                return default(ValueTask);
            });
            Assert.That(order, Is.Empty);
            Assert.That(pool.Count, Is.EqualTo(3));

            Task<bool> drain = pool.DrainAsync().AsTask();
            Assert.That(pool.TryEnqueue(() => order.Add(4)), Is.False);
            try
            {
                for (int frame = 0; frame < 120 && !started; ++frame)
                {
                    yield return null;
                }
                Assert.That(started, Is.True);
                Assert.That(order, Is.EqualTo(new[] { 1 }));
                Assert.That(drain.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }

            yield return AwaitCompletion(drain);
            Assert.That(drain.GetAwaiter().GetResult(), Is.True);
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(threads, Is.EqualTo(new[] { ownerThread, ownerThread, ownerThread }));
            Assert.That(reported, Is.EqualTo(new[] { expected }));
            Assert.That(pool.Exceptions, Is.Empty);
            Assert.That(pool.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CanceledDrainLeavesWorkUntilDisposalDiscardsIt()
        {
            using SingleThreadedThreadPool pool = new();
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource<bool> release = new();
            bool started = false;
            bool finished = false;
            bool droppedItemRan = false;
            pool.Enqueue(
                (Func<Task>)(
                    async () =>
                    {
                        started = true;
                        await release.Task;
                        finished = true;
                    }
                )
            );
            pool.Enqueue(() =>
            {
                droppedItemRan = true;
            });

            Task<bool> drain = pool.DrainAsync(cancellation.Token).AsTask();
            Task shutdown;
            try
            {
                for (int frame = 0; frame < 120 && !started; ++frame)
                {
                    yield return null;
                }

                Assert.That(started, Is.True);
                cancellation.Cancel();
                yield return AwaitCompletion(drain);
                Assert.That(drain.GetAwaiter().GetResult(), Is.False);
                Assert.That(pool.Count, Is.EqualTo(2));
                Task<bool> interruptedDrain = pool.DrainAsync().AsTask();
                pool.Dispose();
                shutdown = pool.DisposeAsync().AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(pool.Count, Is.EqualTo(1));
                yield return AwaitCompletion(interruptedDrain);
                Assert.That(interruptedDrain.GetAwaiter().GetResult(), Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }

            yield return AwaitCompletion(shutdown);
            shutdown.GetAwaiter().GetResult();
            Assert.That(finished, Is.True);
            Assert.That(droppedItemRan, Is.False);
            Assert.That(pool.Count, Is.Zero);
        }
    }
#endif
}
