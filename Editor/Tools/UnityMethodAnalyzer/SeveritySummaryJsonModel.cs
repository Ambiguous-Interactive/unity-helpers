// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System.Text.Json.Serialization;

    internal sealed class SeveritySummaryJsonModel
    {
        [JsonPropertyName("critical")]
        public int Critical { get; set; }

        [JsonPropertyName("high")]
        public int High { get; set; }

        [JsonPropertyName("medium")]
        public int Medium { get; set; }

        [JsonPropertyName("low")]
        public int Low { get; set; }

        [JsonPropertyName("info")]
        public int Info { get; set; }
    }
#endif
}
