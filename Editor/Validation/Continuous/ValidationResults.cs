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
        private static readonly Dictionary<string, List<ValidationFinding>> ByAsset =
            new Dictionary<string, List<ValidationFinding>>(StringComparer.Ordinal);

        private static readonly List<string> AssetOrder = new List<string>();

        // Non-zero while a batch is being applied. Without it a scoped merge over 40 imported
        // assets raises 40 times, and every subscriber rebuilds its whole view 39 times for a
        // state nobody saw.
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
            for (int index = 0; index < AssetOrder.Count; index++)
            {
                if (ByAsset.TryGetValue(AssetOrder[index], out List<ValidationFinding> findings))
                {
                    all.AddRange(findings);
                }
            }

            return all;
        }

        /// <summary>
        /// Records the complete result of a whole run, discarding everything previously known.
        /// </summary>
        /// <param name="run">The finished run; <c>null</c> is ignored.</param>
        /// <remarks>
        /// Every asset the run CONSIDERED is recorded, not only the ones with findings, so a later
        /// incremental re-check of a clean asset has an entry to replace and the checked-asset
        /// count means what it says.
        /// </remarks>
        public static void RecordRun(ValidationRun run)
        {
            if (run == null)
            {
                return;
            }

            ByAsset.Clear();
            AssetOrder.Clear();
            HasRun = true;

            // Every asset the run CONSIDERED, not only the ones with findings, so a later
            // incremental re-check of a clean asset has an entry to replace and the checked count
            // means what it says.
            IReadOnlyList<ValidationTarget> targets = run.Targets;
            for (int index = 0; index < targets.Count; index++)
            {
                Entry(targets[index].AssetGuid);
            }

            IReadOnlyList<ValidationFinding> findings = run.Findings;
            for (int index = 0; index < findings.Count; index++)
            {
                Entry(findings[index].AssetGuid).Add(findings[index]);
            }

            Raise();
        }

        /// <summary>
        /// Folds a run over a subset of the project into the store, one asset at a time.
        /// </summary>
        /// <param name="run">The finished run; <c>null</c> or cancelled is ignored.</param>
        /// <remarks>
        /// <see cref="RecordRun"/> would be wrong here, because it replaces the whole store and a
        /// run over three imported assets knows nothing about the rest of the project. Every TARGET
        /// is replaced, findings or none, so an asset whose problem was just fixed loses its entry
        /// rather than keeping a finding nothing reproduces. A cancelled run is dropped entirely:
        /// its later targets were never looked at, and recording them as clean would be a claim it
        /// did not make.
        /// </remarks>
        public static void MergeScopedRun(ValidationRun run)
        {
            if (run == null || run.IsCancelled)
            {
                return;
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
