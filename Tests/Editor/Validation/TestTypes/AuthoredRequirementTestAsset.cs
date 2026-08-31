// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// The subject of the committed authored-requirement fixtures, one field per shape the gate
    /// judges.
    /// </summary>
    /// <remarks>
    /// Two committed assets sit beside it: one with every required slot filled and one with every
    /// required slot empty. Both are text a hand wrote rather than something a test saves, because
    /// the defect the gate exists to catch is an asset nobody has opened since it broke.
    /// </remarks>
    internal sealed class AuthoredRequirementTestAsset : ScriptableObject
    {
        /// <summary>A single required object reference.</summary>
        [WNotNull]
        public Material requiredMaterial;

        /// <summary>A required string, which the inspector calls empty when it is blank.</summary>
        [WNotNull]
        public string requiredName;

        /// <summary>A required reference the drawer judges once per element.</summary>
        [WNotNull]
        public Material[] requiredMaterials;

        /// <summary>
        /// A required reference the assets still record under its old name, because
        /// <see cref="FormerlySerializedAsAttribute"/> keeps the game correct and leaves the file
        /// saying what it said before.
        /// </summary>
        [WNotNull]
        [FormerlySerializedAs("legacyIcon")]
        public Sprite icon;

        /// <summary>A reference nothing requires, which must never be reported.</summary>
        public Material optionalMaterial;

        /// <summary>A number, which has no empty state the inspector reports.</summary>
        public int weight;
    }
}
