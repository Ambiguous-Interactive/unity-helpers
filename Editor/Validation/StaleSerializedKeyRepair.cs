// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Drops serialized keys no field claims, one asset at a time, and undoes any rewrite that
    /// loses content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no "delete this key" API. The lever is
    /// <see cref="AssetDatabase.ForceReserializeAssets(IEnumerable{string}, ForceReserializeAssetsOptions)"/>,
    /// which rewrites an asset from what it loaded: every dead key dropped, every new field written
    /// at its default, every <c>FormerlySerializedAs</c> alias migrated to its current name.
    /// </para>
    /// <para>
    /// It is not safe unsupervised, which is why this wrapper is the deliverable rather than the
    /// one-liner. An asset whose content lives in sub-objects can come back with them gone -- a
    /// render profile measured going from twenty serialized documents to one, losing every volume
    /// component, while the rewrite reported success. So each asset is rewritten alone, its
    /// non-null object count compared before and after, and any rewrite that lowers the count is
    /// undone by writing the original bytes back.
    /// </para>
    /// <para>
    /// Restoring the file is only half of an undo: the editor still holds the damaged object, and
    /// the next save writes it straight back out. The other half is a forced synchronous re-import,
    /// which this does. The count comes from
    /// <see cref="AssetDatabase.LoadAllAssetsAtPath"/> rather than from counting documents in the
    /// text, because a profile's components are hidden sub-objects a text reader can disagree about.
    /// </para>
    /// <para>
    /// A bad subject list is the likely failure -- the asset that broke in the measurement above was
    /// in the set only because a crude line matcher hit a live key and read it as a retired one --
    /// so this has to survive one, and refusals are reported rather than swallowed.
    /// </para>
    /// </remarks>
    public static class StaleSerializedKeyRepair
    {
        /// <summary>
        /// Rewrites each of <paramref name="assetPaths"/> alone, undoing any that loses content.
        /// </summary>
        /// <param name="assetPaths">The assets to rewrite.</param>
        /// <param name="outcomes">Receives what happened to each asset, keyed by path.</param>
        /// <returns><c>false</c> when the repair could not run at all.</returns>
        public static bool TryRepair(
            IReadOnlyList<string> assetPaths,
            Dictionary<string, StaleSerializedKeyRepairOutcome> outcomes
        )
        {
            if (assetPaths == null || outcomes == null)
            {
                return false;
            }

            outcomes.Clear();
            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                outcomes[assetPath] = RepairAsset(assetPath);
            }

            return true;
        }

        /// <summary>
        /// Rewrites one asset, undoing the rewrite when it comes back with less than it went in with.
        /// </summary>
        /// <param name="assetPath">The asset to rewrite.</param>
        /// <returns>What happened.</returns>
        public static StaleSerializedKeyRepairOutcome RepairAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            byte[] original;
            try
            {
                if (!File.Exists(assetPath))
                {
                    return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
                }

                original = File.ReadAllBytes(assetPath);
            }
            catch (Exception)
            {
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            int before = LoadedObjectCount(assetPath);
            if (before <= 0)
            {
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            try
            {
                /*
                    Metadata rather than assets-only, because with assets-only a prefab is silently
                    not rewritten at all -- measured on ten of them, which read as "these had no
                    stale keys".
                */
                AssetDatabase.ForceReserializeAssets(
                    new[] { assetPath },
                    ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata
                );
            }
            catch (Exception)
            {
                Restore(assetPath, original);
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            int after = LoadedObjectCount(assetPath);
            if (after < before)
            {
                Restore(assetPath, original);
                return StaleSerializedKeyRepairOutcome.RefusedLostSubObjects;
            }

            return SameBytes(assetPath, original)
                ? StaleSerializedKeyRepairOutcome.NotRewritten
                : StaleSerializedKeyRepairOutcome.Repaired;
        }

        private static int LoadedObjectCount(string assetPath)
        {
            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (loaded == null)
            {
                return 0;
            }

            int count = 0;
            for (int index = 0; index < loaded.Length; ++index)
            {
                if (loaded[index] != null)
                {
                    ++count;
                }
            }

            return count;
        }

        private static bool SameBytes(string assetPath, byte[] original)
        {
            try
            {
                byte[] current = File.ReadAllBytes(assetPath);
                if (current.Length != original.Length)
                {
                    return false;
                }

                for (int index = 0; index < current.Length; ++index)
                {
                    if (current[index] != original[index])
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Restore(string assetPath, byte[] original)
        {
            try
            {
                File.WriteAllBytes(assetPath, original);
            }
            catch (Exception)
            {
                return;
            }

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );
        }
    }
#endif
}
