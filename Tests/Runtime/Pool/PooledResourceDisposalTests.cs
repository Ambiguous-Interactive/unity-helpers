// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Threading;
    using System.Threading.Tasks;
#endif

    /// <summary>
    /// A pooled resource must be returned at most once per rent, however many copies of the lease
    /// exist. A pool that holds one instance twice hands it to two live callers, and from then on
    /// every write through one is visible through the other.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PooledResourceDisposalTests
    {
        private static WallstopGenericPool<List<int>> NewPool()
        {
            return new WallstopGenericPool<List<int>>(
                () => new List<int>(),
                onRelease: list => list.Clear()
            );
        }

        // Passing a lease by value creates copies that both the callee and the caller can dispose.
        private static void DisposeByValue(PooledResource<List<int>> lease)
        {
            lease.Dispose();
        }

        [Test]
        public void TwoLiveRentalsNeverResolveToTheSameInstance()
        {
            using WallstopGenericPool<List<int>> pool = NewPool();

            PooledResource<List<int>> lease = pool.Get(out List<int> _);
            DisposeByValue(lease);
            lease.Dispose();

            using PooledResource<List<int>> first = pool.Get(out List<int> firstList);
            using PooledResource<List<int>> second = pool.Get(out List<int> secondList);

            Assert.That(
                ReferenceEquals(firstList, secondList),
                Is.False,
                "Two live rentals resolved to the same instance."
            );

            firstList.Add(1);
            secondList.Add(2);

            Assert.That(firstList, Is.EqualTo(new[] { 1 }));
            Assert.That(secondList, Is.EqualTo(new[] { 2 }));
        }

        /*
            A stale lease may outlive a fresh rental; returning it must not clear another renter's active
            resource.
        */
        [Test]
        public void AStaleCopyDisposedAfterTheInstanceWasRentedAgainDoesNothing()
        {
            using WallstopGenericPool<List<int>> pool = NewPool();

            PooledResource<List<int>> lease = pool.Get(out List<int> instance);
            PooledResource<List<int>> stale = lease;
            lease.Dispose();

            using PooledResource<List<int>> current = pool.Get(out List<int> rentedAgain);
            Assume.That(ReferenceEquals(instance, rentedAgain), Is.True);
            rentedAgain.Add(42);

            stale.Dispose();

            Assert.That(
                rentedAgain,
                Is.EqualTo(new[] { 42 }),
                "A stale lease copy cleared a list its current renter was using."
            );
            Assert.That(
                pool.CurrentPooledCount,
                Is.Zero,
                "A rented instance was parked back in the pool while its renter still held it."
            );
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(8)]
        public void ExtraDisposalsOfCopiesReturnNothingFurther(int extraCopies)
        {
            using WallstopGenericPool<List<int>> pool = NewPool();

            PooledResource<List<int>> lease = pool.Get(out List<int> _);
            List<PooledResource<List<int>>> copies = new(extraCopies);
            for (int i = 0; i < extraCopies; ++i)
            {
                copies.Add(lease);
            }

            lease.Dispose();
            foreach (PooledResource<List<int>> copy in copies)
            {
                copy.Dispose();
            }

            Assert.That(pool.CurrentPooledCount, Is.EqualTo(1));
        }

        [Test]
        public void EachRentGrantsExactlyOneReturn()
        {
            using WallstopGenericPool<List<int>> pool = NewPool();

            for (int i = 0; i < 5; ++i)
            {
                PooledResource<List<int>> lease = pool.Get(out List<int> _);
                PooledResource<List<int>> copy = lease;
                lease.Dispose();
                copy.Dispose();
                Assert.That(pool.CurrentPooledCount, Is.EqualTo(1), $"iteration {i}");
            }
        }

        /*
            Prewarmed instances obtain leases through a different path and need the same stale-return
            protection.
        */
        [Test]
        public void APreWarmedInstanceIsAlsoProtected()
        {
            using WallstopGenericPool<List<int>> pool = new(() => new List<int>(), preWarmCount: 1);

            PooledResource<List<int>> lease = pool.Get(out List<int> _);
            PooledResource<List<int>> copy = lease;
            lease.Dispose();
            copy.Dispose();

            Assert.That(pool.CurrentPooledCount, Is.EqualTo(1));
        }

        // Verify legitimate returns still reuse instances so a guard cannot pass by disabling pooling.
        [Test]
        public void OrdinaryReuseStillReturnsTheSameInstance()
        {
            int produced = 0;
            using WallstopGenericPool<List<int>> pool = new(() =>
            {
                ++produced;
                return new List<int>();
            });

            for (int i = 0; i < 50; ++i)
            {
                using PooledResource<List<int>> lease = pool.Get(out List<int> list);
                list.Add(i);
            }

            Assert.That(produced, Is.EqualTo(1));
        }

        // Publicly constructed leases have no pool to enforce single disposal.
        [Test]
        public void APubliclyConstructedLeaseInvokesItsActionOnce()
        {
            int calls = 0;
            PooledResource<string> lease = new("value", _ => ++calls);
            PooledResource<string> copy = lease;

            lease.Dispose();
            copy.Dispose();

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ValueTypedPoolsStillRoundTrip()
        {
            using WallstopGenericPool<int> pool = new(() => 7);

            using (pool.Get(out int value))
            {
                Assert.That(value, Is.EqualTo(7));
            }

            Assert.That(pool.CurrentPooledCount, Is.EqualTo(1));
        }

        /*
            A short warm-up leaves tracker growth boundaries inside the measured window; a 10,000-iteration
            warm-up previously hid ramp allocations.
        */
        [Test]
        public void RentAndReturnAllocatesNothing()
        {
            using WallstopGenericPool<List<int>> pool = NewPool();

            GCAssert.DoesNotAllocate(
                () =>
                {
                    using PooledResource<List<int>> lease = pool.Get(out List<int> _);
                },
                warmupIterations: 256,
                measuredIterations: 10_000
            );
        }

        // The public lease constructor must preserve the same allocation guarantee.
        [Test]
        public void APubliclyConstructedLeaseAllocatesNothing()
        {
            Action<string> onDispose = _ => { };

            GCAssert.DoesNotAllocate(() =>
            {
                using PooledResource<string> lease = new("value", onDispose);
            });
        }

        /*
            ConcurrentStack already allocates array-pool nodes per return; zero-allocation lease behavior is
            covered separately by DisposalLeaseTests.
        */

        // A duplicate array return clears data held by its current renter, beyond merely aliasing storage.
        [Test]
        public void ACopiedArrayLeaseDoesNotReturnTheArrayTwice()
        {
            PooledArray<int> lease = WallstopArrayPool<int>.Get(8, out int[] first);
            PooledArray<int> copy = lease;
            lease.Dispose();
            copy.Dispose();

            using PooledArray<int> a = WallstopArrayPool<int>.Get(8, out int[] arrayA);
            using PooledArray<int> b = WallstopArrayPool<int>.Get(8, out int[] arrayB);

            Assert.That(
                ReferenceEquals(arrayA, arrayB),
                Is.False,
                "Two live array rentals resolved to the same array."
            );
            Assert.That(first, Is.Not.Null);
        }

        // Empty rents share Array.Empty<T>() globally, so they must not contend for a lease generation.
        [Test]
        public void ZeroLengthArrayLeasesAreInert()
        {
            using PooledArray<int> first = WallstopArrayPool<int>.Get(0, out int[] a);
            PooledArray<int> second = WallstopArrayPool<int>.Get(0, out int[] b);

            second.Dispose();
            second.Dispose();

            Assert.That(a, Is.SameAs(b));
            Assert.That(a, Is.Empty);
        }

#if !SINGLE_THREADED
        // Concurrent unwinding of copied leases must permit exactly one return.
        [Test]
        public void ConcurrentDisposalOfCopiesReturnsOnce()
        {
            for (int attempt = 0; attempt < 100; ++attempt)
            {
                using WallstopGenericPool<List<int>> pool = NewPool();
                PooledResource<List<int>> lease = pool.Get(out List<int> _);
                PooledResource<List<int>> copy = lease;

                using Barrier barrier = new(2);
                Task first = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    lease.Dispose();
                });
                Task second = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    copy.Dispose();
                });
                Task.WaitAll(first, second);

                Assert.That(pool.CurrentPooledCount, Is.EqualTo(1), $"attempt {attempt}");
            }
        }
#endif
    }
}
