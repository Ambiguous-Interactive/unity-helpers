// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpatialTree3DPerformanceTests
    {
        private const float BoundsTolerance3D = 1e-1f;
        private const float PointBoundsSize = 0.001f;
        private const int BenchmarkTimeoutMilliseconds = 180_000;
        private const int WarmupIterations = 3;

        private static readonly TimeSpan BenchmarkDuration = TimeSpan.FromSeconds(1);

        private static readonly DatasetSpec[] DatasetSpecs =
        {
            new("1,000,000 entries", new Vector3Int(100, 100, 100)),
            new("100,000 entries", new Vector3Int(100, 100, 10)),
            new("10,000 entries", new Vector3Int(100, 10, 10)),
            new("1,000 entries", new Vector3Int(10, 10, 10)),
            new("100 entries", new Vector3Int(10, 5, 2)),
        };

        private static readonly (string Name, float Ratio)[] RangeBenchmarkDefinitions =
        {
            ("Full (~span/2)", 0.5f),
            ("Half (~span/4)", 0.25f),
            ("Quarter (~span/8)", 0.125f),
            ("Tiny (~span/1000)", 0.001f),
        };

        private static readonly (string Name, float Ratio)[] BoundsBenchmarkDefinitions =
        {
            ("Full", 1f),
            ("Half", 0.5f),
            ("Quarter", 0.25f),
        };

        private static readonly (string Label, int Count)[] NeighborBenchmarkDefinitions =
        {
            ("500 neighbors", 500),
            ("100 neighbors", 100),
            ("10 neighbors", 10),
            ("1 neighbor", 1),
        };

        private readonly struct DatasetSpec
        {
            public DatasetSpec(string label, Vector3Int gridSize)
            {
                Label = label;
                GridSize = gridSize;
                TotalPoints = gridSize.x * gridSize.y * gridSize.z;
                Span = new Vector3(
                    Mathf.Max(gridSize.x - 1, 1),
                    Mathf.Max(gridSize.y - 1, 1),
                    Mathf.Max(gridSize.z - 1, 1)
                );
                BoundsCenter = new Vector3(
                    (gridSize.x - 1) * 0.5f,
                    (gridSize.y - 1) * 0.5f,
                    (gridSize.z - 1) * 0.5f
                );
                BoundsSize = Span;
                MaxSpan = Mathf.Max(Span.x, Mathf.Max(Span.y, Span.z));
            }

            public string Label { get; }
            public Vector3Int GridSize { get; }
            public int TotalPoints { get; }
            public Vector3 Span { get; }
            public Vector3 BoundsCenter { get; }
            public Vector3 BoundsSize { get; }
            public float MaxSpan { get; }
        }

        private readonly struct TreeSpec
        {
            public TreeSpec(
                string name,
                Func<IEnumerable<Vector3>, ISpatialTree3D<Vector3>> factory
            )
            {
                Name = name;
                Factory = factory;
            }

            public string Name { get; }
            public Func<IEnumerable<Vector3>, ISpatialTree3D<Vector3>> Factory { get; }
        }

        private readonly struct BoundsSpec
        {
            public BoundsSpec(string label, Bounds bounds)
            {
                Label = label;
                Bounds = bounds;
            }

            public string Label { get; }
            public Bounds Bounds { get; }
        }

        [UnityTest]
        [Timeout(BenchmarkTimeoutMilliseconds)]
        public IEnumerator Benchmark()
        {
            TreeSpec[] treeSpecs = BuildTreeSpecs();
            List<string> treeNames = treeSpecs.Select(spec => spec.Name).ToList();
            List<(DatasetSpec Dataset, List<string> Lines)> datasetOutputs = new();
            foreach (DatasetSpec dataset in DatasetSpecs)
            {
                UnityEngine.Debug.Log(string.Empty);
                UnityEngine.Debug.Log($"SpatialTree3D Benchmarks - {dataset.Label}");
                Vector3[] points = CreateGridPoints(dataset);
                BoundsSpec[] boundsSpecs = BuildBoundsSpecs(dataset);
                (string Label, float Radius)[] rangeBenchmarks = BuildRangeBenchmarks(dataset);
                (string Label, int Count)[] neighborBenchmarks = BuildNeighborBenchmarks(dataset);
                List<string> readmeLines = new();
                Dictionary<string, List<string>> groupRows = new();
                Dictionary<string, Dictionary<string, string>> rowValues = new();
                Dictionary<string, (string Group, string Label)> rowMetadata = new();
                Dictionary<string, int> expectedCounts = new();

                foreach (TreeSpec spec in treeSpecs)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    ISpatialTree3D<Vector3> tree = spec.Factory(points);
                    timer.Stop();
                    RecordRow(
                        groupRows,
                        rowValues,
                        rowMetadata,
                        "Construction",
                        dataset.Label,
                        spec.Name,
                        FormatConstruction(timer.Elapsed)
                    );

                    Vector3 boundaryCenter = tree.Boundary.center;
                    Vector3 rangeCenter = boundaryCenter;
                    List<Vector3> rangeResults = new();
                    foreach ((string label, float radius) in rangeBenchmarks)
                    {
                        tree.GetElementsInRange(rangeCenter, radius, rangeResults);
                        ValidateCount(
                            tree,
                            expectedCounts,
                            "Elements In Range",
                            label,
                            rangeResults.Count
                        );
                        int iterations = MeasureRange(tree, rangeCenter, radius, rangeResults);

                        RecordRow(
                            groupRows,
                            rowValues,
                            rowMetadata,
                            "Elements In Range",
                            label,
                            spec.Name,
                            FormatRate(iterations)
                        );
                    }

                    List<Vector3> boundsResults = new();

                    foreach (BoundsSpec boundsSpec in boundsSpecs)
                    {
                        Bounds queryBounds = TranslateBounds(boundsSpec.Bounds, boundaryCenter);

                        GetElementsInBoundsWithTolerance(tree, queryBounds, boundsResults);

                        ValidateCount(
                            tree,
                            expectedCounts,
                            "Get Elements In Bounds",
                            boundsSpec.Label,
                            boundsResults.Count
                        );

                        int iterations = MeasureBounds(
                            tree,
                            queryBounds,
                            boundsResults,
                            BoundsTolerance3D
                        );

                        RecordRow(
                            groupRows,
                            rowValues,
                            rowMetadata,
                            "Get Elements In Bounds",
                            boundsSpec.Label,
                            spec.Name,
                            FormatRate(iterations)
                        );
                    }

                    List<Vector3> nearestNeighbors = new();

                    foreach ((string label, int count) in neighborBenchmarks)
                    {
                        tree.GetApproximateNearestNeighbors(rangeCenter, count, nearestNeighbors);

                        Assert.LessOrEqual(
                            nearestNeighbors.Count,
                            count,
                            $"Tree '{spec.Name}' returned more than {count} neighbors for '{label}'."
                        );

                        int iterations = MeasureApproximateNearestNeighbors(
                            tree,
                            rangeCenter,
                            count,
                            nearestNeighbors
                        );

                        RecordRow(
                            groupRows,
                            rowValues,
                            rowMetadata,
                            "Approximate Nearest Neighbors",
                            label,
                            spec.Name,
                            FormatRate(iterations)
                        );
                    }
                }

                string[] groupOrder =
                {
                    "Construction",
                    "Elements In Range",
                    "Get Elements In Bounds",
                    "Approximate Nearest Neighbors",
                };

                foreach (string group in groupOrder)
                {
                    if (!groupRows.TryGetValue(group, out List<string> rows))
                    {
                        continue;
                    }

                    LogTable(group, treeNames, rows, rowValues, rowMetadata, readmeLines);

                    UnityEngine.Debug.Log(string.Empty);
                }

                if (readmeLines.Count > 0 && string.IsNullOrWhiteSpace(readmeLines[^1]))
                {
                    readmeLines.RemoveAt(readmeLines.Count - 1);
                }
                datasetOutputs.Add((dataset, readmeLines));
            }

            List<string> finalReadmeLines = BuildTabbedReadmeLines(datasetOutputs);
            BenchmarkReadmeUpdater.UpdateSection(
                "SPATIAL_TREE_3D_BENCHMARKS",
                finalReadmeLines,
                "docs/performance/spatial-tree-3d-performance.md"
            );

            yield break;
        }

        private static TreeSpec[] BuildTreeSpecs()
        {
            return new[]
            {
                new TreeSpec(
                    "KDTree3D (Balanced)",
                    points => new KdTree3D<Vector3>(points, p => p)
                ),
                new TreeSpec(
                    "KDTree3D (Unbalanced)",
                    points => new KdTree3D<Vector3>(points, p => p, balanced: false)
                ),
                new TreeSpec("OctTree3D", points => new OctTree3D<Vector3>(points, p => p)),
                new TreeSpec("RTree3D", points => new RTree3D<Vector3>(points, CreatePointBounds)),
            };
        }

        private static BoundsSpec[] BuildBoundsSpecs(DatasetSpec dataset)
        {
            Vector3 center = dataset.BoundsCenter;
            Vector3 baseSize = dataset.BoundsSize;
            List<BoundsSpec> specs = new();
            foreach ((string name, float ratio) in BoundsBenchmarkDefinitions)
            {
                Vector3 size = new(
                    Mathf.Max(baseSize.x * ratio, 1f),
                    Mathf.Max(baseSize.y * ratio, 1f),
                    Mathf.Max(baseSize.z * ratio, 1f)
                );

                string label =
                    $"{name} (size≈{FormatValue(size.x)}x{FormatValue(size.y)}x{FormatValue(size.z)})";

                specs.Add(new BoundsSpec(label, new Bounds(center, size)));
            }
            specs.Add(new BoundsSpec("Unit (size=1)", new Bounds(center, new Vector3(1f, 1f, 1f))));
            return specs.ToArray();
        }

        private static Vector3[] CreateGridPoints(DatasetSpec dataset)
        {
            Vector3[] points = new Vector3[dataset.TotalPoints];
            int width = dataset.GridSize.x;
            int height = dataset.GridSize.y;
            int depth = dataset.GridSize.z;
            Parallel.For(
                0,
                depth,
                z =>
                {
                    int layerOffset = z * width * height;
                    for (int y = 0; y < height; ++y)
                    {
                        int rowOffset = layerOffset + y * width;
                        for (int x = 0; x < width; ++x)
                        {
                            int index = rowOffset + x;
                            if (index >= points.Length)
                            {
                                continue;
                            }
                            points[index] = new Vector3(x, y, z);
                        }
                    }
                }
            );

            return points;
        }

        private static (string Label, float Radius)[] BuildRangeBenchmarks(DatasetSpec dataset)
        {
            (string Label, float Radius)[] benchmarks = new (string, float)[
                RangeBenchmarkDefinitions.Length
            ];

            for (int i = 0; i < RangeBenchmarkDefinitions.Length; ++i)
            {
                (string name, float ratio) = RangeBenchmarkDefinitions[i];
                float radius = Mathf.Max(1f, dataset.MaxSpan * ratio);
                benchmarks[i] = ($"{name} (r={FormatValue(radius)})", radius);
            }

            return benchmarks;
        }

        private static (string Label, int Count)[] BuildNeighborBenchmarks(DatasetSpec dataset)
        {
            List<(string Label, int Count)> benchmarks = new();
            HashSet<int> seenCounts = new();
            foreach ((string label, int count) in NeighborBenchmarkDefinitions)
            {
                int effectiveCount = Mathf.Min(count, dataset.TotalPoints);

                if (effectiveCount <= 0 || !seenCounts.Add(effectiveCount))
                {
                    continue;
                }

                string effectiveLabel =
                    count == effectiveCount ? label : $"{effectiveCount} neighbors (max)";

                benchmarks.Add((effectiveLabel, effectiveCount));
            }

            return benchmarks.ToArray();
        }

        private static List<string> BuildTabbedReadmeLines(
            IReadOnlyList<(DatasetSpec Dataset, List<string> Lines)> datasetOutputs
        )
        {
            List<string> lines = new();
            if (datasetOutputs.Count == 0)
            {
                return lines;
            }

            // Add an intermediate heading so tab headers (####) increment correctly from h3.
            lines.Add("### Datasets");
            lines.Add(string.Empty);
            lines.Add("<!-- tabs:start -->");
            lines.Add(string.Empty);
            foreach ((DatasetSpec dataset, List<string> datasetLines) in datasetOutputs)
            {
                lines.Add($"#### **{dataset.Label}**");
                lines.Add(string.Empty);
                if (datasetLines.Count > 0)
                {
                    lines.AddRange(datasetLines);
                }
                else
                {
                    lines.Add("_No benchmark data recorded._");
                }

                lines.Add(string.Empty);
            }

            if (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }
            lines.Add("<!-- tabs:end -->");
            return lines;
        }

        private static Bounds TranslateBounds(Bounds template, Vector3 newCenter)
        {
            return new Bounds(newCenter, template.size);
        }

        private static int MeasureRange(
            ISpatialTree3D<Vector3> tree,
            Vector3 center,
            float radius,
            List<Vector3> buffer
        )
        {
            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetElementsInRange(center, radius, buffer);
            }

            Stopwatch timer = Stopwatch.StartNew();
            int iterations = 0;
            do
            {
                tree.GetElementsInRange(center, radius, buffer);
                ++iterations;
            } while (timer.Elapsed < BenchmarkDuration);

            return iterations;
        }

        private static int MeasureBounds(
            ISpatialTree3D<Vector3> tree,
            Bounds bounds,
            List<Vector3> buffer,
            float tolerance
        )
        {
            for (int i = 0; i < WarmupIterations; ++i)
            {
                GetElementsInBoundsWithTolerance(tree, bounds, buffer);
            }

            Stopwatch timer = Stopwatch.StartNew();
            int iterations = 0;
            do
            {
                GetElementsInBoundsWithTolerance(tree, bounds, buffer);
                ++iterations;
            } while (timer.Elapsed < BenchmarkDuration);

            return iterations;
        }

        private static void GetElementsInBoundsWithTolerance(
            ISpatialTree3D<Vector3> tree,
            Bounds bounds,
            List<Vector3> buffer
        )
        {
            // Try tolerance-aware overloads on KD and Oct trees; fallback to interface call otherwise
            switch (tree)
            {
                case KdTree3D<Vector3> kd:
                    kd.GetElementsInBounds(bounds, buffer);
                    break;
                case OctTree3D<Vector3> oct:
                    oct.GetElementsInBounds(bounds, buffer);
                    break;
                default:
                    tree.GetElementsInBounds(bounds, buffer);
                    break;
            }
        }

        private static int MeasureApproximateNearestNeighbors(
            ISpatialTree3D<Vector3> tree,
            Vector3 center,
            int count,
            List<Vector3> buffer
        )
        {
            for (int i = 0; i < WarmupIterations; ++i)
            {
                tree.GetApproximateNearestNeighbors(center, count, buffer);
            }

            Stopwatch timer = Stopwatch.StartNew();
            int iterations = 0;
            do
            {
                tree.GetApproximateNearestNeighbors(center, count, buffer);
                ++iterations;
            } while (timer.Elapsed < BenchmarkDuration);

            return iterations;
        }

        private static void RecordRow(
            Dictionary<string, List<string>> groupRows,
            Dictionary<string, Dictionary<string, string>> rowValues,
            Dictionary<string, (string Group, string Label)> rowMetadata,
            string group,
            string label,
            string treeName,
            string value
        )
        {
            string key = $"{group}::{label}";

            List<string> rows = groupRows.GetOrAdd(group);

            if (!rowValues.TryGetValue(key, out Dictionary<string, string> row))
            {
                row = new Dictionary<string, string>();
                rowValues[key] = row;
                rows.Add(key);
                rowMetadata[key] = (group, label);
            }

            row[treeName] = value;
        }

        private static void ValidateCount(
            ISpatialTree3D<Vector3> tree,
            IDictionary<string, int> expectedCounts,
            string group,
            string label,
            int actualCount
        )
        {
            string key = $"{group}::{label}";
            if (!expectedCounts.TryGetValue(key, out int expected))
            {
                return;
            }

            Assert.AreEqual(
                expected,
                actualCount,
                delta: Math.Max(10, actualCount / 9.5),
                $"Expected '{group}' ({tree.GetType().Name}) -> '{label}' to return {expected} elements, but received {actualCount}."
            );
        }

        private static string FormatRate(int iterations)
        {
            double perSecond = iterations / BenchmarkDuration.TotalSeconds;
            return perSecond.ToString("N0");
        }

        private static string FormatConstruction(TimeSpan elapsed)
        {
            if (elapsed <= TimeSpan.Zero)
            {
                return "n/a";
            }

            double rate = Math.Floor(1d / elapsed.TotalSeconds);
            return $"{rate:N0} ({elapsed.TotalSeconds:F3}s)";
        }

        private static string FormatValue(float value)
        {
            if (value >= 1_000f)
            {
                return value.ToString("N0");
            }

            if (value >= 100f)
            {
                return value.ToString("N1");
            }

            if (value >= 10f)
            {
                return value.ToString("N2");
            }

            if (value >= 1f)
            {
                return value.ToString("0.##");
            }

            return value.ToString("0.###");
        }

        private static void LogTable(
            string header,
            IReadOnlyList<string> treeNames,
            IEnumerable<string> rowKeys,
            IReadOnlyDictionary<string, Dictionary<string, string>> rowValues,
            IReadOnlyDictionary<string, (string Group, string Label)> rowMetadata,
            ICollection<string> readmeLines
        )
        {
            string headerLine = $"| {header} | {string.Join(" | ", treeNames)} |";
            string dividerLine =
                $"| {string.Join(" | ", Enumerable.Repeat("---", treeNames.Count + 1))} |";

            if (readmeLines != null)
            {
                readmeLines.Add($"##### {header}");
                readmeLines.Add("<table data-sortable>");
                readmeLines.Add("  <thead>");
                readmeLines.Add("    <tr>");
                readmeLines.Add($"      <th align=\"left\">{header}</th>");
                foreach (string treeName in treeNames)
                {
                    readmeLines.Add($"      <th align=\"right\">{treeName}</th>");
                }
                readmeLines.Add("    </tr>");
                readmeLines.Add("  </thead>");
                readmeLines.Add("  <tbody>");
            }

            UnityEngine.Debug.Log(headerLine);
            UnityEngine.Debug.Log(dividerLine);

            foreach (string rowKey in rowKeys)
            {
                string label = rowMetadata[rowKey].Label;
                Dictionary<string, string> values = rowValues[rowKey];
                string rowLine =
                    "| "
                    + label
                    + " | "
                    + string.Join(
                        " | ",
                        treeNames.Select(name =>
                            values.TryGetValue(name, out string value) ? value : string.Empty
                        )
                    )
                    + " |";
                UnityEngine.Debug.Log(rowLine);
                if (readmeLines != null)
                {
                    System.Text.StringBuilder htmlRow = new();
                    htmlRow.Append("    <tr><td align=\"left\">");
                    htmlRow.Append(label);
                    htmlRow.Append("</td>");
                    foreach (string name in treeNames)
                    {
                        string cellValue = values.TryGetValue(name, out string v)
                            ? v
                            : string.Empty;
                        htmlRow.Append("<td align=\"right\">");
                        htmlRow.Append(cellValue);
                        htmlRow.Append("</td>");
                    }
                    htmlRow.Append("</tr>");
                    readmeLines.Add(htmlRow.ToString());
                }
            }

            if (readmeLines != null)
            {
                readmeLines.Add("  </tbody>");
                readmeLines.Add("</table>");
                readmeLines.Add(string.Empty);
            }
        }

        private static Bounds CreatePointBounds(Vector3 point)
        {
            return new Bounds(
                point,
                new Vector3(PointBoundsSize, PointBoundsSize, PointBoundsSize)
            );
        }
    }
}
