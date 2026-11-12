// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Math
{
    using System;
    using System.Runtime.CompilerServices;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Math = System.Math;

    /// <summary>A CSS cubic Bézier easing from (0, 0) to (1, 1).</summary>
    [Serializable]
    public struct CubicBezier : IEquatable<CubicBezier>
    {
        /// <summary>The largest supported animation-curve key count.</summary>
        public const int MaximumKeyCount = 4096;

        private const int MaximumNewtonSteps = 8;
        private const int MaximumBisectionSteps = 1100;

        /// <summary>The first control point's input coordinate.</summary>
        public float FirstControlX => _firstControlX;

        /// <summary>The first control point's output coordinate.</summary>
        public float FirstControlY => _firstControlY;

        /// <summary>The second control point's input coordinate.</summary>
        public float SecondControlX => _secondControlX;

        /// <summary>The second control point's output coordinate.</summary>
        public float SecondControlY => _secondControlY;

        /// <summary>Whether the controls are finite and both input coordinates lie in [0, 1].</summary>
        public bool IsValid =>
            float.IsFinite(_firstControlX)
            && 0f <= _firstControlX
            && _firstControlX <= 1f
            && float.IsFinite(_secondControlX)
            && 0f <= _secondControlX
            && _secondControlX <= 1f
            && float.IsFinite(_firstControlY)
            && float.IsFinite(_secondControlY);

        [SerializeField]
        private float _firstControlX;

        [SerializeField]
        private float _firstControlY;

        [SerializeField]
        private float _secondControlX;

        [SerializeField]
        private float _secondControlY;

        /// <summary>Stores controls; invalid controls are reported by IsValid without throwing.</summary>
        public CubicBezier(
            float firstControlX,
            float firstControlY,
            float secondControlX,
            float secondControlY
        )
        {
            _firstControlX = firstControlX;
            _firstControlY = firstControlY;
            _secondControlX = secondControlX;
            _secondControlY = secondControlY;
        }

        private static double Coordinate(double parameter, double first, double second)
        {
            double remaining = 1d - parameter;
            return 3d * remaining * remaining * parameter * first
                + 3d * remaining * parameter * parameter * second
                + parameter * parameter * parameter;
        }

        private static double Derivative(double parameter, double first, double second)
        {
            double remaining = 1d - parameter;
            return 3d
                * (
                    remaining * remaining * first
                    + 2d * remaining * parameter * (second - first)
                    + parameter * parameter * (1d - second)
                );
        }

        private static bool TryHandle(
            double run,
            double rise,
            double duration,
            out float tangent,
            out float weight
        )
        {
            if (run == 0d)
            {
                tangent = 0f;
                weight = 0f;
                return rise == 0d;
            }
            float slope = (float)(rise / run);
            float fraction = (float)(run / duration);
            if (
                !float.IsFinite(slope)
                || !float.IsFinite(fraction)
                || fraction < 0f
                || 1f < fraction
            )
            {
                tangent = 0f;
                weight = 0f;
                return false;
            }
            tangent = slope;
            weight = fraction;
            return true;
        }

        private static double SolveParameter(double x, double first, double second)
        {
            if (first == 0d && second == 0d)
            {
                return Math.Pow(x, 1d / 3d);
            }
            if (first == 1d && second == 0d && 0.25d <= x)
            {
                double offset = x - 0.5d;
                double distance = Math.Pow(Math.Abs(offset) * 0.25d, 1d / 3d);
                return 0.5d + (offset < 0d ? -distance : distance);
            }
            double lower = 0d;
            double upper = 1d;
            double parameter = x;
            for (int iteration = 0; iteration < MaximumNewtonSteps; ++iteration)
            {
                double observed = Coordinate(parameter, first, second);
                if (observed == x)
                {
                    return parameter;
                }
                if (observed < x)
                {
                    lower = parameter;
                }
                else
                {
                    upper = parameter;
                }
                double slope = Derivative(parameter, first, second);
                double next = parameter - (observed - x) / slope;
                parameter = lower < next && next < upper ? next : lower + (upper - lower) * 0.5d;
            }
            for (int iteration = 0; iteration < MaximumBisectionSteps; ++iteration)
            {
                double observed = Coordinate(parameter, first, second);
                if (observed == x)
                {
                    break;
                }
                if (observed < x)
                {
                    lower = parameter;
                }
                else
                {
                    upper = parameter;
                }
                double next = lower + (upper - lower) * 0.5d;
                if (parameter == next)
                {
                    break;
                }
                parameter = next;
            }
            return parameter;
        }

        /// <summary>Evaluates the easing without allocating; invalid controls or nonfinite input return NaN.</summary>
        /// <remarks>Finite inputs outside [0, 1] follow the CSS endpoint lines; output overshoot is preserved.</remarks>
        public double Evaluate(double x)
        {
            if (!IsValid || !double.IsFinite(x))
            {
                return double.NaN;
            }
            if (x < 0d)
            {
                double slope =
                    0f < _firstControlX ? (double)_firstControlY / _firstControlX
                    : 0f < _secondControlX ? (double)_secondControlY / _secondControlX
                    : 0d;
                return x * slope;
            }
            if (1d < x)
            {
                double slope =
                    _secondControlX < 1f ? (1d - _secondControlY) / (1d - _secondControlX)
                    : _firstControlX < 1f ? (1d - _firstControlY) / (1d - _firstControlX)
                    : 0d;
                return 1d + (x - 1d) * slope;
            }
            if (x == 0d || x == 1d)
            {
                return x;
            }
            if (_firstControlX == _firstControlY && _secondControlX == _secondControlY)
            {
                return x;
            }

            bool reflected = 0.5d < x;
            double target = reflected ? 1d - x : x;
            double first = reflected ? 1d - _secondControlX : _firstControlX;
            double second = reflected ? 1d - _firstControlX : _secondControlX;
            double parameter = SolveParameter(target, first, second);
            if (reflected)
            {
                parameter = 1d - parameter;
            }
            return Coordinate(parameter, _firstControlY, _secondControlY);
        }

        /// <summary>Builds a weighted curve, or returns null when conversion is not representable.</summary>
        /// <remarks>Uses 2 through MaximumKeyCount keys at uniform Bézier parameter intervals. Vertical handles and float overflow or collapsed key times are refused. The returned curve clamps outside [0, 1].</remarks>
        public AnimationCurve ToAnimationCurve(int keyCount = 2)
        {
            return TryToAnimationCurve(keyCount, out AnimationCurve curve) ? curve : null;
        }

        /// <summary>Builds a weighted curve when all controls, keys and tangents are representable.</summary>
        public bool TryToAnimationCurve(int keyCount, out AnimationCurve curve)
        {
            if (!IsValid || keyCount < 2 || MaximumKeyCount < keyCount)
            {
                curve = null;
                return false;
            }
            Keyframe[] keys = new Keyframe[keyCount];
            double step = 1d / (keyCount - 1);
            for (int index = 0; index < keyCount; ++index)
            {
                double parameter = index == keyCount - 1 ? 1d : index * step;
                float time = (float)Coordinate(parameter, _firstControlX, _secondControlX);
                float value = (float)Coordinate(parameter, _firstControlY, _secondControlY);
                if (!float.IsFinite(value) || (0 < index && time <= keys[index - 1].time))
                {
                    curve = null;
                    return false;
                }
                keys[index] = new Keyframe(time, value) { weightedMode = WeightedMode.Both };
            }
            for (int index = 0; index < keyCount - 1; ++index)
            {
                double start = index * step;
                double end = index == keyCount - 2 ? 1d : (index + 1) * step;
                double duration =
                    Coordinate(end, _firstControlX, _secondControlX)
                    - Coordinate(start, _firstControlX, _secondControlX);
                double handleScale = (end - start) / 3d;
                if (
                    !TryHandle(
                        Derivative(start, _firstControlX, _secondControlX) * handleScale,
                        Derivative(start, _firstControlY, _secondControlY) * handleScale,
                        duration,
                        out float outTangent,
                        out float outWeight
                    )
                    || !TryHandle(
                        Derivative(end, _firstControlX, _secondControlX) * handleScale,
                        Derivative(end, _firstControlY, _secondControlY) * handleScale,
                        duration,
                        out float inTangent,
                        out float inWeight
                    )
                )
                {
                    curve = null;
                    return false;
                }
                keys[index].outTangent = outTangent;
                keys[index].outWeight = outWeight;
                keys[index + 1].inTangent = inTangent;
                keys[index + 1].inWeight = inWeight;
            }
            keys[0].weightedMode = WeightedMode.Out;
            keys[keyCount - 1].weightedMode = WeightedMode.In;
            curve = new AnimationCurve(keys)
            {
                preWrapMode = WrapMode.ClampForever,
                postWrapMode = WrapMode.ClampForever,
            };
            return true;
        }

        /// <summary>Compares all four control coordinates.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CubicBezier other)
        {
            return _firstControlX.Equals(other._firstControlX)
                && _firstControlY.Equals(other._firstControlY)
                && _secondControlX.Equals(other._secondControlX)
                && _secondControlY.Equals(other._secondControlY);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is CubicBezier other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(
                _firstControlX,
                _firstControlY,
                _secondControlX,
                _secondControlY
            );
        }
    }
}
