// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR

    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Provides shared constants, state management, and helper methods for inline editor drawers.
    /// </summary>
    /// <remarks>
    /// This utility class consolidates common code used by both the standard PropertyDrawer
    /// (<see cref="WInLineEditorDrawer"/>) and the Odin Inspector drawer implementations
    /// of WInLineEditor. By centralizing these elements, we ensure consistent behavior
    /// and eliminate code duplication.
    /// </remarks>
    public static class InLineEditorShared
    {
        /// <summary>
        /// Height of the foldout header in pixels.
        /// </summary>
        public const float HeaderHeight = 20f;

        /// <summary>
        /// Standard vertical spacing between elements.
        /// </summary>
        public const float Spacing = 2f;

        /// <summary>
        /// Padding around content areas.
        /// </summary>
        public const float ContentPadding = 2f;

        /// <summary>
        /// Minimum width required for the foldout label before ping button is hidden.
        /// </summary>
        public const float MinimumFoldoutLabelWidth = 40f;

        /// <summary>
        /// Internal padding added to the ping button width calculation.
        /// </summary>
        public const float PingButtonPadding = 6f;

        /// <summary>
        /// Right margin for the ping button.
        /// </summary>
        public const float PingButtonRightMargin = 2f;

        /// <summary>
        /// Property path of the Unity script field to exclude from drawing.
        /// </summary>
        public const string ScriptPropertyPath = "m_Script";

        /// <summary>
        /// Separator used when building foldout keys.
        /// </summary>
        public const string FoldoutKeySeparator = "::";

        /// <summary>
        /// Prefix used for scroll position keys.
        /// </summary>
        public const string ScrollKeyPrefix = "scroll";

        /// <summary>
        /// Spacing between the header label and ping button.
        /// </summary>
        public const float HeaderPingSpacing = 4f;

        /// <summary>
        /// Ratio of the total content width allocated to label widths in inline editor UI.
        /// Labels display property names on the left side of the inspector.
        /// </summary>
        public const float DefaultLabelWidthRatio = 0.4f;

        /// <summary>
        /// Maximum number of foldout states to cache.
        /// Prevents unbounded memory growth in projects with many inline editors.
        /// </summary>
        private const int MaxFoldoutStatesCacheSize = 5000;

        /// <summary>
        /// Maximum number of scroll positions to cache.
        /// Prevents unbounded memory growth in projects with many scrollable inline editors.
        /// </summary>
        private const int MaxScrollPositionsCacheSize = 5000;

        /// <summary>
        /// Maximum number of editor instances to cache.
        /// Editor instances consume significant memory, so this limit is more conservative.
        /// </summary>
        private const int MaxEditorCacheSize = 500;

        /// <summary>
        /// Cache for foldout expansion states, keyed by a unique foldout identifier.
        /// Limited to <see cref="MaxFoldoutStatesCacheSize"/> entries to prevent unbounded memory growth.
        /// </summary>
        private static readonly Dictionary<string, bool> FoldoutStates = new Dictionary<
            string,
            bool
        >(StringComparer.Ordinal);

        /// <summary>
        /// Cache for scroll positions, keyed by a unique scroll identifier.
        /// Limited to <see cref="MaxScrollPositionsCacheSize"/> entries to prevent unbounded memory growth.
        /// </summary>
        private static readonly Dictionary<string, Vector2> ScrollPositions = new Dictionary<
            string,
            Vector2
        >(StringComparer.Ordinal);

        /// <summary>
        /// Backing field for lazy-initialized EditorCache.
        /// Lazy initialization is CRITICAL to prevent Unity Editor hangs during static initialization.
        /// Eager initialization would trigger Cache construction during domain reload,
        /// which can cause deadlocks during Unity's "Open Project: Open Scene" phase.
        /// </summary>
        private static Cache<long, Editor> _editorCache;

        /// <summary>
        /// Cache for Unity Editor instances, keyed by object instance ID.
        /// Uses <see cref="Cache{TKey,TValue}"/> with LRU eviction and eviction callback
        /// to properly destroy Editor instances when they are evicted from the cache.
        /// Lazy-initialized to prevent Unity Editor hangs during static initialization.
        /// </summary>
        private static Cache<long, Editor> EditorCache =>
            _editorCache ??= CacheBuilder<long, Editor>
                .NewBuilder()
                .MaximumSize(MaxEditorCacheSize)
                .OnEviction(OnEditorEvicted)
                .Build();

        /// <summary>
        /// Callback invoked when an Editor is evicted from the cache.
        /// Properly destroys the Editor instance to prevent memory leaks.
        /// </summary>
        /// <param name="key">The instance ID of the evicted editor.</param>
        /// <param name="editor">The Editor instance being evicted.</param>
        /// <param name="reason">The reason for eviction.</param>
        private static void OnEditorEvicted(long key, Editor editor, EvictionReason reason)
        {
            if (editor != null)
            {
                Object.DestroyImmediate(editor);
            }
        }

        /// <summary>
        /// Reusable GUIContent for ping button to avoid allocations.
        /// </summary>
        public static readonly GUIContent PingButtonContent = new GUIContent(
            "Ping",
            "Ping object in the Project window"
        );

        /// <summary>
        /// Reusable GUIContent for header labels to avoid allocations.
        /// </summary>
        public static readonly GUIContent ReusableHeaderContent = new GUIContent();

        /// <summary>
        /// Resolves the effective mode for an inline editor attribute.
        /// </summary>
        /// <param name="inlineAttribute">The attribute to resolve the mode for.</param>
        /// <returns>The resolved <see cref="WInLineEditorMode"/>.</returns>
        /// <remarks>
        /// If the attribute mode is <see cref="WInLineEditorMode.UseSettings"/>,
        /// the setting from <see cref="UnityHelpersSettings"/> is used.
        /// </remarks>
        public static WInLineEditorMode ResolveMode(WInLineEditorAttribute inlineAttribute)
        {
            if (inlineAttribute == null)
            {
                return WInLineEditorMode.FoldoutExpanded;
            }

            if (inlineAttribute.Mode != WInLineEditorMode.UseSettings)
            {
                return inlineAttribute.Mode;
            }

            UnityHelpersSettings.InlineEditorFoldoutBehavior behavior =
                UnityHelpersSettings.GetInlineEditorFoldoutBehavior();

            return behavior switch
            {
                UnityHelpersSettings.InlineEditorFoldoutBehavior.AlwaysOpen =>
                    WInLineEditorMode.AlwaysExpanded,

                UnityHelpersSettings.InlineEditorFoldoutBehavior.StartCollapsed =>
                    WInLineEditorMode.FoldoutCollapsed,

                _ => WInLineEditorMode.FoldoutExpanded,
            };
        }

        /// <summary>
        /// Gets the foldout state for a given key and mode.
        /// </summary>
        /// <param name="foldoutKey">The unique key for the foldout.</param>
        /// <param name="resolvedMode">The resolved mode to determine initial state.</param>
        /// <returns>True if the foldout is expanded; false otherwise.</returns>
        public static bool GetFoldoutState(string foldoutKey, WInLineEditorMode resolvedMode)
        {
            if (string.IsNullOrEmpty(foldoutKey))
            {
                return true;
            }

            if (
                EditorCacheHelper.TryGetFromBoundedLRUCache(
                    FoldoutStates,
                    foldoutKey,
                    out bool value
                )
            )
            {
                return value;
            }

            bool initialState = resolvedMode switch
            {
                WInLineEditorMode.AlwaysExpanded => true,

                WInLineEditorMode.FoldoutExpanded => true,

                WInLineEditorMode.FoldoutCollapsed => false,

                _ => true,
            };

            EditorCacheHelper.AddToBoundedCache(
                FoldoutStates,
                foldoutKey,
                initialState,
                MaxFoldoutStatesCacheSize
            );

            return initialState;
        }

        /// <summary>
        /// Sets the foldout state for a given key.
        /// </summary>
        /// <param name="foldoutKey">The unique key for the foldout.</param>
        /// <param name="expanded">Whether the foldout should be expanded.</param>
        public static void SetFoldoutState(string foldoutKey, bool expanded)
        {
            if (string.IsNullOrEmpty(foldoutKey))
            {
                return;
            }

            EditorCacheHelper.AddToBoundedCache(
                FoldoutStates,
                foldoutKey,
                expanded,
                MaxFoldoutStatesCacheSize
            );
        }

        /// <summary>
        /// Gets the scroll position for a given key.
        /// </summary>
        /// <param name="scrollKey">The unique key for the scroll position.</param>
        /// <returns>The stored scroll position, or <see cref="Vector2.zero"/> if not found.</returns>
        public static Vector2 GetScrollPosition(string scrollKey)
        {
            if (string.IsNullOrEmpty(scrollKey))
            {
                return Vector2.zero;
            }

            if (
                EditorCacheHelper.TryGetFromBoundedLRUCache(
                    ScrollPositions,
                    scrollKey,
                    out Vector2 position
                )
            )
            {
                return position;
            }

            return Vector2.zero;
        }

        /// <summary>
        /// Sets the scroll position for a given key.
        /// </summary>
        /// <param name="scrollKey">The unique key for the scroll position.</param>
        /// <param name="position">The scroll position to store.</param>
        public static void SetScrollPosition(string scrollKey, Vector2 position)
        {
            if (string.IsNullOrEmpty(scrollKey))
            {
                return;
            }

            EditorCacheHelper.AddToBoundedCache(
                ScrollPositions,
                scrollKey,
                position,
                MaxScrollPositionsCacheSize
            );
        }

        /// <summary>
        /// Gets or creates a cached editor for the given object.
        /// Uses <see cref="Cache{TKey,TValue}"/> with LRU eviction to manage editor instances.
        /// When editors are evicted from the cache, they are properly destroyed via <see cref="OnEditorEvicted"/>.
        /// </summary>
        /// <param name="value">The object to get or create an editor for.</param>
        /// <returns>The cached editor, or null if the value is null.</returns>
        public static Editor GetOrCreateEditor(Object value)
        {
            if (value == null)
            {
                return null;
            }

            long key = value.GetUnityObjectId();

            // Check if we have a valid cached editor (TryGet marks it as accessed for LRU)
            if (EditorCache.TryGet(key, out Editor cachedEditor) && cachedEditor != null)
            {
                return cachedEditor;
            }

            // Create a new editor
            Editor newEditor = null;
            Editor.CreateCachedEditor(value, null, ref newEditor);

            // Add to cache (will evict LRU entry if at capacity, triggering OnEditorEvicted)
            EditorCache.Set(key, newEditor);

            return newEditor;
        }

        /// <summary>
        /// Gets a cached string representation of an integer.
        /// Delegates to the centralized <see cref="EditorCacheHelper.GetCachedIntString"/> for shared LRU caching.
        /// </summary>
        /// <param name="value">The integer value to convert.</param>
        /// <returns>The cached string representation.</returns>
        public static string GetCachedIntString(int value)
        {
            return EditorCacheHelper.GetCachedIntString(value);
        }

        /// <summary>
        /// Calculates the width of the ping button.
        /// </summary>
        /// <returns>The calculated width including padding.</returns>
        public static float GetPingButtonWidth()
        {
            GUIStyle style = EditorStyles.miniButton;

            if (style == null)
            {
                return 0f;
            }

            Vector2 contentSize = style.CalcSize(PingButtonContent);

            return Mathf.Ceil(contentSize.x + PingButtonPadding);
        }

        /// <summary>
        /// Determines whether the ping button should be shown for the given object.
        /// </summary>
        /// <param name="value">The object to check.</param>
        /// <returns>True if the ping button should be shown; false otherwise.</returns>
        public static bool ShouldShowPingButton(Object value)
        {
            if (value == null)
            {
                return false;
            }

            return ProjectBrowserVisibilityUtility.IsProjectBrowserVisible();
        }

        /// <summary>
        /// Determines whether a standalone header should be drawn.
        /// </summary>
        /// <param name="inlineAttribute">The inline editor attribute.</param>
        /// <returns>True if a standalone header should be drawn; false otherwise.</returns>
        /// <remarks>
        /// A standalone header is drawn when the object field is not being drawn,
        /// meaning the header needs to provide the foldout functionality.
        /// </remarks>
        public static bool ShouldDrawStandaloneHeader(WInLineEditorAttribute inlineAttribute)
        {
            if (inlineAttribute == null)
            {
                return false;
            }

            return !inlineAttribute.DrawObjectField;
        }

        /// <summary>
        /// Builds a foldout key from instance ID and property path.
        /// </summary>
        /// <param name="parentInstanceId">The instance ID of the parent object.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <returns>A unique foldout key string.</returns>
        public static string BuildFoldoutKey(long parentInstanceId, string propertyPath)
        {
            return parentInstanceId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + FoldoutKeySeparator
                + propertyPath;
        }

        /// <summary>
        /// Builds a scroll key from a foldout key.
        /// </summary>
        /// <param name="foldoutKey">The foldout key to base the scroll key on.</param>
        /// <returns>A unique scroll key string.</returns>
        public static string BuildScrollKey(string foldoutKey)
        {
            return ScrollKeyPrefix + FoldoutKeySeparator + foldoutKey;
        }

        /// <summary>
        /// Builds a scroll key from instance ID and property path.
        /// </summary>
        /// <param name="parentInstanceId">The instance ID of the parent object.</param>
        /// <param name="propertyPath">The property path.</param>
        /// <returns>A unique scroll key string.</returns>
        public static string BuildScrollKey(long parentInstanceId, string propertyPath)
        {
            return ScrollKeyPrefix
                + FoldoutKeySeparator
                + parentInstanceId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + FoldoutKeySeparator
                + propertyPath;
        }

        /// <summary>
        /// Prepares header content for display, combining label and object name.
        /// </summary>
        /// <param name="value">The object being displayed.</param>
        /// <param name="label">Optional label to prepend.</param>
        /// <returns>The prepared header content.</returns>
        public static GUIContent PrepareHeaderContent(Object value, GUIContent label)
        {
            if (value == null)
            {
                return label ?? GUIContent.none;
            }

            GUIContent headerContent = EditorGUIUtility.ObjectContent(value, value.GetType());

            if (headerContent == null || string.IsNullOrEmpty(headerContent.text))
            {
                ReusableHeaderContent.text = value.name;

                ReusableHeaderContent.image = headerContent != null ? headerContent.image : null;

                ReusableHeaderContent.tooltip =
                    headerContent != null ? headerContent.tooltip ?? string.Empty : string.Empty;

                headerContent = ReusableHeaderContent;
            }

            if (label != null && !string.IsNullOrEmpty(label.text))
            {
                ReusableHeaderContent.text = label.text + " (" + headerContent.text + ")";

                ReusableHeaderContent.image = headerContent.image;

                ReusableHeaderContent.tooltip = headerContent.tooltip ?? string.Empty;

                headerContent = ReusableHeaderContent;
            }

            return headerContent;
        }

        /// <summary>
        /// Draws the serialized object's properties, skipping the script field.
        /// </summary>
        /// <param name="serializedObject">The serialized object to draw.</param>
        /// <remarks>
        /// This uses EditorGUILayout for automatic layout. For IMGUI-based drawing
        /// with explicit rects, use the overload that accepts a rect parameter.
        /// </remarks>
        public static void DrawSerializedObject(SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();

            SerializedProperty iterator = serializedObject.GetIterator();

            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                if (
                    string.Equals(
                        iterator.propertyPath,
                        ScriptPropertyPath,
                        StringComparison.Ordinal
                    )
                )
                {
                    enterChildren = false;

                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);

                enterChildren = false;
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws the serialized object's properties within a specific rect, skipping the script field.
        /// </summary>
        /// <param name="rect">The rect to draw within.</param>
        /// <param name="serializedObject">The serialized object to draw.</param>
        /// <remarks>
        /// This version uses EditorGUI with explicit rects for IMGUI-based drawing.
        /// </remarks>
        public static void DrawSerializedObjectInRect(Rect rect, SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUIUtility.labelWidth = rect.width * DefaultLabelWidthRatio;

            try
            {
                serializedObject.UpdateIfRequiredOrScript();

                SerializedProperty iterator = serializedObject.GetIterator();

                bool enterChildren = true;

                Rect currentRect = new Rect(rect.x, rect.y, rect.width, 0f);

                bool firstPropertyDrawn = false;

                while (iterator.NextVisible(enterChildren))
                {
                    if (
                        string.Equals(
                            iterator.propertyPath,
                            ScriptPropertyPath,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        enterChildren = false;

                        continue;
                    }

                    if (firstPropertyDrawn)
                    {
                        currentRect.y += EditorGUIUtility.standardVerticalSpacing;
                    }

                    float propertyHeight = EditorGUI.GetPropertyHeight(iterator, true);

                    currentRect.height = propertyHeight;

                    EditorGUI.PropertyField(currentRect, iterator, true);

                    currentRect.y += propertyHeight;

                    enterChildren = false;

                    firstPropertyDrawn = true;
                }

                serializedObject.ApplyModifiedProperties();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        /// <summary>
        /// Clears all cached state. Called during domain reload to prevent stale references.
        /// Note: IntToString cache is managed centrally by EditorCacheHelper.
        /// The EditorCache.Clear() method will trigger <see cref="OnEditorEvicted"/> for each cached editor,
        /// which properly destroys the Editor instances.
        /// </summary>
        internal static void ClearCache()
        {
            FoldoutStates.Clear();

            ScrollPositions.Clear();

            // Only clear if the cache has been initialized (avoid triggering lazy initialization during cache clear)
            // Clear() triggers OnEviction callback for each entry, which calls DestroyImmediate
            _editorCache?.Clear();
        }

        /// <summary>
        /// Clears all cached state. Primarily for testing purposes.
        /// </summary>
        internal static void ClearCachedStateForTesting()
        {
            ClearCache();
        }

        /// <summary>
        /// Test hook to set the foldout state for a given key.
        /// </summary>
        /// <param name="key">The foldout key.</param>
        /// <param name="expanded">Whether the foldout should be expanded.</param>
        internal static void SetFoldoutStateForTesting(string key, bool expanded)
        {
            if (!string.IsNullOrEmpty(key))
            {
                FoldoutStates[key] = expanded;
            }
        }

        /// <summary>
        /// Test hook to get the foldout state for a given key.
        /// </summary>
        /// <param name="key">The foldout key.</param>
        /// <returns>True if expanded; false otherwise.</returns>
        internal static bool GetFoldoutStateForTesting(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return FoldoutStates.TryGetValue(key, out bool value) && value;
        }

        /// <summary>
        /// Test hook to get the number of cached editors.
        /// </summary>
        /// <returns>The number of cached editors.</returns>
        internal static int GetEditorCacheCountForTesting()
        {
            return EditorCache.Count;
        }

        /// <summary>
        /// Test hook to get the number of cached foldout states.
        /// </summary>
        /// <returns>The number of cached foldout states.</returns>
        internal static int GetFoldoutStateCacheCountForTesting()
        {
            return FoldoutStates.Count;
        }

        /// <summary>
        /// Test hook to get the number of cached scroll positions.
        /// </summary>
        /// <returns>The number of cached scroll positions.</returns>
        internal static int GetScrollPositionCacheCountForTesting()
        {
            return ScrollPositions.Count;
        }
    }

#endif
}
