// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;

    /// <summary>
    /// Stateful pity-timer helper that increases success chance after consecutive failures.
    /// </summary>
    /// <remarks>
    /// Use this when player-facing randomness should avoid long dry streaks while still feeling random.
    /// The current chance starts at <see cref="BaseChance"/>, increases by
    /// <see cref="ChanceIncreasePerFailure"/> after each failure, and optionally forces success once
    /// <see cref="GuaranteedAfterFailures"/> failures have accumulated. Success resets the failure count.
    /// </remarks>
    public sealed class BadLuckProtection
    {
        /// <summary>
        /// Gets the chance used before any failures have accumulated.
        /// </summary>
        public float BaseChance { get; }

        /// <summary>
        /// Gets the chance added for each consecutive failure.
        /// </summary>
        public float ChanceIncreasePerFailure { get; }

        /// <summary>
        /// Gets the failure count that forces the next roll to succeed. Values less than or equal to zero disable the guarantee.
        /// </summary>
        public int GuaranteedAfterFailures { get; }

        /// <summary>
        /// Gets the number of consecutive failures since the last success or reset.
        /// </summary>
        public int FailuresSinceSuccess { get; private set; }

        /// <summary>
        /// Gets whether this helper has a hard guarantee configured.
        /// </summary>
        public bool HasGuarantee => 0 < GuaranteedAfterFailures;

        /// <summary>
        /// Gets the chance that will be used by the next roll.
        /// </summary>
        public float CurrentChance
        {
            get
            {
                if (HasGuarantee && GuaranteedAfterFailures <= FailuresSinceSuccess)
                {
                    return 1f;
                }

                double chance =
                    BaseChance
                    + (double)ChanceIncreasePerFailure * Math.Max(0, FailuresSinceSuccess);
                if (1d <= chance)
                {
                    return 1f;
                }

                return chance <= 0d ? 0f : (float)chance;
            }
        }

        private BadLuckProtection(
            float baseChance,
            float chanceIncreasePerFailure,
            int guaranteedAfterFailures
        )
        {
            BaseChance = baseChance;
            ChanceIncreasePerFailure = chanceIncreasePerFailure;
            GuaranteedAfterFailures = guaranteedAfterFailures;
        }

        /// <summary>
        /// Creates a validated bad-luck protection helper.
        /// </summary>
        /// <param name="baseChance">The initial success chance in the inclusive range [0, 1].</param>
        /// <param name="chanceIncreasePerFailure">The non-negative chance added after each failure.</param>
        /// <param name="guaranteedAfterFailures">The failure count that forces success. Zero disables the guarantee.</param>
        /// <param name="protection">The created helper when validation succeeds.</param>
        /// <returns>True when all parameters are finite and valid; otherwise false.</returns>
        public static bool TryCreate(
            float baseChance,
            float chanceIncreasePerFailure,
            int guaranteedAfterFailures,
            out BadLuckProtection protection
        )
        {
            return TryCreate(
                baseChance,
                chanceIncreasePerFailure,
                guaranteedAfterFailures,
                0,
                out protection
            );
        }

        /// <summary>
        /// Creates a validated bad-luck protection helper with restored failure state.
        /// </summary>
        /// <param name="baseChance">The initial success chance in the inclusive range [0, 1].</param>
        /// <param name="chanceIncreasePerFailure">The non-negative chance added after each failure.</param>
        /// <param name="guaranteedAfterFailures">The failure count that forces success. Zero disables the guarantee.</param>
        /// <param name="failuresSinceSuccess">The persisted consecutive failure count to restore.</param>
        /// <param name="protection">The created helper when validation succeeds.</param>
        /// <returns>True when all parameters are finite and valid; otherwise false.</returns>
        public static bool TryCreate(
            float baseChance,
            float chanceIncreasePerFailure,
            int guaranteedAfterFailures,
            int failuresSinceSuccess,
            out BadLuckProtection protection
        )
        {
            protection = null;
            if (
                !IsFinite(baseChance)
                || !IsFinite(chanceIncreasePerFailure)
                || baseChance < 0f
                || 1f < baseChance
                || chanceIncreasePerFailure < 0f
                || guaranteedAfterFailures < 0
                || failuresSinceSuccess < 0
            )
            {
                return false;
            }

            protection = new BadLuckProtection(
                baseChance,
                chanceIncreasePerFailure,
                guaranteedAfterFailures
            );
            protection.FailuresSinceSuccess = failuresSinceSuccess;
            return true;
        }

        /// <summary>
        /// Rolls against <see cref="CurrentChance"/> and updates the failure count.
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
        /// Clears accumulated failures without changing the configured probabilities.
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
