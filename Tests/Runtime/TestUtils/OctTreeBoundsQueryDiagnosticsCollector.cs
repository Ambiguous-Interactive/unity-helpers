// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public sealed class OctTreeBoundsQueryDiagnosticsCollector
        : OctTree3D<Vector3>.IOctTreeBoundsQueryLogger
    {
        private readonly List<OctTree3D<Vector3>.BoundsQueryNodeTrace> _nodes = new();
        private readonly List<PointEvaluationRecord> _points = new();
        private readonly List<BulkAppendRecord> _bulkAppends = new();
        private readonly List<ChildPruneRecord> _childPrunes = new();

        public Bounds ClosedQuery { get; private set; }

        public BoundingBox3D HalfOpenQuery { get; private set; }

        public BoundingBox3D TreeBounds { get; private set; }

        public bool RootPruned { get; private set; }

        public IReadOnlyList<OctTree3D<Vector3>.BoundsQueryNodeTrace> Nodes => _nodes;

        public IReadOnlyList<PointEvaluationRecord> Points => _points;

        public IReadOnlyList<BulkAppendRecord> BulkAppends => _bulkAppends;

        public IReadOnlyList<ChildPruneRecord> ChildPrunes => _childPrunes;

        public void OnQueryInitialized(
            Bounds closedQuery,
            BoundingBox3D halfOpenQuery,
            BoundingBox3D treeBounds
        )
        {
            ClosedQuery = closedQuery;
            HalfOpenQuery = halfOpenQuery;
            TreeBounds = treeBounds;
        }

        public void OnRootPruned()
        {
            RootPruned = true;
        }

        public void OnNodeVisited(in OctTree3D<Vector3>.BoundsQueryNodeTrace trace)
        {
            _nodes.Add(trace);
        }

        public void OnBulkAppend(
            in OctTree3D<Vector3>.BoundsQueryNodeTrace trace,
            int appendedCount,
            bool viaClosedContainment
        )
        {
            _bulkAppends.Add(new BulkAppendRecord(trace, appendedCount, viaClosedContainment));
        }

        public void OnPointEvaluated(
            Vector3 position,
            bool included,
            in OctTree3D<Vector3>.BoundsQueryNodeTrace trace
        )
        {
            _points.Add(new PointEvaluationRecord(position, included, trace.VisitKind));
        }

        public void OnChildPruned(
            BoundingBox3D childBounds,
            OctTree3D<Vector3>.NodePruneReason reason
        )
        {
            _childPrunes.Add(new ChildPruneRecord(childBounds, reason));
        }

        public string BuildReport(
            ICollection<Vector3> expected,
            ICollection<Vector3> actual,
            int maxItems = 64
        )
        {
            StringBuilder builder = new();
            builder.AppendLine("OctTree diagnostics:");
            builder.AppendLine(
                $"  Query closed center={FormatVector(ClosedQuery.center)}, size={FormatVector(ClosedQuery.size)}"
            );
            builder.AppendLine(
                $"  Half-open min={FormatVector(HalfOpenQuery.min)}, max={FormatVector(HalfOpenQuery.max)}"
            );
            builder.AppendLine($"  Root pruned: {RootPruned}");
            builder.AppendLine($"  Node visits: {_nodes.Count}, point checks: {_points.Count}");
            builder.AppendLine(
                $"  Bulk appends: {_bulkAppends.Count}, child prunes: {_childPrunes.Count}"
            );

            if (0 < _nodes.Count)
            {
                builder.AppendLine("  Sample node visits:");
                foreach (
                    OctTree3D<Vector3>.BoundsQueryNodeTrace trace in _nodes.Take(
                        Math.Min(6, _nodes.Count)
                    )
                )
                {
                    builder.AppendLine(
                        $"    {trace.VisitKind} count={trace.Count} contained={trace.NodeFullyContained} bounds={FormatBox(trace.Boundary)} unity={FormatBounds(trace.UnityBounds)}"
                    );
                }
                builder.AppendLine("  Node visit summary: " + FormatVisitSummary(_nodes));
            }

            if (0 < _points.Count)
            {
                builder.AppendLine("  Sample point evaluations:");
                foreach (PointEvaluationRecord record in _points.Take(Math.Min(8, _points.Count)))
                {
                    builder.AppendLine(
                        $"    {(record.Included ? "✔" : "✖")} {FormatVector(record.Position)} via {record.VisitKind}"
                    );
                }
            }

            if (0 < _childPrunes.Count)
            {
                builder.AppendLine("  Sample child prunes:");
                foreach (
                    ChildPruneRecord record in _childPrunes.Take(Math.Min(6, _childPrunes.Count))
                )
                {
                    builder.AppendLine($"    {record.Reason} bounds={FormatBox(record.Bounds)}");
                }
                builder.AppendLine(
                    "  Child prune summary: "
                        + FormatReasonSummary(_childPrunes.Select(p => p.Reason))
                );
            }

            (List<Vector3> missing, List<Vector3> extra) = ComputeDifferences(
                expected,
                actual,
                maxItems
            );
            if (0 < missing.Count || 0 < extra.Count)
            {
                builder.AppendLine($"  Missing ({missing.Count}): {FormatVectorList(missing)}");
                builder.AppendLine($"  Extra ({extra.Count}): {FormatVectorList(extra)}");
            }

            return builder.ToString();
        }

        private static string FormatVectorList(List<Vector3> items)
        {
            if (items.Count == 0)
            {
                return "[]";
            }

            return "[" + string.Join(", ", items.Select(FormatVector)) + "]";
        }

        private static (List<Vector3> missing, List<Vector3> extra) ComputeDifferences(
            ICollection<Vector3> expected,
            ICollection<Vector3> actual,
            int maxItems
        )
        {
            Dictionary<Vector3, int> left = new();
            foreach (Vector3 v in expected)
            {
                left.TryGetValue(v, out int count);
                left[v] = count + 1;
            }

            Dictionary<Vector3, int> right = new();
            foreach (Vector3 v in actual)
            {
                right.TryGetValue(v, out int count);
                right[v] = count + 1;
            }

            HashSet<Vector3> keys = new(left.Keys);
            keys.UnionWith(right.Keys);

            List<Vector3> missing = new();
            List<Vector3> extra = new();

            foreach (Vector3 key in keys)
            {
                left.TryGetValue(key, out int lc);
                right.TryGetValue(key, out int rc);
                if (rc < lc)
                {
                    int diff = Math.Min(maxItems - missing.Count, lc - rc);
                    for (int i = 0; i < diff; ++i)
                    {
                        missing.Add(key);
                    }
                }
                else if (lc < rc)
                {
                    int diff = Math.Min(maxItems - extra.Count, rc - lc);
                    for (int i = 0; i < diff; ++i)
                    {
                        extra.Add(key);
                    }
                }
                if (maxItems <= missing.Count && maxItems <= extra.Count)
                {
                    break;
                }
            }

            missing.Sort(CompareVector);
            extra.Sort(CompareVector);
            return (missing, extra);
        }

        private static int CompareVector(Vector3 a, Vector3 b)
        {
            int x = a.x.CompareTo(b.x);
            if (x != 0)
            {
                return x;
            }

            int y = a.y.CompareTo(b.y);
            if (y != 0)
            {
                return y;
            }

            return a.z.CompareTo(b.z);
        }

        private static string FormatBox(BoundingBox3D box)
        {
            return $"min={FormatVector(box.min)} max={FormatVector(box.max)}";
        }

        private static string FormatBounds(Bounds bounds)
        {
            return $"min={FormatVector(bounds.min)} max={FormatVector(bounds.max)}";
        }

        private static string FormatVector(Vector3 v)
        {
            return $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
        }

        private static string FormatReasonSummary(
            IEnumerable<OctTree3D<Vector3>.NodePruneReason> reasons
        )
        {
            Dictionary<OctTree3D<Vector3>.NodePruneReason, int> counts = new();
            foreach (OctTree3D<Vector3>.NodePruneReason reason in reasons)
            {
                counts.TryGetValue(reason, out int current);
                counts[reason] = current + 1;
            }

            if (counts.Count == 0)
            {
                return "none";
            }

            return string.Join(", ", counts.Select(pair => $"{pair.Key}: {pair.Value}"));
        }

        private static string FormatVisitSummary(
            IEnumerable<OctTree3D<Vector3>.BoundsQueryNodeTrace> traces
        )
        {
            Dictionary<OctTree3D<Vector3>.NodeVisitKind, int> counts = new();
            foreach (OctTree3D<Vector3>.BoundsQueryNodeTrace trace in traces)
            {
                counts.TryGetValue(trace.VisitKind, out int current);
                counts[trace.VisitKind] = current + 1;
            }

            if (counts.Count == 0)
            {
                return "none";
            }

            return string.Join(", ", counts.Select(pair => $"{pair.Key}: {pair.Value}"));
        }

        public readonly struct PointEvaluationRecord
        {
            internal PointEvaluationRecord(
                Vector3 position,
                bool included,
                OctTree3D<Vector3>.NodeVisitKind visitKind
            )
            {
                Position = position;
                Included = included;
                VisitKind = visitKind;
            }

            public Vector3 Position { get; }

            public bool Included { get; }

            public OctTree3D<Vector3>.NodeVisitKind VisitKind { get; }
        }

        public readonly struct BulkAppendRecord
        {
            internal BulkAppendRecord(
                OctTree3D<Vector3>.BoundsQueryNodeTrace trace,
                int appendedCount,
                bool viaClosedContainment
            )
            {
                Trace = trace;
                AppendedCount = appendedCount;
                ViaClosedContainment = viaClosedContainment;
            }

            public OctTree3D<Vector3>.BoundsQueryNodeTrace Trace { get; }

            public int AppendedCount { get; }

            public bool ViaClosedContainment { get; }
        }

        public readonly struct ChildPruneRecord
        {
            internal ChildPruneRecord(
                BoundingBox3D bounds,
                OctTree3D<Vector3>.NodePruneReason reason
            )
            {
                Bounds = bounds;
                Reason = reason;
            }

            public BoundingBox3D Bounds { get; }

            public OctTree3D<Vector3>.NodePruneReason Reason { get; }
        }
    }
}
