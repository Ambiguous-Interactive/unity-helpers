// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>One annotated field, classified into the shape the text reader can judge.</summary>
    internal readonly struct AuthoredRequirementField
    {
        /// <summary>The type declaring the field.</summary>
        public Type DeclaringType { get; }

        /// <summary>The field's name.</summary>
        public string Name { get; }

        /// <summary>Whether the field holds an object reference rather than a string.</summary>
        public bool IsObjectReference { get; }

        /// <summary>Whether the field holds a collection whose elements are judged one by one.</summary>
        public bool IsCollection { get; }

        /// <summary>The names older assets still record the field under.</summary>
        public IReadOnlyList<string> Aliases { get; }

        /// <summary>Initializes a new instance of the <see cref="AuthoredRequirementField"/> struct.</summary>
        /// <param name="declaringType">The type declaring the field.</param>
        /// <param name="name">The field's name.</param>
        /// <param name="isObjectReference">Whether the field holds an object reference.</param>
        /// <param name="isCollection">Whether the field holds a collection.</param>
        /// <param name="aliases">The names older assets still record the field under.</param>
        public AuthoredRequirementField(
            Type declaringType,
            string name,
            bool isObjectReference,
            bool isCollection,
            IReadOnlyList<string> aliases
        )
        {
            DeclaringType = declaringType;
            Name = name;
            IsObjectReference = isObjectReference;
            IsCollection = isCollection;
            Aliases = aliases;
        }
    }
#endif
}
