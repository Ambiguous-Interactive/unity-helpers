// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System.Collections;
    using System.Diagnostics;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Slow")]
    [Timeout(60000)]
    public sealed class PoolMaintenanceRunnerTests
    {
        [Test]
        public void RuntimeStartupCreatesOneRunnerAndInitializationIsIdempotent()
        {
            PoolMaintenanceRunner[] initial =
                Resources.FindObjectsOfTypeAll<PoolMaintenanceRunner>();
            Assert.AreEqual(
                1,
                initial.Length,
                "Player startup must install the maintenance runner."
            );
            PoolMaintenanceRunner.Initialize();
            PoolMaintenanceRunner.Initialize();
            PoolMaintenanceRunner[] runners =
                Resources.FindObjectsOfTypeAll<PoolMaintenanceRunner>();
            Assert.AreEqual(1, runners.Length);
            Assert.AreSame(initial[0], runners[0]);
            Assert.AreEqual(HideFlags.HideInHierarchy, runners[0].gameObject.hideFlags);
            Assert.IsTrue(runners[0].enabled);
        }

        [UnityTest]
        public IEnumerator IdlePoolIsReapedAtZeroTimeScaleWithoutAnotherRental()
        {
            bool previousMemoryMonitoring = MemoryPressureMonitor.Enabled;
            float previousScale = Time.timeScale;
            PoolPurgeSettings.ResetToDefaults();
            MemoryPressureMonitor.Enabled = false;
            float clock = 1f;
            try
            {
                using WallstopGenericPool<object> pool = new WallstopGenericPool<object>(
                    () => new object(),
                    preWarmCount: 3,
                    options: new PoolOptions<object> { TimeProvider = () => clock }
                );
                Assert.IsTrue(pool.UseIntelligentPurging);
                Assert.AreEqual(3, pool.Count);
                clock += PoolPurgeSettings.DefaultIdleTimeoutSeconds + 1f;
                Time.timeScale = 0f;
                Stopwatch deadline = Stopwatch.StartNew();
                while (0 < pool.Count && deadline.Elapsed.TotalSeconds < 45)
                {
                    yield return null;
                }
                Assert.AreEqual(0, pool.Count, "The runtime frame loop must clean an idle pool.");
                Assert.AreEqual(0, pool.GetStatistics().RentCount);
            }
            finally
            {
                Time.timeScale = previousScale;
                MemoryPressureMonitor.Enabled = previousMemoryMonitoring;
                PoolPurgeSettings.ResetToDefaults();
            }
        }
    }
}
