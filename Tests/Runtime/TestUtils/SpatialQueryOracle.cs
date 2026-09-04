// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Brute-force O(n) reference answers for spatial queries. Nothing here calls a package helper:
    /// it has to be an independent second opinion, or it would agree with the indexed structures for
    /// the same wrong reason. The one thing it does borrow is the documented contract for invalid
    /// input -- a NaN edge, an inverted box, a non-finite center or radius all answer "nothing" --
    /// because that contract is what the structures are being held to.
    /// </summary>
    internal static class SpatialQueryOracle
    {
        internal static List<int> WithinRadius2D(
            IReadOnlyList<Sample> samples,
            Vector2 center,
            float radius,
            float minimumRadius = 0f
        )
        {
            List<int> matches = new();
            if (float.IsNaN(radius) || radius < 0f || !IsFinite(center))
            {
                return matches;
            }

            /*
                Double, not float: squaring a radius past roughly 1.8446744e19 saturates float, and
                so does the squared distance to anything further out, so a float oracle answers
                "everything is inside" for the same wrong reason the structures used to. Double
                never saturates for float inputs, so this stays an independent second opinion.
            */
            double radiusSquared = (double)radius * radius;
            double minimumRadiusSquared = (double)minimumRadius * minimumRadius;
            bool hasMinimum = 0f < minimumRadius;
            for (int i = 0; i < samples.Count; ++i)
            {
                double deltaX = (double)samples[i].position.x - center.x;
                double deltaY = (double)samples[i].position.y - center.y;
                double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                if (radiusSquared < distanceSquared)
                {
                    continue;
                }

                if (hasMinimum && distanceSquared <= minimumRadiusSquared)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        internal static List<int> WithinRadius3D(
            IReadOnlyList<Sample> samples,
            Vector3 center,
            float radius,
            float minimumRadius = 0f
        )
        {
            List<int> matches = new();
            if (float.IsNaN(radius) || radius < 0f || !IsFinite(center))
            {
                return matches;
            }

            /*
                Double, not float: squaring a radius past roughly 1.8446744e19 saturates float, and
                so does the squared distance to anything further out, so a float oracle answers
                "everything is inside" for the same wrong reason the structures used to. Double
                never saturates for float inputs, so this stays an independent second opinion.
            */
            double radiusSquared = (double)radius * radius;
            double minimumRadiusSquared = (double)minimumRadius * minimumRadius;
            bool hasMinimum = 0f < minimumRadius;
            for (int i = 0; i < samples.Count; ++i)
            {
                double deltaX = (double)samples[i].position.x - center.x;
                double deltaY = (double)samples[i].position.y - center.y;
                double deltaZ = (double)samples[i].position.z - center.z;
                double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
                if (radiusSquared < distanceSquared)
                {
                    continue;
                }

                if (hasMinimum && distanceSquared <= minimumRadiusSquared)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        internal static List<int> InsideBox2D(
            IReadOnlyList<Sample> samples,
            Vector2 minimum,
            Vector2 maximum
        )
        {
            List<int> matches = new();
            if (IsNaN(minimum) || IsNaN(maximum))
            {
                return matches;
            }

            for (int i = 0; i < samples.Count; ++i)
            {
                float x = samples[i].position.x;
                float y = samples[i].position.y;
                if (x < minimum.x || maximum.x < x || y < minimum.y || maximum.y < y)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        internal static List<int> InsideBox3D(
            IReadOnlyList<Sample> samples,
            Vector3 minimum,
            Vector3 maximum
        )
        {
            List<int> matches = new();
            if (IsNaN(minimum) || IsNaN(maximum))
            {
                return matches;
            }

            for (int i = 0; i < samples.Count; ++i)
            {
                Vector3 position = samples[i].position;
                if (position.x < minimum.x || maximum.x < position.x)
                {
                    continue;
                }

                if (position.y < minimum.y || maximum.y < position.y)
                {
                    continue;
                }

                if (position.z < minimum.z || maximum.z < position.z)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        /// <summary>
        /// Every extent that touches the closed query box, ignoring z. This is what
        /// <c>GetElementsInBounds</c> promises for a structure whose elements have size: a
        /// straddling element is a true hit, so a broad phase never omits it.
        /// </summary>
        internal static List<int> TouchingBox2D(
            IReadOnlyList<Bounds> extents,
            Vector2 minimum,
            Vector2 maximum
        )
        {
            List<int> matches = new();
            if (IsNaN(minimum) || IsNaN(maximum))
            {
                return matches;
            }

            for (int i = 0; i < extents.Count; ++i)
            {
                Vector3 extentMinimum = extents[i].min;
                Vector3 extentMaximum = extents[i].max;
                if (maximum.x < extentMinimum.x || extentMaximum.x < minimum.x)
                {
                    continue;
                }

                if (maximum.y < extentMinimum.y || extentMaximum.y < minimum.y)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        /// <summary>
        /// Every extent that touches the closed query box. The 3D half of
        /// <see cref="TouchingBox2D"/>.
        /// </summary>
        internal static List<int> TouchingBox3D(
            IReadOnlyList<Bounds> extents,
            Vector3 minimum,
            Vector3 maximum
        )
        {
            List<int> matches = new();
            if (IsNaN(minimum) || IsNaN(maximum))
            {
                return matches;
            }

            for (int i = 0; i < extents.Count; ++i)
            {
                Vector3 extentMinimum = extents[i].min;
                Vector3 extentMaximum = extents[i].max;
                if (maximum.x < extentMinimum.x || extentMaximum.x < minimum.x)
                {
                    continue;
                }

                if (maximum.y < extentMinimum.y || extentMaximum.y < minimum.y)
                {
                    continue;
                }

                if (maximum.z < extentMinimum.z || extentMaximum.z < minimum.z)
                {
                    continue;
                }

                matches.Add(i);
            }

            return matches;
        }

        /// <summary>
        /// The <c>min(count, n)</c> nearest samples, ordered by distance and then by insertion
        /// index, so equal-valued and equidistant samples still have one right answer.
        /// </summary>
        internal static List<int> Nearest2D(
            IReadOnlyList<Sample> samples,
            Vector2 center,
            int count
        )
        {
            List<int> ordered = new();
            if (count <= 0)
            {
                return ordered;
            }

            List<float> distances = new();
            for (int i = 0; i < samples.Count; ++i)
            {
                float deltaX = samples[i].position.x - center.x;
                float deltaY = samples[i].position.y - center.y;
                ordered.Add(i);
                distances.Add((deltaX * deltaX) + (deltaY * deltaY));
            }

            SortByDistanceThenIndex(ordered, distances, samples);
            TrimTo(ordered, count);
            return ordered;
        }

        /// <summary>
        /// The <c>min(count, n)</c> nearest samples, ordered by distance and then by insertion
        /// index, so equal-valued and equidistant samples still have one right answer.
        /// </summary>
        internal static List<int> Nearest3D(
            IReadOnlyList<Sample> samples,
            Vector3 center,
            int count
        )
        {
            List<int> ordered = new();
            if (count <= 0)
            {
                return ordered;
            }

            List<float> distances = new();
            for (int i = 0; i < samples.Count; ++i)
            {
                Vector3 position = samples[i].position;
                float deltaX = position.x - center.x;
                float deltaY = position.y - center.y;
                float deltaZ = position.z - center.z;
                ordered.Add(i);
                distances.Add((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
            }

            SortByDistanceThenIndex(ordered, distances, samples);
            TrimTo(ordered, count);
            return ordered;
        }

        /// <summary>
        /// The <c>min(count, n)</c> <b>farthest</b> samples: the tail of the same ordering. A
        /// nearest-neighbor search that returned these instead would pass an ordering assertion and
        /// a sub-multiset assertion, so the suites compare against this too.
        /// </summary>
        internal static List<int> Farthest2D(
            IReadOnlyList<Sample> samples,
            Vector2 center,
            int count
        )
        {
            List<int> ordered = Nearest2D(samples, center, samples.Count);
            return TakeTail(ordered, count);
        }

        /// <summary>
        /// The <c>min(count, n)</c> <b>farthest</b> samples: the tail of the same ordering.
        /// </summary>
        internal static List<int> Farthest3D(
            IReadOnlyList<Sample> samples,
            Vector3 center,
            int count
        )
        {
            List<int> ordered = Nearest3D(samples, center, samples.Count);
            return TakeTail(ordered, count);
        }

        internal static List<Sample> Project(
            IReadOnlyList<Sample> samples,
            IReadOnlyList<int> picks
        )
        {
            List<Sample> projected = new();
            for (int i = 0; i < picks.Count; ++i)
            {
                projected.Add(samples[picks[i]]);
            }

            return projected;
        }

        private static bool IsFinite(Vector2 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsNaN(Vector2 value)
        {
            return float.IsNaN(value.x) || float.IsNaN(value.y);
        }

        private static bool IsNaN(Vector3 value)
        {
            return float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z);
        }

        private static List<int> TakeTail(List<int> ordered, int count)
        {
            List<int> tail = new();
            int taken = Math.Min(Math.Max(0, count), ordered.Count);
            for (int i = ordered.Count - taken; i < ordered.Count; ++i)
            {
                tail.Add(ordered[i]);
            }

            return tail;
        }

        private static void SortByDistanceThenIndex(
            List<int> ordered,
            List<float> distances,
            IReadOnlyList<Sample> samples
        )
        {
            ordered.Sort(
                (left, right) =>
                {
                    int byDistance = distances[left].CompareTo(distances[right]);
                    if (byDistance != 0)
                    {
                        return byDistance;
                    }

                    return samples[left].insertionIndex.CompareTo(samples[right].insertionIndex);
                }
            );
        }

        private static void TrimTo(List<int> ordered, int count)
        {
            if (count < ordered.Count)
            {
                ordered.RemoveRange(count, ordered.Count - count);
            }
        }

        /// <summary>
        /// One test element: where it is, what it is worth, and which insert produced it. Equality
        /// deliberately ignores <see cref="insertionIndex"/>, so a structure that de-duplicates by
        /// value collapses two samples that a multiset comparison then reports as missing.
        /// </summary>
        internal readonly struct Sample : IEquatable<Sample>
        {
            internal readonly Vector3 position;
            internal readonly int value;
            internal readonly int insertionIndex;

            internal Sample(Vector3 position, int value, int insertionIndex)
            {
                this.position = position;
                this.value = value;
                this.insertionIndex = insertionIndex;
            }

            internal Vector2 Position2D => new(position.x, position.y);

            public bool Equals(Sample other)
            {
                return value == other.value && position.Equals(other.position);
            }

            public override bool Equals(object obj)
            {
                return obj is Sample other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (position.GetHashCode() * 397) ^ value;
            }

            public override string ToString()
            {
                return $"Sample(position: {position:F4}, value: {value}, insert: {insertionIndex})";
            }
        }
    }
}
