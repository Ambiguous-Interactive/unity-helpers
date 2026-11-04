// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Keeps one-shot coroutine handles only while their callbacks are pending.</summary>
    public static class TrackedDelayExtensions
    {
        /// <summary>Schedules an action and tracks its pending handle in the supplied owner list.</summary>
        /// <remarks>Invalid inputs do nothing. Nonpositive and nonfinite delays resume after time advances.</remarks>
        public static void ExecuteTrackedAfterDelay(
            this MonoBehaviour owner,
            List<Coroutine> pending,
            Action action,
            float delay
        )
        {
            owner.TryExecuteTrackedAfterDelay(pending, action, delay);
        }

        /// <summary>Attempts to schedule an action and removes its handle before invoking the callback.</summary>
        /// <remarks>
        /// Returns false for a missing owner, inactive GameObject, missing list or missing action.
        /// Nonpositive and nonfinite delays resume after time advances. Call StopTrackedDelays from owner teardown;
        /// stopping coroutines through other APIs does not remove their handles from the list.
        /// </remarks>
        public static bool TryExecuteTrackedAfterDelay(
            this MonoBehaviour owner,
            List<Coroutine> pending,
            Action action,
            float delay
        )
        {
            if (
                owner == null
                || pending == null
                || action == null
                || !owner.gameObject.activeInHierarchy
            )
            {
                return false;
            }

            bool elapsed = false;
            Coroutine started = null;
            started = owner.ExecuteFunctionAfterDelay(
                () =>
                {
                    elapsed = true;
                    if (started != null)
                    {
                        pending.Remove(started);
                    }

                    action();
                },
                0f < delay && !float.IsInfinity(delay) ? delay : 0f
            );
            if (!elapsed && started != null)
            {
                pending.Add(started);
            }

            return elapsed || started != null;
        }

        /// <summary>Forgets and stops the handles currently pending, preserving work scheduled during cancellation.</summary>
        /// <remarks>A missing or destroyed owner still clears the list; this operation is safe to repeat.</remarks>
        public static void StopTrackedDelays(this MonoBehaviour owner, List<Coroutine> pending)
        {
            if (pending == null)
            {
                return;
            }

            int count = pending.Count;
            if (count == 0 || owner == null)
            {
                pending.Clear();
                return;
            }

            using PooledArray<Coroutine> lease = SystemArrayPool<Coroutine>.Get(
                count,
                out Coroutine[] snapshot
            );
            pending.CopyTo(snapshot);
            pending.Clear();
            for (int index = 0; index < count; ++index)
            {
                Coroutine handle = snapshot[index];
                if (handle != null && owner != null)
                {
                    owner.StopCoroutine(handle);
                }
            }
        }
    }
}
