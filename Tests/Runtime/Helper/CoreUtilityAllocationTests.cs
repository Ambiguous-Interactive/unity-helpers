// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_5_3_OR_NEWER
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class CoreUtilityAllocationTests : CommonTestBase
    {
        private const int Calls = 128;
        private const int RecorderCapacity = 512;

        private static long Measure(Action action)
        {
            return ProfilerAllocationCounter.Measure(action, Calls, RecorderCapacity);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void WarmUtilityPathsProduceNoObservableAllocation(int shape)
        {
            object[] retained = new object[1];
            object preboxed = 42;
            Action allocate = () => retained[0] = (object)42;
            Action reuse = () => retained[0] = preboxed;
            long control = Measure(allocate);
            if (control == 0)
            {
                Assert.Ignore("The profiler did not observe retained boxes.");
            }
            Assert.That(control, Is.EqualTo(Calls));
            Assert.That(Measure(reuse), Is.Zero);
            int sum = 0;
            (int from, int to)[] edges = { (0, 1), (1, 2), (2, 0), (2, 3) };
            BitSet result = new(64);
            LongMap<int> map = new(64);
            LongSet set = new(64);
            for (ulong key = 0; key < 64; ++key)
            {
                map.TryAdd(key << 32, (int)key);
                set.TryAdd(key << 32);
            }
            using KeyedObjectPool<int, Texture2D> pool = new(_ => Track(new Texture2D(1, 1)));
            Action subject = shape switch
            {
                0 => () =>
                {
                    foreach (int bit in BitOps.SetBits(ulong.MaxValue))
                    {
                        sum += bit;
                    }
                },
                1 => () =>
                {
                    sum += EnumValues<DayOfWeek>.IsDefined(DayOfWeek.Monday) ? 1 : 0;
                    sum += EnumValues<DayOfWeek>.IsDefined((DayOfWeek)128) ? 1 : 0;
                    sum += EnumNameCache<DayOfWeek>.TryParse("Monday".AsSpan(), out _) ? 1 : 0;
                    sum += EnumNameCache<DayOfWeek>.TryParse("monday".AsSpan(), out _, true)
                        ? 1
                        : 0;
                },
                2 => () => sum += GraphReach.Reachable(4, edges, 0, result) ? 1 : 0,
                3 => () => sum += (int)GraphReach.Reachable(4, edges, 0),
                4 => () =>
                {
                    if (pool.TryTake(1, out Texture2D item))
                    {
                        sum += pool.Release(item) ? 1 : 0;
                    }
                },
                5 => () =>
                {
                    foreach (System.Collections.Generic.KeyValuePair<ulong, int> pair in map)
                    {
                        sum += pair.Value;
                    }
                    foreach (ulong key in map.Keys)
                    {
                        sum += (int)(key >> 32);
                    }
                    foreach (int value in map.Values)
                    {
                        sum += value;
                    }
                    map.Remove(0, out _);
                    map.TryAdd(0, 0);
                    map.Clear();
                    for (ulong key = 0; key < 64; ++key)
                    {
                        map.TryAdd(key << 32, (int)key);
                    }
                },
                _ => () =>
                {
                    foreach (ulong key in set)
                    {
                        sum += (int)(key >> 32);
                    }
                    set.Remove(0);
                    set.TryAdd(0);
                    set.Clear();
                    for (ulong key = 0; key < 64; ++key)
                    {
                        set.TryAdd(key << 32);
                    }
                },
            };
            for (int trial = 0; trial < 3; ++trial)
            {
                Assert.That(Measure(subject), Is.Zero, $"Shape {shape}, trial {trial}");
            }
            Assert.That(sum, Is.Not.Zero);
            GC.KeepAlive(retained);
        }
    }
#endif
}
