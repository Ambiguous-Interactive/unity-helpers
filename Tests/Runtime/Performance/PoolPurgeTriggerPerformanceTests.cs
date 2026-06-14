// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Threading.Tasks;
#endif

    /// <summary>
    /// Performance tests that validate all purge trigger strategies are non-pathological for hot-path usage.
    /// Guards against regression of GitHub issue #226 where the default OnRent trigger caused 119,752% over budget.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    public sealed class PoolPurgeTriggerPerformanceTests
    {
        private const int Iterations = 100_000;
        private const int WarmupIterations = 1000;
        private const long DefaultBudgetMs = 200;
        private const long RelaxedBudgetMs = 500;

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

        private static long MeasureRentReturnCycles(
            WallstopGenericPool<List<int>> pool,
            int warmupIterations,
            int iterations
        )
        {
            for (int i = 0; i < warmupIterations; i++)
            {
                using PooledResource<List<int>> resource = pool.Get(out List<int> list);
                list.Clear();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                using PooledResource<List<int>> resource = pool.Get(out List<int> list);
                list.Clear();
            }
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private static IEnumerable<TestCaseData> SingleThreadedPoolPerformanceCases()
        {
            yield return new TestCaseData(
                "Default settings",
                -1,
                false,
                0f,
                0,
                0,
                DefaultBudgetMs,
                0f
            ).SetName("Trigger.Default.CompletesWithinBudget");

            yield return new TestCaseData(
                "Periodic trigger",
                (int)PurgeTrigger.Periodic,
                false,
                0f,
                0,
                0,
                DefaultBudgetMs,
                0f
            ).SetName("Trigger.Periodic.CompletesWithinBudget");

            yield return new TestCaseData(
                "Explicit trigger",
                (int)PurgeTrigger.Explicit,
                false,
                0f,
                0,
                0,
                DefaultBudgetMs,
                0f
            ).SetName("Trigger.Explicit.CompletesWithinBudget");

            yield return new TestCaseData(
                "OnRent trigger (default options)",
                (int)PurgeTrigger.OnRent,
                false,
                0f,
                0,
                0,
                RelaxedBudgetMs,
                0f
            ).SetName("Trigger.OnRent.DefaultOptions.CompletesWithinBudget");

            yield return new TestCaseData(
                "OnReturn trigger (default options)",
                (int)PurgeTrigger.OnReturn,
                false,
                0f,
                0,
                0,
                RelaxedBudgetMs,
                0f
            ).SetName("Trigger.OnReturn.DefaultOptions.CompletesWithinBudget");

            yield return new TestCaseData(
                "OnRent trigger (with purge criteria)",
                (int)PurgeTrigger.OnRent,
                false,
                60f,
                1000,
                0,
                RelaxedBudgetMs,
                0f
            ).SetName("Trigger.OnRent.WithPurgeCriteria.CompletesWithinBudget");

            yield return new TestCaseData(
                "Combined OnRent|OnReturn",
                (int)(PurgeTrigger.OnRent | PurgeTrigger.OnReturn),
                false,
                0f,
                0,
                0,
                RelaxedBudgetMs,
                0f
            ).SetName("Trigger.Combined.OnRentOnReturn.CompletesWithinBudget");

            yield return new TestCaseData(
                "Periodic + intelligent purging",
                (int)PurgeTrigger.Periodic,
                true,
                300f,
                0,
                0,
                400L,
                0f
            ).SetName("Trigger.Periodic.IntelligentPurging.CompletesWithinBudget");

            yield return new TestCaseData(
                "Pre-warmed (100 items)",
                -1,
                false,
                0f,
                0,
                100,
                DefaultBudgetMs,
                0f
            ).SetName("Trigger.Default.PreWarmed.CompletesWithinBudget");

            yield return new TestCaseData(
                "All triggers combined",
                (int)(PurgeTrigger.OnRent | PurgeTrigger.OnReturn | PurgeTrigger.Periodic),
                false,
                0f,
                0,
                0,
                RelaxedBudgetMs,
                0f
            ).SetName("Trigger.All.CompletesWithinBudget");

            yield return new TestCaseData(
                "Periodic with short interval",
                (int)PurgeTrigger.Periodic,
                false,
                0f,
                0,
                0,
                DefaultBudgetMs,
                0.001f
            ).SetName("Trigger.Periodic.ShortInterval.CompletesWithinBudget");
        }

        [Test]
        [TestCaseSource(nameof(SingleThreadedPoolPerformanceCases))]
        public void SingleThreadedPoolPerformance(
            string description,
            int triggerValue,
            bool useIntelligentPurging,
            float idleTimeoutSeconds,
            int maxPoolSize,
            int preWarmCount,
            long budgetMs,
            float purgeIntervalSeconds
        )
        {
            WallstopGenericPool<List<int>> pool;
            if (triggerValue == -1)
            {
                pool = new WallstopGenericPool<List<int>>(
                    () => new List<int>(),
                    preWarmCount: preWarmCount
                );
            }
            else
            {
                PoolOptions<List<int>> options = new PoolOptions<List<int>>
                {
                    Triggers = (PurgeTrigger)triggerValue,
                    UseIntelligentPurging = useIntelligentPurging,
                };
                if (idleTimeoutSeconds > 0f)
                {
                    options.IdleTimeoutSeconds = idleTimeoutSeconds;
                }
                if (maxPoolSize > 0)
                {
                    options.MaxPoolSize = maxPoolSize;
                }
                if (purgeIntervalSeconds > 0f)
                {
                    options.PurgeIntervalSeconds = purgeIntervalSeconds;
                }
                pool = new WallstopGenericPool<List<int>>(
                    () => new List<int>(),
                    preWarmCount: preWarmCount,
                    options: options
                );
            }

            using (pool)
            {
                long elapsedMs = MeasureRentReturnCycles(pool, WarmupIterations, Iterations);
                double nsPerOp = elapsedMs * 1_000_000.0 / Iterations;

                TestContext.WriteLine(
                    $"{description}: {elapsedMs}ms for {Iterations} rent/return cycles ({nsPerOp:F1} ns/op)"
                );
                Assert.Less(
                    elapsedMs,
                    budgetMs,
                    $"{description} exceeded budget: {elapsedMs}ms (budget: {budgetMs}ms)"
                );
            }
        }

        /// <summary>
        /// Validates that Periodic trigger is not more than 2x slower than Explicit trigger.
        /// </summary>
        [Test]
        public void PeriodicNotMoreThanTwoTimesSlowerThanExplicit()
        {
            using WallstopGenericPool<List<int>> explicitPool = new(
                () => new List<int>(),
                options: new PoolOptions<List<int>> { Triggers = PurgeTrigger.Explicit }
            );

            using WallstopGenericPool<List<int>> periodicPool = new(
                () => new List<int>(),
                options: new PoolOptions<List<int>> { Triggers = PurgeTrigger.Periodic }
            );

            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<List<int>> resource = explicitPool.Get(out List<int> list);
                list.Clear();
            }

            Stopwatch explicitStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                using PooledResource<List<int>> resource = explicitPool.Get(out List<int> list);
                list.Clear();
            }
            explicitStopwatch.Stop();

            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<List<int>> resource = periodicPool.Get(out List<int> list);
                list.Clear();
            }

            Stopwatch periodicStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                using PooledResource<List<int>> resource = periodicPool.Get(out List<int> list);
                list.Clear();
            }
            periodicStopwatch.Stop();

            long explicitMs = explicitStopwatch.ElapsedMilliseconds;
            long periodicMs = periodicStopwatch.ElapsedMilliseconds;
            double explicitNsPerOp = explicitMs * 1_000_000.0 / Iterations;
            double periodicNsPerOp = periodicMs * 1_000_000.0 / Iterations;

            TestContext.WriteLine(
                $"Explicit: {explicitMs}ms ({explicitNsPerOp:F1} ns/op), Periodic: {periodicMs}ms ({periodicNsPerOp:F1} ns/op) for {Iterations} rent/return cycles"
            );
            long adjustedExplicitMs = Math.Max(explicitMs, 1);
            Assert.Less(
                periodicMs,
                adjustedExplicitMs * 2,
                $"Periodic ({periodicMs}ms) was more than 2x slower than Explicit ({explicitMs}ms)"
            );
        }

        /// <summary>
        /// Validates that the static Buffers List pool completes 100K rent/return cycles within budget.
        /// </summary>
        [Test]
        public void BuffersListPoolRentReturnCompletesWithinBudget()
        {
            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<List<int>> resource = Buffers<int>.List.Get(
                    out List<int> list
                );
                list.Clear();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                using PooledResource<List<int>> resource = Buffers<int>.List.Get(
                    out List<int> list
                );
                list.Clear();
            }
            stopwatch.Stop();

            double nsPerOp = stopwatch.ElapsedMilliseconds * 1_000_000.0 / Iterations;
            TestContext.WriteLine(
                $"Buffers<int>.List: {stopwatch.ElapsedMilliseconds}ms for {Iterations} rent/return cycles ({nsPerOp:F1} ns/op)"
            );
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                DefaultBudgetMs,
                $"Buffers<int>.List rent/return exceeded budget: {stopwatch.ElapsedMilliseconds}ms (budget: {DefaultBudgetMs}ms)"
            );
        }

        /// <summary>
        /// Validates that the static Buffers HashSet pool completes 100K rent/return cycles within budget.
        /// </summary>
        [Test]
        public void BuffersHashSetPoolRentReturnCompletesWithinBudget()
        {
            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<HashSet<int>> resource = Buffers<int>.HashSet.Get(
                    out HashSet<int> set
                );
                set.Clear();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                using PooledResource<HashSet<int>> resource = Buffers<int>.HashSet.Get(
                    out HashSet<int> set
                );
                set.Clear();
            }
            stopwatch.Stop();

            double nsPerOp = stopwatch.ElapsedMilliseconds * 1_000_000.0 / Iterations;
            TestContext.WriteLine(
                $"Buffers<int>.HashSet: {stopwatch.ElapsedMilliseconds}ms for {Iterations} rent/return cycles ({nsPerOp:F1} ns/op)"
            );
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                DefaultBudgetMs,
                $"Buffers<int>.HashSet rent/return exceeded budget: {stopwatch.ElapsedMilliseconds}ms (budget: {DefaultBudgetMs}ms)"
            );
        }

        /// <summary>
        /// Validates that the static DictionaryBuffer pool completes 100K rent/return cycles within budget.
        /// </summary>
        [Test]
        public void BuffersDictionaryPoolRentReturnCompletesWithinBudget()
        {
            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<Dictionary<int, int>> resource = DictionaryBuffer<
                    int,
                    int
                >.Dictionary.Get(out Dictionary<int, int> dict);
                dict.Clear();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                using PooledResource<Dictionary<int, int>> resource = DictionaryBuffer<
                    int,
                    int
                >.Dictionary.Get(out Dictionary<int, int> dict);
                dict.Clear();
            }
            stopwatch.Stop();

            double nsPerOp = stopwatch.ElapsedMilliseconds * 1_000_000.0 / Iterations;
            TestContext.WriteLine(
                $"DictionaryBuffer<int, int>.Dictionary: {stopwatch.ElapsedMilliseconds}ms for {Iterations} rent/return cycles ({nsPerOp:F1} ns/op)"
            );
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                DefaultBudgetMs,
                $"DictionaryBuffer rent/return exceeded budget: {stopwatch.ElapsedMilliseconds}ms (budget: {DefaultBudgetMs}ms)"
            );
        }

#if !SINGLE_THREADED
        /// <summary>
        /// Validates that multi-threaded access with Periodic trigger completes within budget.
        /// </summary>
        [Test]
        public void HighContentionPeriodicTriggerCompletesWithinBudget()
        {
            const int threadCount = 4;
            const int iterationsPerThread = Iterations / threadCount;
            const long contentionBudgetMs = 1000;

            using WallstopGenericPool<List<int>> pool = new(
                () => new List<int>(),
                options: new PoolOptions<List<int>> { Triggers = PurgeTrigger.Periodic }
            );

            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<List<int>> resource = pool.Get(out List<int> list);
                list.Clear();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        using PooledResource<List<int>> resource = pool.Get(out List<int> list);
                        list.Clear();
                    }
                });
            }
            Task.WaitAll(tasks);
            stopwatch.Stop();

            TestContext.WriteLine(
                $"High contention ({threadCount} threads): {stopwatch.ElapsedMilliseconds}ms for {Iterations} total rent/return cycles"
            );
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                contentionBudgetMs,
                $"High contention Periodic trigger exceeded budget: {stopwatch.ElapsedMilliseconds}ms (budget: {contentionBudgetMs}ms)"
            );
        }
#endif

#if !SINGLE_THREADED
        /// <summary>
        /// Validates that multi-threaded access with OnRent trigger completes within budget.
        /// Exercises the healthy-pool fast path: the per-call throttle skip (a single
        /// volatile read compared against MinAutoPurgeIntervalSeconds) prevents the
        /// pathological O(n) rescan per Rent/Return under high contention.
        /// </summary>
        [Test]
        public void HighContentionOnRentTriggerCompletesWithinBudget()
        {
            const int threadCount = 4;
            const int iterationsPerThread = Iterations / threadCount;
            const long contentionBudgetMs = 2000;

            using WallstopGenericPool<List<int>> pool = new(
                () => new List<int>(),
                options: new PoolOptions<List<int>>
                {
                    Triggers = PurgeTrigger.OnRent,
                    IdleTimeoutSeconds = 60f,
                    MaxPoolSize = 1000,
                }
            );

            for (int i = 0; i < WarmupIterations; i++)
            {
                using PooledResource<List<int>> resource = pool.Get(out List<int> list);
                list.Clear();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterationsPerThread; i++)
                    {
                        using PooledResource<List<int>> resource = pool.Get(out List<int> list);
                        list.Clear();
                    }
                });
            }
            Task.WaitAll(tasks);
            stopwatch.Stop();

            TestContext.WriteLine(
                $"High contention OnRent ({threadCount} threads): {stopwatch.ElapsedMilliseconds}ms for {Iterations} total rent/return cycles"
            );
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                contentionBudgetMs,
                $"High contention OnRent trigger exceeded budget: {stopwatch.ElapsedMilliseconds}ms (budget: {contentionBudgetMs}ms)"
            );
        }
#endif
    }
}
