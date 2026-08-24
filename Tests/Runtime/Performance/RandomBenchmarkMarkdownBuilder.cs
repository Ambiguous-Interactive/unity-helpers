// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Random;

    internal static class RandomBenchmarkMarkdownBuilder
    {
        public static List<string> BuildTables(IEnumerable<RandomBenchmarkResult> results)
        {
            if (results == null)
            {
                return BuildEmptyPlaceholder();
            }

            List<RandomBenchmarkResult> orderedResults = results
                .OrderByDescending(r => r.SpeedBucketSortValue)
                .ThenByDescending(r => r.SpeedRatio)
                .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
                .ToList();

            if (orderedResults.Count == 0)
            {
                return BuildEmptyPlaceholder();
            }

            List<string> lines = new() { "## Summary (fastest first)" };

            lines.AddRange(BuildSummaryTable(orderedResults));
            lines.Add(string.Empty);
            lines.Add("## Detailed Metrics");
            lines.AddRange(BuildDetailTable(orderedResults));

            return lines;
        }

        private static IEnumerable<string> BuildSummaryTable(
            IEnumerable<RandomBenchmarkResult> orderedResults
        )
        {
            List<string> lines = new()
            {
                "<table data-sortable>",
                "  <thead>",
                "    <tr>",
                "      <th align=\"left\">Random</th>",
                "      <th align=\"right\">NextUint (ops/s)</th>",
                "      <th align=\"left\">Speed</th>",
                "      <th align=\"left\">Quality</th>",
                "      <th align=\"left\">Notes</th>",
                "    </tr>",
                "  </thead>",
                "  <tbody>",
            };

            foreach (RandomBenchmarkResult result in orderedResults)
            {
                lines.Add(BuildSummaryRow(result));
            }

            lines.Add("  </tbody>");
            lines.Add("</table>");
            return lines;
        }

        private static IEnumerable<string> BuildDetailTable(
            IEnumerable<RandomBenchmarkResult> orderedResults
        )
        {
            List<string> lines = new()
            {
                "<table data-sortable>",
                "  <thead>",
                "    <tr>",
                "      <th align=\"left\">Random</th>",
                "      <th align=\"right\">NextBool</th>",
                "      <th align=\"right\">Next</th>",
                "      <th align=\"right\">NextUint</th>",
                "      <th align=\"right\">NextFloat</th>",
                "      <th align=\"right\">NextDouble</th>",
                "      <th align=\"right\">NextUint (Range)</th>",
                "      <th align=\"right\">NextInt (Range)</th>",
                "    </tr>",
                "  </thead>",
                "  <tbody>",
            };

            foreach (RandomBenchmarkResult result in orderedResults)
            {
                lines.Add(BuildDetailRow(result));
            }

            lines.Add("  </tbody>");
            lines.Add("</table>");
            return lines;
        }

        private static string BuildSummaryRow(RandomBenchmarkResult result)
        {
            string name = HtmlEncode(result.DisplayName);
            string opsPerSecond = result.NextUintPerSecond.ToString(
                "N0",
                CultureInfo.InvariantCulture
            );
            string speedLabel = HtmlEncode(result.SpeedBucketLabel);
            string qualityLabel = HtmlEncode(result.QualityLabel);
            string notes = BuildNotesCell(result.Metadata);

            return $"    <tr>"
                + $"<td>{name}</td>"
                + $"<td align=\"right\">{opsPerSecond}</td>"
                + $"<td data-sort-value=\"{result.SpeedBucketSortValue}\">{speedLabel}</td>"
                + $"<td data-sort-value=\"{result.QualitySortValue}\">{qualityLabel}</td>"
                + $"<td>{notes}</td>"
                + "</tr>";
        }

        private static string BuildDetailRow(RandomBenchmarkResult result)
        {
            string name = HtmlEncode(result.DisplayName);

            return $"    <tr>"
                + $"<td>{name}</td>"
                + $"<td align=\"right\">{Format(result.NextBoolPerSecond)}</td>"
                + $"<td align=\"right\">{Format(result.NextIntPerSecond)}</td>"
                + $"<td align=\"right\">{Format(result.NextUintPerSecond)}</td>"
                + $"<td align=\"right\">{Format(result.NextFloatPerSecond)}</td>"
                + $"<td align=\"right\">{Format(result.NextDoublePerSecond)}</td>"
                + $"<td align=\"right\">{Format(result.NextUintRangePerSecond)}</td>"
                + $"<td align=\"right\">{Format(result.NextIntRangePerSecond)}</td>"
                + "</tr>";

            string Format(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static List<string> BuildEmptyPlaceholder()
        {
            return new List<string>
            {
                "_No benchmark data available yet. Run `RandomPerformanceTests.Benchmark` to populate these tables._",
            };
        }

        private static string BuildNotesCell(RandomGeneratorMetadata metadata)
        {
            StringBuilder builder = new();

            if (!string.IsNullOrWhiteSpace(metadata.Notes))
            {
                builder.Append(HtmlEncode(metadata.Notes));
            }

            if (!string.IsNullOrWhiteSpace(metadata.ReferenceUrl))
            {
                if (0 < builder.Length)
                {
                    builder.Append(' ');
                }

                string label = string.IsNullOrWhiteSpace(metadata.Reference)
                    ? metadata.ReferenceUrl
                    : metadata.Reference;
                builder.Append("<a href=\"");
                builder.Append(HtmlAttributeEncode(metadata.ReferenceUrl));
                builder.Append("\">");
                builder.Append(HtmlEncode(label));
                builder.Append("</a>");
            }

            if (builder.Length == 0)
            {
                return "&mdash;";
            }

            return builder.ToString();
        }

        private static string HtmlEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&#39;", StringComparison.Ordinal);
        }

        private static string HtmlAttributeEncode(string value)
        {
            return HtmlEncode(value);
        }
    }
}
