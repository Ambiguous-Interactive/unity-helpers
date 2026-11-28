// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using WallstopStudios.UnityHelpers.Core.Helper;
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>Identifies a field judged by an authored requirement scan.</summary>
    public readonly struct AuthoredRequirementField : IEquatable<AuthoredRequirementField>
    {
        /// <summary>The condition applied to the field.</summary>
        public AuthoredRequirementKind Kind { get; }

        /// <summary>The type declaring the field.</summary>
        public Type DeclaringType { get; }

        /// <summary>The field's name.</summary>
        public string Name { get; }

        /// <summary>Whether the scalar is serialized as a number or boolean.</summary>
        internal bool IsNumeric { get; }

        /// <summary>Whether the field is a structured value with serialized child values.</summary>
        internal bool IsComposite { get; }

        /// <summary>Whether the field holds an object reference rather than a string.</summary>
        internal bool IsObjectReference { get; }

        /// <summary>Whether the field holds a collection whose elements are judged one by one.</summary>
        internal bool IsCollection { get; }

        internal bool IsDictionary { get; }

        internal Type ValueType { get; }

        /// <summary>The names older assets still record the field under.</summary>
        internal IReadOnlyList<string> Aliases { get; }

        /// <summary>Initializes a new instance of the <see cref="AuthoredRequirementField"/> struct.</summary>
        /// <param name="declaringType">The type declaring the field.</param>
        /// <param name="name">The field's name.</param>
        /// <param name="isObjectReference">Whether the field holds an object reference.</param>
        /// <param name="isCollection">Whether the field holds a collection.</param>
        /// <param name="aliases">The names older assets still record the field under.</param>
        /// <param name="kind">The requirement applied to the field.</param>
        /// <param name="isNumeric">Whether the scalar is numeric or boolean.</param>
        /// <param name="isComposite">Whether the field contains structured serialized values.</param>
        /// <param name="isDictionary">Whether the collection stores its keys in a separate sequence.</param>
        /// <param name="valueType">The serialized value type.</param>
        internal AuthoredRequirementField(
            Type declaringType,
            string name,
            bool isObjectReference,
            bool isCollection,
            IReadOnlyList<string> aliases,
            AuthoredRequirementKind kind = AuthoredRequirementKind.Assigned,
            bool isNumeric = false,
            bool isComposite = false,
            bool isDictionary = false,
            Type valueType = null
        )
        {
            Kind = kind;
            IsNumeric = isNumeric;
            IsComposite = isComposite;
            DeclaringType = declaringType;
            Name = name;
            IsObjectReference = isObjectReference;
            IsCollection = isCollection;
            IsDictionary = isDictionary;
            ValueType = valueType;
            Aliases = aliases;
        }

        /// <summary>Compares the field declaration and its requirement kind.</summary>
        public bool Equals(AuthoredRequirementField other)
        {
            return Kind == other.Kind
                && DeclaringType == other.DeclaringType
                && string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is AuthoredRequirementField other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(Kind, DeclaringType, Name);
        }
    }
#endif
}
