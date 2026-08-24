// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using UnityEngine;

    /// <summary>
    /// Math helpers for common geometric conversions and tests.
    /// </summary>
    public static partial class Helpers
    {
        /// <summary>
        /// Determines whether a point lies to the left of the ray from <paramref name="a"/> to <paramref name="b"/>.
        /// </summary>
        /// <remarks>
        /// Returns false when on or to the right of the ray.
        /// </remarks>
        public static bool IsLeft(Vector2 a, Vector2 b, Vector2 point)
        {
            // https://alienryderflex.com/point_left_of_ray/

            //check which side of line AB the point P is on
            float cross = (b.x - a.x) * (point.y - a.y) - (point.x - a.x) * (b.y - a.y);
            return 0f < cross;
        }

        /// <summary>
        /// Converts radians to a unit <see cref="Vector2"/>.
        /// </summary>
        public static Vector2 RadianToVector2(float radian)
        {
            return new Vector2(Mathf.Cos(radian), Mathf.Sin(radian));
        }

        /// <summary>
        /// Converts degrees to a unit <see cref="Vector2"/>.
        /// </summary>
        public static Vector2 DegreeToVector2(float degree)
        {
            return RadianToVector2(degree * Mathf.Deg2Rad);
        }
    }
}
