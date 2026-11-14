// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Runtime;
    using NUnit.Framework;
    using ThreadAllocationCounter = WallstopStudios.UnityHelpers.Tests.Core.ThreadAllocationCounter;

    [TestFixture]
    [NonParallelizable]
    public sealed class AllocationMeasurementScopeTests
    {
        [Test]
        public void ScopeKeepsAnAllocationFreeLoopInsideANoGcRegion()
        {
            using AllocationMeasurementScope scope = AllocationMeasurementScope.Start();
            Assert.AreEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
            long before = ThreadAllocationCounter.Read();
            long sum = 0;
            for (int index = 0; index < 10_000; ++index)
            {
                sum += index;
            }

            long allocated = ThreadAllocationCounter.Read() - before;
            Assert.AreEqual(49_995_000, sum);
            Assert.AreEqual(0, allocated);
        }

        [Test]
        public void ScopeKeepsRealAllocationsVisible()
        {
            using AllocationMeasurementScope scope = AllocationMeasurementScope.Start();
            long before = ThreadAllocationCounter.Read();
            byte[] payload = new byte[4096];
            long allocated = ThreadAllocationCounter.Read() - before;
            GC.KeepAlive(payload);
            Assert.GreaterOrEqual(allocated, payload.Length);
        }

        [Test]
        public void ScopeReleasesItsRegionWhenTheMeasurementFails()
        {
            GCLatencyMode original = GCSettings.LatencyMode;
            Assert.Throws<AssertionException>(() =>
            {
                using AllocationMeasurementScope scope = AllocationMeasurementScope.Start();
                Assert.Fail("Controlled measurement failure.");
            });
            Assert.AreEqual(original, GCSettings.LatencyMode);
            using AllocationMeasurementScope next = AllocationMeasurementScope.Start();
            Assert.AreEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        }

        [Test]
        public void ScopeRejectsNestedEntryWithoutEndingTheExistingRegion()
        {
            using AllocationMeasurementScope owner = AllocationMeasurementScope.Start();
            Assert.Throws<AssertionException>(() => AllocationMeasurementScope.Start());
            Assert.AreEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        }

        [Test]
        public void ScopeRejectsACollectionDuringTheMeasurement()
        {
            AllocationMeasurementScope scope = AllocationMeasurementScope.Start();
            GC.Collect(0, GCCollectionMode.Forced, blocking: true);
            Assert.Throws<InvalidOperationException>(() => scope.Dispose());
            Assert.AreNotEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
            using AllocationMeasurementScope next = AllocationMeasurementScope.Start();
            Assert.AreEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        }

        [Test]
        public void ScopeDisposalIsIdempotent()
        {
            GCLatencyMode original = GCSettings.LatencyMode;
            AllocationMeasurementScope scope = AllocationMeasurementScope.Start();
            scope.Dispose();
            scope.Dispose();
            Assert.AreEqual(original, GCSettings.LatencyMode);
        }

        [Test]
        public void AliasedScopeDoesNotEndTheNextScopesRegion()
        {
            AllocationMeasurementScope first = AllocationMeasurementScope.Start();
            AllocationMeasurementScope alias = first;
            first.Dispose();
            using AllocationMeasurementScope next = AllocationMeasurementScope.Start();
            alias.Dispose();
            Assert.AreEqual(GCLatencyMode.NoGCRegion, GCSettings.LatencyMode);
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void InvalidBudgetDoesNotAcquireARegion(long allocationBudget)
        {
            GCLatencyMode original = GCSettings.LatencyMode;
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AllocationMeasurementScope(allocationBudget)
            );
            Assert.AreEqual(original, GCSettings.LatencyMode);
        }
    }
}
