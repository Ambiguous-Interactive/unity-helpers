// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using UnityEngine;

    /// <summary>
    /// Runtime tracking for a single <see cref="PeriodicEffectDefinition"/> instance.
    /// Maintains timing and execution counters for periodic ticks while an effect handle is active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each effect handle owns its own runtime states, so multiple handles can execute the same
    /// periodic definition independently (with unique timers and tick counts).
    /// </para>
    /// <para>
    /// Intervals are clamped to a minimum of 0.01 seconds. Finite timing retains the initial delay
    /// and cadence independently of absolute float clock precision. Refreshing the owning handle
    /// preserves the periodic phase; creating a new handle starts a new schedule.
    /// </para>
    /// </remarks>
    internal sealed class PeriodicEffectRuntimeState
    {
        private const long OrdinalLimbMask = (1L << 24) - 1L;
        private const double OrdinalMiddleScale = 1L << 24;
        private const double OrdinalHighScale = 1L << 48;

        /// <summary>
        /// Indicates whether the runtime has executed the maximum allowed ticks for the definition.
        /// </summary>
        internal bool IsComplete => 0 < definition.maxTicks && definition.maxTicks <= ExecutedTicks;

        /// <summary>
        /// The number of ticks that have successfully executed so far.
        /// </summary>
        internal int ExecutedTicks { get; set; }

        internal readonly PeriodicEffectDefinition definition;
        internal readonly float interval;

        internal long _scheduleOrdinal;

        private readonly double _origin;
        private readonly double _originRemainder;
        private readonly bool _usesNonfiniteTiming;
        private float _legacyNextTickTime;

        /// <summary>
        /// Creates runtime tracking for a periodic definition, clamping invalid authoring values.
        /// </summary>
        /// <param name="definition">The authoring data that describes cadence and modifications.</param>
        /// <param name="startTime">The current time (in seconds) to seed the next tick timestamp.</param>
        internal PeriodicEffectRuntimeState(PeriodicEffectDefinition definition, float startTime)
        {
            this.definition = definition;
            ExecutedTicks = 0;
            float clampedInterval = Mathf.Max(0.01f, definition.interval);
            interval = clampedInterval;
            float initialDelay = Mathf.Max(0f, definition.initialDelay);
            _legacyNextTickTime = (float)((double)startTime + initialDelay);
            _usesNonfiniteTiming =
                !float.IsFinite(startTime)
                || !float.IsFinite(initialDelay)
                || !float.IsFinite(interval);
            TwoSum(startTime, initialDelay, out _origin, out _originRemainder);
        }

        private static void TwoSum(double left, double right, out double sum, out double remainder)
        {
            double total = left + right;
            double roundedRight = total - left;
            double error = (left - (total - roundedRight)) + (right - roundedRight);
            sum = total;
            remainder = error;
        }

        /// <summary>
        /// Attempts to advance the runtime to the next tick if the current time has passed the scheduled timestamp.
        /// </summary>
        /// <param name="currentTime">The current time (in seconds).</param>
        /// <returns><c>true</c> when a tick was consumed and <see cref="ExecutedTicks"/> incremented; otherwise, <c>false</c>.</returns>
        internal bool TryConsumeTick(float currentTime)
        {
            if (IsComplete)
            {
                return false;
            }

            if (_usesNonfiniteTiming || !float.IsFinite(currentTime))
            {
                if (currentTime < _legacyNextTickTime)
                {
                    return false;
                }
            }
            else if (!IsTickDue(currentTime))
            {
                return false;
            }

            ++ExecutedTicks;
            ++_scheduleOrdinal;
            _legacyNextTickTime = (float)((double)_legacyNextTickTime + interval);
            return true;
        }

        private bool IsTickDue(float currentTime)
        {
            TwoSum(currentTime, -_origin, out double elapsed, out double elapsedError);
            TwoSum(
                elapsed,
                -_originRemainder,
                out double adjustedElapsed,
                out double adjustmentError
            );
            TwoSum(
                adjustedElapsed,
                elapsedError + adjustmentError,
                out double elapsedHigh,
                out double elapsedLow
            );
            GetCadenceOffset(out double scheduledHigh, out double scheduledLow);
            if (elapsedHigh < scheduledHigh)
            {
                return false;
            }
            if (scheduledHigh < elapsedHigh)
            {
                return true;
            }
            return scheduledLow <= elapsedLow;
        }

        private void GetCadenceOffset(out double offset, out double remainder)
        {
            double ordinalHigh = _scheduleOrdinal >> 48;
            double ordinalMiddle = (_scheduleOrdinal >> 24) & OrdinalLimbMask;
            double ordinalLow = _scheduleOrdinal & OrdinalLimbMask;
            double highProduct = interval * ordinalHigh * OrdinalHighScale;
            double middleProduct = interval * ordinalMiddle * OrdinalMiddleScale;
            double lowProduct = interval * ordinalLow;
            TwoSum(highProduct, middleProduct, out double combined, out double combinationError);
            TwoSum(combined, lowProduct, out double total, out double additionError);
            TwoSum(
                total,
                combinationError + additionError,
                out double normalizedOffset,
                out double normalizedRemainder
            );
            offset = normalizedOffset;
            remainder = normalizedRemainder;
        }
    }
}
