// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    public static partial class UnityExtensions
    {
        /// <summary>
        /// Stops a Rigidbody2D by zeroing its velocity, angular velocity, and putting it to sleep.
        /// </summary>
        public static void Stop(this Rigidbody2D rigidBody)
        {
            if (rigidBody == null)
            {
                return;
            }

            rigidBody.linearVelocity = Vector2.zero;
            rigidBody.angularVelocity = 0;
            rigidBody.Sleep();
        }

        /// <summary>
        /// Determines if a circle is fully contained within a Collider2D.
        /// </summary>
        public static bool IsCircleFullyContained(
            this Collider2D targetCollider,
            Vector2 center,
            float radius,
            int sampleCount = 16
        )
        {
            if (targetCollider == null)
            {
                throw new ArgumentNullException(nameof(targetCollider));
            }

            if (sampleCount <= 0)
            {
                sampleCount = 1;
            }

            float step = Mathf.PI * 2f / sampleCount;
            for (int i = 0; i < sampleCount; i++)
            {
                float angle = i * step;
                Vector2 pointOnCircle =
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (!targetCollider.OverlapPoint(pointOnCircle))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Inverts a PolygonCollider2D by making holes become solid and the outside become a hole.
        /// </summary>
        public static void Invert(this PolygonCollider2D col, Rect outerRect)
        {
            if (col == null)
            {
                throw new ArgumentNullException(nameof(col));
            }

            int originalCount = col.pathCount;
            if (originalCount == 0)
            {
                return;
            }

            using PooledResource<List<List<Vector2>>> pathsBuffer = Buffers<List<Vector2>>.List.Get(
                out List<List<Vector2>> originalPaths
            );
            using PooledResource<List<PooledResource<List<Vector2>>>> leasesBuffer = Buffers<
                PooledResource<List<Vector2>>
            >.List.Get(out List<PooledResource<List<Vector2>>> leases);

            for (int i = 0; i < originalCount; i++)
            {
                Vector2[] path = col.GetPath(i);
                PooledResource<List<Vector2>> lease = Buffers<Vector2>.List.Get(
                    out List<Vector2> points
                );
                leases.Add(lease);
                points.AddRange(path);
                originalPaths.Add(points);
            }

            using PooledResource<List<Vector2>> outerPathBuffer = Buffers<Vector2>.List.Get(
                out List<Vector2> outerPath
            );
            outerPath.Add(new Vector2(outerRect.xMin, outerRect.yMin));
            outerPath.Add(new Vector2(outerRect.xMin, outerRect.yMax));
            outerPath.Add(new Vector2(outerRect.xMax, outerRect.yMax));
            outerPath.Add(new Vector2(outerRect.xMax, outerRect.yMin));

            col.pathCount = originalCount + 1;
            col.SetPath(0, outerPath);

            for (int i = 0; i < originalCount; ++i)
            {
                List<Vector2> hole = originalPaths[i];
                hole.Reverse();
                col.SetPath(i + 1, hole);
            }

            foreach (PooledResource<List<Vector2>> lease in leases)
            {
                lease.Dispose();
            }
        }
    }
}
