// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    /// <summary>
    /// The stable identifiers of the rules this package ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every id is <c>UnityHelpers.&lt;Area&gt;.&lt;Check&gt;</c>: the vendor, the thing being
    /// checked, and what is being asked of it. A new check adds a name; nothing is renumbered and
    /// nothing is renamed. An id is half of every <see cref="ValidationFinding.Id"/> and is what a
    /// suppression line names, so it is a compatibility surface from the first release that ships
    /// one -- renaming an id silently un-suppresses every decision recorded against it.
    /// </para>
    /// <para>
    /// Deliberately not the <c>WUH###</c> / <c>WPROTO###</c> shape the analyzers use. Those are
    /// compiler diagnostics, where a short code has to fit a build log and a pragma; a validation id
    /// is read in a suppression file a reviewer diffs, where a name says what was switched off and a
    /// number does not. Keeping the two schemes apart also means a reader never has to work out
    /// which subsystem a code came from.
    /// </para>
    /// <para>
    /// A consuming project writes its own ids under its own vendor prefix, which is what keeps two
    /// packages' rules from colliding in one suppression file.
    /// </para>
    /// </remarks>
    public static class ValidationRuleIds
    {
        /// <summary>The prefix every rule this package ships is named under.</summary>
        public const string Prefix = "UnityHelpers.";

        /// <summary>Reports an authored slot a <c>[WNotNull]</c> annotation says must be filled.</summary>
        public const string RequiredFieldEmpty = Prefix + "Assets.RequiredFieldEmpty";

        /// <summary>Reports an authored dictionary whose keys and values no longer pair up.</summary>
        public const string DictionaryPairing = Prefix + "Assets.DictionaryPairing";

        /// <summary>Reports an animation keyframe whose object no longer resolves.</summary>
        public const string AnimationKeyframeEmpty = Prefix + "Assets.AnimationKeyframeEmpty";

        /// <summary>Reports a script asset that is not named after the type it binds.</summary>
        public const string ScriptFileNameMismatch = Prefix + "Scripts.FileNameMismatch";
    }
#endif
}
