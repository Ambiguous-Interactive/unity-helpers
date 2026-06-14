// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ConvexHullPerformanceTests : CommonTestBase
    {
        private const int LargeMinX = -1700;
        private const int LargeMaxX = 1700;
        private const int LargeMinY = -850;
        private const int LargeMaxY = 850;
        private const long RotatedVectorAllocationBudgetBytes = 8_192;
        private const long RotatedGridAllocationBudgetBytes = 12_288;

        [Test]
        public void BuildConvexHullRotatedVectorAllocationsStayBounded()
        {
            const float angleDegrees = 18.75f;
            float radians = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            List<Vector2> perimeter = CreateRotatedVectorPerimeter(
                LargeMinX,
                LargeMaxX,
                LargeMinY,
                LargeMaxY,
                angleDegrees: 18.75f
            );

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<Vector2> hull = perimeter.BuildConvexHull(includeColinearPoints: false);
                    Assert.AreEqual(4, hull.Count);
                },
                warmupIterations: 2,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                RotatedVectorAllocationBudgetBytes,
                $"Rotated Vector2 convex hull should allocate no more than {RotatedVectorAllocationBudgetBytes} bytes (measured {allocated})."
            );
        }

        [Test]
        public void BuildConvexHullRotatedGridAllocationsStayBounded()
        {
            GameObject gridObject = Track(new GameObject("ConvexHullRotatedGrid"));
            Grid grid = gridObject.AddComponent<Grid>();
            grid.transform.position = new Vector3(5000f, -12500f, 0f);
            grid.transform.rotation = Quaternion.Euler(0f, 0f, 22.5f);
            grid.transform.localScale = new Vector3(0.125f, 0.333f, 1f);
            grid.cellSize = new Vector3(0.125f, 0.333f, 1f);

            List<FastVector3Int> perimeter = CreateGridPerimeter(
                LargeMinX,
                LargeMaxX,
                LargeMinY,
                LargeMaxY
            );

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<FastVector3Int> hull = perimeter.BuildConvexHull(
                        grid,
                        includeColinearPoints: false
                    );
                    Assert.AreEqual(4, hull.Count);
                },
                warmupIterations: 2,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                RotatedGridAllocationBudgetBytes,
                $"Rotated grid convex hull should allocate no more than {RotatedGridAllocationBudgetBytes} bytes (measured {allocated})."
            );
        }

        private static List<Vector2> CreateRotatedVectorPerimeter(
            int minX,
            int maxX,
            int minY,
            int maxY,
            float angleDegrees
        )
        {
            double cos = Math.Cos(angleDegrees * Mathf.Deg2Rad);
            double sin = Math.Sin(angleDegrees * Mathf.Deg2Rad);

            Vector2 bottomLeft = RotateExact(minX, minY, cos, sin);
            Vector2 bottomRight = RotateExact(maxX, minY, cos, sin);
            Vector2 topRight = RotateExact(maxX, maxY, cos, sin);
            Vector2 topLeft = RotateExact(minX, maxY, cos, sin);

            List<Vector2> perimeter = new((maxX - minX + 1) * 2 + Math.Max(0, maxY - minY - 1) * 2);

            for (int x = minX; x <= maxX; ++x)
            {
                float t = (x - minX) / (float)(maxX - minX);
                perimeter.Add(Quantize(Vector2.Lerp(bottomLeft, bottomRight, t)));
            }

            for (int x = minX; x <= maxX; ++x)
            {
                float t = (x - minX) / (float)(maxX - minX);
                perimeter.Add(Quantize(Vector2.Lerp(topLeft, topRight, t)));
            }

            for (int y = minY + 1; y < maxY; ++y)
            {
                float t = (y - minY) / (float)(maxY - minY);
                perimeter.Add(Quantize(Vector2.Lerp(topLeft, bottomLeft, t)));
                perimeter.Add(Quantize(Vector2.Lerp(topRight, bottomRight, t)));
            }

            return perimeter;
        }

        private static Vector2 RotateExact(int x, int y, double cos, double sin)
        {
            double fx = x;
            double fy = y;
            double rotatedX = fx * cos - fy * sin;
            double rotatedY = fx * sin + fy * cos;
            return new Vector2((float)rotatedX, (float)rotatedY);
        }

        private static Vector2 Quantize(Vector2 value)
        {
            float x = Mathf.Round(value.x * 1000f) * 0.001f;
            float y = Mathf.Round(value.y * 1000f) * 0.001f;
            return new Vector2(x, y);
        }

        private static List<FastVector3Int> CreateGridPerimeter(
            int minX,
            int maxX,
            int minY,
            int maxY
        )
        {
            List<FastVector3Int> perimeter = new((maxX - minX + 1) * 4);
            for (int x = minX; x <= maxX; ++x)
            {
                perimeter.Add(new FastVector3Int(x, minY, 0));
                perimeter.Add(new FastVector3Int(x, maxY, 0));
            }

            for (int y = minY + 1; y < maxY; ++y)
            {
                perimeter.Add(new FastVector3Int(minX, y, 0));
                perimeter.Add(new FastVector3Int(maxX, y, 0));
            }

            return perimeter;
        }
    }
}
