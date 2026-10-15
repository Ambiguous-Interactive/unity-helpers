// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using System.Collections.Generic;
#if UNITY_EDITOR
    using System;

    /// <summary>One annotated field the scan could not judge, and why.</summary>
    public readonly struct AuthoredRequirementExemption
        : global::System.IEquatable<AuthoredRequirementExemption>
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

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(AuthoredRequirementExemption other)
        {
            return EqualityComparer<Type>.Default.Equals(DeclaringType, other.DeclaringType)
                && EqualityComparer<string>.Default.Equals(FieldName, other.FieldName)
                && EqualityComparer<AuthoredRequirementExemptionReason>.Default.Equals(
                    Reason,
                    other.Reason
                );
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is AuthoredRequirementExemption other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                DeclaringType,
                FieldName,
                Reason
            );
        }
    }
#endif
}
