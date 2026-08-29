// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// Something that threw while validating one asset, recorded instead of ending the run.
    /// </summary>
    /// <remarks>
    /// A run reports these separately from findings. A rule that throws has produced no answer for
    /// that asset, which is not the same as answering "nothing wrong" -- presenting it as a clean
    /// result would be the run lying about coverage.
    /// </remarks>
    public readonly struct ValidationRuleFailure
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationRuleFailure"/> struct.
        /// </summary>
        /// <param name="ruleId">
        /// The rule that threw, or <c>null</c> when the asset itself failed to load, which is not
        /// any one rule's fault.
        /// </param>
        /// <param name="assetPath">The asset it was validating.</param>
        /// <param name="exception">What it threw.</param>
        public ValidationRuleFailure(string ruleId, string assetPath, Exception exception)
        {
            RuleId = ruleId;
            AssetPath = assetPath;
            Exception = exception;
        }

        /// <summary>
        /// The rule that threw, or <c>null</c> when loading the asset threw.
        /// </summary>
        /// <remarks>
        /// A run substitutes the rule's type name when a rule's own <c>RuleId</c> is unusable, so
        /// <c>null</c> here always means the loader rather than an unnamed rule. Prefer
        /// <see cref="IsLoadFailure"/> to reading it for that.
        /// </remarks>
        public string RuleId { get; }

        /// <summary>Whether loading the asset threw, rather than a rule.</summary>
        public bool IsLoadFailure => string.IsNullOrEmpty(RuleId);

        /// <summary>The asset it was validating when it threw.</summary>
        public string AssetPath { get; }

        /// <summary>What it threw.</summary>
        public Exception Exception { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            string thrown = Exception == null ? "an exception" : Exception.ToString();
            string subject = IsLoadFailure ? "Loading the asset" : RuleId;
            return $"{subject} threw while validating {AssetPath}: {thrown}";
        }
    }
#endif
}
