// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class IListSortingPerformanceTests
    {
        private const string DocumentPath = "docs/performance/ilist-sorting-performance.md";
        private const string SectionPrefix = "ILIST_SORT_";
        private const int NearlySortedSwapPercentage = 50;
        private const int BenchmarkTimeoutMilliseconds = 300_000;
        private const int BenchmarkWarmupIterations = 3;

        private static readonly DatasetSizeSpec[] DatasetSizeSpecs =
        {
            new("100", 100),
            new("1,000", 1_000),
            new("10,000", 10_000),
            new("100,000", 100_000),
        };

        private static readonly DatasetState[] DatasetStates =
        {
            new("Sorted", BuildSortedData),
            new("Nearly Sorted (2% swaps)", BuildNearlySortedData),
            new("Shuffled (deterministic)", BuildShuffledData),
        };

        private static readonly SortImplementation[] SortImplementations =
        {
            new("Ghost", SortAlgorithm.Ghost, false, int.MaxValue),
            new("Meteor", SortAlgorithm.Meteor, false, int.MaxValue),
            new(
                "Pattern-Defeating QuickSort",
                SortAlgorithm.PatternDefeatingQuickSort,
                false,
                int.MaxValue
            ),
            new("Grail", SortAlgorithm.Grail, true, int.MaxValue),
            new("Power", SortAlgorithm.Power, true, int.MaxValue),
            new("Insertion", SortAlgorithm.Insertion, true, 10_000),
            new("Tim", SortAlgorithm.Tim, true, int.MaxValue),
            new("Jesse", SortAlgorithm.Jesse, false, int.MaxValue),
            new("Green", SortAlgorithm.Green, true, int.MaxValue),
            new("Ska", SortAlgorithm.Ska, false, int.MaxValue),
            new("Ipn", SortAlgorithm.Ipn, false, int.MaxValue),
            new("Smooth", SortAlgorithm.Smooth, false, int.MaxValue),
            new("Block", SortAlgorithm.Block, true, int.MaxValue),
            new("IPS4o", SortAlgorithm.Ips4o, false, int.MaxValue),
            new("Power+", SortAlgorithm.PowerPlus, true, int.MaxValue),
            new("Glide", SortAlgorithm.Glide, true, int.MaxValue),
            new("Flux", SortAlgorithm.Flux, false, int.MaxValue),
        };

        [Test]
        [Timeout(BenchmarkTimeoutMilliseconds)]
        public void Benchmark()
        {
            string operatingSystemToken = GetOperatingSystemToken();
            string sectionName = SectionPrefix + operatingSystemToken;

            List<string> readmeLines = new()
            {
                string.Format(
                    CultureInfo.InvariantCulture,
                    "_Last updated {0:yyyy-MM-dd HH:mm} UTC on {1}_",
                    DateTime.UtcNow,
                    SystemInfo.operatingSystem
                ),
                string.Empty,
                "Times are single-pass measurements in milliseconds (lower is better). `n/a` indicates the algorithm was skipped for the dataset size.",
                string.Empty,
            };

            IComparer<int> comparer = Comparer<int>.Default;
            string headerLine = BuildHeaderLine();
            string dividerLine = BuildDividerLine();

            foreach (DatasetState datasetState in DatasetStates)
            {
                UnityEngine.Debug.Log($"IList Sorting Benchmarks - {datasetState.Label}");
                UnityEngine.Debug.Log(headerLine);
                UnityEngine.Debug.Log(dividerLine);

                readmeLines.Add($"### {datasetState.Label}");
                readmeLines.AddRange(BuildTableHeader());

                foreach (DatasetSizeSpec sizeSpec in DatasetSizeSpecs)
                {
                    int[] baseData = datasetState.CreateData(sizeSpec.Count);
                    string rowLine = BuildRowLine(sizeSpec.Label, baseData, comparer);
                    UnityEngine.Debug.Log(rowLine);
                    readmeLines.Add(rowLine);
                }

                readmeLines.AddRange(BuildTableFooter());
                readmeLines.Add(string.Empty);
                UnityEngine.Debug.Log(string.Empty);
            }

            BenchmarkReadmeUpdater.UpdateSection(sectionName, readmeLines, DocumentPath);
        }

        private static List<string> BuildTableHeader()
        {
            List<string> lines = new()
            {
                "<table data-sortable>",
                "  <thead>",
                "    <tr>",
                "      <th align=\"left\">List Size</th>",
            };

            foreach (SortImplementation implementation in SortImplementations)
            {
                lines.Add($"      <th align=\"right\">{implementation.Label}</th>");
            }

            lines.Add("    </tr>");
            lines.Add("  </thead>");
            lines.Add("  <tbody>");

            return lines;
        }

        private static List<string> BuildTableFooter()
        {
            return new List<string> { "  </tbody>", "</table>" };
        }

        private static string BuildHeaderLine()
        {
            StringBuilder headerBuilder = new();
            headerBuilder.Append("| List Size |");
            foreach (SortImplementation implementation in SortImplementations)
            {
                headerBuilder.Append(' ');
                headerBuilder.Append(implementation.Label);
                headerBuilder.Append(" |");
            }

            return headerBuilder.ToString();
        }

        private static string BuildDividerLine()
        {
            StringBuilder dividerBuilder = new();
            dividerBuilder.Append("| --- |");
            foreach (SortImplementation implementation in SortImplementations)
            {
                dividerBuilder.Append(" --- |");
            }

            return dividerBuilder.ToString();
        }

        private static string BuildRowLine(
            string sizeLabel,
            int[] baseData,
            IComparer<int> comparer
        )
        {
            StringBuilder rowBuilder = new();
            rowBuilder.Append("    <tr><td align=\"left\">");
            rowBuilder.Append(sizeLabel);
            rowBuilder.Append("</td>");

            foreach (SortImplementation implementation in SortImplementations)
            {
                string result = BenchmarkImplementation(implementation, baseData, comparer);
                rowBuilder.Append("<td align=\"right\">");
                rowBuilder.Append(result);
                rowBuilder.Append("</td>");
            }

            rowBuilder.Append("</tr>");
            return rowBuilder.ToString();
        }

        private static string BenchmarkImplementation(
            SortImplementation implementation,
            int[] baseData,
            IComparer<int> comparer
        )
        {
            if (baseData.Length > implementation.MaxSupportedCount)
            {
                return "n/a";
            }

            int[] workingData = new int[baseData.Length];
            IList<int> workingList = workingData;
            RunWarmups(implementation, baseData, comparer, workingData, workingList);
            Array.Copy(baseData, workingData, baseData.Length);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                implementation.Execute(workingList, comparer);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError(
                    $"Sorting benchmark failed for {implementation.Label}: {exception.Message}"
                );
                return "error";
            }
            finally
            {
                stopwatch.Stop();
            }

            double milliseconds = stopwatch.Elapsed.TotalMilliseconds;
            return FormatDuration(milliseconds);
        }

        private static void RunWarmups(
            SortImplementation implementation,
            int[] baseData,
            IComparer<int> comparer,
            int[] workingData,
            IList<int> workingList
        )
        {
            if (BenchmarkWarmupIterations <= 0 || baseData.Length == 0)
            {
                return;
            }

            for (int i = 0; i < BenchmarkWarmupIterations; ++i)
            {
                Array.Copy(baseData, workingData, baseData.Length);
                implementation.Execute(workingList, comparer);
            }
        }

        private static string FormatDuration(double milliseconds)
        {
            if (milliseconds >= 1000d)
            {
                double seconds = milliseconds / 1000d;
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} s", seconds);
            }

            if (milliseconds >= 100d)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0} ms", milliseconds);
            }

            if (milliseconds >= 10d)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} ms", milliseconds);
            }

            if (milliseconds >= 1d)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.00} ms", milliseconds);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.000} ms", milliseconds);
        }

        private static int[] BuildSortedData(int count)
        {
            int[] data = new int[count];
            for (int i = 0; i < count; ++i)
            {
                data[i] = i;
            }

            return data;
        }

        private static int[] BuildNearlySortedData(int count)
        {
            int[] data = BuildSortedData(count);
            if (count <= 1)
            {
                return data;
            }

            int swaps = Math.Max(1, count / NearlySortedSwapPercentage);
            IRandom random = new PcgRandom(unchecked(count * 37));

            for (int i = 0; i < swaps; ++i)
            {
                int index = random.Next(0, count - 1);
                int neighbor = Math.Min(count - 1, index + 1);
                int temp = data[index];
                data[index] = data[neighbor];
                data[neighbor] = temp;
            }

            return data;
        }

        private static int[] BuildShuffledData(int count)
        {
            int[] data = BuildSortedData(count);
            if (count <= 1)
            {
                return data;
            }

            IRandom random = new PcgRandom(unchecked(count * 7919));

            for (int i = count - 1; i > 0; --i)
            {
                int swapIndex = random.Next(0, i + 1);
                int temp = data[i];
                data[i] = data[swapIndex];
                data[swapIndex] = temp;
            }

            return data;
        }

        private static string GetOperatingSystemToken()
        {
            RuntimePlatform platform = Application.platform;
            switch (platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsServer:
                    return "WINDOWS";
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    return "MACOS";
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxServer:
                    return "LINUX";
                default:
                    return "OTHER";
            }
        }

        private readonly struct DatasetSizeSpec
        {
            public DatasetSizeSpec(string label, int count)
            {
                Label = label;
                Count = count;
            }

            public string Label { get; }

            public int Count { get; }
        }

        private readonly struct DatasetState
        {
            private readonly Func<int, int[]> generator;

            public DatasetState(string label, Func<int, int[]> generator)
            {
                Label = label;
                this.generator = generator;
            }

            public string Label { get; }

            public int[] CreateData(int count)
            {
                return generator(count);
            }
        }

        private readonly struct SortImplementation
        {
            public SortImplementation(
                string label,
                SortAlgorithm algorithm,
                bool isStable,
                int maxSupportedCount
            )
            {
                Label = label;
                Algorithm = algorithm;
                IsStable = isStable;
                MaxSupportedCount = maxSupportedCount;
            }

            public string Label { get; }

            public SortAlgorithm Algorithm { get; }

            public bool IsStable { get; }

            public int MaxSupportedCount { get; }

            public void Execute(IList<int> list, IComparer<int> comparer)
            {
                list.Sort(comparer, Algorithm);
            }
        }
    }
}
