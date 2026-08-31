// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Brute-force O(n) reference answers for spatial queries. Nothing here calls a package helper:
    /// it has to be an independent second opinion, or it would agree with the indexed structures for
    /// the same wrong reason.
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
            if (
                float.IsNaN(radius)
                || radius < 0f
                || float.IsNaN(center.x)
                || float.IsNaN(center.y)
            )
            {
                return matches;
            }

            float radiusSquared = radius * radius;
            float minimumRadiusSquared = minimumRadius * minimumRadius;
            bool hasMinimum = 0f < minimumRadius;
            for (int i = 0; i < samples.Count; ++i)
            {
                float deltaX = samples[i].position.x - center.x;
                float deltaY = samples[i].position.y - center.y;
                float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
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
            if (
                float.IsNaN(radius)
                || radius < 0f
                || float.IsNaN(center.x)
                || float.IsNaN(center.y)
                || float.IsNaN(center.z)
            )
            {
                return matches;
            }

            float radiusSquared = radius * radius;
            float minimumRadiusSquared = minimumRadius * minimumRadius;
            bool hasMinimum = 0f < minimumRadius;
            for (int i = 0; i < samples.Count; ++i)
            {
                float deltaX = samples[i].position.x - center.x;
                float deltaY = samples[i].position.y - center.y;
                float deltaZ = samples[i].position.z - center.z;
                float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
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
