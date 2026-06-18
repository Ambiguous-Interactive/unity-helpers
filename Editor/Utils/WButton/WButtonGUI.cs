// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    public enum WButtonPlacement
    {
        Top = 0,
        Bottom = 1,
    }

    /// <summary>
    /// Compound key for grouping WButtons by group priority, draw order, and group name.
    /// This allows multiple groups with different names to render separately with controlled ordering.
    /// </summary>
    internal readonly struct WButtonGroupKey
        : IEquatable<WButtonGroupKey>,
            IComparable<WButtonGroupKey>
    {
        internal readonly int _groupPriority;
        internal readonly int _drawOrder;
        internal readonly string _groupName;
        internal readonly int _declarationOrder;
        internal readonly WButtonGroupPlacement _groupPlacement;

        internal WButtonGroupKey(
            int groupPriority,
            int drawOrder,
            string groupName,
            int declarationOrder,
            WButtonGroupPlacement groupPlacement
        )
        {
            _groupPriority = groupPriority;
            _drawOrder = drawOrder;
            _groupName = groupName ?? string.Empty;
            _declarationOrder = declarationOrder;
            _groupPlacement = groupPlacement;
        }

        public bool Equals(WButtonGroupKey other)
        {
            return _groupPriority == other._groupPriority
                && _drawOrder == other._drawOrder
                && _declarationOrder == other._declarationOrder
                && _groupPlacement == other._groupPlacement
                && string.Equals(_groupName, other._groupName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is WButtonGroupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(
                _groupPriority,
                _drawOrder,
                _declarationOrder,
                _groupPlacement,
                _groupName
            );
        }

        public int CompareTo(WButtonGroupKey other)
        {
            // First compare by group priority (lower values first, NoGroupPriority sorts last)
            int priorityComparison = _groupPriority.CompareTo(other._groupPriority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            // Then compare by draw order (lower values first)
            int drawOrderComparison = _drawOrder.CompareTo(other._drawOrder);
            if (drawOrderComparison != 0)
            {
                return drawOrderComparison;
            }

            // Finally by declaration order to preserve source code order
            return _declarationOrder.CompareTo(other._declarationOrder);
        }
    }

    internal static class WButtonGUI
    {
        private static readonly Dictionary<WButtonGroupKey, int> GroupCounts = new();
        private static readonly Dictionary<WButtonGroupKey, string> GroupNames = new();
        private static readonly Dictionary<WButtonGroupKey, AnimBool> FoldoutAnimations = new();
        private static readonly Dictionary<WButtonGroupKey, GUIContent> GroupHeaderCache = new();
        private static readonly Dictionary<(string, int), string> GroupHeaderTextCache = new();
        private static readonly GUIContent ClearHistoryContent = new("Clear History");
        private static readonly GUIContent RecentResultsHeaderContent = new("Recent Results");

        private static readonly SortedDictionary<
            WButtonGroupKey,
            List<WButtonMethodContext>
        > ReusableGroups = new();
        private static readonly Dictionary<
            WButtonGroupKey,
            PooledResource<List<WButtonMethodContext>>
        > ReusableGroupLeases = new();
        private static readonly Dictionary<string, GUIContent> ButtonDisplayNameCache = new(
            StringComparer.Ordinal
        );
        private const string RunningLabel = "Running...";

        private const float ClearHistoryButtonPadding = 12f;
        private const float ClearHistoryMinWidth = 96f;
        private const float ClearHistorySpacing = 6f;

        /// <summary>
        /// Gets a cached pagination label. Delegates to <see cref="EditorCacheHelper.GetPaginationLabel"/>.
        /// </summary>
        private static string GetPaginationLabel(int page, int totalPages)
        {
            return EditorCacheHelper.GetPaginationLabel(page, totalPages);
        }

        /// <summary>
        /// Gets a cached string representation of an integer.
        /// Delegates to <see cref="EditorCacheHelper.GetCachedIntString"/>.
        /// </summary>
        /// <param name="value">The integer value to convert.</param>
        /// <returns>A cached string representation of the integer.</returns>
        private static string GetCachedIntString(int value)
        {
            return EditorCacheHelper.GetCachedIntString(value);
        }

        private static readonly Dictionary<int, string> RunningLabelByCountCache = new();

        private static string GetRunningLabel(int count)
        {
            if (count == 1)
            {
                return RunningLabel;
            }
            return RunningLabelByCountCache.GetOrAdd(
                count,
                c => "Running (" + GetCachedIntString(c) + ")"
            );
        }

        internal static bool DrawButtons(
            Editor editor,
            WButtonPlacement placement,
            IDictionary<WButtonGroupKey, WButtonPaginationState> paginationStates,
            IDictionary<WButtonGroupKey, bool> foldoutStates,
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior,
            List<WButtonMethodContext> triggeredContexts = null,
            bool globalPlacementIsTop = true
        )
        {
            if (editor == null)
            {
                return false;
            }

            UnityEngine.Object[] targets = editor.targets;
            if (targets == null || targets.Length == 0 || targets[0] == null)
            {
                return false;
            }

            Type inspectedType = targets[0].GetType();
            IReadOnlyList<WButtonMethodMetadata> metadataList = WButtonMetadataCache.GetMetadata(
                inspectedType
            );
            if (metadataList.Count == 0)
            {
                return false;
            }

            using PooledResource<List<WButtonMethodContext>> contextsLease =
                Buffers<WButtonMethodContext>.GetList(
                    metadataList.Count,
                    out List<WButtonMethodContext> contexts
                );
            BuildContexts(metadataList, targets, contexts);
            if (contexts.Count == 0)
            {
                return false;
            }

            SortedDictionary<WButtonGroupKey, List<WButtonMethodContext>> groups = ReusableGroups;
            Dictionary<WButtonGroupKey, PooledResource<List<WButtonMethodContext>>> groupLeases =
                ReusableGroupLeases;
            GroupByDrawOrderAndGroupName(contexts, groups, groupLeases);

            try
            {
                bool anyDrawn = false;
                GroupCounts.Clear();
                GroupNames.Clear();
                foreach (KeyValuePair<WButtonGroupKey, List<WButtonMethodContext>> entry in groups)
                {
                    List<WButtonMethodContext> groupContexts = entry.Value;
                    GroupCounts[entry.Key] = groupContexts?.Count ?? 0;
                    string resolvedGroupName = ResolveGroupName(groupContexts);
                    if (!string.IsNullOrWhiteSpace(resolvedGroupName))
                    {
                        GroupNames[entry.Key] = resolvedGroupName;
                    }
                }

                foreach (KeyValuePair<WButtonGroupKey, List<WButtonMethodContext>> entry in groups)
                {
                    WButtonGroupKey groupKey = entry.Key;
                    WButtonGroupPlacement groupPlacement = groupKey._groupPlacement;

                    // Resolve effective placement based on group placement setting
                    bool drawOnTop;
                    if (groupPlacement == WButtonGroupPlacement.UseGlobalSetting)
                    {
                        // Use global setting: render based on the global placement passed by caller
                        drawOnTop = globalPlacementIsTop;
                    }
                    else
                    {
                        drawOnTop = groupPlacement == WButtonGroupPlacement.Top;
                    }

                    if (
                        (placement == WButtonPlacement.Top && drawOnTop)
                        || (placement == WButtonPlacement.Bottom && !drawOnTop)
                    )
                    {
                        DrawGroup(
                            groupKey,
                            entry.Value,
                            paginationStates,
                            foldoutStates,
                            foldoutBehavior,
                            triggeredContexts
                        );
                        anyDrawn = true;
                    }
                }

                return anyDrawn;
            }
            finally
            {
                foreach (
                    KeyValuePair<
                        WButtonGroupKey,
                        PooledResource<List<WButtonMethodContext>>
                    > entry in groupLeases
                )
                {
                    entry.Value.Dispose();
                }
            }
        }

        internal static Dictionary<WButtonGroupKey, int> GetGroupCountsForTesting()
        {
            return GroupCounts;
        }

        internal static Dictionary<WButtonGroupKey, string> GetGroupNamesForTesting()
        {
            return GroupNames;
        }

        /// <summary>
        /// For testing: sets group counts with simple int keys (legacy compatibility).
        /// Creates group keys with the given draw order and empty group name.
        /// </summary>
        internal static void SetGroupCountsForTesting(Dictionary<int, int> counts)
        {
            GroupCounts.Clear();
            foreach (KeyValuePair<int, int> entry in counts)
            {
                WButtonGroupKey key = new(
                    WButtonAttribute.NoGroupPriority,
                    entry.Key,
                    null,
                    0,
                    WButtonGroupPlacement.UseGlobalSetting
                );
                GroupCounts[key] = entry.Value;
            }
        }

        /// <summary>
        /// For testing: sets group names with simple int keys (legacy compatibility).
        /// Creates group keys with the given draw order and empty group name.
        /// </summary>
        internal static void SetGroupNamesForTesting(Dictionary<int, string> names)
        {
            GroupNames.Clear();
            foreach (KeyValuePair<int, string> entry in names)
            {
                WButtonGroupKey key = new(
                    WButtonAttribute.NoGroupPriority,
                    entry.Key,
                    null,
                    0,
                    WButtonGroupPlacement.UseGlobalSetting
                );
                GroupNames[key] = entry.Value;
            }
        }

        /// <summary>
        /// For testing: clears all group counts and names.
        /// </summary>
        internal static void ClearGroupDataForTesting()
        {
            GroupCounts.Clear();
            GroupNames.Clear();
        }

        private static void BuildContexts(
            IReadOnlyList<WButtonMethodMetadata> metadataList,
            UnityEngine.Object[] targets,
            List<WButtonMethodContext> contexts
        )
        {
            contexts.Clear();
            int targetCount = targets.Length;

            for (int index = 0; index < metadataList.Count; index++)
            {
                WButtonMethodMetadata metadata = metadataList[index];
                bool allValid = true;

                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    if (targets[targetIndex] == null)
                    {
                        allValid = false;
                        break;
                    }
                }

                if (!allValid)
                {
                    continue;
                }

                WButtonMethodContext existingContext = FindCachedContext(metadata, targets);
                if (existingContext != null)
                {
                    contexts.Add(existingContext);
                    continue;
                }

                WButtonMethodState[] states = new WButtonMethodState[targetCount];
                UnityEngine.Object[] contextTargets = new UnityEngine.Object[targetCount];

                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    UnityEngine.Object target = targets[targetIndex];
                    WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
                    states[targetIndex] = targetState.GetOrCreateMethodState(metadata);
                    contextTargets[targetIndex] = target;
                }

                WButtonMethodContext context = new(metadata, states, contextTargets);
                CacheContext(metadata, targets, context);
                contexts.Add(context);
            }
        }

        private static readonly Dictionary<ContextCacheKey, WButtonMethodContext> ContextCache =
            new();

        private static WButtonMethodContext FindCachedContext(
            WButtonMethodMetadata metadata,
            UnityEngine.Object[] targets
        )
        {
            ContextCacheKey key = new(metadata, targets);
            if (ContextCache.TryGetValue(key, out WButtonMethodContext context))
            {
                if (ValidateContext(context, targets))
                {
                    return context;
                }
                ContextCache.Remove(key);
            }
            return null;
        }

        private static void CacheContext(
            WButtonMethodMetadata metadata,
            UnityEngine.Object[] targets,
            WButtonMethodContext context
        )
        {
            ContextCacheKey key = new(metadata, targets);
            ContextCache[key] = context;
        }

        private static bool ValidateContext(
            WButtonMethodContext context,
            UnityEngine.Object[] targets
        )
        {
            UnityEngine.Object[] contextTargets = context.Targets;
            if (contextTargets.Length != targets.Length)
            {
                return false;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (!ReferenceEquals(contextTargets[i], targets[i]))
                {
                    return false;
                }
            }
            return true;
        }

        internal static void ClearContextCache()
        {
            ContextCache.Clear();
        }

        private static void GroupByDrawOrderAndGroupName(
            List<WButtonMethodContext> contexts,
            SortedDictionary<WButtonGroupKey, List<WButtonMethodContext>> groups,
            Dictionary<WButtonGroupKey, PooledResource<List<WButtonMethodContext>>> leases
        )
        {
            groups.Clear();
            leases.Clear();
            ConflictingDrawOrderWarnings.Clear();
            ConflictingGroupPriorityWarnings.Clear();
            ConflictingGroupPlacementWarnings.Clear();

            // For buttons with a groupName, we need to merge them into a single group even if they have different drawOrders.
            // We use the first (minimum) declaration order's values as the canonical values for the group.
            // Buttons without a groupName (empty string) are grouped by their individual drawOrder.

            // Track: groupName -> (first declaration order, canonical draw order, canonical group priority, canonical group placement)
            Dictionary<
                string,
                (
                    int declarationOrder,
                    int drawOrder,
                    int groupPriority,
                    WButtonGroupPlacement groupPlacement
                )
            > namedGroupInfo = new();

            // Track conflicting draw orders for warning purposes
            // groupName -> HashSet of all draw orders seen for that group
            Dictionary<string, HashSet<int>> drawOrdersPerGroup = new();

            // Track conflicting group priorities for warning purposes
            // groupName -> HashSet of all group priorities seen for that group
            Dictionary<string, HashSet<int>> groupPrioritiesPerGroup = new();

            // Track conflicting group placements for warning purposes
            // groupName -> HashSet of all group placements seen for that group
            Dictionary<string, HashSet<WButtonGroupPlacement>> groupPlacementsPerGroup = new();

            // First pass: determine canonical values for each named group (based on first declared button)
            foreach (WButtonMethodContext context in contexts)
            {
                string groupName = context.Metadata.GroupName ?? string.Empty;

                if (string.IsNullOrEmpty(groupName))
                {
                    // Buttons without a group name are handled separately (grouped by drawOrder alone)
                    continue;
                }

                int drawOrder = context.Metadata.DrawOrder;
                int declarationOrder = context.Metadata.DeclarationOrder;
                int groupPriority = context.Metadata.GroupPriority;
                WButtonGroupPlacement groupPlacement = context.Metadata.GroupPlacement;

                // Track all draw orders seen for this group (for warning purposes)
                drawOrdersPerGroup.GetOrAdd(groupName).Add(drawOrder);

                // Track only explicit group priorities for conflict detection (ignore NoGroupPriority sentinel)
                if (groupPriority != WButtonAttribute.NoGroupPriority)
                {
                    groupPrioritiesPerGroup.GetOrAdd(groupName).Add(groupPriority);
                }

                // Track only explicit group placements for conflict detection (ignore UseGlobalSetting sentinel)
                if (groupPlacement != WButtonGroupPlacement.UseGlobalSetting)
                {
                    groupPlacementsPerGroup.GetOrAdd(groupName).Add(groupPlacement);
                }

                if (
                    !namedGroupInfo.TryGetValue(
                        groupName,
                        out (
                            int declarationOrder,
                            int drawOrder,
                            int groupPriority,
                            WButtonGroupPlacement groupPlacement
                        ) existing
                    )
                )
                {
                    namedGroupInfo[groupName] = (
                        declarationOrder,
                        drawOrder,
                        groupPriority,
                        groupPlacement
                    );
                }
                else if (declarationOrder < existing.declarationOrder)
                {
                    // This button was declared earlier, use its values as canonical
                    namedGroupInfo[groupName] = (
                        declarationOrder,
                        drawOrder,
                        groupPriority,
                        groupPlacement
                    );
                }
            }

            // Generate warnings for groups with conflicting draw orders
            foreach (KeyValuePair<string, HashSet<int>> entry in drawOrdersPerGroup)
            {
                if (entry.Value.Count > 1)
                {
                    (
                        int declarationOrder,
                        int drawOrder,
                        int groupPriority,
                        WButtonGroupPlacement groupPlacement
                    ) info = namedGroupInfo[entry.Key];
                    ConflictingDrawOrderWarnings[entry.Key] = new DrawOrderConflictInfo(
                        entry.Key,
                        info.drawOrder,
                        entry.Value
                    );
                }
            }

            // Generate warnings for groups with conflicting group priorities
            foreach (KeyValuePair<string, HashSet<int>> entry in groupPrioritiesPerGroup)
            {
                if (entry.Value.Count > 1)
                {
                    (
                        int declarationOrder,
                        int drawOrder,
                        int groupPriority,
                        WButtonGroupPlacement groupPlacement
                    ) info = namedGroupInfo[entry.Key];
                    ConflictingGroupPriorityWarnings[entry.Key] = new GroupPriorityConflictInfo(
                        entry.Key,
                        info.groupPriority,
                        entry.Value
                    );
                }
            }

            // Generate warnings for groups with conflicting group placements
            foreach (
                KeyValuePair<
                    string,
                    HashSet<WButtonGroupPlacement>
                > entry in groupPlacementsPerGroup
            )
            {
                if (entry.Value.Count > 1)
                {
                    (
                        int declarationOrder,
                        int drawOrder,
                        int groupPriority,
                        WButtonGroupPlacement groupPlacement
                    ) info = namedGroupInfo[entry.Key];
                    ConflictingGroupPlacementWarnings[entry.Key] = new GroupPlacementConflictInfo(
                        entry.Key,
                        info.groupPlacement,
                        entry.Value
                    );
                }
            }

            // Track the first declaration order for each unique group key (for ungrouped buttons)
            Dictionary<(int, string), int> firstDeclarationOrderForUngrouped = new();

            // First pass for ungrouped buttons: find minimum declaration order per (drawOrder, empty groupName)
            foreach (WButtonMethodContext context in contexts)
            {
                string groupName = context.Metadata.GroupName ?? string.Empty;
                if (!string.IsNullOrEmpty(groupName))
                {
                    continue;
                }

                int drawOrder = context.Metadata.DrawOrder;
                int declarationOrder = context.Metadata.DeclarationOrder;
                (int, string) lookupKey = (drawOrder, groupName);

                if (
                    !firstDeclarationOrderForUngrouped.TryGetValue(lookupKey, out int existingOrder)
                )
                {
                    firstDeclarationOrderForUngrouped[lookupKey] = declarationOrder;
                }
                else if (declarationOrder < existingOrder)
                {
                    firstDeclarationOrderForUngrouped[lookupKey] = declarationOrder;
                }
            }

            // Second pass: build groups
            foreach (WButtonMethodContext context in contexts)
            {
                string groupName = context.Metadata.GroupName ?? string.Empty;
                int drawOrder;
                int groupDeclarationOrder;
                int groupPriority;
                WButtonGroupPlacement groupPlacement;

                if (!string.IsNullOrEmpty(groupName))
                {
                    // Named group: use the canonical values from the first declared button
                    (
                        int declarationOrder,
                        int canonicalDrawOrder,
                        int canonicalGroupPriority,
                        WButtonGroupPlacement canonicalGroupPlacement
                    ) info = namedGroupInfo[groupName];
                    drawOrder = info.canonicalDrawOrder;
                    groupDeclarationOrder = info.declarationOrder;
                    groupPriority = info.canonicalGroupPriority;
                    groupPlacement = info.canonicalGroupPlacement;
                }
                else
                {
                    // Ungrouped button: use its own values, ignore groupPriority and groupPlacement
                    drawOrder = context.Metadata.DrawOrder;
                    (int, string) lookupKey = (drawOrder, groupName);
                    groupDeclarationOrder = firstDeclarationOrderForUngrouped[lookupKey];
                    groupPriority = WButtonAttribute.NoGroupPriority;
                    groupPlacement = WButtonGroupPlacement.UseGlobalSetting;
                }

                WButtonGroupKey groupKey = new(
                    groupPriority,
                    drawOrder,
                    groupName,
                    groupDeclarationOrder,
                    groupPlacement
                );

                if (!groups.TryGetValue(groupKey, out List<WButtonMethodContext> group))
                {
                    PooledResource<List<WButtonMethodContext>> lease =
                        Buffers<WButtonMethodContext>.GetList(4, out group);
                    groups[groupKey] = group;
                    leases[groupKey] = lease;
                }

                group.Add(context);
            }
        }

        /// <summary>
        /// Information about conflicting draw orders within a named group.
        /// </summary>
        internal readonly struct DrawOrderConflictInfo
        {
            // ReSharper disable once NotAccessedField.Global
            internal readonly string _groupName;
            internal readonly int _canonicalDrawOrder;
            internal readonly HashSet<int> _allDrawOrders;

            internal DrawOrderConflictInfo(
                string groupName,
                int canonicalDrawOrder,
                HashSet<int> allDrawOrders
            )
            {
                _groupName = groupName;
                _canonicalDrawOrder = canonicalDrawOrder;
                _allDrawOrders = allDrawOrders;
            }
        }

        /// <summary>
        /// Information about conflicting group priorities within a named group.
        /// </summary>
        internal readonly struct GroupPriorityConflictInfo
        {
            // ReSharper disable once NotAccessedField.Global
            internal readonly string _groupName;
            internal readonly int _canonicalGroupPriority;
            internal readonly HashSet<int> _allGroupPriorities;

            internal GroupPriorityConflictInfo(
                string groupName,
                int canonicalGroupPriority,
                HashSet<int> allGroupPriorities
            )
            {
                _groupName = groupName;
                _canonicalGroupPriority = canonicalGroupPriority;
                _allGroupPriorities = allGroupPriorities;
            }
        }

        /// <summary>
        /// Information about conflicting group placements within a named group.
        /// </summary>
        internal readonly struct GroupPlacementConflictInfo
        {
            // ReSharper disable once NotAccessedField.Global
            internal readonly string _groupName;
            internal readonly WButtonGroupPlacement _canonicalGroupPlacement;
            internal readonly HashSet<WButtonGroupPlacement> _allGroupPlacements;

            internal GroupPlacementConflictInfo(
                string groupName,
                WButtonGroupPlacement canonicalGroupPlacement,
                HashSet<WButtonGroupPlacement> allGroupPlacements
            )
            {
                _groupName = groupName;
                _canonicalGroupPlacement = canonicalGroupPlacement;
                _allGroupPlacements = allGroupPlacements;
            }
        }

        /// <summary>
        /// Warnings about groups with conflicting draw orders. Populated during grouping.
        /// </summary>
        private static readonly Dictionary<
            string,
            DrawOrderConflictInfo
        > ConflictingDrawOrderWarnings = new();

        /// <summary>
        /// Warnings about groups with conflicting group priorities. Populated during grouping.
        /// </summary>
        private static readonly Dictionary<
            string,
            GroupPriorityConflictInfo
        > ConflictingGroupPriorityWarnings = new();

        /// <summary>
        /// Warnings about groups with conflicting group placements. Populated during grouping.
        /// </summary>
        private static readonly Dictionary<
            string,
            GroupPlacementConflictInfo
        > ConflictingGroupPlacementWarnings = new();

        /// <summary>
        /// Gets the current conflicting draw order warnings. Used for testing and UI display.
        /// </summary>
        internal static IReadOnlyDictionary<
            string,
            DrawOrderConflictInfo
        > GetConflictingDrawOrderWarnings()
        {
            return ConflictingDrawOrderWarnings;
        }

        /// <summary>
        /// Gets the current conflicting group priority warnings. Used for testing and UI display.
        /// </summary>
        internal static IReadOnlyDictionary<
            string,
            GroupPriorityConflictInfo
        > GetConflictingGroupPriorityWarnings()
        {
            return ConflictingGroupPriorityWarnings;
        }

        /// <summary>
        /// Gets the current conflicting group placement warnings. Used for testing and UI display.
        /// </summary>
        internal static IReadOnlyDictionary<
            string,
            GroupPlacementConflictInfo
        > GetConflictingGroupPlacementWarnings()
        {
            return ConflictingGroupPlacementWarnings;
        }

        /// <summary>
        /// Clears conflicting draw order warnings. Used for testing.
        /// </summary>
        internal static void ClearConflictingDrawOrderWarningsForTesting()
        {
            ConflictingDrawOrderWarnings.Clear();
        }

        /// <summary>
        /// Clears conflicting group priority warnings. Used for testing.
        /// </summary>
        internal static void ClearConflictingGroupPriorityWarningsForTesting()
        {
            ConflictingGroupPriorityWarnings.Clear();
        }

        /// <summary>
        /// Clears conflicting group placement warnings. Used for testing.
        /// </summary>
        internal static void ClearConflictingGroupPlacementWarningsForTesting()
        {
            ConflictingGroupPlacementWarnings.Clear();
        }

        private static void DrawGroup(
            WButtonGroupKey groupKey,
            List<WButtonMethodContext> contexts,
            IDictionary<WButtonGroupKey, WButtonPaginationState> paginationStates,
            IDictionary<WButtonGroupKey, bool> foldoutStates,
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior,
            List<WButtonMethodContext> triggeredContexts
        )
        {
            if (contexts == null || contexts.Count == 0)
            {
                return;
            }

            // Guard against calling GUI methods outside of a valid GUI context (e.g., in tests)
            if (Event.current == null)
            {
                return;
            }

            GUIContent header = BuildGroupHeader(groupKey);
            bool alwaysOpen =
                foldoutBehavior == UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen;
            bool expanded = alwaysOpen || GetFoldoutState(foldoutStates, groupKey, foldoutBehavior);
            bool effectiveExpanded = expanded;
            bool tweenEnabled = UnityHelpersSettings.ShouldTweenWButtonFoldouts();
            AnimBool foldoutAnim =
                alwaysOpen || !tweenEnabled ? null : GetFoldoutAnim(groupKey, expanded);
            if (!tweenEnabled)
            {
                if (FoldoutAnimations.TryGetValue(groupKey, out AnimBool cached) && cached != null)
                {
                    cached.valueChanged.RemoveListener(RequestRepaint);
                }
                FoldoutAnimations.Remove(groupKey);
            }

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = WButtonStyles.GetFoldoutBackgroundColor(expanded || alwaysOpen);

            GUILayout.BeginVertical(
                alwaysOpen
                    ? WButtonStyles.GroupStyle
                    : WButtonStyles.GetFoldoutContainerStyle(expanded)
            );

            GUI.backgroundColor = previousBackground;

            if (alwaysOpen)
            {
                GUILayout.Label(header, WButtonStyles.HeaderStyle);
                EditorGUILayout.Space(WButtonStyles.FoldoutContentSpacing);
            }
            else
            {
                Rect headerRect = GUILayoutUtility.GetRect(
                    header,
                    WButtonStyles.FoldoutHeaderStyle,
                    GUILayout.ExpandWidth(true)
                );
                headerRect.xMin += WButtonStyles.FoldoutIconOffset;

                EditorGUI.indentLevel++;
                bool newExpanded = EditorGUI.Foldout(
                    headerRect,
                    expanded,
                    header,
                    true,
                    WButtonStyles.FoldoutHeaderStyle
                );
                EditorGUI.indentLevel--;

                if (foldoutStates != null)
                {
                    foldoutStates[groupKey] = newExpanded;
                }

                if (foldoutAnim != null)
                {
                    foldoutAnim.target = newExpanded;
                }
                effectiveExpanded = newExpanded;

                EditorGUILayout.Space(WButtonStyles.FoldoutContentSpacing);
            }

            DrawConflictWarnings(groupKey);
            if (alwaysOpen)
            {
                DrawGroupContent(groupKey, contexts, paginationStates, triggeredContexts);
            }
            else
            {
                float fade = foldoutAnim?.faded ?? (effectiveExpanded ? 1f : 0f);
                if (foldoutAnim == null)
                {
                    if (effectiveExpanded)
                    {
                        DrawGroupContent(groupKey, contexts, paginationStates, triggeredContexts);
                    }
                }
                else
                {
                    bool visible = EditorGUILayout.BeginFadeGroup(fade);
                    if (visible)
                    {
                        DrawGroupContent(groupKey, contexts, paginationStates, triggeredContexts);
                    }
                    EditorGUILayout.EndFadeGroup();
                }
            }

            GUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private static void DrawGroupContent(
            WButtonGroupKey groupKey,
            List<WButtonMethodContext> contexts,
            IDictionary<WButtonGroupKey, WButtonPaginationState> paginationStates,
            List<WButtonMethodContext> triggeredContexts
        )
        {
            int pageSize = UnityHelpersSettings.GetWButtonPageSize();
            WButtonPaginationState state = GetPaginationState(
                paginationStates,
                groupKey,
                contexts.Count
            );

            DrawPaginationControls(state, contexts.Count, pageSize);

            int startIndex = state._pageIndex * pageSize;
            int endIndex = Mathf.Min(startIndex + pageSize, contexts.Count);

            for (int index = startIndex; index < endIndex; index++)
            {
                WButtonMethodContext context = contexts[index];
                DrawMethod(context, triggeredContexts);
                if (index < endIndex - 1)
                {
                    EditorGUILayout.Space(6f);
                }
            }

            if (endIndex > startIndex)
            {
                EditorGUILayout.Space(4f);
            }
        }

        private static void DrawPaginationControls(
            WButtonPaginationState state,
            int totalItems,
            int pageSize
        )
        {
            if (totalItems <= pageSize)
            {
                state._pageIndex = 0;
                return;
            }

            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / pageSize));
            if (state._pageIndex >= totalPages)
            {
                state._pageIndex = totalPages - 1;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    GetPaginationLabel(state._pageIndex + 1, totalPages),
                    GUILayout.Width(90f)
                );

                using (new EditorGUI.DisabledScope(state._pageIndex == 0))
                {
                    if (GUILayout.Button("Prev", GUILayout.Width(50f)))
                    {
                        state._pageIndex--;
                        if (state._pageIndex < 0)
                        {
                            state._pageIndex = 0;
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(state._pageIndex >= totalPages - 1))
                {
                    if (GUILayout.Button("Next", GUILayout.Width(50f)))
                    {
                        state._pageIndex++;
                        if (state._pageIndex >= totalPages)
                        {
                            state._pageIndex = totalPages - 1;
                        }
                    }
                }
            }
            EditorGUILayout.Space(4f);
        }

        private static readonly Dictionary<string, string> ConflictWarningTextCache = new();
        private static readonly Dictionary<string, string> GroupPriorityWarningTextCache = new();
        private static readonly Dictionary<string, string> GroupPlacementWarningTextCache = new();

        private static void DrawConflictWarnings(WButtonGroupKey groupKey)
        {
            DrawConflictingDrawOrderWarning(groupKey);
            DrawConflictingGroupPriorityWarning(groupKey);
            DrawConflictingGroupPlacementWarning(groupKey);
        }

        private static void DrawConflictingDrawOrderWarning(WButtonGroupKey groupKey)
        {
            string groupName = groupKey._groupName;
            if (string.IsNullOrEmpty(groupName))
            {
                return;
            }

            if (
                !ConflictingDrawOrderWarnings.TryGetValue(
                    groupName,
                    out DrawOrderConflictInfo conflict
                )
            )
            {
                return;
            }

            if (!ConflictWarningTextCache.TryGetValue(groupName, out string warningText))
            {
                List<int> sortedOrders = new(conflict._allDrawOrders);
                sortedOrders.Sort();
                string ordersText = string.Join(", ", sortedOrders);
                warningText =
                    $"Conflicting drawOrder values ({ordersText}) in group \"{groupName}\". Using {conflict._canonicalDrawOrder} from first declared button.";
                ConflictWarningTextCache[groupName] = warningText;
            }

            EditorGUILayout.HelpBox(warningText, MessageType.Warning);
            EditorGUILayout.Space(2f);
        }

        private static void DrawConflictingGroupPriorityWarning(WButtonGroupKey groupKey)
        {
            string groupName = groupKey._groupName;
            if (string.IsNullOrEmpty(groupName))
            {
                return;
            }

            if (
                !ConflictingGroupPriorityWarnings.TryGetValue(
                    groupName,
                    out GroupPriorityConflictInfo conflict
                )
            )
            {
                return;
            }

            string cacheKey = "priority_" + groupName;
            if (!GroupPriorityWarningTextCache.TryGetValue(cacheKey, out string warningText))
            {
                List<int> sortedPriorities = new(conflict._allGroupPriorities);
                sortedPriorities.Sort();
                List<string> priorityStrings = new(sortedPriorities.Count);
                foreach (int priority in sortedPriorities)
                {
                    priorityStrings.Add(
                        priority == WButtonAttribute.NoGroupPriority
                            ? "NoGroupPriority"
                            : priority.ToString()
                    );
                }
                string prioritiesText = string.Join(", ", priorityStrings);
                string canonicalText =
                    conflict._canonicalGroupPriority == WButtonAttribute.NoGroupPriority
                        ? "NoGroupPriority"
                        : conflict._canonicalGroupPriority.ToString();
                warningText =
                    $"Conflicting groupPriority values ({prioritiesText}) in group \"{groupName}\". Using {canonicalText} from first declared button.";
                GroupPriorityWarningTextCache[cacheKey] = warningText;
            }

            EditorGUILayout.HelpBox(warningText, MessageType.Warning);
            EditorGUILayout.Space(2f);
        }

        private static void DrawConflictingGroupPlacementWarning(WButtonGroupKey groupKey)
        {
            string groupName = groupKey._groupName;
            if (string.IsNullOrEmpty(groupName))
            {
                return;
            }

            if (
                !ConflictingGroupPlacementWarnings.TryGetValue(
                    groupName,
                    out GroupPlacementConflictInfo conflict
                )
            )
            {
                return;
            }

            string cacheKey = "placement_" + groupName;
            if (!GroupPlacementWarningTextCache.TryGetValue(cacheKey, out string warningText))
            {
                List<WButtonGroupPlacement> sortedPlacements = new(conflict._allGroupPlacements);
                sortedPlacements.Sort();
                string placementsText = string.Join(", ", sortedPlacements);
                warningText =
                    $"Conflicting groupPlacement values ({placementsText}) in group \"{groupName}\". Using {conflict._canonicalGroupPlacement} from first declared button.";
                GroupPlacementWarningTextCache[cacheKey] = warningText;
            }

            EditorGUILayout.HelpBox(warningText, MessageType.Warning);
            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// Clears the conflict warning content cache. Used for testing.
        /// </summary>
        internal static void ClearConflictWarningContentCacheForTesting()
        {
            ConflictWarningTextCache.Clear();
            GroupPriorityWarningTextCache.Clear();
            GroupPlacementWarningTextCache.Clear();
        }

        /// <summary>
        /// Gets cached group placement warning text by group name. Used for testing.
        /// </summary>
        internal static bool TryGetGroupPlacementWarningTextForTesting(
            string groupName,
            out string warningText
        )
        {
            if (string.IsNullOrEmpty(groupName))
            {
                warningText = null;
                return false;
            }

            return GroupPlacementWarningTextCache.TryGetValue(
                "placement_" + groupName,
                out warningText
            );
        }

        /// <summary>
        /// Gets cached group priority warning text by group name. Used for testing.
        /// </summary>
        internal static bool TryGetGroupPriorityWarningTextForTesting(
            string groupName,
            out string warningText
        )
        {
            if (string.IsNullOrEmpty(groupName))
            {
                warningText = null;
                return false;
            }

            return GroupPriorityWarningTextCache.TryGetValue(
                "priority_" + groupName,
                out warningText
            );
        }

        /// <summary>
        /// Gets cached draw order warning text by group name. Used for testing.
        /// </summary>
        internal static bool TryGetDrawOrderWarningTextForTesting(
            string groupName,
            out string warningText
        )
        {
            if (string.IsNullOrEmpty(groupName))
            {
                warningText = null;
                return false;
            }

            return ConflictWarningTextCache.TryGetValue(groupName, out warningText);
        }

        private static bool GetFoldoutState(
            IDictionary<WButtonGroupKey, bool> foldoutStates,
            WButtonGroupKey groupKey,
            UnityHelpersSettings.WButtonFoldoutBehavior behavior
        )
        {
            bool defaultExpanded =
                behavior != UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed;
            if (foldoutStates == null)
            {
                return defaultExpanded;
            }

            if (foldoutStates.TryGetValue(groupKey, out bool current))
            {
                return current;
            }

            foldoutStates[groupKey] = defaultExpanded;
            return defaultExpanded;
        }

        private static AnimBool GetFoldoutAnim(WButtonGroupKey groupKey, bool expanded)
        {
            float speed = UnityHelpersSettings.GetWButtonFoldoutSpeed();
            if (!FoldoutAnimations.TryGetValue(groupKey, out AnimBool anim) || anim == null)
            {
                anim = new AnimBool(expanded) { speed = speed };
                anim.valueChanged.AddListener(RequestRepaint);
                FoldoutAnimations[groupKey] = anim;
            }

            anim.speed = speed;
            anim.target = expanded;
            return anim;
        }

        private static void RequestRepaint()
        {
            InternalEditorUtility.RepaintAllViews();
        }

        internal static GUIContent BuildGroupHeader(WButtonGroupKey groupKey)
        {
            WButtonGroupPlacement groupPlacement = groupKey._groupPlacement;
            // Use placement to determine label style
            GUIContent baseLabel =
                groupPlacement == WButtonGroupPlacement.Bottom
                    ? WButtonStyles.BottomGroupLabel
                    : WButtonStyles.TopGroupLabel;

            if (
                GroupNames.TryGetValue(groupKey, out string customName)
                && !string.IsNullOrWhiteSpace(customName)
            )
            {
                if (!GroupHeaderCache.TryGetValue(groupKey, out GUIContent cached))
                {
                    cached = new GUIContent(customName, baseLabel.tooltip);
                    GroupHeaderCache[groupKey] = cached;
                }
                else if (!string.Equals(cached.text, customName, StringComparison.Ordinal))
                {
                    cached.text = customName;
                    cached.tooltip = baseLabel.tooltip;
                }
                return cached;
            }

            if (GroupCounts.Count <= 1)
            {
                return baseLabel;
            }

            if (!GroupCounts.TryGetValue(groupKey, out int count) || count <= 0)
            {
                return baseLabel;
            }

            int drawOrder = groupKey._drawOrder;
            (string, int) textCacheKey = (baseLabel.text, drawOrder);
            if (!GroupHeaderTextCache.TryGetValue(textCacheKey, out string textWithOrder))
            {
                textWithOrder = baseLabel.text + " (" + GetCachedIntString(drawOrder) + ")";
                GroupHeaderTextCache[textCacheKey] = textWithOrder;
            }

            if (!GroupHeaderCache.TryGetValue(groupKey, out GUIContent cachedWithOrder))
            {
                cachedWithOrder = new GUIContent(textWithOrder, baseLabel.tooltip);
                GroupHeaderCache[groupKey] = cachedWithOrder;
            }
            else if (!string.Equals(cachedWithOrder.text, textWithOrder, StringComparison.Ordinal))
            {
                cachedWithOrder.text = textWithOrder;
                cachedWithOrder.tooltip = baseLabel.tooltip;
            }
            return cachedWithOrder;
        }

        /// <summary>
        /// Legacy overload for testing compatibility.
        /// </summary>
        internal static GUIContent BuildGroupHeader(int drawOrder)
        {
            WButtonGroupKey key = new(
                WButtonAttribute.NoGroupPriority,
                drawOrder,
                null,
                0,
                WButtonGroupPlacement.UseGlobalSetting
            );
            return BuildGroupHeader(key);
        }

        private static string ResolveGroupName(List<WButtonMethodContext> contexts)
        {
            if (contexts == null)
            {
                return null;
            }

            for (int index = 0; index < contexts.Count; index++)
            {
                WButtonMethodContext context = contexts[index];
                string groupName = context?.Metadata?.GroupName;
                if (!string.IsNullOrWhiteSpace(groupName))
                {
                    return groupName;
                }
            }

            return null;
        }

        private static void DrawMethod(
            WButtonMethodContext context,
            List<WButtonMethodContext> triggeredContexts
        )
        {
            WButtonMethodMetadata metadata = context.Metadata;
            GUILayout.BeginVertical(EditorStyles.helpBox);

            WButtonMethodState[] states = context.States;
            if (states.Length > 0 && states[0].Parameters.Length > 0)
            {
                EditorGUI.indentLevel++;
                WButtonParameterDrawer.DrawParameters(states);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(3f);
            }

            GetInvocationStatus(states, out int runningCount, out bool cancellable);
            bool isRunning = runningCount > 0;

            if (isRunning)
            {
                DrawRunningStatus(context, runningCount, cancellable);
            }

            DrawHistory(states[0]);

            UnityHelpersSettings.WButtonPaletteEntry palette =
                UnityHelpersSettings.ResolveWButtonPalette(metadata.ColorKey);
            GUIStyle buttonStyle = WButtonStyles.GetColoredButtonStyle(
                palette.ButtonColor,
                palette.TextColor
            );

            if (
                !ButtonDisplayNameCache.TryGetValue(
                    metadata.DisplayName,
                    out GUIContent buttonContent
                )
            )
            {
                buttonContent = new GUIContent(metadata.DisplayName);
                ButtonDisplayNameCache[metadata.DisplayName] = buttonContent;
            }

            Rect buttonRect = GUILayoutUtility.GetRect(
                buttonContent,
                buttonStyle,
                GUILayout.Height(WButtonStyles.ButtonHeight),
                GUILayout.ExpandWidth(true)
            );

            using (new EditorGUI.DisabledScope(isRunning))
            {
                if (GUI.Button(buttonRect, metadata.DisplayName, buttonStyle))
                {
                    context.MarkTriggered();
                    if (triggeredContexts != null)
                    {
                        triggeredContexts.Add(context);
                    }
                }
            }

            GUILayout.Space(2f);
            GUILayout.EndVertical();
        }

        internal static void GetInvocationStatus(
            WButtonMethodState[] states,
            out int runningCount,
            out bool cancellable
        )
        {
            runningCount = 0;
            cancellable = false;

            if (states == null || states.Length == 0)
            {
                return;
            }

            foreach (WButtonMethodState state in states)
            {
                WButtonInvocationHandle handle = state.ActiveInvocation;
                if (handle == null)
                {
                    continue;
                }

                if (
                    handle.Status == WButtonInvocationStatus.Running
                    || handle.Status == WButtonInvocationStatus.CancelRequested
                )
                {
                    runningCount++;
                    cancellable |= handle.SupportsCancellation;
                }
            }
        }

        private static void DrawRunningStatus(
            WButtonMethodContext context,
            int runningCount,
            bool cancellable
        )
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(GetRunningLabel(runningCount), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (cancellable)
                {
                    UnityHelpersSettings.WButtonPaletteEntry cancelColors =
                        UnityHelpersSettings.GetWButtonCancelButtonColors();
                    GUIStyle cancelStyle = WButtonStyles.GetColoredMiniButtonStyle(
                        cancelColors.ButtonColor,
                        cancelColors.TextColor
                    );
                    if (GUILayout.Button("Cancel", cancelStyle, GUILayout.Width(70f)))
                    {
                        WButtonInvocationController.CancelActiveInvocations(context);
                    }
                }
            }
            EditorGUILayout.Space(2f);
        }

        private static void DrawHistory(WButtonMethodState state)
        {
            if (state is not { HasHistory: true })
            {
                return;
            }

            GUILayout.BeginVertical(EditorStyles.helpBox);
            Rect headerRect = EditorGUILayout.GetControlRect(
                false,
                EditorGUIUtility.singleLineHeight,
                GUILayout.ExpandWidth(true)
            );
            Vector2 labelSize = EditorStyles.miniBoldLabel.CalcSize(RecentResultsHeaderContent);
            float buttonWidth = Mathf.Max(
                ClearHistoryMinWidth,
                EditorStyles.miniButton.CalcSize(ClearHistoryContent).x + ClearHistoryButtonPadding
            );
            float availableWidth = headerRect.width;
            bool canShowButton =
                availableWidth >= (labelSize.x + ClearHistorySpacing + buttonWidth);
            float labelWidth = canShowButton
                ? Mathf.Min(labelSize.x, availableWidth - (buttonWidth + ClearHistorySpacing))
                : availableWidth;

            Rect labelRect = new(headerRect.x, headerRect.y, labelWidth, headerRect.height);
            GUI.Label(labelRect, RecentResultsHeaderContent, EditorStyles.miniBoldLabel);

            if (canShowButton)
            {
                Rect buttonRect = new(
                    headerRect.xMax - buttonWidth,
                    headerRect.y,
                    buttonWidth,
                    headerRect.height
                );
                UnityHelpersSettings.WButtonPaletteEntry clearHistoryColors =
                    UnityHelpersSettings.GetWButtonClearHistoryButtonColors();
                GUIStyle clearHistoryStyle = WButtonStyles.GetColoredMiniButtonStyle(
                    clearHistoryColors.ButtonColor,
                    clearHistoryColors.TextColor
                );
                if (GUI.Button(buttonRect, ClearHistoryContent, clearHistoryStyle))
                {
                    state.ClearHistory();
                    GUI.FocusControl(null);
                }
            }

            if (!state.HasHistory)
            {
                GUILayout.EndVertical();
                EditorGUILayout.Space(3f);
                return;
            }

            for (int index = state.History.Count - 1; index >= 0; index--)
            {
                WButtonResultEntry entry = state.History[index];
                DrawHistoryEntry(entry);
            }

            GUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private static void DrawHistoryEntry(WButtonResultEntry entry)
        {
            string summary = entry.GetDisplayString();
            GUIStyle labelStyle =
                entry.Kind == WButtonResultKind.Error
                    ? EditorStyles.miniBoldLabel
                    : EditorStyles.miniLabel;

            EditorGUILayout.LabelField(summary, labelStyle);

            if (entry.ObjectReference != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        "Result",
                        entry.ObjectReference,
                        entry.ObjectReference.GetType(),
                        true
                    );
                }
            }

            if (entry.Kind == WButtonResultKind.Error && entry.Exception != null)
            {
                EditorGUILayout.LabelField(
                    entry.Exception.GetType().Name,
                    EditorStyles.wordWrappedMiniLabel
                );
            }
        }

        private static WButtonPaginationState GetPaginationState(
            IDictionary<WButtonGroupKey, WButtonPaginationState> paginationStates,
            WButtonGroupKey groupKey,
            int itemCount
        )
        {
            if (paginationStates == null)
            {
                return WButtonPaginationState.Fallback;
            }

            WButtonPaginationState state = paginationStates.GetOrAdd(groupKey);

            int pageSize = UnityHelpersSettings.GetWButtonPageSize();
            int pageCount = Mathf.Max(1, Mathf.CeilToInt((float)itemCount / pageSize));
            if (state._pageIndex >= pageCount)
            {
                state._pageIndex = pageCount - 1;
            }

            if (state._pageIndex < 0)
            {
                state._pageIndex = 0;
            }

            return state;
        }
    }

    internal sealed class WButtonPaginationState
    {
        internal static readonly WButtonPaginationState Fallback = new();
        internal int _pageIndex;
    }

    internal sealed class WButtonMethodContext
    {
        internal WButtonMethodContext(
            WButtonMethodMetadata metadata,
            WButtonMethodState[] states,
            UnityEngine.Object[] targets
        )
        {
            Metadata = metadata;
            States = states;
            Targets = targets;
        }

        internal WButtonMethodMetadata Metadata { get; }

        internal WButtonMethodState[] States { get; }

        internal UnityEngine.Object[] Targets { get; }

        internal bool InvocationRequested { get; private set; }

        internal void MarkTriggered()
        {
            InvocationRequested = true;
        }

        internal void ResetTrigger()
        {
            InvocationRequested = false;
        }
    }

    internal readonly struct ContextCacheKey : IEquatable<ContextCacheKey>
    {
        private readonly WButtonMethodMetadata _metadata;
        private readonly int _targetHash;

        internal ContextCacheKey(WButtonMethodMetadata metadata, UnityEngine.Object[] targets)
        {
            _metadata = metadata;
            _targetHash = ComputeTargetHash(targets);
        }

        private static int ComputeTargetHash(UnityEngine.Object[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return 0;
            }

            Span<int> instanceIds = stackalloc int[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                UnityEngine.Object target = targets[i];
                instanceIds[i] = target != null ? target.GetUnityObjectId() : 0;
            }

            return Objects.SpanHashCode<int>(instanceIds);
        }

        public bool Equals(ContextCacheKey other)
        {
            return ReferenceEquals(_metadata, other._metadata) && _targetHash == other._targetHash;
        }

        public override bool Equals(object obj)
        {
            return obj is ContextCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_metadata, _targetHash);
        }
    }
#endif
}
