// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// One asset a validation run will consider, described entirely from metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here requires the asset to be loaded, and that is the point.
    /// <see cref="IValidationRule.AppliesTo"/> is answered from this struct alone, so a project-wide
    /// run deserializes only the assets some rule actually asked for. Loading an asset to decide
    /// whether it is interesting runs its <c>OnEnable</c> and its owner's <c>OnValidate</c>, which
    /// is the cost that made an earlier project-wide scan stall the editor.
    /// </para>
    /// <para>
    /// <see cref="MainAssetType"/> comes from Unity's import metadata rather than from a load, so it
    /// can be <c>null</c> for an asset Unity has no importer for.
    /// </para>
    /// </remarks>
    public readonly struct ValidationTarget : IEquatable<ValidationTarget>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationTarget"/> struct.
        /// </summary>
        /// <param name="assetGuid">The asset's GUID, stable across moves and renames.</param>
        /// <param name="assetPath">The asset's project-relative path.</param>
        /// <param name="mainAssetType">The main asset's type, or <c>null</c> when unknown.</param>
        public ValidationTarget(string assetGuid, string assetPath, Type mainAssetType)
        {
            AssetGuid = assetGuid;
            AssetPath = assetPath;
            MainAssetType = mainAssetType;
        }

        /// <summary>The asset's GUID. This is the identity a finding is remembered under.</summary>
        public string AssetGuid { get; }

        /// <summary>The asset's project-relative path, for display and for loading.</summary>
        public string AssetPath { get; }

        /// <summary>The main asset's type, or <c>null</c> when Unity reports none.</summary>
        public Type MainAssetType { get; }

        /// <summary>Reports whether this target names an asset at all.</summary>
        /// <returns><c>true</c> when both the GUID and the path are present.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(AssetGuid) && !string.IsNullOrEmpty(AssetPath);
        }

        /// <summary>Reports whether two targets name the same asset.</summary>
        /// <param name="other">The target to compare against.</param>
        /// <returns><c>true</c> when both carry the same GUID.</returns>
        public bool Equals(ValidationTarget other)
        {
            return string.Equals(AssetGuid, other.AssetGuid, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ValidationTarget other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return AssetGuid == null ? 0 : StringComparer.Ordinal.GetHashCode(AssetGuid);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.IsNullOrEmpty(AssetPath) ? AssetGuid : AssetPath;
        }
    }
#endif
}
