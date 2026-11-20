// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Animation
{
    using System;

    /// <summary>Samples the built-in curves for normalized animation progress.</summary>
    public static class MotionCurves
    {
        private const float BackOvershoot = 1.70158f;
        private const float BackInOutOvershoot = BackOvershoot * 1.525f;
        private const float BounceScale = 7.5625f;
        private const float BounceInterval = 2.75f;
        private const float ElasticPeriod = 0.3f;
        private const float ElasticInOutPeriod = 0.45f;
        private const float HalfPi = MathF.PI * 0.5f;
        private const float TwoPi = MathF.PI * 2f;

        /// <summary>Remaps a time fraction using the requested motion curve.</summary>
        /// <param name="curve">The curve to sample. Unrecognized values use linear interpolation.</param>
        /// <param name="progress">The time fraction, clamped to [0, 1]. NaN is treated as zero.</param>
        /// <returns>The remapped fraction. Back and elastic curves can go outside [0, 1].</returns>
        public static float Sample(MotionCurve curve, float progress)
        {
            if (float.IsNaN(progress) || progress <= 0f)
            {
                return 0f;
            }
            if (1f <= progress)
            {
                return 1f;
            }

            float remaining = 1f - progress;
            return curve switch
            {
                MotionCurve.QuadraticIn => progress * progress,
                MotionCurve.QuadraticOut => progress * (2f - progress),
                MotionCurve.QuadraticInOut => SampleInOut(MotionCurve.QuadraticIn, progress),
                MotionCurve.SineIn => 2f * SineSquared(HalfPi * 0.5f * progress),
                MotionCurve.SineOut => MathF.Sin(HalfPi * progress),
                MotionCurve.SineInOut => SineSquared(HalfPi * progress),
                MotionCurve.CubicIn => Cube(progress),
                MotionCurve.CubicOut => 1f - Cube(remaining),
                MotionCurve.CubicInOut => SampleInOut(MotionCurve.CubicIn, progress),
                MotionCurve.QuarticIn => FourthPower(progress),
                MotionCurve.QuarticOut => 1f - FourthPower(remaining),
                MotionCurve.QuarticInOut => SampleInOut(MotionCurve.QuarticIn, progress),
                MotionCurve.QuinticIn => progress * FourthPower(progress),
                MotionCurve.QuinticOut => 1f - remaining * FourthPower(remaining),
                MotionCurve.QuinticInOut => SampleInOut(MotionCurve.QuinticIn, progress),
                MotionCurve.ExponentialIn => ExponentialIn(progress),
                MotionCurve.ExponentialOut => 1f - ExponentialIn(remaining),
                MotionCurve.ExponentialInOut => SampleInOut(MotionCurve.ExponentialIn, progress),
                MotionCurve.CircularIn => CircularIn(progress),
                MotionCurve.CircularOut => MathF.Sqrt(progress * (2f - progress)),
                MotionCurve.CircularInOut => SampleInOut(MotionCurve.CircularIn, progress),
                MotionCurve.BackIn => BackIn(progress, BackOvershoot),
                MotionCurve.BackOut => 1f - BackIn(remaining, BackOvershoot),
                MotionCurve.BackInOut => SampleInOut(MotionCurve.BackIn, progress),
                MotionCurve.ElasticIn => ElasticIn(progress, ElasticPeriod),
                MotionCurve.ElasticOut => 1f - ElasticIn(remaining, ElasticPeriod),
                MotionCurve.ElasticInOut => SampleInOut(MotionCurve.ElasticIn, progress),
                MotionCurve.BounceIn => 1f - BounceOut(remaining),
                MotionCurve.BounceOut => BounceOut(progress),
                MotionCurve.BounceInOut => SampleInOut(MotionCurve.BounceIn, progress),
                _ => progress,
            };
        }

        internal static MotionCurve Normalize(MotionCurve curve)
        {
            // The defined values form a contiguous range after the unset sentinel.
            return curve < MotionCurve.Linear || MotionCurve.BounceInOut < curve
                ? MotionCurve.Linear
                : curve;
        }

        private static float SampleInOut(MotionCurve curve, float progress)
        {
            bool firstHalf = progress <= 0.5f;
            float halfProgress = 2f * (firstHalf ? progress : 1f - progress);
            float result = curve switch
            {
                MotionCurve.BackIn => BackIn(halfProgress, BackInOutOvershoot),
                MotionCurve.ElasticIn => ElasticIn(halfProgress, ElasticInOutPeriod),
                _ => Sample(curve, halfProgress),
            };
            return firstHalf ? result * 0.5f : 1f - result * 0.5f;
        }

        private static float SineSquared(float angle)
        {
            float sine = MathF.Sin(angle);
            return sine * sine;
        }

        private static float Cube(float value)
        {
            return value * value * value;
        }

        private static float FourthPower(float value)
        {
            float square = value * value;
            return square * square;
        }

        private static float ExponentialIn(float progress)
        {
            return MathF.Pow(2f, 10f * (progress - 1f));
        }

        private static float CircularIn(float progress)
        {
            float square = progress * progress;
            return square / (1f + MathF.Sqrt(1f - square));
        }

        private static float BackIn(float progress, float overshoot)
        {
            return progress * progress * ((overshoot + 1f) * progress - overshoot);
        }

        private static float ElasticIn(float progress, float period)
        {
            return ExponentialIn(progress) * MathF.Cos(TwoPi * (progress - 1f) / period);
        }

        private static float BounceOut(float progress)
        {
            if (progress < 1f / BounceInterval)
            {
                return BounceScale * progress * progress;
            }
            if (progress < 2f / BounceInterval)
            {
                float distance = progress - 1.5f / BounceInterval;
                return BounceScale * distance * distance + 0.75f;
            }
            if (progress < 2.5f / BounceInterval)
            {
                float distance = progress - 2.25f / BounceInterval;
                return BounceScale * distance * distance + 0.9375f;
            }

            float finalDistance = progress - 2.625f / BounceInterval;
            return BounceScale * finalDistance * finalDistance + 0.984375f;
        }
    }
}
