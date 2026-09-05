// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Reports every authored slot a <c>[WNotNull]</c> annotation says must be filled and is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The continuous half of <see cref="AuthoredRequirementValidator"/>, judging one asset at a
    /// time through that validator's own code rather than a second copy of it.
    /// </para>
    /// <para>
    /// <b>Severity is <see cref="ValidationSeverity.Error"/>.</b> The annotation is the author's own
    /// statement that the slot must be filled, and an empty one is a null reference the moment
    /// something reads it -- a contract the asset cannot satisfy rather than a shape somebody might
    /// have meant. The drawer already says so in the inspector; a build has nobody looking at one.
    /// </para>
    /// <para>
    /// The field index -- which script guid carries which annotated field -- is built once, on the
    /// first asset this rule is given, and held for the life of the instance. Rebuilding it per
    /// asset would make the per-asset cost a <c>TypeCache</c> sweep plus a script lookup per
    /// carrying type. It cannot go stale across a domain reload, because it is instance state on a
    /// rule that a run constructs and discards, and because everything it is derived from can only
    /// change through a script compile, which reloads the domain.
    /// </para>
    /// </remarks>
    public sealed class AuthoredRequirementRule : IValidationRule
    {
        private readonly List<AuthoredRequirementFinding> _found =
            new List<AuthoredRequirementFinding>();

        private readonly List<AuthoredRequirementExemption> _exemptions =
            new List<AuthoredRequirementExemption>();

        private readonly Dictionary<string, int> _occurrences = new Dictionary<string, int>(
            StringComparer.Ordinal
        );

        private Dictionary<string, List<AuthoredRequirementField>> _fieldsByScriptGuid;
        private bool _indexBuilt;

        /// <inheritdoc />
        public string RuleId => ValidationRuleIds.RequiredFieldEmpty;

        /// <inheritdoc />
        public string DisplayName => "Required fields are filled";

        /// <summary>
        /// The annotated fields this rule looks for, keyed by the script guid that carries them.
        /// </summary>
        /// <remarks>Built on first use; empty when the project annotates nothing.</remarks>
        internal IReadOnlyDictionary<string, List<AuthoredRequirementField>> FieldsByScriptGuid
        {
            get
            {
                BuildIndexOnce();
                return _fieldsByScriptGuid;
            }
        }

        /// <inheritdoc />
        public bool AppliesTo(in ValidationTarget target)
        {
            return AuthoredTextAssets.CarriesAuthoredDocuments(in target);
        }

        /// <inheritdoc />
        public void Validate(
            in ValidationTarget target,
            Object asset,
            List<ValidationFinding> findings
        )
        {
            if (findings == null || !target.IsValid())
            {
                return;
            }

            BuildIndexOnce();
            if (_fieldsByScriptGuid.Count <= 0)
            {
                return;
            }

            if (
                !AuthoredAssetYaml.TryReadDocuments(
                    AuthoredAssetPaths.ToFileSystemPath(target.AssetPath),
                    out IReadOnlyList<string> lines,
                    out IReadOnlyList<AuthoredAssetDocument> documents
                )
            )
            {
                findings.Add(
                    ValidationCoverage.Unreadable(
                        RuleId,
                        in target,
                        "this asset holds no readable Unity document"
                    )
                );
                return;
            }

            _found.Clear();
            _occurrences.Clear();
            AuthoredRequirementValidator.JudgeDocuments(
                target.AssetPath,
                lines,
                documents,
                _fieldsByScriptGuid,
                _found
            );

            foreach (AuthoredRequirementFinding found in _found)
            {
                string field =
                    (
                        found.DeclaringType == null
                            ? string.Empty
                            : found.DeclaringType.FullName + "."
                    ) + found.FieldName;
                findings.Add(
                    new ValidationFinding(
                        RuleId,
                        ValidationSeverity.Error,
                        asset,
                        target.AssetGuid,
                        target.AssetPath,
                        ValidationDiscriminators.Occurrence(_occurrences, field),
                        field
                            + " is required and empty, on line "
                            + found.LineNumber.ToString(CultureInfo.InvariantCulture)
                            + "."
                    )
                );
            }
        }

        private void BuildIndexOnce()
        {
            if (_indexBuilt)
            {
                return;
            }

            _indexBuilt = true;
            _fieldsByScriptGuid = AuthoredRequirementValidator.FieldsByScriptGuid(
                typeof(WNotNullAttribute),
                _exemptions
            );
        }
    }
#endif
}
