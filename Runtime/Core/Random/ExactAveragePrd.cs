// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
// cspell:ignore PRD Prd prd

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;

    /// <summary>
    /// Stateful pseudo-random distribution that preserves an exact long-run average chance.
    /// </summary>
    /// <remarks>
    /// This implements the common game-design PRD pattern used to reduce visible streaks while keeping
    /// the configured long-run chance. Each failure raises the next attempt chance by a solved coefficient;
    /// success resets the attempt state. The coefficient is solved so the expected attempts per success are
    /// <c>1 / TargetChance</c>.
    /// </remarks>
    public sealed class ExactAveragePrd
    {
        private const int SolverIterations = 48;
        private const double SurvivalEpsilon = 1e-14d;
        private const int ExpectedAttemptSafetyLimit = 10_000_000;

        /// <summary>
        /// The smallest non-zero target chance supported by the bounded coefficient solver.
        /// </summary>
        /// <remarks>
        /// Lower rates are better modeled with <see cref="BadLuckProtection"/> or
        /// <see cref="WeightedShuffleBag{T}"/> so construction remains predictable in gameplay code.
        /// </remarks>
        public const float MinimumPositiveTargetChance = 0.0001f;

        /// <summary>
        /// Gets the configured long-run success chance.
        /// </summary>
        public float TargetChance { get; }

        /// <summary>
        /// Gets the solved chance increment applied per attempt.
        /// </summary>
        public float Coefficient { get; }

        /// <summary>
        /// Gets the attempt number whose chance reaches one, or <see cref="int.MaxValue"/> when no guarantee is practical.
        /// </summary>
        public int GuaranteedAttempt { get; }

        /// <summary>
        /// Gets the number of consecutive failures since the last success or reset.
        /// </summary>
        public int FailuresSinceSuccess { get; private set; }

        /// <summary>
        /// Gets the one-based attempt number that will be used by the next roll.
        /// </summary>
        public int NextAttempt =>
            FailuresSinceSuccess == int.MaxValue ? int.MaxValue : FailuresSinceSuccess + 1;

        /// <summary>
        /// Gets the success chance that will be used by the next roll.
        /// </summary>
        public float CurrentChance => GetChanceForAttempt(NextAttempt);

        private ExactAveragePrd(float targetChance, float coefficient, int guaranteedAttempt)
        {
            TargetChance = targetChance;
            Coefficient = coefficient;
            GuaranteedAttempt = guaranteedAttempt;
        }

        /// <summary>
        /// Creates a validated exact-average PRD helper.
        /// </summary>
        /// <param name="targetChance">The long-run success chance in the inclusive range [0, 1].</param>
        /// <param name="prd">The created helper when validation succeeds.</param>
        /// <returns>True when <paramref name="targetChance"/> is finite and valid; otherwise false.</returns>
        public static bool TryCreate(float targetChance, out ExactAveragePrd prd)
        {
            return TryCreate(targetChance, 0, out prd);
        }

        /// <summary>
        /// Creates a validated exact-average PRD helper with restored failure state.
        /// </summary>
        /// <param name="targetChance">The long-run success chance in the inclusive range [0, 1].</param>
        /// <param name="failuresSinceSuccess">The persisted consecutive failure count to restore.</param>
        /// <param name="prd">The created helper when validation succeeds.</param>
        /// <returns>True when all parameters are finite and valid; otherwise false.</returns>
        public static bool TryCreate(
            float targetChance,
            int failuresSinceSuccess,
            out ExactAveragePrd prd
        )
        {
            prd = null;
            if (
                !IsFinite(targetChance)
                || targetChance < 0f
                || 1f < targetChance
                || failuresSinceSuccess < 0
                || (0f < targetChance && targetChance < MinimumPositiveTargetChance)
            )
            {
                return false;
            }

            if (targetChance <= 0f)
            {
                prd = new ExactAveragePrd(0f, 0f, int.MaxValue);
                prd.FailuresSinceSuccess = failuresSinceSuccess;
                return true;
            }

            if (1f <= targetChance)
            {
                prd = new ExactAveragePrd(1f, 1f, 1);
                prd.FailuresSinceSuccess = failuresSinceSuccess;
                return true;
            }

            float coefficient = SolveCoefficient(targetChance);
            int guaranteedAttempt = ResolveGuaranteedAttempt(coefficient);
            prd = new ExactAveragePrd(targetChance, coefficient, guaranteedAttempt);
            prd.FailuresSinceSuccess = failuresSinceSuccess;
            return true;
        }

        /// <summary>
        /// Gets the success chance for a one-based attempt number.
        /// </summary>
        /// <param name="attempt">The one-based attempt number.</param>
        /// <returns>The chance for that attempt, clamped to [0, 1].</returns>
        public float GetChanceForAttempt(int attempt)
        {
            if (attempt <= 0 || Coefficient <= 0f)
            {
                return 0f;
            }

            double chance = (double)Coefficient * attempt;
            if (1d <= chance)
            {
                return 1f;
            }

            return chance <= 0d ? 0f : (float)chance;
        }

        /// <summary>
        /// Rolls against <see cref="CurrentChance"/> and updates the PRD state.
        /// </summary>
        /// <param name="random">The random generator to use. When null, <see cref="PRNG.Instance"/> is used.</param>
        /// <returns>True when the roll succeeds; otherwise false.</returns>
        public bool Roll(IRandom random = null)
        {
            float chance = CurrentChance;
            if (1f <= chance)
            {
                Reset();
                return true;
            }

            if (0f < chance)
            {
                IRandom generator = random ?? PRNG.Instance;
                if (generator.NextFloat() < chance)
                {
                    Reset();
                    return true;
                }
            }

            IncrementFailures();
            return false;
        }

        /// <summary>
        /// Clears accumulated failures without changing the solved distribution.
        /// </summary>
        public void Reset()
        {
            FailuresSinceSuccess = 0;
        }

        /// <summary>
        /// Restores accumulated failures from persisted state.
        /// </summary>
        /// <param name="failuresSinceSuccess">The non-negative consecutive failure count to restore.</param>
        /// <returns>True when the state was restored; otherwise false.</returns>
        public bool TrySetFailuresSinceSuccess(int failuresSinceSuccess)
        {
            if (failuresSinceSuccess < 0)
            {
                return false;
            }

            FailuresSinceSuccess = failuresSinceSuccess;
            return true;
        }

        private static float SolveCoefficient(float targetChance)
        {
            double low = 0d;
            double high = targetChance;
            double targetExpectedAttempts = 1d / targetChance;

            for (int i = 0; i < SolverIterations; ++i)
            {
                double candidate = (low + high) * 0.5d;
                double expectedAttempts = EstimateExpectedAttempts(candidate);
                if (expectedAttempts > targetExpectedAttempts)
                {
                    low = candidate;
                }
                else
                {
                    high = candidate;
                }
            }

            return (float)((low + high) * 0.5d);
        }

        private static double EstimateExpectedAttempts(double coefficient)
        {
            if (coefficient <= 0d)
            {
                return double.PositiveInfinity;
            }

            double expectedAttempts = 0d;
            double survival = 1d;
            for (int attempt = 1; attempt <= ExpectedAttemptSafetyLimit; ++attempt)
            {
                expectedAttempts += survival;
                double chance = coefficient * attempt;
                if (1d <= chance)
                {
                    return expectedAttempts;
                }

                survival *= 1d - chance;
                if (survival <= SurvivalEpsilon)
                {
                    return expectedAttempts;
                }
            }

            return expectedAttempts;
        }

        private static int ResolveGuaranteedAttempt(float coefficient)
        {
            if (coefficient <= 0f)
            {
                return int.MaxValue;
            }

            double attempt = Math.Ceiling(1d / coefficient);
            if (int.MaxValue <= attempt)
            {
                return int.MaxValue;
            }

            return Math.Max(1, (int)attempt);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void IncrementFailures()
        {
            if (FailuresSinceSuccess < int.MaxValue)
            {
                ++FailuresSinceSuccess;
            }
        }
    }
}
