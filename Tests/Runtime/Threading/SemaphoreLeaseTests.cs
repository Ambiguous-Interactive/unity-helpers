// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Threading
{
    using System;
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Threading;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class SemaphoreLeaseTests : CommonTestBase
    {
        private static readonly TimeSpan NoWait = TimeSpan.Zero;
        private static readonly TimeSpan ShortWait = TimeSpan.FromMilliseconds(50);

        [Test]
        public void AcquireTakesThePermitAndDisposeReturnsIt()
        {
            using SemaphoreSlim semaphore = new(1, 1);

            using (SemaphoreLease lease = semaphore.Acquire())
            {
                Assert.IsTrue(lease.IsHeld);
                Assert.AreEqual(0, semaphore.CurrentCount);
            }

            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [Test]
        public void DisposingTwiceReturnsOnlyOnePermit()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            SemaphoreLease lease = semaphore.Acquire();

            lease.Dispose();
            lease.Dispose();

            // The second release would raise the count above the semaphore's maximum and let two
            // callers into a section built for one.
            Assert.AreEqual(1, semaphore.CurrentCount);
            Assert.IsFalse(lease.IsHeld);
        }

        [Test]
        public void ADefaultLeaseIsNotHeldAndDisposesHarmlessly()
        {
            SemaphoreLease lease = default;

            Assert.IsFalse(lease.IsHeld);
            Assert.DoesNotThrow(() => lease.Dispose());
        }

        [Test]
        public void DisposingALeaseWhoseSemaphoreIsGoneDoesNotThrow()
        {
            SemaphoreSlim semaphore = new(1, 1);
            SemaphoreLease lease = semaphore.Acquire();
            semaphore.Dispose();

            Assert.DoesNotThrow(() => lease.Dispose());
            Assert.IsFalse(lease.IsHeld);
        }

        [Test]
        public void ADisposedSemaphoreDoesNotMaskTheCallersException()
        {
            SemaphoreSlim semaphore = new(1, 1);

            // The whole point of never throwing from Dispose: this runs from the `finally` of the
            // using, so a throw there would replace the caller's real failure with a confusing one.
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            {
                using (semaphore.Acquire())
                {
                    semaphore.Dispose();
                    throw new InvalidOperationException("the real failure");
                }
            });

            Assert.AreEqual("the real failure", thrown.Message);
        }

        [Test]
        public void DisposingACopiedLeaseIsSwallowedWhenTheMaximumIsExplicit()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            SemaphoreLease lease = semaphore.Acquire();
            // Copying is documented as forbidden. With an explicit maximum, doing it anyway degrades
            // to a no-op: the extra release throws SemaphoreFullException and Dispose swallows it.
            SemaphoreLease copy = lease;

            lease.Dispose();

            Assert.DoesNotThrow(() => copy.Dispose());
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [Test]
        public void DisposingACopiedLeaseInflatesACountWithNoExplicitMaximum()
        {
            // The unhappy half of the pair, pinned because the docs used to claim a guarantee that
            // only ever held for the bounded case. new SemaphoreSlim(1) has a maximum of
            // int.MaxValue, so the copy's release SUCCEEDS -- nothing throws, nothing is swallowed,
            // and a second caller is now admitted to a section built for one. Per-lease disposal
            // tracking cannot prevent this: the copy carries its own flags.
            using SemaphoreSlim semaphore = new(1);
            SemaphoreLease lease = semaphore.Acquire();
            SemaphoreLease copy = lease;

            lease.Dispose();
            copy.Dispose();

            Assert.AreEqual(2, semaphore.CurrentCount);
        }

        [Test]
        public void TryAcquireTakesAFreePermit()
        {
            using SemaphoreSlim semaphore = new(1, 1);

            bool acquired = semaphore.TryAcquire(out SemaphoreLease lease);

            Assert.IsTrue(acquired);
            Assert.IsTrue(lease.IsHeld);
            Assert.AreEqual(0, semaphore.CurrentCount);
            lease.Dispose();
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [Test]
        public void TryAcquireReportsFailureWhenNoPermitIsFree()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            using SemaphoreLease held = semaphore.Acquire();

            bool acquired = semaphore.TryAcquire(NoWait, out SemaphoreLease lease);

            Assert.IsFalse(acquired);
            Assert.IsFalse(lease.IsHeld);
            // Disposing a lease that was never held must not manufacture a permit.
            lease.Dispose();
            Assert.AreEqual(0, semaphore.CurrentCount);
        }

        [Test]
        public void TryAcquireReportsFailureWhenTheTimeoutElapses()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            using SemaphoreLease held = semaphore.Acquire();

            bool acquired = semaphore.TryAcquire(ShortWait, out SemaphoreLease lease);

            Assert.IsFalse(acquired);
            Assert.IsFalse(lease.IsHeld);
        }

        [Test]
        public void TryAcquireOnANullSemaphoreReportsFailureWithoutThrowing()
        {
            SemaphoreSlim semaphore = null;

            bool acquired = semaphore.TryAcquire(NoWait, out SemaphoreLease lease);

            Assert.IsFalse(acquired);
            Assert.IsFalse(lease.IsHeld);
        }

        [Test]
        public void AcquireOnANullSemaphoreThrowsRatherThanHandingBackAnUnheldLease()
        {
            SemaphoreSlim semaphore = null;

            Assert.Throws<ArgumentNullException>(() => semaphore.Acquire());
        }

        [Test]
        public void ConcurrentLeasesSerializeTheCriticalSection()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            const int workerCount = 8;
            const int incrementsPerWorker = 500;
            int unguardedCounter = 0;

            Task[] workers = new Task[workerCount];
            for (int worker = 0; worker < workerCount; ++worker)
            {
                workers[worker] = Task.Run(() =>
                {
                    for (int i = 0; i < incrementsPerWorker; ++i)
                    {
                        using (semaphore.Acquire())
                        {
                            // Deliberately not Interlocked: the lease is what must make this safe.
                            int read = unguardedCounter;
                            Thread.Yield();
                            unguardedCounter = read + 1;
                        }
                    }
                });
            }

            Task.WaitAll(workers);

            Assert.AreEqual(workerCount * incrementsPerWorker, unguardedCounter);
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [UnityTest]
        public IEnumerator AcquireAsyncTakesThePermitAndDisposeReturnsIt()
        {
            using SemaphoreSlim semaphore = new(1, 1);

            Task<SemaphoreLease> acquire = semaphore.AcquireAsync().AsTask();
            while (!acquire.IsCompleted)
            {
                yield return null;
            }

            SemaphoreLease lease = acquire.Result;
            Assert.IsTrue(lease.IsHeld);
            Assert.AreEqual(0, semaphore.CurrentCount);
            lease.Dispose();
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [UnityTest]
        public IEnumerator AcquireAsyncWaitsForTheHeldPermit()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            SemaphoreLease held = semaphore.Acquire();

            Task<SemaphoreLease> acquire = semaphore.AcquireAsync().AsTask();
            Assert.IsFalse(acquire.IsCompleted);

            held.Dispose();
            while (!acquire.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(acquire.Result.IsHeld);
            acquire.Result.Dispose();
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [UnityTest]
        public IEnumerator ACancelledAcquireAsyncReleasesNothing()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            using SemaphoreLease held = semaphore.Acquire();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Task<SemaphoreLease> acquire = semaphore.AcquireAsync(cancellation.Token).AsTask();
            while (!acquire.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(acquire.IsCanceled || acquire.IsFaulted);
            // The permit the caller already holds must still be the only one outstanding.
            Assert.AreEqual(0, semaphore.CurrentCount);
        }
    }
}
