// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Shared deferral primitive for <see cref="AssetPostprocessor"/> callbacks.
    /// Routes work out of Unity's asset-import phase via <c>EditorApplication.delayCall</c>, so
    /// that reentering the <c>AssetDatabase</c> happens on an editor tick of its own rather than
    /// while Unity is still importing.
    /// </summary>
    /// <remarks>
    /// <b>Deferral is necessary and not sufficient, and this summary claimed otherwise for three
    /// sessions (#280).</b> It does not make <c>AssetDatabase.LoadAllAssetsAtPath</c> safe. Unity
    /// raises "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" around
    /// every <c>OnValidate</c> it runs, at any time -- and loading an asset deserializes it, which
    /// runs the consumer's <c>OnValidate</c> inside the drain, one tick later, exactly as it would
    /// have inside the callback. A question that asset metadata can answer must never be answered
    /// by a load.
    /// </remarks>
    internal static class AssetPostprocessorDeferral
    {
        internal static readonly List<Action> PendingDrains = new();
        internal static bool _scheduled;
        internal static bool _draining;
        private static int? _mainThreadId;

        /// <summary>
        /// Enqueues <paramref name="drain"/> to run one editor tick after the current
        /// asset-import phase completes. Invocations are deduplicated by delegate
        /// reference (using <see cref="object.ReferenceEquals"/>, not
        /// <see cref="Delegate.Equals(object)"/>): scheduling the same delegate
        /// reference multiple times before the drain fires coalesces into a single
        /// invocation. Structurally-equal-but-distinct delegates (for example,
        /// lambdas produced by a local function that captures only outer-method
        /// variables — the C# compiler lowers all such lambdas to the same Method
        /// and Target) are intentionally NOT deduplicated: callers cache their drain
        /// in a <c>static readonly</c> field so the dedup target is identity-based
        /// (see <c>.llm/skills/asset-postprocessor-safety.md</c>). If
        /// <see cref="UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks"/> is
        /// <see langword="false"/>, drains inline for users who require synchronous
        /// callback invocation.
        /// </summary>
        internal static void Schedule(Action drain)
        {
            if (drain == null)
            {
                return;
            }

            AssertOnMainThread();

            if (!ShouldDefer())
            {
                // The setting affects future calls; previously queued drains still run on their scheduled tick.
                RunSafely(drain);
                return;
            }

            // Deduplicate delegate identities; structural equality can merge separately created callbacks sharing captured state.
            bool alreadyPending = false;
            foreach (System.Action pendingDrainsElement in PendingDrains)
            {
                if (ReferenceEquals(pendingDrainsElement, drain))
                {
                    alreadyPending = true;
                    break;
                }
            }
            if (!alreadyPending)
            {
                PendingDrains.Add(drain);
            }

            if (_scheduled)
            {
                return;
            }

            _scheduled = true;
            EditorApplication.delayCall += DrainScheduled;
        }

        internal static void DrainScheduled()
        {
            _scheduled = false;
            DrainPending();
        }

        internal static void DrainPending()
        {
            if (PendingDrains.Count == 0)
            {
                return;
            }

            if (_draining)
            {
                return;
            }

            _draining = true;
            try
            {
                // Clear before invoking the snapshot so callbacks can schedule themselves for the next batch.
                Action[] drainsToRun = PendingDrains.ToArray();
                PendingDrains.Clear();

                foreach (System.Action drainsToRunElement in drainsToRun)
                {
                    RunSafely(drainsToRunElement);
                }

                if (0 < PendingDrains.Count && !_scheduled)
                {
                    _scheduled = true;
                    EditorApplication.delayCall += DrainScheduled;
                }
            }
            finally
            {
                _draining = false;
            }
        }

        internal static void ResetForDomainReload()
        {
            PendingDrains.Clear();
            _scheduled = false;
            _draining = false;
        }

        private static void RunSafely(Action drain)
        {
            try
            {
                drain();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.LogException(ex);
            }
        }

        private static bool ShouldDefer()
        {
            try
            {
                return UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Default to safe deferral while settings are unavailable during reload.
                Debug.LogException(ex);
                return true;
            }
        }

        [InitializeOnLoadMethod]
        private static void RegisterDomainCleanup()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            AssemblyReloadEvents.beforeAssemblyReload -= ResetForDomainReload;
            AssemblyReloadEvents.beforeAssemblyReload += ResetForDomainReload;
        }

        [System.Diagnostics.Conditional("UNITY_ASSERTIONS")]
        [System.Diagnostics.Conditional("DEBUG")]
        private static void AssertOnMainThread()
        {
            int? mainThreadId = _mainThreadId;
            if (mainThreadId == null)
            {
                // Main-thread identity is unavailable before initialization, so the assertion cannot measure this call yet.
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId.Value)
            {
                Debug.LogError(
                    "AssetPostprocessorDeferral.Schedule called from a background thread. "
                        + "Schedule must be invoked from the Unity main thread."
                );
            }
        }
    }
}
