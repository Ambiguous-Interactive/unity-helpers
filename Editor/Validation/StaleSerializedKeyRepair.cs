// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
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
            return RepairAsset(assetPath, null);
        }

        /// <summary>
        /// Rewrites one asset, counting what it holds through <paramref name="objectCounter"/>.
        /// </summary>
        /// <param name="assetPath">The asset to rewrite.</param>
        /// <param name="objectCounter">
        /// Answers how many non-null objects the asset holds, or <c>null</c> for the asset database.
        /// </param>
        /// <returns>What happened.</returns>
        /// <remarks>
        /// The production entry point supplies no counter and so takes the asset database's answer.
        /// The seam exists because nothing a test can author makes <c>ForceReserializeAssets</c>
        /// lose content -- a <c>VolumeProfile</c> does, and five modelled <c>HideFlags</c> shapes do
        /// not -- so the undo this type exists for would otherwise never execute.
        /// </remarks>
        internal static StaleSerializedKeyRepairOutcome RepairAsset(
            string assetPath,
            Func<string, int> objectCounter
        )
        {
            return RepairAsset(assetPath, objectCounter, null);
        }

        /// <summary>
        /// Rewrites one asset through <paramref name="rewriteAsset"/>, counting what it holds
        /// through <paramref name="objectCounter"/>.
        /// </summary>
        /// <param name="assetPath">The asset to rewrite.</param>
        /// <param name="objectCounter">
        /// Answers how many non-null objects the asset holds, or <c>null</c> for the asset database.
        /// </param>
        /// <param name="rewriteAsset">
        /// Rewrites the asset, or <c>null</c> for <c>ForceReserializeAssets</c>.
        /// </param>
        /// <returns>What happened.</returns>
        /// <remarks>
        /// The second seam exists for the same reason the first one does: nothing a test can author
        /// makes <c>ForceReserializeAssets</c> throw, so the branch that undoes a failed rewrite
        /// would otherwise never execute and its outcome would be unverified forever.
        /// </remarks>
        internal static StaleSerializedKeyRepairOutcome RepairAsset(
            string assetPath,
            Func<string, int> objectCounter,
            Action<string> rewriteAsset
        )
        {
            Func<string, int> countObjects = objectCounter ?? LoadedObjectCount;
            Action<string> rewrite = rewriteAsset ?? ForceReserialize;
            if (string.IsNullOrEmpty(assetPath))
            {
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            // Resolve filesystem paths against the project, not the process working directory.
            string filePath = AuthoredAssetPaths.ToFileSystemPath(assetPath);
            byte[] original;
            try
            {
                if (!File.Exists(filePath))
                {
                    return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
                }

                original = File.ReadAllBytes(filePath);
            }
            catch (Exception)
            {
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            int before = countObjects(assetPath);
            if (before <= 0)
            {
                return StaleSerializedKeyRepairOutcome.RefusedUnreadable;
            }

            try
            {
                rewrite(assetPath);
            }
            catch (Exception exception)
            {
                // Report restoration only after its outcome is known.
                Debug.LogError(
                    $"[Unity Helpers] Rewriting {assetPath} threw: {exception.Message}. "
                        + "Nothing was repaired."
                );
                return Restore(assetPath, filePath, original)
                    ? StaleSerializedKeyRepairOutcome.RefusedRewriteThrew
                    : StaleSerializedKeyRepairOutcome.RefusedUndoFailed;
            }

            int after = countObjects(assetPath);
            if (after < before)
            {
                return Restore(assetPath, filePath, original)
                    ? StaleSerializedKeyRepairOutcome.RefusedLostSubObjects
                    : StaleSerializedKeyRepairOutcome.RefusedUndoFailed;
            }

            return SameBytes(filePath, original)
                ? StaleSerializedKeyRepairOutcome.NotRewritten
                : StaleSerializedKeyRepairOutcome.Repaired;
        }

        private static void ForceReserialize(string assetPath)
        {
            // Prefab rewrites require metadata serialization; assets-only silently leaves them unchanged.
            AssetDatabase.ForceReserializeAssets(
                new[] { assetPath },
                ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata
            );
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

        private static bool SameBytes(string filePath, byte[] original)
        {
            try
            {
                byte[] current = File.ReadAllBytes(filePath);
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

        /// <summary>Puts the original bytes back and makes the editor re-read them.</summary>
        /// <param name="assetPath">The asset path, for the re-import.</param>
        /// <param name="filePath">The resolved path, for the write.</param>
        /// <param name="original">The bytes captured before the rewrite.</param>
        /// <returns><c>false</c> when the undo did not complete.</returns>
        /// <remarks>
        /// A failure here is the worst outcome this type has: the rewrite already happened, so
        /// swallowing it would leave a damaged asset while the caller reported a refusal. It is
        /// reported as an error naming the file, because the recovery is a human restoring it.
        /// </remarks>
        private static bool Restore(string assetPath, string filePath, byte[] original)
        {
            try
            {
                File.WriteAllBytes(filePath, original);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[Unity Helpers] Could not undo the rewrite of {assetPath}: {exception.Message}. "
                        + $"The file at {filePath} holds the rewritten bytes, not the original. "
                        + "Restore it from source control before saving the project."
                );
                return false;
            }

            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );
            return true;
        }
    }
#endif
}
