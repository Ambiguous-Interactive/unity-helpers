// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using UnityEditor.IMGUI.Controls;
    using UnityEngine;
#if !UNITY_2021 && !UNITY_2022 && !UNITY_2023
    using UnityMethodAnalyzerTreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
    using UnityMethodAnalyzerTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
    using UnityMethodAnalyzerTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
#else
    using UnityMethodAnalyzerTreeView = UnityEditor.IMGUI.Controls.TreeView;
    using UnityMethodAnalyzerTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem;
    using UnityMethodAnalyzerTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
#endif

    /// <summary>
    /// TreeView item for displaying analyzer issues.
    /// </summary>
    internal sealed class IssueTreeViewItem : UnityMethodAnalyzerTreeViewItem
    {
        public AnalyzerIssue Issue { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public bool IsFile { get; }
        public bool IsCategory { get; }
        public IssueSeverity? Severity { get; }
        public int IssueCount { get; set; }
        public int CriticalCount { get; set; }
        public int HighCount { get; set; }

        public IssueTreeViewItem(
            int id,
            int depth,
            string displayName,
            AnalyzerIssue issue = null,
            string filePath = null,
            int lineNumber = 0,
            bool isFile = false,
            bool isCategory = false,
            IssueSeverity? severity = null
        )
            : base(id, depth, displayName)
        {
            Issue = issue;
            FilePath = filePath;
            LineNumber = lineNumber;
            IsFile = isFile;
            IsCategory = isCategory;
            Severity = severity;
        }
    }

    /// <summary>
    /// TreeView for displaying analyzer issues in a hierarchical format.
    /// </summary>
    internal sealed class IssueTreeView : UnityMethodAnalyzerTreeView
    {
        private IReadOnlyList<AnalyzerIssue> _issues;
        private string _rootPath;
        private bool _groupByFile = true;
        private bool _groupBySeverity;
        private bool _groupByCategory;
        private IssueSeverity? _severityFilter;
        private IssueCategory? _categoryFilter;
        private string _searchFilter;

        public event Action<AnalyzerIssue> OnIssueSelected;
        public event Action<string, int> OnOpenFile;
        public event Action<string> OnRevealInExplorer;
        public event Action<AnalyzerIssue> OnCopyIssueAsJson;
        public event Action<AnalyzerIssue> OnCopyIssueAsMarkdown;
        public event Action OnCopyAllAsJson;
        public event Action OnCopyAllAsMarkdown;

        public IssueTreeView(UnityMethodAnalyzerTreeViewState state)
            : base(state)
        {
            _issues = Array.Empty<AnalyzerIssue>();
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            Reload();
        }

        public void SetIssues(IReadOnlyList<AnalyzerIssue> issues, string rootPath)
        {
            _issues = issues ?? Array.Empty<AnalyzerIssue>();
            _rootPath = rootPath;
            Reload();
        }

        public void SetGrouping(bool byFile, bool bySeverity, bool byCategory)
        {
            _groupByFile = byFile;
            _groupBySeverity = bySeverity;
            _groupByCategory = byCategory;
            Reload();
        }

        public void SetFilters(IssueSeverity? severity, IssueCategory? category, string search)
        {
            _severityFilter = severity;
            _categoryFilter = category;
            _searchFilter = search;
            Reload();
        }

        protected override UnityMethodAnalyzerTreeViewItem BuildRoot()
        {
            UnityMethodAnalyzerTreeViewItem root = new(-1, -1, "Root");

            if (_issues == null || _issues.Count == 0)
            {
                root.AddChild(new UnityMethodAnalyzerTreeViewItem(0, 0, "No issues found"));
                return root;
            }

            // Build filtered list in a single pass to reduce allocations
            List<AnalyzerIssue> issueList = BuildFilteredIssueList();

            if (issueList.Count == 0)
            {
                root.AddChild(
                    new UnityMethodAnalyzerTreeViewItem(0, 0, "No issues match the current filters")
                );
                return root;
            }

            int id = 1;

            if (_groupBySeverity)
            {
                BuildTreeBySeverity(root, issueList, ref id);
            }
            else if (_groupByCategory)
            {
                BuildTreeByCategory(root, issueList, ref id);
            }
            else if (_groupByFile)
            {
                BuildTreeByFile(root, issueList, ref id);
            }
            else
            {
                BuildFlatTree(root, issueList, ref id);
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        /// <summary>
        /// Builds a filtered issue list in a single pass to reduce allocations.
        /// Uses case-insensitive comparison instead of ToLowerInvariant() to avoid string allocations.
        /// </summary>
        private List<AnalyzerIssue> BuildFilteredIssueList()
        {
            bool hasSeverityFilter = _severityFilter.HasValue;
            bool hasCategoryFilter = _categoryFilter.HasValue;
            bool hasSearchFilter = !string.IsNullOrWhiteSpace(_searchFilter);

            // If no filters, return a copy of the list
            if (!hasSeverityFilter && !hasCategoryFilter && !hasSearchFilter)
            {
                return new List<AnalyzerIssue>(_issues);
            }

            // Pre-allocate with estimated capacity
            List<AnalyzerIssue> filtered = new(_issues.Count);

            foreach (AnalyzerIssue issue in _issues)
            {
                // Apply severity filter
                if (hasSeverityFilter && issue.Severity != _severityFilter.Value)
                {
                    continue;
                }

                // Apply category filter
                if (hasCategoryFilter && issue.Category != _categoryFilter.Value)
                {
                    continue;
                }

                // Apply search filter using IndexOf with OrdinalIgnoreCase to avoid string allocations
                if (hasSearchFilter)
                {
                    bool matchesSearch =
                        issue.ClassName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase)
                            >= 0
                        || issue.MethodName.IndexOf(
                            _searchFilter,
                            StringComparison.OrdinalIgnoreCase
                        ) >= 0
                        || issue.IssueType.IndexOf(
                            _searchFilter,
                            StringComparison.OrdinalIgnoreCase
                        ) >= 0
                        || issue.FilePath.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase)
                            >= 0
                        || issue.Description.IndexOf(
                            _searchFilter,
                            StringComparison.OrdinalIgnoreCase
                        ) >= 0;

                    if (!matchesSearch)
                    {
                        continue;
                    }
                }

                filtered.Add(issue);
            }

            return filtered;
        }

        private void BuildTreeBySeverity(
            UnityMethodAnalyzerTreeViewItem root,
            List<AnalyzerIssue> issues,
            ref int id
        )
        {
            // Build grouped structure in a single pass using dictionary
            Dictionary<IssueSeverity, Dictionary<string, List<AnalyzerIssue>>> severityGroups =
                new();

            foreach (AnalyzerIssue issue in issues)
            {
                if (
                    !severityGroups.TryGetValue(
                        issue.Severity,
                        out Dictionary<string, List<AnalyzerIssue>> fileGroups
                    )
                )
                {
                    fileGroups = new Dictionary<string, List<AnalyzerIssue>>();
                    severityGroups[issue.Severity] = fileGroups;
                }

                fileGroups.GetOrAdd(issue.FilePath).Add(issue);
            }

            // Build tree from grouped data
            foreach (
                IssueSeverity severity in new[]
                {
                    IssueSeverity.Critical,
                    IssueSeverity.High,
                    IssueSeverity.Medium,
                    IssueSeverity.Low,
                    IssueSeverity.Info,
                }
            )
            {
                if (
                    !severityGroups.TryGetValue(
                        severity,
                        out Dictionary<string, List<AnalyzerIssue>> fileGroups
                    )
                )
                {
                    continue;
                }

                int totalCount = 0;
                foreach (List<AnalyzerIssue> fileIssues in fileGroups.Values)
                {
                    totalCount += fileIssues.Count;
                }

                string severityName = GetSeverityDisplayName(severity);
                IssueTreeViewItem severityItem = new(
                    id++,
                    0,
                    $"{severityName} ({totalCount})",
                    isCategory: true,
                    severity: severity
                )
                {
                    IssueCount = totalCount,
                };

                List<string> sortedFilePaths = new(fileGroups.Keys);
                sortedFilePaths.Sort(StringComparer.Ordinal);

                foreach (string filePath in sortedFilePaths)
                {
                    List<AnalyzerIssue> fileIssues = fileGroups[filePath];
                    int criticalCount = 0;
                    int highCount = 0;
                    foreach (AnalyzerIssue issue in fileIssues)
                    {
                        if (issue.Severity == IssueSeverity.Critical)
                        {
                            criticalCount++;
                        }
                        else if (issue.Severity == IssueSeverity.High)
                        {
                            highCount++;
                        }
                    }

                    IssueTreeViewItem fileItem = new(
                        id++,
                        1,
                        $"{filePath} ({fileIssues.Count})",
                        filePath: filePath,
                        isFile: true
                    )
                    {
                        IssueCount = fileIssues.Count,
                        CriticalCount = criticalCount,
                        HighCount = highCount,
                    };

                    // Sort by line number
                    fileIssues.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));

                    foreach (AnalyzerIssue issue in fileIssues)
                    {
                        string display = FormatIssueDisplay(issue);
                        IssueTreeViewItem issueItem = new(
                            id++,
                            2,
                            display,
                            issue,
                            issue.FilePath,
                            issue.LineNumber,
                            severity: issue.Severity
                        );
                        fileItem.AddChild(issueItem);
                    }

                    severityItem.AddChild(fileItem);
                }

                root.AddChild(severityItem);
            }
        }

        private void BuildTreeByCategory(
            UnityMethodAnalyzerTreeViewItem root,
            List<AnalyzerIssue> issues,
            ref int id
        )
        {
            // Build grouped structure in a single pass using dictionary
            Dictionary<IssueCategory, Dictionary<string, List<AnalyzerIssue>>> categoryGroups =
                new();

            foreach (AnalyzerIssue issue in issues)
            {
                if (
                    !categoryGroups.TryGetValue(
                        issue.Category,
                        out Dictionary<string, List<AnalyzerIssue>> fileGroups
                    )
                )
                {
                    fileGroups = new Dictionary<string, List<AnalyzerIssue>>();
                    categoryGroups[issue.Category] = fileGroups;
                }

                fileGroups.GetOrAdd(issue.FilePath).Add(issue);
            }

            // Build tree from grouped data
            foreach (
                IssueCategory category in new[]
                {
                    IssueCategory.UnityLifecycle,
                    IssueCategory.UnityInheritance,
                    IssueCategory.GeneralInheritance,
                }
            )
            {
                if (
                    !categoryGroups.TryGetValue(
                        category,
                        out Dictionary<string, List<AnalyzerIssue>> fileGroups
                    )
                )
                {
                    continue;
                }

                int totalCount = 0;
                int totalCritical = 0;
                int totalHigh = 0;
                foreach (List<AnalyzerIssue> fileIssues in fileGroups.Values)
                {
                    foreach (AnalyzerIssue issue in fileIssues)
                    {
                        totalCount++;
                        if (issue.Severity == IssueSeverity.Critical)
                        {
                            totalCritical++;
                        }
                        else if (issue.Severity == IssueSeverity.High)
                        {
                            totalHigh++;
                        }
                    }
                }

                string categoryName = GetCategoryDisplayName(category);
                IssueTreeViewItem categoryItem = new(
                    id++,
                    0,
                    $"{categoryName} ({totalCount})",
                    isCategory: true
                )
                {
                    IssueCount = totalCount,
                    CriticalCount = totalCritical,
                    HighCount = totalHigh,
                };

                List<string> sortedFilePaths = new(fileGroups.Keys);
                sortedFilePaths.Sort(StringComparer.Ordinal);

                foreach (string filePath in sortedFilePaths)
                {
                    List<AnalyzerIssue> fileIssues = fileGroups[filePath];
                    int criticalCount = 0;
                    int highCount = 0;
                    foreach (AnalyzerIssue issue in fileIssues)
                    {
                        if (issue.Severity == IssueSeverity.Critical)
                        {
                            criticalCount++;
                        }
                        else if (issue.Severity == IssueSeverity.High)
                        {
                            highCount++;
                        }
                    }

                    IssueTreeViewItem fileItem = new(
                        id++,
                        1,
                        $"{filePath} ({fileIssues.Count})",
                        filePath: filePath,
                        isFile: true
                    )
                    {
                        IssueCount = fileIssues.Count,
                        CriticalCount = criticalCount,
                        HighCount = highCount,
                    };

                    // Sort by line number
                    fileIssues.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));

                    foreach (AnalyzerIssue issue in fileIssues)
                    {
                        string display = FormatIssueDisplay(issue);
                        IssueTreeViewItem issueItem = new(
                            id++,
                            2,
                            display,
                            issue,
                            issue.FilePath,
                            issue.LineNumber,
                            severity: issue.Severity
                        );
                        fileItem.AddChild(issueItem);
                    }

                    categoryItem.AddChild(fileItem);
                }

                root.AddChild(categoryItem);
            }
        }

        private void BuildTreeByFile(
            UnityMethodAnalyzerTreeViewItem root,
            List<AnalyzerIssue> issues,
            ref int id
        )
        {
            // Build grouped structure with counts in a single pass
            Dictionary<string, (List<AnalyzerIssue> issues, int critical, int high)> fileGroups =
                new();

            foreach (AnalyzerIssue issue in issues)
            {
                if (
                    !fileGroups.TryGetValue(
                        issue.FilePath,
                        out (List<AnalyzerIssue> issues, int critical, int high) group
                    )
                )
                {
                    group = (new List<AnalyzerIssue>(), 0, 0);
                }

                group.issues.Add(issue);
                if (issue.Severity == IssueSeverity.Critical)
                {
                    group.critical++;
                }
                else if (issue.Severity == IssueSeverity.High)
                {
                    group.high++;
                }

                fileGroups[issue.FilePath] = group;
            }

            // Sort file paths by critical count desc, high count desc, then alphabetically
            List<string> sortedFilePaths = new(fileGroups.Keys);
            sortedFilePaths.Sort(
                (a, b) =>
                {
                    (List<AnalyzerIssue> _, int critical, int high) groupA = fileGroups[a];
                    (List<AnalyzerIssue> _, int critical, int high) groupB = fileGroups[b];

                    int criticalCompare = groupB.critical.CompareTo(groupA.critical);
                    if (criticalCompare != 0)
                    {
                        return criticalCompare;
                    }

                    int highCompare = groupB.high.CompareTo(groupA.high);
                    if (highCompare != 0)
                    {
                        return highCompare;
                    }

                    return string.Compare(a, b, StringComparison.Ordinal);
                }
            );

            foreach (string filePath in sortedFilePaths)
            {
                (List<AnalyzerIssue> fileIssues, int criticalCount, int highCount) = fileGroups[
                    filePath
                ];

                string prefix =
                    criticalCount > 0 ? "🔴 "
                    : highCount > 0 ? "🟠 "
                    : "🟡 ";

                IssueTreeViewItem fileItem = new(
                    id++,
                    0,
                    $"{prefix}{filePath} ({fileIssues.Count})",
                    filePath: filePath,
                    isFile: true
                )
                {
                    IssueCount = fileIssues.Count,
                    CriticalCount = criticalCount,
                    HighCount = highCount,
                };

                // Sort by line number
                fileIssues.Sort((a, b) => a.LineNumber.CompareTo(b.LineNumber));

                foreach (AnalyzerIssue issue in fileIssues)
                {
                    string display = FormatIssueDisplay(issue);
                    IssueTreeViewItem issueItem = new(
                        id++,
                        1,
                        display,
                        issue,
                        issue.FilePath,
                        issue.LineNumber,
                        severity: issue.Severity
                    );
                    fileItem.AddChild(issueItem);
                }

                root.AddChild(fileItem);
            }
        }

        private void BuildFlatTree(
            UnityMethodAnalyzerTreeViewItem root,
            List<AnalyzerIssue> issues,
            ref int id
        )
        {
            // Sort in-place to avoid creating new collection
            issues.Sort(
                (a, b) =>
                {
                    int severityCompare = ((int)a.Severity).CompareTo((int)b.Severity);
                    if (severityCompare != 0)
                    {
                        return severityCompare;
                    }

                    int fileCompare = string.Compare(
                        a.FilePath,
                        b.FilePath,
                        StringComparison.Ordinal
                    );
                    if (fileCompare != 0)
                    {
                        return fileCompare;
                    }

                    return a.LineNumber.CompareTo(b.LineNumber);
                }
            );

            foreach (AnalyzerIssue issue in issues)
            {
                string display =
                    $"{FormatIssueDisplay(issue)} - {issue.FilePath}:{issue.LineNumber}";
                IssueTreeViewItem issueItem = new(
                    id++,
                    0,
                    display,
                    issue,
                    issue.FilePath,
                    issue.LineNumber,
                    severity: issue.Severity
                );
                root.AddChild(issueItem);
            }
        }

        private static string FormatIssueDisplay(AnalyzerIssue issue)
        {
            string severityEmoji = issue.Severity switch
            {
                IssueSeverity.Critical => "🔴",
                IssueSeverity.High => "🟠",
                IssueSeverity.Medium => "🟡",
                IssueSeverity.Low => "🟢",
                IssueSeverity.Info => "🔵",
                _ => "⚪",
            };

            return $"{severityEmoji} Line {issue.LineNumber}: {issue.ClassName}.{issue.MethodName} - {issue.IssueType}";
        }

        private static string GetSeverityDisplayName(IssueSeverity severity)
        {
            return severity switch
            {
                IssueSeverity.Critical => "🔴 Critical",
                IssueSeverity.High => "🟠 High",
                IssueSeverity.Medium => "🟡 Medium",
                IssueSeverity.Low => "🟢 Low",
                IssueSeverity.Info => "🔵 Info",
                _ => "Unknown",
            };
        }

        private static string GetCategoryDisplayName(IssueCategory category)
        {
            return category switch
            {
                IssueCategory.UnityLifecycle => "🎮 Unity Lifecycle",
                IssueCategory.UnityInheritance => "🔷 Unity Inheritance",
                IssueCategory.GeneralInheritance => "📦 General Inheritance",
                _ => "Unknown",
            };
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            IssueTreeViewItem item = args.item as IssueTreeViewItem;

            Rect contentRect = args.rowRect;
            contentRect.xMin += GetContentIndent(args.item);

            if (item?.Issue != null)
            {
                GUIStyle style = new(EditorStyles.label);

                Color textColor = item.Severity switch
                {
                    IssueSeverity.Critical => new Color(1f, 0.3f, 0.3f),
                    IssueSeverity.High => new Color(1f, 0.6f, 0.2f),
                    IssueSeverity.Medium => new Color(1f, 0.9f, 0.2f),
                    IssueSeverity.Low => new Color(0.5f, 0.9f, 0.5f),
                    _ => Color.white,
                };

                style.normal.textColor = textColor;
                GUI.Label(contentRect, args.item.displayName, style);
            }
            else if (item?.IsFile == true)
            {
                GUIStyle style = new(EditorStyles.boldLabel);

                if (item.CriticalCount > 0)
                {
                    style.normal.textColor = new Color(1f, 0.5f, 0.5f);
                }
                else if (item.HighCount > 0)
                {
                    style.normal.textColor = new Color(1f, 0.7f, 0.4f);
                }

                GUI.Label(contentRect, args.item.displayName, style);
            }
            else
            {
                base.RowGUI(args);
            }
        }

        protected override void SingleClickedItem(int id)
        {
            UnityMethodAnalyzerTreeViewItem item = FindItem(id, rootItem);
            if (item is IssueTreeViewItem issueItem)
            {
                if (issueItem.Issue != null)
                {
                    OnIssueSelected?.Invoke(issueItem.Issue);
                }
            }
        }

        protected override void DoubleClickedItem(int id)
        {
            UnityMethodAnalyzerTreeViewItem item = FindItem(id, rootItem);
            if (item is IssueTreeViewItem issueItem)
            {
                string filePath = issueItem.FilePath;
                int lineNumber = issueItem.LineNumber;

                if (issueItem.Issue != null)
                {
                    filePath = issueItem.Issue.FilePath;
                    lineNumber = issueItem.Issue.LineNumber;
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    OnOpenFile?.Invoke(filePath, lineNumber);
                }
            }
        }

        protected override void ContextClickedItem(int id)
        {
            UnityMethodAnalyzerTreeViewItem item = FindItem(id, rootItem);
            if (item is IssueTreeViewItem issueItem)
            {
                string filePath = issueItem.FilePath;
                AnalyzerIssue issue = issueItem.Issue;
                if (issue != null)
                {
                    filePath = issue.FilePath;
                }

                GenericMenu menu = new();

                if (!string.IsNullOrEmpty(filePath))
                {
                    menu.AddItem(
                        new GUIContent("Open File"),
                        false,
                        () =>
                        {
                            int lineNumber = issueItem.LineNumber;
                            if (issue != null)
                            {
                                lineNumber = issue.LineNumber;
                            }

                            OnOpenFile?.Invoke(filePath, lineNumber);
                        }
                    );
                    menu.AddItem(
                        new GUIContent("Reveal in File Browser"),
                        false,
                        () => OnRevealInExplorer?.Invoke(filePath)
                    );
                }

                if (issue != null)
                {
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        menu.AddSeparator("");
                    }

                    menu.AddItem(
                        new GUIContent("Copy Issue as JSON"),
                        false,
                        () => OnCopyIssueAsJson?.Invoke(issue)
                    );
                    menu.AddItem(
                        new GUIContent("Copy Issue as Markdown"),
                        false,
                        () => OnCopyIssueAsMarkdown?.Invoke(issue)
                    );
                }

                menu.AddSeparator("");
                menu.AddItem(
                    new GUIContent("Copy All Issues as JSON"),
                    false,
                    () => OnCopyAllAsJson?.Invoke()
                );
                menu.AddItem(
                    new GUIContent("Copy All Issues as Markdown"),
                    false,
                    () => OnCopyAllAsMarkdown?.Invoke()
                );

                menu.ShowAsContext();
            }
        }
    }
#endif
}
