// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>
    /// Builds the asset list a <see cref="ValidationRun"/> walks, without loading anything.
    /// </summary>
    /// <remarks>
    /// Every question answered here comes from Unity's import metadata:
    /// <c>AssetDatabase.FindAssets</c>, <c>GUIDToAssetPath</c> and <c>GetMainAssetTypeAtPath</c> all
    /// read the database rather than deserializing the asset. Answering "what type is this" with a
    /// load would run the asset's <c>OnEnable</c> and its consumers' <c>OnValidate</c> for every
    /// asset in the project, which is the cost that makes an unbounded scan unusable.
    /// </remarks>
    public static class ValidationTargets
    {
        /// <summary>
        /// Matches every asset. An empty filter is not reliably "everything" across editor
        /// versions; a type filter naming the root of Unity's own hierarchy is.
        /// </summary>
        private const string EveryAsset = "t:Object";

        /// <summary>
        /// Enumerates every asset under <paramref name="searchInFolders"/>.
        /// </summary>
        /// <param name="searchInFolders">
        /// Project-relative folders to search, or <c>null</c>/empty for the whole project. A folder
        /// that does not exist is skipped rather than reported.
        /// </param>
        /// <returns>
        /// One target per asset, in the asset database's order; never <c>null</c>, and empty when
        /// the search matched nothing or the editor refused the query.
        /// </returns>
        public static List<ValidationTarget> Enumerate(params string[] searchInFolders)
        {
            List<ValidationTarget> targets = new List<ValidationTarget>();
            string[] guids;
            try
            {
                if (searchInFolders == null || searchInFolders.Length == 0)
                {
                    guids = AssetDatabase.FindAssets(EveryAsset);
                }
                else
                {
                    // Unity logs a warning for every folder it cannot find, so a caller passing a
                    // folder that was renamed or never existed would fill the console rather than
                    // get an empty answer. Asking the database first is the same question without
                    // the noise.
                    List<string> existing = new List<string>(searchInFolders.Length);
                    foreach (string folder in searchInFolders)
                    {
                        if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                        {
                            existing.Add(folder);
                        }
                    }

                    if (existing.Count == 0)
                    {
                        return targets;
                    }

                    guids = AssetDatabase.FindAssets(EveryAsset, existing.ToArray());
                }
            }
            catch (Exception)
            {
                return targets;
            }

            if (guids == null)
            {
                return targets;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                if (string.IsNullOrEmpty(guid) || !seen.Add(guid))
                {
                    continue;
                }

                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                targets.Add(
                    new ValidationTarget(guid, path, AssetDatabase.GetMainAssetTypeAtPath(path))
                );
            }

            return targets;
        }
    }
#endif
}
