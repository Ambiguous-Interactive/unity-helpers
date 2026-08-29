// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// A rule that threw while validating one asset, recorded instead of ending the run.
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
        /// <param name="ruleId">The rule that threw.</param>
        /// <param name="assetPath">The asset it was validating.</param>
        /// <param name="exception">What it threw.</param>
        public ValidationRuleFailure(string ruleId, string assetPath, Exception exception)
        {
            RuleId = ruleId;
            AssetPath = assetPath;
            Exception = exception;
        }

        /// <summary>The rule that threw.</summary>
        public string RuleId { get; }

        /// <summary>The asset it was validating when it threw.</summary>
        public string AssetPath { get; }

        /// <summary>What it threw.</summary>
        public Exception Exception { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            string thrown = Exception == null ? "an exception" : Exception.ToString();
            return $"{RuleId} threw while validating {AssetPath}: {thrown}";
        }
    }
#endif
}
