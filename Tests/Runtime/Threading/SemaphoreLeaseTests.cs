// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Threading
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
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

            /*
                The second release would raise the count above the semaphore's maximum and let two callers into
                a section built for one.
            */
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

            /*
                The whole point of never throwing from Dispose: this runs from the `finally` of the using, so a
                throw there would replace the caller's real failure with a confusing one.
            */
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
        public void DisposingACopiedLeaseReleasesOnceWhenTheMaximumIsExplicit()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            SemaphoreLease lease = semaphore.Acquire();
            SemaphoreLease copy = lease;

            lease.Dispose();

            // An explicit semaphore maximum formerly hid duplicate release behind a swallowed exception.
            Assert.DoesNotThrow(() => copy.Dispose());
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        /*
            The implicit maximum allows duplicate release without throwing; shared lease state must prevent
            admitting extra callers.
        */
        [Test]
        public void DisposingACopiedLeaseDoesNotInflateACountWithNoExplicitMaximum()
        {
            using SemaphoreSlim semaphore = new(1);
            SemaphoreLease lease = semaphore.Acquire();
            SemaphoreLease copy = lease;

            lease.Dispose();
            copy.Dispose();

            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(16)]
        public void ManyCopiesOfALeaseStillReleaseOnce(int copies)
        {
            using SemaphoreSlim semaphore = new(1);
            SemaphoreLease lease = semaphore.Acquire();
            List<SemaphoreLease> duplicates = new(copies);
            for (int i = 0; i < copies; ++i)
            {
                duplicates.Add(lease);
            }

            lease.Dispose();
            foreach (SemaphoreLease duplicate in duplicates)
            {
                duplicate.Dispose();
            }

            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [Test]
        public void ACopyReportsNotHeldOnceAnotherCopyHasReleased()
        {
            using SemaphoreSlim semaphore = new(1, 1);
            SemaphoreLease lease = semaphore.Acquire();
            SemaphoreLease copy = lease;

            Assert.IsTrue(copy.IsHeld);

            lease.Dispose();

            Assert.IsFalse(copy.IsHeld);
            Assert.IsFalse(lease.IsHeld);
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

            Assert.AreEqual(0, semaphore.CurrentCount);
        }
    }
}
