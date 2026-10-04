// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>Restores shared editor state between package fixtures.</summary>
    public static class EditorStateTestUtilities
    {
        private const int FlushIterationCap = 32;

        /// <summary>Clears actual singleton creation state between fixtures.</summary>
        public static void ResetSingletonCreationState()
        {
            ScriptableObjectSingletonCreator._isEnsuring = false;
            ScriptableObjectSingletonCreator.CancelScheduledEnsureInvocation();
            ScriptableObjectSingletonCreator._retryAttempts = 0;
            ScriptableObjectSingletonCreator._consecutiveZeroProgressRetries = 0;
        }

        /// <summary>Drains actual deferred asset actions within a bounded setup budget.</summary>
        public static void FlushDeferredAssetActions()
        {
            if (AssetPostprocessorDeferral._draining)
            {
                Debug.LogWarning(
                    "Flush called reentrantly during drain — flush is a no-op; "
                        + "ensure tests don't call Flush from a handler callback."
                );
                return;
            }

            for (int iteration = 0; iteration < FlushIterationCap; ++iteration)
            {
                AssetPostprocessorDeferral._scheduled = false;
                AssetPostprocessorDeferral.DrainPending();
                if (AssetPostprocessorDeferral.PendingDrains.Count == 0)
                {
                    AssetPostprocessorDeferral._scheduled = false;
                    return;
                }
            }

            Debug.LogWarning(
                "Flush hit the iteration cap ("
                    + FlushIterationCap
                    + ") with "
                    + AssetPostprocessorDeferral.PendingDrains.Count
                    + " drain(s) still pending. A drain handler is likely re-scheduling itself. "
                    + "Remaining drains will fire on the next editor tick, which may pollute the next test."
            );
        }
    }
#endif
}
