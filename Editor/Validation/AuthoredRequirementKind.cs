// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>The condition an authored field must satisfy.</summary>
    public enum AuthoredRequirementKind
    {
        /// <summary>No requirement was selected.</summary>
        [Obsolete("Select a requirement kind.")]
        None = 0,

        /// <summary>Strings and object references must be assigned, including each collection element.</summary>
        Assigned = 1,

        /// <summary>A string or collection must contain at least one item.</summary>
        NotEmpty = 2,

        /// <summary>A scalar must differ from its serialized default value.</summary>
        NotDefault = 3,
    }
#endif
}
