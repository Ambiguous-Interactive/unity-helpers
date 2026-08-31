// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public static class SpatialDiagnostics
    {
        public static void AssertMatchingResults(
            string context,
            Bounds bounds,
            ICollection<Vector3> expected,
            ICollection<Vector3> actual,
            int maxItems = 64,
            OctTreeBoundsQueryDiagnosticsCollector octDiagnostics = null
        )
        {
            if (expected is null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (actual is null)
            {
                throw new ArgumentNullException(nameof(actual));
            }

            if (expected.Count == actual.Count)
            {
                if (expected.Count <= maxItems)
                {
                    try
                    {
                        CollectionAssert.AreEquivalent(expected, actual);
                        return;
                    }
                    catch
                    {
                        // Fall through to detailed diff
                    }
                }
                else
                {
                    // Large sets with equal counts: verify via multiset quickly
                    if (AreMultisetsEqual(expected, actual))
                    {
                        return;
                    }
                }
            }

            string message = BuildDifferenceMessage(context, bounds, expected, actual, maxItems);
            if (octDiagnostics is not null)
            {
                message +=
                    Environment.NewLine + octDiagnostics.BuildReport(expected, actual, maxItems);
            }
            Assert.Fail(message);
        }

        private static bool AreMultisetsEqual(ICollection<Vector3> left, ICollection<Vector3> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            Dictionary<Vector3, int> counts = new();
            foreach (Vector3 v in left)
            {
                counts[v] = counts.TryGetValue(v, out int seen) ? seen + 1 : 1;
            }

            foreach (Vector3 v in right)
            {
                if (!counts.TryGetValue(v, out int c))
                {
                    return false;
                }
                if (c == 1)
                {
                    counts.Remove(v);
                }
                else
                {
                    counts[v] = c - 1;
                }
            }

            return counts.Count == 0;
        }

        private static string BuildDifferenceMessage(
            string context,
            Bounds bounds,
            ICollection<Vector3> expected,
            ICollection<Vector3> actual,
            int maxItems
        )
        {
            (List<Vector3> missing, List<Vector3> extra) = ComputeDifferences(
                expected,
                actual,
                maxItems
            );

            BoundingBox3D halfOpen = BoundingBox3D.FromClosedBoundsInclusiveMax(bounds);
            string header =
                $"{context}"
                + $"\nBounds center={FormatVector(bounds.center)}, size={FormatVector(bounds.size)}"
                + $"\nClosed min/max: {FormatVector(bounds.min)} -> {FormatVector(bounds.max)}"
                + $"\nHalf-open min/max(exclusive): {FormatVector(halfOpen.min)} -> {FormatVector(halfOpen.max)}"
                + $"\nKD count={expected.Count}, Oct count={actual.Count}";

            string missingStr =
                missing.Count == 0
                    ? "[]"
                    : "[" + string.Join(", ", missing.Select(FormatVector)) + "]";
            string extraStr =
                extra.Count == 0 ? "[]" : "[" + string.Join(", ", extra.Select(FormatVector)) + "]";

            return header
                + $"\nMissing in Oct ({missing.Count}): {missingStr}"
                + $"\nExtra in Oct ({extra.Count}): {extraStr}";
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
                left[v] = left.TryGetValue(v, out int seen) ? seen + 1 : 1;
            }

            Dictionary<Vector3, int> right = new();
            foreach (Vector3 v in actual)
            {
                right[v] = right.TryGetValue(v, out int seen) ? seen + 1 : 1;
            }

            List<Vector3> missing = new();
            List<Vector3> extra = new();

            HashSet<Vector3> all = new(left.Keys);
            all.UnionWith(right.Keys);

            foreach (Vector3 key in all)
            {
                int lc = left.TryGetValue(key, out int leftCount) ? leftCount : 0;
                int rc = right.TryGetValue(key, out int rightCount) ? rightCount : 0;
                if (rc < lc)
                {
                    int diff = lc - rc;
                    for (int i = 0; i < diff && missing.Count < maxItems; ++i)
                    {
                        missing.Add(key);
                    }
                }
                else if (lc < rc)
                {
                    int diff = rc - lc;
                    for (int i = 0; i < diff && extra.Count < maxItems; ++i)
                    {
                        extra.Add(key);
                    }
                }
            }

            missing.Sort(CompareVector);
            extra.Sort(CompareVector);

            return (missing, extra);
        }

        private static int CompareVector(Vector3 a, Vector3 b)
        {
            int cx = a.x.CompareTo(b.x);
            if (cx != 0)
            {
                return cx;
            }

            int cy = a.y.CompareTo(b.y);
            if (cy != 0)
            {
                return cy;
            }

            return a.z.CompareTo(b.z);
        }

        private static string FormatVector(Vector3 v)
        {
            return $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
        }
    }
}
