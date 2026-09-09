// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System.Collections.Generic;

    /// <summary>
    /// Represents the severity level of a detected issue.
    /// </summary>
    public enum IssueSeverity
    {
        Critical = 1,
        High = 2,
        Medium = 3,
        Low = 4,
        Info = 5,
    }

    /// <summary>
    /// Categorizes issues by their context.
    /// </summary>
    public enum IssueCategory
    {
        UnityLifecycle,
        UnityInheritance,
        GeneralInheritance,
    }

    /// <summary>
    /// Represents a detected issue in the codebase.
    /// </summary>
    public sealed class AnalyzerIssue
    {
        public string FilePath { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public string IssueType { get; set; }
        public string Description { get; set; }
        public IssueSeverity Severity { get; set; }
        public string RecommendedFix { get; set; }
        public int LineNumber { get; set; }
        public IssueCategory Category { get; set; }
        public string BaseClassName { get; set; }
        public string BaseMethodSignature { get; set; }
        public string DerivedMethodSignature { get; set; }

        public AnalyzerIssue(
            string filePath,
            string className,
            string methodName,
            string issueType,
            string description,
            IssueSeverity severity,
            string recommendedFix,
            int lineNumber,
            IssueCategory category = IssueCategory.GeneralInheritance,
            string baseClassName = null,
            string baseMethodSignature = null,
            string derivedMethodSignature = null
        )
        {
            FilePath = filePath;
            ClassName = className;
            MethodName = methodName;
            IssueType = issueType;
            Description = description;
            Severity = severity;
            RecommendedFix = recommendedFix;
            LineNumber = lineNumber;
            Category = category;
            BaseClassName = baseClassName;
            BaseMethodSignature = baseMethodSignature;
            DerivedMethodSignature = derivedMethodSignature;
        }
    }

    /// <summary>
    /// Contains information about a method declaration.
    /// </summary>
    public sealed class AnalyzerMethodInfo
    {
        public string Name { get; set; }
        public string Signature { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsOverride { get; set; }
        public bool IsNew { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsProtected { get; set; }
        public bool IsPublic { get; set; }
        public bool IsInternal { get; set; }
        public bool IsStatic { get; set; }
        public int LineNumber { get; set; }
        public string ReturnType { get; set; }
        public List<string> Parameters { get; set; }
        public List<string> ParameterTypes { get; set; }

        /// <summary>
        /// Indicates whether this method is marked with [SuppressAnalyzer] attribute.
        /// Methods marked as suppressed will not generate analyzer warnings.
        /// </summary>
        public bool IsSuppressed { get; set; }

        /// <summary>
        /// Gets the parameter types as a joined string for efficient comparison.
        /// This value is cached after first computation to avoid repeated allocations.
        /// </summary>
        public string ParameterTypesString
        {
            get
            {
                if (_cachedParameterTypesString == null && ParameterTypes != null)
                {
                    _cachedParameterTypesString = string.Join(", ", ParameterTypes);
                }

                return _cachedParameterTypesString ?? string.Empty;
            }
        }

        /// <summary>
        /// Cached joined parameter types string for efficient comparison.
        /// Lazily computed on first access to avoid allocations when not needed.
        /// </summary>
        private string _cachedParameterTypesString;

        public AnalyzerMethodInfo()
        {
            Parameters = new List<string>();
            ParameterTypes = new List<string>();
        }
    }

    /// <summary>
    /// Contains information about a class declaration.
    /// </summary>
    public sealed class AnalyzerClassInfo
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string BaseClassName { get; set; }
        public string FilePath { get; set; }
        public Dictionary<string, List<AnalyzerMethodInfo>> Methods { get; set; }
        public int LineNumber { get; set; }

        /// <summary>
        /// The generic type parameters declared by this class (e.g., ["TKey", "TValue"]).
        /// </summary>
        public List<string> GenericTypeParameters { get; set; }

        /// <summary>
        /// The concrete type arguments provided to the base class (e.g., ["int", "string"]).
        /// Maps positionally to the base class's GenericTypeParameters.
        /// </summary>
        public List<string> BaseClassTypeArguments { get; set; }

        /// <summary>
        /// The full base class declaration including generic arguments (e.g., "WDropDownSelectorBase&lt;int&gt;").
        /// </summary>
        public string BaseClassFullDeclaration { get; set; }

        /// <summary>
        /// The list of interfaces implemented by this class.
        /// </summary>
        public List<string> ImplementedInterfaces { get; set; }

        /// <summary>
        /// Indicates whether this class is marked with [SuppressAnalyzer] attribute.
        /// Classes marked as suppressed will not generate analyzer warnings.
        /// </summary>
        public bool IsSuppressed { get; set; }

        public AnalyzerClassInfo()
        {
            Methods = new Dictionary<string, List<AnalyzerMethodInfo>>();
            GenericTypeParameters = new List<string>();
            BaseClassTypeArguments = new List<string>();
            ImplementedInterfaces = new List<string>();
        }
    }
#endif
}
