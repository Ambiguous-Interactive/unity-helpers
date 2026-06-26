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
    /// Intervals are clamped to a minimum of 0.01 seconds to prevent zero‑division and busy loops,
    /// and <see cref="NextTickTime"/> is initialized using <see cref="PeriodicEffectDefinition.initialDelay"/>.
    /// </para>
    /// </remarks>
    internal sealed class PeriodicEffectRuntimeState
    {
        /// <summary>
        /// Indicates whether the runtime has executed the maximum allowed ticks for the definition.
        /// </summary>
        internal bool IsComplete => definition.maxTicks > 0 && ExecutedTicks >= definition.maxTicks;

        /// <summary>
        /// The absolute timestamp (in seconds) when the next tick should execute.
        /// </summary>
        internal float NextTickTime { get; private set; }

        /// <summary>
        /// The number of ticks that have successfully executed so far.
        /// </summary>
        internal int ExecutedTicks { get; private set; }

        internal readonly PeriodicEffectDefinition definition;
        internal readonly float interval;

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
            NextTickTime = startTime + Mathf.Max(0f, definition.initialDelay);
        }

        /// <summary>
        /// Attempts to advance the runtime to the next tick if the current time has passed the scheduled timestamp.
        /// </summary>
        /// <param name="currentTime">The current time (in seconds).</param>
        /// <returns><c>true</c> when a tick was consumed and <see cref="ExecutedTicks"/> incremented; otherwise, <c>false</c>.</returns>
        internal bool TryConsumeTick(float currentTime)
        {
            if (currentTime < NextTickTime || IsComplete)
            {
                return false;
            }

            ++ExecutedTicks;
            NextTickTime += interval;
            return true;
        }
    }
}
