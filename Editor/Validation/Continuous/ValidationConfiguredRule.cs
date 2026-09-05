// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using Object = UnityEngine.Object;

    internal sealed class ValidationConfiguredRule : IValidationRule
    {
        private readonly IValidationRule _rule;
        private readonly ValidationSeverity? _severity;

        internal ValidationConfiguredRule(IValidationRule rule, ValidationSeverity? severity)
        {
            _rule = rule;
            _severity = severity;
        }

        /// <inheritdoc />
        public string RuleId => _rule.RuleId;

        /// <inheritdoc />
        public string DisplayName => _rule.DisplayName;

        /// <inheritdoc />
        public bool AppliesTo(in ValidationTarget target) => _rule.AppliesTo(in target);

        /// <inheritdoc />
        public void Validate(
            in ValidationTarget target,
            Object asset,
            List<ValidationFinding> findings
        )
        {
            int first = findings.Count;
            _rule.Validate(in target, asset, findings);
            if (!_severity.HasValue)
                return;
            for (int index = first; index < findings.Count; index++)
            {
                ValidationFinding finding = findings[index];
                Object subject = finding.TryGetTarget(out Object live) ? live : null;
                findings[index] = new ValidationFinding(
                    finding.RuleId,
                    _severity.Value,
                    subject,
                    finding.AssetGuid,
                    finding.AssetPath,
                    finding.Discriminator,
                    finding.Message,
                    finding.SourceFingerprint,
                    finding.OriginalSeverity
                );
            }
        }
    }
#endif
}
