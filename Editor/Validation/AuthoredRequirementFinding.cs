// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using System.Collections.Generic;
#if UNITY_EDITOR
    using System;

    /// <summary>One authored slot an attribute said must be filled, and is not.</summary>
    public readonly struct AuthoredRequirementFinding
        : global::System.IEquatable<AuthoredRequirementFinding>
    {
        /// <summary>The requirement the authored value violates.</summary>
        public AuthoredRequirementKind Kind { get; }

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
            : this(
                assetPath,
                lineNumber,
                declaringType,
                fieldName,
                AuthoredRequirementKind.Assigned
            ) { }

        /// <summary>Creates a finding for an explicit requirement kind.</summary>
        /// <param name="assetPath">The asset carrying the value.</param>
        /// <param name="lineNumber">The one-based source line.</param>
        /// <param name="declaringType">The type declaring the annotated field.</param>
        /// <param name="fieldName">The annotated field's name.</param>
        /// <param name="kind">The requirement that was violated.</param>
        public AuthoredRequirementFinding(
            string assetPath,
            int lineNumber,
            Type declaringType,
            string fieldName,
            AuthoredRequirementKind kind
        )
        {
            Kind = kind;
            AssetPath = assetPath;
            LineNumber = lineNumber;
            DeclaringType = declaringType;
            FieldName = fieldName;
        }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            string state = Kind == AuthoredRequirementKind.NotDefault ? "default" : "empty";
            return $"{AssetPath}:{LineNumber}: {DeclaringType?.Name}.{FieldName} is required and {state}.";
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(AuthoredRequirementFinding other)
        {
            return Kind == other.Kind
                && EqualityComparer<string>.Default.Equals(AssetPath, other.AssetPath)
                && LineNumber.Equals(other.LineNumber)
                && EqualityComparer<Type>.Default.Equals(DeclaringType, other.DeclaringType)
                && EqualityComparer<string>.Default.Equals(FieldName, other.FieldName);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is AuthoredRequirementFinding other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                AssetPath,
                LineNumber,
                DeclaringType,
                FieldName,
                Kind
            );
        }
    }
#endif
}
