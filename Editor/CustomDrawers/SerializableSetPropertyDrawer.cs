// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    [CustomPropertyDrawer(typeof(SerializableHashSet<>), true)]
    [CustomPropertyDrawer(typeof(SerializableSortedSet<>), true)]
    public sealed class SerializableSetPropertyDrawer : PropertyDrawer
    {
        private const float SectionSpacing = 4f;
        private const float ButtonSpacing = 4f;
        private const float PaginationButtonWidth = 28f;
        private const int DefaultPageSize = 15;
        private const int MaxAutoAddAttempts = 256;
        private const int MaxPageSize = 250;
        private const float PaginationLabelWidth = 80f;
        private const float PaginationHeaderHeightPadding = 7f;
        private const float InspectorHeightPadding = 2.5f;
        private const float PaginationLabelVerticalOffset = -2f;
        private const float PaginationButtonsVerticalOffset = 0f;

        private static readonly GUIContent AddEntryContent = new("Add");
        private static readonly GUIContent ClearAllContent = new("Clear All");
        private static readonly GUIContent SortContent = new("Sort");
        private static readonly GUIContent MoveUpContent = new("\u2191", "Move selected entry up");
        private static readonly GUIContent MoveDownContent = new(
            "\u2193",
            "Move selected entry down"
        );
        private static readonly GUIContent FirstPageContent = new("<<", "First Page");
        private static readonly GUIContent PreviousPageContent = new("<", "Previous Page");
        private static readonly GUIContent NextPageContent = new(">", "Next Page");
        private static readonly GUIContent LastPageContent = new(">>", "Last Page");
        private static readonly GUIContent PaginationPageLabelContent = new();
        private static readonly GUIContent RangeLabelGUIContent = new();
        private static readonly object NullComparable = new();
        private static readonly Dictionary<(int, int, int), string> RangeLabelCache = new();

        // GUIStyles are built lazily on first GUI access (see the *ButtonStyle properties below).
        // Building them in static field initializers / a static constructor would touch
        // EditorStyles at type-load time, which throws a NullReferenceException (cascading as a
        // TypeInitializationException) whenever the type is first loaded outside an active IMGUI
        // context -- e.g. in batch-mode test runs. Lazy initialization defers all EditorStyles
        // access to actual rendering, where the editor skin is guaranteed to be ready.
        private static GUIStyle _addButtonStyle;
        private static GUIStyle _clearAllActiveButtonStyle;
        private static GUIStyle _clearAllInactiveButtonStyle;
        private static GUIStyle _removeButtonStyle;
        private static GUIStyle _moveButtonStyle;

        private static GUIStyle AddButtonStyle =>
            _addButtonStyle ??= BuildButtonStyle(new Color(0.22f, 0.62f, 0.29f));
        private static GUIStyle ClearAllActiveButtonStyle =>
            _clearAllActiveButtonStyle ??= BuildButtonStyle(new Color(0.82f, 0.27f, 0.27f));
        private static GUIStyle ClearAllInactiveButtonStyle =>
            _clearAllInactiveButtonStyle ??= BuildButtonStyle(new Color(0.55f, 0.55f, 0.55f));
        private static GUIStyle RemoveButtonStyle =>
            _removeButtonStyle ??= BuildRemoveButtonStyle(new Color(0.86f, 0.23f, 0.23f));
        private static GUIStyle MoveButtonStyle =>
            _moveButtonStyle ??= BuildMoveButtonStyle(new Color(0.98f, 0.95f, 0.65f));
        private static readonly Color DuplicatePrimaryColor = new(0.99f, 0.82f, 0.35f, 0.55f);
        private static readonly Color DuplicateSecondaryColor = new(0.96f, 0.45f, 0.45f, 0.65f);
        private static readonly Color DuplicateOutlineColor = new(0.65f, 0.18f, 0.18f, 0.9f);
        private static readonly Color LightRowColor = new(0.97f, 0.97f, 0.97f, 1f);
        private static readonly Color DarkRowColor = new(0.16f, 0.16f, 0.16f, 0.45f);
        private static readonly Color NullEntryHighlightColor = new(0.84f, 0.2f, 0.2f, 0.6f);
        internal const float WGroupFoldoutAlignmentOffset = 2.5f;
        private const float ManualEntrySectionPadding = 6f;
        private const float ManualEntrySectionPaddingSettings = 2f;
        private const float ManualEntryFoldoutToggleOffsetInspector = 16f;
        private const float ManualEntryFoldoutToggleOffsetSettings = 6f;
        private const float ManualEntryFoldoutLabelPadding = 0f;
        private const float ManualEntryFoldoutLabelContentOffset = 0f;
        private const float ManualEntryButtonWidth = 110f;
        private const float ManualEntryResetWidth = 70f;
        private const float ManualEntryValueContentLeftShift = 8.5f;
        private const float ManualEntryFoldoutValueLeftShiftReduction = 6f;
        private const float ManualEntryFoldoutValueRightShift = 3f;
        private const float ManualEntryExpandableValueFoldoutGutter = 7f;

        // Lazy initialization to avoid calling EditorGUIUtility during static class loading,
        // which can hang Unity during "Open Project: Open Scene" if the class is accessed
        // before EditorGUIUtility is fully initialized.
        private static GUIContent _manualEntryFoldoutContent;
        private static GUIContent _manualEntryValueContent;
        private static GUIContent _manualEntryAddContent;

        private static GUIContent ManualEntryFoldoutContent =>
            _manualEntryFoldoutContent ??= EditorGUIUtility.TrTextContent("New Entry");

        private static GUIContent ManualEntryValueContent =>
            _manualEntryValueContent ??= EditorGUIUtility.TrTextContent("Value");

        private static GUIContent ManualEntryAddContent =>
            _manualEntryAddContent ??= EditorGUIUtility.TrTextContent("Add");
        private static readonly GUIContent ManualEntryResetContent = new("Reset");
        private static GUIStyle _manualEntryFoldoutLabelStyle;
        private const float DuplicateShakeAmplitude = 2f;
        private const float DuplicateShakeFrequency = 7f;
        private const float DuplicateOutlineThickness = 1f;
        private static readonly GUIContent NullEntryTooltipContent = new();
        private static readonly GUIContent FoldoutLabelContent = new();
        private static readonly GUIContent UnsupportedTypeContent = new();
        private static readonly Dictionary<Type, string> UnsupportedTypeMessageCache = new();
        private static readonly Dictionary<string, Type> PropertyTypeResolutionCache = new(
            StringComparer.Ordinal
        );
        private static readonly ConcurrentDictionary<Type, Func<object>> ParameterlessFactoryCache =
            new();
        private static readonly ConcurrentDictionary<Type, byte> UnsupportedParameterlessTypes =
            new();

        /// <summary>
        /// Struct key for MainFoldoutAnimations cache to avoid string allocations.
        /// Uses (instanceId, propertyPath) pair for cache identity.
        /// </summary>
        private readonly struct MainFoldoutCacheKey : IEquatable<MainFoldoutCacheKey>
        {
            public readonly int InstanceId;
            public readonly string PropertyPath;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MainFoldoutCacheKey(int instanceId, string propertyPath)
            {
                InstanceId = instanceId;
                PropertyPath = propertyPath;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(MainFoldoutCacheKey other)
            {
                return InstanceId == other.InstanceId
                    && string.Equals(PropertyPath, other.PropertyPath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MainFoldoutCacheKey other && Equals(other);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode()
            {
                return Objects.HashCode(InstanceId, PropertyPath);
            }

            /// <summary>
            /// Converts to string representation for test verification.
            /// </summary>
            public override string ToString()
            {
                return $"{InstanceId}:{PropertyPath}";
            }
        }

        // Main foldout animation cache - static because property drawers can be recreated
        // Keys include the target object's instance ID to prevent cache collisions between different objects
        private static readonly Dictionary<MainFoldoutCacheKey, AnimBool> MainFoldoutAnimations =
            new();

        /// <summary>
        /// Computes the cache key for main foldout animations.
        /// Includes the target object's instance ID to prevent collisions between different objects.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MainFoldoutCacheKey GetMainFoldoutCacheKey(
            SerializedObject serializedObject,
            string propertyPath
        )
        {
            int instanceId =
                serializedObject?.targetObject != null
                    ? serializedObject.targetObject.GetInstanceID()
                    : 0;
            return new MainFoldoutCacheKey(instanceId, propertyPath);
        }

        private readonly Dictionary<string, PaginationState> _paginationStates = new();
        private readonly Dictionary<string, DuplicateState> _duplicateStates = new();
        private readonly Dictionary<string, NullEntryState> _nullEntryStates = new();
        private readonly Dictionary<string, PendingEntry> _pendingEntries = new();
        private readonly Dictionary<string, ReorderableList> _lists = new();
        private readonly Dictionary<string, ListPageCache> _pageCaches = new();
        private readonly Dictionary<string, SetListRenderContext> _listContexts = new();
        private readonly Dictionary<RowFoldoutKey, bool> _rowFoldoutStates = new();
        private readonly Dictionary<string, HeightCacheEntry> _heightCache = new();
        private readonly Dictionary<RowRenderKey, RowRenderData> _rowRenderCache = new();
        private readonly Dictionary<string, SortedSetCacheEntry> _sortedSetCache = new();
        private readonly Dictionary<string, InspectorCacheEntry> _inspectorCache = new();
        private readonly Dictionary<string, CachedItemsProperty> _cachedItemsProperties = new();

        private string _cachedPropertyPath;
        private string _cachedListKey;
        private int _lastDuplicateRefreshFrame = -1;
        private int _lastNullEntryRefreshFrame = -1;
        private int _lastRowRenderCacheFrame = -1;
        private int _lastItemsPropertyCacheFrame = -1;

        private sealed class CachedItemsProperty
        {
            public SerializedProperty itemsProperty;
        }

        private sealed class SortedSetCacheEntry
        {
            public bool isSorted;
            public int frameNumber;
        }

        private sealed class InspectorCacheEntry
        {
            public ISerializableSetInspector inspector;
            public int frameNumber;
        }

        private sealed class HeightCacheEntry
        {
            public float height;
            public int arraySize;
            public int pageIndex;
            public bool isExpanded;
            public bool hasNullEntries;
            public bool hasDuplicates;
            public bool pendingIsExpanded;
            public float pendingFoldoutProgress;
            public float mainFoldoutProgress;
            public int frameNumber;
        }

        private readonly struct RowRenderKey : IEquatable<RowRenderKey>
        {
            public readonly string listKey;
            public readonly int globalIndex;

            public RowRenderKey(string listKey, int globalIndex)
            {
                this.listKey = listKey;
                this.globalIndex = globalIndex;
            }

            public bool Equals(RowRenderKey other)
            {
                return globalIndex == other.globalIndex && listKey == other.listKey;
            }

            public override bool Equals(object obj)
            {
                return obj is RowRenderKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Objects.HashCode(listKey, globalIndex);
            }
        }

        private sealed class RowRenderData
        {
            public SerializedProperty itemProperty;
            public float rowHeight;
            public float itemHeight;
            public bool isValid;
        }

        internal Rect LastResolvedPosition { get; private set; }
        internal Rect LastItemsContainerRect { get; private set; }
        internal bool HasItemsContainerRect { get; private set; }
        internal static bool HasLastManualEntryHeaderRect { get; private set; }
        internal static Rect LastManualEntryHeaderRect { get; private set; }
        internal static Rect LastManualEntryToggleRect { get; private set; }
        internal static bool HasLastMainFoldoutRect { get; private set; }
        internal static Rect LastMainFoldoutRect { get; private set; }
        internal static bool HasLastManualEntryValueRect { get; private set; }
        internal static Rect LastManualEntryValueRect { get; private set; }
        internal static bool LastManualEntryValueUsedFoldoutLabel { get; private set; }
        internal static float LastManualEntryValueFoldoutOffset { get; private set; }
        internal static bool HasLastRowContentRect { get; private set; }
        internal static Rect LastRowContentRect { get; private set; }

        // Footer tracking for WGroup integration tests
        internal static float LastFooterWGroupLeftPadding { get; private set; }
        internal static float LastFooterWGroupRightPadding { get; private set; }
        internal static float LastFooterAvailableWidth { get; private set; }
        internal static float LastFooterRangeWidth { get; private set; }
        internal static bool LastFooterRangeLabelWasDrawn { get; private set; }
        internal static Rect LastFooterRangeLabelRect { get; private set; }

        /// <summary>
        /// Frame number when a child property drawer signaled that its height changed.
        /// When this matches the current frame, the row render cache should be invalidated.
        /// </summary>
        private static int _childHeightChangedFrame = -1;

        internal sealed class PaginationState
        {
            public int page;
            public int pageSize = DefaultPageSize;
            public int selectedIndex = -1;
        }

        internal sealed class DuplicateState
        {
            public bool hasDuplicates;
            public readonly HashSet<int> duplicateIndices = new();
            public string summary = string.Empty;
            public readonly Dictionary<int, double> animationStartTimes = new();
            public readonly Dictionary<int, bool> primaryFlags = new();
            public readonly Dictionary<object, List<int>> grouping = new();
            public readonly Dictionary<List<int>, PooledResource<List<int>>> groupingLeases = new();
            public readonly List<int> animationKeysScratch = new();
            public readonly List<object> groupingKeysScratch = new();
            private bool _lastHadDuplicates;
            private int _lastArraySize = -1;
            private bool _animationsCompleted;

            public bool IsDirty => _lastArraySize < 0;

            public bool IsAnimating => hasDuplicates && !_animationsCompleted;

            public void MarkDirty()
            {
                _lastArraySize = -1;
            }

            public void UpdateArraySize(int newSize)
            {
                _lastArraySize = newSize;
            }

            public void UpdateLastHadDuplicates(bool hadDuplicates, bool forceReset = false)
            {
                bool changed = hasDuplicates != _lastHadDuplicates;
                _lastHadDuplicates = hadDuplicates;
                if ((changed || forceReset) && hasDuplicates)
                {
                    _animationsCompleted = false;
                }
            }

            public bool ShouldSkipRefresh(int currentArraySize)
            {
                return currentArraySize == _lastArraySize && !_lastHadDuplicates && !hasDuplicates;
            }

            public void CheckAnimationCompletion(double currentTime, int cycleLimit)
            {
                if (_animationsCompleted || !hasDuplicates || cycleLimit <= 0)
                {
                    return;
                }

                double cycleDuration = (2d * Math.PI) / DuplicateShakeFrequency;
                double maxDuration = cycleDuration * cycleLimit;

                bool allComplete = true;
                foreach (KeyValuePair<int, double> entry in animationStartTimes)
                {
                    double elapsed = currentTime - entry.Value;
                    if (elapsed < maxDuration)
                    {
                        allComplete = false;
                        break;
                    }
                }

                if (allComplete)
                {
                    _animationsCompleted = true;
                }
            }

            public void ClearAnimationTracking()
            {
                _lastHadDuplicates = false;
                _lastArraySize = -1;
                animationStartTimes.Clear();
            }

            public float GetAnimationOffset(int arrayIndex, double currentTime, int cycleLimit)
            {
                if (!animationStartTimes.TryGetValue(arrayIndex, out double startTime))
                {
                    startTime = currentTime;
                    animationStartTimes[arrayIndex] = startTime;
                    _animationsCompleted = false;
                }

                return EvaluateDuplicateShakeOffset(arrayIndex, startTime, currentTime, cycleLimit);
            }
        }

        internal sealed class NullEntryState
        {
            public bool hasNullEntries;
            public readonly HashSet<int> nullIndices = new();
            public readonly Dictionary<int, string> tooltips = new();
            public string summary = string.Empty;
            public readonly List<int> scratch = new();
        }

        internal sealed class PendingEntry
        {
            public object value;
            public bool isExpanded;
            public bool isSorted;
            public Type elementType;
            public string errorMessage;
            public AnimBool foldoutAnim;
            public PendingValueWrapper valueWrapper;
            public SerializedObject valueWrapperSerialized;
            public SerializedProperty valueWrapperProperty;
            public bool valueWrapperDirty = true;
        }

        private struct SetElementData
        {
            public SerializedPropertyType propertyType;
            public object comparable;
            public object value;
        }

        internal sealed class SetListRenderContext
        {
            public SerializedProperty setProperty;
            public SerializedProperty itemsProperty;
            public DuplicateState duplicateState;
            public NullEntryState nullState;
            public Type elementType;
            public bool needsDuplicateRefresh;
        }

        private readonly struct RowFoldoutKey : IEquatable<RowFoldoutKey>
        {
            public RowFoldoutKey(string cacheKey, int index)
            {
                CacheKey = cacheKey;
                Index = index;
            }

            public string CacheKey { get; }

            private int Index { get; }

            public bool IsValid => !string.IsNullOrEmpty(CacheKey) && Index >= 0;

            public bool Equals(RowFoldoutKey other)
            {
                return Index == other.Index && string.Equals(CacheKey, other.CacheKey);
            }

            public override bool Equals(object obj)
            {
                return obj is RowFoldoutKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Objects.HashCode(CacheKey, Index);
            }
        }

        internal sealed class ListPageCache
        {
            public readonly List<PageEntry> entries = new();
            public int pageIndex = -1;
            public int pageSize = -1;
            public int itemCount = -1;
            public bool dirty = true;
        }

        internal sealed class PageEntry
        {
            public int arrayIndex;
        }

        private static float GetFooterHeight()
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            return lineHeight + EditorGUIUtility.standardVerticalSpacing * 2f;
        }

        private static float GetPaginationHeaderHeight()
        {
            return EditorGUIUtility.singleLineHeight + PaginationHeaderHeightPadding;
        }

        private void DrawEmptySetDrawer(
            Rect rect,
            SerializedProperty property,
            string propertyPath,
            SerializedProperty itemsProperty,
            PaginationState pagination
        )
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            float padding = 6f;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float verticalSpacing = EditorGUIUtility.standardVerticalSpacing;

            Rect messageRect = new(
                rect.x + padding,
                rect.y + padding,
                rect.width - padding * 2f,
                lineHeight
            );
            EditorGUI.LabelField(messageRect, "List is empty.", EditorStyles.miniLabel);

            Rect buttonsRect = new(
                messageRect.x,
                messageRect.yMax + verticalSpacing,
                messageRect.width,
                lineHeight
            );

            SerializedProperty propertyRef = property;
            SerializedProperty itemsPropertyRef = itemsProperty;
            if (!TryGetSetInspector(propertyRef, propertyPath, out _))
            {
                return;
            }

            if (itemsPropertyRef == null)
            {
                return;
            }

            int totalCount = itemsPropertyRef is { isArray: true } ? itemsPropertyRef.arraySize : 0;
            float rightCursor = buttonsRect.xMax;

            Rect addRect = new(rightCursor - 60f, buttonsRect.y, 60f, lineHeight);
            if (GUI.Button(addRect, AddEntryContent, AddButtonStyle))
            {
                if (
                    TryAddNewElement(
                        ref propertyRef,
                        propertyPath,
                        ref itemsPropertyRef,
                        pagination
                    )
                )
                {
                    itemsPropertyRef = propertyRef.FindPropertyRelative(
                        SerializableHashSetSerializedPropertyNames.Items
                    );
                    totalCount = itemsPropertyRef is { isArray: true }
                        ? itemsPropertyRef.arraySize
                        : 0;
                    EnsurePaginationBounds(pagination, totalCount);
                }
            }
            rightCursor = addRect.x - ButtonSpacing;

            Rect clearRect = new(rightCursor - 80f, buttonsRect.y, 80f, lineHeight);
            bool canClear = totalCount > 0;
            GUIStyle clearStyle = canClear
                ? ClearAllActiveButtonStyle
                : ClearAllInactiveButtonStyle;
            using (new EditorGUI.DisabledScope(!canClear))
            {
                if (GUI.Button(clearRect, ClearAllContent, clearStyle) && canClear)
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "Clear Set",
                        "Remove all entries from this set?",
                        "Clear",
                        "Cancel"
                    );
                    if (
                        confirmed
                        && TryClearSet(ref propertyRef, propertyPath, ref itemsPropertyRef)
                    )
                    {
                        pagination.page = 0;
                        pagination.selectedIndex = -1;
                        itemsPropertyRef = propertyRef.FindPropertyRelative(
                            SerializableHashSetSerializedPropertyNames.Items
                        );
                        totalCount = itemsPropertyRef is { isArray: true }
                            ? itemsPropertyRef.arraySize
                            : 0;
                        EnsurePaginationBounds(pagination, totalCount);
                    }
                }
            }
        }

        private static float GetEmptySetDrawerHeight()
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float padding = 6f;
            float verticalSpacing = EditorGUIUtility.standardVerticalSpacing;
            float contentHeight = lineHeight + verticalSpacing + lineHeight;
            return padding * 2f + contentHeight;
        }

        private static void DrawSetBodyTopBorder(Rect rect)
        {
            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.25f, 0.25f, 0.25f, 1f)
                : new Color(0.7f, 0.7f, 0.7f, 1f);
            EditorGUI.DrawRect(rect, borderColor);
        }

        private enum PaginationControlLayout
        {
            None,
            PrevNext,
            Full,
        }

        private static GUIStyle BuildButtonStyle(Color baseColor)
        {
            GUIStyle style = CreateSolidButtonStyle(baseColor);
            ConfigureButtonStyle(style, EditorGUIUtility.singleLineHeight);
            return style;
        }

        private static GUIStyle BuildMoveButtonStyle(Color baseColor)
        {
            GUIStyle style = BuildButtonStyle(baseColor);
            SetButtonTextColor(style, Color.black);
            return style;
        }

        private static GUIStyle BuildRemoveButtonStyle(Color baseColor)
        {
            GUIStyle style = BuildButtonStyle(baseColor);
            style.fixedWidth = 0f;
            style.padding = new RectOffset(3, 3, 1, 1);
            style.margin = new RectOffset(0, 0, 1, 1);
            return style;
        }

        private static GUIStyle CreateSolidButtonStyle(Color baseColor)
        {
            Color hoverColor = AdjustColorBrightness(baseColor, 0.12f);
            Color activeColor = AdjustColorBrightness(baseColor, -0.18f);

            GUIStyle style = new(EditorStyles.miniButton);
            Texture2D normalTexture = CreateSolidTexture(baseColor);
            Texture2D hoverTexture = CreateSolidTexture(hoverColor);
            Texture2D activeTexture = CreateSolidTexture(activeColor);

            style.normal.background = normalTexture;
            style.normal.textColor = Color.white;
            style.hover.background = hoverTexture;
            style.hover.textColor = Color.white;
            style.active.background = activeTexture;
            style.active.textColor = Color.white;

            style.onNormal.background = normalTexture;
            style.onNormal.textColor = Color.white;
            style.onHover.background = hoverTexture;
            style.onHover.textColor = Color.white;
            style.onActive.background = activeTexture;
            style.onActive.textColor = Color.white;

            style.focused.background = normalTexture;
            style.focused.textColor = Color.white;
            style.onFocused.background = normalTexture;
            style.onFocused.textColor = Color.white;

            return style;
        }

        private static void ConfigureButtonStyle(GUIStyle style, float lineHeight)
        {
            if (style == null)
            {
                return;
            }

            style.fixedHeight = lineHeight;
            style.margin = new RectOffset(1, 1, 1, 1);
            style.padding = new RectOffset(6, 6, 2, 2);
        }

        private static void SetButtonTextColor(GUIStyle style, Color color)
        {
            if (style == null)
            {
                return;
            }

            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            Color opaque = new(color.r, color.g, color.b, 1f);
            texture.SetPixel(0, 0, opaque);
            texture.Apply();
            return texture;
        }

        private static Color AdjustColorBrightness(Color color, float amount)
        {
            if (amount > 0f)
            {
                return Color.Lerp(color, Color.white, Mathf.Clamp01(amount));
            }

            if (amount < 0f)
            {
                return Color.Lerp(color, Color.black, Mathf.Clamp01(-amount));
            }

            return color;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect originalPosition = position;
            bool targetsSettings = TargetsUnityHelpersSettings(property?.serializedObject);
            Rect contentPosition = ResolveContentRect(originalPosition, targetsSettings);
            LastResolvedPosition = contentPosition;
            HasItemsContainerRect = false;

            // Log all tween settings on first draw or when inside WGroup for debugging
            if (GroupGUIWidthUtility.CurrentScopeDepth > 0)
            {
                SerializableCollectionTweenDiagnostics.LogAllTweenSettings(
                    $"OnGUI_InWGroup (path={property?.propertyPath ?? "(null)"})"
                );
            }

            EditorGUI.BeginProperty(originalPosition, label, property);
            int previousIndentScope = EditorGUI.indentLevel;

            try
            {
                // In SettingsProvider context, we handle our own indentation via WGroup padding
                // Reset indent level to avoid double-indentation from EditorGUI methods
                if (targetsSettings)
                {
                    EditorGUI.indentLevel = 0;
                }

                position = contentPosition;

                SerializedObject serializedObject = property.serializedObject;

                string propertyPath = property.propertyPath;
                bool hasInspector = TryGetSetInspectorCached(
                    property,
                    propertyPath,
                    out ISerializableSetInspector inspector
                );
                Type elementType = inspector?.ElementType;

                string listKey = GetListKey(property);
                CachedItemsProperty cachedItems = GetOrCreateCachedItemsProperty(listKey, property);
                SerializedProperty itemsProperty = cachedItems.itemsProperty;

                bool hasItemsArray = itemsProperty is { isArray: true };
                int totalCount = hasItemsArray ? itemsProperty.arraySize : 0;

                GUIContent foldoutLabel = GetFoldoutLabelContent(label);

                // Apply additional foldout alignment offset when inside a WGroup property context
                float foldoutAlignmentOffset =
                    GroupGUIWidthUtility.CurrentScopeDepth > 0 && !targetsSettings
                        ? WGroupFoldoutAlignmentOffset
                        : 0f;

                Rect foldoutRect = new(
                    position.x + foldoutAlignmentOffset,
                    position.y,
                    position.width - foldoutAlignmentOffset,
                    EditorGUIUtility.singleLineHeight
                );
                if (!targetsSettings)
                {
                    WSerializableCollectionFoldoutUtility.EnsureFoldoutInitialized(
                        property,
                        fieldInfo,
                        WSerializableCollectionFoldoutUtility.SerializableCollectionType.Set
                    );
                }

                property.isExpanded = EditorGUI.Foldout(
                    foldoutRect,
                    property.isExpanded,
                    foldoutLabel,
                    true
                );

                // Track the foldout rect for testing
                HasLastMainFoldoutRect = true;
                LastMainFoldoutRect = foldoutRect;

                // Get main foldout animation progress for smooth expand/collapse animation
                float mainFoldoutProgress = GetMainFoldoutProgress(
                    property.serializedObject,
                    property.propertyPath,
                    property.isExpanded,
                    IsSortedSetCached(property)
                );

                float y = foldoutRect.yMax + SectionSpacing;

                // During collapse, hide content early and fade faster to avoid visual confusion
                // where large sets' contents "stick around" longer than surrounding elements
                bool isCollapsing = !property.isExpanded && mainFoldoutProgress < 1f;
                bool shouldDrawContent;
                float contentAlpha;

                if (isCollapsing)
                {
                    // During collapse, skip drawing content below threshold for snappier feel
                    const float CollapseContentThreshold = 0.4f;
                    shouldDrawContent = mainFoldoutProgress >= CollapseContentThreshold;
                    // Use cubic curve so alpha drops much faster at start of collapse
                    // When progress is 0.5, alpha becomes ~0.125 (very faded)
                    contentAlpha = mainFoldoutProgress * mainFoldoutProgress * mainFoldoutProgress;
                }
                else
                {
                    shouldDrawContent = mainFoldoutProgress > 0f;
                    contentAlpha = mainFoldoutProgress;
                }

                // Draw expanded content with animation support
                if (!shouldDrawContent)
                {
                    return;
                }

                // Apply alpha fade during animation
                bool adjustAlpha = !Mathf.Approximately(contentAlpha, 1f);
                Color previousColor = GUI.color;
                if (adjustAlpha)
                {
                    GUI.color = new Color(
                        previousColor.r,
                        previousColor.g,
                        previousColor.b,
                        previousColor.a * Mathf.Clamp01(contentAlpha)
                    );
                }

                try
                {
                    PaginationState pagination = GetOrCreatePaginationState(property);
                    EnsurePaginationBounds(pagination, totalCount);

                    bool isSortedSet = IsSortedSetCached(property);

                    // Refresh the cached items property in case serialized object changed
                    cachedItems = GetOrCreateCachedItemsProperty(listKey, property);
                    itemsProperty = cachedItems.itemsProperty;
                    hasItemsArray = itemsProperty is { isArray: true };
                    totalCount = hasItemsArray ? itemsProperty.arraySize : 0;
                    EnsurePaginationBounds(pagination, totalCount);

                    // Check if an element was modified and force duplicate refresh
                    SetListRenderContext renderContext = GetOrCreateListContext(listKey);
                    bool forceRefresh = renderContext.needsDuplicateRefresh;
                    if (forceRefresh)
                    {
                        renderContext.needsDuplicateRefresh = false;
                    }

                    DuplicateState duplicateState = EvaluateDuplicateState(
                        property,
                        itemsProperty,
                        forceRefresh
                    );
                    NullEntryState nullState = EvaluateNullEntryState(
                        property,
                        itemsProperty,
                        forceRefresh
                    );

                    if (nullState.hasNullEntries && !string.IsNullOrEmpty(nullState.summary))
                    {
                        float helpHeight = GetWarningBarHeight();
                        Rect helpRect = new(position.x, y, position.width, helpHeight);
                        EditorGUI.HelpBox(helpRect, nullState.summary, MessageType.Warning);
                        y = helpRect.yMax + SectionSpacing;
                    }

                    if (
                        duplicateState.hasDuplicates
                        && !string.IsNullOrEmpty(duplicateState.summary)
                    )
                    {
                        float helpHeight = GetWarningBarHeight();
                        Rect helpRect = new(position.x, y, position.width, helpHeight);
                        EditorGUI.HelpBox(helpRect, duplicateState.summary, MessageType.Warning);
                        y = helpRect.yMax + SectionSpacing;
                    }

                    bool drewPendingEntry = false;
                    if (hasInspector && elementType != null)
                    {
                        PendingEntry pendingEntry = GetOrCreatePendingEntry(
                            property,
                            propertyPath,
                            elementType,
                            isSortedSet
                        );
                        DrawPendingEntryUI(
                            ref y,
                            position,
                            pendingEntry,
                            property,
                            propertyPath,
                            ref itemsProperty,
                            pagination,
                            inspector,
                            elementType
                        );
                        y += SectionSpacing;
                        drewPendingEntry = true;
                    }

                    if (totalCount <= 0)
                    {
                        if (drewPendingEntry)
                        {
                            float infoHeight = GetWarningBarHeight();
                            Rect infoRect = new(position.x, y, position.width, infoHeight);
                            EditorGUI.HelpBox(infoRect, "No entries yet.", MessageType.Info);
                        }
                        else
                        {
                            float emptyHeight = GetEmptySetDrawerHeight();
                            Rect emptyRect = new(position.x, y, position.width, emptyHeight);
                            LastItemsContainerRect = emptyRect;
                            HasItemsContainerRect = true;
                            DrawEmptySetDrawer(
                                emptyRect,
                                property,
                                propertyPath,
                                itemsProperty,
                                pagination
                            );
                        }
                    }
                    else
                    {
                        UpdateListContext(
                            listKey,
                            property,
                            itemsProperty,
                            duplicateState,
                            nullState,
                            elementType
                        );
                        ReorderableList list = GetOrCreateList(
                            listKey,
                            property,
                            itemsProperty,
                            pagination,
                            propertyPath,
                            isSortedSet
                        );
                        float listHeight =
                            list?.GetHeight()
                            ?? GetPaginationHeaderHeight()
                                + EditorGUIUtility.singleLineHeight
                                    * Mathf.Max(1, Mathf.Min(totalCount, pagination.pageSize))
                                + GetFooterHeight();
                        Rect listRect = new(position.x, y, position.width, listHeight);
                        if (list != null && Event.current.type == EventType.Repaint)
                        {
                            GUIStyle listBackgroundStyle =
                                ReorderableList.defaultBehaviours.boxBackground ?? GUI.skin.box;
                            float headerHeight = Mathf.Max(0f, list.headerHeight);
                            float footerHeight = Mathf.Max(0f, list.footerHeight);
                            float bodyHeight = Mathf.Max(
                                0f,
                                listRect.height - headerHeight - footerHeight
                            );
                            if (bodyHeight > 0f)
                            {
                                float overlap = Mathf.Min(5f, bodyHeight);
                                float bodyTop = listRect.y + headerHeight - overlap;
                                float adjustedHeight = bodyHeight + overlap;
                                Rect bodyRect = new(
                                    listRect.x,
                                    bodyTop,
                                    listRect.width,
                                    adjustedHeight
                                );
                                listBackgroundStyle.Draw(
                                    bodyRect,
                                    GUIContent.none,
                                    false,
                                    false,
                                    false,
                                    false
                                );
                            }
                        }
                        LastItemsContainerRect = listRect;
                        HasItemsContainerRect = true;
                        if (list != null)
                        {
                            list.DoList(listRect);
                        }
                        else
                        {
                            GUI.Box(listRect, GUIContent.none, EditorStyles.helpBox);
                        }
                    }
                }
                finally
                {
                    if (adjustAlpha)
                    {
                        GUI.color = previousColor;
                    }
                }

                bool applied = serializedObject.ApplyModifiedProperties();
                if (applied)
                {
                    SyncRuntimeSet(property);
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentScope;
                EditorGUI.EndProperty();
            }
        }

        private static Rect ResolveContentRect(Rect position, bool skipIndentation = false)
        {
            float leftPadding = GroupGUIWidthUtility.CurrentLeftPadding;
            float rightPadding = GroupGUIWidthUtility.CurrentRightPadding;
            int indentLevel = EditorGUI.indentLevel;
            bool isInsideWGroupProperty = GroupGUIWidthUtility.IsInsideWGroupPropertyDraw;

            // When inside WGroup property context, WGroup uses EditorGUILayout.PropertyField
            // which means Unity's layout system has ALREADY:
            // 1. Positioned the rect based on the current layout group (with WGroup padding)
            // 2. Applied indentation based on EditorGUI.indentLevel
            // Apply a small alignment offset to align with other WGroup content.
            if (isInsideWGroupProperty)
            {
                const float WGroupAlignmentOffset = -4f;
                Rect alignedPosition = position;
                // Note: Modifying xMin automatically adjusts width to keep xMax constant
                alignedPosition.xMin += WGroupAlignmentOffset;
                return alignedPosition;
            }

            // When skipIndentation is true, we're in a GUILayout context (e.g., SettingsProvider)
            // where Unity's layout system handles standard indentation.
            // However, WGroup padding (tracked via GroupGUIWidthUtility) is NOT automatically
            // applied by the layout system - we must still apply it manually.
            if (skipIndentation)
            {
                Rect result = position;

                // Apply WGroup padding even when skipping standard indentation
                if (leftPadding > 0f || rightPadding > 0f)
                {
                    result.xMin += leftPadding;
                    result.xMax -= rightPadding;
                    if (result.width < 0f || float.IsNaN(result.width))
                    {
                        result.width = 0f;
                    }
                }

                return result;
            }

            // Normal context (outside WGroup): apply WGroup padding ourselves
            Rect padded = GroupGUIWidthUtility.ApplyCurrentPadding(position);
            if (
                (leftPadding > 0f || rightPadding > 0f)
                && Mathf.Approximately(padded.xMin, position.xMin)
                && Mathf.Approximately(padded.width, position.width)
            )
            {
                padded.xMin += leftPadding;
                padded.xMax -= rightPadding;
                if (padded.width < 0f || float.IsNaN(padded.width))
                {
                    padded.width = 0f;
                }
            }

            // Skip EditorGUI.IndentedRect when indentLevel is 0 to ensure consistent behavior
            // across all Unity versions. In some versions, IndentedRect unexpectedly modifies
            // the rect width at level 0 (shifts xMax left by ~1.25), while in other versions
            // it returns the rect unchanged. By skipping the call at level 0, we handle both
            // cases consistently. Our own alignment offset is applied below for level 0.
            Rect indentedResult = indentLevel > 0 ? EditorGUI.IndentedRect(padded) : padded;

            // Clamp width to non-negative after IndentedRect (high indent levels can cause negative width)
            if (indentedResult.width < 0f || float.IsNaN(indentedResult.width))
            {
                indentedResult.width = 0f;
            }

            Rect final = indentedResult;

            // Only apply Unity list alignment offset when truly outside a WGroup context
            // (scopeDepth == 0 means no WGroup padding is active)
            int scopeDepth = GroupGUIWidthUtility.CurrentScopeDepth;
            if (scopeDepth == 0)
            {
                // When outside a WGroup, shift slightly left to align with Unity's default
                // list/array rendering
                const float UnityListAlignmentOffset = -1.25f;
                // Note: Modifying xMin automatically adjusts width to keep xMax constant
                final.xMin += UnityListAlignmentOffset;

                // Ensure xMin doesn't go negative (can happen when original rect starts at x=0)
                // Simply clamp xMin to 0; this preserves xMax and correctly adjusts width
                if (final.xMin < 0f)
                {
                    final.xMin = 0f;
                }
            }

            return final;
        }

        /// <summary>
        /// Resolves the content rect for testing purposes, applying WGroup padding and indentation
        /// without requiring a full OnGUI context.
        /// </summary>
        /// <param name="position">The original position rect.</param>
        /// <param name="skipIndentation">Whether to skip standard Unity indentation.</param>
        /// <returns>The resolved content rect.</returns>
        internal static Rect ResolveContentRectForTests(Rect position, bool skipIndentation = false)
        {
            return ResolveContentRect(position, skipIndentation);
        }

        internal static void ResetLayoutTrackingForTests()
        {
            HasLastManualEntryHeaderRect = false;
            LastManualEntryHeaderRect = default;
            LastManualEntryToggleRect = default;
            HasLastMainFoldoutRect = false;
            LastMainFoldoutRect = default;
            HasLastManualEntryValueRect = false;
            LastManualEntryValueRect = default;
            LastManualEntryValueUsedFoldoutLabel = false;
            LastManualEntryValueFoldoutOffset = 0f;
            HasLastRowContentRect = false;
            LastRowContentRect = default;

            // Reset footer tracking properties
            LastFooterWGroupLeftPadding = 0f;
            LastFooterWGroupRightPadding = 0f;
            LastFooterAvailableWidth = 0f;
            LastFooterRangeWidth = 0f;
            LastFooterRangeLabelWasDrawn = false;
            LastFooterRangeLabelRect = default;

            // Clear main foldout animation cache
            foreach (KeyValuePair<MainFoldoutCacheKey, AnimBool> kvp in MainFoldoutAnimations)
            {
                kvp.Value?.valueChanged.RemoveListener(RequestRepaint);
            }
            MainFoldoutAnimations.Clear();
        }

        /// <summary>
        /// Signals that a child property drawer's height has changed and the parent
        /// SerializableSetPropertyDrawer should invalidate its row height cache.
        /// This should be called when nested foldouts or expandable sections change state.
        /// </summary>
        internal static void SignalChildHeightChanged()
        {
            _childHeightChangedFrame = Time.frameCount;
            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// Gets the frame number when the child height changed signal was last set.
        /// For testing purposes only.
        /// </summary>
        internal static int GetChildHeightChangedFrameForTests()
        {
            return _childHeightChangedFrame;
        }

        /// <summary>
        /// Resets the child height changed frame to -1 for testing purposes.
        /// </summary>
        internal static void ResetChildHeightChangedFrameForTests()
        {
            _childHeightChangedFrame = -1;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUIUtility.singleLineHeight;

            PaginationState pagination = GetOrCreatePaginationState(property);

            string listKey = GetListKey(property);
            CachedItemsProperty cachedItems = GetOrCreateCachedItemsProperty(listKey, property);
            SerializedProperty itemsProperty = cachedItems.itemsProperty;

            bool hasItemsArray = itemsProperty is { isArray: true };
            int totalCount = hasItemsArray ? itemsProperty.arraySize : 0;
            string propertyPath = property.propertyPath;
            bool hasInspector = TryGetSetInspectorCached(
                property,
                propertyPath,
                out ISerializableSetInspector inspector
            );
            Type elementType = inspector?.ElementType;

            bool isSortedSet = IsSortedSetCached(property);

            // Get main foldout animation progress
            float mainFoldoutProgress = GetMainFoldoutProgress(
                property.serializedObject,
                propertyPath,
                property.isExpanded,
                isSortedSet
            );

            // If fully collapsed (animation complete), return only header height
            if (mainFoldoutProgress <= 0f)
            {
                return baseHeight + InspectorHeightPadding;
            }

            string cacheKey = listKey;
            int currentFrame = Time.frameCount;

            EnsurePaginationBounds(pagination, totalCount);
            int pageIndex = pagination.page;

            DuplicateState duplicateState = EvaluateDuplicateState(property, itemsProperty);
            NullEntryState nullState = EvaluateNullEntryState(property, itemsProperty);

            bool hasNullEntries =
                nullState.hasNullEntries && !string.IsNullOrEmpty(nullState.summary);
            bool hasDuplicates =
                duplicateState.hasDuplicates && !string.IsNullOrEmpty(duplicateState.summary);

            bool shouldDrawPendingEntry = hasInspector && elementType != null;
            PendingEntry pendingEntry = null;
            bool pendingIsExpanded = false;
            float pendingFoldoutProgress = 0f;
            if (shouldDrawPendingEntry)
            {
                pendingEntry = GetOrCreatePendingEntry(
                    property,
                    propertyPath,
                    elementType,
                    isSortedSet
                );
                pendingIsExpanded = pendingEntry.isExpanded;
                pendingFoldoutProgress = GetPendingFoldoutProgress(pendingEntry);
            }

            if (_heightCache.TryGetValue(cacheKey, out HeightCacheEntry cached))
            {
                const float foldoutProgressTolerance = 0.001f;
                if (
                    cached.frameNumber == currentFrame
                    && cached.arraySize == totalCount
                    && cached.pageIndex == pageIndex
                    && cached.isExpanded == property.isExpanded
                    && cached.hasNullEntries == hasNullEntries
                    && cached.hasDuplicates == hasDuplicates
                    && cached.pendingIsExpanded == pendingIsExpanded
                    && Mathf.Abs(cached.pendingFoldoutProgress - pendingFoldoutProgress)
                        < foldoutProgressTolerance
                    && Mathf.Abs(cached.mainFoldoutProgress - mainFoldoutProgress)
                        < foldoutProgressTolerance
                )
                {
                    return cached.height;
                }
            }

            // Calculate the expanded content height (everything after the header)
            float footerHeight = GetFooterHeight();
            float expandedContentHeight = SectionSpacing;

            if (hasNullEntries)
            {
                expandedContentHeight += GetWarningBarHeight() + SectionSpacing;
            }

            if (hasDuplicates)
            {
                expandedContentHeight += GetWarningBarHeight() + SectionSpacing;
            }

            float pendingHeightWithSpacing = 0f;
            if (shouldDrawPendingEntry && pendingEntry != null)
            {
                pendingHeightWithSpacing = GetPendingSectionHeight(pendingEntry) + SectionSpacing;
            }

            if (totalCount <= 0)
            {
                if (shouldDrawPendingEntry)
                {
                    expandedContentHeight += pendingHeightWithSpacing;
                    expandedContentHeight += GetWarningBarHeight() + SectionSpacing;
                }
                else
                {
                    expandedContentHeight += GetEmptySetDrawerHeight() + SectionSpacing;
                }
            }
            else
            {
                propertyPath = property.propertyPath;
                UpdateListContext(
                    listKey,
                    property,
                    itemsProperty,
                    duplicateState,
                    nullState,
                    elementType
                );
                ReorderableList list = GetOrCreateList(
                    listKey,
                    property,
                    itemsProperty,
                    pagination,
                    propertyPath,
                    isSortedSet
                );

                float listHeight =
                    list?.GetHeight()
                    ?? GetPaginationHeaderHeight()
                        + EditorGUIUtility.singleLineHeight
                            * Mathf.Max(1, Mathf.Min(totalCount, pagination.pageSize))
                        + footerHeight;
                expandedContentHeight += listHeight + SectionSpacing;

                if (shouldDrawPendingEntry)
                {
                    expandedContentHeight += pendingHeightWithSpacing;
                }
            }

            // Interpolate height based on main foldout animation progress
            float height = baseHeight + (expandedContentHeight * mainFoldoutProgress);
            float finalHeight = height + InspectorHeightPadding;

            CacheHeight(
                cacheKey,
                finalHeight,
                totalCount,
                pageIndex,
                property.isExpanded,
                hasNullEntries,
                hasDuplicates,
                pendingIsExpanded,
                pendingFoldoutProgress,
                mainFoldoutProgress,
                currentFrame
            );
            return finalHeight;
        }

        private void CacheHeight(
            string cacheKey,
            float height,
            int arraySize,
            int pageIndex,
            bool isExpanded,
            bool hasNullEntries,
            bool hasDuplicates,
            bool pendingIsExpanded,
            float pendingFoldoutProgress,
            float mainFoldoutProgress,
            int frameNumber
        )
        {
            if (!_heightCache.TryGetValue(cacheKey, out HeightCacheEntry entry))
            {
                entry = new HeightCacheEntry();
                _heightCache[cacheKey] = entry;
            }
            entry.height = height;
            entry.arraySize = arraySize;
            entry.pageIndex = pageIndex;
            entry.isExpanded = isExpanded;
            entry.hasNullEntries = hasNullEntries;
            entry.hasDuplicates = hasDuplicates;
            entry.pendingIsExpanded = pendingIsExpanded;
            entry.pendingFoldoutProgress = pendingFoldoutProgress;
            entry.mainFoldoutProgress = mainFoldoutProgress;
            entry.frameNumber = frameNumber;
        }

        internal PaginationState GetOrCreatePaginationState(SerializedProperty property)
        {
            string key = GetPropertyCacheKey(property);
            PaginationState state = _paginationStates.GetOrAdd(key);

            int configuredPageSize = UnityHelpersSettings.GetSerializableSetPageSize();
            if (state.pageSize != configuredPageSize)
            {
                state.pageSize = configuredPageSize;
                state.page = 0;
            }

            return state;
        }

        internal ReorderableList GetOrCreateList(SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            string listKey = GetListKey(property);
            CachedItemsProperty cachedItems = GetOrCreateCachedItemsProperty(listKey, property);
            SerializedProperty itemsProperty = cachedItems.itemsProperty;
            PaginationState pagination = GetOrCreatePaginationState(property);
            EnsurePaginationBounds(
                pagination,
                itemsProperty is { isArray: true } ? itemsProperty.arraySize : 0
            );

            DuplicateState duplicateState = EvaluateDuplicateState(property, itemsProperty);
            NullEntryState nullState = EvaluateNullEntryState(property, itemsProperty);
            string propertyPath = property.propertyPath;
            bool isSortedSet = IsSortedSetCached(property);
            Type elementType = null;
            if (
                !string.IsNullOrEmpty(propertyPath)
                && TryGetSetInspectorCached(
                    property,
                    propertyPath,
                    out ISerializableSetInspector inspector
                )
            )
            {
                elementType = inspector.ElementType;
            }
            UpdateListContext(
                listKey,
                property,
                itemsProperty,
                duplicateState,
                nullState,
                elementType
            );

            return GetOrCreateList(
                listKey,
                property,
                itemsProperty,
                pagination,
                propertyPath,
                isSortedSet
            );
        }

        internal SetListRenderContext GetOrCreateListContext(string key)
        {
            if (_listContexts.TryGetValue(key, out SetListRenderContext context))
            {
                return context;
            }

            context = new SetListRenderContext();
            _listContexts[key] = context;
            return context;
        }

        internal string GetPropertyCacheKey(SerializedProperty property)
        {
            string propertyPath = property?.propertyPath;
            if (
                propertyPath != null
                && string.Equals(_cachedPropertyPath, propertyPath, StringComparison.Ordinal)
            )
            {
                return _cachedListKey;
            }

            string key = BuildPropertyCacheKey(property);
            _cachedPropertyPath = propertyPath;
            _cachedListKey = key;
            return key;
        }

        /// <summary>
        /// Struct key for static property cache lookup to avoid string allocations.
        /// </summary>
        private readonly struct PropertyCacheKey : IEquatable<PropertyCacheKey>
        {
            public readonly int InstanceId;
            public readonly string PropertyPath;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public PropertyCacheKey(int instanceId, string propertyPath)
            {
                InstanceId = instanceId;
                PropertyPath = propertyPath;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(PropertyCacheKey other)
            {
                return InstanceId == other.InstanceId
                    && string.Equals(PropertyPath, other.PropertyPath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is PropertyCacheKey other && Equals(other);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode()
            {
                return Objects.HashCode(InstanceId, PropertyPath);
            }
        }

        // Static cache for single-target property cache keys to avoid repeated string allocations
        private static readonly Dictionary<PropertyCacheKey, string> SingleTargetPropertyKeyCache =
            new();

        private const int MaxSingleTargetPropertyKeyCacheSize = 512;

        private static string BuildPropertyCacheKey(SerializedProperty property)
        {
            if (property == null)
            {
                return string.Empty;
            }

            SerializedObject serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath ?? string.Empty;

            if (serializedObject == null)
            {
                return propertyPath;
            }

            Object[] targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                int fallbackId = RuntimeHelpers.GetHashCode(serializedObject);
                return $"{fallbackId}_{propertyPath}";
            }

            // Common case: single target - use static cache to avoid string allocation
            if (targets.Length == 1 && targets[0] != null)
            {
                int instanceId = targets[0].GetInstanceID();
                PropertyCacheKey cacheKey = new(instanceId, propertyPath);

                if (SingleTargetPropertyKeyCache.TryGetValue(cacheKey, out string cached))
                {
                    return cached;
                }

                // Build and cache the key string
                using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                    propertyPath.Length + 16,
                    out StringBuilder builder
                );
                builder.Clear();
                builder.Append(instanceId);
                builder.Append('_');
                builder.Append(propertyPath);
                string result = builder.ToString();

                // Limit cache size to prevent unbounded growth
                if (SingleTargetPropertyKeyCache.Count < MaxSingleTargetPropertyKeyCacheSize)
                {
                    SingleTargetPropertyKeyCache[cacheKey] = result;
                }

                return result;
            }

            using PooledResource<StringBuilder> keyBuilderLease = Buffers.GetStringBuilder(
                propertyPath.Length + Math.Max(32, targets.Length * 12),
                out StringBuilder keyBuilder
            );
            keyBuilder.Append(propertyPath);
            keyBuilder.Append('|');

            for (int index = 0; index < targets.Length; index++)
            {
                int id = targets[index] != null ? targets[index].GetInstanceID() : 0;
                keyBuilder.Append(id);
                if (index < targets.Length - 1)
                {
                    keyBuilder.Append(',');
                }
            }

            return keyBuilder.ToString();
        }

        internal string GetListKey(SerializedProperty property)
        {
            return GetPropertyCacheKey(property);
        }

        private CachedItemsProperty GetOrCreateCachedItemsProperty(
            string listKey,
            SerializedProperty setProperty
        )
        {
            int currentFrame = Time.frameCount;
            if (_lastItemsPropertyCacheFrame != currentFrame)
            {
                _cachedItemsProperties.Clear();
                _lastItemsPropertyCacheFrame = currentFrame;
            }

            if (_cachedItemsProperties.TryGetValue(listKey, out CachedItemsProperty cached))
            {
                return cached;
            }

            CachedItemsProperty entry = new()
            {
                itemsProperty = setProperty.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                ),
            };
            _cachedItemsProperties[listKey] = entry;
            return entry;
        }

        private static RowFoldoutKey BuildRowFoldoutKey(string cacheKey, int globalIndex)
        {
            return new RowFoldoutKey(cacheKey, globalIndex);
        }

        private bool EnsureRowFoldoutState(RowFoldoutKey foldoutKey, SerializedProperty element)
        {
            if (element == null || !foldoutKey.IsValid)
            {
                return false;
            }

            bool state = _rowFoldoutStates.GetOrAdd(foldoutKey, _ => true);
            element.isExpanded = state;
            return state;
        }

        private void InvalidateRowFoldoutStates(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey) || _rowFoldoutStates.Count == 0)
            {
                return;
            }

            using PooledResource<List<RowFoldoutKey>> keysToRemoveResource =
                Buffers<RowFoldoutKey>.List.Get(out List<RowFoldoutKey> keysToRemove);

            foreach (KeyValuePair<RowFoldoutKey, bool> entry in _rowFoldoutStates)
            {
                if (entry.Key.IsValid && entry.Key.CacheKey == cacheKey)
                {
                    keysToRemove.Add(entry.Key);
                }
            }

            foreach (RowFoldoutKey key in keysToRemove)
            {
                _rowFoldoutStates.Remove(key);
            }
        }

        private static void EnsurePaginationBounds(PaginationState state, int totalCount)
        {
            int pageSize = state.pageSize;
            if (totalCount <= 0)
            {
                state.page = 0;
                state.selectedIndex = -1;
                return;
            }

            if (state.selectedIndex >= totalCount)
            {
                state.selectedIndex = totalCount - 1;
            }

            if (state.selectedIndex < 0)
            {
                state.selectedIndex = -1;
            }

            int pageCount = pageSize > 0 ? Mathf.Max(1, (totalCount + pageSize - 1) / pageSize) : 1;

            if (state.page >= pageCount)
            {
                state.page = pageCount - 1;
            }

            if (state.page < 0)
            {
                state.page = 0;
            }

            if (state.selectedIndex >= 0)
            {
                int selectedPage = Mathf.Clamp(state.selectedIndex / pageSize, 0, pageCount - 1);
                state.page = selectedPage;
            }
        }

        private static bool RelativeIndexIsValid(ListPageCache cache, int relativeIndex)
        {
            return cache != null && relativeIndex >= 0 && relativeIndex < cache.entries.Count;
        }

        private static int GetRelativeIndex(ListPageCache cache, int globalIndex)
        {
            if (cache == null || globalIndex < 0)
            {
                return -1;
            }

            for (int i = 0; i < cache.entries.Count; i++)
            {
                if (cache.entries[i].arrayIndex == globalIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SyncListSelectionWithPagination(
            ReorderableList list,
            PaginationState pagination,
            ListPageCache cache
        )
        {
            if (list == null || pagination == null || cache == null)
            {
                return;
            }

            int relativeIndex = GetRelativeIndex(cache, pagination.selectedIndex);
            list.index = relativeIndex;
        }

        private void UpdateListContext(
            string listKey,
            SerializedProperty setProperty,
            SerializedProperty itemsProperty,
            DuplicateState duplicateState,
            NullEntryState nullState,
            Type elementType
        )
        {
            SetListRenderContext context = GetOrCreateListContext(listKey);
            context.setProperty = setProperty;
            context.itemsProperty = itemsProperty;
            context.duplicateState = duplicateState;
            context.nullState = nullState;
            context.elementType = elementType;
        }

        private ReorderableList GetOrCreateList(
            string listKey,
            SerializedProperty property,
            SerializedProperty itemsProperty,
            PaginationState pagination,
            string propertyPath,
            bool isSortedSet
        )
        {
            Func<ListPageCache> cacheProvider = () =>
                EnsurePageCache(listKey, itemsProperty, pagination);
            ListPageCache cache = cacheProvider();

            ReorderableList list;

            bool hasExisting = _lists.TryGetValue(listKey, out ReorderableList existing);
            if (hasExisting && existing.headerHeight > 0f)
            {
                list = existing;
                list.list = cache.entries;
            }
            else
            {
                if (hasExisting)
                {
                    _lists.Remove(listKey);
                }

                list = new ReorderableList(
                    cache.entries,
                    typeof(PageEntry),
                    draggable: true,
                    displayHeader: true,
                    displayAddButton: false,
                    displayRemoveButton: false
                )
                {
                    elementHeight = EditorGUIUtility.singleLineHeight,
                    footerHeight = 0f,
                    elementHeightCallback = index =>
                        GetSetListElementHeight(listKey, cacheProvider(), index),
                    drawElementCallback = (rect, index, active, focused) =>
                    {
                        DrawSetListElement(listKey, cacheProvider(), rect, index);
                    },
                };

                list.onReorderCallbackWithDetails = (_, oldIndex, newIndex) =>
                {
                    HandleListReorder(
                        listKey,
                        property,
                        itemsProperty,
                        pagination,
                        cacheProvider(),
                        oldIndex,
                        newIndex
                    );
                    ListPageCache refreshedCache = cacheProvider();
                    SyncListSelectionWithPagination(list, pagination, refreshedCache);
                };

                list.onSelectCallback = reorderableList =>
                {
                    if (!RelativeIndexIsValid(cacheProvider(), reorderableList.index))
                    {
                        return;
                    }

                    PageEntry entry = cacheProvider().entries[reorderableList.index];
                    pagination.selectedIndex = entry.arrayIndex;
                };

                _lists[listKey] = list;
            }

            list.list = cache.entries;
            list.headerHeight = GetPaginationHeaderHeight();
            list.footerHeight = GetFooterHeight();
            SerializedObject serializedObject = property.serializedObject;
            list.drawFooterCallback = rect =>
            {
                SerializedProperty currentProperty = serializedObject?.FindProperty(propertyPath);
                SerializedProperty currentItemsProperty = currentProperty?.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );
                if (currentItemsProperty == null)
                {
                    return;
                }

                DrawFooterControls(
                    rect,
                    currentProperty,
                    propertyPath,
                    currentItemsProperty,
                    pagination,
                    isSortedSet,
                    cacheProvider
                );
            };
            list.drawHeaderCallback = rect =>
            {
                ListPageCache currentCache = cacheProvider();
                int totalCount =
                    currentCache != null ? Mathf.Max(0, currentCache.itemCount)
                    : itemsProperty is { isArray: true } ? Mathf.Max(0, itemsProperty.arraySize)
                    : 0;
                DrawHeaderControls(rect, pagination, totalCount);
            };

            SyncListSelectionWithPagination(list, pagination, cache);
            return list;
        }

        private void DrawFooterControls(
            Rect rect,
            SerializedProperty property,
            string propertyPath,
            SerializedProperty itemsProperty,
            PaginationState pagination,
            bool isSortedSet,
            Func<ListPageCache> cacheProvider
        )
        {
            SerializedProperty propertyRef = property;
            SerializedProperty itemsPropertyRef = itemsProperty;
            if (
                !TryGetSetInspector(
                    propertyRef,
                    propertyPath,
                    out ISerializableSetInspector inspector
                )
            )
            {
                return;
            }

            if (itemsPropertyRef == null)
            {
                return;
            }

            ListPageCache cache = cacheProvider?.Invoke();

            if (Event.current.type == EventType.Repaint)
            {
                GUIStyle footerStyle =
                    ReorderableList.defaultBehaviours.footerBackground ?? "RL Footer";
                footerStyle.Draw(rect, GUIContent.none, false, false, false, false);
                DrawSetBodyTopBorder(new Rect(rect.x, rect.y, rect.width, 1f));
            }

            Type elementType = inspector.ElementType;
            int totalCount = itemsPropertyRef is { isArray: true } ? itemsPropertyRef.arraySize : 0;
            float padding = 4f;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float verticalCenter = rect.y + Mathf.Max(0f, (rect.height - lineHeight) * 0.5f);
            float rightCursor = rect.xMax - padding;
            float leftCursor = rect.x + padding;
            float buttonSpacing = ButtonSpacing;

            Rect addRect = new(rightCursor - 60f, verticalCenter, 60f, lineHeight);
            if (GUI.Button(addRect, AddEntryContent, AddButtonStyle))
            {
                if (
                    TryAddNewElement(
                        ref propertyRef,
                        propertyPath,
                        ref itemsPropertyRef,
                        pagination
                    )
                )
                {
                    itemsPropertyRef = propertyRef.FindPropertyRelative(
                        SerializableHashSetSerializedPropertyNames.Items
                    );
                    totalCount = itemsPropertyRef is { isArray: true }
                        ? itemsPropertyRef.arraySize
                        : 0;
                    EnsurePaginationBounds(pagination, totalCount);
                }
            }
            rightCursor = addRect.x - buttonSpacing;

            Rect clearRect = new(rightCursor - 80f, verticalCenter, 80f, lineHeight);
            bool canClear = totalCount > 0;
            GUIStyle clearStyle = canClear
                ? ClearAllActiveButtonStyle
                : ClearAllInactiveButtonStyle;
            if (GUI.Button(clearRect, ClearAllContent, clearStyle) && canClear)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Clear Set",
                    "Remove all entries from this set?",
                    "Clear",
                    "Cancel"
                );
                if (confirmed && TryClearSet(ref propertyRef, propertyPath, ref itemsPropertyRef))
                {
                    pagination.page = 0;
                    pagination.selectedIndex = -1;
                    itemsPropertyRef = propertyRef.FindPropertyRelative(
                        SerializableHashSetSerializedPropertyNames.Items
                    );
                    totalCount = itemsPropertyRef is { isArray: true }
                        ? itemsPropertyRef.arraySize
                        : 0;
                    EnsurePaginationBounds(pagination, totalCount);
                }
            }
            rightCursor = clearRect.x - buttonSpacing;

            bool allowSort = isSortedSet || ElementSupportsManualSorting(elementType);
            bool showSort = PageEntriesNeedSorting(cache, itemsPropertyRef, allowSort);
            if (showSort)
            {
                Rect sortRect = new Rect(rightCursor - 60f, verticalCenter, 60f, lineHeight);
                bool canSort = showSort;
                using (new EditorGUI.DisabledScope(!canSort))
                {
                    GUIStyle sortStyle = SolidButtonStyles.GetSolidButtonStyle(
                        "Sort",
                        canSort && GUI.enabled
                    );
                    if (GUI.Button(sortRect, SortContent, sortStyle) && canSort)
                    {
                        if (TrySortElements(ref propertyRef, propertyPath, itemsPropertyRef))
                        {
                            itemsPropertyRef = propertyRef.FindPropertyRelative(
                                SerializableHashSetSerializedPropertyNames.Items
                            );
                            totalCount = itemsPropertyRef is { isArray: true }
                                ? itemsPropertyRef.arraySize
                                : 0;
                            EnsurePaginationBounds(pagination, totalCount);
                        }
                    }
                }
                rightCursor = sortRect.x - buttonSpacing;
            }

            bool hasSelection =
                totalCount > 0
                && pagination.selectedIndex >= 0
                && pagination.selectedIndex < totalCount;

            if (hasSelection)
            {
                float removeWidth = Mathf.Max(18f, PaginationButtonWidth - 8f);
                Rect removeRect = new(
                    rightCursor - removeWidth,
                    verticalCenter,
                    removeWidth,
                    lineHeight
                );
                if (GUI.Button(removeRect, "-", RemoveButtonStyle))
                {
                    TryRemoveSelectedEntry(
                        ref propertyRef,
                        propertyPath,
                        ref itemsPropertyRef,
                        pagination
                    );
                    itemsPropertyRef = propertyRef.FindPropertyRelative(
                        SerializableHashSetSerializedPropertyNames.Items
                    );
                    totalCount = itemsPropertyRef is { isArray: true }
                        ? itemsPropertyRef.arraySize
                        : 0;
                    EnsurePaginationBounds(pagination, totalCount);
                }

                rightCursor = removeRect.x - buttonSpacing;

                if (totalCount > 1)
                {
                    float moveWidth = Mathf.Max(20f, PaginationButtonWidth - 10f);
                    Rect moveDownRect = new(
                        rightCursor - moveWidth,
                        verticalCenter,
                        moveWidth,
                        lineHeight
                    );
                    using (new EditorGUI.DisabledScope(pagination.selectedIndex >= totalCount - 1))
                    {
                        if (GUI.Button(moveDownRect, MoveDownContent, MoveButtonStyle))
                        {
                            TryMoveSelectedEntry(
                                ref propertyRef,
                                propertyPath,
                                ref itemsPropertyRef,
                                pagination,
                                direction: 1
                            );
                            itemsPropertyRef = propertyRef.FindPropertyRelative(
                                SerializableHashSetSerializedPropertyNames.Items
                            );
                            totalCount = itemsPropertyRef is { isArray: true }
                                ? itemsPropertyRef.arraySize
                                : 0;
                            EnsurePaginationBounds(pagination, totalCount);
                        }
                    }

                    rightCursor = moveDownRect.x - buttonSpacing;

                    Rect moveUpRect = new(
                        rightCursor - moveWidth,
                        verticalCenter,
                        moveWidth,
                        lineHeight
                    );
                    using (new EditorGUI.DisabledScope(pagination.selectedIndex <= 0))
                    {
                        if (GUI.Button(moveUpRect, MoveUpContent, MoveButtonStyle))
                        {
                            TryMoveSelectedEntry(
                                ref propertyRef,
                                propertyPath,
                                ref itemsPropertyRef,
                                pagination,
                                direction: -1
                            );
                            itemsPropertyRef = propertyRef.FindPropertyRelative(
                                SerializableHashSetSerializedPropertyNames.Items
                            );
                            totalCount = itemsPropertyRef is { isArray: true }
                                ? itemsPropertyRef.arraySize
                                : 0;
                            EnsurePaginationBounds(pagination, totalCount);
                        }
                    }

                    rightCursor = moveUpRect.x - buttonSpacing;
                }
            }

            int pageSize = Mathf.Max(1, pagination.pageSize);
            int start =
                totalCount == 0 ? 0 : Mathf.Clamp(pagination.page * pageSize, 0, totalCount) + 1;
            int end = Mathf.Min((pagination.page + 1) * pageSize, totalCount);
            string rangeText = totalCount == 0 ? "Empty" : GetRangeLabel(start, end, totalCount);
            GUIStyle rangeStyle = EditorStyles.miniLabel;
            RangeLabelGUIContent.text = rangeText;
            float rangeWidth = rangeStyle.CalcSize(RangeLabelGUIContent).x;
            float availableWidth = Mathf.Max(0f, rightCursor - leftCursor);

            // Capture footer tracking data for tests
            LastFooterWGroupLeftPadding = GroupGUIWidthUtility.CurrentLeftPadding;
            LastFooterWGroupRightPadding = GroupGUIWidthUtility.CurrentRightPadding;
            LastFooterAvailableWidth = availableWidth;
            LastFooterRangeWidth = rangeWidth;

            if (rangeWidth <= availableWidth)
            {
                float rangeHeight = lineHeight + 4f;
                float rangeY = rect.y + Mathf.Max(0f, (rect.height - rangeHeight) * 0.5f) - 2.5f;
                Rect rangeRect = new(leftCursor, rangeY, rangeWidth, rangeHeight);
                EditorGUI.LabelField(rangeRect, rangeText, rangeStyle);
                LastFooterRangeLabelWasDrawn = true;
                LastFooterRangeLabelRect = rangeRect;
            }
            else
            {
                LastFooterRangeLabelWasDrawn = false;
                LastFooterRangeLabelRect = default;
            }
        }

        private void DrawHeaderControls(Rect rect, PaginationState pagination, int totalCount)
        {
            Rect contentRect = new(rect.x + 8f, rect.y, rect.width - 16f, rect.height);
            float navWidthFull = PaginationButtonWidth * 4f + ButtonSpacing * 3f;
            float navWidthCompact = PaginationButtonWidth * 2f + ButtonSpacing;
            float labelWidth = PaginationLabelWidth;

            PaginationControlLayout layout = PaginationControlLayout.Full;
            bool showLabel = true;
            float navWidth = navWidthFull;
            float controlsWidth = navWidth + labelWidth + ButtonSpacing;

            if (controlsWidth > contentRect.width)
            {
                showLabel = false;
                controlsWidth = navWidth;
            }

            if (controlsWidth > contentRect.width)
            {
                layout = PaginationControlLayout.PrevNext;
                navWidth = navWidthCompact;
                controlsWidth = showLabel ? navWidth + labelWidth + ButtonSpacing : navWidth;
            }

            if (controlsWidth > contentRect.width)
            {
                layout = PaginationControlLayout.None;
                navWidth = 0f;
                showLabel = false;
            }

            if (layout == PaginationControlLayout.None && !showLabel)
            {
                return;
            }

            Rect controlsRect = new(
                contentRect.xMax - Mathf.Max(controlsWidth, 0f),
                contentRect.y,
                Mathf.Max(controlsWidth, 0f),
                contentRect.height
            );
            float labelHeight = EditorGUIUtility.singleLineHeight;
            float labelCenterOffset = Mathf.Max(0f, (controlsRect.height - labelHeight) * 0.5f);

            float cursor = controlsRect.x;
            if (showLabel)
            {
                int pageSize = Mathf.Max(1, pagination.pageSize);
                int pageCount =
                    totalCount > 0 ? Mathf.Max(1, (totalCount + pageSize - 1) / pageSize) : 1;
                int currentPage =
                    totalCount > 0 ? Mathf.Clamp(pagination.page + 1, 1, pageCount) : 0;
                PaginationPageLabelContent.text =
                    totalCount == 0 ? "Page 0/0" : GetPaginationLabel(currentPage, pageCount);
                float labelY = controlsRect.y + labelCenterOffset + PaginationLabelVerticalOffset;
                Rect labelRect = new(cursor, labelY, labelWidth, labelHeight);
                EditorGUI.LabelField(labelRect, PaginationPageLabelContent, EditorStyles.miniLabel);
                cursor = labelRect.xMax + ButtonSpacing;
            }

            if (layout == PaginationControlLayout.None)
            {
                return;
            }

            float buttonHeight = Mathf.Max(
                0f,
                controlsRect.height - PaginationButtonsVerticalOffset
            );
            float buttonY = controlsRect.y + PaginationButtonsVerticalOffset;
            Rect navRect = new(cursor, buttonY, navWidth, buttonHeight);
            DrawPaginationButtons(navRect, pagination, totalCount);
        }

        private void DrawPaginationButtons(Rect rect, PaginationState pagination, int totalCount)
        {
            int pageSize = pagination.pageSize;
            int pageCount = pageSize > 0 ? Mathf.Max(1, (totalCount + pageSize - 1) / pageSize) : 1;
            int currentPage = pagination.page;

            Rect firstRect = new(rect.x, rect.y, PaginationButtonWidth, rect.height);
            Rect prevRect = new(
                firstRect.xMax + ButtonSpacing,
                rect.y,
                PaginationButtonWidth,
                rect.height
            );
            Rect nextRect = new(
                prevRect.xMax + ButtonSpacing,
                rect.y,
                PaginationButtonWidth,
                rect.height
            );
            Rect lastRect = new(
                nextRect.xMax + ButtonSpacing,
                rect.y,
                PaginationButtonWidth,
                rect.height
            );

            using (new EditorGUI.DisabledScope(currentPage <= 0))
            {
                if (GUI.Button(firstRect, FirstPageContent, EditorStyles.miniButton))
                {
                    pagination.page = 0;
                    SnapSelectionToPage(pagination, totalCount);
                }

                if (GUI.Button(prevRect, PreviousPageContent, EditorStyles.miniButton))
                {
                    pagination.page = Mathf.Max(0, pagination.page - 1);
                    SnapSelectionToPage(pagination, totalCount);
                }
            }

            using (new EditorGUI.DisabledScope(currentPage >= pageCount - 1))
            {
                if (GUI.Button(nextRect, NextPageContent, EditorStyles.miniButton))
                {
                    pagination.page = Mathf.Min(pageCount - 1, pagination.page + 1);
                    SnapSelectionToPage(pagination, totalCount);
                }

                if (GUI.Button(lastRect, LastPageContent, EditorStyles.miniButton))
                {
                    pagination.page = pageCount - 1;
                    SnapSelectionToPage(pagination, totalCount);
                }
            }
        }

        private void DrawPendingEntryUI(
            ref float y,
            Rect fullPosition,
            PendingEntry pending,
            SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination,
            ISerializableSetInspector inspector,
            Type elementType
        )
        {
            if (pending == null)
            {
                return;
            }

            int previousIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            try
            {
                float rowHeight = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;

                float containerY = y;
                float containerX = fullPosition.x;
                float containerWidth = fullPosition.width;

                float resolvedSectionPadding = ResolveManualEntrySectionPadding(property);

                AnimBool foldoutAnim = EnsureManualEntryFoldoutAnim(pending, propertyPath);
                float foldoutProgress = GetPendingFoldoutProgress(pending, propertyPath);
                float sectionHeight = GetPendingSectionHeight(pending, property);

                SerializableCollectionTweenDiagnostics.LogAnimBoolState(
                    "DrawManualEntryUI",
                    propertyPath ?? "(null)",
                    pending.isExpanded,
                    foldoutAnim?.faded ?? (pending.isExpanded ? 1f : 0f),
                    foldoutAnim?.target ?? false ? 1f : 0f,
                    foldoutAnim?.speed ?? 0f,
                    foldoutAnim?.isAnimating ?? false
                );

                Rect backgroundRect = new(containerX, containerY, containerWidth, sectionHeight);
                Color backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.18f, 0.18f, 0.18f, 1f)
                    : new Color(0.92f, 0.92f, 0.92f, 1f);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(backgroundRect, backgroundColor);
                }

                float headerY = containerY + resolvedSectionPadding;
                Rect headerRect = new(
                    containerX + resolvedSectionPadding,
                    headerY,
                    containerWidth - resolvedSectionPadding * 2f,
                    rowHeight
                );

                float resolvedToggleOffset = ResolveManualEntryFoldoutToggleOffset(property);
                float toggleWidthBudget = Mathf.Max(0f, headerRect.width - resolvedToggleOffset);
                float foldoutToggleWidth = Mathf.Min(
                    EditorGUIUtility.singleLineHeight,
                    toggleWidthBudget
                );
                Rect foldoutToggleRect = new(
                    headerRect.x + resolvedToggleOffset,
                    headerRect.y,
                    foldoutToggleWidth,
                    headerRect.height
                );
                float labelWidth = Mathf.Max(
                    0f,
                    headerRect.width - (foldoutToggleRect.xMax - headerRect.x)
                );
                Rect labelHitRect = new(
                    foldoutToggleRect.xMax,
                    headerRect.y,
                    labelWidth,
                    headerRect.height
                );
                float labelContentOffset =
                    ManualEntryFoldoutLabelPadding + ManualEntryFoldoutLabelContentOffset;
                Rect labelRect = new(
                    labelHitRect.x + labelContentOffset,
                    headerRect.y,
                    Mathf.Max(0f, labelHitRect.width - labelContentOffset),
                    headerRect.height
                );

                HasLastManualEntryHeaderRect = true;
                LastManualEntryHeaderRect = headerRect;
                LastManualEntryToggleRect = foldoutToggleRect;

                Event currentEvent = Event.current;
                bool expanded = pending.isExpanded;

                // Log mouse events for debugging click handling in WGroup contexts
                bool mouseInLabelRect = labelHitRect.Contains(currentEvent.mousePosition);
                SerializableCollectionTweenDiagnostics.LogMouseEvent(
                    "ManualEntryLabelClick",
                    propertyPath ?? "(null)",
                    currentEvent.type,
                    currentEvent.mousePosition,
                    labelHitRect,
                    mouseInLabelRect
                );

                if (
                    currentEvent.type == EventType.MouseDown
                    && currentEvent.button == 0
                    && mouseInLabelRect
                )
                {
                    expanded = !expanded;
                    GUI.changed = true;
                    currentEvent.Use();
                }

                EditorGUI.BeginChangeCheck();
                bool arrowExpanded = EditorGUI.Foldout(
                    foldoutToggleRect,
                    expanded,
                    GUIContent.none,
                    toggleOnLabelClick: false
                );
                if (EditorGUI.EndChangeCheck())
                {
                    expanded = arrowExpanded;
                }

                GUIStyle manualEntryLabelStyle = GetManualEntryFoldoutLabelStyle();
                EditorGUIUtility.AddCursorRect(labelHitRect, MouseCursor.Link);
                EditorGUI.LabelField(labelRect, ManualEntryFoldoutContent, manualEntryLabelStyle);

                if (expanded != pending.isExpanded)
                {
                    SerializableCollectionTweenDiagnostics.LogExpandedStateChange(
                        propertyPath ?? "(null)",
                        pending.isExpanded,
                        expanded,
                        foldoutProgress
                    );

                    pending.isExpanded = expanded;
                    foldoutAnim = EnsureManualEntryFoldoutAnim(pending, propertyPath);
                    if (foldoutAnim != null)
                    {
                        foldoutAnim.target = expanded;

                        SerializableCollectionTweenDiagnostics.LogAnimBoolState(
                            "ExpandedStateChange",
                            propertyPath ?? "(null)",
                            expanded,
                            foldoutAnim.faded,
                            foldoutAnim.target ? 1f : 0f,
                            foldoutAnim.speed,
                            foldoutAnim.isAnimating
                        );

                        // Additional timing diagnostic to track if animation should be running
                        SerializableCollectionTweenDiagnostics.LogAnimBoolTiming(
                            "PostExpandChange",
                            propertyPath ?? "(null)",
                            foldoutAnim,
                            expanded
                        );
                    }

                    sectionHeight = GetPendingSectionHeight(pending, property);
                    backgroundRect.height = sectionHeight;
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(backgroundRect, backgroundColor);
                    }
                    RequestRepaint();
                }

                if (foldoutAnim != null)
                {
                    foldoutAnim.target = pending.isExpanded;
                    foldoutProgress = foldoutAnim.faded;
                }
                else
                {
                    foldoutProgress = pending.isExpanded ? 1f : 0f;
                }

                SerializableCollectionTweenDiagnostics.LogContentFadeApplication(
                    propertyPath ?? "(null)",
                    foldoutProgress,
                    foldoutProgress > 0f || pending.isExpanded,
                    foldoutProgress <= 0f && !pending.isExpanded
                );

                if (foldoutProgress <= 0f && !pending.isExpanded)
                {
                    y = backgroundRect.yMax;
                    return;
                }

                float innerX = containerX + resolvedSectionPadding;
                float innerWidth = containerWidth - resolvedSectionPadding * 2f;
                float innerY = headerRect.yMax + spacing;

                Color previousColor = GUI.color;
                if (!Mathf.Approximately(foldoutProgress, 1f))
                {
                    GUI.color = new Color(
                        previousColor.r,
                        previousColor.g,
                        previousColor.b,
                        previousColor.a * Mathf.Clamp01(foldoutProgress)
                    );
                }

                bool valueSupportsFoldout = ManualEntryValueSupportsFoldout(pending);
                Rect valueRect = new(innerX, innerY, innerWidth, rowHeight);
                if (valueSupportsFoldout)
                {
                    valueRect.x += ManualEntryExpandableValueFoldoutGutter;
                    valueRect.width = Mathf.Max(
                        0f,
                        valueRect.width - ManualEntryExpandableValueFoldoutGutter
                    );
                }
                float valueLeftShift = ManualEntryValueContentLeftShift;
                if (valueSupportsFoldout)
                {
                    valueLeftShift = Mathf.Max(
                        0f,
                        valueLeftShift - ManualEntryFoldoutValueLeftShiftReduction
                    );
                }
                if (valueLeftShift > 0f)
                {
                    float shift = Mathf.Min(valueLeftShift, Mathf.Max(0f, valueRect.x - innerX));
                    valueRect.x -= shift;
                    valueRect.width += shift;
                }
                if (valueSupportsFoldout && ManualEntryFoldoutValueRightShift > 0f)
                {
                    float shift = Mathf.Min(ManualEntryFoldoutValueRightShift, valueRect.width);
                    valueRect.x += shift;
                    valueRect.width = Mathf.Max(0f, valueRect.width - shift);
                }

                HasLastManualEntryValueRect = true;
                LastManualEntryValueRect = valueRect;
                LastManualEntryValueUsedFoldoutLabel = valueSupportsFoldout;
                LastManualEntryValueFoldoutOffset = valueSupportsFoldout
                    ? ManualEntryExpandableValueFoldoutGutter
                    : 0f;

                object previousValue = pending.value;
                object updatedValue = DrawFieldForType(
                    valueRect,
                    ManualEntryValueContent,
                    pending.value,
                    elementType,
                    pending
                );
                object normalizedValue = CloneComplexValue(updatedValue, elementType);
                bool valueChanged = !ValuesEqual(previousValue, normalizedValue);
                if (valueChanged)
                {
                    pending.value = normalizedValue;
                    pending.errorMessage = null;
                    pending.valueWrapperDirty = true;
                }
                else
                {
                    pending.value = normalizedValue;
                }
                innerY += rowHeight + spacing;
                if (
                    valueSupportsFoldout
                    && pending.valueWrapperProperty != null
                    && !pending.valueWrapperProperty.isExpanded
                )
                {
                    pending.valueWrapperProperty.isExpanded = true;
                }

                bool inspectorAvailable = inspector != null;
                bool typeSupported = elementType != null && IsTypeSupported(elementType);
                bool elementAllowsNull = ElementTypeSupportsNull(elementType);
                bool valueProvided = pending.value != null || elementAllowsNull;
                bool duplicateExists = false;
                if (inspectorAvailable && typeSupported && valueProvided)
                {
                    object candidate = ConvertSnapshotValue(elementType, pending.value);
                    duplicateExists = inspector.ContainsElement(candidate);
                }

                // Determine if the value is valid or represents a "danger" state (null object, empty string)
                string pendingValueString = pending.value as string;
                bool valueValid = ValueIsValid(elementType, pending.value);
                bool isBlankStringValue = IsBlankStringValue(elementType, pending.value);
                bool isNullObjectValue = IsNullUnityObjectValue(elementType, pending.value);
                bool isDangerValue = isBlankStringValue || isNullObjectValue;
                bool canCommit =
                    inspectorAvailable
                    && typeSupported
                    && (valueValid || isDangerValue)
                    && !duplicateExists;

                Rect addRect = new(innerX, innerY, ManualEntryButtonWidth, rowHeight);
                Rect resetRect = new(
                    addRect.xMax + spacing,
                    innerY,
                    ManualEntryResetWidth,
                    rowHeight
                );
                float infoX = resetRect.xMax + spacing;
                float infoWidth = Mathf.Max(0f, innerX + innerWidth - infoX);

                string infoMessage = GetManualEntryInfoMessage(
                    inspectorAvailable,
                    typeSupported,
                    valueValid,
                    isDangerValue,
                    isBlankStringValue,
                    isNullObjectValue,
                    pendingValueString,
                    duplicateExists,
                    elementType
                );

                bool addEnabled = canCommit;
                using (new EditorGUI.DisabledScope(!addEnabled))
                {
                    // Use "AddEmpty" style for danger values (empty strings, null objects) to show warning
                    string styleKey =
                        isDangerValue && addEnabled ? "AddEmpty"
                        : duplicateExists ? "Overwrite"
                        : "Add";
                    GUIStyle addStyle = SolidButtonStyles.GetSolidButtonStyle(styleKey, addEnabled);
                    if (
                        GUI.Button(addRect, ManualEntryAddContent, addStyle)
                        && TryCommitPendingEntry(
                            pending,
                            property,
                            propertyPath,
                            ref itemsProperty,
                            pagination,
                            inspector
                        )
                    )
                    {
                        duplicateExists = false;
                        GUI.FocusControl(null);
                    }
                }

                object defaultElementValue =
                    elementType != null
                        ? SerializableDictionaryPropertyDrawer.GetDefaultValue(elementType)
                        : null;
                bool resetEnabled =
                    elementType != null && !ValuesEqual(pending.value, defaultElementValue);
                bool parentGuiEnabled = GUI.enabled;
                using (new EditorGUI.DisabledScope(!resetEnabled))
                {
                    bool styleEnabled = resetEnabled && parentGuiEnabled;
                    GUIStyle resetStyle = SolidButtonStyles.GetSolidButtonStyle(
                        "Reset",
                        styleEnabled
                    );
                    if (GUI.Button(resetRect, ManualEntryResetContent, resetStyle))
                    {
                        ResetPendingEntry(pending, collapseFoldout: false);
                        SyncPendingWrapperValue(pending);
                        GUI.FocusControl(null);
                    }
                }

                if (infoWidth > 0f && !string.IsNullOrEmpty(infoMessage))
                {
                    Rect infoRect = new(infoX, innerY, infoWidth, rowHeight);
                    GUI.Label(infoRect, infoMessage, EditorStyles.miniLabel);
                }

                innerY += rowHeight + spacing;

                if (!string.IsNullOrEmpty(pending.errorMessage))
                {
                    float warningHeight = GetWarningBarHeight();
                    Rect warningRect = new(innerX, innerY, innerWidth, warningHeight);
                    EditorGUI.HelpBox(warningRect, pending.errorMessage, MessageType.Warning);
                }

                GUI.color = previousColor;
                y = backgroundRect.yMax;
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        private static string GetManualEntryInfoMessage(
            bool inspectorAvailable,
            bool typeSupported,
            bool valueValid,
            bool isDangerValue,
            bool isBlankStringValue,
            bool isNullObjectValue,
            string pendingValueString,
            bool duplicateExists,
            Type elementType
        )
        {
            if (!inspectorAvailable)
            {
                return "Set inspector unavailable.";
            }

            if (!typeSupported)
            {
                if (elementType == null)
                {
                    return "Unsupported element type.";
                }

                using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                    32 + elementType.Name.Length,
                    out StringBuilder builder
                );
                builder.Clear();
                builder.Append("Unsupported type (");
                builder.Append(elementType.Name);
                builder.Append(").");
                return builder.ToString();
            }

            if (!valueValid && !isDangerValue)
            {
                return "Value required.";
            }

            if (duplicateExists)
            {
                return "Value already exists.";
            }

            // Show warning messages for danger values
            if (isBlankStringValue)
            {
                string descriptor = string.IsNullOrEmpty(pendingValueString)
                    ? "empty"
                    : "whitespace-only";
                return $"Adding {descriptor} string value.";
            }

            if (isNullObjectValue)
            {
                return "Adding null object reference.";
            }

            return string.Empty;
        }

        private static float ResolveManualEntryFoldoutToggleOffset(SerializedProperty property)
        {
            SerializedObject serializedObject = property?.serializedObject;
            return TargetsUnityHelpersSettings(serializedObject)
                ? ManualEntryFoldoutToggleOffsetSettings
                : ManualEntryFoldoutToggleOffsetInspector;
        }

        private static float ResolveManualEntrySectionPadding(SerializedProperty property)
        {
            SerializedObject serializedObject = property?.serializedObject;
            return TargetsUnityHelpersSettings(serializedObject)
                ? ManualEntrySectionPaddingSettings
                : ManualEntrySectionPadding;
        }

        private static GUIStyle GetManualEntryFoldoutLabelStyle()
        {
            if (_manualEntryFoldoutLabelStyle != null)
            {
                return _manualEntryFoldoutLabelStyle;
            }

            _manualEntryFoldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
            };
            return _manualEntryFoldoutLabelStyle;
        }

        private static bool ManualEntryValueSupportsFoldout(PendingEntry pending)
        {
            if (pending == null || pending.elementType == null)
            {
                return false;
            }

            // Trust the type-based check when the type is known to support complex editing,
            // because Unity's hasVisibleChildren may temporarily return false for newly created
            // array elements before the serialization system fully processes the type structure.
            Type elementType = pending.elementType;
            bool typeSupportsComplex =
                TypeSupportsComplexEditing(elementType)
                && !typeof(Object).IsAssignableFrom(elementType);

            SerializedProperty property = pending.valueWrapperProperty;
            if (property != null)
            {
                return typeSupportsComplex || SerializedPropertySupportsFoldout(property);
            }

            PendingWrapperContext context = EnsurePendingWrapper(pending, elementType);
            property = context.Property;

            return typeSupportsComplex || SerializedPropertySupportsFoldout(property);
        }

        private static bool TargetsUnityHelpersSettings(SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                return false;
            }

            if (serializedObject.targetObject is UnityHelpersSettings)
            {
                return true;
            }

            Object[] targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                return false;
            }

            foreach (Object target in targets)
            {
                if (target is UnityHelpersSettings)
                {
                    return true;
                }
            }

            return false;
        }

        internal PendingEntry GetOrCreatePendingEntry(
            SerializedProperty property,
            string propertyPath,
            Type elementType,
            bool isSortedSet
        )
        {
            string pendingKey = GetPropertyCacheKey(property);
            PendingEntry entry = _pendingEntries.GetOrAdd(pendingKey);
            entry.isSorted = isSortedSet;

            if (entry.elementType != elementType)
            {
                entry.elementType = elementType;
                ReleasePendingWrapper(entry);
                entry.valueWrapperDirty = true;
                if (elementType != null)
                {
                    entry.value = CloneComplexValue(
                        SerializableDictionaryPropertyDrawer.GetDefaultValue(elementType),
                        elementType
                    );
                }
                else
                {
                    entry.value = null;
                }

                entry.errorMessage = null;
                entry.isExpanded = false;
            }
            else if (entry.value == null && elementType != null)
            {
                entry.value = CloneComplexValue(
                    SerializableDictionaryPropertyDrawer.GetDefaultValue(elementType),
                    elementType
                );
                entry.valueWrapperDirty = true;
            }

            entry.valueWrapperDirty = true;
            SyncPendingWrapperValue(entry);
            return entry;
        }

        private static void ResetPendingEntry(PendingEntry pending, bool collapseFoldout = true)
        {
            if (pending == null)
            {
                return;
            }

            if (pending.elementType != null)
            {
                pending.value = CloneComplexValue(
                    SerializableDictionaryPropertyDrawer.GetDefaultValue(pending.elementType),
                    pending.elementType
                );
            }
            else
            {
                pending.value = null;
            }

            pending.errorMessage = null;
            if (collapseFoldout)
            {
                pending.isExpanded = false;
            }
            SyncPendingWrapperValue(pending);
            pending.valueWrapperDirty = true;
        }

        private static void SyncPendingWrapperValue(PendingEntry pending)
        {
            if (
                pending?.valueWrapper == null
                || pending.valueWrapperSerialized == null
                || pending.valueWrapperProperty == null
            )
            {
                return;
            }

            object currentValue = pending.valueWrapper.GetValue();
            if (!pending.valueWrapperDirty && ValuesEqual(currentValue, pending.value))
            {
                return;
            }

            pending.valueWrapper.SetValue(CloneComplexValue(pending.value, pending.elementType));
            pending.valueWrapperSerialized.Update();
            pending.valueWrapperSerialized.ApplyModifiedPropertiesWithoutUndo();
            pending.valueWrapperDirty = false;
        }

        private static PendingWrapperContext EnsurePendingWrapper(
            PendingEntry pending,
            Type elementType
        )
        {
            if (pending == null || elementType == null)
            {
                return PendingWrapperContext.Empty;
            }

            PendingValueWrapper wrapper = pending.valueWrapper;
            SerializedObject serialized = pending.valueWrapperSerialized;
            SerializedProperty property = pending.valueWrapperProperty;

            if (wrapper == null)
            {
                wrapper = ScriptableObject.CreateInstance<PendingValueWrapper>();
                wrapper.hideFlags = HideFlags.HideAndDontSave;
                serialized = null;
                property = null;
            }

            if (serialized == null)
            {
                serialized = new SerializedObject(wrapper);
                property = wrapper.FindValueProperty(serialized);
            }

            if (property == null)
            {
                ReleasePendingWrapper(pending);
                return PendingWrapperContext.Empty;
            }

            pending.valueWrapper = wrapper;
            pending.valueWrapperSerialized = serialized;
            pending.valueWrapperProperty = property;
            serialized.Update();
            return new PendingWrapperContext(wrapper, serialized, property);
        }

        private static void ReleasePendingWrapper(PendingEntry pending)
        {
            if (pending == null)
            {
                return;
            }

            if (pending.valueWrapper != null)
            {
                Object.DestroyImmediate(pending.valueWrapper);
            }

            pending.valueWrapper = null;
            pending.valueWrapperSerialized = null;
            pending.valueWrapperProperty = null;
            pending.valueWrapperDirty = true;
        }

        internal bool TryCommitPendingEntry(
            PendingEntry pending,
            SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination,
            ISerializableSetInspector inspector
        )
        {
            SerializedObject serializedObject = property.serializedObject;
            int itemsSizeBefore = itemsProperty is { isArray: true } ? itemsProperty.arraySize : 0;

            PaletteSerializationDiagnostics.ReportSetCommitStart(
                serializedObject,
                propertyPath,
                itemsSizeBefore,
                pending?.value
            );

            if (pending == null)
            {
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    "pending is null"
                );
                return false;
            }

            Type elementType = pending.elementType;
            if (elementType == null)
            {
                pending.errorMessage = "Unknown element type.";
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    pending.errorMessage
                );
                return false;
            }

            if (inspector == null)
            {
                pending.errorMessage = "Set inspector unavailable.";
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    pending.errorMessage
                );
                return false;
            }

            if (!IsTypeSupported(elementType))
            {
                pending.errorMessage = $"Unsupported type ({elementType.Name}).";
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    pending.errorMessage
                );
                return false;
            }

            if (pending.value == null && !ElementTypeSupportsNull(elementType))
            {
                pending.errorMessage = "Value cannot be null.";
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    pending.errorMessage
                );
                return false;
            }

            object normalizedCandidate = ConvertSnapshotValue(elementType, pending.value);
            if (inspector.ContainsElement(normalizedCandidate))
            {
                pending.errorMessage = "Value already exists in this set.";
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    pending.errorMessage
                );
                return false;
            }

            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Add Set Entry");
            }

            if (!inspector.TryAddElement(normalizedCandidate, out object normalizedValue))
            {
                pending.errorMessage = "Unable to add value to set.";
                PaletteSerializationDiagnostics.ReportSetAddResult(
                    serializedObject,
                    propertyPath,
                    false,
                    pending.errorMessage
                );
                return false;
            }

            Array snapshot = BuildSnapshotArray(itemsProperty, elementType);
            int originalLength = snapshot?.Length ?? 0;
            Array updated = Array.CreateInstance(elementType, originalLength + 1);
            if (snapshot != null && originalLength > 0)
            {
                snapshot.CopyTo(updated, 0);
            }
            updated.SetValue(normalizedValue, originalLength);

            inspector.SetSerializedItemsSnapshot(updated, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            Undo.FlushUndoRecordObjects();

            serializedObject.Update();
            property = serializedObject.FindProperty(propertyPath);
            itemsProperty = property?.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            int totalCount = itemsProperty is { isArray: true } ? itemsProperty.arraySize : 0;

            EnsurePaginationBounds(pagination, totalCount);
            EvaluateDuplicateState(property, itemsProperty, force: true);
            EvaluateNullEntryState(property, itemsProperty);
            SyncRuntimeSet(property);
            if (totalCount > 0)
            {
                pagination.selectedIndex = totalCount - 1;
            }

            MarkListCacheDirty(GetPropertyCacheKey(property));
            pending.errorMessage = null;
            bool wasExpanded = pending.isExpanded;
            ResetPendingEntry(pending);
            pending.isExpanded = wasExpanded;
            RequestRepaint();

            PaletteSerializationDiagnostics.ReportSetAddResult(
                serializedObject,
                propertyPath,
                true,
                null
            );
            PaletteSerializationDiagnostics.ReportSetCommitComplete(
                serializedObject,
                propertyPath,
                totalCount
            );

            return true;
        }

        internal static bool IsTweeningEnabledForTests(bool isSortedSet)
        {
            return ShouldTweenManualEntryFoldout(isSortedSet);
        }

        /// <summary>
        /// Returns the expected static foldout progress without animation.
        /// This static method cannot access instance animation state.
        /// Use <see cref="GetPendingFoldoutProgressFromInstance"/> for actual animation testing.
        /// </summary>
        internal static float GetPendingFoldoutProgressForTests(
            SerializedProperty property,
            bool expanded,
            bool isSorted
        )
        {
            // Static method - returns immediate value since we can't access instance state
            return expanded ? 1f : 0f;
        }

        /// <summary>
        /// Gets the actual animated foldout progress from the drawer instance's pending entry.
        /// Use this for testing that animations are actually progressing over time.
        /// </summary>
        /// <param name="property">The serialized property for the set.</param>
        /// <returns>
        /// The current animation progress (0 to 1), or -1 if no pending entry exists for this property.
        /// When tweening is disabled, returns 0 or 1 immediately based on expanded state.
        /// </returns>
        internal float GetPendingFoldoutProgressFromInstance(SerializedProperty property)
        {
            if (property == null)
            {
                return -1f;
            }

            string cacheKey = GetPropertyCacheKey(property);
            if (!_pendingEntries.TryGetValue(cacheKey, out PendingEntry pending) || pending == null)
            {
                return -1f;
            }

            return GetPendingFoldoutProgress(pending);
        }

        /// <summary>
        /// Gets the pending entry's expanded state and animation information for testing.
        /// </summary>
        /// <param name="property">The serialized property for the set.</param>
        /// <param name="isExpanded">Output: whether the pending section is logically expanded.</param>
        /// <param name="animProgress">Output: the current animation progress (0 to 1).</param>
        /// <param name="hasAnimBool">Output: whether an AnimBool is active for this entry.</param>
        /// <returns>True if a pending entry was found, false otherwise.</returns>
        internal bool TryGetPendingAnimationStateForTests(
            SerializedProperty property,
            out bool isExpanded,
            out float animProgress,
            out bool hasAnimBool
        )
        {
            isExpanded = false;
            animProgress = 0f;
            hasAnimBool = false;

            if (property == null)
            {
                return false;
            }

            string cacheKey = GetPropertyCacheKey(property);
            if (!_pendingEntries.TryGetValue(cacheKey, out PendingEntry pending) || pending == null)
            {
                return false;
            }

            isExpanded = pending.isExpanded;
            hasAnimBool = pending.foldoutAnim != null;
            animProgress = GetPendingFoldoutProgress(pending);
            return true;
        }

        /// <summary>
        /// Sets the pending entry's expanded state for testing purposes.
        /// This properly triggers animation state updates.
        /// </summary>
        internal void SetPendingExpandedStateForTests(SerializedProperty property, bool expanded)
        {
            if (property == null)
            {
                return;
            }

            string cacheKey = GetPropertyCacheKey(property);
            if (!_pendingEntries.TryGetValue(cacheKey, out PendingEntry pending) || pending == null)
            {
                return;
            }

            pending.isExpanded = expanded;
            AnimBool anim = EnsureManualEntryFoldoutAnim(pending, property.propertyPath);
            if (anim != null)
            {
                anim.target = expanded;
            }
        }

        private static bool ShouldTweenMainFoldout(bool isSortedSet)
        {
            return isSortedSet
                ? UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts()
                : UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
        }

        private static float GetMainFoldoutAnimationSpeed(bool isSortedSet)
        {
            return isSortedSet
                ? UnityHelpersSettings.GetSerializableSortedSetFoldoutSpeed()
                : UnityHelpersSettings.GetSerializableSetFoldoutSpeed();
        }

        private static AnimBool EnsureMainFoldoutAnim(
            SerializedObject serializedObject,
            string propertyPath,
            bool isExpanded,
            bool isSortedSet
        )
        {
            bool shouldTween = ShouldTweenMainFoldout(isSortedSet);
            float speed = GetMainFoldoutAnimationSpeed(isSortedSet);
            MainFoldoutCacheKey cacheKey = GetMainFoldoutCacheKey(serializedObject, propertyPath);

            SerializableCollectionTweenDiagnostics.LogTweenSettingsQuery(
                "EnsureMainFoldoutAnim",
                propertyPath ?? "(unknown)",
                isSortedSet,
                shouldTween,
                speed
            );

            if (!shouldTween)
            {
                if (MainFoldoutAnimations.TryGetValue(cacheKey, out AnimBool existing))
                {
                    existing.valueChanged.RemoveListener(RequestRepaint);
                    MainFoldoutAnimations.Remove(cacheKey);

                    SerializableCollectionTweenDiagnostics.LogAnimBoolDestroyed(
                        propertyPath,
                        "MainFoldout_TweeningDisabled"
                    );
                }

                return null;
            }

            if (!MainFoldoutAnimations.TryGetValue(cacheKey, out AnimBool anim) || anim == null)
            {
                anim = new AnimBool(isExpanded) { speed = speed };
                anim.valueChanged.AddListener(RequestRepaint);
                MainFoldoutAnimations[cacheKey] = anim;

                SerializableCollectionTweenDiagnostics.LogAnimBoolCreation(
                    propertyPath,
                    isExpanded,
                    isSortedSet,
                    speed
                );
            }
            else
            {
                anim.speed = speed;
            }

            anim.target = isExpanded;
            return anim;
        }

        private static float GetMainFoldoutProgress(
            SerializedObject serializedObject,
            string propertyPath,
            bool isExpanded,
            bool isSortedSet
        )
        {
            bool shouldTween = ShouldTweenMainFoldout(isSortedSet);
            if (!shouldTween)
            {
                float immediateProgress = isExpanded ? 1f : 0f;

                SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                    "GetMainFoldoutProgress_NoTween",
                    propertyPath ?? "(unknown)",
                    false,
                    isExpanded,
                    immediateProgress,
                    false
                );

                return immediateProgress;
            }

            AnimBool anim = EnsureMainFoldoutAnim(
                serializedObject,
                propertyPath,
                isExpanded,
                isSortedSet
            );
            if (anim == null)
            {
                float fallbackProgress = isExpanded ? 1f : 0f;

                SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                    "GetMainFoldoutProgress_NoAnimBool",
                    propertyPath ?? "(unknown)",
                    true,
                    isExpanded,
                    fallbackProgress,
                    false
                );

                return fallbackProgress;
            }

            if (anim.isAnimating)
            {
                RequestRepaint();
            }

            float animatedProgress = anim.faded;

            SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                "GetMainFoldoutProgress_Animated",
                propertyPath ?? "(unknown)",
                true,
                isExpanded,
                animatedProgress,
                true
            );

            return animatedProgress;
        }

        /// <summary>
        /// Clears the main foldout animation cache. Used for testing purposes.
        /// </summary>
        internal static void ClearMainFoldoutAnimCacheForTests()
        {
            foreach (KeyValuePair<MainFoldoutCacheKey, AnimBool> kvp in MainFoldoutAnimations)
            {
                kvp.Value?.valueChanged.RemoveListener(RequestRepaint);
            }
            MainFoldoutAnimations.Clear();
        }

        /// <summary>
        /// Returns true if a main foldout AnimBool exists for the given property path and target object.
        /// </summary>
        internal static bool HasMainFoldoutAnimBoolForTests(
            SerializedObject serializedObject,
            string propertyPath
        )
        {
            MainFoldoutCacheKey cacheKey = GetMainFoldoutCacheKey(serializedObject, propertyPath);
            return MainFoldoutAnimations.ContainsKey(cacheKey);
        }

        /// <summary>
        /// Gets the main foldout progress for testing purposes.
        /// </summary>
        internal static float GetMainFoldoutProgressForTests(
            SerializedObject serializedObject,
            string propertyPath,
            bool isExpanded,
            bool isSortedSet
        )
        {
            return GetMainFoldoutProgress(serializedObject, propertyPath, isExpanded, isSortedSet);
        }

        /// <summary>
        /// Gets the main foldout cache key for testing purposes.
        /// Returns the string representation of the struct key.
        /// </summary>
        internal static string GetMainFoldoutCacheKeyForTests(
            SerializedObject serializedObject,
            string propertyPath
        )
        {
            return GetMainFoldoutCacheKey(serializedObject, propertyPath).ToString();
        }

        private static bool ShouldTweenManualEntryFoldout(bool isSortedSet)
        {
            return isSortedSet
                ? UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts()
                : UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
        }

        private static float GetManualEntryFoldoutSpeed(bool isSortedSet)
        {
            return isSortedSet
                ? UnityHelpersSettings.GetSerializableSortedSetFoldoutSpeed()
                : UnityHelpersSettings.GetSerializableSetFoldoutSpeed();
        }

        private static AnimBool CreateManualEntryFoldoutAnim(
            bool initialValue,
            bool isSortedSet,
            string propertyPath = null
        )
        {
            float speed = GetManualEntryFoldoutSpeed(isSortedSet);
            AnimBool anim = new(initialValue) { speed = speed };
            anim.valueChanged.AddListener(RequestRepaint);

            SerializableCollectionTweenDiagnostics.LogAnimBoolCreation(
                propertyPath ?? "(unknown)",
                initialValue,
                isSortedSet,
                speed
            );

            return anim;
        }

        private static AnimBool EnsureManualEntryFoldoutAnim(
            PendingEntry pending,
            string propertyPath = null
        )
        {
            if (pending == null)
            {
                return null;
            }

            bool shouldTween = ShouldTweenManualEntryFoldout(pending.isSorted);
            float speed = GetManualEntryFoldoutSpeed(pending.isSorted);

            SerializableCollectionTweenDiagnostics.LogTweenSettingsQuery(
                "EnsureManualEntryFoldoutAnim",
                propertyPath ?? "(unknown)",
                pending.isSorted,
                shouldTween,
                speed
            );

            if (!shouldTween)
            {
                if (pending.foldoutAnim != null)
                {
                    pending.foldoutAnim.valueChanged.RemoveListener(RequestRepaint);
                    pending.foldoutAnim = null;

                    SerializableCollectionTweenDiagnostics.LogAnimBoolDestroyed(
                        propertyPath ?? "(unknown)",
                        "TweeningDisabled"
                    );
                }

                return null;
            }

            if (pending.foldoutAnim == null)
            {
                pending.foldoutAnim = CreateManualEntryFoldoutAnim(
                    pending.isExpanded,
                    pending.isSorted,
                    propertyPath
                );
            }
            else
            {
                pending.foldoutAnim.speed = speed;
                // Sync AnimBool target with pending.isExpanded if they're out of sync.
                // This starts the animation toward the new state when isExpanded changes
                // (e.g., via foldout click or programmatic assignment).
                if (pending.foldoutAnim.target != pending.isExpanded)
                {
                    pending.foldoutAnim.target = pending.isExpanded;
                }
            }

            return pending.foldoutAnim;
        }

        private static float GetPendingFoldoutProgress(
            PendingEntry pending,
            string propertyPath = null
        )
        {
            if (pending == null)
            {
                return 0f;
            }

            bool shouldTween = ShouldTweenManualEntryFoldout(pending.isSorted);

            // Always call EnsureManualEntryFoldoutAnim to properly clean up the AnimBool when
            // tweening is disabled. This ensures the foldoutAnim is set to null when shouldTween
            // is false, which is important for consistent state management.
            AnimBool anim = EnsureManualEntryFoldoutAnim(pending, propertyPath);

            if (!shouldTween)
            {
                float immediateProgress = pending.isExpanded ? 1f : 0f;

                SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                    "GetPendingFoldoutProgress_NoTween",
                    propertyPath ?? "(unknown)",
                    false,
                    pending.isExpanded,
                    immediateProgress,
                    pending.foldoutAnim != null
                );

                return immediateProgress;
            }

            // anim should not be null here since shouldTween is true, but handle defensively
            if (anim == null)
            {
                float fallbackProgress = pending.isExpanded ? 1f : 0f;

                SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                    "GetPendingFoldoutProgress_NoAnimBool",
                    propertyPath ?? "(unknown)",
                    true,
                    pending.isExpanded,
                    fallbackProgress,
                    false
                );

                return fallbackProgress;
            }

            anim.target = pending.isExpanded;
            float animatedProgress = anim.faded;

            SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                "GetPendingFoldoutProgress_Animated",
                propertyPath ?? "(unknown)",
                true,
                pending.isExpanded,
                animatedProgress,
                true
            );

            return animatedProgress;
        }

        private static float GetPendingSectionHeight(PendingEntry pending)
        {
            return GetPendingSectionHeight(pending, null);
        }

        private static float GetPendingSectionHeight(
            PendingEntry pending,
            SerializedProperty property
        )
        {
            if (pending == null)
            {
                return 0f;
            }

            float resolvedSectionPadding = ResolveManualEntrySectionPadding(property);
            float collapsedHeight = EditorGUIUtility.singleLineHeight + resolvedSectionPadding * 2f;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float expandedExtra = EditorGUIUtility.singleLineHeight * 2f + spacing * 3f;

            if (!string.IsNullOrEmpty(pending.errorMessage))
            {
                expandedExtra += GetWarningBarHeight() + spacing;
            }

            string propertyPath = property?.propertyPath;
            float progress = GetPendingFoldoutProgress(pending, propertyPath);
            float finalHeight = collapsedHeight + expandedExtra * Mathf.Clamp01(progress);

            SerializableCollectionTweenDiagnostics.LogPendingSectionHeightCalc(
                propertyPath ?? "(unknown)",
                collapsedHeight,
                expandedExtra,
                progress,
                finalHeight
            );

            return finalHeight;
        }

        private static void RequestRepaint()
        {
            // Always repaint all views to ensure animations work correctly
            // in both Inspector and SettingsProvider contexts
            InternalEditorUtility.RepaintAllViews();
        }

        private static GUIContent GetUnsupportedTypeContent(Type type)
        {
            string typeName = type?.Name ?? "Unknown";
            if (type == null || !UnsupportedTypeMessageCache.TryGetValue(type, out string message))
            {
                message = "Unsupported type (" + typeName + ")";
                if (type != null)
                {
                    UnsupportedTypeMessageCache[type] = message;
                }
            }
            UnsupportedTypeContent.text = message;
            return UnsupportedTypeContent;
        }

        internal static object DrawFieldForType(
            Rect rect,
            GUIContent content,
            object current,
            Type type,
            PendingEntry pending
        )
        {
            if (TryDrawComplexTypeField(rect, content, ref current, type, pending))
            {
                return current;
            }

            if (!IsTypeSupported(type))
            {
                EditorGUI.LabelField(rect, content, GetUnsupportedTypeContent(type));
                return current;
            }

            if (type == typeof(string))
            {
                return EditorGUI.TextField(rect, content, current as string ?? string.Empty);
            }

            if (type == typeof(int))
            {
                return EditorGUI.IntField(rect, content, current is int i ? i : default);
            }

            if (type == typeof(float))
            {
                return EditorGUI.FloatField(rect, content, current is float f ? f : default);
            }

            if (type == typeof(double))
            {
                return EditorGUI.DoubleField(rect, content, current is double d ? d : default);
            }

            if (type == typeof(long))
            {
                return EditorGUI.LongField(rect, content, current is long l ? l : default);
            }

            if (type == typeof(bool))
            {
                return EditorGUI.Toggle(rect, content, current is true);
            }

            if (type == typeof(Vector2))
            {
                return EditorGUI.Vector2Field(
                    rect,
                    content.text,
                    current is Vector2 v2 ? v2 : Vector2.zero
                );
            }

            if (type == typeof(Vector3))
            {
                return EditorGUI.Vector3Field(
                    rect,
                    content.text,
                    current is Vector3 v3 ? v3 : Vector3.zero
                );
            }

            if (type == typeof(Vector4))
            {
                return EditorGUI.Vector4Field(
                    rect,
                    content.text,
                    current is Vector4 v4 ? v4 : Vector4.zero
                );
            }

            if (type == typeof(Vector2Int))
            {
                Vector2Int value = current is Vector2Int v2int ? v2int : default;
                return EditorGUI.Vector2IntField(rect, content.text, value);
            }

            if (type == typeof(Vector3Int))
            {
                Vector3Int value = current is Vector3Int v3int ? v3int : default;
                return EditorGUI.Vector3IntField(rect, content.text, value);
            }

            if (type == typeof(Rect))
            {
                Rect value = current is Rect rectValue ? rectValue : default;
                return EditorGUI.RectField(rect, content.text, value);
            }

            if (type == typeof(RectInt))
            {
                RectInt value = current is RectInt rectInt ? rectInt : default;
                return EditorGUI.RectIntField(rect, content.text, value);
            }

            if (type == typeof(Bounds))
            {
                Bounds value = current is Bounds bounds ? bounds : default;
                return EditorGUI.BoundsField(rect, content.text, value);
            }

            if (type == typeof(BoundsInt))
            {
                BoundsInt value = current is BoundsInt boundsInt ? boundsInt : default;
                return EditorGUI.BoundsIntField(rect, content.text, value);
            }

            if (type == typeof(Color))
            {
                Color value = current is Color color ? color : Color.clear;
                return EditorGUI.ColorField(rect, content.text, value);
            }

            if (type == typeof(AnimationCurve))
            {
                AnimationCurve value = current as AnimationCurve ?? new AnimationCurve();
                return EditorGUI.CurveField(rect, content.text, value);
            }

            if (type.IsEnum)
            {
                Enum enumValue = current as Enum ?? (Enum)Enum.ToObject(type, 0);
                return EditorGUI.EnumPopup(rect, content, enumValue);
            }

            if (typeof(Object).IsAssignableFrom(type))
            {
                Object obj = current as Object;
                return EditorGUI.ObjectField(rect, content, obj, type, allowSceneObjects: false);
            }

            EditorGUI.LabelField(rect, content, GetUnsupportedTypeContent(type));
            return current;
        }

        private static bool TryDrawComplexTypeField(
            Rect rect,
            GUIContent content,
            ref object current,
            Type type,
            PendingEntry pending
        )
        {
            if (
                pending == null
                || type == null
                || !TypeSupportsComplexEditing(type)
                || (type.IsValueType && !typeof(Object).IsAssignableFrom(type))
                || typeof(Object).IsAssignableFrom(type)
                || type == typeof(string)
            )
            {
                return false;
            }

            PendingWrapperContext context = EnsurePendingWrapper(pending, type);
            if (context.Property == null)
            {
                return false;
            }

            object targetValue =
                current
                ?? CloneComplexValue(
                    SerializableDictionaryPropertyDrawer.GetDefaultValue(type),
                    type
                );
            object wrapperValue = context.Wrapper.GetValue();
            if (!ValuesEqual(wrapperValue, targetValue))
            {
                context.Wrapper.SetValue(CloneComplexValue(targetValue, type));
                context.Serialized.Update();
                context.Serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (type.IsClass && context.Wrapper.GetValue() == null)
            {
                context.Wrapper.SetValue(
                    CloneComplexValue(
                        SerializableDictionaryPropertyDrawer.GetDefaultValue(type),
                        type
                    )
                );
                context.Serialized.Update();
                context.Serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, context.Property, content, includeChildren: true);
            if (EditorGUI.EndChangeCheck())
            {
                context.Serialized.ApplyModifiedProperties();
                context.Serialized.Update();
                object updated = context.Wrapper.GetValue();
                current = CloneComplexValue(updated, type);
            }

            return true;
        }

        private static bool TypeSupportsComplexEditing(Type type)
        {
            while (true)
            {
                if (type == null)
                {
                    return false;
                }

                if (typeof(Object).IsAssignableFrom(type))
                {
                    return true;
                }

                if (IsSimplePendingFieldType(type))
                {
                    return false;
                }

                if (type.IsArray)
                {
                    type = type.GetElementType();
                    continue;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                {
                    type = type.GetGenericArguments()[0];
                    continue;
                }

                return type.IsSerializable;
            }
        }

        private static bool IsSimplePendingFieldType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Vector2Int)
                || type == typeof(Vector3Int)
                || type == typeof(Rect)
                || type == typeof(RectInt)
                || type == typeof(Bounds)
                || type == typeof(BoundsInt)
                || type == typeof(Color)
                || type == typeof(AnimationCurve);
        }

        private static bool IsTypeSupported(Type type)
        {
            if (type == null)
            {
                return false;
            }

            return type == typeof(string)
                || type == typeof(int)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(long)
                || type == typeof(bool)
                || type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Vector2Int)
                || type == typeof(Vector3Int)
                || type == typeof(Rect)
                || type == typeof(RectInt)
                || type == typeof(Bounds)
                || type == typeof(BoundsInt)
                || type == typeof(Color)
                || type == typeof(AnimationCurve)
                || type.IsEnum
                || typeof(Object).IsAssignableFrom(type)
                || TypeSupportsComplexEditing(type);
        }

        private static bool TryCreateDefaultInstance(Type type, out object instance)
        {
            instance = null;
            if (type == null)
            {
                return false;
            }

            if (type.IsAbstract || type.IsInterface)
            {
                return false;
            }

            if (typeof(Object).IsAssignableFrom(type))
            {
                return false;
            }

            return TryInvokeParameterlessConstructor(type, out instance);
        }

        private static bool TryInvokeParameterlessConstructor(Type type, out object value)
        {
            value = null;
            if (!TryGetParameterlessFactory(type, out Func<object> factory))
            {
                return false;
            }

            value = factory();
            if (value != null)
            {
                return true;
            }

            ParameterlessFactoryCache.TryRemove(type, out _);
            UnsupportedParameterlessTypes[type] = 0;
            return false;
        }

        internal static bool PageEntriesNeedSorting(
            ListPageCache cache,
            SerializedProperty itemsProperty,
            bool allowSort
        )
        {
            if (cache?.entries is not { Count: > 1 } || !CanSortElements(itemsProperty, allowSort))
            {
                return false;
            }

            SetElementData previous = default;
            bool hasPrevious = false;

            foreach (PageEntry entry in cache.entries)
            {
                if (entry == null)
                {
                    continue;
                }

                int arrayIndex = entry.arrayIndex;
                if (arrayIndex < 0 || arrayIndex >= itemsProperty.arraySize)
                {
                    continue;
                }

                SerializedProperty elementProperty = itemsProperty.GetArrayElementAtIndex(
                    arrayIndex
                );
                SetElementData current = ReadElementData(elementProperty);

                if (hasPrevious)
                {
                    int comparison = CompareComparableValues(
                        previous.comparable,
                        current.comparable
                    );
                    if (comparison > 0)
                    {
                        return true;
                    }

                    if (comparison == 0)
                    {
                        string previousFallback =
                            previous.value != null ? previous.value.ToString() : string.Empty;
                        string currentFallback =
                            current.value != null ? current.value.ToString() : string.Empty;
                        if (string.CompareOrdinal(previousFallback, currentFallback) > 0)
                        {
                            return true;
                        }
                    }
                }

                previous = current;
                hasPrevious = true;
            }

            return false;
        }

        private static bool ShouldDeepClone(Type type)
        {
            return type != null
                && !type.IsValueType
                && type != typeof(string)
                && !typeof(Object).IsAssignableFrom(type);
        }

        internal static object CloneComplexValue(object source, Type type)
        {
            if (source == null)
            {
                if (type == null)
                {
                    return null;
                }

                if (type.IsValueType)
                {
                    return TryInvokeParameterlessConstructor(type, out object value) ? value : null;
                }

                return null;
            }

            if (
                type == null
                || type.IsValueType
                || type == typeof(string)
                || typeof(Object).IsAssignableFrom(type)
            )
            {
                return source;
            }

            if (!ShouldDeepClone(type))
            {
                return source;
            }

            if (!TryCreateDefaultInstance(type, out object clone))
            {
                return source;
            }

            try
            {
                string json = JsonUtility.ToJson(source);
                JsonUtility.FromJsonOverwrite(json, clone);
                return clone;
            }
            catch
            {
                return source;
            }
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left is Object leftObject && right is Object rightObject)
            {
                return leftObject == rightObject;
            }

            return left.Equals(right);
        }

        internal sealed class PendingValueWrapper : ScriptableObject
        {
            internal const string PropertyName = "boxedValue";

            [SerializeReference]
            private object boxedValue;

            public object GetValue()
            {
                return boxedValue;
            }

            public void SetValue(object incoming)
            {
                boxedValue = incoming;
            }

            public SerializedProperty FindValueProperty(SerializedObject serializedObject)
            {
                return serializedObject.FindProperty(PropertyName);
            }
        }

        private readonly struct PendingWrapperContext
        {
            public static readonly PendingWrapperContext Empty = new(null, null, null);

            public PendingWrapperContext(
                PendingValueWrapper wrapper,
                SerializedObject serialized,
                SerializedProperty property
            )
            {
                Wrapper = wrapper;
                Serialized = serialized;
                Property = property;
            }

            public PendingValueWrapper Wrapper { get; }

            public SerializedObject Serialized { get; }

            public SerializedProperty Property { get; }
        }

        private static void SnapSelectionToPage(PaginationState pagination, int totalCount)
        {
            if (totalCount <= 0)
            {
                pagination.selectedIndex = -1;
                return;
            }

            int pageSize = Mathf.Max(1, pagination.pageSize);
            int pageStart = pagination.page * pageSize;
            if (pageStart >= totalCount)
            {
                pageStart = Mathf.Max(0, totalCount - 1);
            }

            pagination.selectedIndex = Mathf.Clamp(pageStart, 0, totalCount - 1);
        }

        internal static float EvaluateDuplicateShakeOffset(
            int arrayIndex,
            double startTime,
            double currentTime,
            int cycleLimit
        )
        {
            if (cycleLimit == 0)
            {
                return 0f;
            }

            if (currentTime < startTime)
            {
                startTime = currentTime;
            }

            double elapsed = currentTime - startTime;

            if (cycleLimit > 0)
            {
                double cycleDuration = (2d * Math.PI) / DuplicateShakeFrequency;
                double maxDuration = cycleDuration * cycleLimit;
                if (elapsed >= maxDuration)
                {
                    return 0f;
                }
            }

            float phase = (float)(elapsed * DuplicateShakeFrequency);
            float seed = arrayIndex * 0.35f;
            return Mathf.Sin(phase + seed) * DuplicateShakeAmplitude;
        }

        internal static Rect ExpandRowRectVertically(Rect rect)
        {
            rect.yMin -= 1f;
            rect.yMax += 1f;
            return rect;
        }

        /// <summary>
        /// Gets high-resolution time in seconds using <see cref="Stopwatch"/> for accurate animation timing.
        /// This is more reliable than <see cref="EditorApplication.timeSinceStartup"/> for animations
        /// that need consistent frame-to-frame timing.
        /// </summary>
        private static double GetHighResolutionTime()
        {
            return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }

        private static void DrawDuplicateOutline(Rect rect)
        {
            Rect top = new(rect.x, rect.y, rect.width, DuplicateOutlineThickness);
            Rect bottom = new(
                rect.x,
                rect.yMax - DuplicateOutlineThickness,
                rect.width,
                DuplicateOutlineThickness
            );
            Rect left = new(rect.x, rect.y, DuplicateOutlineThickness, rect.height);
            Rect right = new(
                rect.xMax - DuplicateOutlineThickness,
                rect.y,
                DuplicateOutlineThickness,
                rect.height
            );

            EditorGUI.DrawRect(top, DuplicateOutlineColor);
            EditorGUI.DrawRect(bottom, DuplicateOutlineColor);
            EditorGUI.DrawRect(left, DuplicateOutlineColor);
            EditorGUI.DrawRect(right, DuplicateOutlineColor);
        }

        private static float GetWarningBarHeight()
        {
            return EditorGUIUtility.singleLineHeight * 1.6f;
        }

        private static void DrawNullEntryTooltip(Rect rect, string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip) || Event.current.type != EventType.Repaint)
            {
                return;
            }

            NullEntryTooltipContent.text = string.Empty;
            NullEntryTooltipContent.image = null;
            NullEntryTooltipContent.tooltip = tooltip;
            GUI.Label(rect, NullEntryTooltipContent, GUIStyle.none);
        }

        private static bool ElementTypeSupportsNull(Type type)
        {
            return type != null && (!type.IsValueType || typeof(Object).IsAssignableFrom(type));
        }

        /// <summary>
        /// Determines whether a pending value is strictly valid for the given element type.
        /// Returns false for null Unity objects, null reference types, and empty strings.
        /// </summary>
        internal static bool ValueIsValid(Type elementType, object value)
        {
            if (elementType == null)
            {
                return false;
            }

            if (elementType == typeof(string))
            {
                return !string.IsNullOrEmpty(value as string);
            }

            if (typeof(Object).IsAssignableFrom(elementType))
            {
                return value is Object obj && obj != null;
            }

            return value != null || elementType.IsValueType;
        }

        /// <summary>
        /// Determines whether the pending value represents a blank (empty or whitespace-only) string.
        /// Used to show warning-style UI when adding empty/whitespace string values.
        /// </summary>
        internal static bool IsBlankStringValue(Type elementType, object value)
        {
            if (elementType != typeof(string))
            {
                return false;
            }

            string stringValue = value as string;
            return string.IsNullOrWhiteSpace(stringValue);
        }

        /// <summary>
        /// Determines whether the pending value represents a null Unity object reference.
        /// Used to show warning-style UI when adding null object references.
        /// </summary>
        internal static bool IsNullUnityObjectValue(Type elementType, object value)
        {
            if (elementType == null || !typeof(Object).IsAssignableFrom(elementType))
            {
                return false;
            }

            if (value == null)
            {
                return true;
            }

            return value is Object obj && obj == null;
        }

        private static bool ElementSupportsManualSorting(Type elementType)
        {
            if (elementType == null)
            {
                return false;
            }

            Type candidate = Nullable.GetUnderlyingType(elementType) ?? elementType;
            if (typeof(Object).IsAssignableFrom(candidate))
            {
                return true;
            }

            if (typeof(IComparable).IsAssignableFrom(candidate))
            {
                return true;
            }

            Type genericComparable = typeof(IComparable<>).MakeGenericType(candidate);
            return genericComparable.IsAssignableFrom(candidate);
        }

        internal static bool ShouldShowSortButton(
            bool isSortedSet,
            Type elementType,
            SerializedProperty itemsProperty
        )
        {
            bool allowSort = isSortedSet || ElementSupportsManualSorting(elementType);
            return NeedsSorting(itemsProperty, allowSort);
        }

        private static string BuildNullEntrySummary(List<int> indices)
        {
            if (indices == null || indices.Count == 0)
            {
                return string.Empty;
            }

            indices.Sort();

            if (indices.Count == 1)
            {
                return $"Null entry detected at index {indices[0]}. Value will be ignored at runtime.";
            }

            const int maxDisplay = 5;
            int displayCount = Math.Min(indices.Count, maxDisplay);

            using PooledResource<StringBuilder> builderLease = Buffers.GetStringBuilder(
                Math.Max(indices.Count * 6 + 64, 64),
                out StringBuilder builder
            );
            builder.Clear();
            builder.Append("Null entries detected at indices ");

            for (int i = 0; i < displayCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(indices[i]);
            }

            if (indices.Count > maxDisplay)
            {
                builder.Append(", ... (");
                builder.Append(indices.Count - maxDisplay);
                builder.Append(" more)");
            }

            builder.Append(". Values will be ignored at runtime.");
            return builder.ToString();
        }

        private static string BuildNullEntryTooltip(int index)
        {
            using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                64,
                out StringBuilder builder
            );
            builder.Clear();
            builder.Append("Null entry detected at index ");
            builder.Append(index);
            builder.Append(". Value will be ignored at runtime.");
            return builder.ToString();
        }

        internal static void RemoveEntry(SerializedProperty itemsProperty, int index)
        {
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return;
            }

            if (index < 0 || index >= itemsProperty.arraySize)
            {
                return;
            }

            itemsProperty.DeleteArrayElementAtIndex(index);

            if (
                index < itemsProperty.arraySize
                && itemsProperty.GetArrayElementAtIndex(index).propertyType
                    == SerializedPropertyType.ObjectReference
                && itemsProperty.GetArrayElementAtIndex(index).objectReferenceValue == null
            )
            {
                itemsProperty.DeleteArrayElementAtIndex(index);
            }
        }

        internal NullEntryState EvaluateNullEntryState(
            SerializedProperty property,
            SerializedProperty itemsProperty,
            bool force = false
        )
        {
            string key = GetPropertyCacheKey(property);
            NullEntryState state = _nullEntryStates.GetOrAdd(key);

            int currentFrame = Time.frameCount;
            bool alreadyRefreshedThisFrame = _lastNullEntryRefreshFrame == currentFrame;

            if (!force)
            {
                _lastNullEntryRefreshFrame = currentFrame;
                if (alreadyRefreshedThisFrame)
                {
                    return state;
                }
            }

            bool hasEvent = Event.current != null;
            EventType eventType = hasEvent ? Event.current.type : EventType.Repaint;
            if (!force && eventType != EventType.Repaint)
            {
                return state;
            }

            state.nullIndices.Clear();
            state.tooltips.Clear();
            state.summary = string.Empty;
            state.hasNullEntries = false;
            state.scratch.Clear();

            if (
                itemsProperty == null
                || !itemsProperty.isArray
                || itemsProperty.arraySize == 0
                || !TryGetSetInspector(
                    property,
                    property.propertyPath,
                    out ISerializableSetInspector inspector
                )
            )
            {
                return state;
            }

            Type elementType = inspector.ElementType;
            if (!ElementTypeSupportsNull(elementType))
            {
                return state;
            }

            int count = itemsProperty.arraySize;
            for (int index = 0; index < count; index++)
            {
                SerializedProperty element = itemsProperty.GetArrayElementAtIndex(index);
                SetElementData data = ReadElementData(element);
                if (ReferenceEquals(data.value, null))
                {
                    state.nullIndices.Add(index);
                    state.tooltips[index] = BuildNullEntryTooltip(index);
                    state.scratch.Add(index);
                }
            }

            if (state.nullIndices.Count > 0)
            {
                state.hasNullEntries = true;
                state.summary = BuildNullEntrySummary(state.scratch);
            }
            else
            {
                _nullEntryStates.Remove(key);
            }

            return state;
        }

        private static List<int> RentGroupingList(DuplicateState state)
        {
            PooledResource<List<int>> lease = Buffers<int>.List.Get(out List<int> list);
            state.groupingLeases[list] = lease;
            return list;
        }

        private static void ReleaseGroupingList(DuplicateState state, List<int> list)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            if (!state.groupingLeases.Remove(list, out PooledResource<List<int>> lease))
            {
                return;
            }

            lease.Dispose();
        }

        private static void ReleaseAllGroupingLists(DuplicateState state)
        {
            if (state.grouping.Count == 0)
            {
                if (state.groupingLeases.Count > 0)
                {
                    foreach (
                        KeyValuePair<
                            List<int>,
                            PooledResource<List<int>>
                        > leaseEntry in state.groupingLeases
                    )
                    {
                        leaseEntry.Key?.Clear();
                        leaseEntry.Value.Dispose();
                    }

                    state.groupingLeases.Clear();
                }

                return;
            }

            state.groupingKeysScratch.Clear();
            foreach (KeyValuePair<object, List<int>> bucket in state.grouping)
            {
                ReleaseGroupingList(state, bucket.Value);
                state.groupingKeysScratch.Add(bucket.Key);
            }

            for (int i = 0; i < state.groupingKeysScratch.Count; i++)
            {
                state.grouping.Remove(state.groupingKeysScratch[i]);
            }

            state.groupingKeysScratch.Clear();
        }

        internal DuplicateState EvaluateDuplicateState(
            SerializedProperty property,
            SerializedProperty itemsProperty,
            bool force = false
        )
        {
            string key = GetPropertyCacheKey(property);
            DuplicateState state = _duplicateStates.GetOrAdd(key);

            int currentFrame = Time.frameCount;
            bool alreadyRefreshedThisFrame = _lastDuplicateRefreshFrame == currentFrame;

            if (!force)
            {
                _lastDuplicateRefreshFrame = currentFrame;
                if (alreadyRefreshedThisFrame && !state.IsDirty)
                {
                    return state;
                }
            }

            ReleaseAllGroupingLists(state);

            bool hasEvent = Event.current != null;
            EventType eventType = hasEvent ? Event.current.type : EventType.Repaint;
            bool shouldRefresh = eventType == EventType.Repaint || state.IsDirty || force;

            if (!shouldRefresh)
            {
                return state;
            }

            state.duplicateIndices.Clear();
            state.primaryFlags.Clear();
            state.summary = string.Empty;
            state.hasDuplicates = false;

            if (itemsProperty == null || !itemsProperty.isArray)
            {
                state.ClearAnimationTracking();
                return state;
            }

            int currentArraySize = itemsProperty.arraySize;
            if (currentArraySize <= 1)
            {
                state.UpdateArraySize(currentArraySize);
                state.animationStartTimes.Clear();
                state.UpdateLastHadDuplicates(false);
                return state;
            }

            if (!force && state.ShouldSkipRefresh(currentArraySize))
            {
                state.UpdateArraySize(currentArraySize);
                return state;
            }

            state.UpdateArraySize(currentArraySize);

            int count = currentArraySize;
            using PooledResource<StringBuilder> summaryBuilderLease = Buffers.GetStringBuilder(
                Math.Max(count * 8, 64),
                out StringBuilder summaryBuilder
            );
            summaryBuilder.Clear();

            for (int index = 0; index < count; index++)
            {
                SerializedProperty element = itemsProperty.GetArrayElementAtIndex(index);
                SetElementData data = ReadElementData(element);
                object keyValue = data.comparable ?? NullComparable;

                if (!state.grouping.TryGetValue(keyValue, out List<int> list))
                {
                    list = RentGroupingList(state);
                    state.grouping[keyValue] = list;
                }

                list.Add(index);
            }

            UnityHelpersSettings.DuplicateRowAnimationMode animationMode =
                UnityHelpersSettings.GetDuplicateRowAnimationMode();
            bool animateDuplicates =
                animationMode == UnityHelpersSettings.DuplicateRowAnimationMode.Tween;
            double now = animateDuplicates ? GetHighResolutionTime() : 0d;

            int duplicateGroupCount = 0;
            state.groupingKeysScratch.Clear();
            state.groupingKeysScratch.AddRange(state.grouping.Keys);

            for (int keyIndex = 0; keyIndex < state.groupingKeysScratch.Count; keyIndex++)
            {
                object groupingKey = state.groupingKeysScratch[keyIndex];
                if (!state.grouping.TryGetValue(groupingKey, out List<int> indices))
                {
                    continue;
                }

                if (indices.Count <= 1)
                {
                    ReleaseGroupingList(state, indices);
                    state.grouping.Remove(groupingKey);
                    continue;
                }

                indices.Sort();
                duplicateGroupCount++;
                state.hasDuplicates = true;

                bool isPrimary = true;
                foreach (int duplicateIndex in indices)
                {
                    state.duplicateIndices.Add(duplicateIndex);
                    state.primaryFlags[duplicateIndex] = isPrimary;
                    isPrimary = false;

                    if (
                        animateDuplicates
                        && (force || !state.animationStartTimes.ContainsKey(duplicateIndex))
                    )
                    {
                        state.animationStartTimes[duplicateIndex] = now;
                    }
                }

                if (summaryBuilder.Length > 0)
                {
                    summaryBuilder.AppendLine();
                }

                if (duplicateGroupCount <= 5)
                {
                    summaryBuilder.Append("Duplicate entry ");
                    summaryBuilder.Append(ConvertDuplicateKeyToString(groupingKey));
                    summaryBuilder.Append(" at indices ");
                    AppendIndexList(summaryBuilder, indices);
                }

                ReleaseGroupingList(state, indices);
                state.grouping.Remove(groupingKey);
            }

            state.groupingKeysScratch.Clear();

            if (duplicateGroupCount > 5)
            {
                if (summaryBuilder.Length > 0)
                {
                    summaryBuilder.AppendLine();
                }

                summaryBuilder.Append("Additional duplicate groups omitted for brevity.");
            }

            if (state.animationStartTimes.Count > 0)
            {
                state.animationKeysScratch.Clear();
                state.animationKeysScratch.AddRange(state.animationStartTimes.Keys);
                foreach (int trackedIndex in state.animationKeysScratch)
                {
                    if (!state.duplicateIndices.Contains(trackedIndex) || !animateDuplicates)
                    {
                        state.animationStartTimes.Remove(trackedIndex);
                    }
                }
                state.animationKeysScratch.Clear();
            }

            if (!animateDuplicates)
            {
                state.animationStartTimes.Clear();
            }

            if (state.hasDuplicates)
            {
                state.summary =
                    summaryBuilder.Length > 0
                        ? summaryBuilder.ToString()
                        : "Duplicate values detected.";
            }
            else
            {
                state.summary = string.Empty;
            }

            state.UpdateLastHadDuplicates(state.hasDuplicates, forceReset: force);
            ReleaseAllGroupingLists(state);

            if (state.hasDuplicates && animateDuplicates)
            {
                int tweenCycleLimit =
                    UnityHelpersSettings.GetSerializableSetDuplicateTweenCycleLimit();
                double currentTime = GetHighResolutionTime();
                state.CheckAnimationCompletion(currentTime, tweenCycleLimit);

                if (state.IsAnimating)
                {
                    EditorWindow focusedWindow = EditorWindow.focusedWindow;
                    if (focusedWindow != null)
                    {
                        focusedWindow.Repaint();
                    }
                }
            }

            return state;
        }

        private static string ConvertDuplicateKeyToString(object key)
        {
            if (key == NullComparable || key == null)
            {
                return "null";
            }

            return key switch
            {
                Object obj => obj != null ? obj.name : "null object",
                _ => key.ToString(),
            };
        }

        private static void AppendIndexList(StringBuilder builder, List<int> indices)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(indices[i]);
            }
        }

        private static bool TryGetParameterlessFactory(Type type, out Func<object> factory)
        {
            factory = null;
            if (type == null)
            {
                return false;
            }

            if (ParameterlessFactoryCache.TryGetValue(type, out Func<object> cached))
            {
                factory = cached;
                return factory != null;
            }

            if (UnsupportedParameterlessTypes.ContainsKey(type))
            {
                return false;
            }

            Func<object> resolved = TryResolveFactory(type);
            if (resolved == null)
            {
                resolved = TryBuildFormatterFactory(type);
            }

            if (resolved != null)
            {
                ParameterlessFactoryCache[type] = resolved;
                factory = resolved;
                return true;
            }

            UnsupportedParameterlessTypes[type] = 0;
            return false;
        }

        private static Func<object> TryResolveFactory(Type type)
        {
            try
            {
                return ReflectionHelpers.GetParameterlessConstructor(type);
            }
            catch (ArgumentException)
            {
                ConstructorInfo ctor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    Type.EmptyTypes,
                    modifiers: null
                );
                if (ctor == null)
                {
                    return null;
                }

                return () =>
                {
                    try
                    {
                        return ctor.Invoke(null);
                    }
                    catch
                    {
                        return null;
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        private static Func<object> TryBuildFormatterFactory(Type type)
        {
            if (!type.IsSerializable)
            {
                return null;
            }

            return () =>
            {
                try
                {
                    return FormatterServices.GetUninitializedObject(type);
                }
                catch
                {
                    return null;
                }
            };
        }

        private bool IsSortedSetCached(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            string cacheKey = GetPropertyCacheKey(property);
            int currentFrame = Time.frameCount;

            if (_sortedSetCache.TryGetValue(cacheKey, out SortedSetCacheEntry entry))
            {
                if (entry.frameNumber == currentFrame)
                {
                    return entry.isSorted;
                }
            }
            else
            {
                entry = new SortedSetCacheEntry();
                _sortedSetCache[cacheKey] = entry;
            }

            entry.isSorted = IsSortedSet(property);
            entry.frameNumber = currentFrame;
            return entry.isSorted;
        }

        internal static bool IsSortedSet(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            string propertyTypeName = property.type ?? string.Empty;
            if (
                propertyTypeName.IndexOf("SerializableSortedSet", StringComparison.Ordinal) >= 0
                || propertyTypeName.IndexOf("SortedSet", StringComparison.Ordinal) >= 0
            )
            {
                return true;
            }

            Type fieldType = property.GetManagedType();
            if (TypeMatchesGenericDefinition(fieldType, typeof(SerializableSortedSet<>)))
            {
                return true;
            }

            Type declaredType = TryResolveDeclaredSetType(property);
            if (TypeMatchesGenericDefinition(declaredType, typeof(SerializableSortedSet<>)))
            {
                return true;
            }

            Type unityResolvedType = ResolveUnityPropertyType(property);
            if (TypeMatchesGenericDefinition(unityResolvedType, typeof(SerializableSortedSet<>)))
            {
                return true;
            }

            object instance = GetSetInstance(property, property.propertyPath);
            if (instance == null)
            {
                SerializedObject serializedObject = property.serializedObject;
                if (serializedObject != null)
                {
                    Object target = serializedObject.targetObject;
                    if (target != null)
                    {
                        instance = GetMemberValue(target, property.name);
                    }
                }
            }

            if (instance is ISerializableSetInspector { SupportsSorting: true })
            {
                return true;
            }

            Type instanceType = instance?.GetType();
            return TypeMatchesGenericDefinition(instanceType, typeof(SerializableSortedSet<>));
        }

        private static Type TryResolveDeclaredSetType(SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            SerializedObject serializedObject = property.serializedObject;
            if (serializedObject == null)
            {
                return null;
            }

            Object targetObject = serializedObject.targetObject;
            if (targetObject == null)
            {
                return null;
            }

            string propertyPath = property.propertyPath;
            if (string.IsNullOrEmpty(propertyPath))
            {
                return null;
            }

            string[] segments = propertyPath.Split('.');
            return ResolveDeclaredType(targetObject.GetType(), segments, 0);
        }

        private static Type ResolveDeclaredType(Type type, string[] segments, int index)
        {
            while (type != null && segments != null && index < segments.Length)
            {
                string segment = segments[index];
                if (segment == "Array")
                {
                    if (index + 1 >= segments.Length)
                    {
                        break;
                    }

                    string next = segments[index + 1];
                    if (!next.StartsWith("data[", StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (type.IsArray)
                    {
                        type = type.GetElementType();
                        index += 2;
                        continue;
                    }

                    if (type.IsGenericType)
                    {
                        Type[] arguments = type.GetGenericArguments();
                        if (arguments.Length == 1)
                        {
                            type = arguments[0];
                            index += 2;
                            continue;
                        }
                    }

                    break;
                }

                FieldInfo field = type.GetField(
                    segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (field != null)
                {
                    type = field.FieldType;
                    index += 1;
                    continue;
                }

                PropertyInfo propertyInfo = type.GetProperty(
                    segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (propertyInfo != null)
                {
                    type = propertyInfo.PropertyType;
                    index += 1;
                    continue;
                }

                break;
            }

            return type;
        }

        private static bool TypeMatchesGenericDefinition(Type candidate, Type openGeneric)
        {
            Type current = candidate;
            string openGenericName = openGeneric.Name;
            int tickIndex = openGenericName.IndexOf('`');
            if (tickIndex >= 0)
            {
                openGenericName = openGenericName.Substring(0, tickIndex);
            }

            while (current != null)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openGeneric)
                {
                    return true;
                }

                string currentName = current.FullName ?? current.Name;
                if (
                    !string.IsNullOrEmpty(currentName)
                    && currentName.IndexOf(openGenericName, StringComparison.Ordinal) >= 0
                    && typeof(ISerializableSetInspector).IsAssignableFrom(current)
                )
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static Type ResolveUnityPropertyType(SerializedProperty property)
        {
            string typeName = property?.type;
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            if (!PropertyTypeResolutionCache.TryGetValue(typeName, out Type cached))
            {
                cached = FindTypeByUnityName(typeName);
                PropertyTypeResolutionCache[typeName] = cached;
            }

            return cached;
        }

        private static Type FindTypeByUnityName(string typeName)
        {
            foreach (Assembly assembly in ReflectionHelpers.GetAllLoadedAssemblies())
            {
                Type match = FindTypeByName(assembly, typeName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Type FindTypeByName(Assembly assembly, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            string trimmedName = typeName.Replace("+", ".").Trim();
            bool hasNamespace = typeName.Contains(".");

            Type[] types = ReflectionHelpers.GetTypesFromAssembly(assembly) ?? Array.Empty<Type>();

            foreach (Type candidate in types)
            {
                if (
                    candidate != null
                    && (
                        string.Equals(candidate.Name, typeName, StringComparison.Ordinal)
                        || string.Equals(candidate.FullName, typeName, StringComparison.Ordinal)
                        || string.Equals(candidate.FullName, trimmedName, StringComparison.Ordinal)
                        || (
                            !hasNamespace
                            && (
                                candidate.FullName?.EndsWith(
                                    "." + typeName,
                                    StringComparison.Ordinal
                                ) == true
                                || candidate.FullName?.EndsWith(
                                    "+" + typeName,
                                    StringComparison.Ordinal
                                ) == true
                            )
                        )
                    )
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool CanSortElements(SerializedProperty itemsProperty, bool allowSort)
        {
            if (!allowSort)
            {
                return false;
            }

            if (itemsProperty is not { isArray: true } || itemsProperty.arraySize <= 1)
            {
                return false;
            }

            return true;
        }

        private static bool NeedsSorting(SerializedProperty itemsProperty, bool allowSort)
        {
            if (!CanSortElements(itemsProperty, allowSort))
            {
                return false;
            }

            int count = itemsProperty.arraySize;
            SetElementData previous = default;
            bool hasPrevious = false;

            for (int index = 0; index < count; index++)
            {
                SerializedProperty elementProperty = itemsProperty.GetArrayElementAtIndex(index);
                SetElementData current = ReadElementData(elementProperty);

                if (hasPrevious)
                {
                    int comparison = CompareComparableValues(
                        previous.comparable,
                        current.comparable
                    );
                    if (comparison > 0)
                    {
                        return true;
                    }

                    if (comparison == 0)
                    {
                        string previousFallback =
                            previous.value != null ? previous.value.ToString() : string.Empty;
                        string currentFallback =
                            current.value != null ? current.value.ToString() : string.Empty;
                        if (string.CompareOrdinal(previousFallback, currentFallback) > 0)
                        {
                            return true;
                        }
                    }
                }

                previous = current;
                hasPrevious = true;
            }

            return false;
        }

        internal bool TryAddNewElement(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination
        )
        {
            if (
                !TryGetSetInspector(property, propertyPath, out ISerializableSetInspector inspector)
            )
            {
                return false;
            }

            Type elementType = inspector.ElementType;
            if (elementType == null)
            {
                return false;
            }

            if (ElementTypeSupportsNull(elementType) && elementType != typeof(string))
            {
                if (
                    AppendNullPlaceholderEntry(
                        ref property,
                        propertyPath,
                        ref itemsProperty,
                        pagination,
                        inspector
                    )
                )
                {
                    return true;
                }
            }

            Array snapshot = null;
            bool hasSerializedArray = itemsProperty is { isArray: true };
            int estimatedCount = hasSerializedArray ? Math.Max(0, itemsProperty.arraySize) : 0;
            if (!hasSerializedArray)
            {
                snapshot = inspector.GetSerializedItemsSnapshot();
                estimatedCount = snapshot?.Length ?? 0;
            }

            using PooledResource<List<object>> existingValuesLease = Buffers<object>.GetList(
                Math.Max(estimatedCount, 4),
                out List<object> existingValues
            );

            existingValues.Clear();
            if (hasSerializedArray)
            {
                for (int index = 0; index < itemsProperty.arraySize; index++)
                {
                    SerializedProperty element = itemsProperty.GetArrayElementAtIndex(index);
                    existingValues.Add(ReadElementData(element).value);
                }
            }
            else if (snapshot is { Length: > 0 })
            {
                foreach (object value in snapshot)
                {
                    existingValues.Add(value);
                }
            }

            SerializedObject serializedObject = property.serializedObject;
            Object[] targets = serializedObject.targetObjects;

            foreach (
                object candidate in GenerateCandidateValues(elementType, inspector.UniqueCount)
            )
            {
                if (inspector.ContainsElement(candidate))
                {
                    continue;
                }

                if (targets.Length > 0)
                {
                    Undo.RecordObjects(targets, "Add Set Entry");
                }

                if (!inspector.TryAddElement(candidate, out object normalizedValue))
                {
                    continue;
                }

                existingValues.Add(normalizedValue);

                Array updated = Array.CreateInstance(elementType, existingValues.Count);
                for (int index = 0; index < existingValues.Count; index++)
                {
                    object coerced = ConvertSnapshotValue(elementType, existingValues[index]);
                    updated.SetValue(coerced, index);
                }

                inspector.SetSerializedItemsSnapshot(updated, preserveSerializedEntries: true);
                inspector.SynchronizeSerializedState();
                Undo.FlushUndoRecordObjects();

                serializedObject.Update();
                property = serializedObject.FindProperty(propertyPath);
                itemsProperty = property?.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );
                int totalCount = itemsProperty is { isArray: true } ? itemsProperty.arraySize : 0;
                EnsurePaginationBounds(pagination, totalCount);
                EvaluateDuplicateState(property, itemsProperty, force: true);
                EvaluateNullEntryState(property, itemsProperty);
                SyncRuntimeSet(property);
                if (totalCount > 0)
                {
                    pagination.selectedIndex = totalCount - 1;
                }
                MarkListCacheDirty(GetPropertyCacheKey(property));
                return true;
            }

            if (ElementTypeSupportsNull(elementType))
            {
                if (
                    AppendNullPlaceholderEntry(
                        ref property,
                        propertyPath,
                        ref itemsProperty,
                        pagination,
                        inspector
                    )
                )
                {
                    return true;
                }
            }

            Debug.LogWarning("Unable to generate a unique value for this set element type.");
            return false;
        }

        private bool AppendNullPlaceholderEntry(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination,
            ISerializableSetInspector inspector
        )
        {
            Type elementType = inspector.ElementType;
            SerializedObject serializedObject = property.serializedObject;

            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Add Set Entry");
            }

            Array snapshot = inspector.GetSerializedItemsSnapshot();
            int existingCount = snapshot?.Length ?? inspector.SerializedCount;
            Array expanded = Array.CreateInstance(elementType, existingCount + 1);

            if (snapshot is { Length: > 0 })
            {
                Array.Copy(snapshot, expanded, snapshot.Length);
            }
            else if (itemsProperty is { isArray: true })
            {
                for (
                    int index = 0;
                    index < itemsProperty.arraySize && index < existingCount;
                    index++
                )
                {
                    SerializedProperty element = itemsProperty.GetArrayElementAtIndex(index);
                    object value = ReadElementData(element).value;
                    expanded.SetValue(ConvertSnapshotValue(elementType, value), index);
                }
            }

            expanded.SetValue(null, existingCount);
            inspector.SetSerializedItemsSnapshot(expanded, preserveSerializedEntries: true);
            inspector.SynchronizeSerializedState();
            Undo.FlushUndoRecordObjects();

            serializedObject.Update();
            property = serializedObject.FindProperty(propertyPath);
            itemsProperty = property?.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            int totalCount = itemsProperty is { isArray: true } ? itemsProperty.arraySize : 0;
            EnsurePaginationBounds(pagination, totalCount);
            EvaluateDuplicateState(property, itemsProperty, force: true);
            EvaluateNullEntryState(property, itemsProperty);
            SyncRuntimeSet(property);
            if (totalCount > 0)
            {
                pagination.selectedIndex = totalCount - 1;
            }
            MarkListCacheDirty(GetPropertyCacheKey(property));

            return true;
        }

        internal ListPageCache EnsurePageCache(
            string cacheKey,
            SerializedProperty itemsProperty,
            PaginationState pagination
        )
        {
            ListPageCache cache = GetOrCreatePageCache(cacheKey);
            int itemCount = itemsProperty is { isArray: true } ? itemsProperty.arraySize : 0;
            if (
                cache.dirty
                || cache.pageIndex != pagination.page
                || cache.pageSize != pagination.pageSize
                || cache.itemCount != itemCount
            )
            {
                RefreshPageCache(cache, itemsProperty, pagination);
            }

            return cache;
        }

        private ListPageCache GetOrCreatePageCache(string cacheKey)
        {
            if (_pageCaches.TryGetValue(cacheKey, out ListPageCache cache))
            {
                return cache;
            }

            cache = new ListPageCache();
            _pageCaches[cacheKey] = cache;
            return cache;
        }

        private static void RefreshPageCache(
            ListPageCache cache,
            SerializedProperty itemsProperty,
            PaginationState pagination
        )
        {
            cache.entries.Clear();

            if (itemsProperty is not { isArray: true })
            {
                cache.itemCount = 0;
                cache.pageIndex = pagination.page;
                cache.pageSize = pagination.pageSize;
                cache.dirty = false;
                return;
            }

            cache.itemCount = itemsProperty.arraySize;
            cache.pageIndex = pagination.page;
            int effectivePageSize = Mathf.Clamp(pagination.pageSize, 1, MaxPageSize);
            cache.pageSize = effectivePageSize;
            pagination.pageSize = effectivePageSize;
            cache.dirty = false;

            if (cache.itemCount <= 0)
            {
                return;
            }

            int startIndex = pagination.page * effectivePageSize;
            startIndex = Mathf.Clamp(startIndex, 0, cache.itemCount);
            int endIndex = Mathf.Min(startIndex + effectivePageSize, cache.itemCount);

            for (int i = startIndex; i < endIndex; i++)
            {
                PageEntry entry = new() { arrayIndex = i };
                cache.entries.Add(entry);
            }
        }

        private void MarkListCacheDirty(string cacheKey)
        {
            _lists.Remove(cacheKey);
            InvalidateRowFoldoutStates(cacheKey);

            if (_pageCaches.TryGetValue(cacheKey, out ListPageCache cache))
            {
                cache.entries.Clear();
                cache.dirty = true;
                cache.pageIndex = -1;
                cache.pageSize = -1;
                cache.itemCount = -1;
            }
        }

        internal static void SyncRuntimeSet(SerializedProperty setProperty)
        {
            if (setProperty == null)
            {
                return;
            }

            SerializedObject sharedSerializedObject = setProperty.serializedObject;
            Object[] targets = sharedSerializedObject.targetObjects;
            string propertyPath = setProperty.propertyPath;

            foreach (Object target in targets)
            {
                bool isScriptableSingletonTarget = IsScriptableSingletonType(target);
                bool calledSave = false;

                using SerializedObject targetSerializedObject = new(target);
                targetSerializedObject.UpdateIfRequiredOrScript();
                SerializedProperty targetSetProperty = targetSerializedObject.FindProperty(
                    propertyPath
                );
                if (targetSetProperty == null)
                {
                    continue;
                }

                SerializedProperty targetItemsProperty = targetSetProperty.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );

                object setInstance = GetTargetObjectOfProperty(target, propertyPath);
                bool isInspector = setInstance is ISerializableSetInspector;

                if (setInstance is not ISerializableSetInspector inspector)
                {
                    PaletteSerializationDiagnostics.ReportSyncRuntimeSet(
                        sharedSerializedObject,
                        propertyPath,
                        setInstance,
                        isInspector,
                        calledSave
                    );
                    continue;
                }

                // For ScriptableSingleton targets, we must be careful
                // about how we sync because the managed serialized fields (_items) might not be
                // updated by Unity's serialization system yet.
                // We read from SerializedProperties (which have current data) and use the
                // inspector interface to update the runtime state.
                Array snapshot = BuildSnapshotArray(targetItemsProperty, inspector.ElementType);
                inspector.SetSerializedItemsSnapshot(snapshot, preserveSerializedEntries: true);

                if (setInstance is ISerializableSetEditorSync editorSync)
                {
                    editorSync.EditorAfterDeserialize();
                }

                inspector.SynchronizeSerializedState();
                EditorUtility.SetDirty(target);

                if (isScriptableSingletonTarget)
                {
                    if (target is UnityHelpersSettings unitySettings)
                    {
                        unitySettings.SaveSettings();
                    }
                    else
                    {
                        SaveScriptableSingleton(target);
                    }
                    calledSave = true;
                }

                PaletteSerializationDiagnostics.ReportSyncRuntimeSet(
                    sharedSerializedObject,
                    propertyPath,
                    setInstance,
                    isInspector,
                    calledSave
                );
            }

            sharedSerializedObject.UpdateIfRequiredOrScript();
        }

        /// <summary>
        /// Checks if the target is a ScriptableSingleton type.
        /// ScriptableSingletons have issues with ApplyModifiedProperties not persisting changes.
        /// </summary>
        internal static bool IsScriptableSingletonType(Object target)
        {
            if (target == null)
            {
                return false;
            }

            // Check if the type inherits from ScriptableSingleton<T>
            Type scriptableSingletonGenericType = typeof(ScriptableSingleton<>);
            Type type = target.GetType();
            while (type != null)
            {
                if (
                    type.IsGenericType
                    && type.GetGenericTypeDefinition() == scriptableSingletonGenericType
                )
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Saves a ScriptableSingleton by calling its Save(true) method via reflection.
        /// </summary>
        internal static void SaveScriptableSingleton(Object target)
        {
            if (target == null)
            {
                return;
            }

            // Find the Save method on ScriptableSingleton<T>
            Type type = target.GetType();
            MethodInfo saveMethod = null;
            while (type != null && saveMethod == null)
            {
                saveMethod = type.GetMethod(
                    "Save",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null
                );
                type = type.BaseType;
            }

            if (saveMethod != null)
            {
                saveMethod.Invoke(target, new object[] { true });
            }
        }

        private RowRenderData GetOrCreateRowRenderData(
            string listKey,
            int arrayIndex,
            SerializedProperty itemsProperty,
            Type elementType
        )
        {
            int currentFrame = Time.frameCount;
            if (_lastRowRenderCacheFrame != currentFrame)
            {
                _rowRenderCache.Clear();
                _lastRowRenderCacheFrame = currentFrame;
            }

            RowRenderKey cacheKey = new(listKey, arrayIndex);
            if (_rowRenderCache.TryGetValue(cacheKey, out RowRenderData cached))
            {
                return cached;
            }

            RowRenderData data = new();

            if (
                itemsProperty == null
                || !itemsProperty.isArray
                || arrayIndex < 0
                || arrayIndex >= itemsProperty.arraySize
            )
            {
                data.isValid = false;
                _rowRenderCache[cacheKey] = data;
                return data;
            }

            data.itemProperty = itemsProperty.GetArrayElementAtIndex(arrayIndex);
            data.isValid = true;

            bool valueSupportsFoldout = ShouldUseElementFoldout(elementType, data.itemProperty);

            data.itemHeight = SerializableDictionaryPropertyDrawer.CalculateValueContentHeight(
                data.itemProperty,
                valueSupportsFoldout
            );

            float padding = EditorGUIUtility.standardVerticalSpacing * 2f;
            data.rowHeight =
                Mathf.Max(data.itemHeight, EditorGUIUtility.singleLineHeight) + padding;

            _rowRenderCache[cacheKey] = data;
            return data;
        }

        private float GetSetListElementHeight(
            string listKey,
            ListPageCache cache,
            int relativeIndex
        )
        {
            if (!RelativeIndexIsValid(cache, relativeIndex))
            {
                return EditorGUIUtility.singleLineHeight;
            }

            if (!_listContexts.TryGetValue(listKey, out SetListRenderContext context))
            {
                return EditorGUIUtility.singleLineHeight;
            }

            SerializedProperty itemsProperty = context.itemsProperty;
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            int arrayIndex = cache.entries[relativeIndex].arrayIndex;

            RowRenderData rowData = GetOrCreateRowRenderData(
                listKey,
                arrayIndex,
                itemsProperty,
                context.elementType
            );

            if (!rowData.isValid)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            bool valueSupportsFoldout = ShouldUseElementFoldout(
                context.elementType,
                rowData.itemProperty
            );
            if (valueSupportsFoldout)
            {
                RowFoldoutKey foldoutKey = BuildRowFoldoutKey(listKey, arrayIndex);
                EnsureRowFoldoutState(foldoutKey, rowData.itemProperty);
            }

            return rowData.rowHeight;
        }

        private void DrawSetListElement(
            string listKey,
            ListPageCache cache,
            Rect rect,
            int relativeIndex
        )
        {
            if (!RelativeIndexIsValid(cache, relativeIndex))
            {
                return;
            }

            if (!_listContexts.TryGetValue(listKey, out SetListRenderContext context))
            {
                return;
            }

            SerializedProperty itemsProperty = context.itemsProperty;
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return;
            }

            int arrayIndex = cache.entries[relativeIndex].arrayIndex;

            RowRenderData rowData = GetOrCreateRowRenderData(
                listKey,
                arrayIndex,
                itemsProperty,
                context.elementType
            );

            if (!rowData.isValid)
            {
                return;
            }

            SerializedProperty element = rowData.itemProperty;
            bool valueSupportsFoldout = ShouldUseElementFoldout(context.elementType, element);
            RowFoldoutKey foldoutKey = default;
            bool hasFoldoutKey = false;
            if (valueSupportsFoldout)
            {
                foldoutKey = BuildRowFoldoutKey(listKey, arrayIndex);
                hasFoldoutKey = foldoutKey.IsValid;
                if (hasFoldoutKey)
                {
                    EnsureRowFoldoutState(foldoutKey, element);
                }
            }
            bool isDuplicate =
                context.duplicateState != null
                && context.duplicateState.duplicateIndices.Contains(arrayIndex);
            bool isPrimaryDuplicate =
                context.duplicateState != null
                && context.duplicateState.primaryFlags.GetValueOrDefault(arrayIndex, false);
            bool hasNullValue =
                context.nullState is { hasNullEntries: true }
                && context.nullState.nullIndices.Contains(arrayIndex);

            Rect backgroundRect = new(rect.x, rect.y + 1f, rect.width, rect.height - 2f);
            backgroundRect.x += 2f;
            backgroundRect.width = Mathf.Max(0f, backgroundRect.width - 4f);

            Color baseRowColor = EditorGUIUtility.isProSkin ? DarkRowColor : LightRowColor;
            EditorGUI.DrawRect(backgroundRect, baseRowColor);

            UnityHelpersSettings.DuplicateRowAnimationMode animationMode =
                UnityHelpersSettings.GetDuplicateRowAnimationMode();
#pragma warning disable CS0618
            bool highlightDuplicates =
                animationMode != UnityHelpersSettings.DuplicateRowAnimationMode.None
                && context.duplicateState != null;
#pragma warning restore CS0618
            bool animateDuplicates =
                highlightDuplicates && UnityHelpersSettings.ShouldTweenSerializableSetDuplicates();
            int tweenCycleLimit = UnityHelpersSettings.GetSerializableSetDuplicateTweenCycleLimit();
            double currentTime = animateDuplicates ? GetHighResolutionTime() : 0d;

            float shakeOffset = 0f;
            if (isDuplicate && animateDuplicates && context.duplicateState != null)
            {
                shakeOffset = context.duplicateState.GetAnimationOffset(
                    arrayIndex,
                    currentTime,
                    tweenCycleLimit
                );
            }

            Rect highlightRect = ExpandRowRectVertically(backgroundRect);
            highlightRect.x += shakeOffset;

            Rect insetHighlightRect = highlightRect;
            insetHighlightRect.xMin += 1f;
            insetHighlightRect.xMax -= 1f;
            insetHighlightRect.yMin += 1f;
            insetHighlightRect.yMax -= 1f;
            insetHighlightRect.height = Mathf.Max(0f, insetHighlightRect.height);

            if (isDuplicate && highlightDuplicates)
            {
                Color duplicateColor = isPrimaryDuplicate
                    ? DuplicatePrimaryColor
                    : DuplicateSecondaryColor;
                EditorGUI.DrawRect(insetHighlightRect, duplicateColor);
                DrawDuplicateOutline(insetHighlightRect);
            }

            if (hasNullValue)
            {
                EditorGUI.DrawRect(insetHighlightRect, NullEntryHighlightColor);
                DrawDuplicateOutline(insetHighlightRect);
                if (
                    context.nullState != null
                    && context.nullState.tooltips.TryGetValue(arrayIndex, out string tooltip)
                )
                {
                    DrawNullEntryTooltip(insetHighlightRect, tooltip);
                }
            }

            float padding = EditorGUIUtility.standardVerticalSpacing;
            float propertyHeight = SerializableDictionaryPropertyDrawer.CalculateValueContentHeight(
                element,
                valueSupportsFoldout
            );
            Rect contentRect = new(
                rect.x + 16f,
                rect.y + padding,
                Mathf.Max(0f, rect.width - 20f),
                propertyHeight
            );
            contentRect.x += shakeOffset;
            float maxContentBottom = backgroundRect.yMax - padding;
            if (contentRect.yMax > maxContentBottom)
            {
                contentRect.height = Mathf.Max(0f, maxContentBottom - contentRect.y);
            }
            bool elementChanged = false;
            if (valueSupportsFoldout)
            {
                elementChanged = DrawSetRowFoldoutValue(
                    contentRect,
                    element,
                    out float renderedHeight
                );
                contentRect.height = renderedHeight;
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(contentRect, element, GUIContent.none, true);
                elementChanged = EditorGUI.EndChangeCheck();
            }

            if (elementChanged)
            {
                context.needsDuplicateRefresh = true;
                MarkListCacheDirty(listKey);
                RequestRepaint();
            }

            if (contentRect.yMax > maxContentBottom)
            {
                contentRect.height = Mathf.Max(0f, maxContentBottom - contentRect.y);
            }

            HasLastRowContentRect = true;
            LastRowContentRect = contentRect;

            if (valueSupportsFoldout && hasFoldoutKey)
            {
                _rowFoldoutStates[foldoutKey] = element.isExpanded;
            }
        }

        private static bool SerializedPropertySupportsFoldout(SerializedProperty property)
        {
            return property != null && property.hasVisibleChildren;
        }

        private static bool ShouldUseElementFoldout(Type elementType, SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            // Trust the type-based check when the type is known to support complex editing,
            // because Unity's hasVisibleChildren may temporarily return false for newly created
            // array elements before the serialization system fully processes the type structure.
            bool typeSupports =
                elementType == null
                    ? property.hasVisibleChildren
                    : TypeSupportsComplexEditing(elementType)
                        && !typeof(Object).IsAssignableFrom(elementType);

            // Use OR: if the type definitively supports foldout OR property has visible children
            return typeSupports || SerializedPropertySupportsFoldout(property);
        }

        private static bool DrawSetRowFoldoutValue(
            Rect valueRect,
            SerializedProperty valueProperty,
            out float renderedHeight
        )
        {
            if (valueProperty == null)
            {
                renderedHeight = EditorGUIUtility.singleLineHeight;
                return false;
            }

            bool changed = false;
            float headerHeight = EditorGUIUtility.singleLineHeight;
            Rect headerRect = new(valueRect.x, valueRect.y, valueRect.width, headerHeight);

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(
                headerRect,
                valueProperty,
                GUIContent.none,
                includeChildren: false
            );
            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
            }

            float childY = headerRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            if (valueProperty.isExpanded && valueProperty.hasVisibleChildren)
            {
                SerializedProperty iterator = valueProperty.Copy();
                SerializedProperty endProperty = iterator.GetEndProperty();
                bool enterChildren = true;
                int baseDepth = valueProperty.depth;
                int previousIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel++;

                while (
                    iterator.NextVisible(enterChildren)
                    && !SerializedProperty.EqualContents(iterator, endProperty)
                )
                {
                    enterChildren = false;
                    if (iterator.depth <= baseDepth)
                    {
                        break;
                    }

                    float childHeight = EditorGUI.GetPropertyHeight(iterator, true);
                    Rect childRect = new(valueRect.x, childY, valueRect.width, childHeight);
                    EditorGUI.PropertyField(childRect, iterator, true);
                    childY = childRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                }

                EditorGUI.indentLevel = previousIndent;
            }

            renderedHeight = Mathf.Max(
                headerHeight,
                (childY - EditorGUIUtility.standardVerticalSpacing) - valueRect.y
            );
            return changed;
        }

        private void HandleListReorder(
            string listKey,
            SerializedProperty property,
            SerializedProperty itemsProperty,
            PaginationState pagination,
            ListPageCache cache,
            int oldIndex,
            int newIndex
        )
        {
            if (!RelativeIndexIsValid(cache, oldIndex) || cache.entries.Count == 0)
            {
                return;
            }

            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return;
            }

            SerializedObject serializedObject = property.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Reorder Set Entries");
            }

            using PooledResource<List<int>> orderedIndicesLease = Buffers<int>.GetList(
                cache.entries.Count,
                out List<int> orderedIndices
            );
            {
                foreach (PageEntry entry in cache.entries)
                {
                    orderedIndices.Add(entry.arrayIndex);
                }

                int pageSize = Mathf.Max(1, pagination.pageSize);
                int maxStart = Mathf.Max(0, itemsProperty.arraySize - orderedIndices.Count);
                int pageStart = Mathf.Clamp(pagination.page * pageSize, 0, maxStart);

                ApplySliceOrder(itemsProperty, orderedIndices, pageStart);
                int relativeSelection = Mathf.Clamp(newIndex, 0, orderedIndices.Count - 1);
                pagination.selectedIndex = pageStart + relativeSelection;
            }

            serializedObject.ApplyModifiedProperties();
            SyncRuntimeSet(property);
            serializedObject.Update();

            SerializedProperty refreshedProperty = serializedObject.FindProperty(listKey);
            SerializedProperty refreshedItemsProperty = refreshedProperty?.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            EnsurePaginationBounds(
                pagination,
                refreshedItemsProperty is { isArray: true } ? refreshedItemsProperty.arraySize : 0
            );

            MarkListCacheDirty(listKey);
            SerializedProperty finalItemsProperty = refreshedItemsProperty ?? itemsProperty;
            ListPageCache refreshedCache = EnsurePageCache(listKey, finalItemsProperty, pagination);
            if (_lists.TryGetValue(listKey, out ReorderableList existingList))
            {
                existingList.list = refreshedCache.entries;
                SyncListSelectionWithPagination(existingList, pagination, refreshedCache);
            }
            GUI.changed = true;
        }

        private static void ApplySliceOrder(
            SerializedProperty itemsProperty,
            List<int> orderedIndices,
            int pageStart
        )
        {
            if (itemsProperty == null || orderedIndices == null)
            {
                return;
            }

            for (int i = 0; i < orderedIndices.Count; i++)
            {
                int desiredIndex = pageStart + i;
                int currentIndex = orderedIndices[i];
                if (currentIndex == desiredIndex)
                {
                    continue;
                }

                itemsProperty.MoveArrayElement(currentIndex, desiredIndex);

                if (currentIndex < desiredIndex)
                {
                    for (int j = i + 1; j < orderedIndices.Count; j++)
                    {
                        if (orderedIndices[j] > currentIndex && orderedIndices[j] <= desiredIndex)
                        {
                            orderedIndices[j]--;
                        }
                    }
                }
                else
                {
                    for (int j = i + 1; j < orderedIndices.Count; j++)
                    {
                        if (orderedIndices[j] >= desiredIndex && orderedIndices[j] < currentIndex)
                        {
                            orderedIndices[j]++;
                        }
                    }
                }

                orderedIndices[i] = desiredIndex;
            }
        }

        private bool TryClearSet(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty
        )
        {
            if (
                !TryGetSetInspector(property, propertyPath, out ISerializableSetInspector inspector)
            )
            {
                return false;
            }

            SerializedObject serializedObject = property.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Clear Set Entries");
            }

            inspector.ClearElements();
            inspector.SynchronizeSerializedState();
            Undo.FlushUndoRecordObjects();
            serializedObject.Update();
            property = serializedObject.FindProperty(propertyPath);
            itemsProperty = property?.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            SyncRuntimeSet(property);
            EvaluateDuplicateState(property, itemsProperty, force: true);
            EvaluateNullEntryState(property, itemsProperty);
            MarkListCacheDirty(GetPropertyCacheKey(property));
            return true;
        }

        private void TryMoveSelectedEntry(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination,
            int direction
        )
        {
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return;
            }

            int totalCount = itemsProperty.arraySize;
            if (totalCount <= 1)
            {
                return;
            }

            int selectedIndex = pagination.selectedIndex;
            if (selectedIndex < 0 || selectedIndex >= totalCount)
            {
                return;
            }

            int targetIndex = selectedIndex + direction;
            if (targetIndex < 0 || targetIndex >= totalCount)
            {
                return;
            }

            SerializedObject serializedObject = property.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Move Set Entry");
            }

            itemsProperty.MoveArrayElement(selectedIndex, targetIndex);
            pagination.selectedIndex = targetIndex;

            serializedObject.ApplyModifiedProperties();
            SyncRuntimeSet(property);
            serializedObject.Update();
            property = serializedObject.FindProperty(propertyPath);
            itemsProperty = property.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            EnsurePaginationBounds(pagination, itemsProperty?.arraySize ?? 0);
            EvaluateDuplicateState(property, itemsProperty, force: true);
            EvaluateNullEntryState(property, itemsProperty);
            MarkListCacheDirty(GetPropertyCacheKey(property));
        }

        private void TryRemoveSelectedEntry(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination
        )
        {
            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return;
            }

            int targetIndex = pagination.selectedIndex;
            if (targetIndex < 0 || targetIndex >= itemsProperty.arraySize)
            {
                return;
            }

            SerializedObject serializedObject = property.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Remove Set Entry");
            }

            SerializedProperty element = itemsProperty.GetArrayElementAtIndex(targetIndex);
            SetElementData elementData = ReadElementData(element);
            RemoveEntry(itemsProperty, targetIndex);
            RemoveValueFromSet(property, propertyPath, elementData.value);
            serializedObject.ApplyModifiedProperties();
            SyncRuntimeSet(property);
            serializedObject.Update();
            property = serializedObject.FindProperty(propertyPath);
            itemsProperty = property.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            bool hasItemsArray = itemsProperty is { isArray: true };
            int totalCount = hasItemsArray ? itemsProperty.arraySize : 0;
            if (pagination.selectedIndex >= totalCount)
            {
                pagination.selectedIndex = totalCount - 1;
            }
            EnsurePaginationBounds(pagination, totalCount);
            EvaluateDuplicateState(property, itemsProperty, force: true);
            MarkListCacheDirty(GetPropertyCacheKey(property));
        }

        private bool TrySortElements(
            ref SerializedProperty property,
            string propertyPath,
            SerializedProperty itemsProperty
        )
        {
            if (
                itemsProperty == null
                || !itemsProperty.isArray
                || itemsProperty.arraySize <= 1
                || !TryGetSetInspector(
                    property,
                    propertyPath,
                    out ISerializableSetInspector inspector
                )
            )
            {
                return false;
            }

            bool allowSort =
                inspector.SupportsSorting || ElementSupportsManualSorting(inspector.ElementType);
            if (!allowSort)
            {
                return false;
            }

            SerializedObject serializedObject = property.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Sort Set Entries");
            }

            int count = itemsProperty.arraySize;
            using PooledResource<List<SetElementData>> elementsLease =
                Buffers<SetElementData>.GetList(count, out List<SetElementData> elements);

            for (int index = 0; index < count; index++)
            {
                SerializedProperty elementProperty = itemsProperty.GetArrayElementAtIndex(index);
                elements.Add(ReadElementData(elementProperty));
            }

            elements.Sort(
                (left, right) =>
                {
                    int comparison = CompareComparableValues(left.comparable, right.comparable);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    string leftFallback = left.value != null ? left.value.ToString() : string.Empty;
                    string rightFallback =
                        right.value != null ? right.value.ToString() : string.Empty;
                    return string.CompareOrdinal(leftFallback, rightFallback);
                }
            );

            for (int index = 0; index < count; index++)
            {
                SerializedProperty elementProperty = itemsProperty.GetArrayElementAtIndex(index);
                WriteElementValue(elementProperty, elements[index]);
            }

            inspector.ClearElements();
            foreach (SetElementData element in elements)
            {
                inspector.TryAddElement(element.value, out object _);
            }

            inspector.SynchronizeSerializedState();
            Undo.FlushUndoRecordObjects();

            property.serializedObject.Update();
            property = property.serializedObject.FindProperty(propertyPath);
            SyncRuntimeSet(property);
            MarkListCacheDirty(GetPropertyCacheKey(property));
            return true;
        }

        internal void RemoveValueFromSet(
            SerializedProperty property,
            string propertyPath,
            object value
        )
        {
            if (
                !TryGetSetInspector(property, propertyPath, out ISerializableSetInspector inspector)
            )
            {
                return;
            }

            if (inspector.RemoveElement(value))
            {
                inspector.SynchronizeSerializedState();
            }
        }

        private static object GetSetInstance(SerializedProperty property, string propertyPath)
        {
            return GetTargetObjectOfProperty(property.serializedObject.targetObject, propertyPath);
        }

        private bool TryGetSetInspectorCached(
            SerializedProperty property,
            string propertyPath,
            out ISerializableSetInspector inspector
        )
        {
            string cacheKey = GetPropertyCacheKey(property);
            int currentFrame = Time.frameCount;

            if (_inspectorCache.TryGetValue(cacheKey, out InspectorCacheEntry entry))
            {
                if (entry.frameNumber == currentFrame)
                {
                    inspector = entry.inspector;
                    return inspector != null;
                }
            }
            else
            {
                entry = new InspectorCacheEntry();
                _inspectorCache[cacheKey] = entry;
            }

            bool result = TryGetSetInspector(property, propertyPath, out inspector);
            entry.inspector = inspector;
            entry.frameNumber = currentFrame;
            return result;
        }

        private bool TryGetSetInspector(
            SerializedProperty property,
            string propertyPath,
            out ISerializableSetInspector inspector
        )
        {
            object setInstance = GetSetInstance(property, propertyPath);
            inspector = setInstance as ISerializableSetInspector;
            return inspector != null;
        }

        private static IEnumerable<object> GenerateCandidateValues(
            Type elementType,
            int existingCount
        )
        {
            if (elementType == typeof(string))
            {
                yield return string.Empty;
                for (int i = 1; i < MaxAutoAddAttempts; i++)
                {
                    yield return $"New Entry {i}";
                }
                yield break;
            }

            if (IsSignedIntegral(elementType))
            {
                for (long i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return Convert.ChangeType(i, elementType);
                    if (i > 0)
                    {
                        yield return Convert.ChangeType(-i, elementType);
                    }
                }
                yield break;
            }

            if (IsUnsignedIntegral(elementType))
            {
                for (long i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return Convert.ChangeType(i, elementType);
                }
                yield break;
            }

            if (
                elementType == typeof(float)
                || elementType == typeof(double)
                || elementType == typeof(decimal)
            )
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return Convert.ChangeType(i, elementType);
                }
                yield break;
            }

            if (elementType == typeof(bool))
            {
                yield return false;
                yield return true;
                yield break;
            }

            if (elementType == typeof(char))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return (char)('A' + (i % 26));
                }
                yield break;
            }

            if (elementType.IsEnum)
            {
                foreach (object value in Enum.GetValues(elementType))
                {
                    yield return value;
                }
                yield break;
            }

            if (elementType == typeof(Vector2))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new Vector2(i + 1f, 0f);
                }
                yield break;
            }

            if (elementType == typeof(Vector3))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new Vector3(i + 1f, 0f, 0f);
                }
                yield break;
            }

            if (elementType == typeof(Vector4))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new Vector4(i + 1f, 0f, 0f, 0f);
                }
                yield break;
            }

            if (elementType == typeof(Vector2Int))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new Vector2Int(i + 1, 0);
                }
                yield break;
            }

            if (elementType == typeof(Vector3Int))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new Vector3Int(i + 1, 0, 0);
                }
                yield break;
            }

            if (elementType == typeof(Rect))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new Rect(i + 1f, 0f, 1f, 1f);
                }
                yield break;
            }

            if (elementType == typeof(RectInt))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return new RectInt(i + 1, 0, 1, 1);
                }
                yield break;
            }

            if (elementType == typeof(Bounds))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    Bounds bounds = new(new Vector3(i + 1f, 0f, 0f), Vector3.one);
                    yield return bounds;
                }
                yield break;
            }

            if (elementType == typeof(BoundsInt))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    BoundsInt bounds = new(new Vector3Int(i + 1, 0, 0), Vector3Int.one);
                    yield return bounds;
                }
                yield break;
            }

            if (elementType == typeof(Color))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    float hue =
                        (existingCount + i) % MaxAutoAddAttempts / (float)MaxAutoAddAttempts;
                    yield return Color.HSVToRGB(hue, 0.8f, 1f);
                }
                yield break;
            }

            if (elementType == typeof(Quaternion))
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return Quaternion.Euler((existingCount + i) * 10f, 0f, 0f);
                }
                yield break;
            }

            if (elementType == typeof(Hash128))
            {
                for (uint i = 1; i <= MaxAutoAddAttempts; i++)
                {
                    yield return new Hash128(i, 0u, 0u, 0u);
                }
                yield break;
            }

            if (typeof(Object).IsAssignableFrom(elementType))
            {
                yield return null;
                yield break;
            }

            if (
                !elementType.IsAbstract
                && TryGetParameterlessFactory(elementType, out Func<object> elementFactory)
            )
            {
                for (int i = 0; i < MaxAutoAddAttempts; i++)
                {
                    yield return elementFactory();
                }
                yield break;
            }

            if (
                elementType.IsValueType
                && TryGetParameterlessFactory(elementType, out Func<object> valueFactory)
            )
            {
                yield return valueFactory();
            }
            else
            {
                yield return null;
            }
        }

        private static bool IsSignedIntegral(Type type)
        {
            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(sbyte);
        }

        private static bool IsUnsignedIntegral(Type type)
        {
            return type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort)
                || type == typeof(byte);
        }

        private static object GetTargetObjectOfProperty(object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
            {
                return null;
            }

            object currentTarget = target;
            string[] elements = propertyPath.Replace(".Array.data[", "[").Split('.');
            foreach (string element in elements)
            {
                if (currentTarget == null)
                {
                    return null;
                }

                if (element.Contains("["))
                {
                    int leftBracket = element.IndexOf('[');
                    string elementName = element.Substring(0, leftBracket);
                    string indexPart = element
                        .Substring(leftBracket)
                        .Replace("[", string.Empty)
                        .Replace("]", string.Empty);
                    if (!int.TryParse(indexPart, out int index))
                    {
                        return null;
                    }

                    currentTarget = GetIndexedValue(currentTarget, elementName, index);
                }
                else
                {
                    currentTarget = GetMemberValue(currentTarget, element);
                }
            }

            return currentTarget;
        }

        private static object GetMemberValue(object source, string name)
        {
            if (source == null)
            {
                return null;
            }

            Type type = source.GetType();

            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (field != null)
            {
                return field.GetValue(source);
            }

            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            return property?.GetValue(source);
        }

        private static object GetIndexedValue(object source, string name, int index)
        {
            object collection = GetMemberValue(source, name);
            if (collection is not IEnumerable enumerable)
            {
                return null;
            }

            IEnumerator enumerator = enumerable.GetEnumerator();
            try
            {
                for (int i = 0; i <= index; i++)
                {
                    if (!enumerator.MoveNext())
                    {
                        return null;
                    }
                }

                return enumerator.Current;
            }
            finally
            {
                if (enumerator is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        internal void SortElements(SerializedProperty property, SerializedProperty itemsProperty)
        {
            TrySortElements(ref property, property.propertyPath, itemsProperty);
        }

        private static int CompareComparableValues(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null || left == NullComparable)
            {
                return right == null || right == NullComparable ? 0 : -1;
            }

            if (right == null || right == NullComparable)
            {
                return 1;
            }

            if (left is Object || right is Object)
            {
                Object leftObject = left as Object;
                Object rightObject = right as Object;
                return UnityObjectNameComparer<Object>.Instance.Compare(leftObject, rightObject);
            }

            if (left is IComparable comparable)
            {
                return comparable.CompareTo(right);
            }

            if (right is IComparable comparableRight)
            {
                return -comparableRight.CompareTo(left);
            }

            string leftString = left.ToString();
            string rightString = right.ToString();
            return string.CompareOrdinal(leftString, rightString);
        }

        private static SetElementData ReadElementData(SerializedProperty property)
        {
            SetElementData data = new()
            {
                propertyType = property.propertyType,
                comparable = null,
                value = null,
            };

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.ArraySize:
                    long longValue = property.longValue;
                    data.value = longValue;
                    data.comparable = longValue;
                    break;
                case SerializedPropertyType.Boolean:
                    bool boolValue = property.boolValue;
                    data.value = boolValue;
                    data.comparable = boolValue ? 1 : 0;
                    break;
                case SerializedPropertyType.Float:
                    double doubleValue = property.doubleValue;
                    data.value = doubleValue;
                    data.comparable = doubleValue;
                    break;
                case SerializedPropertyType.String:
                    string stringValue = property.stringValue ?? string.Empty;
                    data.value = stringValue;
                    data.comparable = stringValue;
                    break;
                case SerializedPropertyType.Color:
                    Color colorValue = property.colorValue;
                    data.value = colorValue;
                    data.comparable = colorValue;
                    break;
                case SerializedPropertyType.Vector2:
                    Vector2 vector2Value = property.vector2Value;
                    data.value = vector2Value;
                    data.comparable = vector2Value;
                    break;
                case SerializedPropertyType.Vector3:
                    Vector3 vector3Value = property.vector3Value;
                    data.value = vector3Value;
                    data.comparable = vector3Value;
                    break;
                case SerializedPropertyType.Vector4:
                    Vector4 vector4Value = property.vector4Value;
                    data.value = vector4Value;
                    data.comparable = vector4Value;
                    break;
                case SerializedPropertyType.Rect:
                    Rect rectValue = property.rectValue;
                    data.value = rectValue;
                    data.comparable = rectValue;
                    break;
                case SerializedPropertyType.Bounds:
                    Bounds boundsValue = property.boundsValue;
                    data.value = boundsValue;
                    data.comparable = boundsValue;
                    break;
                case SerializedPropertyType.Vector2Int:
                    Vector2Int vector2IntValue = property.vector2IntValue;
                    data.value = vector2IntValue;
                    data.comparable = vector2IntValue;
                    break;
                case SerializedPropertyType.Vector3Int:
                    Vector3Int vector3IntValue = property.vector3IntValue;
                    data.value = vector3IntValue;
                    data.comparable = vector3IntValue;
                    break;
                case SerializedPropertyType.RectInt:
                    RectInt rectIntValue = property.rectIntValue;
                    data.value = rectIntValue;
                    data.comparable = rectIntValue;
                    break;
                case SerializedPropertyType.BoundsInt:
                    BoundsInt boundsIntValue = property.boundsIntValue;
                    data.value = boundsIntValue;
                    data.comparable = boundsIntValue;
                    break;
                case SerializedPropertyType.Hash128:
                    Hash128 hashValue = property.hash128Value;
                    data.value = hashValue;
                    data.comparable = hashValue;
                    break;
                case SerializedPropertyType.Quaternion:
                    Quaternion quaternionValue = property.quaternionValue;
                    data.value = quaternionValue;
                    data.comparable = quaternionValue;
                    break;
                case SerializedPropertyType.ObjectReference:
                    Object objectReferenceValue = property.objectReferenceValue;
                    data.value = objectReferenceValue;
                    data.comparable = objectReferenceValue ?? NullComparable;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    AnimationCurve curveValue = property.animationCurveValue;
                    data.value = curveValue;
                    data.comparable = curveValue?.length ?? 0;
                    break;
                case SerializedPropertyType.ManagedReference:
                case SerializedPropertyType.Generic:
#if UNITY_2022_1_OR_NEWER
                    // SerializedProperty.boxedValue did not exist before Unity 2022.1;
                    // on 2021.3 fall back to the property path (the same degraded
                    // identity the catch below already uses for unreadable values).
                    try
                    {
                        object boxed = property.boxedValue;
                        data.value = boxed;
                        data.comparable = boxed ?? NullComparable;
                    }
                    catch (Exception)
                    {
                        data.value = property.propertyPath;
                        data.comparable = property.propertyPath;
                    }
#else
                    data.value = property.propertyPath;
                    data.comparable = property.propertyPath;
#endif
                    break;
                default:
                    data.value = property.propertyPath;
                    data.comparable = property.propertyPath;
                    break;
            }

            return data;
        }

        private static Array BuildSnapshotArray(SerializedProperty itemsProperty, Type elementType)
        {
            elementType ??= typeof(object);

            if (itemsProperty == null || !itemsProperty.isArray)
            {
                return Array.CreateInstance(elementType, 0);
            }

            int count = itemsProperty.arraySize;
            Array snapshot = Array.CreateInstance(elementType, count);

            for (int index = 0; index < count; index++)
            {
                SerializedProperty element = itemsProperty.GetArrayElementAtIndex(index);
                SetElementData elementData = ReadElementData(element);
                object value = ConvertSnapshotValue(elementType, elementData.value);
                snapshot.SetValue(value, index);
            }

            return snapshot;
        }

        private static object ConvertSnapshotValue(Type elementType, object value)
        {
            if (value == null)
            {
                return null;
            }

            if (elementType == null)
            {
                return value;
            }

            Type nullableUnderlying = Nullable.GetUnderlyingType(elementType);
            if (nullableUnderlying != null)
            {
                return ConvertSnapshotValue(nullableUnderlying, value);
            }

            if (elementType.IsInstanceOfType(value))
            {
                return value;
            }

            Type targetType = elementType;

            try
            {
                if (targetType.IsEnum)
                {
                    if (value is string enumName)
                    {
                        return Enum.Parse(targetType, enumName, ignoreCase: true);
                    }

                    return Enum.ToObject(targetType, value);
                }

                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
                {
                    return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                return value;
            }

            return value;
        }

        private static void WriteElementValue(SerializedProperty property, SetElementData data)
        {
            switch (data.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.ArraySize:
                    property.longValue = Convert.ToInt64(data.value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(data.value);
                    break;
                case SerializedPropertyType.Float:
                    property.doubleValue = Convert.ToDouble(data.value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = data.value as string ?? string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = data.value is Color color ? color : Color.white;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = data.value is Vector2 vector2 ? vector2 : Vector2.zero;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = data.value is Vector3 vector3 ? vector3 : Vector3.zero;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = data.value is Vector4 vector4 ? vector4 : Vector4.zero;
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = data.value is Rect rect ? rect : default;
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = data.value is Bounds bounds ? bounds : default;
                    break;
                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = data.value is Vector2Int vector2Int
                        ? vector2Int
                        : default;
                    break;
                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = data.value is Vector3Int vector3Int
                        ? vector3Int
                        : default;
                    break;
                case SerializedPropertyType.RectInt:
                    property.rectIntValue = data.value is RectInt rectInt ? rectInt : default;
                    break;
                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = data.value is BoundsInt boundsInt
                        ? boundsInt
                        : default;
                    break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = data.value is Quaternion quaternion
                        ? quaternion
                        : Quaternion.identity;
                    break;
                case SerializedPropertyType.Hash128:
                    property.hash128Value = data.value is Hash128 hash ? hash : default;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = data.value as Object;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = data.value as AnimationCurve;
                    break;
                case SerializedPropertyType.ManagedReference:
                case SerializedPropertyType.Generic:
#if UNITY_2022_1_OR_NEWER
                    // SerializedProperty.boxedValue is unavailable before Unity 2022.1;
                    // the managed/generic value is simply not written back on 2021.3.
                    try
                    {
                        property.boxedValue = data.value;
                    }
                    catch (Exception)
                    {
                        // Swallow
                    }
#endif
                    break;
            }
        }

        private static GUIContent GetFoldoutLabelContent(GUIContent label)
        {
            string text = label != null ? label.text : "Serialized HashSet";
            string tooltip = label != null ? label.tooltip : null;
            FoldoutLabelContent.text = text;
            FoldoutLabelContent.tooltip = tooltip;
            FoldoutLabelContent.image = label?.image;
            return FoldoutLabelContent;
        }

        /// <summary>
        /// Gets a cached pagination label in the format "Page X / Y".
        /// Delegates to <see cref="EditorCacheHelper.GetPaginationLabel"/> for shared LRU caching.
        /// </summary>
        private static string GetPaginationLabel(int currentPage, int pageCount)
        {
            return EditorCacheHelper.GetPaginationLabel(currentPage, pageCount);
        }

        private static string GetRangeLabel(int start, int end, int total)
        {
            (int, int, int) key = (start, end, total);
            if (RangeLabelCache.TryGetValue(key, out string cached))
            {
                return cached;
            }

            using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                32,
                out StringBuilder builder
            );
            builder.Clear();
            builder.Append(start);
            builder.Append('-');
            builder.Append(end);
            builder.Append(" of ");
            builder.Append(total);
            string result = builder.ToString();

            if (RangeLabelCache.Count < 10000)
            {
                RangeLabelCache[key] = result;
            }

            return result;
        }

        internal bool InvokeTryClearSet(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty
        )
        {
            return TryClearSet(ref property, propertyPath, ref itemsProperty);
        }

        internal void InvokeTryMoveSelectedEntry(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination,
            int direction
        )
        {
            TryMoveSelectedEntry(
                ref property,
                propertyPath,
                ref itemsProperty,
                pagination,
                direction
            );
        }

        internal void InvokeTryRemoveSelectedEntry(
            ref SerializedProperty property,
            string propertyPath,
            ref SerializedProperty itemsProperty,
            PaginationState pagination
        )
        {
            TryRemoveSelectedEntry(ref property, propertyPath, ref itemsProperty, pagination);
        }

        internal bool InvokeTrySortElements(
            ref SerializedProperty property,
            string propertyPath,
            SerializedProperty itemsProperty
        )
        {
            return TrySortElements(ref property, propertyPath, itemsProperty);
        }
    }

    internal static class SerializableSetPropertyDrawerExtensions
    {
        public static Type GetManagedType(this SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            Type type = property.serializedObject.targetObject.GetType();
            string[] path = property.propertyPath.Split('.');
            return ResolveType(type, path, 0);
        }

        private static Type ResolveType(Type rootType, string[] path, int index)
        {
            while (true)
            {
                if (rootType == null || path == null || index >= path.Length)
                {
                    return rootType;
                }

                string segment = path[index];
                FieldInfo field = rootType.GetField(
                    segment,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (field == null)
                {
                    return rootType;
                }

                Type fieldType = field.FieldType;

                if (
                    segment != "Array"
                    || index + 1 >= path.Length
                    || !path[index + 1].StartsWith("data[", StringComparison.Ordinal)
                )
                {
                    rootType = fieldType;
                    index += 1;
                    continue;
                }

                if (fieldType.IsArray)
                {
                    rootType = fieldType.GetElementType();
                    index += 2;
                    continue;
                }

                if (fieldType.IsGenericType)
                {
                    Type[] arguments = fieldType.GetGenericArguments();
                    if (arguments.Length == 1)
                    {
                        rootType = arguments[0];
                        index += 2;
                        continue;
                    }
                }

                return fieldType;
            }
        }
    }
}
