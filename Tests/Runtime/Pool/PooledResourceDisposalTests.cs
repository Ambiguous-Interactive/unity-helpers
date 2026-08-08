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

        // Nobody writes `PooledResource<T> copy = lease;` on purpose. They pass the lease to a
        // method, which takes it by value, and both the callee and the `using` dispose it.
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

        // The dangerous shape, and the one a "is it already in the free list?" check cannot see: by
        // the time the stale copy is disposed the instance is not free, it is rented by someone
        // else, so the release callback clears a list its current renter is still using.
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

        // Each rent grants exactly one return, and a fresh rent re-arms it.
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

        // The guarantee must survive a pool that had instances before anyone rented one, because a
        // pre-warmed instance gets its lease on a different path than a produced one.
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

        // Ordinary pooling must still pool: if the guard rejected legitimate returns the pool would
        // quietly produce a fresh instance every time and nothing else here would notice.
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

        // A lease built through the public constructor has no pool behind it; a single disposal
        // must still run the action exactly once.
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

        // The guarantee has to be free, or it would be paid for on every buffer rent in the
        // package. The lease is a (slot, generation) handle held outside the struct, so acquiring
        // and claiming one allocates nothing; a heap object per lease would show up here.
        // The window is wide on purpose. A pool reaches steady state only after its internal lists
        // and usage tracker have grown once, and GCAssert's default ten iterations still sit inside
        // that one-off cost -- measured at 280 bytes for the first ten cycles and 0.00 bytes per
        // cycle over 100,000, identically before and after the lease existed.
        [Test]
        public void RentAndReturnAllocatesNothing()
        {
            using WallstopGenericPool<List<int>> pool = NewPool();

            GCAssert.DoesNotAllocate(
                () =>
                {
                    using PooledResource<List<int>> lease = pool.Get(out List<int> _);
                },
                warmupIterations: 10_000,
                measuredIterations: 10_000
            );
        }

        // The publicly constructed lease is the path a consumer writes by hand, and it must not be
        // the one that allocates either.
        [Test]
        public void APubliclyConstructedLeaseAllocatesNothing()
        {
            Action<string> onDispose = _ => { };

            GCAssert.DoesNotAllocate(() =>
            {
                using PooledResource<string> lease = new("value", onDispose);
            });
        }

        // Deliberately not asserted for the array pools: they already allocate 32 bytes per rent on
        // their own, because ConcurrentStack allocates a node per push, and that is true with or
        // without the lease (measured identically on both). Asserting zero there would fail for a
        // reason this change did not cause. Tracked in issue 367; the lease's own cost is pinned by
        // DisposalLeaseTests.AcquiringAndClaimingAllocatesNothing.

        // PooledArray has the same defect and the same remedy. WallstopArrayPool clears on return,
        // so a second return does not merely alias the array -- it wipes what the current holder
        // put there.
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

        // Zero-length rents share one Array.Empty<T>() instance across every pool, so they must not
        // take a slot at all -- otherwise all of them would contend over one generation.
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
        // Where a double return actually happens in a game: two threads unwinding the same copied
        // lease. Exactly one may win, on every attempt.
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
