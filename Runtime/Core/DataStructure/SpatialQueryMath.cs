// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>
    /// Grid arithmetic shared by <see cref="SpatialHash2D{T}"/> and <see cref="SpatialHash3D{T}"/>.
    /// Every helper is total: a non-finite or out-of-range input produces a clamped answer rather
    /// than a platform-defined float-to-int conversion or a loop bound that cannot terminate.
    /// </summary>
    internal static class SpatialQueryMath
    {
        /// <summary>
        /// The widest cell radius a query can need. The whole signed-int cell grid is this many
        /// cells across, so clamping here can never exclude an occupied cell.
        /// </summary>
        internal const long MaximumCellRadius = uint.MaxValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsFinite(Vector2 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        /// <summary>
        /// True when any component is NaN. Infinite components are allowed: they clamp onto the
        /// ends of the cell grid and describe a query volume that covers everything.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNaN(Vector2 value)
        {
            return float.IsNaN(value.x) || float.IsNaN(value.y);
        }

        /// <summary>
        /// True when any component is NaN. Infinite components are allowed: they clamp onto the
        /// ends of the cell grid and describe a query volume that covers everything.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNaN(Vector3 value)
        {
            return float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z);
        }

        /// <summary>
        /// True when a query box cannot describe a region at all: a NaN edge, or a negative size
        /// that leaves its max below its min. A zero-size box is valid and matches points on it.
        /// </summary>
        internal static bool IsInvalidQueryBounds(in Bounds bounds)
        {
            /*
                One local copy: Unity's Bounds is not a readonly struct and exposes no fields, so
                every property read through an `in` parameter would take its own defensive copy.
            */
            Bounds self = bounds;
            Vector3 minimum = self.min;
            Vector3 maximum = self.max;
            if (IsNaN(minimum) || IsNaN(maximum))
            {
                return true;
            }

            return maximum.x < minimum.x || maximum.y < minimum.y || maximum.z < minimum.z;
        }

        /// <summary>
        /// True when squaring a finite value saturates float. Above roughly 1.8446744e19 the
        /// square is positive infinity, and so is the squared distance to anything further out, so
        /// the <c>rangeSquared &lt; distanceSquared</c> filter every range query applies compares
        /// two infinities and admits an element it should reject.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool SquareSaturates(float value)
        {
            return float.IsFinite(value) && float.IsPositiveInfinity(value * value);
        }

        /// <summary>
        /// Squared distance between two points, in double. Double never saturates for float
        /// inputs, so this separates the pair that <see cref="SquareSaturates"/> reports on.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double DistanceSquared(Vector2 left, Vector2 right)
        {
            double deltaX = (double)left.x - right.x;
            double deltaY = (double)left.y - right.y;
            return (deltaX * deltaX) + (deltaY * deltaY);
        }

        /// <summary>
        /// Squared distance between two points, in double. The 3D half of
        /// <see cref="DistanceSquared(Vector2, Vector2)"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static double DistanceSquared(Vector3 left, Vector3 right)
        {
            double deltaX = (double)left.x - right.x;
            double deltaY = (double)left.y - right.y;
            double deltaZ = (double)left.z - right.z;
            return (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
        }

        /// <summary>
        /// Squared distance from a point to the nearest point of a box, in double, ignoring z.
        /// Zero when the point is inside the box.
        /// </summary>
        internal static double DistanceSquaredToBox2D(
            Vector3 minimum,
            Vector3 maximum,
            Vector2 point
        )
        {
            double deltaX = AxisDistance(minimum.x, maximum.x, point.x);
            double deltaY = AxisDistance(minimum.y, maximum.y, point.y);
            return (deltaX * deltaX) + (deltaY * deltaY);
        }

        /// <summary>
        /// Squared distance from a point to the nearest point of a box, in double. The 3D half of
        /// <see cref="DistanceSquaredToBox2D"/>.
        /// </summary>
        internal static double DistanceSquaredToBox(Vector3 minimum, Vector3 maximum, Vector3 point)
        {
            double deltaX = AxisDistance(minimum.x, maximum.x, point.x);
            double deltaY = AxisDistance(minimum.y, maximum.y, point.y);
            double deltaZ = AxisDistance(minimum.z, maximum.z, point.z);
            return (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double AxisDistance(float minimum, float maximum, float coordinate)
        {
            if (coordinate < minimum)
            {
                return (double)minimum - coordinate;
            }

            if (maximum < coordinate)
            {
                return (double)coordinate - maximum;
            }

            return 0d;
        }

        /// <summary>
        /// Maps a world coordinate onto its cell index, saturating at the ends of the int range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int ToCellCoordinate(float coordinate, float cellSize)
        {
            /*
                Mathf.FloorToInt of a ratio outside int range is an out-of-range conversion whose
                result is platform-defined, so the clamp has to happen in double before the cast.
            */
            double cell = Math.Floor((double)coordinate / cellSize);
            if (double.IsNaN(cell))
            {
                /*
                    Every comparison below is false for NaN, which would fall through to the very
                    cast this method exists to avoid.
                */
                return 0;
            }

            if (cell <= int.MinValue)
            {
                return int.MinValue;
            }

            if (int.MaxValue <= cell)
            {
                return int.MaxValue;
            }

            return (int)cell;
        }

        /// <summary>
        /// Converts a query radius into a cell radius, saturating at <see cref="MaximumCellRadius"/>.
        /// </summary>
        internal static long CellRadiusFor(float radius, float cellSize)
        {
            double cells = Math.Ceiling((double)radius / cellSize);
            if (double.IsNaN(cells) || cells <= 0d)
            {
                return 0L;
            }

            if (MaximumCellRadius <= cells)
            {
                return MaximumCellRadius;
            }

            return (long)cells;
        }

        /// <summary>
        /// Number of cells a query spans along one axis, given an inclusive cell radius.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long SpanForRadius(long cellRadius)
        {
            return (2L * cellRadius) + 1L;
        }

        /// <summary>
        /// Number of cells an inclusive cell range spans along one axis.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long SpanForRange(int minimumCell, int maximumCell)
        {
            return ((long)maximumCell - minimumCell) + 1L;
        }

        /// <summary>
        /// Answers whether walking every cell of a 2D query volume is cheaper than walking every
        /// occupied bucket. A dense walk costs the volume whatever the hash holds, so a huge radius
        /// has to take the bucket scan or the query never returns.
        /// </summary>
        internal static bool DenseScanIsCheaper(long spanX, long spanY, int occupiedCells)
        {
            return MultiplySaturating(spanX, spanY) <= occupiedCells;
        }

        /// <summary>
        /// Answers whether walking every cell of a 3D query volume is cheaper than walking every
        /// occupied bucket.
        /// </summary>
        internal static bool DenseScanIsCheaper(
            long spanX,
            long spanY,
            long spanZ,
            int occupiedCells
        )
        {
            return MultiplySaturating(MultiplySaturating(spanX, spanY), spanZ) <= occupiedCells;
        }

        private static long MultiplySaturating(long left, long right)
        {
            if (left <= 0L || right <= 0L)
            {
                return 0L;
            }

            if (long.MaxValue / left < right)
            {
                return long.MaxValue;
            }

            return left * right;
        }
    }
}
