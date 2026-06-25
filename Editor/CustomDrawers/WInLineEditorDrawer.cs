// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Portions of this file are adapted from Unity Editor Toolbox (InlineEditorAttributeDrawer)
// Copyright (c) 2017-2023 arimger
// Licensed under the MIT License: https://github.com/arimger/Unity-Editor-Toolbox/blob/main/LICENSE.md

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Internal;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    [CustomPropertyDrawer(typeof(WInLineEditorAttribute))]
    /// <summary>
    /// Portions of this implementation draw inspiration from Unity Editor Toolbox's Inline Editor drawer.
    /// Unity Editor Toolbox is Copyright (c) 2017-2023 arimger and distributed under the MIT License.
    /// Source: https://github.com/arimger/Unity-Editor-Toolbox (MIT)
    /// </summary>
    public sealed class WInLineEditorDrawer : PropertyDrawer
    {
        // Inspired by the Unity Editor Toolbox inline editor drawer (MIT):
        // https://github.com/arimger/Unity-Editor-Toolbox
        private const float FoldoutOffset = 6.5f;

        /// <summary>
        /// Ratio of the total content width allocated to field widths in inline editor UI.
        /// Fields display property values/controls on the right side of the inspector.
        /// </summary>
        private const float DefaultFieldWidthRatio = 0.6f;

        private static readonly Dictionary<string, float> PropertyWidths = new Dictionary<
            string,
            float
        >(System.StringComparer.Ordinal);

        private static readonly Dictionary<
            (long instanceId, string propertyPath),
            string
        > FoldoutKeyCache = new Dictionary<(long, string), string>();
        private static readonly Dictionary<
            (long instanceId, string propertyPath),
            string
        > ScrollKeyCache = new Dictionary<(long, string), string>();

        // Cache for InspectorHeightInfo to avoid redundant calculations within the same frame
        private static readonly Dictionary<
            (long instanceId, float width),
            InspectorHeightInfoCacheEntry
        > InspectorHeightCache = new Dictionary<(long, float), InspectorHeightInfoCacheEntry>();
        private static int _lastInspectorHeightCacheFrame = -1;

        // Animation cache for smooth foldout transitions
        private static readonly Dictionary<string, AnimBool> FoldoutAnimations = new Dictionary<
            string,
            AnimBool
        >(System.StringComparer.Ordinal);

        private sealed class InspectorHeightInfoCacheEntry
        {
            public InspectorHeightInfo heightInfo;
        }

        // Recursion guard to prevent EditorGUI.GetPropertyHeight from triggering
        // our GetPropertyHeight recursively
        [System.ThreadStatic]
        private static bool _isCalculatingHeight;

        // Since reflection-based width override is unreliable across Unity versions,
        // we use a simpler approach: always use the serialized inspector for inline editors.
        // This provides correct layout at the cost of custom editor features like buttons.
        // The _forceSerializedInspector flag can be toggled if needed.
        private static bool _forceSerializedInspector = true;

        private static float GetHorizontalScrollbarHeight()
        {
            // Avoid accessing GUI.skin outside OnGUI context - it throws an exception
            GUIStyle scrollbarStyle = null;
            try
            {
                scrollbarStyle = GUI.skin != null ? GUI.skin.horizontalScrollbar : null;
            }
            catch (System.ArgumentException)
            {
                // Occurs when called outside OnGUI - use fallback
            }

            float height =
                scrollbarStyle != null && scrollbarStyle.fixedHeight > 0f
                    ? scrollbarStyle.fixedHeight
                    : EditorGUIUtility.singleLineHeight;
            return Mathf.Max(12f, height);
        }

        private static void SetPropertyWidth(SerializedProperty property, float width)
        {
            if (property == null)
            {
                return;
            }

            string key = BuildFoldoutKey(property);
            PropertyWidths[key] = Mathf.Max(0f, width);
        }

        private static float GetEstimatedPropertyWidth(SerializedProperty property)
        {
            if (property != null)
            {
                string key = BuildFoldoutKey(property);
                if (PropertyWidths.TryGetValue(key, out float width) && width > 0f)
                {
                    return width;
                }
            }

            // EditorGUIUtility.currentViewWidth throws when called outside OnGUI context
            try
            {
                return EditorGUIUtility.currentViewWidth;
            }
            catch (System.ArgumentException)
            {
                // Fallback for calls outside OnGUI - use a reasonable default
                return 300f;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Guard against recursive calls - if we're already calculating height,
            // just return the base property height to prevent infinite recursion
            if (_isCalculatingHeight)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)attribute;

            float height;
            try
            {
                _isCalculatingHeight = true;
                height = inlineAttribute.DrawObjectField
                    ? EditorGUI.GetPropertyHeight(property, label, false)
                    : EditorGUIUtility.singleLineHeight;
            }
            finally
            {
                _isCalculatingHeight = false;
            }

            Object value = property.hasMultipleDifferentValues
                ? null
                : property.objectReferenceValue;
            if (value == null || property.propertyType != SerializedPropertyType.ObjectReference)
            {
                return height;
            }

            float availableWidth = GetEstimatedPropertyWidth(property);
            float inlineHeight = CalculateInlineHeight(
                property,
                inlineAttribute,
                value,
                availableWidth
            );
            return height
                + (
                    inlineHeight <= 0f
                        ? 0f
                        : EditorGUIUtility.standardVerticalSpacing + inlineHeight
                );
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)attribute;
            EditorGUI.BeginProperty(position, label, property);

            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.HelpBox(
                    position,
                    "WInLineEditor only supports object references.",
                    MessageType.Warning
                );
                EditorGUI.EndProperty();
                return;
            }

            if (property.hasMultipleDifferentValues)
            {
                if (inlineAttribute.DrawObjectField)
                {
                    EditorGUI.PropertyField(position, property, label, false);
                }
                else
                {
                    EditorGUI.LabelField(position, label);
                }

                EditorGUI.EndProperty();
                return;
            }

            SetPropertyWidth(property, position.width);

            float fieldHeight = inlineAttribute.DrawObjectField
                ? EditorGUI.GetPropertyHeight(property, label, false)
                : EditorGUIUtility.singleLineHeight;
            Rect currentRect = new Rect(position.x, position.y, position.width, fieldHeight);

            WInLineEditorMode mode = InLineEditorShared.ResolveMode(inlineAttribute);
            string foldoutKey = BuildFoldoutKey(property);
            bool foldoutState = GetFoldoutState(property, inlineAttribute, mode);

            if (inlineAttribute.DrawObjectField)
            {
                foldoutState = DrawInlineObjectReferenceField(
                    currentRect,
                    property,
                    label,
                    foldoutState,
                    foldoutKey,
                    mode
                );
            }
            else
            {
                foldoutState = DrawCompactObjectReferenceField(
                    currentRect,
                    property,
                    label,
                    foldoutState,
                    foldoutKey,
                    mode
                );
            }

            currentRect.y += currentRect.height + EditorGUIUtility.standardVerticalSpacing;

            Object value = property.objectReferenceValue;
            if (value != null)
            {
                float inlineHeight = CalculateInlineHeight(
                    property,
                    inlineAttribute,
                    value,
                    currentRect.width
                );
                if (inlineHeight > 0f)
                {
                    InspectorHeightInfo inspectorHeightInfo = ResolveInspectorHeightInfo(
                        value,
                        inlineAttribute,
                        currentRect.width
                    );
                    Rect inlineRect = new Rect(
                        currentRect.x,
                        currentRect.y,
                        currentRect.width,
                        inlineHeight
                    );
                    DrawInlineInspector(
                        inlineRect,
                        property,
                        inlineAttribute,
                        label,
                        value,
                        foldoutState,
                        foldoutKey,
                        mode,
                        inspectorHeightInfo
                    );
                }
            }

            EditorGUI.EndProperty();
        }

        private static float CalculateInlineHeight(
            SerializedProperty property,
            WInLineEditorAttribute inlineAttribute,
            Object value,
            float availableWidth
        )
        {
            WInLineEditorMode mode = InLineEditorShared.ResolveMode(inlineAttribute);
            bool useStandaloneHeader = InLineEditorShared.ShouldDrawStandaloneHeader(
                inlineAttribute
            );
            bool showHeader =
                useStandaloneHeader
                && (inlineAttribute.DrawHeader || mode != WInLineEditorMode.AlwaysExpanded);
            bool foldoutState = GetFoldoutState(property, inlineAttribute, mode);
            bool isAlwaysExpanded = mode == WInLineEditorMode.AlwaysExpanded;
            bool showBody = isAlwaysExpanded || foldoutState;

            float height = 0f;
            if (showHeader)
            {
                height += InLineEditorShared.HeaderHeight + InLineEditorShared.Spacing;
            }

            // Calculate body height - when tweening, we need the full height for animation
            bool shouldTween = UnityHelpersSettings.ShouldTweenInlineEditorFoldouts();
            bool canAnimate = !isAlwaysExpanded && shouldTween;

            // If not showing body and not animating, return header-only height
            if (!showBody && !canAnimate)
            {
                return height;
            }

            // Calculate the full body height
            InspectorHeightInfo inspectorHeight = ResolveInspectorHeightInfo(
                value,
                inlineAttribute,
                availableWidth
            );
            float bodyHeight = inspectorHeight.DisplayHeight;
            if (inlineAttribute.DrawPreview)
            {
                bodyHeight += InLineEditorShared.Spacing + inlineAttribute.PreviewHeight;
            }

            // Apply animation fade to body height
            if (canAnimate)
            {
                string foldoutKey = BuildFoldoutKey(property);
                float fade = GetFadeProgress(foldoutKey, foldoutState);
                height += bodyHeight * fade;
            }
            else
            {
                height += bodyHeight;
            }

            return height;
        }

        private static bool DrawInlineObjectReferenceField(
            Rect rect,
            SerializedProperty property,
            GUIContent label,
            bool foldoutState,
            string foldoutKey,
            WInLineEditorMode mode
        )
        {
            Rect indentedRect = EditorGUI.IndentedRect(rect);
            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float labelWidth = Mathf.Min(EditorGUIUtility.labelWidth, indentedRect.width);
            Rect labelRect = new Rect(
                indentedRect.x,
                indentedRect.y,
                labelWidth,
                indentedRect.height
            );
            Rect fieldRect = new Rect(
                labelRect.xMax,
                indentedRect.y,
                Mathf.Max(0f, indentedRect.width - labelWidth),
                indentedRect.height
            );

            EditorGUI.ObjectField(fieldRect, property, GUIContent.none);
            Object currentValue = property.objectReferenceValue;

            bool showFoldoutToggle =
                currentValue != null && mode != WInLineEditorMode.AlwaysExpanded;
            bool showPingButton = InLineEditorShared.ShouldShowPingButton(currentValue);
            float pingWidth = showPingButton ? InLineEditorShared.GetPingButtonWidth() : 0f;
            float pingSpacing = showPingButton ? InLineEditorShared.Spacing : 0f;
            float pingRightMargin = showPingButton ? InLineEditorShared.PingButtonRightMargin : 0f;
            bool hasSpaceForPing =
                showPingButton
                && labelRect.width - pingWidth - pingSpacing - pingRightMargin
                    >= InLineEditorShared.MinimumFoldoutLabelWidth;
            if (!hasSpaceForPing)
            {
                showPingButton = false;
                pingWidth = 0f;
                pingSpacing = 0f;
                pingRightMargin = 0f;
            }

            float foldoutWidth = Mathf.Max(
                0f,
                showPingButton
                    ? labelRect.width - pingWidth - pingSpacing - pingRightMargin
                    : labelRect.width
            );
            Rect foldoutRect = new Rect(labelRect.x, labelRect.y, foldoutWidth, labelRect.height);

            GUIContent foldoutLabel = label ?? GUIContent.none;
            if (showFoldoutToggle)
            {
                Rect adjustedFoldoutRect = new Rect(
                    foldoutRect.x + FoldoutOffset,
                    foldoutRect.y,
                    Mathf.Max(0f, foldoutRect.width - FoldoutOffset),
                    foldoutRect.height
                );
                bool newState = EditorGUI.Foldout(
                    adjustedFoldoutRect,
                    foldoutState,
                    foldoutLabel,
                    true
                );
                if (newState != foldoutState)
                {
                    foldoutState = newState;
                    SetFoldoutState(foldoutKey, foldoutState);
                }
            }
            else
            {
                EditorGUI.LabelField(foldoutRect, foldoutLabel);
            }

            if (showPingButton)
            {
                Rect pingRect = new Rect(
                    foldoutRect.x + foldoutRect.width + pingSpacing,
                    labelRect.y,
                    pingWidth,
                    labelRect.height
                );
                using (new EditorGUI.DisabledScope(currentValue == null))
                {
                    if (
                        GUI.Button(
                            pingRect,
                            InLineEditorShared.PingButtonContent,
                            EditorStyles.miniButton
                        )
                    )
                    {
                        EditorGUIUtility.PingObject(currentValue);
                    }
                }
            }

            EditorGUI.indentLevel = previousIndent;
            return foldoutState;
        }

        private static bool DrawCompactObjectReferenceField(
            Rect rect,
            SerializedProperty property,
            GUIContent label,
            bool foldoutState,
            string foldoutKey,
            WInLineEditorMode mode
        )
        {
            // Compact mode: draw label on left, small object picker on right
            // This allows object selection while hiding the full object field
            Rect indentedRect = EditorGUI.IndentedRect(rect);
            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // Reserve space for a small object picker on the right
            const float pickerWidth = 20f;
            const float pickerSpacing = 2f;

            float availableLabelWidth = Mathf.Max(
                0f,
                indentedRect.width - pickerWidth - pickerSpacing
            );
            Rect labelRect = new Rect(
                indentedRect.x,
                indentedRect.y,
                availableLabelWidth,
                indentedRect.height
            );
            Rect pickerRect = new Rect(
                labelRect.xMax + pickerSpacing,
                indentedRect.y,
                pickerWidth,
                indentedRect.height
            );

            Object currentValue = property.objectReferenceValue;
            bool showFoldoutToggle =
                currentValue != null && mode != WInLineEditorMode.AlwaysExpanded;

            GUIContent foldoutLabel = label ?? GUIContent.none;
            if (showFoldoutToggle)
            {
                Rect adjustedFoldoutRect = new Rect(
                    labelRect.x + FoldoutOffset,
                    labelRect.y,
                    Mathf.Max(0f, labelRect.width - FoldoutOffset),
                    labelRect.height
                );
                bool newState = EditorGUI.Foldout(
                    adjustedFoldoutRect,
                    foldoutState,
                    foldoutLabel,
                    true
                );
                if (newState != foldoutState)
                {
                    foldoutState = newState;
                    SetFoldoutState(foldoutKey, foldoutState);
                }
            }
            else
            {
                EditorGUI.LabelField(labelRect, foldoutLabel);
            }

            // Draw a minimal object picker field (just the circle button)
            EditorGUI.ObjectField(pickerRect, property, GUIContent.none);

            EditorGUI.indentLevel = previousIndent;
            return foldoutState;
        }

        private static void DrawInlineInspector(
            Rect rect,
            SerializedProperty property,
            WInLineEditorAttribute inlineAttribute,
            GUIContent label,
            Object value,
            bool foldoutState,
            string foldoutKey,
            WInLineEditorMode mode,
            InspectorHeightInfo inspectorHeight
        )
        {
            bool useStandaloneHeader = InLineEditorShared.ShouldDrawStandaloneHeader(
                inlineAttribute
            );
            bool showHeader =
                useStandaloneHeader
                && (inlineAttribute.DrawHeader || mode != WInLineEditorMode.AlwaysExpanded);

            if (showHeader)
            {
                Rect headerRect = new Rect(
                    rect.x,
                    rect.y,
                    rect.width,
                    InLineEditorShared.HeaderHeight
                );
                bool showFoldoutToggle = mode != WInLineEditorMode.AlwaysExpanded;
                foldoutState = DrawHeader(
                    headerRect,
                    property,
                    value,
                    label,
                    showFoldoutToggle,
                    foldoutState
                );
                SetFoldoutState(foldoutKey, foldoutState);
                rect.y += InLineEditorShared.HeaderHeight + InLineEditorShared.Spacing;
                rect.height -= InLineEditorShared.HeaderHeight + InLineEditorShared.Spacing;
            }

            bool isAlwaysExpanded = mode == WInLineEditorMode.AlwaysExpanded;
            bool shouldTween = UnityHelpersSettings.ShouldTweenInlineEditorFoldouts();
            bool canAnimate = !isAlwaysExpanded && shouldTween;

            // Determine if we should show the body content
            bool showBody = isAlwaysExpanded || foldoutState;

            // When animating, use fade group for smooth transitions
            if (canAnimate)
            {
                float fade = GetFadeProgress(foldoutKey, foldoutState);
                bool visible = EditorGUILayout.BeginFadeGroup(fade);
                if (visible)
                {
                    DrawInlineInspectorBody(
                        rect,
                        property,
                        inlineAttribute,
                        value,
                        inspectorHeight
                    );
                }
                EditorGUILayout.EndFadeGroup();
            }
            else if (showBody)
            {
                DrawInlineInspectorBody(rect, property, inlineAttribute, value, inspectorHeight);
            }
        }

        private static void DrawInlineInspectorBody(
            Rect rect,
            SerializedProperty property,
            WInLineEditorAttribute inlineAttribute,
            Object value,
            InspectorHeightInfo inspectorHeight
        )
        {
            Editor editor = InLineEditorShared.GetOrCreateEditor(value);
            if (editor == null)
            {
                return;
            }

            Rect inspectorRect = new Rect(
                rect.x,
                rect.y,
                rect.width,
                inspectorHeight.DisplayHeight
            );
            DrawInspectorBody(property, inspectorRect, editor, inlineAttribute, inspectorHeight);
            rect.y += inspectorHeight.DisplayHeight;

            if (inlineAttribute.DrawPreview && editor.HasPreviewGUI())
            {
                rect.y += InLineEditorShared.Spacing;
                Rect previewRect = new Rect(
                    rect.x,
                    rect.y,
                    rect.width,
                    inlineAttribute.PreviewHeight
                );
                GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);
                Rect previewContentRect = new Rect(
                    previewRect.x + 2f,
                    previewRect.y + 2f,
                    previewRect.width - 4f,
                    previewRect.height - 4f
                );
                editor.OnPreviewGUI(previewContentRect, GUIStyle.none);
            }
        }

        private static void DrawInspectorBody(
            SerializedProperty property,
            Rect rect,
            Editor editor,
            WInLineEditorAttribute inlineAttribute,
            InspectorHeightInfo inspectorHeight
        )
        {
            Rect backgroundRect = new Rect(rect.x, rect.y, rect.width, rect.height);
            GUI.Box(backgroundRect, GUIContent.none, EditorStyles.helpBox);
            Rect contentRect = GetInlineContentRect(backgroundRect);

            string scrollKey = BuildScrollKey(property);
            bool useSerializedInspector = inspectorHeight.UsesSerializedInspector;
            bool needsHorizontalScroll =
                inlineAttribute.EnableScrolling && inspectorHeight.RequiresHorizontalScrollbar;
            bool needsVerticalScroll =
                inlineAttribute.EnableScrolling
                && inspectorHeight.ContentHeight > contentRect.height + 0.5f;
            bool useScrollView =
                inlineAttribute.EnableScrolling && (needsHorizontalScroll || needsVerticalScroll);

            // Save editor state - will be restored after drawing
            int previousIndentLevel = EditorGUI.indentLevel;

            // Reset indent level to 0 since we're starting fresh in the inline area
            EditorGUI.indentLevel = 0;

            try
            {
                if (useScrollView)
                {
                    Vector2 scrollPosition = InLineEditorShared.GetScrollPosition(scrollKey);
                    float viewWidth = needsHorizontalScroll
                        ? Mathf.Max(inlineAttribute.MinInspectorWidth, contentRect.width)
                        : contentRect.width;
                    float viewHeight = inspectorHeight.ContentHeight;

                    // Use absolute coordinates for the scroll view
                    Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
                    scrollPosition = GUI.BeginScrollView(
                        contentRect,
                        scrollPosition,
                        viewRect,
                        needsHorizontalScroll,
                        needsVerticalScroll
                    );
                    // Inside scroll view, coordinates are relative to the view
                    DrawInspectorContents(editor, useSerializedInspector, viewRect);
                    GUI.EndScrollView();

                    InLineEditorShared.SetScrollPosition(scrollKey, scrollPosition);
                    return;
                }

                // For non-scrolling content, use GUI.BeginGroup to establish coordinate
                // transformation, then call DrawInspectorContents with a rect at origin.
                GUI.BeginGroup(contentRect);
                Rect drawRect = new Rect(0f, 0f, contentRect.width, inspectorHeight.ContentHeight);
                DrawInspectorContents(editor, useSerializedInspector, drawRect);
                GUI.EndGroup();
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        private static void DrawInspectorContents(
            Editor editor,
            bool useSerializedInspector,
            Rect rect
        )
        {
            if (editor == null)
            {
                return;
            }

            // Due to Unity's EditorGUILayout width calculation limitations (it uses the read-only
            // currentViewWidth instead of respecting GUILayout.BeginArea bounds), we always use
            // the rect-based serialized inspector approach for inline editors.
            //
            // This provides correct layout and label/field proportions, but means custom editor
            // features (like buttons from WButtonInspector) won't be rendered inside inline editors.
            // The trade-off is necessary for correct visual layout.
            //
            // If _forceSerializedInspector is false, we attempt to use the custom editor, but
            // it will likely have the 50% width issue.

            if (useSerializedInspector || _forceSerializedInspector)
            {
                DrawSerializedInspector(rect, editor);
                return;
            }

            // Fallback path for custom editors (known to have width issues)
            // This path is only taken if _forceSerializedInspector is explicitly set to false

            // Save current values
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            float previousFieldWidth = EditorGUIUtility.fieldWidth;

            // Set labelWidth based on our rect width
            float contentWidth = rect.width;
            EditorGUIUtility.labelWidth = contentWidth * InLineEditorShared.DefaultLabelWidthRatio;
            EditorGUIUtility.fieldWidth = contentWidth * DefaultFieldWidthRatio;

            try
            {
                GUILayout.BeginArea(rect);
                using (InlineInspectorContext.Enter())
                {
                    editor.OnInspectorGUI();
                }
                GUILayout.EndArea();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUIUtility.fieldWidth = previousFieldWidth;
            }
        }

        private static void DrawSerializedInspector(Rect rect, Editor editor)
        {
            SerializedObject serializedObject = editor.serializedObject;
            InLineEditorShared.DrawSerializedObjectInRect(rect, serializedObject);
        }

        private static InspectorHeightInfo ResolveInspectorHeightInfo(
            Object value,
            WInLineEditorAttribute inlineAttribute,
            float availableWidth
        )
        {
            if (value == null)
            {
                return InspectorHeightInfo.Empty;
            }

            // Check frame-based cache to avoid redundant calculations
            int currentFrame = Time.frameCount;
            if (_lastInspectorHeightCacheFrame != currentFrame)
            {
                InspectorHeightCache.Clear();
                _lastInspectorHeightCacheFrame = currentFrame;
            }

            long instanceId = value.GetUnityObjectId();
            // Round width to avoid cache misses from floating point variations
            float roundedWidth = Mathf.Round(availableWidth);
            (long, float) cacheKey = (instanceId, roundedWidth);

            if (
                InspectorHeightCache.TryGetValue(cacheKey, out InspectorHeightInfoCacheEntry cached)
            )
            {
                return cached.heightInfo;
            }

            InspectorHeightInfo result = CalculateInspectorHeightInfoUncached(
                value,
                inlineAttribute,
                availableWidth
            );

            if (!InspectorHeightCache.TryGetValue(cacheKey, out cached))
            {
                cached = new InspectorHeightInfoCacheEntry();
                InspectorHeightCache[cacheKey] = cached;
            }
            cached.heightInfo = result;

            return result;
        }

        private static InspectorHeightInfo CalculateInspectorHeightInfoUncached(
            Object value,
            WInLineEditorAttribute inlineAttribute,
            float availableWidth
        )
        {
            Editor editor = InLineEditorShared.GetOrCreateEditor(value);
            SerializedObject analysisObject = GetSerializedObjectForAnalysis(editor, value);
            bool hasSerializedData = analysisObject != null;
            bool hasSimpleLayout =
                hasSerializedData && SerializedObjectHasOnlySimpleProperties(analysisObject);
            bool canUseSerializedInspector =
                hasSerializedData && ShouldUseSerializedInspector(editor);

            if (TryCalculateSerializedInspectorHeight(analysisObject, out float contentHeight))
            {
                InspectorHeightInfo info = BuildInspectorHeightInfo(
                    inlineAttribute,
                    availableWidth,
                    contentHeight,
                    canUseSerializedInspector,
                    hasSimpleLayout
                );
                return info;
            }

            float fallbackHeight = inlineAttribute.InspectorHeight;
            return BuildInspectorHeightInfo(
                inlineAttribute,
                availableWidth,
                fallbackHeight,
                canUseSerializedInspector,
                hasSimpleLayout
            );
        }

        private static InspectorHeightInfo BuildInspectorHeightInfo(
            WInLineEditorAttribute inlineAttribute,
            float availableWidth,
            float contentHeight,
            bool usesSerializedInspector,
            bool hasSimpleLayout
        )
        {
            float displayHeight = inlineAttribute.EnableScrolling
                ? Mathf.Min(contentHeight, inlineAttribute.InspectorHeight)
                : contentHeight;

            float effectiveWidth = Mathf.Max(
                0f,
                availableWidth - (InLineEditorShared.ContentPadding * 2f)
            );
            // Enable horizontal scroll when:
            // 1. Scrolling is enabled AND
            // 2. MinInspectorWidth is set AND
            // 3. Either: user explicitly set MinInspectorWidth, OR layout is complex, OR width is very narrow
            const float MinimumUsableWidth = 200f;
            bool widthIsTooNarrow = effectiveWidth < MinimumUsableWidth;
            bool shouldRespectMinWidth =
                inlineAttribute.HasExplicitMinInspectorWidth
                || !hasSimpleLayout
                || widthIsTooNarrow;
            bool requiresHorizontalScroll =
                inlineAttribute.EnableScrolling
                && inlineAttribute.MinInspectorWidth > 0f
                && shouldRespectMinWidth
                && inlineAttribute.MinInspectorWidth - effectiveWidth > 0.5f;
            float horizontalScrollbarHeight = requiresHorizontalScroll
                ? GetHorizontalScrollbarHeight()
                : 0f;

            float paddingContribution = InLineEditorShared.ContentPadding * 2f;
            float finalDisplayHeight =
                displayHeight + horizontalScrollbarHeight + paddingContribution;
            return new InspectorHeightInfo(
                contentHeight,
                finalDisplayHeight,
                usesSerializedInspector,
                horizontalScrollbarHeight,
                requiresHorizontalScroll,
                paddingContribution
            );
        }

        private static bool TryCalculateSerializedInspectorHeight(
            SerializedObject serializedObject,
            out float contentHeight
        )
        {
            contentHeight = 0f;
            if (serializedObject == null)
            {
                return false;
            }

            contentHeight = CalculateSerializedInspectorHeight(serializedObject);
            return true;
        }

        private static bool ShouldUseSerializedInspector(Editor editor)
        {
            if (editor == null)
            {
                return true;
            }

            return editor.GetType() == typeof(Editor);
        }

        private static float CalculateSerializedInspectorHeight(SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                return 0f;
            }

            serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            float height = 0f;
            bool firstPropertyMeasured = false;
            while (iterator.NextVisible(enterChildren))
            {
                if (
                    string.Equals(
                        iterator.propertyPath,
                        InLineEditorShared.ScriptPropertyPath,
                        System.StringComparison.Ordinal
                    )
                )
                {
                    enterChildren = false;
                    continue;
                }

                if (firstPropertyMeasured)
                {
                    height += EditorGUIUtility.standardVerticalSpacing;
                }

                height += EditorGUI.GetPropertyHeight(iterator, true);
                enterChildren = false;
                firstPropertyMeasured = true;
            }

            return Mathf.Max(0f, height);
        }

        private static SerializedObject GetSerializedObjectForAnalysis(Editor editor, Object value)
        {
            if (!SupportsSerializedInspectorTarget(value))
            {
                return null;
            }

            SerializedObject serializedObject =
                editor != null ? editor.serializedObject : new SerializedObject(value);
            return serializedObject;
        }

        private static bool SupportsSerializedInspectorTarget(Object value)
        {
            return value is ScriptableObject || value is MonoBehaviour;
        }

        private static bool SerializedObjectHasOnlySimpleProperties(
            SerializedObject serializedObject
        )
        {
            if (serializedObject == null)
            {
                return false;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            bool hasAnyProperty = false;
            while (iterator.NextVisible(enterChildren))
            {
                if (
                    string.Equals(
                        iterator.propertyPath,
                        InLineEditorShared.ScriptPropertyPath,
                        System.StringComparison.Ordinal
                    )
                )
                {
                    enterChildren = false;
                    continue;
                }

                hasAnyProperty = true;
                if (!IsSimpleSerializedProperty(iterator))
                {
                    return false;
                }

                enterChildren = false;
            }

            return hasAnyProperty;
        }

        private static bool IsSimpleSerializedProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return true;
            }

            // Check property type BEFORE isArray, since strings are arrays internally
            // but should be considered simple (they render as single-line text fields)
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    return true;
                case SerializedPropertyType.Generic:
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Gradient:
                case SerializedPropertyType.ManagedReference:
                    return false;
            }

            if (property.isArray)
            {
                return false;
            }

            return true;
        }

        private readonly struct InspectorHeightInfo
        {
            public InspectorHeightInfo(
                float contentHeight,
                float displayHeight,
                bool usesSerializedInspector,
                float horizontalScrollbarHeight,
                bool requiresHorizontalScrollbar,
                float paddingHeight
            )
            {
                ContentHeight = Mathf.Max(0f, contentHeight);
                DisplayHeight = Mathf.Max(0f, displayHeight);
                UsesSerializedInspector = usesSerializedInspector;
                HorizontalScrollbarHeight = Mathf.Max(0f, horizontalScrollbarHeight);
                RequiresHorizontalScrollbar = requiresHorizontalScrollbar;
                PaddingHeight = Mathf.Max(0f, paddingHeight);
            }

            public float ContentHeight { get; }
            public float DisplayHeight { get; }
            public bool UsesSerializedInspector { get; }
            public float HorizontalScrollbarHeight { get; }
            public bool RequiresHorizontalScrollbar { get; }
            public float PaddingHeight { get; }

            public static InspectorHeightInfo Empty =>
                new InspectorHeightInfo(0f, 0f, false, 0f, false, 0f);
        }

        private static Rect GetInlineContentRect(Rect backgroundRect)
        {
            return new Rect(
                backgroundRect.x + InLineEditorShared.ContentPadding,
                backgroundRect.y + InLineEditorShared.ContentPadding,
                backgroundRect.width - (InLineEditorShared.ContentPadding * 2f),
                Mathf.Max(0f, backgroundRect.height - (InLineEditorShared.ContentPadding * 2f))
            );
        }

        internal static Rect GetInlineContentRectForTesting(Rect backgroundRect)
        {
            return GetInlineContentRect(backgroundRect);
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
        ) GetHeightCalculationDetailsForTesting(
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

        /// <summary>
        /// Test hook to get extensive diagnostic info for debugging height calculation issues.
        /// </summary>
        internal static string GetExtensiveDiagnosticsForTesting(
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

            // Attribute info
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

            // Mode resolution
            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(inlineAttribute);
            sb.AppendLine($"--- Mode Resolution ---");
            sb.AppendLine($"  Resolved mode: {resolvedMode}");
            if (inlineAttribute.Mode == WInLineEditorMode.UseSettings)
            {
                UnityHelpersSettings.InlineEditorFoldoutBehavior behavior =
                    UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
                sb.AppendLine($"  Settings behavior: {behavior}");
            }

            // Foldout state
            string foldoutKey = BuildFoldoutKey(property);
            bool foldoutInCache = InLineEditorShared.GetFoldoutStateForTesting(foldoutKey);
            bool foldoutState = GetFoldoutState(property, inlineAttribute, resolvedMode);
            sb.AppendLine($"--- Foldout State ---");
            sb.AppendLine($"  Foldout key: {foldoutKey}");
            sb.AppendLine($"  In cache before GetFoldoutState: {foldoutInCache}");
            sb.AppendLine($"  GetFoldoutState result: {foldoutState}");

            // Header/body visibility
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

            // Inspector height info
            sb.AppendLine($"--- Inspector Height ---");
            Editor editor = InLineEditorShared.GetOrCreateEditor(value);
            SerializedObject analysisObject = GetSerializedObjectForAnalysis(editor, value);
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

                // List all properties
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

            // Final calculation
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

        private static bool DrawHeader(
            Rect rect,
            SerializedProperty property,
            Object value,
            GUIContent label,
            bool showFoldoutToggle,
            bool foldoutState
        )
        {
            float pingWidth = InLineEditorShared.GetPingButtonWidth();
            bool showPingButton = InLineEditorShared.ShouldShowPingButton(value);
            float headerSpacing = 0f;
            float headerRightMargin = 0f;
            if (showPingButton)
            {
                headerSpacing = InLineEditorShared.HeaderPingSpacing;
                headerRightMargin = InLineEditorShared.PingButtonRightMargin;
                bool hasSpace =
                    rect.width - pingWidth - headerSpacing - headerRightMargin
                    >= InLineEditorShared.MinimumFoldoutLabelWidth;
                if (!hasSpace)
                {
                    showPingButton = false;
                    headerSpacing = 0f;
                    headerRightMargin = 0f;
                }
            }
            float labelWidth = showPingButton
                ? Mathf.Max(0f, rect.width - pingWidth - headerSpacing - headerRightMargin)
                : rect.width;
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect pingRect = new Rect(
                rect.x + labelWidth + (showPingButton ? headerSpacing : 0f),
                rect.y,
                pingWidth,
                rect.height
            );

            GUIContent headerContent = InLineEditorShared.PrepareHeaderContent(value, label);

            if (showFoldoutToggle)
            {
                bool newState = EditorGUI.Foldout(labelRect, foldoutState, headerContent, true);
                if (newState != foldoutState)
                {
                    foldoutState = newState;
                }
            }
            else
            {
                EditorGUI.LabelField(labelRect, headerContent, EditorStyles.boldLabel);
            }

            if (showPingButton)
            {
                using (new EditorGUI.DisabledScope(value == null))
                {
                    if (
                        GUI.Button(
                            pingRect,
                            InLineEditorShared.PingButtonContent,
                            EditorStyles.miniButton
                        )
                    )
                    {
                        EditorGUIUtility.PingObject(value);
                    }
                }
            }

            return foldoutState;
        }

        private static string BuildFoldoutKey(SerializedProperty property)
        {
            Object target =
                property.serializedObject != null ? property.serializedObject.targetObject : null;
            long id = target != null ? target.GetUnityObjectId() : 0;
            string propertyPath = property.propertyPath;
            (long, string) cacheKey = (id, propertyPath);
            if (!FoldoutKeyCache.TryGetValue(cacheKey, out string key))
            {
                key = InLineEditorShared.BuildFoldoutKey(id, propertyPath);
                FoldoutKeyCache[cacheKey] = key;
            }
            return key;
        }

        private static string BuildScrollKey(SerializedProperty property)
        {
            Object target =
                property.serializedObject != null ? property.serializedObject.targetObject : null;
            long id = target != null ? target.GetUnityObjectId() : 0;
            string propertyPath = property.propertyPath;
            (long, string) cacheKey = (id, propertyPath);
            if (!ScrollKeyCache.TryGetValue(cacheKey, out string key))
            {
                key = InLineEditorShared.BuildScrollKey(id, propertyPath);
                ScrollKeyCache[cacheKey] = key;
            }
            return key;
        }

        internal static bool ShouldShowPingButton(Object value)
        {
            return InLineEditorShared.ShouldShowPingButton(value);
        }

        private static bool GetFoldoutState(
            SerializedProperty property,
            WInLineEditorAttribute inlineAttribute,
            WInLineEditorMode resolvedMode
        )
        {
            string key = BuildFoldoutKey(property);
            return InLineEditorShared.GetFoldoutState(key, resolvedMode);
        }

        private static void SetFoldoutState(string key, bool value)
        {
            InLineEditorShared.SetFoldoutState(key, value);
        }

        private static AnimBool GetOrCreateFoldoutAnim(string foldoutKey, bool expanded)
        {
            float speed = UnityHelpersSettings.GetInlineEditorFoldoutSpeed();

            if (!FoldoutAnimations.TryGetValue(foldoutKey, out AnimBool anim) || anim == null)
            {
                anim = new AnimBool(expanded) { speed = speed };
                anim.valueChanged.AddListener(RequestRepaint);
                FoldoutAnimations[foldoutKey] = anim;
            }

            anim.speed = speed;
            anim.target = expanded;
            return anim;
        }

        private static float GetFadeProgress(string foldoutKey, bool expanded)
        {
            if (!UnityHelpersSettings.ShouldTweenInlineEditorFoldouts())
            {
                return expanded ? 1f : 0f;
            }

            AnimBool anim = GetOrCreateFoldoutAnim(foldoutKey, expanded);
            return anim.faded;
        }

        private static void RequestRepaint()
        {
            InternalEditorUtility.RepaintAllViews();
        }

        internal static void ClearAnimationCacheForTesting()
        {
            foreach (KeyValuePair<string, AnimBool> kvp in FoldoutAnimations)
            {
                AnimBool anim = kvp.Value;
                if (anim != null)
                {
                    anim.valueChanged.RemoveListener(RequestRepaint);
                }
            }
            FoldoutAnimations.Clear();
        }

        /// <summary>
        /// Test hook to get the number of cached animation entries.
        /// </summary>
        internal static int GetAnimationCacheCountForTesting()
        {
            return FoldoutAnimations.Count;
        }

        /// <summary>
        /// Test hook to check if an animation entry exists for a specific key.
        /// </summary>
        internal static bool HasAnimationCacheEntryForTesting(string foldoutKey)
        {
            return FoldoutAnimations.ContainsKey(foldoutKey);
        }

        /// <summary>
        /// Test hook to get or create a foldout animation for testing purposes.
        /// </summary>
        internal static AnimBool GetOrCreateFoldoutAnimForTesting(string foldoutKey, bool expanded)
        {
            return GetOrCreateFoldoutAnim(foldoutKey, expanded);
        }

        /// <summary>
        /// Test hook to get the fade progress for a foldout.
        /// </summary>
        internal static float GetFadeProgressForTesting(string foldoutKey, bool expanded)
        {
            return GetFadeProgress(foldoutKey, expanded);
        }

        /// <summary>
        /// Test hook to build a foldout key from a serialized property.
        /// </summary>
        internal static string BuildFoldoutKeyForTesting(SerializedProperty property)
        {
            return BuildFoldoutKey(property);
        }

        internal static void ClearCachedStateForTesting()
        {
            InLineEditorShared.ClearCachedStateForTesting();
            PropertyWidths.Clear();
            InspectorHeightCache.Clear();
            _lastInspectorHeightCacheFrame = -1;
            ClearAnimationCacheForTesting();
        }

        internal static void SetInlineFoldoutStateForTesting(
            SerializedProperty property,
            bool expanded
        )
        {
            if (property == null)
            {
                return;
            }

            string key = BuildFoldoutKey(property);
            InLineEditorShared.SetFoldoutState(key, expanded);
        }

        internal static bool UsesHorizontalScrollbarForTesting(
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
        /// Test hook to directly check if a SerializedObject has only simple properties.
        /// This allows unit testing the simple layout detection without full editor integration.
        /// </summary>
        internal static bool HasOnlySimplePropertiesForTesting(SerializedObject serializedObject)
        {
            return SerializedObjectHasOnlySimpleProperties(serializedObject);
        }

        /// <summary>
        /// Test hook to directly check horizontal scrollbar requirement with explicit parameters.
        /// This bypasses editor creation and allows testing the decision logic directly.
        /// </summary>
        internal static bool RequiresHorizontalScrollbarForTesting(
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
            // Match the production logic: also trigger scroll when width is very narrow
            const float MinimumUsableWidth = 200f;
            bool widthIsTooNarrow = effectiveWidth < MinimumUsableWidth;
            bool shouldRespectMinWidth =
                hasExplicitMinInspectorWidth || !hasSimpleLayout || widthIsTooNarrow;
            return enableScrolling
                && minInspectorWidth > 0f
                && shouldRespectMinWidth
                && minInspectorWidth - effectiveWidth > 0.5f;
        }

        /// <summary>
        /// Test hook to check if the force serialized inspector flag is enabled.
        /// </summary>
        internal static bool ForceSerializedInspectorForTesting => _forceSerializedInspector;

        /// <summary>
        /// Test hook to calculate label width for a given available width.
        /// </summary>
        internal static float CalculateLabelWidthForTesting(float availableWidth)
        {
            return availableWidth * InLineEditorShared.DefaultLabelWidthRatio;
        }
    }
}
