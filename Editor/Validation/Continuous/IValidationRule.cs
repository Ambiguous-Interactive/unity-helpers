// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A check a project runs continuously against its own assets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented in editor code and registered with a <see cref="ValidationRun"/>. Every call
    /// happens on the main thread, because everything a rule can usefully ask about an asset is a
    /// Unity API and none of them are thread-safe.
    /// </para>
    /// <para>
    /// <see cref="AppliesTo"/> is answered before the asset is loaded and must stay cheap: it is
    /// asked once per rule per asset over the whole project. Answering <c>false</c> for the assets a
    /// rule does not care about is what keeps a project-wide run from deserializing everything.
    /// </para>
    /// <para>
    /// A rule that throws does not fail the run. The exception is recorded against the rule as a
    /// <see cref="ValidationRuleFailure"/> and the run continues, because one broken rule must not
    /// hide every other rule's findings.
    /// </para>
    /// </remarks>
    public interface IValidationRule
    {
        /// <summary>
        /// A stable identifier for this rule, unique within a project.
        /// </summary>
        /// <remarks>
        /// It is half of every <see cref="ValidationFinding.Id"/> this rule produces, so renaming it
        /// orphans any suppression recorded against it. Treat it the way a diagnostic code is
        /// treated: choose once, never change.
        /// </remarks>
        string RuleId { get; }

        /// <summary>A short human-readable name, for a results list.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Reports whether this rule wants <paramref name="target"/> loaded and validated.
        /// </summary>
        /// <param name="target">The asset, described from import metadata only.</param>
        /// <returns><c>true</c> to have the asset loaded and passed to <see cref="Validate"/>.</returns>
        bool AppliesTo(in ValidationTarget target);

        /// <summary>
        /// Adds a finding to <paramref name="findings"/> for everything wrong with the asset.
        /// </summary>
        /// <param name="target">The asset, described from import metadata.</param>
        /// <param name="asset">
        /// The loaded main asset. It can be <c>null</c> when Unity declined to load it, and a rule
        /// must handle that rather than assume the load succeeded.
        /// </param>
        /// <param name="findings">The run's collector. Never <c>null</c>; append, do not clear.</param>
        /// <remarks>
        /// A rule must not drive the run that is calling it. Stepping a run from inside
        /// <see cref="Validate"/> re-enters the collector it was handed.
        /// </remarks>
        void Validate(in ValidationTarget target, Object asset, List<ValidationFinding> findings);
    }
#endif
}
