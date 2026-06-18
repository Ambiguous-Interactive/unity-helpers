// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WGroup
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Diagnostics helper for debugging WGroup indentation issues.
    /// </summary>
    /// <remarks>
    /// Enable logging by setting <see cref="Enabled"/> to true. Logs are written to the Unity console
    /// with the prefix "[WGroupIndent]" for easy filtering.
    /// </remarks>
    internal static class WGroupIndentDiagnostics
    {
        /// <summary>
        /// When true, enables diagnostic logging for WGroup padding operations.
        /// </summary>
        internal static bool Enabled { get; set; }

        /// <summary>
        /// When set, only logs for groups matching this name (substring match).
        /// Leave null to log all groups.
        /// </summary>
        internal static string GroupNameFilter { get; set; }

        private const string LogPrefix = "[WGroupIndent] ";

        private static bool ShouldLog(string groupName)
        {
            if (!Enabled)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(GroupNameFilter))
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    return false;
                }

                if (
                    groupName.IndexOf(GroupNameFilter, System.StringComparison.OrdinalIgnoreCase)
                    < 0
                )
                {
                    return false;
                }
            }

            return true;
        }

        internal static void LogPushPadding(
            string groupName,
            float horizontalPadding,
            float leftPadding,
            float rightPadding,
            int indentLevel
        )
        {
            if (!ShouldLog(groupName))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}PushPadding: group={groupName ?? "(null)"}, "
                    + $"horizontal={horizontalPadding:F2}, left={leftPadding:F2}, right={rightPadding:F2}, "
                    + $"indentLevel={indentLevel}, "
                    + $"beforePush: totalLeft={GroupGUIWidthUtility.CurrentLeftPadding:F2}, "
                    + $"totalRight={GroupGUIWidthUtility.CurrentRightPadding:F2}, "
                    + $"depth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogAfterPush(string groupName)
        {
            if (!ShouldLog(groupName))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}AfterPush: group={groupName ?? "(null)"}, "
                    + $"totalLeft={GroupGUIWidthUtility.CurrentLeftPadding:F2}, "
                    + $"totalRight={GroupGUIWidthUtility.CurrentRightPadding:F2}, "
                    + $"depth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogAfterPop(string groupName)
        {
            if (!ShouldLog(groupName))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}AfterPop: group={groupName ?? "(null)"}, "
                    + $"totalLeft={GroupGUIWidthUtility.CurrentLeftPadding:F2}, "
                    + $"totalRight={GroupGUIWidthUtility.CurrentRightPadding:F2}, "
                    + $"depth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogDrawProperty(string groupName, string propertyPath, int indentLevel)
        {
            if (!ShouldLog(groupName))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}DrawProperty: group={groupName ?? "(null)"}, "
                    + $"property={propertyPath}, indentLevel={indentLevel}, "
                    + $"totalLeft={GroupGUIWidthUtility.CurrentLeftPadding:F2}, "
                    + $"totalRight={GroupGUIWidthUtility.CurrentRightPadding:F2}"
            );
        }
    }

    internal static class WGroupGUI
    {
        internal delegate bool PropertyOverride(
            SerializedObject owner,
            SerializedProperty property
        );

        public static void DrawRectUntinted(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            EditorGUI.DrawRect(rect, color);
            GUI.color = previousColor;
        }

        /// <summary>
        /// Draws a group using a pre-built property lookup to avoid FindProperty allocations.
        /// </summary>
        internal static void DrawGroup(
            WGroupDefinition definition,
            SerializedObject serializedObject,
            Dictionary<int, bool> foldoutStates,
            IReadOnlyDictionary<string, SerializedProperty> propertyLookup,
            PropertyOverride overrideDrawer = null
        )
        {
            DrawGroupInternal(
                definition,
                serializedObject,
                foldoutStates,
                propertyLookup,
                overrideDrawer
            );
        }

        internal static void DrawGroup(
            WGroupDefinition definition,
            SerializedObject serializedObject,
            Dictionary<int, bool> foldoutStates,
            PropertyOverride overrideDrawer = null
        )
        {
            DrawGroupInternal(definition, serializedObject, foldoutStates, null, overrideDrawer);
        }

        private static void DrawGroupInternal(
            WGroupDefinition definition,
            SerializedObject serializedObject,
            Dictionary<int, bool> foldoutStates,
            IReadOnlyDictionary<string, SerializedProperty> propertyLookup,
            PropertyOverride overrideDrawer
        )
        {
            if (definition == null || serializedObject == null)
            {
                return;
            }

            // Use a plain vertical group for layout, then manually draw the helpBox background.
            // This prevents helpBox styling from affecting child element rendering
            // (which was causing tinted headers on Unity's built-in ReorderableList for arrays/lists).
            EditorGUILayout.BeginVertical();
            Rect groupRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            {
                bool expanded = true;
                bool allowHeader = !definition.HideHeader;
                bool headerHasFoldout = HeaderHasFoldout(definition);
                AnimBool foldoutAnim = null;

                UnityEngine.Object targetObject =
                    serializedObject != null ? serializedObject.targetObject : null;
                int targetInstanceId = targetObject.GetUnityObjectId();

                if (headerHasFoldout)
                {
                    expanded = DrawFoldoutHeader(definition, foldoutStates, targetInstanceId);

                    // Initialize animation state only for collapsible groups when tweening is enabled
                    if (UnityHelpersSettings.ShouldTweenWGroupFoldouts())
                    {
                        foldoutAnim = WGroupAnimationState.GetOrCreateAnim(
                            definition,
                            expanded,
                            targetInstanceId
                        );
                    }
                }
                else if (allowHeader)
                {
                    DrawHeader(definition.DisplayName);
                }

                // Calculate fade progress for animated content
                float fade = foldoutAnim?.faded ?? (expanded ? 1f : 0f);

                if (foldoutAnim == null)
                {
                    // No animation - draw content immediately when expanded
                    if (expanded)
                    {
                        DrawGroupContent(
                            definition,
                            serializedObject,
                            foldoutStates,
                            propertyLookup,
                            overrideDrawer,
                            allowHeader
                        );
                    }
                }
                else
                {
                    // Animated - use fade group for smooth transition
                    bool visible = EditorGUILayout.BeginFadeGroup(fade);
                    if (visible)
                    {
                        DrawGroupContent(
                            definition,
                            serializedObject,
                            foldoutStates,
                            propertyLookup,
                            overrideDrawer,
                            allowHeader
                        );
                    }
                    EditorGUILayout.EndFadeGroup();
                }
            }

            EditorGUILayout.EndVertical();

            // Draw helpBox background manually after getting the final rect.
            // This preserves the visual appearance without affecting child rendering.
            if (Event.current.type == EventType.Repaint)
            {
                // Get the full group rect including any padding
                Rect backgroundRect = groupRect;
                // Add some padding to match helpBox appearance
                backgroundRect.x -= 3f;
                backgroundRect.width += 6f;
                backgroundRect.y -= 2f;
                backgroundRect.height += 4f;
                EditorStyles.helpBox.Draw(backgroundRect, false, false, false, false);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private static bool DrawFoldoutHeader(
            WGroupDefinition definition,
            Dictionary<int, bool> foldoutStates,
            int targetInstanceId
        )
        {
            int key = Objects.HashCode(
                definition.Name,
                definition.AnchorPropertyPath,
                targetInstanceId
            );
            if (foldoutStates != null && foldoutStates.TryGetValue(key, out bool expanded))
            {
                // value already loaded
            }
            else
            {
                expanded = !definition.StartCollapsed;
            }

            GUIStyle foldoutStyle = WGroupStyles.GetFoldoutStyle();
            float headerHeight = WGroupStyles.GetHeaderHeight();
            Rect headerRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                foldoutStyle,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(headerHeight)
            );

            bool headerHasFoldout = HeaderHasFoldout(definition);
            Rect foldoutRect = WGroupHeaderVisualUtility.GetContentRect(
                headerRect,
                WGroupStyles.HeaderTopPadding,
                WGroupStyles.HeaderBottomPadding,
                headerHasFoldout
            );

            int originalIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            expanded = EditorGUI.Foldout(
                foldoutRect,
                expanded,
                definition.DisplayName,
                true,
                foldoutStyle
            );
            EditorGUI.indentLevel = originalIndent;

            if (foldoutStates != null)
            {
                foldoutStates[key] = expanded;
            }

            GUILayout.Space(2f);
            return expanded;
        }

        private static Rect DrawHeader(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return Rect.zero;
            }

            GUIContent content = EditorGUIUtility.TrTextContent(displayName);
            GUIStyle labelStyle = WGroupStyles.GetHeaderLabelStyle();
            float headerHeight = WGroupStyles.GetHeaderHeight();
            Rect labelRect = GUILayoutUtility.GetRect(
                content,
                labelStyle,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(headerHeight)
            );
            Rect contentRect = WGroupHeaderVisualUtility.GetContentRect(
                labelRect,
                WGroupStyles.HeaderTopPadding,
                WGroupStyles.HeaderBottomPadding
            );
            GUI.Label(contentRect, content, labelStyle);
            GUILayout.Space(2f);
            return labelRect;
        }

        private static void DrawGroupContent(
            WGroupDefinition definition,
            SerializedObject serializedObject,
            Dictionary<int, bool> foldoutStates,
            IReadOnlyDictionary<string, SerializedProperty> propertyLookup,
            PropertyOverride overrideDrawer,
            bool allowHeader
        )
        {
            if (allowHeader)
            {
                EditorGUILayout.Space(2f);
            }
            EditorGUI.indentLevel++;

            float horizontalPadding = GroupGUIWidthUtility.CalculateHorizontalPadding(
                EditorStyles.helpBox,
                out float leftPadding,
                out float rightPadding
            );

            WGroupIndentDiagnostics.LogPushPadding(
                definition?.Name,
                horizontalPadding,
                leftPadding,
                rightPadding,
                EditorGUI.indentLevel
            );

            using (
                GroupGUIWidthUtility.PushContentPadding(
                    horizontalPadding,
                    leftPadding,
                    rightPadding
                )
            )
            {
                WGroupIndentDiagnostics.LogAfterPush(definition?.Name);

                IReadOnlyList<string> propertyPaths = definition.PropertyPaths;
                int propertyCount = propertyPaths.Count;
                if (propertyCount > 0)
                {
                    AddContentPadding();
                }

                // Build lookup of child groups by their anchor path
                Dictionary<string, WGroupDefinition> childByAnchor = null;
                if (definition.ChildGroups.Count > 0)
                {
                    childByAnchor = new Dictionary<string, WGroupDefinition>(
                        StringComparer.Ordinal
                    );
                    foreach (WGroupDefinition child in definition.ChildGroups)
                    {
                        childByAnchor[child.AnchorPropertyPath] = child;
                    }
                }

                for (int index = 0; index < propertyCount; index++)
                {
                    string propertyPath = propertyPaths[index];

                    // Check if this is a child group anchor
                    if (
                        childByAnchor != null
                        && childByAnchor.TryGetValue(propertyPath, out WGroupDefinition childGroup)
                    )
                    {
                        // Render child group recursively
                        DrawGroup(
                            childGroup,
                            serializedObject,
                            foldoutStates,
                            propertyLookup,
                            overrideDrawer
                        );
                        continue;
                    }

                    SerializedProperty property = ResolveProperty(
                        serializedObject,
                        propertyPath,
                        propertyLookup
                    );
                    if (property == null)
                    {
                        continue;
                    }

                    // Check WShowIf condition - this handles conditional visibility for all properties
                    // including arrays/lists which need editor-level handling since PropertyDrawers
                    // for attributes on arrays only affect elements, not the array itself
                    if (!WShowIfPropertyDrawer.ShouldShowProperty(property))
                    {
                        continue;
                    }

                    WGroupIndentDiagnostics.LogDrawProperty(
                        definition?.Name,
                        propertyPath,
                        EditorGUI.indentLevel
                    );

                    if (overrideDrawer != null && overrideDrawer(serializedObject, property))
                    {
                        continue;
                    }

                    // All properties are drawn within a WGroup property context.
                    // Custom drawers can check GroupGUIWidthUtility.IsInsideWGroupPropertyDraw
                    // to detect they're inside a WGroup and adjust their layout accordingly.
                    // Simple properties use indent compensation; complex properties with custom
                    // drawers can handle their own layout knowing they're in WGroup context.
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        if (property.hasVisibleChildren)
                        {
                            // Reset GUI.color to prevent helpBox background from tinting
                            // Unity's built-in ReorderableList header (for arrays/lists)
                            Color prevColor = GUI.color;
                            GUI.color = Color.white;
                            EditorGUILayout.PropertyField(property, true);
                            GUI.color = prevColor;
                        }
                        else
                        {
                            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
                                EditorGUILayout.PropertyField(property, true)
                            );
                        }
                    }
                }

                if (propertyCount > 0)
                {
                    AddContentPadding();
                }
            }

            WGroupIndentDiagnostics.LogAfterPop(definition?.Name);
            EditorGUI.indentLevel--;
        }

        private static bool HeaderHasFoldout(WGroupDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            if (definition.HideHeader)
            {
                return false;
            }

            return definition.Collapsible;
        }

        private static void AddContentPadding()
        {
            float spacing = Mathf.Max(1f, EditorGUIUtility.standardVerticalSpacing);
            GUILayout.Space(spacing);
        }

        private static SerializedProperty ResolveProperty(
            SerializedObject serializedObject,
            string propertyPath,
            IReadOnlyDictionary<string, SerializedProperty> propertyLookup
        )
        {
            if (
                propertyLookup != null
                && propertyLookup.TryGetValue(propertyPath, out SerializedProperty cached)
            )
            {
                return cached;
            }

            return serializedObject.FindProperty(propertyPath);
        }
    }

    internal static class WGroupStyles
    {
        internal const float HeaderTopPadding = 1f;
        internal const float HeaderBottomPadding = 1f;
        internal const float HeaderVerticalPadding = 4f;
        private static GUIStyle _foldoutStyle;
        private static GUIStyle _headerLabelStyle;

        internal static float GetHeaderHeight()
        {
            GUIStyle foldoutStyle = GetFoldoutStyle();
            GUIStyle headerStyle = GetHeaderLabelStyle();

            float foldoutLineHeight = Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                foldoutStyle.lineHeight
            );
            float headerLineHeight = Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                headerStyle.lineHeight
            );

            float tallestLineHeight = Mathf.Max(foldoutLineHeight, headerLineHeight);
            return tallestLineHeight + HeaderVerticalPadding;
        }

        internal static GUIStyle GetFoldoutStyle()
        {
            if (_foldoutStyle == null)
            {
                _foldoutStyle = new GUIStyle(EditorStyles.foldoutHeader)
                {
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(16, 6, 3, 3),
                };
            }
            return _foldoutStyle;
        }

        internal static GUIStyle GetHeaderLabelStyle()
        {
            if (_headerLabelStyle == null)
            {
                _headerLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(0, 4, 0, 0),
                };
            }

            return _headerLabelStyle;
        }
    }

    internal static class WGroupHeaderVisualUtility
    {
        private const float HorizontalContentPadding = 3f;
        private const float VerticalContentPaddingTop = 1f;
        private const float VerticalContentPaddingBottom = 3f;

        /// <summary>
        /// Additional left offset applied to foldout header content rects in Inspector context.
        /// </summary>
        /// <remarks>
        /// This offset ensures the foldout arrow is visually contained within the
        /// header background box. Combined with HorizontalContentPadding (3f) and
        /// the foldout style's internal left padding (16px), this creates proper
        /// visual encapsulation of the header content.
        /// Total left offset in Inspector: 3f (horizontal) + 9f (foldout) + 16f (style) = 28f
        /// </remarks>
        private const float FoldoutContentOffsetInspector = 9f;

        /// <summary>
        /// Additional left offset applied to foldout header content rects in Settings context.
        /// </summary>
        /// <remarks>
        /// In SettingsProvider contexts, the helpBox and ambient padding already provide
        /// sufficient visual structure. Using zero offset here prevents excessive indentation.
        /// Total left offset in Settings: 3f (horizontal) + 0f (foldout) + 16f (style) = 19f
        /// </remarks>
        private const float FoldoutContentOffsetSettings = 0f;

        private static bool _isSettingsContext;

        /// <summary>
        /// Gets or sets whether WGroup headers are being drawn in a SettingsProvider context.
        /// </summary>
        /// <remarks>
        /// When true, foldout headers use reduced left offset to prevent excessive indentation.
        /// This should be set to true before drawing WGroups in SettingsProvider.guiHandler
        /// and reset to false afterward.
        /// </remarks>
        internal static bool IsSettingsContext
        {
            get => _isSettingsContext;
            set => _isSettingsContext = value;
        }

        private static float CurrentFoldoutContentOffset =>
            _isSettingsContext ? FoldoutContentOffsetSettings : FoldoutContentOffsetInspector;

        /// <summary>
        /// A disposable scope that sets <see cref="IsSettingsContext"/> to true for its duration.
        /// </summary>
        /// <remarks>
        /// Use this scope when drawing WGroups in SettingsProvider contexts to ensure
        /// proper indentation behavior.
        /// </remarks>
        internal sealed class SettingsContextScope : IDisposable
        {
            private readonly bool _previousValue;
            private bool _disposed;

            public SettingsContextScope()
            {
                _previousValue = _isSettingsContext;
                _isSettingsContext = true;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _isSettingsContext = _previousValue;
            }
        }

        internal static Rect GetContentRect(
            Rect rect,
            float additionalTopPadding,
            float additionalBottomPadding,
            bool includeFoldoutOffset = false
        )
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return rect;
            }

            Rect contentRect = rect;
            float horizontal = HorizontalContentPadding;
            float topPadding = VerticalContentPaddingTop + Mathf.Max(0f, additionalTopPadding);
            float bottomPadding =
                VerticalContentPaddingBottom + Mathf.Max(0f, additionalBottomPadding);

            contentRect.xMin += horizontal;
            contentRect.xMax -= horizontal;

            if (includeFoldoutOffset)
            {
                contentRect.xMin += CurrentFoldoutContentOffset;
            }
            contentRect.yMin += topPadding;
            contentRect.yMax -= bottomPadding;

            if (contentRect.xMax < contentRect.xMin)
            {
                float centerX = rect.center.x;
                contentRect.xMin = centerX;
                contentRect.xMax = centerX;
            }

            if (contentRect.yMax < contentRect.yMin)
            {
                float centerY = rect.center.y;
                contentRect.yMin = centerY;
                contentRect.yMax = centerY;
            }

            return contentRect;
        }
    }
#endif
}
