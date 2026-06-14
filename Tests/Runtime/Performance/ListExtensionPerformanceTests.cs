// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ListExtensionPerformanceTests
    {
        private sealed class IntComparer : IComparer<int>
        {
            public static readonly IntComparer Instance = new();

            private IntComparer() { }

            public int Compare(int x, int y)
            {
                return x.CompareTo(y);
            }
        }

        [Test]
        public void SortPerformanceTest()
        {
            const int NumInvocationsPerIteration = 100;
            TimeSpan timeout = TimeSpan.FromSeconds(2.5);

            PcgRandom random = new(123456);

            List<int> list = Enumerable.Range(0, 1_000).ToList();
            list.Shuffle(random);

            int reference = RunTest(list, Array.Sort, timeout);
            int insertionSort = RunTest(
                list,
                input => input.InsertionSort(IntComparer.Instance),
                timeout
            );
            int shellSort = RunTest(list, input => input.GhostSort(IntComparer.Instance), timeout);

            UnityEngine.Debug.Log("| Operation | Operations / Second |");
            UnityEngine.Debug.Log($"| Reference | {reference / timeout.TotalSeconds:N0} |");
            UnityEngine.Debug.Log($"| InsertionSort | {insertionSort / timeout.TotalSeconds:N0} |");
            UnityEngine.Debug.Log($"| ShellSort | {shellSort / timeout.TotalSeconds:N0} |");
            return;

            static int RunTest(List<int> input, Action<int[]> sorter, TimeSpan timeout)
            {
                int[] copy = input.ToArray();
                int length = input.Count;

                int[] toBeSorted = input.ToArray();
                sorter(toBeSorted);
                Assert.IsTrue(toBeSorted.IsSorted());
                Array.Copy(copy, toBeSorted, length);

                int count = 0;
                Stopwatch timer = Stopwatch.StartNew();
                do
                {
                    for (int i = 0; i < NumInvocationsPerIteration; ++i)
                    {
                        sorter(toBeSorted);
                        Array.Copy(copy, toBeSorted, length);
                        ++count;
                    }
                } while (timer.Elapsed < timeout);

                return count;
            }
        }
    }
}
