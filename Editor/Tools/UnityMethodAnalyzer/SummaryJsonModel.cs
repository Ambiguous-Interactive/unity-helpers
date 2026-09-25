// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System.Text.Json.Serialization;

    internal sealed class SummaryJsonModel
    {
        [JsonPropertyName("bySeverity")]
        public SeveritySummaryJsonModel BySeverity { get; set; }

        [JsonPropertyName("byCategory")]
        public CategorySummaryJsonModel ByCategory { get; set; }
    }
#endif
}
