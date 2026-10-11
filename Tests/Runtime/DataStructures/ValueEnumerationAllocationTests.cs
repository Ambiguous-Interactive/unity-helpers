// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    public sealed class ValueEnumerationAllocationTests
    {
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
