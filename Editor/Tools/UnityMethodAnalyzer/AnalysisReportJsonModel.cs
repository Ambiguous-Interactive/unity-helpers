// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    internal sealed class AnalysisReportJsonModel
    {
        [JsonPropertyName("generatedAt")]
        public string GeneratedAt { get; set; }

        [JsonPropertyName("coverageStatus")]
        public string CoverageStatus { get; set; }

        [JsonPropertyName("totalIssues")]
        public int TotalIssues { get; set; }

        [JsonPropertyName("summary")]
        public SummaryJsonModel Summary { get; set; }

        [JsonPropertyName("issues")]
        public List<IssueJsonModel> Issues { get; set; }
    }
#endif
}
