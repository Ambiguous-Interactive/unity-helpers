// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Threading
{
#if !SINGLE_THREADED
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
    public sealed class SingleThreadedThreadPoolTests
    {
        public static IEnumerable<TestCaseData> IdleWaitTimeoutCases =>
            CreateIdleTimeoutCases("IdleWait");

        public static IEnumerable<TestCaseData> QueuedWorkTimeoutCases =>
            CreateIdleTimeoutCases("QueuedWork");

        private static IEnumerable<TestCaseData> CreateIdleTimeoutCases(string scope)
        {
            long[] ticks =
            {
                TimeSpan.MinValue.Ticks,
                -2 * TimeSpan.TicksPerMillisecond,
                -2 * TimeSpan.TicksPerMillisecond + 1,
                -TimeSpan.TicksPerMillisecond - 1,
                -TimeSpan.TicksPerMillisecond,
                -1,
                0,
                1,
                TimeSpan.TicksPerMillisecond,
                (long)int.MaxValue * TimeSpan.TicksPerMillisecond,
                (long)int.MaxValue * TimeSpan.TicksPerMillisecond + 1,
                ((long)int.MaxValue + 1) * TimeSpan.TicksPerMillisecond - 1,
                ((long)int.MaxValue + 1) * TimeSpan.TicksPerMillisecond,
                TimeSpan.MaxValue.Ticks,
            };
            foreach (bool runInBackground in new[] { false, true })
            {
                foreach (long timeoutTicks in ticks)
                {
                    yield return new TestCaseData(timeoutTicks, runInBackground).SetName(
                        $"{scope}.Ticks{timeoutTicks}.Background{runInBackground}"
                    );
                }
            }
        }

        [TestCaseSource(nameof(IdleWaitTimeoutCases))]
        public void IdleWaitAcceptsEveryConfiguredDelayAndHonorsCancellation(
            long timeoutTicks,
            bool runInBackground
        )
        {
            using SingleThreadedThreadPool threadPool = new(
                runInBackground,
                TimeSpan.FromTicks(timeoutTicks)
            );
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Catch<OperationCanceledException>(() =>
                threadPool.WaitForWorkAsync(cancellation.Token).GetAwaiter().GetResult()
            );
        }

        [TestCaseSource(nameof(QueuedWorkTimeoutCases))]
        public void ConfiguredIdleDelayPreservesOrderedActionsTasksAndValueTasks(
            long timeoutTicks,
            bool runInBackground
        )
        {
            using SingleThreadedThreadPool threadPool = new(
                runInBackground,
                TimeSpan.FromTicks(timeoutTicks)
            );
            List<int> received = new();
            for (int index = 0; index < 30; ++index)
            {
                int captured = index;
                switch (index % 3)
                {
                    case 0:
                        threadPool.Enqueue(() => received.Add(captured));
                        break;
                    case 1:
                        threadPool.Enqueue(() =>
                        {
                            received.Add(captured);
                            return Task.CompletedTask;
                        });
                        break;
                    default:
                        threadPool.Enqueue(() =>
                        {
                            received.Add(captured);
                            return default(ValueTask);
                        });
                        break;
                }
            }

            Task<bool> drain = threadPool.DrainAsync().AsTask();
            Assert.IsTrue(drain.Wait(5000), "Configured idle delay left accepted work waiting.");
            Assert.IsTrue(drain.Result);
            Assert.AreEqual(30, received.Count);
            for (int index = 0; index < received.Count; ++index)
            {
                Assert.AreEqual(index, received[index]);
            }
            Assert.AreEqual(0, threadPool.Count);
            Assert.IsFalse(threadPool.IsAcceptingWork);
            Assert.IsEmpty(threadPool.Exceptions);
        }

        [Test]
        public void Disposal()
        {
            using SingleThreadedThreadPool threadPool = new();
        }

        [UnityTest]
        public IEnumerator StuffHappening()
        {
            bool thingHappened = false;
            using SingleThreadedThreadPool threadPool = new();
            for (int i = 0; i < 100; ++i)
            {
                thingHappened = false;
                threadPool.Enqueue(() => thingHappened = true);
                while (!thingHappened)
                {
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator Ordering()
        {
            List<int> received = new();
            using SingleThreadedThreadPool threadPool = new();
            for (int i = 0; i < 100; ++i)
            {
                int localValue = i;
                threadPool.Enqueue(() => received.Add(localValue));
            }
            while (received.Count < 100)
            {
                yield return null;
            }

            for (int i = 0; i < 100; ++i)
            {
                Assert.AreEqual(i, received[i]);
            }
        }
    }
#endif
}
