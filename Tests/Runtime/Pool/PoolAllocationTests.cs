// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using NUnit.Framework;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Is = UnityEngine.TestTools.Constraints.Is;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolAllocationTests
    {
        private bool _wasMemoryPressureEnabled;

        [SetUp]
        public void SetUp()
        {
            PoolPurgeSettings.ResetToDefaults();
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        /// <remarks>
        /// The measured window starts at the pool's 257th rent, which is where the usage tracker's
        /// sample buffer used to double. Every pool in the package rebuilt that buffer at each
        /// power of two for its first ten thousand rents, so a caller who warmed a rent path and
        /// then measured it saw an allocation whose cause was nowhere near the code it was
        /// measuring.
        /// </remarks>
        [Test]
        public void WarmRentAndReturnDoNotAllocate()
        {
            AllocationProbe.IgnoreWhenUnmeasurable();

            object shared = new();
            using WallstopGenericPool<object> pool = new(() => shared, preWarmCount: 1);

            for (int index = 0; index < AllocationProbe.Iterations; ++index)
            {
                using PooledResource<object> warming = pool.Get(out object _);
            }

            Assert.AreEqual(1, pool.Count, "the warm loop must leave exactly the pooled instance");

            Assert.That(
                () =>
                {
                    for (int index = 0; index < AllocationProbe.Iterations; ++index)
                    {
                        using PooledResource<object> rented = pool.Get(out object _);
                    }
                },
                Is.Not.AllocatingGCMemory(),
                "a rent that finds an instance in the pool allocates nothing, and neither does the "
                    + "usage tracking it records on the way"
            );
        }
    }
}
