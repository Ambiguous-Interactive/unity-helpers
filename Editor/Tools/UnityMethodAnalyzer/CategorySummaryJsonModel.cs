// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System.Text.Json.Serialization;

    internal sealed class CategorySummaryJsonModel
    {
        [JsonPropertyName("unityLifecycle")]
        public int UnityLifecycle { get; set; }

        [JsonPropertyName("unityInheritance")]
        public int UnityInheritance { get; set; }

        [JsonPropertyName("generalInheritance")]
        public int GeneralInheritance { get; set; }
    }
#endif
}
