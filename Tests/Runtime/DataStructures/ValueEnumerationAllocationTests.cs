// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Unity.Profiling;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    public sealed class ValueEnumerationAllocationTests
    {
        private const int ProfilerCalls = 128;
        private const int ProfilerCapacity = 512;

        private static int _observedSum;

        private static void Consume(IEnumerable<int> source, bool wrapped)
        {
            int sum = 0;
            if (wrapped)
            {
                foreach (int value in source.AsValueEnumerable())
                {
                    sum += value;
                }
            }
            else
            {
                foreach (int value in source)
                {
                    sum += value;
                }
            }
            _observedSum = sum;
        }

        private static long Measure(Action action)
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            return -1;
#else
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            for (int index = 0; index < 64; ++index)
            {
                action();
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 128; ++index)
            {
                action();
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
#endif
        }

        private static long MeasureProfilerAllocationCount(Action action)
        {
            for (int index = 0; index < 64; ++index)
            {
                action();
            }
            using ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                ProfilerCapacity,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread
            );
            if (!recorder.Valid)
            {
                Assert.Ignore("This runtime does not expose the GC.Alloc marker.");
            }
            for (int index = 0; index < ProfilerCalls; ++index)
            {
                action();
            }
            recorder.Stop();
            Assert.IsFalse(
                recorder.WrappedAround,
                "Allocation samples exceeded recorder capacity."
            );
            long count = 0;
            for (int index = 0; index < recorder.Count; ++index)
            {
                count += recorder.GetSample(index).Count;
            }
            return count;
        }

        [TestCase(0, 1)]
        [TestCase(0, 16)]
        [TestCase(0, 4096)]
        [TestCase(1, 1)]
        [TestCase(1, 16)]
        [TestCase(1, 4096)]
        [TestCase(2, 1)]
        [TestCase(2, 16)]
        [TestCase(2, 4096)]
        public void InterfaceEnumerationAvoidsObservableProfilerAllocations(int shape, int length)
        {
            object[] retained = new object[1];
            object preboxed = 42;
            Action allocate = () => retained[0] = (object)42;
            Action reuse = () => retained[0] = preboxed;
            for (int trial = 0; trial < 3; ++trial)
            {
                long positive = MeasureProfilerAllocationCount(allocate);
                if (positive == 0)
                {
                    Assert.Ignore("The GC.Alloc marker did not observe fresh retained boxes.");
                }
                Assert.AreEqual(ProfilerCalls, positive, "Fresh-box positive control.");
                Assert.AreEqual(0, MeasureProfilerAllocationCount(reuse), "Preboxed control.");
            }

            int[] values = new int[length];
            for (int index = 0; index < values.Length; ++index)
            {
                values[index] = index + 1;
            }
            IEnumerable<int> source =
                shape == 0 ? values
                : shape == 1 ? new List<int>(values)
                : new HashSet<int>(values);
            Action original = () => Consume(source, false);
            Action wrapped = () => Consume(source, true);
            for (int trial = 0; trial < 3; ++trial)
            {
                Assert.AreEqual(ProfilerCalls, MeasureProfilerAllocationCount(original));
                Assert.AreEqual(0, MeasureProfilerAllocationCount(wrapped));
                Assert.AreEqual(length * (length + 1) / 2, _observedSum);
            }
            GC.KeepAlive(retained);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InterfaceEnumerationAvoidsBoxingForCommonSources(int shape)
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            Assert.Ignore("The allocation counter is unsafe on older IL2CPP players.");
#else
            int[] values = { 1, 2, 3, 4, 5 };
            IEnumerable<int> source =
                shape == 0 ? values
                : shape == 1 ? new List<int>(values)
                : new HashSet<int>(values);
            Action original = () => Consume(source, false);
            Action wrapped = () => Consume(source, true);
            long control = Measure(original);
            if (control <= 0)
            {
                Assert.Ignore("The control did not observe interface enumerator allocation.");
            }
            Assert.That(control, Is.GreaterThan(0));
            long actual = Measure(wrapped);
            Assert.That(actual, Is.EqualTo(0));
            Assert.That(_observedSum, Is.EqualTo(15));
#endif
        }
    }
}
