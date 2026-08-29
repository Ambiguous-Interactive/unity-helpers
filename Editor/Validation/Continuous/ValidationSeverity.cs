// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// How much a <see cref="ValidationFinding"/> should interrupt whoever is reading it.
    /// </summary>
    /// <remarks>
    /// Deliberately ordered so a numeric comparison is a severity comparison: a consumer filtering
    /// to "at least a warning" writes <c>ValidationSeverity.Warning &lt;= finding.Severity</c>
    /// rather than enumerating members.
    /// </remarks>
    public enum ValidationSeverity
    {
        /// <summary>
        /// Reserved for uninitialized state. Do not use directly.
        /// </summary>
        [Obsolete("Use a specific ValidationSeverity value.")]
        Unknown = 0,

        /// <summary>
        /// Something worth knowing that is not wrong. A rule reporting a convention or a count
        /// uses this, and a default filter is expected to hide it.
        /// </summary>
        Info = 1,

        /// <summary>
        /// Something that is probably a mistake but that the project still runs with.
        /// </summary>
        Warning = 2,

        /// <summary>
        /// Something that is broken: data already lost, a reference that cannot resolve, a
        /// contract the asset cannot satisfy.
        /// </summary>
        Error = 3,
    }
#endif
}
