// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System.Text.Json.Serialization;

    internal sealed class IssueJsonModel
    {
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; }

        [JsonPropertyName("lineNumber")]
        public int LineNumber { get; set; }

        [JsonPropertyName("className")]
        public string ClassName { get; set; }

        [JsonPropertyName("methodName")]
        public string MethodName { get; set; }

        [JsonPropertyName("issueType")]
        public string IssueType { get; set; }

        [JsonPropertyName("severity")]
        public string Severity { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("recommendedFix")]
        public string RecommendedFix { get; set; }

        [JsonPropertyName("baseClassName")]
        public string BaseClassName { get; set; }

        [JsonPropertyName("baseMethodSignature")]
        public string BaseMethodSignature { get; set; }

        [JsonPropertyName("derivedMethodSignature")]
        public string DerivedMethodSignature { get; set; }

        public IssueJsonModel() { }

        public IssueJsonModel(AnalyzerIssue issue)
        {
            FilePath = issue.FilePath;
            LineNumber = issue.LineNumber;
            ClassName = issue.ClassName;
            MethodName = issue.MethodName;
            IssueType = issue.IssueType;
            Severity = issue.Severity.ToString();
            Category = issue.Category.ToString();
            Description = issue.Description;
            RecommendedFix = issue.RecommendedFix;
            BaseClassName = issue.BaseClassName;
            BaseMethodSignature = issue.BaseMethodSignature;
            DerivedMethodSignature = issue.DerivedMethodSignature;
        }
    }
#endif
}
