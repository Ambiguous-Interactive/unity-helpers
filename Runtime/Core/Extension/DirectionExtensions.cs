// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Model;
    using UnityEngine;

    /// <summary>
    /// Extension methods for Direction enum, providing conversions to vectors, direction combining, and directional operations.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe as they operate on value types without shared mutable state.
    /// Performance: Most operations are O(1) constant time. Split operations are O(8) for iterating all directions.
    /// </remarks>
    public static class DirectionExtensions
    {
        private static readonly Direction[] Directions =
        {
            Direction.North,
            Direction.NorthEast,
            Direction.East,
            Direction.SouthEast,
            Direction.South,
            Direction.SouthWest,
            Direction.West,
            Direction.NorthWest,
        };

        /// <summary>
        /// Returns the opposite direction of the given direction.
        /// </summary>
        /// <param name="direction">The direction to get the opposite of.</param>
        /// <returns>The opposite direction (e.g., North returns South, NorthEast returns SouthWest).</returns>
        /// <remarks>
        /// <para>Null handling: Direction is a value type, cannot be null.</para>
        /// <para>Thread safety: Thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) - simple switch statement.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Direction.None returns Direction.None. Unknown direction values throw ArgumentException.</para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when direction is not a recognized Direction value.</exception>
        public static Direction Opposite(this Direction direction)
        {
            switch (direction)
            {
                case Direction.North:
                    return Direction.South;
                case Direction.NorthEast:
                    return Direction.SouthWest;
                case Direction.East:
                    return Direction.West;
                case Direction.SouthEast:
                    return Direction.NorthWest;
                case Direction.South:
                    return Direction.North;
                case Direction.SouthWest:
                    return Direction.NorthEast;
                case Direction.West:
                    return Direction.East;
                case Direction.NorthWest:
                    return Direction.SouthEast;
                case Direction.None:
                    return Direction.None;
                default:
                    throw new ArgumentException($"Unknown direction {direction}.");
            }
        }

        /// <summary>
        /// Converts a Direction to its corresponding Vector2Int representation.
        /// </summary>
        /// <param name="direction">The direction to convert.</param>
        /// <returns>A Vector2Int representing the direction (e.g., North = (0,1), East = (1,0)).</returns>
        /// <remarks>
        /// <para>Null handling: Direction is a value type, cannot be null.</para>
        /// <para>Thread safety: Thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) - simple switch statement.</para>
        /// <para>Allocations: Allocates a Vector2Int struct (stack allocation).</para>
        /// <para>Edge cases: Direction.None returns Vector2Int.zero. Diagonal directions return unit diagonals (not normalized). Unknown direction values throw ArgumentException.</para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when direction is not a recognized Direction value.</exception>
        public static Vector2Int AsVector2Int(this Direction direction)
        {
            switch (direction)
            {
                case Direction.None:
                    return Vector2Int.zero;
                case Direction.North:
                    return Vector2Int.up;
                case Direction.NorthEast:
                    return new Vector2Int(1, 1);
                case Direction.East:
                    return Vector2Int.right;
                case Direction.SouthEast:
                    return new Vector2Int(1, -1);
                case Direction.South:
                    return Vector2Int.down;
                case Direction.SouthWest:
                    return new Vector2Int(-1, -1);
                case Direction.West:
                    return Vector2Int.left;
                case Direction.NorthWest:
                    return new Vector2Int(-1, 1);
                default:
                    throw new ArgumentException($"Unknown direction {direction}.");
            }
        }

        /// <summary>
        /// Converts a Direction to its corresponding Vector2 representation.
        /// </summary>
        /// <param name="direction">The direction to convert.</param>
        /// <returns>A Vector2 representing the direction via implicit conversion from Vector2Int.</returns>
        /// <remarks>
        /// <para>Null handling: Direction is a value type, cannot be null.</para>
        /// <para>Thread safety: Thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) - delegates to AsVector2Int.</para>
        /// <para>Allocations: Allocates a Vector2 struct (stack allocation).</para>
        /// <para>Edge cases: Same behavior as AsVector2Int. Diagonal directions have magnitude ~1.41 (not normalized).</para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when direction is not a recognized Direction value.</exception>
        public static Vector2 AsVector2(this Direction direction)
        {
            return direction.AsVector2Int();
        }

        /// <summary>
        /// Converts a Vector3 to its closest Direction by treating it as a Vector2 (ignoring z-component).
        /// </summary>
        /// <param name="vector3">The vector to convert to a direction.</param>
        /// <returns>The closest cardinal or diagonal Direction based on the vector's angle.</returns>
        /// <remarks>
        /// <para>Null handling: Vector3 is a value type, cannot be null.</para>
        /// <para>Thread safety: Thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) - delegates to Vector2 overload which performs angle calculations.</para>
        /// <para>Allocations: Minimal stack allocations for Vector2 cast.</para>
        /// <para>Edge cases: Z-component is ignored. Zero vector returns Direction.None.</para>
        /// </remarks>
        public static Direction AsDirection(this Vector3 vector3)
        {
            return ((Vector2)vector3).AsDirection();
        }

        /// <summary>
        /// Splits a combined Direction flags value into individual Direction values.
        /// </summary>
        /// <param name="direction">The direction flags to split.</param>
        /// <returns>An enumerable of individual Direction values that are set in the flags.</returns>
        /// <remarks>
        /// <para>Null handling: Direction is a value type, cannot be null.</para>
        /// <para>Thread safety: Thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(8) - iterates through all 8 possible directions.</para>
        /// <para>Allocations: Allocates iterator state machine for yield return.</para>
        /// <para>Edge cases: If no flags are set, yields Direction.None. Multiple flags yield multiple directions.</para>
        /// </remarks>
        public static IEnumerable<Direction> Split(this Direction direction)
        {
            bool foundAny = false;
            foreach (Direction singleDirection in Directions)
            {
                if (direction.HasFlagNoAlloc(singleDirection))
                {
                    foundAny = true;
                    yield return singleDirection;
                }
            }

            if (!foundAny)
            {
                yield return Direction.None;
            }
        }

        /// <summary>
        /// Splits a combined Direction flags value into individual Direction values, storing them in a provided buffer.
        /// </summary>
        /// <param name="direction">The direction flags to split.</param>
        /// <param name="buffer">The list to clear and populate with individual directions.</param>
        /// <returns>The same buffer list passed in, now populated with individual Direction values.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if buffer is null.</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(8) - iterates through all 8 possible directions.</para>
        /// <para>Allocations: No allocations if buffer has sufficient capacity. May allocate if buffer needs to grow.</para>
        /// <para>Edge cases: If no flags are set, buffer contains only Direction.None. Buffer is cleared before populating.</para>
        /// </remarks>
        public static List<Direction> Split(this Direction direction, List<Direction> buffer)
        {
            buffer.Clear();
            foreach (Direction singleDirection in Directions)
            {
                if (direction.HasFlagNoAlloc(singleDirection))
                {
                    buffer.Add(singleDirection);
                }
            }

            if (buffer.Count == 0)
            {
                buffer.Add(Direction.None);
            }

            return buffer;
        }

        /// <summary>
        /// Combines multiple Direction values into a single Direction flags value using bitwise OR.
        /// </summary>
        /// <param name="directions">The enumerable of directions to combine.</param>
        /// <returns>A Direction value with all input direction flags set.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if directions is null.</para>
        /// <para>Thread safety: Thread-safe for read-only collections. Not thread-safe if collection is modified during enumeration. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of directions. Optimized for IReadOnlyList and HashSet.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Empty enumerable returns Direction.None. Duplicate directions have no additional effect due to bitwise OR.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when directions is null.</exception>
        public static Direction Combine(this IEnumerable<Direction> directions)
        {
            if (directions == null)
            {
                throw new ArgumentNullException(nameof(directions));
            }

            Direction combined = Direction.None;
            switch (directions)
            {
                case IReadOnlyList<Direction> list:
                {
                    for (int i = 0; i < list.Count; ++i)
                    {
                        combined |= list[i];
                    }

                    break;
                }
                case HashSet<Direction> set:
                {
                    foreach (Direction direction in set)
                    {
                        combined |= direction;
                    }

                    break;
                }
                default:
                {
                    foreach (Direction direction in directions.AsValueEnumerableRequired())
                    {
                        combined |= direction;
                    }

                    break;
                }
            }

            return combined;
        }

        /// <summary>
        /// Converts a Vector2 to its closest Direction based on angle.
        /// </summary>
        /// <param name="vector">The vector to convert to a direction.</param>
        /// <param name="preferAngles">If true, uses wider angle ranges (60 degrees) favoring diagonal directions. If false, uses equal ranges (45 degrees).</param>
        /// <returns>The closest Direction based on the vector's angle from north (up).</returns>
        /// <remarks>
        /// <para>Null handling: Vector2 is a value type, cannot be null.</para>
        /// <para>Thread safety: Thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) - calculates angle once using Atan2, then performs range checks.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Zero vector returns Direction.None. preferAngles=true uses 60-degree ranges for diagonals and 30-degree for cardinals. preferAngles=false uses equal 45-degree ranges.</para>
        /// </remarks>
        public static Direction AsDirection(this Vector2 vector, bool preferAngles = false)
        {
            if (vector == Vector2.zero)
            {
                return Direction.None;
            }

            float angle;
            if (vector.x < 0)
            {
                angle = 360 - Mathf.Atan2(vector.x, vector.y) * Mathf.Rad2Deg * -1;
            }
            else
            {
                angle = Mathf.Atan2(vector.x, vector.y) * Mathf.Rad2Deg;
            }

            if (preferAngles)
            {
                if (345 <= angle || angle < 15)
                {
                    return Direction.North;
                }

                if (angle is >= 15 and < 75)
                {
                    return Direction.NorthEast;
                }

                if (angle is >= 75 and < 105)
                {
                    return Direction.East;
                }

                if (angle is >= 105 and < 165)
                {
                    return Direction.SouthEast;
                }

                if (angle is >= 165 and < 195)
                {
                    return Direction.South;
                }

                if (angle is >= 195 and < 255)
                {
                    return Direction.SouthWest;
                }

                if (angle is >= 255 and < 285)
                {
                    return Direction.West;
                }

                if (angle is >= 285 and < 345)
                {
                    return Direction.NorthWest;
                }
            }

            if (337.5 <= angle || angle < 22.5)
            {
                return Direction.North;
            }

            if (22.5 <= angle && angle < 67.5)
            {
                return Direction.NorthEast;
            }

            if (67.5 <= angle && angle < 112.5)
            {
                return Direction.East;
            }

            if (112.5 <= angle && angle < 157.5)
            {
                return Direction.SouthEast;
            }

            if (157.5 <= angle && angle < 202.5)
            {
                return Direction.South;
            }

            if (202.5 <= angle && angle < 247.5)
            {
                return Direction.SouthWest;
            }

            if (247.5 <= angle && angle < 292.5)
            {
                return Direction.West;
            }

            if (292.5 <= angle && angle < 337.5)
            {
                return Direction.NorthWest;
            }

            return Direction.None;
        }
    }
}
