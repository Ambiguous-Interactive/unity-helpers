// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Reports every authored <c>SerializableDictionary</c> that will not load as it was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The continuous half of <see cref="SerializableDictionaryAssetValidator"/>, judging one asset
    /// at a time through that validator's own code rather than a second copy of it.
    /// </para>
    /// <para>
    /// <b>Severity is decided per problem rather than per rule</b>, because the three states this
    /// check reports are not equally bad and one severity for all of them would make a severity
    /// floor useless. See <see cref="SeverityOf"/>.
    /// </para>
    /// </remarks>
    public sealed class SerializableDictionaryPairingRule : IValidationRule
    {
        private readonly List<SerializableDictionaryAssetFinding> _found =
            new List<SerializableDictionaryAssetFinding>();

        private readonly Dictionary<string, int> _occurrences = new Dictionary<string, int>(
            StringComparer.Ordinal
        );

        /// <inheritdoc />
        public string RuleId => ValidationRuleIds.DictionaryPairing;

        /// <inheritdoc />
        public string DisplayName => "Serializable dictionaries keep their pairing";

        /// <summary>
        /// How much each authored dictionary state should interrupt a reader.
        /// </summary>
        /// <param name="problem">The state the dictionary is in.</param>
        /// <returns>The severity that state is reported at.</returns>
        /// <remarks>
        /// Dropped values and an unpairable length are <see cref="ValidationSeverity.Error"/>: the
        /// mapping is already gone, the dictionary loads empty, and no lookup against it can
        /// succeed. A null value beside a real key is <see cref="ValidationSeverity.Warning"/>: the
        /// asset is well formed and loads, <c>TryGetValue</c> answers <c>true</c>, and a project
        /// that means it can carry it. Reporting the second as an error would fail builds over a
        /// shape somebody chose.
        /// </remarks>
        public static ValidationSeverity SeverityOf(SerializableDictionaryAssetProblem problem)
        {
            return problem == SerializableDictionaryAssetProblem.NullValueBesideKey
                ? ValidationSeverity.Warning
                : ValidationSeverity.Error;
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
            for (int index = 0; index < documents.Count; ++index)
            {
                SerializableDictionaryAssetValidator.JudgeDocument(
                    target.AssetPath,
                    lines,
                    documents[index],
                    _found
                );
            }

            foreach (SerializableDictionaryAssetFinding found in _found)
            {
                findings.Add(
                    new ValidationFinding(
                        RuleId,
                        SeverityOf(found.Problem),
                        asset,
                        target.AssetGuid,
                        target.AssetPath,
                        ValidationDiscriminators.Occurrence(_occurrences, found.Problem.ToString()),
                        "line "
                            + found.LineNumber.ToString(CultureInfo.InvariantCulture)
                            + ": "
                            + found.Detail
                    )
                );
            }
        }
    }
#endif
}
