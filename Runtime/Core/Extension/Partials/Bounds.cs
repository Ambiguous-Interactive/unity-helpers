// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;
    using UnityEngine;
    using Utils;

    public static partial class UnityExtensions
    {
        public static Vector2 GetCenter(this GameObject gameObject)
        {
            if (gameObject.TryGetComponent(out CenterPointOffset centerPointOffset))
            {
                return centerPointOffset.CenterPoint;
            }

            return gameObject.transform.position;
        }

        public static Bounds Bounds(this in Rect rect)
        {
            Rect self = rect;
            return new Bounds(self.center, self.size);
        }

        public static Rect Rect(this in Bounds bounds)
        {
            Bounds self = bounds;
            return new Rect(self.center - self.extents, self.size);
        }

        public static Rect GetWorldRect(this RectTransform transform)
        {
            // SystemArrayPool: GetWorldCorners requires at least four elements, not exactly
            // four -- measured on 6000.4.6f1, an eight-element array is accepted -- so this does not
            // need the exact-size pool. clearArray is false because the call fills all four.
            using PooledArray<Vector3> fourCornersResource = SystemArrayPool<Vector3>.Get(
                4,
                clearArray: false,
                out Vector3[] fourCorners
            );
            transform.GetWorldCorners(fourCorners);
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (Vector3 corner in fourCorners)
            {
                minX = Mathf.Min(minX, corner.x);
                maxX = Mathf.Max(maxX, corner.x);
                minY = Mathf.Min(minY, corner.y);
                maxY = Mathf.Max(maxY, corner.y);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        public static Bounds OrthographicBounds(this Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            int screenHeight = Screen.height;
            if (screenHeight == 0)
            {
                screenHeight = 1;
            }

            float screenAspect = (float)Screen.width / screenHeight;
            float cameraHeight = camera.orthographicSize * 2;
            float depth = camera.farClipPlane - camera.nearClipPlane;
            if (depth <= 0f)
            {
                depth = 1f;
            }

            Vector3 size = new(cameraHeight * screenAspect, cameraHeight, depth);
            return new Bounds(camera.transform.position, size);
        }

        public static BoundsInt ExpandBounds(this in BoundsInt source, BoundsInt other)
        {
            BoundsInt self = source;
            int xMin = Math.Min(self.xMin, other.xMin);
            int xMax = Math.Max(self.xMax, other.xMax);
            int yMin = Math.Min(self.yMin, other.yMin);
            int yMax = Math.Max(self.yMax, other.yMax);
            int zMin = Math.Min(self.zMin, other.zMin);
            int zMax = Math.Max(self.zMax, other.zMax);
            return new BoundsInt(xMin, yMin, zMin, xMax - xMin, yMax - yMin, zMax - zMin);
        }

        public static BoundsInt? GetBounds(
            this IEnumerable<Vector3Int> positions,
            bool inclusive = false
        )
        {
            bool any = false;
            int xMin = int.MaxValue;
            int xMax = int.MinValue;
            int yMin = int.MaxValue;
            int yMax = int.MinValue;
            int zMin = int.MaxValue;
            int zMax = int.MinValue;
            foreach (Vector3Int position in positions)
            {
                any = true;
                xMin = Math.Min(xMin, position.x);
                xMax = Math.Max(xMax, position.x);
                yMin = Math.Min(yMin, position.y);
                yMax = Math.Max(yMax, position.y);
                zMin = Math.Min(zMin, position.z);
                zMax = Math.Max(zMax, position.z);
            }

            if (!any)
            {
                return null;
            }

            return new BoundsInt(
                xMin,
                yMin,
                zMin,
                xMax - xMin + (inclusive ? 0 : 1),
                yMax - yMin + (inclusive ? 0 : 1),
                zMax - zMin + (inclusive ? 0 : 1)
            );
        }

        public static BoundsInt? GetBounds(this IEnumerable<FastVector3Int> positions)
        {
            bool any = false;
            int xMin = int.MaxValue;
            int xMax = int.MinValue;
            int yMin = int.MaxValue;
            int yMax = int.MinValue;
            int zMin = int.MaxValue;
            int zMax = int.MinValue;
            foreach (FastVector3Int position in positions)
            {
                any = true;
                xMin = Math.Min(xMin, position.X);
                xMax = Math.Max(xMax, position.X);
                yMin = Math.Min(yMin, position.Y);
                yMax = Math.Max(yMax, position.Y);
                zMin = Math.Min(zMin, position.Z);
                zMax = Math.Max(zMax, position.Z);
            }

            if (!any)
            {
                return null;
            }

            return new BoundsInt(
                xMin,
                yMin,
                zMin,
                xMax - xMin + 1,
                yMax - yMin + 1,
                zMax - zMin + 1
            );
        }

        public static Bounds? GetBounds(this IEnumerable<Vector2> positions)
        {
            bool any = false;
            float xMin = float.MaxValue;
            float xMax = float.MinValue;
            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            foreach (Vector2 position in positions)
            {
                any = true;
                xMin = Math.Min(xMin, position.x);
                xMax = Math.Max(xMax, position.x);
                yMin = Math.Min(yMin, position.y);
                yMax = Math.Max(yMax, position.y);
            }

            if (!any)
            {
                return null;
            }

            Vector3 size = new(xMax - xMin, yMax - yMin, 0f);
            Vector3 center = new(xMin + size.x * 0.5f, yMin + size.y * 0.5f, 0f);
            return new Bounds(center, size);
        }

        public static Bounds? GetBounds(this IEnumerable<Bounds> boundaries)
        {
            bool any = false;
            float xMin = float.MaxValue;
            float xMax = float.MinValue;
            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            float zMin = float.MaxValue;
            float zMax = float.MinValue;
            foreach (Bounds boundary in boundaries)
            {
                any = true;
                xMin = Math.Min(xMin, boundary.min.x);
                xMax = Math.Max(xMax, boundary.max.x);
                yMin = Math.Min(yMin, boundary.min.y);
                yMax = Math.Max(yMax, boundary.max.y);
                zMin = Math.Min(zMin, boundary.min.z);
                zMax = Math.Max(zMax, boundary.max.z);
            }

            if (!any)
            {
                return null;
            }

            Vector3 min = new(xMin, yMin, zMin);
            Vector3 max = new(xMax, yMax, zMax);
            return new Bounds((min + max) * 0.5f, max - min);
        }
    }
}
