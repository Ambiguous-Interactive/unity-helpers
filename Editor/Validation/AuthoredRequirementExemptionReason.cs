// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// Why an annotated field could not be judged from the assets that carry it.
    /// </summary>
    /// <remarks>
    /// An exemption is reported rather than skipped. A gate that quietly cannot see part of its
    /// subject is the failure mode the gate exists to prevent, so the population is printed as a
    /// budget that can only shrink.
    /// </remarks>
    public enum AuthoredRequirementExemptionReason
    {
        /// <summary>Unused.</summary>
        [Obsolete("An exemption always names a reason.")]
        Unknown = 0,

        /// <summary>
        /// The declaring type has no <c>MonoScript</c>, so no document names it and its annotations
        /// are unreadable. An inline-only <c>[Serializable]</c> class is the usual case.
        /// </summary>
        NoBoundScript = 1,

        /// <summary>
        /// The field's value has no text form the inspector's own emptiness test applies to, so
        /// restating it here would give the build and the inspector two definitions of one word.
        /// </summary>
        ValueNotReadableAsText = 2,
    }
#endif
}
