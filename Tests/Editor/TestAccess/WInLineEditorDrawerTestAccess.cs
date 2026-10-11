// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Internal;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.WInLineEditorDrawer;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for WInLineEditorDrawer.</summary>
    internal static class WInLineEditorDrawerTestAccess
    {
        /// <summary>
        /// Test hook to check if the force serialized inspector flag is enabled.
        /// </summary>
        internal static bool ForceSerializedInspector => _forceSerializedInspector;

        /// <summary>
        /// Test hook to get extensive diagnostic info for debugging height calculation issues.
        /// </summary>
        internal static string GetExtensiveDiagnostics(
            SerializedProperty property,
            WInLineEditorAttribute inlineAttribute,
            Object value,
            float availableWidth
        )
        {
            if (value == null || property == null)
            {
                return "null property or value";
            }

            System.Text.StringBuilder sb = new();
            sb.AppendLine($"=== Extensive Diagnostics ===");
            sb.AppendLine($"Property path: {property.propertyPath}");
            sb.AppendLine($"Value type: {value.GetType().Name}");
            sb.AppendLine($"Available width: {availableWidth}");

            sb.AppendLine($"--- Attribute ---");
            sb.AppendLine($"  Mode: {inlineAttribute.Mode}");
            sb.AppendLine($"  DrawObjectField: {inlineAttribute.DrawObjectField}");
            sb.AppendLine($"  DrawHeader: {inlineAttribute.DrawHeader}");
            sb.AppendLine($"  EnableScrolling: {inlineAttribute.EnableScrolling}");
            sb.AppendLine($"  InspectorHeight: {inlineAttribute.InspectorHeight}");
            sb.AppendLine($"  MinInspectorWidth: {inlineAttribute.MinInspectorWidth}");
            sb.AppendLine(
                $"  HasExplicitMinInspectorWidth: {inlineAttribute.HasExplicitMinInspectorWidth}"
            );

            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(inlineAttribute);
            sb.AppendLine($"--- Mode Resolution ---");
            sb.AppendLine($"  Resolved mode: {resolvedMode}");
            if (inlineAttribute.Mode == WInLineEditorMode.UseSettings)
            {
                UnityHelpersSettings.InlineEditorFoldoutBehavior behavior =
                    UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
                sb.AppendLine($"  Settings behavior: {behavior}");
            }

            string foldoutKey = BuildFoldoutKey(property);
            bool foldoutInCache = InLineEditorSharedTestAccess.GetFoldoutState(foldoutKey);
            bool foldoutState = GetFoldoutState(property, inlineAttribute, resolvedMode);
            sb.AppendLine($"--- Foldout State ---");
            sb.AppendLine($"  Foldout key: {foldoutKey}");
            sb.AppendLine($"  In cache before GetFoldoutState: {foldoutInCache}");
            sb.AppendLine($"  GetFoldoutState result: {foldoutState}");

            bool useStandaloneHeader = InLineEditorShared.ShouldDrawStandaloneHeader(
                inlineAttribute
            );
            bool showHeader =
                useStandaloneHeader
                && (inlineAttribute.DrawHeader || resolvedMode != WInLineEditorMode.AlwaysExpanded);
            bool showBody = resolvedMode == WInLineEditorMode.AlwaysExpanded || foldoutState;
            sb.AppendLine($"--- Visibility ---");
            sb.AppendLine($"  useStandaloneHeader: {useStandaloneHeader}");
            sb.AppendLine($"  showHeader: {showHeader}");
            sb.AppendLine($"  showBody: {showBody}");

            sb.AppendLine($"--- Inspector Height ---");
            Editor editor = InLineEditorShared.GetOrCreateEditor(value);
            SerializedObject analysisObject = GetSerializedObjectForAnalysis(editor, value);
            using SerializedObject ownedAnalysisObject = editor == null ? analysisObject : null;
            bool hasSerializedData = analysisObject != null;
            bool hasSimpleLayout =
                hasSerializedData && SerializedObjectHasOnlySimpleProperties(analysisObject);
            bool canUseSerializedInspector =
                hasSerializedData && ShouldUseSerializedInspector(editor);
            sb.AppendLine(
                $"  Editor type: {(editor != null ? editor.GetType().FullName : "null")}"
            );
            sb.AppendLine($"  hasSerializedData: {hasSerializedData}");
            sb.AppendLine($"  hasSimpleLayout: {hasSimpleLayout}");
            sb.AppendLine($"  canUseSerializedInspector: {canUseSerializedInspector}");

            if (hasSerializedData)
            {
                float serializedHeight = CalculateSerializedInspectorHeight(analysisObject);
                sb.AppendLine($"  Serialized inspector height: {serializedHeight}");

                sb.AppendLine($"  --- Properties ---");
                analysisObject.UpdateIfRequiredOrScript();
                SerializedProperty iterator = analysisObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    float propHeight = EditorGUI.GetPropertyHeight(iterator, true);
                    bool isScript = string.Equals(
                        iterator.propertyPath,
                        InLineEditorShared.ScriptPropertyPath,
                        System.StringComparison.Ordinal
                    );
                    sb.AppendLine(
                        $"    {iterator.propertyPath}: {propHeight}px (type: {iterator.propertyType}){(isScript ? " [SCRIPT - skipped]" : "")}"
                    );
                    enterChildren = false;
                }
            }

            InspectorHeightInfo heightInfo = ResolveInspectorHeightInfo(
                value,
                inlineAttribute,
                availableWidth
            );
            sb.AppendLine($"--- Height Info Result ---");
            sb.AppendLine($"  ContentHeight: {heightInfo.ContentHeight}");
            sb.AppendLine($"  DisplayHeight: {heightInfo.DisplayHeight}");
            sb.AppendLine($"  UsesSerializedInspector: {heightInfo.UsesSerializedInspector}");
            sb.AppendLine($"  HorizontalScrollbarHeight: {heightInfo.HorizontalScrollbarHeight}");
            sb.AppendLine(
                $"  RequiresHorizontalScrollbar: {heightInfo.RequiresHorizontalScrollbar}"
            );
            sb.AppendLine($"  PaddingHeight: {heightInfo.PaddingHeight}");

            float inlineHeight = 0f;
            if (showHeader)
            {
                inlineHeight += InLineEditorShared.HeaderHeight + InLineEditorShared.Spacing;
            }
            if (showBody)
            {
                inlineHeight += heightInfo.DisplayHeight;
            }
            sb.AppendLine($"--- Final Inline Height ---");
            sb.AppendLine(
                $"  Header contribution: {(showHeader ? InLineEditorShared.HeaderHeight + InLineEditorShared.Spacing : 0f)}"
            );
            sb.AppendLine($"  Body contribution: {(showBody ? heightInfo.DisplayHeight : 0f)}");
            sb.AppendLine($"  Total inline height: {inlineHeight}");

            return sb.ToString();
        }

        internal static void ClearAnimationCache()
        {
            FoldoutAnimations.Clear();
        }

        /// <summary>
        /// Test hook to get the number of cached animation entries.
        /// </summary>
        internal static int GetAnimationCacheCount()
        {
            return FoldoutAnimations.Count;
        }

        /// <summary>
        /// Test hook to check if an animation entry exists for a specific key.
        /// </summary>
        internal static bool HasAnimationCacheEntry(string foldoutKey)
        {
            return FoldoutAnimations.ContainsKey(foldoutKey);
        }

        internal static void ClearCachedState()
        {
            InLineEditorSharedTestAccess.ClearCachedState();
            PropertyWidths.Clear();
            FoldoutKeyCache.Clear();
            ScrollKeyCache.Clear();
            InspectorHeightCache.Clear();
            _lastInspectorHeightCacheFrame = -1;
            ClearAnimationCache();
        }

        internal static void SetInlineFoldoutState(SerializedProperty property, bool expanded)
        {
            if (property == null)
            {
                return;
            }

            string key = BuildFoldoutKey(property);
            InLineEditorShared.SetFoldoutState(key, expanded);
        }

        internal static bool UsesHorizontalScrollbar(
            Object value,
            WInLineEditorAttribute inlineAttribute,
            float availableWidth
        )
        {
            if (value == null || inlineAttribute == null)
            {
                return false;
            }

            InspectorHeightInfo inspectorHeightInfo = ResolveInspectorHeightInfo(
                value,
                inlineAttribute,
                availableWidth
            );
            return inspectorHeightInfo.RequiresHorizontalScrollbar;
        }

        /// <summary>
        /// Test hook to directly check horizontal scrollbar requirement with explicit parameters.
        /// This bypasses editor creation and allows testing the decision logic directly.
        /// </summary>
        internal static bool RequiresHorizontalScrollbar(
            bool enableScrolling,
            float minInspectorWidth,
            bool hasExplicitMinInspectorWidth,
            bool hasSimpleLayout,
            float availableWidth
        )
        {
            float effectiveWidth = Mathf.Max(
                0f,
                availableWidth - (InLineEditorShared.ContentPadding * 2f)
            );

            const float MinimumUsableWidth = 200f;
            bool widthIsTooNarrow = effectiveWidth < MinimumUsableWidth;
            bool shouldRespectMinWidth =
                hasExplicitMinInspectorWidth || !hasSimpleLayout || widthIsTooNarrow;
            return enableScrolling
                && 0f < minInspectorWidth
                && shouldRespectMinWidth
                && 0.5f < minInspectorWidth - effectiveWidth;
        }

        /// <summary>
        /// Test hook to calculate label width for a given available width.
        /// </summary>
        internal static float CalculateLabelWidth(float availableWidth)
        {
            return availableWidth * InLineEditorShared.DefaultLabelWidthRatio;
        }

        /// <summary>
        /// Test hook to get detailed height calculation info for diagnostics.
        /// </summary>
        internal static (
            float baseHeight,
            float inlineHeight,
            bool showHeader,
            bool showBody,
            float displayHeight
        ) GetHeightCalculationDetails(
            SerializedProperty property,
            WInLineEditorAttribute inlineAttribute,
            Object value,
            float availableWidth
        )
        {
            if (value == null || property == null)
            {
                return (0f, 0f, false, false, 0f);
            }

            float baseHeight = inlineAttribute.DrawObjectField
                ? EditorGUI.GetPropertyHeight(property, GUIContent.none, false)
                : EditorGUIUtility.singleLineHeight;

            WInLineEditorMode mode = InLineEditorShared.ResolveMode(inlineAttribute);
            bool useStandaloneHeader = InLineEditorShared.ShouldDrawStandaloneHeader(
                inlineAttribute
            );
            bool showHeader =
                useStandaloneHeader
                && (inlineAttribute.DrawHeader || mode != WInLineEditorMode.AlwaysExpanded);
            bool foldoutState = GetFoldoutState(property, inlineAttribute, mode);
            bool showBody = mode == WInLineEditorMode.AlwaysExpanded || foldoutState;

            float inlineHeight = 0f;
            float displayHeight = 0f;
            if (showHeader)
            {
                inlineHeight += InLineEditorShared.HeaderHeight + InLineEditorShared.Spacing;
            }

            if (showBody)
            {
                InspectorHeightInfo inspectorHeightInfo = ResolveInspectorHeightInfo(
                    value,
                    inlineAttribute,
                    availableWidth
                );
                displayHeight = inspectorHeightInfo.DisplayHeight;
                inlineHeight += displayHeight;
            }

            return (baseHeight, inlineHeight, showHeader, showBody, displayHeight);
        }
    }
#endif
}
