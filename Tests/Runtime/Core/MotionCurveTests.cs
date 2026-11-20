// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Animation;

    [TestFixture]
    public sealed class MotionCurveTests
    {
        [TestCase(MotionCurve.Linear, 0.25f)]
        [TestCase(MotionCurve.QuadraticIn, 0.0625f)]
        [TestCase(MotionCurve.QuadraticOut, 0.4375f)]
        [TestCase(MotionCurve.QuadraticInOut, 0.125f)]
        [TestCase(MotionCurve.SineIn, 0.076120467f)]
        [TestCase(MotionCurve.SineOut, 0.382683432f)]
        [TestCase(MotionCurve.SineInOut, 0.146446609f)]
        [TestCase(MotionCurve.CubicIn, 0.015625f)]
        [TestCase(MotionCurve.CubicOut, 0.578125f)]
        [TestCase(MotionCurve.CubicInOut, 0.0625f)]
        [TestCase(MotionCurve.QuarticIn, 0.00390625f)]
        [TestCase(MotionCurve.QuarticOut, 0.68359375f)]
        [TestCase(MotionCurve.QuarticInOut, 0.03125f)]
        [TestCase(MotionCurve.QuinticIn, 0.0009765625f)]
        [TestCase(MotionCurve.QuinticOut, 0.7626953125f)]
        [TestCase(MotionCurve.QuinticInOut, 0.015625f)]
        [TestCase(MotionCurve.ExponentialIn, 0.005524272f)]
        [TestCase(MotionCurve.ExponentialOut, 0.823223305f)]
        [TestCase(MotionCurve.ExponentialInOut, 0.015625f)]
        [TestCase(MotionCurve.CircularIn, 0.031754163f)]
        [TestCase(MotionCurve.CircularOut, 0.661437828f)]
        [TestCase(MotionCurve.CircularInOut, 0.066987298f)]
        [TestCase(MotionCurve.BackIn, -0.064136563f)]
        [TestCase(MotionCurve.BackOut, 0.817409687f)]
        [TestCase(MotionCurve.BackInOut, -0.099681844f)]
        [TestCase(MotionCurve.ElasticIn, -0.005524272f)]
        [TestCase(MotionCurve.ElasticOut, 0.911611652f)]
        [TestCase(MotionCurve.ElasticInOut, 0.011969444f)]
        [TestCase(MotionCurve.BounceIn, 0.02734375f)]
        [TestCase(MotionCurve.BounceOut, 0.47265625f)]
        [TestCase(MotionCurve.BounceInOut, 0.1171875f)]
        public void SamplesKnownFractionAndExactBoundaries(MotionCurve curve, float expected)
        {
            Assert.That(MotionCurves.Sample(curve, 0f), Is.Zero);
            Assert.That(MotionCurves.Sample(curve, 0.25f), Is.EqualTo(expected).Within(0.000001f));
            Assert.That(MotionCurves.Sample(curve, 1f), Is.EqualTo(1f));
        }

        [TestCase(MotionCurve.QuadraticInOut)]
        [TestCase(MotionCurve.SineInOut)]
        [TestCase(MotionCurve.CubicInOut)]
        [TestCase(MotionCurve.QuarticInOut)]
        [TestCase(MotionCurve.QuinticInOut)]
        [TestCase(MotionCurve.ExponentialInOut)]
        [TestCase(MotionCurve.CircularInOut)]
        [TestCase(MotionCurve.BackInOut)]
        [TestCase(MotionCurve.ElasticInOut)]
        [TestCase(MotionCurve.BounceInOut)]
        public void InOutCurvesMirrorAroundTheMidpoint(MotionCurve curve)
        {
            Assert.That(MotionCurves.Sample(curve, 0.5f), Is.EqualTo(0.5f).Within(0.000001f));
            float early = MotionCurves.Sample(curve, 0.25f);
            Assert.That(
                MotionCurves.Sample(curve, 0.75f),
                Is.EqualTo(1f - early).Within(0.000001f)
            );
        }

        [TestCase(MotionCurve.BackOut, 0.75f)]
        [TestCase(MotionCurve.ElasticOut, 0.5f)]
        public void PreservesBuiltInOvershoot(MotionCurve curve, float progress)
        {
            Assert.That(MotionCurves.Sample(curve, progress), Is.GreaterThan(1f));
        }

        [TestCase(MotionCurve.SineIn, 1.23370055e-10f)]
        [TestCase(MotionCurve.SineInOut, 2.46740110e-10f)]
        [TestCase(MotionCurve.CircularIn, 5e-11f)]
        public void SmallPositiveFractionsRemainRepresentable(MotionCurve curve, float expected)
        {
            Assert.That(MotionCurves.Sample(curve, 0.00001f), Is.EqualTo(expected).Within(1e-15f));
        }

        [TestCase(4f / 11f, 1f)]
        [TestCase(6f / 11f, 0.75f)]
        [TestCase(8f / 11f, 1f)]
        [TestCase(9f / 11f, 0.9375f)]
        [TestCase(10f / 11f, 1f)]
        [TestCase(21f / 22f, 0.984375f)]
        public void BounceOutHitsTheTargetBetweenRebounds(float progress, float expected)
        {
            Assert.That(
                MotionCurves.Sample(MotionCurve.BounceOut, progress),
                Is.EqualTo(expected).Within(0.000001f)
            );
        }

        [TestCase(float.NaN, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        [TestCase(-0.1f, 0f)]
        [TestCase(1.1f, 1f)]
        [TestCase(float.PositiveInfinity, 1f)]
        public void BoundsTimeFraction(float progress, float expected)
        {
            Assert.That(
                MotionCurves.Sample(MotionCurve.ElasticOut, progress),
                Is.EqualTo(expected)
            );
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void UnrecognizedValuesUseLinearInterpolation(int rawCurve)
        {
            Assert.That(MotionCurves.Sample((MotionCurve)rawCurve, 0.25f), Is.EqualTo(0.25f));
        }
    }
}
