// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
#endif

    /// <summary>
    /// The generational handle every struct-based <c>IDisposable</c> in the package relies on to be
    /// disposed exactly once, however many copies of it exist.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DisposalLeaseTests
    {
        [Test]
        public void ADefaultLeaseIsNotHeldAndCannotBeClaimed()
        {
            DisposalLease lease = default;

            Assert.That(lease.IsHeld, Is.False);
            Assert.That(lease.TryClaim(), Is.False);
        }

        [Test]
        public void OnlyTheFirstClaimSucceeds()
        {
            DisposalLease lease = DisposalLeases.Acquire();

            Assert.That(lease.IsHeld, Is.True);
            Assert.That(lease.TryClaim(), Is.True);
            Assert.That(lease.TryClaim(), Is.False);
            Assert.That(lease.IsHeld, Is.False);
        }

        // The whole point: a copy is a separate value carrying the same handle, and only one of the
        // two may win. A bool field inside the struct cannot express this.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(16)]
        public void OnlyOneCopyOfALeaseCanClaim(int copies)
        {
            DisposalLease lease = DisposalLeases.Acquire();
            List<DisposalLease> duplicates = new(copies);
            for (int i = 0; i < copies; ++i)
            {
                duplicates.Add(lease);
            }

            int claimed = lease.TryClaim() ? 1 : 0;
            foreach (DisposalLease duplicate in duplicates)
            {
                if (duplicate.TryClaim())
                {
                    ++claimed;
                }
            }

            Assert.That(claimed, Is.EqualTo(1));
        }

        // A claimed slot goes back on the free list, so the next acquire is very likely to be the
        // same slot. A stale handle from its previous owner must not match it -- this is the case a
        // plain recycled identifier would get wrong.
        [Test]
        public void AStaleLeaseCannotClaimARecycledSlot()
        {
            DisposalLease stale = DisposalLeases.Acquire();
            Assume.That(stale.TryClaim(), Is.True);

            DisposalLease reused = DisposalLeases.Acquire();

            Assert.That(stale.TryClaim(), Is.False, "A stale handle claimed a recycled slot.");
            Assert.That(reused.IsHeld, Is.True);
            Assert.That(reused.TryClaim(), Is.True);
        }

        [Test]
        public void AcquiringAndClaimingAllocatesNothing()
        {
            GCAssert.DoesNotAllocate(() =>
            {
                DisposalLease lease = DisposalLeases.Acquire();
                lease.TryClaim();
            });
        }

        // Balanced acquire and release on one thread must reuse slots rather than mint new ones,
        // or a long-running game would grow the generation table without bound.
        [Test]
        public void BalancedAcquireAndClaimReusesSlots()
        {
            DisposalLease warm = DisposalLeases.Acquire();
            warm.TryClaim();

            int before = DisposalLeases.SlotsCreated;
            for (int i = 0; i < 1000; ++i)
            {
                DisposalLease lease = DisposalLeases.Acquire();
                lease.TryClaim();
            }

            Assert.That(
                DisposalLeases.SlotsCreated,
                Is.EqualTo(before),
                "Balanced acquire/claim created new slots instead of reusing one."
            );
        }

        // Slots are only recycled by a winning claim, so holding many at once must hand out
        // distinct ones.
        [Test]
        public void ConcurrentlyHeldLeasesGetDistinctSlots()
        {
            const int Count = 64;
            List<DisposalLease> held = new(Count);
            for (int i = 0; i < Count; ++i)
            {
                held.Add(DisposalLeases.Acquire());
            }

            foreach (DisposalLease lease in held)
            {
                Assert.That(lease.IsHeld, Is.True);
            }

            int claimed = 0;
            foreach (DisposalLease lease in held)
            {
                if (lease.TryClaim())
                {
                    ++claimed;
                }
            }

            Assert.That(claimed, Is.EqualTo(Count));
        }

        // Enough acquires to force the generation table past its first block, proving growth does
        // not disturb leases already held in earlier blocks.
        [Test]
        public void LeasesSurviveGenerationTableGrowth()
        {
            const int Count = 3000;
            List<DisposalLease> held = new(Count);
            for (int i = 0; i < Count; ++i)
            {
                held.Add(DisposalLeases.Acquire());
            }

            int claimed = 0;
            foreach (DisposalLease lease in held)
            {
                if (lease.TryClaim())
                {
                    ++claimed;
                }
            }

            Assert.That(claimed, Is.EqualTo(Count));
        }

#if !SINGLE_THREADED
        // The one place two threads genuinely contend, so this has to be adversarial rather than
        // merely concurrent. Threads spin on a release flag instead of a Barrier: a barrier's own
        // synchronization scatters the wake-ups, which widens the gap between the racers and lets a
        // deliberately non-atomic claim slip through unnoticed. Verified by mutation -- replacing
        // the compare-and-swap with a read/compare/write fails this test, and did NOT fail the
        // barrier version.
        [Test]
        public void ConcurrentClaimsOfOneLeaseElectOneWinner()
        {
            const int Trials = 3000;
            const int Racers = 4;
            int totalWinners = 0;

            for (int attempt = 0; attempt < Trials; ++attempt)
            {
                DisposalLease lease = DisposalLeases.Acquire();
                int go = 0;
                int winners = 0;

                Task[] racers = new Task[Racers];
                for (int i = 0; i < Racers; ++i)
                {
                    racers[i] = Task.Run(() =>
                    {
                        while (Volatile.Read(ref go) == 0)
                        {
                            Thread.SpinWait(1);
                        }

                        if (lease.TryClaimWithoutRelease())
                        {
                            Interlocked.Increment(ref winners);
                        }
                    });
                }

                // Give the racers a chance to reach their spin loops before releasing them, so they
                // arrive at the claim together rather than strung out behind task startup.
                Thread.Yield();
                Volatile.Write(ref go, 1);
                Task.WaitAll(racers);

                totalWinners += Volatile.Read(ref winners);
                DisposalLeases.Release(lease.SlotForTests);
            }

            Assert.That(
                totalWinners,
                Is.EqualTo(Trials),
                "Across all trials, each lease must be claimed by exactly one racer."
            );
        }

        // The whole registry under contention: many threads acquiring, claiming and recycling at
        // once, which is where a slot handed to two owners, a torn free list, or a lost generation
        // would show up. Every acquire must produce exactly one claim, and no two threads may ever
        // hold the same slot at the same time.
        [Test]
        public void ConcurrentAcquireAndClaimNeverHandsOneSlotToTwoOwners()
        {
            const int Threads = 8;
            const int PerThread = 5_000;
            int claimed = 0;
            int doubleClaimed = 0;
            ConcurrentDictionary<int, int> liveSlots = new();

            Task[] workers = new Task[Threads];
            for (int t = 0; t < Threads; ++t)
            {
                int id = t;
                workers[t] = Task.Run(() =>
                {
                    for (int i = 0; i < PerThread; ++i)
                    {
                        DisposalLease lease = DisposalLeases.Acquire();
                        int slot = lease.SlotForTests;

                        // Two owners of one slot at the same moment is the failure this whole
                        // design exists to prevent, and it would be invisible from claim counts
                        // alone.
                        if (!liveSlots.TryAdd(slot, id))
                        {
                            Interlocked.Increment(ref doubleClaimed);
                        }

                        DisposalLease copy = lease;
                        int wins = 0;
                        if (lease.TryClaimWithoutRelease())
                        {
                            ++wins;
                        }
                        if (copy.TryClaimWithoutRelease())
                        {
                            ++wins;
                        }

                        liveSlots.TryRemove(slot, out int _);
                        DisposalLeases.Release(slot);

                        if (wins == 1)
                        {
                            Interlocked.Increment(ref claimed);
                        }
                    }
                });
            }

            Task.WaitAll(workers);

            Assert.That(doubleClaimed, Is.Zero, "One slot was live on two threads at once.");
            Assert.That(
                claimed,
                Is.EqualTo(Threads * PerThread),
                "Some acquire did not produce exactly one winning claim."
            );
        }

        // Slots are recycled per thread, so leases acquired on one thread and claimed on another
        // must still behave -- that is what a job-system caller does.
        [Test]
        public void LeasesAcquiredOnOneThreadCanBeClaimedOnAnother()
        {
            const int Count = 256;
            DisposalLease[] leases = new DisposalLease[Count];
            Task.Run(() =>
                {
                    for (int i = 0; i < Count; ++i)
                    {
                        leases[i] = DisposalLeases.Acquire();
                    }
                })
                .Wait();

            int claimed = 0;
            Task.Run(() =>
                {
                    foreach (DisposalLease lease in leases)
                    {
                        if (lease.TryClaim())
                        {
                            ++claimed;
                        }
                    }
                })
                .Wait();

            Assert.That(claimed, Is.EqualTo(Count));
        }
#endif
    }
}
