// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    /// <summary>
    /// How a shipped rule says it could not see the asset it was asked about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu commands keep unreadable paths out of their findings entirely, because a finding
    /// there means a defect and a hole in the measurement is not one. A continuous rule has no third
    /// channel: it can add a finding, or it can throw, and a throw is recorded as a
    /// <see cref="ValidationRuleFailure"/> that blocks a batch run whatever the threshold. Unity
    /// writes <c>LightingData.asset</c> as binary whatever the serialization mode says, so throwing
    /// would fail every run of every project with baked lighting, permanently, over something no reader can fix.
    /// </para>
    /// <para>
    /// So the hole is reported as an <see cref="ValidationSeverity.Info"/> finding instead: visible
    /// in the window, below the default severity floor, and suppressible per asset like anything
    /// else. What matters is that a rule never reports an asset clean that it never read.
    /// </para>
    /// </remarks>
    internal static class ValidationCoverage
    {
        /// <summary>What tells a coverage finding apart from a rule's real findings.</summary>
        internal const string UnreadableDiscriminator = "unreadable";

        /// <summary>Builds the finding that says an asset was not checked.</summary>
        /// <param name="ruleId">The reporting rule's stable identifier.</param>
        /// <param name="target">The asset that could not be read.</param>
        /// <param name="reason">What the rule could not do, in one clause.</param>
        /// <returns>An <see cref="ValidationSeverity.Info"/> finding naming the hole.</returns>
        internal static ValidationFinding Unreadable(
            string ruleId,
            in ValidationTarget target,
            string reason
        )
        {
            return new ValidationFinding(
                ruleId,
                ValidationSeverity.Info,
                null,
                target.AssetGuid,
                target.AssetPath,
                UnreadableDiscriminator,
                reason
                    + ", so this rule did not check it. A binary serialized asset, a file lock and "
                    + "a permission error all look like this."
            );
        }
    }
#endif
}
