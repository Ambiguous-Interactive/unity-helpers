// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;

    /// <summary>
    /// Re-checks the assets an import touched, so the project's state stays current without anyone
    /// asking for a whole run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opt-in, and off until someone turns it on. A project-wide engine that starts working the
    /// moment the package is installed is one a consumer discovers through an editor that got
    /// slower, and the setting is per-user because whether the cost is worth paying is a fact about
    /// a workstation rather than about a repository.
    /// </para>
    /// <para>
    /// The import callback records GUIDs and nothing else. Every decision that needs the asset
    /// database happens in the deferred drain, because an <c>AssetPostprocessor</c> callback runs
    /// inside Unity's import phase, where loading an asset produces
    /// <c>SendMessage cannot be called...</c> and re-entrant imports -- and validating an asset is
    /// the loading-est thing this package does. See <c>asset-postprocessor-safety</c>.
    /// </para>
    /// <para>
    /// Coalescing is the deferral's, by delegate identity: <see cref="DrainAction"/> is a
    /// <c>static readonly</c> field, so a hundred imports in one refresh schedule one drain. The
    /// pending set is a <see cref="HashSet{T}"/> for the same reason one level down -- an asset
    /// imported twice before the drain is re-checked once.
    /// </para>
    /// </remarks>
    public static class ValidationAutoRun
    {
        /// <summary>
        /// The <c>EditorPrefs</c> key holding whether automatic re-checks are on.
        /// </summary>
        public const string EnabledPreferenceKey =
            "WallstopStudios.UnityHelpers.Validation.AutoRun";

        private static readonly Action DrainAction = Drain;
        private static readonly EditorApplication.CallbackFunction RetryAction = Retry;
        private static readonly HashSet<string> Pending = new HashSet<string>(
            StringComparer.Ordinal
        );

        private static readonly Dictionary<string, int> TriggerSources = new Dictionary<
            string,
            int
        >(StringComparer.Ordinal);

        private static bool _enabled = EditorPrefs.GetBool(EnabledPreferenceKey, false);
        private static bool _pruneDeleted;

        /// <summary>
        /// Whether an import re-checks the assets it touched. Persisted per user.
        /// </summary>
        /// <remarks>
        /// Turning it off drops anything already queued: the queue exists to answer "what changed
        /// since you last looked", and a run firing after the feature was switched off is exactly
        /// the surprise the setting exists to prevent.
        /// </remarks>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                EditorPrefs.SetBool(EnabledPreferenceKey, value);
                if (!value)
                {
                    Pending.Clear();
                    TriggerSources.Clear();
                    _pruneDeleted = false;
                }
            }
        }

        /// <summary>How many assets are queued for a re-check.</summary>
        public static int PendingCount => Pending.Count;

        /// <summary>
        /// Queues assets for a re-check and asks for one drain.
        /// </summary>
        /// <param name="assetGuids">The GUIDs to re-check; blanks and duplicates are dropped.</param>
        /// <param name="anyDeleted">Whether the batch deleted anything, so stale results are pruned.</param>
        /// <remarks>
        /// Internal rather than private so a fixture can queue without an import, which is the only
        /// way the coalescing is assertable: an <c>AssetPostprocessor</c> callback cannot be raised
        /// from a test without writing a file and waiting for Unity to import it.
        /// </remarks>
        internal static void Queue(
            IReadOnlyList<string> assetGuids,
            bool anyDeleted,
            int trigger = -1
        )
        {
            if (!_enabled)
            {
                return;
            }

            if (assetGuids != null)
            {
                for (int index = 0; index < assetGuids.Count; index++)
                {
                    string guid = assetGuids[index];
                    if (!string.IsNullOrEmpty(guid))
                    {
                        Pending.Add(guid);
                        int flags = TriggerSources.TryGetValue(guid, out int previous)
                            ? previous
                            : 0;
                        TriggerSources[guid] = trigger < 0 ? 4 : flags | (1 << trigger);
                    }
                }
            }

            _pruneDeleted |= anyDeleted;
            if (Pending.Count == 0 && !_pruneDeleted)
            {
                return;
            }

            AssetPostprocessorDeferral.Schedule(DrainAction);
        }

        private static void Retry()
        {
            AssetPostprocessorDeferral.Schedule(DrainAction);
        }

        private static void Drain()
        {
            if (!_enabled)
            {
                Pending.Clear();
                _pruneDeleted = false;
                return;
            }

            if (_pruneDeleted)
            {
                _pruneDeleted = false;
                Prune();
            }

            if (Pending.Count == 0)
            {
                return;
            }

            // Keep queued work until the active scan releases the scheduler.
            if (ValidationScheduler.IsRunning)
            {
                EditorApplication.delayCall += RetryAction;
                return;
            }

            List<ValidationTarget> targets = new List<ValidationTarget>(Pending.Count);
            foreach (string guid in Pending)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                if (TriggerSources.TryGetValue(guid, out int source) && (source & 4) == 0)
                {
                    int trigger = ValidationWorkspaceSettings.instance.TriggerFor(path);
                    if (trigger == 2 || (source & (1 << trigger)) == 0)
                        continue;
                }

                targets.Add(
                    new ValidationTarget(guid, path, AssetDatabase.GetMainAssetTypeAtPath(path))
                );
            }

            if (targets.Count == 0)
            {
                Pending.Clear();
                TriggerSources.Clear();
                return;
            }

            List<IValidationRule> rules = ValidationBatch.DiscoverRules(null);
            if (rules.Count == 0)
            {
                // Discard an uncheckable queue instead of retrying it on every import.
                Pending.Clear();
                TriggerSources.Clear();
                return;
            }

            ValidationRun run = new ValidationRun(
                rules,
                targets,
                ValidationObjectChangeProcessor.Load
            );
            if (
                ValidationScheduler.TryStart(
                    run,
                    ValidationWorkspaceSettings.instance.frameBudget,
                    CompleteRun
                )
            )
            {
                Pending.Clear();
                TriggerSources.Clear();
                return;
            }

            EditorApplication.delayCall += RetryAction;
        }

        private static void CompleteRun(ValidationRun run)
        {
            if (ValidationResults.TryMergeScopedRun(run))
            {
                return;
            }

            if (_enabled && run != null)
            {
                IReadOnlyList<ValidationTarget> targets = run.Targets;
                for (int index = 0; index < targets.Count; index++)
                {
                    Pending.Add(targets[index].AssetGuid);
                }
            }

            string reason =
                run == null ? "no run result"
                : run.IsCancelled ? "the run was cancelled"
                : !run.IsComplete ? "the run was incomplete"
                : run.Failures.Count == 0 ? "the run returned an invalid result"
                : run.Failures.Count + " rule or load failure(s)";
            string queueStatus = _enabled
                ? " The affected assets remain queued for the next import."
                : " Automatic validation is disabled, so the affected assets were not requeued.";
            UnityEngine.Debug.LogWarning(
                "[Asset Validation] Incremental validation retained previous results because "
                    + reason
                    + "."
                    + queueStatus
            );
        }

        internal static void CompleteRunForTesting(ValidationRun run)
        {
            CompleteRun(run);
        }

        internal static void ClearPendingForTesting()
        {
            Pending.Clear();
            TriggerSources.Clear();
        }

        private static void Prune()
        {
            List<string> gone = null;
            IReadOnlyList<string> recorded = ValidationResults.RecordedAssetGuids;
            for (int index = 0; index < recorded.Count; index++)
            {
                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(recorded[index])))
                {
                    gone ??= new List<string>();
                    gone.Add(recorded[index]);
                }
            }

            if (gone == null)
            {
                return;
            }

            _ = ValidationResults.ForgetAll(gone);
        }
    }
#endif
}
