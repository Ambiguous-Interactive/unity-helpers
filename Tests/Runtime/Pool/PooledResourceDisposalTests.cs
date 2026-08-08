// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System.Collections.Generic;
    using NUnit.Framework;
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
