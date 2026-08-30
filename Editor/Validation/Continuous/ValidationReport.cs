// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// A finished <see cref="ValidationRun"/> rendered as JSON, for a build that has to decide
    /// something about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape is flat and every field is a string or a number, because the consumer is a CI step
    /// or another tool rather than this assembly. <see cref="SchemaVersion"/> is written into the
    /// document so a consumer can tell an older report from a newer one instead of guessing from
    /// which fields happen to be present.
    /// </para>
    /// <para>
    /// Suppressed findings are written with <c>suppressed</c> set rather than dropped. A report
    /// that silently omitted them would make a suppression file indistinguishable from a project
    /// that had no findings, which is the difference somebody reviewing the file needs to see.
    /// </para>
    /// <para>
    /// Rendered through <c>JsonUtility</c>, the same way every other editor tool here writes JSON,
    /// so escaping is Unity's problem rather than a hand-rolled writer's.
    /// </para>
    /// </remarks>
    public static class ValidationReport
    {
        /// <summary>The schema version written into every document this produces.</summary>
        public const int SchemaVersion = 1;

        /// <summary>
        /// Renders a run.
        /// </summary>
        /// <param name="run">The run to render; <c>null</c> yields an empty report.</param>
        /// <param name="suppressions">
        /// What the project has decided not to be told about; <c>null</c> suppresses nothing.
        /// </param>
        /// <param name="prettyPrint">Whether to indent the document.</param>
        /// <returns>The JSON document; never <c>null</c>.</returns>
        public static string ToJson(
            ValidationRun run,
            ValidationSuppressions suppressions,
            bool prettyPrint = true
        )
        {
            ValidationSuppressions effective = suppressions ?? ValidationSuppressions.Empty;
            Document document = new Document
            {
                schemaVersion = SchemaVersion,
                assetsConsidered = run == null ? 0 : run.TotalCount,
                assetsProcessed = run == null ? 0 : run.ProcessedCount,
                complete = run != null && run.IsComplete && !run.IsCancelled,
                cancelled = run != null && run.IsCancelled,
            };

            IReadOnlyList<ValidationFinding> findings =
                run == null ? Array.Empty<ValidationFinding>() : run.Findings;
            for (int index = 0; index < findings.Count; index++)
            {
                ValidationFinding finding = findings[index];
                bool suppressed = effective.IsSuppressed(finding);
                document.findings.Add(
                    new FindingRecord
                    {
                        id = finding.Id,
                        ruleId = finding.RuleId,
                        severity = finding.Severity.ToString(),
                        assetGuid = finding.AssetGuid,
                        assetPath = finding.AssetPath,
                        discriminator = finding.Discriminator,
                        message = finding.Message,
                        suppressed = suppressed,
                    }
                );

                if (!suppressed)
                {
                    document.unsuppressedCount++;
                }
            }

            IReadOnlyList<ValidationRuleFailure> failures =
                run == null ? Array.Empty<ValidationRuleFailure>() : run.Failures;
            for (int index = 0; index < failures.Count; index++)
            {
                ValidationRuleFailure failure = failures[index];
                document.failures.Add(
                    new FailureRecord
                    {
                        // A load failure has no rule to blame, and the empty string a JSON reader
                        // sees for a null is indistinguishable from an unnamed rule -- so the
                        // report states which it was rather than leaving it to be inferred.
                        ruleId = failure.RuleId,
                        loadFailure = failure.IsLoadFailure,
                        assetPath = failure.AssetPath,
                        exception =
                            failure.Exception == null ? string.Empty : failure.Exception.ToString(),
                    }
                );
            }

            IReadOnlyList<string> unused = effective.UnusedIn(findings);
            for (int index = 0; index < unused.Count; index++)
            {
                document.unusedSuppressions.Add(unused[index]);
            }

            return JsonUtility.ToJson(document, prettyPrint);
        }

        /// <summary>
        /// Reports whether a run produced anything at or above a severity that is not suppressed.
        /// </summary>
        /// <param name="run">The run to inspect; <c>null</c> counts as nothing found.</param>
        /// <param name="suppressions">What to ignore; <c>null</c> suppresses nothing.</param>
        /// <param name="threshold">The lowest severity that counts.</param>
        /// <returns><c>true</c> when at least one finding at or above <paramref name="threshold"/> stands.</returns>
        /// <remarks>
        /// A rule that threw counts, whatever the threshold. It produced no answer for that asset,
        /// which is not the same as answering "nothing wrong", so a build that passed on it would
        /// be reporting coverage it does not have.
        /// </remarks>
        public static bool HasBlockingResults(
            ValidationRun run,
            ValidationSuppressions suppressions,
            ValidationSeverity threshold
        )
        {
            if (run == null)
            {
                return false;
            }

            if (0 < run.Failures.Count)
            {
                return true;
            }

            ValidationSuppressions effective = suppressions ?? ValidationSuppressions.Empty;
            IReadOnlyList<ValidationFinding> findings = run.Findings;
            for (int index = 0; index < findings.Count; index++)
            {
                ValidationFinding finding = findings[index];
                if (threshold <= finding.Severity && !effective.IsSuppressed(finding))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One finding, as the report writes it.</summary>
        [Serializable]
        public sealed class FindingRecord
        {
            /// <summary>The finding's identity across runs.</summary>
            public string id;

            /// <summary>The reporting rule's stable identifier.</summary>
            public string ruleId;

            /// <summary>The severity's name, so the document reads without a lookup table.</summary>
            public string severity;

            /// <summary>The GUID of the asset the finding belongs to.</summary>
            public string assetGuid;

            /// <summary>The asset's project-relative path as of this run.</summary>
            public string assetPath;

            /// <summary>What tells this finding apart from the rule's others on the same asset.</summary>
            public string discriminator;

            /// <summary>The human-readable description.</summary>
            public string message;

            /// <summary>Whether the project's suppression file silences this finding.</summary>
            public bool suppressed;
        }

        /// <summary>One rule or loader that threw, as the report writes it.</summary>
        [Serializable]
        public sealed class FailureRecord
        {
            /// <summary>The rule that threw, empty when the asset itself failed to load.</summary>
            public string ruleId;

            /// <summary>Whether loading the asset threw, rather than a rule.</summary>
            public bool loadFailure;

            /// <summary>The asset it was validating.</summary>
            public string assetPath;

            /// <summary>What it threw.</summary>
            public string exception;
        }

        /// <summary>The whole document.</summary>
        [Serializable]
        public sealed class Document
        {
            /// <summary>The schema this document follows.</summary>
            public int schemaVersion;

            /// <summary>How many assets the run considered.</summary>
            public int assetsConsidered;

            /// <summary>How many it got through.</summary>
            public int assetsProcessed;

            /// <summary>Whether it reached the end without being cancelled.</summary>
            public bool complete;

            /// <summary>Whether it was cancelled before finishing.</summary>
            public bool cancelled;

            /// <summary>How many findings the suppression file does not silence.</summary>
            public int unsuppressedCount;

            /// <summary>Every finding, suppressed ones included and marked.</summary>
            public List<FindingRecord> findings = new List<FindingRecord>();

            /// <summary>Every rule or loader that threw.</summary>
            public List<FailureRecord> failures = new List<FailureRecord>();

            /// <summary>Suppression entries that silenced nothing in this run.</summary>
            public List<string> unusedSuppressions = new List<string>();
        }
    }
#endif
}
