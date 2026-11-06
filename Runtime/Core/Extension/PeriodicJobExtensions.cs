// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using Unity.Profiling;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Object = UnityEngine.Object;

    /// <summary>Runs named periodic work for the lifetime of a coroutine host.</summary>
    public static class PeriodicJobExtensions
    {
        /// <summary>Starts periodic work with a profiler marker and resilient failure reporting.</summary>
        /// <remarks>
        /// Uses scaled time and invokes at most once per frame, without catch-up ticks.
        /// Nonpositive and nonfinite intervals run once per frame. Inactive GameObjects return null.
        /// The optional context names the work and receives its first failure report; the host owns its lifetime.
        /// Each job allocates on startup; scheduling and waits reuse that storage. Callback allocations are separate.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The owner is missing or destroyed, or the tick is null.</exception>
        public static Coroutine StartPeriodicJob(
            this MonoBehaviour owner,
            Action tick,
            float updateRate,
            bool useJitter = false,
            bool waitBefore = false,
            Object context = null
        )
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (tick == null)
            {
                throw new ArgumentNullException(nameof(tick));
            }
            if (!owner.gameObject.activeInHierarchy)
            {
                return null;
            }

            Object reportingContext = context != null ? context : owner;
            ProfilerMarker marker = new(
                $"PeriodicJob.{reportingContext.GetType().Name}.{tick.Method.Name}"
            );
            return owner.StartFunctionAsCoroutine(
                () =>
                {
                    using (marker.Auto())
                    {
                        tick();
                    }
                },
                updateRate,
                useJitter,
                waitBefore,
                reportingContext
            );
        }
    }
}
