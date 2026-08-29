// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;

    /// <summary>
    /// How much of an assembly's <c>[WProtoSubtype]</c> declarations the run that computed a plan
    /// was actually able to see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the difference between "this declaration is gone" and "this declaration is not
    /// visible from here", and nothing else in the planner can tell them apart. <c>TypeCache</c>
    /// answers for the editor's own compilation: a subtype behind <c>#if !UNITY_EDITOR</c>, behind a
    /// platform define, or in an assembly that failed to compile is absent from it and present in
    /// the player. Retiring such an entry claims its number forever and takes the entry away from a
    /// type that still exists, which is data loss that surfaces only when an old payload is read.
    /// </para>
    /// <para>
    /// One value therefore decides both halves of the policy -- what the plan contains and whether
    /// an unattended pass may write it -- because a guard on the write decision alone is not a guard
    /// on the plan's content.
    /// </para>
    /// </remarks>
    public enum WProtoSubtypeTagDiscovery
    {
        /// <summary>Not a discovery any run has; name <see cref="Complete"/> or <see cref="Partial"/>.</summary>
        [Obsolete("Name the discovery the run actually had: Complete or Partial.")]
        Unknown = 0,

        /// <summary>
        /// Every declaration the assembly has was visible, so an entry with no declaration names a
        /// type that was deleted and its number is retired.
        /// </summary>
        /// <remarks>
        /// What the menu item, the headless entry points and the drift gate use: an explicit act
        /// whose diff a human reads before committing it.
        /// </remarks>
        Complete = 1,

        /// <summary>
        /// Declarations may be missing from the survey, so an entry with no declaration keeps its
        /// number instead of being retired.
        /// </summary>
        /// <remarks>
        /// What the automatic pass uses. Nothing is lost: the number stays claimed, so it cannot be
        /// handed to anything else, and converting it to a retirement remains available on the
        /// explicit run.
        /// </remarks>
        Partial = 2,
    }
}
