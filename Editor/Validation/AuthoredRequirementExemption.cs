// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>One annotated field the scan could not judge, and why.</summary>
    public readonly struct AuthoredRequirementExemption
    {
        /// <summary>The type declaring the annotated field.</summary>
        public Type DeclaringType { get; }

        /// <summary>The field's name.</summary>
        public string FieldName { get; }

        /// <summary>Why the field could not be judged.</summary>
        public AuthoredRequirementExemptionReason Reason { get; }

        /// <summary>Initializes a new instance of the <see cref="AuthoredRequirementExemption"/> struct.</summary>
        /// <param name="declaringType">The type declaring the annotated field.</param>
        /// <param name="fieldName">The field's name.</param>
        /// <param name="reason">Why the field could not be judged.</param>
        public AuthoredRequirementExemption(
            Type declaringType,
            string fieldName,
            AuthoredRequirementExemptionReason reason
        )
        {
            DeclaringType = declaringType;
            FieldName = fieldName;
            Reason = reason;
        }

        /// <summary>Renders the exemption as one budget line.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{DeclaringType?.FullName}.{FieldName}: {Reason}";
        }
    }
#endif
}
