// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Animation
{
    using System;

    /// <summary>Built-in curves for remapping the normalized progress of an animation.</summary>
    public enum MotionCurve
    {
        /// <summary>Treats an uninitialized value as linear.</summary>
        [Obsolete("Use Linear to select the default curve explicitly.")]
        Unset = 0,

        /// <summary>Moves at a constant rate.</summary>
        Linear = 1,

        /// <summary>Decelerates quadratically.</summary>
        QuadraticOut = 2,

        /// <summary>Accelerates quadratically.</summary>
        QuadraticIn = 3,

        /// <summary>Accelerates and decelerates quadratically.</summary>
        QuadraticInOut = 4,

        /// <summary>Accelerates along a sine segment.</summary>
        SineIn = 5,

        /// <summary>Decelerates along a sine segment.</summary>
        SineOut = 6,

        /// <summary>Accelerates and decelerates along a cosine segment.</summary>
        SineInOut = 7,

        /// <summary>Accelerates cubically.</summary>
        CubicIn = 8,

        /// <summary>Decelerates cubically.</summary>
        CubicOut = 9,

        /// <summary>Accelerates and decelerates cubically.</summary>
        CubicInOut = 10,

        /// <summary>Accelerates with the fourth power.</summary>
        QuarticIn = 11,

        /// <summary>Decelerates with the fourth power.</summary>
        QuarticOut = 12,

        /// <summary>Accelerates and decelerates with the fourth power.</summary>
        QuarticInOut = 13,

        /// <summary>Accelerates with the fifth power.</summary>
        QuinticIn = 14,

        /// <summary>Decelerates with the fifth power.</summary>
        QuinticOut = 15,

        /// <summary>Accelerates and decelerates with the fifth power.</summary>
        QuinticInOut = 16,

        /// <summary>Accelerates exponentially.</summary>
        ExponentialIn = 17,

        /// <summary>Decelerates exponentially.</summary>
        ExponentialOut = 18,

        /// <summary>Accelerates and decelerates exponentially.</summary>
        ExponentialInOut = 19,

        /// <summary>Accelerates along a circular arc.</summary>
        CircularIn = 20,

        /// <summary>Decelerates along a circular arc.</summary>
        CircularOut = 21,

        /// <summary>Accelerates and decelerates along circular arcs.</summary>
        CircularInOut = 22,

        /// <summary>Moves backward before accelerating.</summary>
        BackIn = 23,

        /// <summary>Overshoots the target before settling.</summary>
        BackOut = 24,

        /// <summary>Moves backward before accelerating and overshoots before settling.</summary>
        BackInOut = 25,

        /// <summary>Oscillates with increasing amplitude before accelerating.</summary>
        ElasticIn = 26,

        /// <summary>Oscillates with decreasing amplitude before settling.</summary>
        ElasticOut = 27,

        /// <summary>Oscillates at the start and end with the steepest motion in the middle.</summary>
        ElasticInOut = 28,

        /// <summary>Bounces at the start before accelerating.</summary>
        BounceIn = 29,

        /// <summary>Bounces off the target before settling.</summary>
        BounceOut = 30,

        /// <summary>Bounces at the start and at the target.</summary>
        BounceInOut = 31,
    }
}
