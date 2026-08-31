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
    /// <c>ForceReserializeAssets</c> is not safe unsupervised: an asset whose content lives in
    /// sub-objects can come back with them gone while the rewrite reports success. So each asset is
    /// rewritten alone and any rewrite that lowers its non-null object count is undone. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
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
            foreach (Object asset in loaded)
            {
                if (asset != null)
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
