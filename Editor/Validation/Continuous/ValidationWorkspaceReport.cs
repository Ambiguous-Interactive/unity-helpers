// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml;

    internal static class ValidationWorkspaceReport
    {
        internal static string ToJUnit(
            ValidationRun run,
            ValidationSuppressions suppressions,
            ValidationSeverity threshold,
            int workers
        )
        {
            if (run == null)
                throw new ArgumentNullException(nameof(run));
            ValidationSuppressions effective = suppressions ?? ValidationSuppressions.Empty;
            string[] cases = new string[run.Findings.Count];
            bool[] failed = new bool[cases.Length];
            bool[] skipped = new bool[cases.Length];
            Parallel.For(
                0,
                cases.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(32, workers)) },
                index =>
                {
                    ValidationFinding finding = run.Findings[index];
                    skipped[index] = effective.IsSuppressed(in finding);
                    failed[index] = !skipped[index] && threshold <= finding.Severity;
                    StringBuilder text = new StringBuilder();
                    using (
                        XmlWriter writer = XmlWriter.Create(
                            text,
                            new XmlWriterSettings
                            {
                                OmitXmlDeclaration = true,
                                ConformanceLevel = ConformanceLevel.Fragment,
                            }
                        )
                    )
                    {
                        writer.WriteStartElement("testcase");
                        writer.WriteAttributeString("classname", finding.RuleId);
                        writer.WriteAttributeString(
                            "name",
                            finding.AssetPath + "|" + finding.Discriminator
                        );
                        if (skipped[index] || failed[index])
                        {
                            writer.WriteStartElement(skipped[index] ? "skipped" : "failure");
                            writer.WriteAttributeString("message", finding.Message);
                            writer.WriteEndElement();
                        }
                        else
                            writer.WriteElementString("system-out", finding.Message);
                        writer.WriteEndElement();
                    }
                    cases[index] = text.ToString();
                }
            );
            int failures = 0;
            int suppressed = 0;
            foreach (bool value in failed)
                if (value)
                    failures++;
            foreach (bool value in skipped)
                if (value)
                    suppressed++;
            bool incomplete = !run.IsComplete || run.IsCancelled || run.TotalCount == 0;
            int errors = run.Failures.Count + (incomplete ? 1 : 0);
            StringBuilder report = new StringBuilder();
            using (
                XmlWriter writer = XmlWriter.Create(
                    report,
                    new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true }
                )
            )
            {
                writer.WriteStartElement("testsuite");
                writer.WriteAttributeString("name", "Sentinel validation");
                writer.WriteAttributeString(
                    "tests",
                    (cases.Length + errors).ToString(CultureInfo.InvariantCulture)
                );
                writer.WriteAttributeString(
                    "failures",
                    failures.ToString(CultureInfo.InvariantCulture)
                );
                writer.WriteAttributeString(
                    "errors",
                    errors.ToString(CultureInfo.InvariantCulture)
                );
                writer.WriteAttributeString(
                    "skipped",
                    suppressed.ToString(CultureInfo.InvariantCulture)
                );
                foreach (string item in cases)
                    writer.WriteRaw(item);
                for (int index = 0; index < run.Failures.Count; index++)
                    WriteError(writer, run.Failures[index].ToString());
                if (incomplete)
                    WriteError(
                        writer,
                        "The validation run did not complete with nonempty asset coverage."
                    );
                writer.WriteStartElement("system-out");
                writer.WriteString(
                    "Assets processed: " + run.ProcessedCount + " / " + run.TotalCount
                );
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            return report.ToString();
        }

        private static void WriteError(XmlWriter writer, string message)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("name", "Validation coverage");
            writer.WriteElementString("error", message);
            writer.WriteEndElement();
        }
    }
#endif
}
