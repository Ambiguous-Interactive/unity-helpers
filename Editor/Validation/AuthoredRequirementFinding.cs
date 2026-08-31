// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>One authored slot an attribute said must be filled, and is not.</summary>
    public readonly struct AuthoredRequirementFinding
    {
        /// <summary>The asset carrying the empty slot.</summary>
        public string AssetPath { get; }

        /// <summary>The one-based line the slot is written on.</summary>
        public int LineNumber { get; }

        /// <summary>The type declaring the annotated field.</summary>
        public Type DeclaringType { get; }

        /// <summary>The field's name.</summary>
        public string FieldName { get; }

        /// <summary>Initializes a new instance of the <see cref="AuthoredRequirementFinding"/> struct.</summary>
        /// <param name="assetPath">The asset carrying the empty slot.</param>
        /// <param name="lineNumber">The one-based line the slot is written on.</param>
        /// <param name="declaringType">The type declaring the annotated field.</param>
        /// <param name="fieldName">The field's name.</param>
        public AuthoredRequirementFinding(
            string assetPath,
            int lineNumber,
            Type declaringType,
            string fieldName
        )
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            DeclaringType = declaringType;
            FieldName = fieldName;
        }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{AssetPath}:{LineNumber}: {DeclaringType?.Name}.{FieldName} is required and empty.";
        }
    }
#endif
}
