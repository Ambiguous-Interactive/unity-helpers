// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Performance")]
    public sealed class LocalPoolPerformanceTests
    {
        private const int Rounds = 21;
        private const int WarmCalls = 64;
        private const int MaximumCalls = 1 << 24;

        private static long RunStack(Stack<object> pool, int calls)
        {
            long sum = 0;
            for (int index = 0; index < calls; ++index)
            {
                if (pool.TryPop(out object item))
                {
                    pool.Push(item);
                    ++sum;
                }
            }
            return sum;
        }

        private static long RunLocal(LocalPool<object> pool, int calls)
        {
            long sum = 0;
            for (int index = 0; index < calls; ++index)
            {
                object item = pool.Rent();
                pool.Return(item);
                sum += item == null ? 0 : 1;
            }
            return sum;
        }

        private static long RunLocalLease(LocalPool<object> pool, int calls)
        {
            long sum = 0;
            for (int index = 0; index < calls; ++index)
            {
                using PooledResource<object> lease = pool.Get(out object item);
                sum += item == null ? 0 : 1;
            }
            return sum;
        }

        private static long RunSharedLease(WallstopGenericPool<object> pool, int calls)
        {
            long sum = 0;
            for (int index = 0; index < calls; ++index)
            {
                using PooledResource<object> lease = pool.Get(out object item);
                sum += item == null ? 0 : 1;
            }
            return sum;
        }

        private static double Measure(Func<int, long> operation, int calls)
        {
            long start = Stopwatch.GetTimestamp();
            long sum = operation(calls);
            long elapsed = Stopwatch.GetTimestamp() - start;
            Assert.That(sum, Is.EqualTo(calls));
            Assert.That(elapsed, Is.GreaterThan(0));
            return (double)elapsed / Stopwatch.Frequency;
        }

        [Test]
        [Timeout(180000)]
        public void ExplicitAndLeasedPoolsComparedWithBareStack()
        {
            Stopwatch clockControl = Stopwatch.StartNew();
            System.Threading.Thread.Sleep(20);
            clockControl.Stop();
            Assert.IsTrue(Stopwatch.IsHighResolution);
            Assert.That(clockControl.Elapsed.TotalMilliseconds, Is.GreaterThanOrEqualTo(10));
            TestContext.WriteLine(
                FormattableString.Invariant(
                    $"LOCALPOOL_CLOCK,{Stopwatch.Frequency},{clockControl.Elapsed.TotalMilliseconds:R}"
                )
            );
            TestContext.WriteLine(
                $"LOCALPOOL_MODULE,{typeof(LocalPool<object>).Module.ModuleVersionId}"
            );
            Stack<object> stack = new();
            stack.Push(new object());
            LocalPool<object> local = new(static () => new object());
            LocalPool<object> localLease = new(static () => new object());
            using WallstopGenericPool<object> shared = new(static () => new object());
            Func<int, long>[] operations =
            {
                count => RunStack(stack, count),
                count => RunLocal(local, count),
                count => RunLocalLease(localLease, count),
                count => RunSharedLease(shared, count),
            };
            string[] names = { "Stack", "Local", "LocalLease", "SharedLease" };
            foreach (Func<int, long> operation in operations)
            {
                for (int warm = 0; warm < WarmCalls; ++warm)
                {
                    Assert.That(operation(128), Is.EqualTo(128));
                }
            }
            int[] counts = new int[operations.Length];
            for (int arm = 0; arm < operations.Length; ++arm)
            {
                int calls = 128;
                while (Measure(operations[arm], calls) < 0.02 && calls < MaximumCalls)
                {
                    calls *= 2;
                }
                counts[arm] = calls;
            }
            for (int round = 0; round < Rounds; ++round)
            {
                for (int position = 0; position < operations.Length; ++position)
                {
                    int arm = (round + position) % operations.Length;
                    int calls = counts[arm];
                    double seconds = Measure(operations[arm], calls);
                    TestContext.WriteLine(
                        FormattableString.Invariant(
                            $"LOCALPOOL,{round},{names[arm]},{calls},{seconds:R},{seconds * 1e9 / calls:R}"
                        )
                    );
                }
            }
            Assert.That(local.CountInactive, Is.EqualTo(1));
            Assert.That(localLease.CountInactive, Is.EqualTo(1));
        }
    }
}
