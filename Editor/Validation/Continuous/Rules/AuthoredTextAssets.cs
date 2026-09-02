// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;

    /// <summary>
    /// Answers, from import metadata alone, whether an asset is one Unity wrote as authored text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of <see cref="IValidationRule.AppliesTo"/> for the rules that read a file
    /// as text, so it is asked once per rule per asset over the whole project and does nothing but
    /// compare an extension and a type Unity already knows.
    /// </para>
    /// <para>
    /// A <c>.asset</c> is claimed only when its main object is a <see cref="ScriptableObject"/> or
    /// when Unity reports no type at all. Everything else with that extension is a native asset
    /// Unity writes as binary whatever the serialization mode says -- <c>LightingData.asset</c> is
    /// the one every project with baked lighting has -- and claiming those would report a coverage
    /// hole for every one of them on every run, forever.
    /// </para>
    /// </remarks>
    internal static class AuthoredTextAssets
    {
        private const string SceneExtension = ".unity";
        private const string PrefabExtension = ".prefab";
        private const string AssetExtension = ".asset";

        /// <summary>Reports whether <paramref name="target"/> is worth reading as text.</summary>
        /// <param name="target">The asset, described from import metadata only.</param>
        /// <returns><c>true</c> when the asset is one Unity wrote authored documents into.</returns>
        internal static bool CarriesAuthoredDocuments(in ValidationTarget target)
        {
            string assetPath = target.AssetPath;
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (
                assetPath.EndsWith(PrefabExtension, StringComparison.OrdinalIgnoreCase)
                || assetPath.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            if (!assetPath.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Type mainAssetType = target.MainAssetType;
            return mainAssetType == null
                || typeof(ScriptableObject).IsAssignableFrom(mainAssetType);
        }
    }
#endif
}
