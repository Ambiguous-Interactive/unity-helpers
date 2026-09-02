// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What the project currently looks like, assembled from whole runs and from re-checks of the
    /// assets an import touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by asset GUID, and an asset's entry is REPLACED rather than appended to. That is what
    /// makes an incremental re-check correct: an asset whose findings were fixed has to lose them,
    /// and a store that only ever added would report every problem the project has ever had.
    /// Replacing with an empty list is therefore how "this asset is clean now" is recorded, and it
    /// is why <see cref="Replace"/> takes the asset rather than the findings as its subject -- a
    /// clean asset produces no findings to key on.
    /// </para>
    /// <para>
    /// Static and not serialized, so a domain reload empties it. That is deliberate: the findings
    /// describe assets as they were when some rule last looked, and a script compile is exactly the
    /// event most likely to change what the rules say. An empty store reads as "nothing has been
    /// checked", which <see cref="HasRun"/> distinguishes from "checked, and clean".
    /// </para>
    /// </remarks>
    public static class ValidationResults
    {
        private static Dictionary<string, List<ValidationFinding>> ByAsset = new Dictionary<
            string,
            List<ValidationFinding>
        >(StringComparer.Ordinal);

        private static readonly List<string> AssetOrder = new List<string>();

        /*
            Non-zero while a batch is being applied. Without it a scoped merge over 40 imported
            assets raises 40 times, and every subscriber rebuilds its whole view 39 times for a
            state nobody saw.
        */
        private static int _batchDepth;
        private static bool _batchChanged;

        /// <summary>
        /// Raised after any change, so a window can redraw without polling.
        /// </summary>
        /// <remarks>
        /// A subscriber that throws is caught and reported, because an exception escaping here
        /// would abandon the remaining subscribers and leave the store's own callers -- an asset
        /// postprocessor drain among them -- unwinding through Unity's import phase.
        /// </remarks>
        public static event Action Changed;

        /// <summary>Whether anything has been recorded since the last domain reload or clear.</summary>
        public static bool HasRun { get; private set; }

        /// <summary>How many assets have a recorded result, findings or not.</summary>
        public static int CheckedAssetCount => AssetOrder.Count;

        /// <summary>
        /// The assets that have a recorded result, in the order they were first recorded.
        /// </summary>
        public static IReadOnlyList<string> RecordedAssetGuids => AssetOrder;

        /// <summary>
        /// Every finding currently known, asset-major in the order assets were first recorded.
        /// </summary>
        /// <returns>A fresh list; never <c>null</c>.</returns>
        public static List<ValidationFinding> Snapshot()
        {
            List<ValidationFinding> all = new List<ValidationFinding>();
            CopyInto(all);
            return all;
        }

        /// <summary>
        /// Writes every finding currently known into a caller's list, clearing it first.
        /// </summary>
        /// <param name="destination">The list to fill; <c>null</c> is ignored.</param>
        /// <remarks>
        /// The allocation-free half of <see cref="Snapshot"/>, for a caller that refreshes on every
        /// keystroke and would otherwise copy every finding in the project into a new list each time.
        /// </remarks>
        public static void CopyInto(List<ValidationFinding> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            for (int index = 0; index < AssetOrder.Count; index++)
            {
                if (ByAsset.TryGetValue(AssetOrder[index], out List<ValidationFinding> findings))
                {
                    destination.AddRange(findings);
                }
            }
        }

        /// <summary>
        /// Records the complete result of a whole run, discarding everything previously known.
        /// </summary>
        /// <param name="run">
        /// The finished run. <c>null</c>, incomplete, cancelled, and failed runs are ignored.
        /// </param>
        /// <remarks>
        /// Every asset the run CONSIDERED is recorded, not only the ones with findings, so a later
        /// incremental re-check of a clean asset has an entry to replace and the checked-asset
        /// count means what it says.
        /// </remarks>
        public static void RecordRun(ValidationRun run)
        {
            _ = TryRecordRun(run);
        }

        /// <summary>
        /// Tries to record a complete successful whole-project run.
        /// </summary>
        /// <param name="run">The run to commit.</param>
        /// <returns><c>true</c> when the run replaced the current result snapshot.</returns>
        public static bool TryRecordRun(ValidationRun run)
        {
            if (!CanCommit(run))
            {
                return false;
            }

            Dictionary<string, List<ValidationFinding>> nextByAsset = new Dictionary<
                string,
                List<ValidationFinding>
            >(StringComparer.Ordinal);
            List<string> nextAssetOrder = new List<string>();

            /*
                Every asset the run CONSIDERED, not only the ones with findings, so a later
                incremental re-check of a clean asset has an entry to replace and the checked count
                means what it says.
            */
            IReadOnlyList<ValidationTarget> targets = run.Targets;
            for (int index = 0; index < targets.Count; index++)
            {
                string guid = targets[index].AssetGuid;
                if (!nextByAsset.ContainsKey(guid))
                {
                    nextByAsset.Add(guid, new List<ValidationFinding>());
                    nextAssetOrder.Add(guid);
                }
            }

            IReadOnlyList<ValidationFinding> findings = run.Findings;
            for (int index = 0; index < findings.Count; index++)
            {
                ValidationFinding finding = findings[index];
                if (!nextByAsset.TryGetValue(finding.AssetGuid, out List<ValidationFinding> entry))
                {
                    return false;
                }

                entry.Add(finding);
            }

            ByAsset = nextByAsset;
            AssetOrder.Clear();
            AssetOrder.AddRange(nextAssetOrder);
            HasRun = true;
            Raise();
            return true;
        }

        /// <summary>
        /// Folds a run over a subset of the project into the store, one asset at a time.
        /// </summary>
        /// <param name="run">
        /// The finished run. <c>null</c>, incomplete, cancelled, and failed runs are ignored.
        /// </param>
        /// <remarks>
        /// <see cref="RecordRun"/> would be wrong here, because it replaces the whole store and a
        /// run over three imported assets knows nothing about the rest of the project. Every TARGET
        /// is replaced, findings or none, so an asset whose problem was just fixed loses its entry
        /// rather than keeping a finding nothing reproduces. A cancelled run is dropped entirely:
        /// its later targets were never looked at, and recording them as clean would be a claim it
        /// did not make. Failed and incomplete runs are dropped for the same reason: replacing any
        /// target from a partial answer would make the store disagree with the last known complete
        /// result.
        /// </remarks>
        public static void MergeScopedRun(ValidationRun run)
        {
            _ = TryMergeScopedRun(run);
        }

        /// <summary>
        /// Tries to merge a complete successful incremental run into the current snapshot.
        /// </summary>
        /// <param name="run">The scoped run to commit.</param>
        /// <returns><c>true</c> when the run was accepted.</returns>
        public static bool TryMergeScopedRun(ValidationRun run)
        {
            if (!CanCommit(run))
            {
                return false;
            }

            Dictionary<string, List<ValidationFinding>> found = new Dictionary<
                string,
                List<ValidationFinding>
            >(StringComparer.Ordinal);
            IReadOnlyList<ValidationFinding> findings = run.Findings;
            for (int index = 0; index < findings.Count; index++)
            {
                ValidationFinding finding = findings[index];
                if (string.IsNullOrEmpty(finding.AssetGuid))
                {
                    continue;
                }

                if (!found.TryGetValue(finding.AssetGuid, out List<ValidationFinding> entry))
                {
                    entry = new List<ValidationFinding>();
                    found[finding.AssetGuid] = entry;
                }

                entry.Add(finding);
            }

            IReadOnlyList<ValidationTarget> targets = run.Targets;
            _batchDepth++;
            try
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    string guid = targets[index].AssetGuid;
                    Replace(
                        guid,
                        found.TryGetValue(guid, out List<ValidationFinding> entry) ? entry : null
                    );
                }
            }
            finally
            {
                _batchDepth--;
            }

            if (_batchChanged && _batchDepth == 0)
            {
                _batchChanged = false;
                Raise();
            }

            return true;
        }

        /// <summary>
        /// Replaces one asset's findings, adding the asset when it is new.
        /// </summary>
        /// <param name="assetGuid">The asset whose result this is.</param>
        /// <param name="findings">
        /// Its complete current findings. An empty or <c>null</c> list records the asset as clean,
        /// which is how a fixed problem disappears.
        /// </param>
        public static void Replace(string assetGuid, IReadOnlyList<ValidationFinding> findings)
        {
            if (string.IsNullOrEmpty(assetGuid))
            {
                return;
            }

            HasRun = true;
            List<ValidationFinding> entry = Entry(assetGuid);
            entry.Clear();
            if (findings != null)
            {
                for (int index = 0; index < findings.Count; index++)
                {
                    entry.Add(findings[index]);
                }
            }

            Raise();
        }

        /// <summary>
        /// Forgets one asset entirely, for a deleted asset rather than a fixed one.
        /// </summary>
        /// <param name="assetGuid">The asset to forget.</param>
        /// <returns><c>true</c> when the asset had a recorded result.</returns>
        public static bool Forget(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !ByAsset.Remove(assetGuid))
            {
                return false;
            }

            AssetOrder.Remove(assetGuid);
            Raise();
            return true;
        }

        /// <summary>
        /// Forgets several assets at once.
        /// </summary>
        /// <param name="assetGuids">The assets to forget; <c>null</c> and unknown entries are skipped.</param>
        /// <returns>How many assets had a recorded result.</returns>
        /// <remarks>
        /// One <see cref="Changed"/> for the whole set, and one pass over the order list. Calling
        /// <see cref="Forget(string)"/> in a loop instead is quadratic twice over: each removal is a
        /// linear scan of the order list AND raises, so every subscriber rebuilds its whole view
        /// once per deleted asset.
        /// </remarks>
        public static int ForgetAll(IReadOnlyList<string> assetGuids)
        {
            if (assetGuids == null || assetGuids.Count == 0)
            {
                return 0;
            }

            HashSet<string> removing = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < assetGuids.Count; index++)
            {
                string assetGuid = assetGuids[index];
                if (string.IsNullOrEmpty(assetGuid) || !ByAsset.Remove(assetGuid))
                {
                    continue;
                }

                removing.Add(assetGuid);
            }

            if (removing.Count == 0)
            {
                return 0;
            }

            int kept = 0;
            for (int index = 0; index < AssetOrder.Count; index++)
            {
                string assetGuid = AssetOrder[index];
                if (removing.Contains(assetGuid))
                {
                    continue;
                }

                AssetOrder[kept] = assetGuid;
                kept++;
            }

            AssetOrder.RemoveRange(kept, AssetOrder.Count - kept);
            Raise();
            return removing.Count;
        }

        /// <summary>Discards everything and returns to the never-run state.</summary>
        public static void Clear()
        {
            if (!HasRun && AssetOrder.Count == 0)
            {
                return;
            }

            ByAsset.Clear();
            AssetOrder.Clear();
            HasRun = false;
            Raise();
        }

        private static List<ValidationFinding> Entry(string assetGuid)
        {
            if (!ByAsset.TryGetValue(assetGuid, out List<ValidationFinding> findings))
            {
                findings = new List<ValidationFinding>();
                ByAsset[assetGuid] = findings;
                AssetOrder.Add(assetGuid);
            }

            return findings;
        }

        private static bool CanCommit(ValidationRun run)
        {
            if (run == null || !run.IsComplete || run.IsCancelled || run.Failures.Count != 0)
            {
                return false;
            }

            HashSet<string> targetGuids = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<ValidationTarget> targets = run.Targets;
            for (int index = 0; index < targets.Count; index++)
            {
                targetGuids.Add(targets[index].AssetGuid);
            }

            IReadOnlyList<ValidationFinding> findings = run.Findings;
            for (int index = 0; index < findings.Count; index++)
            {
                string assetGuid = findings[index].AssetGuid;
                if (string.IsNullOrEmpty(assetGuid) || !targetGuids.Contains(assetGuid))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Raise()
        {
            if (0 < _batchDepth)
            {
                _batchChanged = true;
                return;
            }

            Action changed = Changed;
            if (changed == null)
            {
                return;
            }

            foreach (Delegate handler in changed.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception thrown)
                {
                    UnityEngine.Debug.LogException(thrown);
                }
            }
        }
    }
#endif
}
