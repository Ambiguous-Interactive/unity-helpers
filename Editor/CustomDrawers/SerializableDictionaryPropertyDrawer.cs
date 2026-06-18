// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable ArrangeRedundantParentheses
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

    /// <summary>
    /// Diagnostics helper for debugging SerializableDictionary/Set foldout tweening issues,
    /// especially within WGroups.
    /// </summary>
    /// <remarks>
    /// Enable logging by setting <see cref="Enabled"/> to true. Logs are written to the Unity console
    /// with the prefix "[DictTween]" for easy filtering.
    /// </remarks>
    internal static class SerializableCollectionTweenDiagnostics
    {
        /// <summary>
        /// When true, enables diagnostic logging for tweening-related calculations.
        /// </summary>
        internal static bool Enabled { get; set; } = false;

        /// <summary>
        /// When set, only logs for properties matching this path (substring match).
        /// Leave null to log all properties.
        /// </summary>
        internal static string PropertyPathFilter { get; set; } = null;

        /// <summary>
        /// When true, only logs when inside a WGroup context (scope depth &gt; 0).
        /// </summary>
        internal static bool OnlyInWGroup { get; set; } = false;

        private const string LogPrefix = "[DictTween] ";

        private static bool ShouldLog(string propertyPath)
        {
            if (!Enabled)
            {
                return false;
            }

            if (OnlyInWGroup && GroupGUIWidthUtility.CurrentScopeDepth <= 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(PropertyPathFilter))
            {
                if (string.IsNullOrEmpty(propertyPath))
                {
                    return false;
                }

                if (
                    propertyPath.IndexOf(PropertyPathFilter, StringComparison.OrdinalIgnoreCase) < 0
                )
                {
                    return false;
                }
            }

            return true;
        }

        internal static void LogAnimBoolState(
            string context,
            string propertyPath,
            bool isExpanded,
            float animFaded,
            float animTarget,
            float animSpeed,
            bool animIsAnimating
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}{context}: path={propertyPath}, "
                    + $"expanded={isExpanded}, faded={animFaded:F4}, target={animTarget:F1}, "
                    + $"speed={animSpeed:F2}, isAnimating={animIsAnimating}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogFoldoutProgressCalculation(
            string context,
            string propertyPath,
            bool shouldTween,
            bool isExpanded,
            float computedProgress,
            bool hasAnimBool
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}{context}: path={propertyPath}, "
                    + $"shouldTween={shouldTween}, expanded={isExpanded}, "
                    + $"progress={computedProgress:F4}, hasAnimBool={hasAnimBool}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogPendingSectionHeightCalc(
            string propertyPath,
            float collapsedHeight,
            float expandedExtraHeight,
            float foldoutProgress,
            float finalHeight
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}PendingSectionHeight: path={propertyPath}, "
                    + $"collapsed={collapsedHeight:F2}, extraExpanded={expandedExtraHeight:F2}, "
                    + $"progress={foldoutProgress:F4}, final={finalHeight:F2}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogTweenSettingsQuery(
            string context,
            string propertyPath,
            bool isSorted,
            bool tweenEnabled,
            float tweenSpeed
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}{context}: path={propertyPath}, isSorted={isSorted}, "
                    + $"tweenEnabled={tweenEnabled}, speed={tweenSpeed:F2}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogAnimBoolCreation(
            string propertyPath,
            bool initialValue,
            bool isSorted,
            float speed
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}AnimBool Created: path={propertyPath}, "
                    + $"initial={initialValue}, isSorted={isSorted}, speed={speed:F2}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogAnimBoolDestroyed(string propertyPath, string reason)
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}AnimBool Destroyed: path={propertyPath}, reason={reason}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogExpandedStateChange(
            string propertyPath,
            bool oldExpanded,
            bool newExpanded,
            float currentProgress
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}ExpandedChange: path={propertyPath}, "
                    + $"old={oldExpanded}, new={newExpanded}, currentProgress={currentProgress:F4}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogRepaintRequest(string propertyPath, string source)
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}RepaintRequested: path={propertyPath}, source={source}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogContentFadeApplication(
            string propertyPath,
            float contentFade,
            bool isVisible,
            bool skipContentDraw
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}ContentFade: path={propertyPath}, "
                    + $"fade={contentFade:F4}, visible={isVisible}, skipDraw={skipContentDraw}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        /// <summary>
        /// Logs a comprehensive dump of all tween-related settings from UnityHelpersSettings.
        /// Call this once per session or when debugging settings issues.
        /// </summary>
        internal static void LogAllTweenSettings(string context)
        {
            if (!Enabled)
            {
                return;
            }

            bool dictEnabled = UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
            float dictSpeed = UnityHelpersSettings.GetSerializableDictionaryFoldoutSpeed();
            bool sortedDictEnabled =
                UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts();
            float sortedDictSpeed =
                UnityHelpersSettings.GetSerializableSortedDictionaryFoldoutSpeed();
            bool setEnabled = UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
            float setSpeed = UnityHelpersSettings.GetSerializableSetFoldoutSpeed();
            bool sortedSetEnabled = UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts();
            float sortedSetSpeed = UnityHelpersSettings.GetSerializableSortedSetFoldoutSpeed();
            bool wgroupEnabled = UnityHelpersSettings.ShouldTweenWGroupFoldouts();
            float wgroupSpeed = UnityHelpersSettings.GetWGroupFoldoutSpeed();

            Debug.Log(
                $"{LogPrefix}AllSettings ({context}): "
                    + $"dict=[enabled={dictEnabled}, speed={dictSpeed:F2}], "
                    + $"sortedDict=[enabled={sortedDictEnabled}, speed={sortedDictSpeed:F2}], "
                    + $"set=[enabled={setEnabled}, speed={setSpeed:F2}], "
                    + $"sortedSet=[enabled={sortedSetEnabled}, speed={sortedSetSpeed:F2}], "
                    + $"wgroup=[enabled={wgroupEnabled}, speed={wgroupSpeed:F2}], "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        /// <summary>
        /// Logs detailed AnimBool timing information for debugging animation state.
        /// </summary>
        internal static void LogAnimBoolTiming(
            string context,
            string propertyPath,
            AnimBool anim,
            bool expectedTarget
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            if (anim == null)
            {
                Debug.Log(
                    $"{LogPrefix}{context}: path={propertyPath}, AnimBool=null, "
                        + $"expectedTarget={expectedTarget}, "
                        + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
                );
                return;
            }

            bool targetMismatch = anim.target != expectedTarget;
            bool valueMismatch = !Mathf.Approximately(anim.faded, expectedTarget ? 1f : 0f);

            Debug.Log(
                $"{LogPrefix}{context}: path={propertyPath}, "
                    + $"target={anim.target}, faded={anim.faded:F4}, "
                    + $"expectedTarget={expectedTarget}, speed={anim.speed:F2}, "
                    + $"isAnimating={anim.isAnimating}, "
                    + $"targetMismatch={targetMismatch}, valueMismatch={valueMismatch}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        /// <summary>
        /// Logs mouse event information for debugging click handling.
        /// </summary>
        internal static void LogMouseEvent(
            string context,
            string propertyPath,
            EventType eventType,
            Vector2 mousePosition,
            Rect hitRect,
            bool isInside
        )
        {
            if (!ShouldLog(propertyPath))
            {
                return;
            }

            // Only log relevant mouse events to avoid spam
            if (
                eventType != EventType.MouseDown
                && eventType != EventType.MouseUp
                && eventType != EventType.Used
            )
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}{context}: path={propertyPath}, "
                    + $"eventType={eventType}, mouse=({mousePosition.x:F1},{mousePosition.y:F1}), "
                    + $"hitRect=({hitRect.x:F1},{hitRect.y:F1},{hitRect.width:F1},{hitRect.height:F1}), "
                    + $"isInside={isInside}, "
                    + $"wgroupDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }
    }

    /// <summary>
    /// Diagnostics helper for debugging SerializableDictionary indentation issues,
    /// especially within WGroups and UnityHelpersSettings.
    /// </summary>
    /// <remarks>
    /// Enable logging by setting <see cref="Enabled"/> to true. Logs are written to the Unity console
    /// with the prefix "[DictIndent]" for easy filtering.
    /// </remarks>
    internal static class SerializableDictionaryIndentDiagnostics
    {
        /// <summary>
        /// When true, enables diagnostic logging for indentation-related calculations.
        /// </summary>
        internal static bool Enabled { get; set; } = false;

        /// <summary>
        /// When set, only logs for properties matching this path (substring match).
        /// Leave null to log all properties.
        /// </summary>
        internal static string PropertyPathFilter { get; set; } = null;

        /// <summary>
        /// When true, only logs for properties targeting UnityHelpersSettings.
        /// </summary>
        internal static bool OnlyUnityHelpersSettings { get; set; } = false;

        private const string LogPrefix = "[DictIndent] ";

        private static bool ShouldLog(string propertyPath, bool isSettings)
        {
            if (!Enabled)
            {
                return false;
            }

            if (OnlyUnityHelpersSettings && !isSettings)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(PropertyPathFilter))
            {
                if (string.IsNullOrEmpty(propertyPath))
                {
                    return false;
                }

                if (
                    propertyPath.IndexOf(PropertyPathFilter, StringComparison.OrdinalIgnoreCase) < 0
                )
                {
                    return false;
                }
            }

            return true;
        }

        internal static void LogOnGUIEntry(
            Rect originalPosition,
            SerializedProperty property,
            bool targetsSettings,
            int indentLevel
        )
        {
            string propertyPath = property?.propertyPath ?? "(null)";
            if (!ShouldLog(propertyPath, targetsSettings))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}OnGUI Entry: path={propertyPath}, targetsSettings={targetsSettings}, "
                    + $"indentLevel={indentLevel}, originalPos={FormatRect(originalPosition)}"
            );
        }

        internal static void LogResolveContentRect(
            Rect inputRect,
            Rect outputRect,
            bool skipIndentation,
            float leftPadding,
            float rightPadding,
            int scopeDepth,
            int indentLevel
        )
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}ResolveContentRect: skip={skipIndentation}, "
                    + $"leftPad={leftPadding:F2}, rightPad={rightPadding:F2}, scopeDepth={scopeDepth}, "
                    + $"indentLevel={indentLevel}, input={FormatRect(inputRect)}, output={FormatRect(outputRect)}"
            );
        }

        internal static void LogResolveContentRectSteps(
            Rect original,
            Rect afterPadding,
            Rect afterIndent,
            Rect final,
            bool skipIndentation,
            float leftPadding,
            float rightPadding,
            int indentLevel
        )
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}ResolveContentRect Steps: skip={skipIndentation}, "
                    + $"leftPad={leftPadding:F2}, rightPad={rightPadding:F2}, indent={indentLevel}\n"
                    + $"  original   = {FormatRect(original)}\n"
                    + $"  afterPad   = {FormatRect(afterPadding)}\n"
                    + $"  afterIndent= {FormatRect(afterIndent)}\n"
                    + $"  final      = {FormatRect(final)}"
            );
        }

        internal static void LogDrawPendingEntryUI(
            string propertyPath,
            Rect position,
            float pendingY,
            bool targetsSettings,
            int indentLevel
        )
        {
            if (!ShouldLog(propertyPath, targetsSettings))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}DrawPendingEntryUI: path={propertyPath}, targetsSettings={targetsSettings}, "
                    + $"indentLevel={indentLevel}, pendingY={pendingY:F2}, position={FormatRect(position)}"
            );
        }

        internal static void LogListDoList(
            string propertyPath,
            Rect listRect,
            bool targetsSettings,
            int indentLevelBefore,
            int indentLevelDuring
        )
        {
            if (!ShouldLog(propertyPath, targetsSettings))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}DoList: path={propertyPath}, targetsSettings={targetsSettings}, "
                    + $"indentBefore={indentLevelBefore}, indentDuring={indentLevelDuring}, "
                    + $"listRect={FormatRect(listRect)}"
            );
        }

        internal static void LogGroupPaddingState(string context)
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}GroupPadding ({context}): "
                    + $"left={GroupGUIWidthUtility.CurrentLeftPadding:F2}, "
                    + $"right={GroupGUIWidthUtility.CurrentRightPadding:F2}, "
                    + $"total={GroupGUIWidthUtility.CurrentHorizontalPadding:F2}, "
                    + $"depth={GroupGUIWidthUtility.CurrentScopeDepth}"
            );
        }

        internal static void LogDrawRowElement(
            string propertyPath,
            int index,
            int globalIndex,
            Rect rect,
            float keyWidth,
            float valueWidth,
            bool targetsSettings
        )
        {
            if (!ShouldLog(propertyPath, targetsSettings))
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}DrawRow: path={propertyPath}, index={index}, globalIdx={globalIndex}, "
                    + $"rect={FormatRect(rect)}, keyW={keyWidth:F2}, valueW={valueWidth:F2}"
            );
        }

        private static string FormatRect(Rect r)
        {
            return $"(x={r.x:F1}, y={r.y:F1}, w={r.width:F1}, h={r.height:F1})";
        }
    }

    [CustomPropertyDrawer(typeof(SerializableDictionary<,>), true)]
    [CustomPropertyDrawer(typeof(SerializableSortedDictionary<,>), true)]
    [CustomPropertyDrawer(typeof(SerializableSortedDictionary<,,>), true)]
    public sealed class SerializableDictionaryPropertyDrawer : PropertyDrawer
    {
        private readonly Dictionary<string, ReorderableList> _lists = new();
        private readonly Dictionary<string, PendingEntry> _pendingEntries = new();
        private readonly Dictionary<string, PaginationState> _paginationStates = new();
        private readonly Dictionary<string, ListPageCache> _pageCaches = new();
        private readonly Dictionary<string, KeyIndexCache> _keyIndexCaches = new();
        private readonly Dictionary<string, DuplicateKeyState> _duplicateStates = new();
        private readonly Dictionary<string, NullKeyState> _nullKeyStates = new();
        private readonly Dictionary<RowFoldoutKey, bool> _rowValueFoldoutStates = new();
        private readonly Dictionary<string, Type> _valueTypes = new();
        private readonly Dictionary<string, int> _sortedOrderHashes = new();
        private readonly Dictionary<string, HeightCacheEntry> _heightCache = new();
        private readonly Dictionary<RowRenderKey, RowRenderData> _rowRenderCache = new();
        private readonly Dictionary<string, CachedPropertyPair> _cachedPropertyPairs = new();
        private readonly HashSet<string> _primedFoldoutCaches = new();

        private string _cachedPropertyPath;
        private string _cachedListKey;
        private SerializedObject _cachedSerializedObject;
        private int _lastDuplicateRefreshFrame = -1;
        private int _lastNullKeyRefreshFrame = -1;
        private int _lastRowRenderCacheFrame = -1;
        private int _lastPropertyPairCacheFrame = -1;
        private int _lastPrimedFoldoutFrame = -1;

        private sealed class CachedPropertyPair
        {
            public SerializedProperty keysProperty;
            public SerializedProperty valuesProperty;
        }

        private sealed class HeightCacheEntry
        {
            public float height;
            public int arraySize;
            public int pageIndex;
            public bool isExpanded;
            public bool hasNullKeys;
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
            public SerializedProperty keyProperty;
            public SerializedProperty valueProperty;
            public float rowHeight;
            public float keyHeight;
            public float valueHeight;
            public bool valueSupportsFoldout;
            public bool isValid;
        }

        internal Rect LastResolvedPosition { get; private set; }
        internal Rect LastListRect { get; private set; }
        internal bool HasLastListRect { get; private set; }

        // Tracks whether the current OnGUI call was initiated inside a WGroup context.
        // Used to apply custom backgrounds over Unity's default ReorderableList styling.
        private bool _currentDrawInsideWGroup;

        private static readonly BindingFlags ReflectionBindingFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly char[] PropertyPathSeparators = { '.' };

        private const float PendingSectionPadding = 6f;
        private const float PendingSectionPaddingProjectSettings = 2f;
        internal const float PendingFoldoutToggleOffset = 17.5f;
        internal const float PendingFoldoutToggleOffsetProjectSettings = 7.5f;
        internal const float PendingFoldoutLabelPadding = 0f;
        internal const float PendingFoldoutLabelContentOffset = -3f;
        private const float PendingFoldoutInspectorLabelShift = 2.5f;
        internal const float WGroupFoldoutAlignmentOffset = 2.5f;
        internal const float DictionaryRowFieldPadding = 4f;
        internal const float DictionaryRowKeyColumnMinWidth = 110f;
        internal const float DictionaryRowValueColumnMinWidth = 150f;
        internal const float DictionaryRowComplexValueMinWidth = 230f;
        internal const float DictionaryRowKeyValueGap = 8f;
        internal const float DictionaryRowFoldoutGapBoost = 4f;
        private const float DictionaryRowSimpleValueWidthRatio = 0.54f;
        private const float DictionaryRowComplexValueWidthRatio = 0.64f;
        private const float DictionaryRowChildLabelWidthRatio = 0.3f;
        private const float DictionaryRowChildLabelWidthMin = 32f;
        private const float DictionaryRowChildLabelWidthMax = 96f;
        private const float DictionaryRowChildHorizontalPadding = 2f;
        private const float DictionaryRowChildLabelTextPadding = 6f;
        internal const float PendingFieldLabelWidth = 72f;
        internal const float PendingValueContentLeftShift = 8.5f;
        internal const float PendingFoldoutValueLeftShiftReduction = 3f;
        internal const float PendingFoldoutValueRightShift = 5.5f;
        internal const float RowValueFoldoutLabelWidth = 2f;
        internal const float ExpandableValueFoldoutLabelWidth = 16f;
        internal const float PendingExpandableValueFoldoutGutter = 7f;
        internal const float RowExpandableValueFoldoutGutter = 24f;
        private const float PendingAddButtonWidth = 110f;
        private const int DefaultPageSize =
            UnityHelpersSettings.DefaultSerializableDictionaryPageSize;

        private const int MaxPageSize = UnityHelpersSettings.MaxSerializableDictionaryPageSize;
        private const int DuplicateSummaryDisplayLimit = 5;
        private const float PaginationButtonWidth = 28f;
        private const float PaginationLabelWidth = 80f;
        private const float PaginationControlSpacing = 4f;
        private const float DuplicateShakeAmplitude = 2f;
        private const float DuplicateShakeFrequency = 7f;
        private const float DuplicateBorderThickness = 1f;
        private static readonly Color LightRowColor = new(0.97f, 0.97f, 0.97f, 1f);
        private static readonly Color DarkRowColor = new(0.16f, 0.16f, 0.16f, 0.45f);

        // Opaque versions for header/footer backgrounds to fully cover Unity's default styling
        private static readonly Color LightHeaderColor = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color DarkHeaderColor = new(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color LightSelectionColor = new(0.33f, 0.62f, 0.95f, 0.65f);
        private static readonly Color DarkSelectionColor = new(0.2f, 0.45f, 0.85f, 0.7f);
        private static readonly ConcurrentDictionary<
            Type,
            Func<object>
        > ParameterlessConstructorCache = new();
        private static readonly ConcurrentDictionary<Type, byte> UnsupportedParameterlessTypes =
            new();
        private static readonly Color DuplicatePrimaryColor = new(0.99f, 0.82f, 0.35f, 0.55f);
        private static readonly Color DuplicateSecondaryColor = new(0.96f, 0.45f, 0.45f, 0.65f);
        private static readonly Color DuplicateOutlineColor = new(0.65f, 0.18f, 0.18f, 0.9f);
        private static readonly Color NullKeyHighlightColor = new(0.84f, 0.2f, 0.2f, 0.6f);
        private static readonly Dictionary<Type, PaletteValueRenderer> PaletteValueRenderers =
            BuildPaletteValueRenderers();
        private static readonly GUIContent DuplicateTooltipContent = new();
        private static readonly GUIContent DuplicateIconContentCache = new();
        private static readonly GUIContent NullKeyTooltipContent = new();
        private static readonly GUIContent NullKeyIconContentCache = new();

        // Lazy initialization to avoid calling EditorGUIUtility during static class loading,
        // which can hang Unity during "Open Project: Open Scene" if the class is accessed
        // before EditorGUIUtility is fully initialized.
        private static GUIContent _duplicateIconTemplate;
        private static GUIContent _pendingFoldoutContent;
        private static GUIContent _paginationPrevContent;
        private static GUIContent _paginationNextContent;
        private static GUIContent _paginationFirstContent;
        private static GUIContent _paginationLastContent;

        private static GUIContent DuplicateIconTemplate =>
            _duplicateIconTemplate ??= EditorGUIUtility.IconContent("console.warnicon.sml");

        private static GUIContent PendingFoldoutContent =>
            _pendingFoldoutContent ??= EditorGUIUtility.TrTextContent("New Entry");

        private static readonly GUIContent FoldoutLabelContent = new();
        private static readonly GUIContent PaginationPageLabelContent = new();
        private static readonly GUIContent PaginationRangeContent = new();
        private static readonly GUIContent UnsupportedTypeContent = new();
        private static readonly GUIContent SuppressedFieldValueContent = new();
        private static readonly Dictionary<Type, string> UnsupportedTypeMessageCache = new();
        private static readonly Dictionary<(int, int, int), string> RangeLabelCache = new();

        private static GUIContent PaginationPrevContent =>
            _paginationPrevContent ??= EditorGUIUtility.TrTextContent("<", "Previous page");

        private static GUIContent PaginationNextContent =>
            _paginationNextContent ??= EditorGUIUtility.TrTextContent(">", "Next page");

        private static GUIContent PaginationFirstContent =>
            _paginationFirstContent ??= EditorGUIUtility.TrTextContent("<<", "Jump to first page");

        private static GUIContent PaginationLastContent =>
            _paginationLastContent ??= EditorGUIUtility.TrTextContent(">>", "Jump to last page");
        private static GUIStyle _footerLabelStyle;
        private static GUIStyle _pendingFoldoutLabelStyle;
        private static GUIStyle _rowChildLabelStyle;
        private static readonly GUIContent FoldoutSpacerLabel = new(" ");
        private static readonly GUIContent RowChildLabelContent = new();
        private static readonly GUIContent ReusableFieldLabelContent = new();
        private static readonly object NullKeySentinel = new();
        private static readonly HashSet<Type> PendingWrapperNonEditableTypes = new();
        private static readonly ReorderableList.DrawNoneElementCallback EmptyDrawNoneCallback =
            static _ => { };

        private const HideFlags PendingWrapperHideFlags =
            HideFlags.HideInHierarchy
            | HideFlags.HideInInspector
            | HideFlags.DontSaveInEditor
            | HideFlags.DontSaveInBuild
            | HideFlags.DontUnloadUnusedAsset;

        internal static bool HasLastPendingHeaderRect { get; private set; }
        internal static Rect LastPendingHeaderRect { get; private set; }
        internal static Rect LastPendingFoldoutToggleRect { get; private set; }
        internal static Rect LastPendingLabelHitRect { get; private set; }
        internal static Rect LastPendingAbsoluteLabelHitRect { get; private set; }
        internal static bool HasLastMainFoldoutRect { get; private set; }
        internal static Rect LastMainFoldoutRect { get; private set; }
        internal static bool HasLastPendingFieldRects { get; private set; }
        internal static Rect LastPendingKeyFieldRect { get; private set; }
        internal static Rect LastPendingValueFieldRect { get; private set; }
        internal static bool HasLastRowRects { get; private set; }
        internal static Rect LastRowOriginalRect { get; private set; }
        internal static Rect LastRowKeyRect { get; private set; }
        internal static Rect LastRowValueRect { get; private set; }
        internal static float LastRowValueBaseX { get; private set; }
        internal static bool LastPendingValueUsedFoldoutLabel { get; private set; }
        internal static bool LastRowValueUsedFoldoutLabel { get; private set; }
        internal static float LastPendingValueFoldoutOffset { get; private set; }
        internal static float LastRowValueFoldoutOffset { get; private set; }
        internal static bool HasLastRowChildContentRect { get; private set; }
        internal static Rect LastRowChildContentRect { get; private set; }
        internal static bool HasRowChildLabelWidthData { get; private set; }
        internal static float LastRowChildMinLabelWidth { get; private set; }
        internal static float LastRowChildMaxLabelWidth { get; private set; }

        /// <summary>
        /// Frame number when a child property drawer signaled that its height changed.
        /// When this matches the current frame, the row render cache should be invalidated.
        /// </summary>
        private static int _childHeightChangedFrame = -1;

        /// <summary>
        /// Signals that a child property drawer's height has changed and the parent
        /// SerializableDictionaryPropertyDrawer should invalidate its row height cache.
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

        internal static void ResetLayoutTrackingForTests()
        {
            HasLastPendingHeaderRect = false;
            LastPendingHeaderRect = default;
            LastPendingFoldoutToggleRect = default;
            LastPendingLabelHitRect = default;
            LastPendingAbsoluteLabelHitRect = default;
            HasLastPendingFieldRects = false;
            HasLastMainFoldoutRect = false;
            LastMainFoldoutRect = default;
            LastPendingValueUsedFoldoutLabel = false;
            LastRowValueUsedFoldoutLabel = false;
            LastPendingValueFoldoutOffset = 0f;
            LastRowValueFoldoutOffset = 0f;
            HasLastRowRects = false;
            HasLastRowChildContentRect = false;
            HasRowChildLabelWidthData = false;
            LastRowChildMinLabelWidth = 0f;
            LastRowChildMaxLabelWidth = 0f;
            LastRowChildContentRect = default;
            LastPendingKeyFieldRect = default;
            LastPendingValueFieldRect = default;
        }

        /// <summary>
        /// Returns the resolved pending section padding value for test diagnostics.
        /// </summary>
        internal static float GetPendingSectionPaddingForTests()
        {
            return PendingSectionPadding;
        }

        /// <summary>
        /// Draws the expandable dictionary inspector UI, including toolbar actions and inline key/value editing.
        /// </summary>
        /// <param name="position">The rectangle reserved by Unity.</param>
        /// <param name="property">The serialized dictionary being edited.</param>
        /// <param name="label">The label displayed for the field.</param>
        /// <example>
        /// <code>
        /// EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(inventory)));
        /// </code>
        /// </example>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            OnGUIInternal(position, property, label);
        }

        private void OnGUIInternal(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect originalPosition = position;
            bool targetsSettings = TargetsUnityHelpersSettings(property?.serializedObject);

            // Track if we need custom backgrounds to override Unity's tinted defaults.
            _currentDrawInsideWGroup = GroupGUIWidthUtility.CurrentScopeDepth > 0;

            SerializableDictionaryIndentDiagnostics.LogOnGUIEntry(
                originalPosition,
                property,
                targetsSettings,
                EditorGUI.indentLevel
            );
            SerializableDictionaryIndentDiagnostics.LogGroupPaddingState("OnGUI entry");

            Rect contentPosition = ResolveContentRect(originalPosition, targetsSettings);
            LastResolvedPosition = contentPosition;
            HasLastListRect = false;

            EditorGUI.BeginProperty(originalPosition, label, property);
            int previousIndentLevel = EditorGUI.indentLevel;

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

                string cacheKey = GetListKey(property);
                CachedPropertyPair propertyPair = GetOrCreateCachedPropertyPair(cacheKey, property);
                SerializedProperty keysProperty = propertyPair.keysProperty;
                SerializedProperty valuesProperty = propertyPair.valuesProperty;
                EnsureParallelArraySizes(keysProperty, valuesProperty);

                bool resolvedTypes = TryResolveKeyValueTypes(
                    fieldInfo,
                    out Type keyType,
                    out Type valueType,
                    out bool isSortedDictionary
                );

                if (!resolvedTypes || keyType == null || valueType == null)
                {
                    object dictionaryInstance = GetDictionaryInstance(property);
                    if (
                        TryResolveKeyValueTypesFromInstance(
                            dictionaryInstance,
                            out Type runtimeKeyType,
                            out Type runtimeValueType,
                            out bool runtimeSorted
                        )
                    )
                    {
                        keyType ??= runtimeKeyType;
                        valueType ??= runtimeValueType;
                        isSortedDictionary = runtimeSorted;
                        resolvedTypes = keyType != null && valueType != null;
                    }
                }

                if (!resolvedTypes)
                {
                    EditorGUI.LabelField(position, label.text, "Unsupported dictionary type");
                    return;
                }

                CacheValueType(cacheKey, valueType);
                DuplicateKeyState duplicateState = RefreshDuplicateState(
                    cacheKey,
                    keysProperty,
                    keyType
                );
                NullKeyState nullKeyState = RefreshNullKeyState(cacheKey, keysProperty, keyType);

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
                        WSerializableCollectionFoldoutUtility.SerializableCollectionType.Dictionary
                    );
                }

                property.isExpanded = EditorGUI.Foldout(
                    foldoutRect,
                    property.isExpanded,
                    label,
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
                    isSortedDictionary
                );

                float iconSize = EditorGUIUtility.singleLineHeight;
                float iconPadding = 4f;
                float nextIconX = Mathf.Max(position.x, position.xMax - iconSize - iconPadding);

                if (duplicateState is { HasDuplicates: true })
                {
                    Rect iconRect = new(nextIconX, foldoutRect.y, iconSize, iconSize);
                    GUI.Label(iconRect, GetDuplicateIconContent(duplicateState.SummaryTooltip));
                    nextIconX -= iconSize + iconPadding;
                }

                if (nullKeyState is { HasNullKeys: true })
                {
                    float clampedX = Mathf.Max(position.x, nextIconX);
                    Rect iconRect = new(clampedX, foldoutRect.y, iconSize, iconSize);
                    GUI.Label(iconRect, GetNullKeyIconContent(nullKeyState.WarningMessage));
                }

                float y = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;

                // During collapse, hide content early and fade faster to avoid visual confusion
                // where large dictionaries' contents "stick around" longer than surrounding elements
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
                if (shouldDrawContent)
                {
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
                        float warningHeight = GetWarningBarHeight();

                        if (nullKeyState is { HasNullKeys: true })
                        {
                            Rect warningRect = new(position.x, y, position.width, warningHeight);
                            EditorGUI.HelpBox(
                                warningRect,
                                nullKeyState.WarningMessage,
                                MessageType.Warning
                            );
                            y = warningRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                        }

                        if (
                            duplicateState is { HasDuplicates: true }
                            && !string.IsNullOrEmpty(duplicateState.SummaryTooltip)
                        )
                        {
                            Rect warningRect = new(position.x, y, position.width, warningHeight);
                            EditorGUI.HelpBox(
                                warningRect,
                                duplicateState.SummaryTooltip,
                                MessageType.Warning
                            );
                            y = warningRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                        }

                        ReorderableList list = GetOrCreateList(property);
                        PaginationState pagination = GetOrCreatePaginationState(property);
                        PendingEntry pending = GetOrCreatePendingEntry(
                            property,
                            keyType,
                            valueType,
                            isSortedDictionary
                        );

                        float pendingY = y;
                        SerializableDictionaryIndentDiagnostics.LogDrawPendingEntryUI(
                            property.propertyPath,
                            position,
                            pendingY,
                            targetsSettings,
                            EditorGUI.indentLevel
                        );
                        DrawPendingEntryUI(
                            ref pendingY,
                            position,
                            pending,
                            list,
                            pagination,
                            property,
                            keysProperty,
                            valuesProperty,
                            keyType,
                            valueType
                        );
                        y = pendingY + EditorGUIUtility.standardVerticalSpacing;

                        Rect listRect = new(position.x, y, position.width, list.GetHeight());
                        LastListRect = listRect;
                        HasLastListRect = true;

                        // Draw custom list container background when inside WGroup to respect theming
                        // This draws before Unity's default ReorderableList backgrounds
                        // Use opaque colors to fully cover Unity's default styling
                        if (_currentDrawInsideWGroup && Event.current.type == EventType.Repaint)
                        {
                            Color listBgColor = EditorGUIUtility.isProSkin
                                ? DarkHeaderColor
                                : LightHeaderColor;
                            // Temporarily reset GUI.color to prevent tinting from parent WGroup
                            Color prevGuiColor = GUI.color;
                            GUI.color = Color.white;
                            EditorGUI.DrawRect(listRect, listBgColor);
                            GUI.color = prevGuiColor;
                        }

                        int previousIndent = EditorGUI.indentLevel;
                        SerializableDictionaryIndentDiagnostics.LogListDoList(
                            property.propertyPath,
                            listRect,
                            targetsSettings,
                            previousIndent,
                            0
                        );
                        EditorGUI.indentLevel = 0;

                        list.DoList(listRect);

                        EditorGUI.indentLevel = previousIndent;
                    }
                    finally
                    {
                        if (adjustAlpha)
                        {
                            GUI.color = previousColor;
                        }
                    }
                }

                bool applied = ApplyModifiedPropertiesWithUndoFallback(
                    serializedObject,
                    property,
                    "OnGUI::ListApply"
                );
                if (applied)
                {
                    SyncRuntimeDictionary(property);
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
                EditorGUI.EndProperty();
            }
        }

        private static Rect ResolveContentRect(Rect position, bool skipIndentation = false)
        {
            float leftPadding = GroupGUIWidthUtility.CurrentLeftPadding;
            float rightPadding = GroupGUIWidthUtility.CurrentRightPadding;
            int scopeDepth = GroupGUIWidthUtility.CurrentScopeDepth;
            int indentLevel = EditorGUI.indentLevel;
            bool isInsideWGroupProperty = GroupGUIWidthUtility.IsInsideWGroupPropertyDraw;
            Rect original = position;

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
                SerializableDictionaryIndentDiagnostics.LogResolveContentRect(
                    original,
                    alignedPosition,
                    skipIndentation,
                    leftPadding,
                    rightPadding,
                    scopeDepth,
                    indentLevel
                );
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

                SerializableDictionaryIndentDiagnostics.LogResolveContentRect(
                    original,
                    result,
                    skipIndentation,
                    leftPadding,
                    rightPadding,
                    scopeDepth,
                    indentLevel
                );
                return result;
            }

            // Normal context (outside WGroup): apply WGroup padding ourselves
            Rect padded2 = GroupGUIWidthUtility.ApplyCurrentPadding(position);
            if (
                (leftPadding > 0f || rightPadding > 0f)
                && Mathf.Approximately(padded2.xMin, position.xMin)
                && Mathf.Approximately(padded2.width, position.width)
            )
            {
                padded2.xMin += leftPadding;
                padded2.xMax -= rightPadding;
                if (padded2.width < 0f || float.IsNaN(padded2.width))
                {
                    padded2.width = 0f;
                }
            }

            // Skip EditorGUI.IndentedRect when indentLevel is 0 to ensure consistent behavior
            // across all Unity versions. In some versions, IndentedRect unexpectedly modifies
            // the rect width at level 0 (shifts xMax left by ~1.25), while in other versions
            // it returns the rect unchanged. By skipping the call at level 0, we handle both
            // cases consistently. Our own alignment offset is applied below for level 0.
            Rect indentedResult = indentLevel > 0 ? EditorGUI.IndentedRect(padded2) : padded2;

            // Clamp width to non-negative after IndentedRect (high indent levels can cause negative width)
            if (indentedResult.width < 0f || float.IsNaN(indentedResult.width))
            {
                indentedResult.width = 0f;
            }

            Rect final = indentedResult;

            // Only apply Unity list alignment offset when truly outside a WGroup context
            // (scopeDepth == 0 means no WGroup padding is active)
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

            SerializableDictionaryIndentDiagnostics.LogResolveContentRectSteps(
                original,
                padded2,
                indentedResult,
                final,
                skipIndentation,
                leftPadding,
                rightPadding,
                indentLevel
            );

            return final;
        }

        private static Rect ConvertGroupRectToAbsolute(Rect rect, Rect groupRect)
        {
            return new Rect(rect.x + groupRect.x, rect.y + groupRect.y, rect.width, rect.height);
        }

        private static EventType GetEffectiveMouseEventType(Event currentEvent)
        {
            if (currentEvent == null)
            {
                return EventType.Ignore;
            }

            return GetEffectiveMouseEventType(currentEvent.type, currentEvent.rawType);
        }

        internal static EventType GetEffectiveMouseEventTypeForTests(
            EventType eventType,
            EventType rawEventType
        )
        {
            return GetEffectiveMouseEventType(eventType, rawEventType);
        }

        private static EventType GetEffectiveMouseEventType(
            EventType eventType,
            EventType rawEventType
        )
        {
            if (eventType == EventType.Used)
            {
                return rawEventType;
            }

            return eventType;
        }

        internal static bool TryTogglePendingFoldoutLabelForTests(
            Event currentEvent,
            Rect labelHitRect,
            Rect absoluteLabelHitRect,
            ref bool expanded
        )
        {
            return TryTogglePendingFoldoutLabel(
                currentEvent,
                labelHitRect,
                absoluteLabelHitRect,
                ref expanded
            );
        }

        private static bool TryTogglePendingFoldoutLabel(
            Event currentEvent,
            Rect labelHitRect,
            Rect absoluteLabelHitRect,
            ref bool expanded
        )
        {
            if (currentEvent == null)
            {
                return false;
            }

            EventType effectiveEventType = GetEffectiveMouseEventType(currentEvent);
            bool mouseInLabelRect =
                labelHitRect.Contains(currentEvent.mousePosition)
                || absoluteLabelHitRect.Contains(currentEvent.mousePosition);
            if (
                effectiveEventType != EventType.MouseDown
                || currentEvent.button != 0
                || !mouseInLabelRect
            )
            {
                return false;
            }

            expanded = !expanded;
            GUI.changed = true;
            if (currentEvent.type != EventType.Used)
            {
                currentEvent.Use();
            }

            return true;
        }

        /// <summary>
        /// Calculates how much vertical space is required to render the dictionary, including any expanded entries and validation messages.
        /// </summary>
        /// <param name="property">The serialized dictionary wrapper.</param>
        /// <param name="label">The label shown for the field.</param>
        /// <returns>The height Unity should reserve for the drawer.</returns>
        /// <example>
        /// <code>
        /// float height = drawer.GetPropertyHeight(property, GUIContent.none);
        /// </code>
        /// </example>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUIUtility.singleLineHeight;

            // Resolve types early to determine if sorted dictionary (affects animation settings)
            bool resolvedTypes = TryResolveKeyValueTypes(
                fieldInfo,
                out Type keyType,
                out Type valueType,
                out bool isSortedDictionary
            );

            if (!resolvedTypes || keyType == null || valueType == null)
            {
                object dictionaryInstance = GetDictionaryInstance(property);
                if (
                    TryResolveKeyValueTypesFromInstance(
                        dictionaryInstance,
                        out Type runtimeKeyType,
                        out Type runtimeValueType,
                        out bool runtimeSorted
                    )
                )
                {
                    keyType ??= runtimeKeyType;
                    valueType ??= runtimeValueType;
                    isSortedDictionary = runtimeSorted;
                    resolvedTypes = keyType != null && valueType != null;
                }
            }

            if (!resolvedTypes)
            {
                return baseHeight;
            }

            // Get main foldout animation progress
            float mainFoldoutProgress = GetMainFoldoutProgress(
                property.serializedObject,
                property.propertyPath,
                property.isExpanded,
                isSortedDictionary
            );

            // If fully collapsed (animation complete), return only header height
            if (mainFoldoutProgress <= 0f)
            {
                return baseHeight;
            }

            string cacheKey = GetListKey(property);
            CachedPropertyPair propertyPair = GetOrCreateCachedPropertyPair(cacheKey, property);
            SerializedProperty keysProperty = propertyPair.keysProperty;
            SerializedProperty valuesProperty = propertyPair.valuesProperty;
            EnsureParallelArraySizes(keysProperty, valuesProperty);

            CacheValueType(cacheKey, valueType);

            int currentFrame = Time.frameCount;
            int arraySize = keysProperty.arraySize;

            PaginationState pagination = GetOrCreatePaginationState(property);
            ClampPaginationState(pagination, arraySize);
            int pageIndex = pagination.pageIndex;

            DuplicateKeyState duplicateState = RefreshDuplicateState(
                cacheKey,
                keysProperty,
                keyType
            );
            NullKeyState nullKeyState = RefreshNullKeyState(cacheKey, keysProperty, keyType);

            bool hasNullKeys = nullKeyState is { HasNullKeys: true };
            bool hasDuplicates =
                duplicateState is { HasDuplicates: true }
                && !string.IsNullOrEmpty(duplicateState.SummaryTooltip);

            PendingEntry pending = GetOrCreatePendingEntry(
                property,
                keyType,
                valueType,
                isSortedDictionary
            );
            bool pendingIsExpanded = pending.isExpanded;
            float pendingFoldoutProgress = GetPendingFoldoutProgress(pending);

            // Skip cache if a child drawer signaled height change this frame
            bool childHeightChangedThisFrame = _childHeightChangedFrame == currentFrame;

            if (
                !childHeightChangedThisFrame
                && _heightCache.TryGetValue(cacheKey, out HeightCacheEntry cached)
            )
            {
                const float foldoutProgressTolerance = 0.001f;
                if (
                    cached.frameNumber == currentFrame
                    && cached.arraySize == arraySize
                    && cached.pageIndex == pageIndex
                    && cached.isExpanded == property.isExpanded
                    && cached.hasNullKeys == hasNullKeys
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
            else
            {
                cached = default;
            }

            // Calculate the expanded content height (everything after the header)
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float expandedContentHeight = spacing;

            float warningHeight = GetWarningBarHeight();
            if (hasNullKeys)
            {
                expandedContentHeight += warningHeight + spacing;
            }

            if (hasDuplicates)
            {
                expandedContentHeight += warningHeight + spacing;
            }

            float pendingHeight = GetPendingSectionHeight(pending, keyType, valueType, property);
            expandedContentHeight += pendingHeight + spacing;

            ReorderableList list = GetOrCreateList(property);
            float listHeight = list.GetHeight();
            expandedContentHeight += listHeight;

            // Interpolate height based on main foldout animation progress
            float height = baseHeight + (expandedContentHeight * mainFoldoutProgress);

            if (cached == null)
            {
                cached = new HeightCacheEntry();
                _heightCache[cacheKey] = cached;
            }
            cached.height = height;
            cached.arraySize = arraySize;
            cached.pageIndex = pageIndex;
            cached.isExpanded = property.isExpanded;
            cached.hasNullKeys = hasNullKeys;
            cached.hasDuplicates = hasDuplicates;
            cached.pendingIsExpanded = pendingIsExpanded;
            cached.pendingFoldoutProgress = pendingFoldoutProgress;
            cached.mainFoldoutProgress = mainFoldoutProgress;
            cached.frameNumber = currentFrame;

            return height;
        }

        internal ReorderableList GetOrCreateList(SerializedProperty dictionaryProperty)
        {
            string key = GetListKey(dictionaryProperty);
            Type resolvedValueType = EnsureValueTypeCached(key, dictionaryProperty);
            PaginationState pagination = GetOrCreatePaginationState(dictionaryProperty);

            CachedPropertyPair propertyPair = GetOrCreateCachedPropertyPair(
                key,
                dictionaryProperty
            );
            SerializedProperty keysProperty = propertyPair.keysProperty;
            SerializedProperty valuesProperty = propertyPair.valuesProperty;
            ClampPaginationState(pagination, keysProperty.arraySize);
            float defaultRowHeight =
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2f;
            float emptyHeight = Mathf.Max(
                EditorGUIUtility.standardVerticalSpacing * 2f,
                EditorGUIUtility.standardVerticalSpacing
            );

            Func<ListPageCache> cacheProvider = () =>
            {
                CachedPropertyPair cachedPair = GetOrCreateCachedPropertyPair(
                    key,
                    dictionaryProperty
                );
                ListPageCache pageCache = EnsurePageCache(key, cachedPair.keysProperty, pagination);
                PrimeRowFoldoutStates(key, pageCache, cachedPair.valuesProperty, resolvedValueType);
                return pageCache;
            };

            ListPageCache cache = cacheProvider();

            if (_lists.TryGetValue(key, out ReorderableList cached))
            {
                cached.list = cache.entries;
                SyncListSelectionWithPagination(cached, pagination, cache);
                cached.drawNoneElementCallback = EmptyDrawNoneCallback;
                cached.elementHeight = keysProperty.arraySize == 0 ? emptyHeight : defaultRowHeight;
                cached.elementHeightCallback = ResolveRowHeight;
                return cached;
            }

            ReorderableList list = new(
                cache.entries,
                typeof(PageEntry),
                draggable: true,
                displayHeader: true,
                displayAddButton: false,
                displayRemoveButton: false
            )
            {
                elementHeight = keysProperty.arraySize == 0 ? emptyHeight : defaultRowHeight,
            };

            list.drawHeaderCallback = rect =>
            {
                ListPageCache currentCache = cacheProvider();
                SyncListSelectionWithPagination(list, pagination, currentCache);
                CachedPropertyPair headerPair = GetOrCreateCachedPropertyPair(
                    key,
                    dictionaryProperty
                );
                DrawListHeader(rect, headerPair.keysProperty, list, pagination, cacheProvider);
            };

            list.elementHeightCallback = ResolveRowHeight;
            list.drawNoneElementCallback = EmptyDrawNoneCallback;

            list.drawElementBackgroundCallback = (rect, index, _, _) =>
            {
                ListPageCache currentCache = cacheProvider();
                if (!RelativeIndexIsValid(currentCache, index))
                {
                    return;
                }

                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }

                int globalIndex = currentCache.entries[index].arrayIndex;
                CachedPropertyPair bgPair = GetOrCreateCachedPropertyPair(key, dictionaryProperty);

                RowRenderData rowData = GetOrCreateRowRenderData(
                    key,
                    globalIndex,
                    bgPair.keysProperty,
                    bgPair.valuesProperty,
                    resolvedValueType
                );

                if (!rowData.isValid)
                {
                    return;
                }

                Rect backgroundRect = new(rect.x, rect.y, rect.width, rowData.rowHeight);
                // Use skin-based row color
                Color rowColor = EditorGUIUtility.isProSkin ? DarkRowColor : LightRowColor;
                // Temporarily reset GUI.color to prevent tinting from parent WGroup
                Color prevGuiColor = GUI.color;
                GUI.color = Color.white;
                EditorGUI.DrawRect(backgroundRect, rowColor);
                GUI.color = prevGuiColor;

                bool hasDuplicate = TryGetDuplicateInfo(
                    key,
                    globalIndex,
                    out DuplicateKeyInfo duplicateInfo,
                    out DuplicateKeyState duplicateState
                );
                bool hasNullKey = TryGetNullKeyInfo(key, globalIndex, out NullKeyInfo nullKeyInfo);

                UnityHelpersSettings.DuplicateRowAnimationMode animationMode =
                    UnityHelpersSettings.GetDuplicateRowAnimationMode();
                bool highlightDuplicates =
#pragma warning disable CS0618 // Type or member is obsolete
                    animationMode != UnityHelpersSettings.DuplicateRowAnimationMode.None;
#pragma warning restore CS0618 // Type or member is obsolete
                bool animateDuplicates =
                    animationMode == UnityHelpersSettings.DuplicateRowAnimationMode.Tween;
                int tweenCycleLimit = UnityHelpersSettings.GetDuplicateRowTweenCycleLimit();
                double currentTime = animateDuplicates ? GetHighResolutionTime() : 0d;

                float shakeOffset = 0f;
                if (hasDuplicate && animateDuplicates && duplicateState != null)
                {
                    shakeOffset = duplicateState.GetAnimationOffset(
                        globalIndex,
                        currentTime,
                        tweenCycleLimit
                    );
                }

                Rect highlightRect = ExpandDictionaryRowRect(backgroundRect);
                Rect animatedRect = highlightRect;
                animatedRect.x += shakeOffset;

                Rect insetHighlightRect = animatedRect;
                insetHighlightRect.xMin += 1f;
                insetHighlightRect.xMax -= 1f;
                insetHighlightRect.yMin += 1f;
                insetHighlightRect.yMax -= 1f;
                insetHighlightRect.height = Mathf.Max(0f, insetHighlightRect.height);

                if (hasDuplicate)
                {
                    if (highlightDuplicates)
                    {
                        Color highlightColor = duplicateInfo.isPrimary
                            ? DuplicatePrimaryColor
                            : DuplicateSecondaryColor;
                        EditorGUI.DrawRect(insetHighlightRect, highlightColor);
                        DrawDuplicateOutline(insetHighlightRect);
                    }

                    DrawDuplicateTooltip(insetHighlightRect, duplicateInfo.tooltip);
                }

                if (hasNullKey)
                {
                    EditorGUI.DrawRect(insetHighlightRect, NullKeyHighlightColor);
                    DrawDuplicateOutline(insetHighlightRect);
                    DrawNullTooltip(insetHighlightRect, nullKeyInfo.tooltip);
                }

                if (list.index == index)
                {
                    Rect selectionRect = highlightRect;
                    selectionRect.x += shakeOffset;
                    // Use skin-based selection color
                    Color selectionColor = EditorGUIUtility.isProSkin
                        ? DarkSelectionColor
                        : LightSelectionColor;
                    // Temporarily reset GUI.color to prevent tinting from parent WGroup
                    prevGuiColor = GUI.color;
                    GUI.color = Color.white;
                    EditorGUI.DrawRect(selectionRect, selectionColor);
                    GUI.color = prevGuiColor;
                }
            };

            list.drawElementCallback = (rect, index, _, _) =>
            {
                ListPageCache currentCache = cacheProvider();
                if (!RelativeIndexIsValid(currentCache, index))
                {
                    return;
                }

                float minContentWidth =
                    DictionaryRowKeyColumnMinWidth
                    + DictionaryRowValueColumnMinWidth
                    + DictionaryRowKeyValueGap
                    + DictionaryRowFieldPadding * 4f;
                if (rect.width < minContentWidth)
                {
                    float fallbackWidth = minContentWidth;
                    if (HasLastListRect && LastListRect.width > 0f)
                    {
                        fallbackWidth = Mathf.Max(fallbackWidth, LastListRect.width);
                    }
                    else if (LastResolvedPosition.width > 0f)
                    {
                        fallbackWidth = Mathf.Max(fallbackWidth, LastResolvedPosition.width);
                    }

                    rect.width = fallbackWidth;
                }

                int globalIndex = currentCache.entries[index].arrayIndex;
                CachedPropertyPair elementPair = GetOrCreateCachedPropertyPair(
                    key,
                    dictionaryProperty
                );

                RowRenderData rowData = GetOrCreateRowRenderData(
                    key,
                    globalIndex,
                    elementPair.keysProperty,
                    elementPair.valuesProperty,
                    resolvedValueType
                );

                if (!rowData.isValid)
                {
                    return;
                }

                SerializedProperty keyProperty = rowData.keyProperty;
                SerializedProperty valueProperty = rowData.valueProperty;

                float spacing = EditorGUIUtility.standardVerticalSpacing;
                bool valueSupportsFoldout = rowData.valueSupportsFoldout;
                RowFoldoutKey rowFoldoutStateKey = default;
                bool hasFoldoutStateKey = false;
                bool rowFoldoutStateChanged = false;
                bool previousExpanded = valueProperty != null && valueProperty.isExpanded;
                if (valueSupportsFoldout && valueProperty != null)
                {
                    rowFoldoutStateKey = BuildRowFoldoutStateKey(key, globalIndex);
                    hasFoldoutStateKey = rowFoldoutStateKey.IsValid;
                    if (hasFoldoutStateKey)
                    {
                        previousExpanded = EnsureRowFoldoutState(
                            rowFoldoutStateKey,
                            valueProperty,
                            out rowFoldoutStateChanged
                        );
                    }
                }
                float keyHeight = rowData.keyHeight;
                float valueHeight = rowData.valueHeight;
                float contentY = rect.y + spacing;
                bool hasDuplicate = TryGetDuplicateInfo(
                    key,
                    globalIndex,
                    out DuplicateKeyInfo duplicateInfo,
                    out DuplicateKeyState duplicateState
                );
                bool hasNullKey = TryGetNullKeyInfo(key, globalIndex, out NullKeyInfo nullKeyInfo);

                UnityHelpersSettings.DuplicateRowAnimationMode animationMode =
                    UnityHelpersSettings.GetDuplicateRowAnimationMode();
                bool animateDuplicates =
                    animationMode == UnityHelpersSettings.DuplicateRowAnimationMode.Tween;
                int tweenCycleLimit = UnityHelpersSettings.GetDuplicateRowTweenCycleLimit();
                double currentTime = animateDuplicates ? GetHighResolutionTime() : 0d;

                if (hasDuplicate && animateDuplicates && duplicateState != null)
                {
                    rect.x += duplicateState.GetAnimationOffset(
                        globalIndex,
                        currentTime,
                        tweenCycleLimit
                    );
                }

                float gap = DictionaryRowKeyValueGap;
                bool complexValue =
                    resolvedValueType != null && TypeSupportsComplexEditing(resolvedValueType);
                if (valueSupportsFoldout)
                {
                    gap += DictionaryRowFoldoutGapBoost;
                }

                float leftPadding = GroupGUIWidthUtility.CurrentLeftPadding;
                float rightPadding = GroupGUIWidthUtility.CurrentRightPadding;
                float horizontalPadding = leftPadding + rightPadding;

                float virtualWidth = rect.width + horizontalPadding;

                bool isSettings =
                    dictionaryProperty?.serializedObject?.targetObject is UnityHelpersSettings;
                SerializableDictionaryIndentDiagnostics.LogDrawRowElement(
                    dictionaryProperty?.propertyPath,
                    index,
                    globalIndex,
                    rect,
                    0f,
                    0f,
                    isSettings
                );
                float availableWidth = Mathf.Max(0f, virtualWidth - gap);
                float minKeyWidth = DictionaryRowKeyColumnMinWidth;
                float minValueWidth = complexValue
                    ? DictionaryRowComplexValueMinWidth
                    : DictionaryRowValueColumnMinWidth;

                float desiredValueWidth = complexValue
                    ? Mathf.Max(availableWidth * DictionaryRowComplexValueWidthRatio, minValueWidth)
                    : Mathf.Max(availableWidth * DictionaryRowSimpleValueWidthRatio, minValueWidth);

                float valueColumnWidth = Mathf.Clamp(
                    desiredValueWidth,
                    minValueWidth,
                    availableWidth
                );
                float keyColumnWidth = Mathf.Max(0f, availableWidth - valueColumnWidth);

                if (keyColumnWidth < minKeyWidth)
                {
                    keyColumnWidth = Mathf.Min(availableWidth, minKeyWidth);
                    valueColumnWidth = Mathf.Max(0f, availableWidth - keyColumnWidth);
                }

                float virtualRowX = rect.x - leftPadding;
                Rect keyRect = new(virtualRowX, contentY, keyColumnWidth, keyHeight);
                Rect valueRect = new(
                    virtualRowX + keyColumnWidth + gap,
                    contentY,
                    valueColumnWidth,
                    valueHeight
                );

                keyRect.x += leftPadding;
                valueRect.x += leftPadding;
                float rowRightEdge = rect.xMax;
                keyRect.width = Mathf.Min(keyRect.width, Mathf.Max(0f, rowRightEdge - keyRect.x));
                valueRect.width = Mathf.Min(
                    valueRect.width,
                    Mathf.Max(0f, rowRightEdge - valueRect.x)
                );

                LastRowOriginalRect = rect;
                LastRowValueBaseX = valueRect.x;

                float rowFieldPadding = DictionaryRowFieldPadding;
                keyRect.x += rowFieldPadding;
                keyRect.width = Mathf.Max(0f, keyRect.width - rowFieldPadding);
                valueRect.x += rowFieldPadding;
                valueRect.width = Mathf.Max(0f, valueRect.width - rowFieldPadding);

                float iconSize = EditorGUIUtility.singleLineHeight;
                float iconSpacing = 3f;
                float consumedWidth = 0f;

                if (hasDuplicate)
                {
                    Rect iconRect = new(keyRect.x + consumedWidth, keyRect.y, iconSize, iconSize);
                    GUIContent iconContent = GetDuplicateIconContent(duplicateInfo.tooltip);
                    GUI.Label(iconRect, iconContent);
                    consumedWidth += iconSize + iconSpacing;
                }

                if (hasNullKey)
                {
                    Rect iconRect = new(keyRect.x + consumedWidth, keyRect.y, iconSize, iconSize);
                    GUIContent iconContent = GetNullKeyIconContent(nullKeyInfo.tooltip);
                    GUI.Label(iconRect, iconContent);
                    consumedWidth += iconSize + iconSpacing;
                }

                if (consumedWidth > 0f)
                {
                    consumedWidth -= iconSpacing;
                    keyRect.x += consumedWidth;
                    keyRect.width -= consumedWidth;
                    if (keyRect.width < 0f)
                    {
                        keyRect.width = 0f;
                    }
                }

                HasLastRowRects = true;
                LastRowKeyRect = keyRect;

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(keyRect, keyProperty, GUIContent.none, true);
                if (EditorGUI.EndChangeCheck())
                {
                    InvalidateKeyCache(key);
                    ApplyAndSyncPaletteRowChange(dictionaryProperty, "KeyFieldChanged");
                }

                GUIContent valueLabel = GUIContent.none;
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                bool restoredLabelWidth = false;
                bool valueChanged = false;
                if (valueSupportsFoldout && valueProperty != null)
                {
                    valueLabel = FoldoutSpacerLabel;
                    EditorGUIUtility.labelWidth = Mathf.Max(RowValueFoldoutLabelWidth, 0f);
                    restoredLabelWidth = true;
                    float foldoutOffset = RowExpandableValueFoldoutGutter;
                    valueRect.x += foldoutOffset;
                    valueRect.width = Mathf.Max(0f, valueRect.width - foldoutOffset);
                    LastRowValueFoldoutOffset = foldoutOffset;
                    valueChanged = DrawRowFoldoutValue(
                        valueRect,
                        valueProperty,
                        valueLabel,
                        valueRect.width,
                        out float renderedValueHeight
                    );
                    valueRect.height = renderedValueHeight;
                }
                else
                {
                    LastRowValueFoldoutOffset = 0f;
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.PropertyField(valueRect, valueProperty, valueLabel, true);
                    valueChanged = EditorGUI.EndChangeCheck();
                }

                // Track the final adjusted value rect after foldout offset is applied
                LastRowValueRect = valueRect;

                if (valueChanged)
                {
                    MarkListCacheDirty(key);
                    ApplyAndSyncPaletteRowChange(dictionaryProperty, "ValueFieldChanged");
                    InvalidatePendingDuplicateCache(key);
                }

                if (restoredLabelWidth)
                {
                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }
                if (rowFoldoutStateChanged)
                {
                    MarkListCacheDirty(key);
                    RequestRepaint();
                }

                if (
                    valueSupportsFoldout
                    && valueProperty != null
                    && hasFoldoutStateKey
                    && valueProperty.isExpanded != previousExpanded
                )
                {
                    _rowValueFoldoutStates[rowFoldoutStateKey] = valueProperty.isExpanded;
                    MarkListCacheDirty(key);
                    RequestRepaint();
                }
                LastRowValueUsedFoldoutLabel = valueSupportsFoldout;
            };

            list.onRemoveCallback = reorderableList =>
            {
                ListPageCache currentCache = cacheProvider();
                if (!RelativeIndexIsValid(currentCache, reorderableList.index))
                {
                    return;
                }

                int globalIndex = currentCache.entries[reorderableList.index].arrayIndex;
                RemoveEntryAtIndex(
                    globalIndex,
                    reorderableList,
                    dictionaryProperty,
                    keysProperty,
                    valuesProperty,
                    pagination
                );
            };

            list.onReorderCallbackWithDetails = (_, oldIndex, _) =>
            {
                ListPageCache currentCache = cacheProvider();
                if (
                    !RelativeIndexIsValid(currentCache, oldIndex)
                    || currentCache.entries.Count == 0
                )
                {
                    return;
                }

                using PooledResource<List<int>> orderedIndicesLease = Buffers<int>.GetList(
                    currentCache.entries.Count,
                    out List<int> orderedIndices
                );
                {
                    foreach (PageEntry entry in currentCache.entries)
                    {
                        orderedIndices.Add(entry.arrayIndex);
                    }

                    int pageSize = Mathf.Max(1, pagination.pageSize);
                    int maxStart = Mathf.Max(0, keysProperty.arraySize - orderedIndices.Count);
                    int pageStart = Mathf.Clamp(pagination.pageIndex * pageSize, 0, maxStart);

                    ApplyDictionarySliceOrder(
                        keysProperty,
                        valuesProperty,
                        orderedIndices,
                        pageStart
                    );
                    int relativeSelection = Mathf.Clamp(list.index, 0, orderedIndices.Count - 1);
                    pagination.selectedIndex = pageStart + relativeSelection;
                    InvalidateKeyCache(key);
                    MarkListCacheDirty(key);

                    ListPageCache refreshedCache = EnsurePageCache(key, keysProperty, pagination);
                    list.list = refreshedCache.entries;
                    SyncListSelectionWithPagination(list, pagination, refreshedCache);
                }
            };

            list.onSelectCallback = reorderableList =>
            {
                if (reorderableList.index >= 0)
                {
                    ListPageCache currentCache = cacheProvider();
                    if (RelativeIndexIsValid(currentCache, reorderableList.index))
                    {
                        pagination.selectedIndex = currentCache
                            .entries[reorderableList.index]
                            .arrayIndex;
                    }
                }
            };

            list.footerHeight =
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 1.5f;
            list.drawFooterCallback = rect =>
            {
                DrawListFooter(
                    rect,
                    list,
                    dictionaryProperty,
                    keysProperty,
                    valuesProperty,
                    pagination,
                    cacheProvider
                );
            };

            SyncListSelectionWithPagination(list, pagination, cache);
            list.elementHeight = keysProperty.arraySize == 0 ? emptyHeight : defaultRowHeight;

            _lists[key] = list;
            return list;

            float ResolveRowHeight(int elementIndex)
            {
                ListPageCache currentCache = cacheProvider();
                if (!RelativeIndexIsValid(currentCache, elementIndex))
                {
                    return defaultRowHeight;
                }

                int globalIndex = currentCache.entries[elementIndex].arrayIndex;
                CachedPropertyPair heightPair = GetOrCreateCachedPropertyPair(
                    key,
                    dictionaryProperty
                );

                RowRenderData rowData = GetOrCreateRowRenderData(
                    key,
                    globalIndex,
                    heightPair.keysProperty,
                    heightPair.valuesProperty,
                    resolvedValueType
                );

                if (!rowData.isValid)
                {
                    return defaultRowHeight;
                }

                if (rowData.valueSupportsFoldout && rowData.valueProperty != null)
                {
                    RowFoldoutKey rowKey = BuildRowFoldoutStateKey(key, globalIndex);
                    if (rowKey.IsValid)
                    {
                        EnsureRowFoldoutState(rowKey, rowData.valueProperty);
                    }
                }

                return rowData.rowHeight;
            }
        }

        internal DuplicateKeyState RefreshDuplicateState(
            string cacheKey,
            SerializedProperty keysProperty,
            Type keyType
        )
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return null;
            }

            int currentFrame = Time.frameCount;
            bool alreadyRefreshedThisFrame = _lastDuplicateRefreshFrame == currentFrame;
            _lastDuplicateRefreshFrame = currentFrame;

            DuplicateKeyState state = _duplicateStates.GetOrAdd(cacheKey);

            if (alreadyRefreshedThisFrame && !state.IsDirty)
            {
                return state;
            }

            bool hasEvent = Event.current != null;
            EventType eventType = hasEvent ? Event.current.type : EventType.Repaint;
            bool shouldRefresh = eventType == EventType.Repaint || state.IsDirty;
            bool changed = false;

            if (shouldRefresh)
            {
                changed = state.Refresh(keysProperty, keyType);
            }

            if (!state.HasDuplicates && state.IsEmpty)
            {
                _duplicateStates.Remove(cacheKey);
            }

            UnityHelpersSettings.DuplicateRowAnimationMode animationMode =
                UnityHelpersSettings.GetDuplicateRowAnimationMode();
            bool animateDuplicates =
                animationMode == UnityHelpersSettings.DuplicateRowAnimationMode.Tween;

            if (state.HasDuplicates && animateDuplicates)
            {
                int tweenCycleLimit = UnityHelpersSettings.GetDuplicateRowTweenCycleLimit();
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
            else if (changed)
            {
                MarkListCacheDirty(cacheKey);
            }

            return state;
        }

        internal NullKeyState RefreshNullKeyState(
            string cacheKey,
            SerializedProperty keysProperty,
            Type keyType
        )
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return null;
            }

            int currentFrame = Time.frameCount;
            bool alreadyRefreshedThisFrame = _lastNullKeyRefreshFrame == currentFrame;
            _lastNullKeyRefreshFrame = currentFrame;
            NullKeyState existingState = _nullKeyStates.GetValueOrDefault(cacheKey);

            if (alreadyRefreshedThisFrame && (existingState == null || !existingState.IsDirty))
            {
                return existingState;
            }

            bool hasEvent = Event.current != null;
            EventType eventType = hasEvent ? Event.current.type : EventType.Repaint;
            bool stateIsDirty = existingState != null && existingState.IsDirty;
            if (eventType != EventType.Repaint && !stateIsDirty)
            {
                return existingState;
            }

            if (!TypeSupportsNullReferences(keyType))
            {
                if (_nullKeyStates.Remove(cacheKey))
                {
                    MarkListCacheDirty(cacheKey);
                    RequestRepaint();
                }

                return null;
            }

            NullKeyState state = _nullKeyStates.GetOrAdd(cacheKey);
            bool changed = state.Refresh(keysProperty, keyType);

            if (!state.HasNullKeys)
            {
                _nullKeyStates.Remove(cacheKey);
                if (changed)
                {
                    MarkListCacheDirty(cacheKey);
                    RequestRepaint();
                }

                return null;
            }

            if (changed)
            {
                MarkListCacheDirty(cacheKey);
                RequestRepaint();
            }

            return state;
        }

        private bool TryGetDuplicateInfo(
            string cacheKey,
            int arrayIndex,
            out DuplicateKeyInfo info,
            out DuplicateKeyState state
        )
        {
            info = null;
            state = null;
            if (string.IsNullOrEmpty(cacheKey))
            {
                return false;
            }

            return _duplicateStates.TryGetValue(cacheKey, out state)
                && state.TryGetInfo(arrayIndex, out info);
        }

        private bool TryGetNullKeyInfo(string cacheKey, int arrayIndex, out NullKeyInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(cacheKey))
            {
                return false;
            }

            return _nullKeyStates.TryGetValue(cacheKey, out NullKeyState state)
                && state.TryGetInfo(arrayIndex, out info);
        }

        private static void DrawDuplicateTooltip(Rect rect, string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip) || Event.current.type != EventType.Repaint)
            {
                return;
            }

            DuplicateTooltipContent.text = string.Empty;
            DuplicateTooltipContent.image = null;
            DuplicateTooltipContent.tooltip = tooltip;
            GUI.Label(rect, DuplicateTooltipContent, GUIStyle.none);
        }

        private static GUIContent GetDuplicateIconContent(string tooltip)
        {
            if (DuplicateIconTemplate != null)
            {
                DuplicateIconContentCache.image = DuplicateIconTemplate.image;
                DuplicateIconContentCache.text = string.Empty;
            }
            else
            {
                DuplicateIconContentCache.image = null;
                DuplicateIconContentCache.text = "!";
            }

            DuplicateIconContentCache.tooltip = tooltip;
            return DuplicateIconContentCache;
        }

        private static void DrawNullTooltip(Rect rect, string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip) || Event.current.type != EventType.Repaint)
            {
                return;
            }

            NullKeyTooltipContent.text = string.Empty;
            NullKeyTooltipContent.image = null;
            NullKeyTooltipContent.tooltip = tooltip;
            GUI.Label(rect, NullKeyTooltipContent, GUIStyle.none);
        }

        internal static float CalculateDictionaryRowHeight(
            SerializedProperty keyProperty,
            SerializedProperty valueProperty,
            bool valueUsesFoldout = false
        )
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float keyHeight =
                keyProperty != null
                    ? EditorGUI.GetPropertyHeight(keyProperty, GUIContent.none, true)
                    : EditorGUIUtility.singleLineHeight;
            float valueHeight =
                valueProperty != null
                    ? CalculateValueContentHeight(valueProperty, valueUsesFoldout)
                    : EditorGUIUtility.singleLineHeight;
            float contentHeight = Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                Mathf.Max(keyHeight, valueHeight)
            );
            return contentHeight + spacing * 2f;
        }

        internal static float CalculateValueContentHeight(
            SerializedProperty valueProperty,
            bool valueUsesFoldout
        )
        {
            if (!valueUsesFoldout || !SerializedPropertySupportsFoldout(valueProperty))
            {
                return EditorGUI.GetPropertyHeight(valueProperty, GUIContent.none, true);
            }

            return CalculateFoldoutValueHeight(valueProperty);
        }

        private static float CalculateFoldoutValueHeight(SerializedProperty valueProperty)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!valueProperty.isExpanded || !valueProperty.hasVisibleChildren)
            {
                return height;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty iterator = valueProperty.Copy();
            SerializedProperty endProperty = iterator.GetEndProperty();
            bool enterChildren = true;
            int baseDepth = valueProperty.depth;

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
                height += spacing + childHeight;
            }

            return height;
        }

        private CachedPropertyPair GetOrCreateCachedPropertyPair(
            string listKey,
            SerializedProperty dictionaryProperty
        )
        {
            int currentFrame = Time.frameCount;
            if (_lastPropertyPairCacheFrame != currentFrame)
            {
                _cachedPropertyPairs.Clear();
                _lastPropertyPairCacheFrame = currentFrame;
            }

            if (_cachedPropertyPairs.TryGetValue(listKey, out CachedPropertyPair cached))
            {
                return cached;
            }

            CachedPropertyPair pair = new()
            {
                keysProperty = dictionaryProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Keys
                ),
                valuesProperty = dictionaryProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Values
                ),
            };
            _cachedPropertyPairs[listKey] = pair;
            return pair;
        }

        private RowRenderData GetOrCreateRowRenderData(
            string listKey,
            int globalIndex,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            Type resolvedValueType
        )
        {
            int currentFrame = Time.frameCount;
            // Clear cache on new frame OR when a child drawer signaled height change
            if (
                _lastRowRenderCacheFrame != currentFrame
                || _childHeightChangedFrame == currentFrame
            )
            {
                _rowRenderCache.Clear();
                _lastRowRenderCacheFrame = currentFrame;
            }

            RowRenderKey cacheKey = new(listKey, globalIndex);
            if (_rowRenderCache.TryGetValue(cacheKey, out RowRenderData cached))
            {
                return cached;
            }

            RowRenderData data = new();

            if (
                globalIndex < 0
                || globalIndex >= keysProperty.arraySize
                || globalIndex >= valuesProperty.arraySize
            )
            {
                data.isValid = false;
                _rowRenderCache[cacheKey] = data;
                return data;
            }

            data.keyProperty = keysProperty.GetArrayElementAtIndex(globalIndex);
            data.valueProperty = valuesProperty.GetArrayElementAtIndex(globalIndex);
            data.isValid = true;

            // Determine if the value type supports foldout/expansion.
            // Trust the type-based check when the type is known to support complex editing,
            // because Unity's hasVisibleChildren may temporarily return false for newly created
            // array elements before the serialization system fully processes the type structure.
            bool shouldAutoExpand = false;
            if (data.valueProperty != null && resolvedValueType != null)
            {
                bool typeSupportsFoldout = ValueTypeSupportsFoldout(resolvedValueType);
                bool propertySupportsFoldout = SerializedPropertySupportsFoldout(
                    data.valueProperty
                );
                // If type definitively supports foldout OR property has visible children, enable foldout
                shouldAutoExpand = typeSupportsFoldout || propertySupportsFoldout;
            }
            else if (data.valueProperty != null)
            {
                shouldAutoExpand = data.valueProperty.hasVisibleChildren;
            }

            data.valueSupportsFoldout = shouldAutoExpand;

            data.keyHeight =
                data.keyProperty != null
                    ? EditorGUI.GetPropertyHeight(data.keyProperty, GUIContent.none, true)
                    : EditorGUIUtility.singleLineHeight;

            data.valueHeight =
                data.valueProperty != null
                    ? CalculateValueContentHeight(data.valueProperty, shouldAutoExpand)
                    : EditorGUIUtility.singleLineHeight;

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float contentHeight = Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                Mathf.Max(data.keyHeight, data.valueHeight)
            );
            data.rowHeight = contentHeight + spacing * 2f;

            _rowRenderCache[cacheKey] = data;
            return data;
        }

        internal static Rect ExpandDictionaryRowRect(Rect rect)
        {
            rect.yMin -= 1f;
            rect.yMax += 1f;
            return rect;
        }

        private static GUIContent GetNullKeyIconContent(string tooltip)
        {
            if (DuplicateIconTemplate != null)
            {
                NullKeyIconContentCache.image = DuplicateIconTemplate.image;
                NullKeyIconContentCache.text = string.Empty;
            }
            else
            {
                NullKeyIconContentCache.image = null;
                NullKeyIconContentCache.text = "!";
            }

            NullKeyIconContentCache.tooltip = tooltip;
            return NullKeyIconContentCache;
        }

        internal static float EvaluateDuplicateTweenOffset(
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

        private static void DrawDuplicateOutline(Rect rect)
        {
            Rect top = new(rect.x, rect.y, rect.width, DuplicateBorderThickness);
            Rect bottom = new(
                rect.x,
                rect.yMax - DuplicateBorderThickness,
                rect.width,
                DuplicateBorderThickness
            );
            Rect left = new(rect.x, rect.y, DuplicateBorderThickness, rect.height);
            Rect right = new(
                rect.xMax - DuplicateBorderThickness,
                rect.y,
                DuplicateBorderThickness,
                rect.height
            );

            EditorGUI.DrawRect(top, DuplicateOutlineColor);
            EditorGUI.DrawRect(bottom, DuplicateOutlineColor);
            EditorGUI.DrawRect(left, DuplicateOutlineColor);
            EditorGUI.DrawRect(right, DuplicateOutlineColor);
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

        private static float GetWarningBarHeight()
        {
            return EditorGUIUtility.singleLineHeight * 1.6f;
        }

        private static bool TypeSupportsNullReferences(Type type)
        {
            return type != null && (!type.IsValueType || typeof(Object).IsAssignableFrom(type));
        }

        private static string BuildNullKeySummary(ICollection<int> indices)
        {
            if (indices == null || indices.Count == 0)
            {
                return string.Empty;
            }

            using PooledResource<List<int>> sortedLease = Buffers<int>.GetList(
                indices.Count,
                out List<int> sorted
            );
            sorted.AddRange(indices);
            sorted.Sort();

            const int maxDisplay = 5;
            int displayCount = Math.Min(sorted.Count, maxDisplay);

            using PooledResource<StringBuilder> builderLease = Buffers.GetStringBuilder(
                Math.Max(sorted.Count * 6 + 64, 64),
                out StringBuilder summaryBuilder
            );
            summaryBuilder.Clear();

            if (sorted.Count == 1)
            {
                summaryBuilder.Append("Null key detected at index ");
                summaryBuilder.Append(sorted[0]);
                summaryBuilder.Append(". Entry will be ignored at runtime.");
                return summaryBuilder.ToString();
            }

            summaryBuilder.Append("Null keys detected at indices ");

            for (int i = 0; i < displayCount; i++)
            {
                if (i > 0)
                {
                    summaryBuilder.Append(", ");
                }

                summaryBuilder.Append(sorted[i]);
            }

            if (sorted.Count > maxDisplay)
            {
                summaryBuilder.Append(", ... (");
                summaryBuilder.Append(sorted.Count - maxDisplay);
                summaryBuilder.Append(" more)");
            }

            summaryBuilder.Append(". Entries will be ignored at runtime.");
            return summaryBuilder.ToString();
        }

        private static string FormatDuplicateKeyDisplay(object keyValue)
        {
            object actualKey = ReferenceEquals(keyValue, NullKeySentinel) ? null : keyValue;

            if (actualKey == null)
            {
                return "<null>";
            }

            if (actualKey is string stringKey)
            {
                if (string.IsNullOrWhiteSpace(stringKey))
                {
                    return "<empty>";
                }

                return stringKey;
            }

            if (actualKey is Object unityObject)
            {
                if (unityObject == null)
                {
                    return "<missing object>";
                }

                return string.IsNullOrEmpty(unityObject.name)
                    ? unityObject.GetType().Name
                    : unityObject.name;
            }

            return actualKey.ToString();
        }

        private static string BuildDuplicateTooltip(string formattedKey, List<int> indices)
        {
            using PooledResource<StringBuilder> builderLease = Buffers.GetStringBuilder(
                64 + (formattedKey?.Length ?? 0) + (indices?.Count ?? 0) * 4,
                out StringBuilder builder
            );
            builder.Clear();

            if (indices == null || indices.Count == 0)
            {
                builder.Append("Duplicate key \"");
                builder.Append(formattedKey);
                builder.Append("\" detected.");
                return builder.ToString();
            }

            int count = indices.Count;
            using PooledResource<List<int>> positionsLease = Buffers<int>.GetList(
                count,
                out List<int> positions
            );
            for (int index = 0; index < count; index++)
            {
                positions.Add(indices[index] + 1);
            }

            positions.Sort();

            builder.Append("Duplicate key \"");
            builder.Append(formattedKey);
            builder.Append("\" is assigned to entries ");

            for (int index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(positions[index]);
            }

            builder.Append(". The last entry will be used at runtime.");
            return builder.ToString();
        }

        internal PendingEntry GetOrCreatePendingEntry(
            SerializedProperty property,
            Type keyType,
            Type valueType,
            bool isSortedDictionary
        )
        {
            string key = GetListKey(property);
            if (_pendingEntries.TryGetValue(key, out PendingEntry entry))
            {
                entry.isSorted = isSortedDictionary;
                EnsurePendingFoldoutAnim(entry);
                return entry;
            }

            entry = new PendingEntry
            {
                key = GetDefaultValue(keyType),
                value = GetDefaultValue(valueType),
                isExpanded = false,
                isSorted = isSortedDictionary,
            };
            if (ShouldTweenPendingFoldout(isSortedDictionary))
            {
                entry.foldoutAnim = CreatePendingFoldoutAnim(entry.isExpanded, isSortedDictionary);
            }
            _pendingEntries[key] = entry;
            return entry;
        }

        internal PaginationState GetOrCreatePaginationState(SerializedProperty property)
        {
            string key = GetListKey(property);
            int configuredPageSize = UnityHelpersSettings.GetSerializableDictionaryPageSize();
            if (_paginationStates.TryGetValue(key, out PaginationState state))
            {
                if (state.pageSize <= 0)
                {
                    state.pageSize = configuredPageSize > 0 ? configuredPageSize : DefaultPageSize;
                }

                if (state.pageSize != configuredPageSize && configuredPageSize > 0)
                {
                    state.pageSize = configuredPageSize;
                    state.pageIndex = 0;
                }

                return state;
            }

            PaginationState newState = new()
            {
                pageIndex = 0,
                pageSize = configuredPageSize > 0 ? configuredPageSize : DefaultPageSize,
                selectedIndex = -1,
            };
            _paginationStates[key] = newState;
            return newState;
        }

        internal ListPageCache EnsurePageCache(
            string cacheKey,
            SerializedProperty keysProperty,
            PaginationState pagination
        )
        {
            ListPageCache cache = GetOrCreatePageCache(cacheKey);
            int itemCount = keysProperty.arraySize;
            if (
                cache.dirty
                || cache.pageIndex != pagination.pageIndex
                || cache.pageSize != pagination.pageSize
                || cache.itemCount != itemCount
            )
            {
                RefreshPageCache(cache, keysProperty, pagination);
            }

            return cache;
        }

        private ListPageCache GetOrCreatePageCache(string cacheKey)
        {
            return _pageCaches.GetOrAdd(cacheKey);
        }

        private static void RefreshPageCache(
            ListPageCache cache,
            SerializedProperty keysProperty,
            PaginationState pagination
        )
        {
            cache.entries.Clear();
            cache.itemCount = keysProperty.arraySize;
            cache.pageIndex = pagination.pageIndex;
            int effectivePageSize = Mathf.Clamp(pagination.pageSize, 1, MaxPageSize);
            cache.pageSize = effectivePageSize;
            pagination.pageSize = effectivePageSize;
            cache.dirty = false;

            if (cache.itemCount <= 0)
            {
                return;
            }

            int startIndex = 0;
            int endIndex = cache.itemCount;
            if (effectivePageSize > 0)
            {
                startIndex = Mathf.Clamp(
                    pagination.pageIndex * effectivePageSize,
                    0,
                    cache.itemCount
                );
                endIndex = Mathf.Min(startIndex + effectivePageSize, cache.itemCount);
            }

            for (int i = startIndex; i < endIndex; i++)
            {
                PageEntry entry = new() { arrayIndex = i };
                cache.entries.Add(entry);
            }
        }

        internal void MarkListCacheDirty(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            _lists.Remove(cacheKey);
            _sortedOrderHashes.Remove(cacheKey);

            if (!_pageCaches.TryGetValue(cacheKey, out ListPageCache cache))
            {
                return;
            }

            cache.entries.Clear();
            cache.dirty = true;
            cache.pageIndex = -1;
            cache.pageSize = -1;
            cache.itemCount = -1;

            if (_rowValueFoldoutStates.Count <= 0)
            {
                return;
            }

            PooledResource<List<RowFoldoutKey>> keysToRemoveLease = default;
            List<RowFoldoutKey> keysToRemove = null;
            foreach (KeyValuePair<RowFoldoutKey, bool> entry in _rowValueFoldoutStates)
            {
                if (entry.Key.IsValid && entry.Key.CacheKey == cacheKey)
                {
                    if (keysToRemove == null)
                    {
                        keysToRemoveLease = Buffers<RowFoldoutKey>.List.Get(out keysToRemove);
                    }

                    keysToRemove.Add(entry.Key);
                }
            }

            if (keysToRemove != null)
            {
                foreach (RowFoldoutKey keyToRemove in keysToRemove)
                {
                    _rowValueFoldoutStates.Remove(keyToRemove);
                }
            }

            keysToRemoveLease.Dispose();
        }

        private bool EnsureRowFoldoutState(
            RowFoldoutKey foldoutKey,
            SerializedProperty valueProperty,
            out bool stateChanged
        )
        {
            stateChanged = false;
            if (valueProperty == null || !foldoutKey.IsValid)
            {
                return false;
            }

            bool previous = valueProperty.isExpanded;
            if (!_rowValueFoldoutStates.TryGetValue(foldoutKey, out bool state))
            {
                state = true;
                _rowValueFoldoutStates[foldoutKey] = state;
            }

            valueProperty.isExpanded = state;
            stateChanged = previous != state;
            return state;
        }

        private bool EnsureRowFoldoutState(
            RowFoldoutKey foldoutKey,
            SerializedProperty valueProperty
        )
        {
            return EnsureRowFoldoutState(foldoutKey, valueProperty, out bool _);
        }

        private void PrimeRowFoldoutStates(
            string cacheKey,
            ListPageCache cache,
            SerializedProperty valuesProperty,
            Type valueType
        )
        {
            if (
                string.IsNullOrEmpty(cacheKey)
                || cache?.entries == null
                || cache.entries.Count == 0
                || valuesProperty == null
            )
            {
                return;
            }

            int currentFrame = Time.frameCount;
            if (_lastPrimedFoldoutFrame != currentFrame)
            {
                _primedFoldoutCaches.Clear();
                _lastPrimedFoldoutFrame = currentFrame;
            }

            if (!_primedFoldoutCaches.Add(cacheKey))
            {
                return;
            }

            bool typeSupportsFoldout = ValueTypeSupportsFoldout(valueType);
            foreach (PageEntry cacheEntry in cache.entries)
            {
                int globalIndex = cacheEntry.arrayIndex;
                if (globalIndex < 0 || globalIndex >= valuesProperty.arraySize)
                {
                    continue;
                }

                SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(
                    globalIndex
                );
                if (valueProperty == null)
                {
                    continue;
                }

                // Trust the type-based check when the type is known to support complex editing,
                // because Unity's hasVisibleChildren may temporarily return false for newly created
                // array elements before the serialization system fully processes the type structure.
                bool propertySupportsFoldout =
                    typeSupportsFoldout
                    || SerializedPropertySupportsFoldout(valueProperty)
                    || valueProperty.hasVisibleChildren;
                if (!propertySupportsFoldout)
                {
                    continue;
                }

                RowFoldoutKey rowKey = BuildRowFoldoutStateKey(cacheKey, globalIndex);
                if (!rowKey.IsValid)
                {
                    continue;
                }

                if (_rowValueFoldoutStates.TryGetValue(rowKey, out bool state))
                {
                    valueProperty.isExpanded = state;
                    continue;
                }

                _rowValueFoldoutStates[rowKey] = true;
                valueProperty.isExpanded = true;
            }
        }

        private static RowFoldoutKey BuildRowFoldoutStateKey(string cacheKey, int globalIndex)
        {
            return new RowFoldoutKey(cacheKey, globalIndex);
        }

        internal void SetRowFoldoutStateForTests(string cacheKey, int globalIndex, bool isExpanded)
        {
            RowFoldoutKey key = BuildRowFoldoutStateKey(cacheKey, globalIndex);
            if (!key.IsValid)
            {
                return;
            }

            _rowValueFoldoutStates[key] = isExpanded;
        }

        private static int GetRelativeIndex(ListPageCache cache, int globalIndex)
        {
            if (globalIndex < 0)
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

        private static void ApplyDictionarySliceOrder(
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            List<int> orderedIndices,
            int pageStart
        )
        {
            if (keysProperty == null || valuesProperty == null || orderedIndices == null)
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

                keysProperty.MoveArrayElement(currentIndex, desiredIndex);
                valuesProperty.MoveArrayElement(currentIndex, desiredIndex);

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

        private static bool RelativeIndexIsValid(ListPageCache cache, int relativeIndex)
        {
            return relativeIndex >= 0 && relativeIndex < cache.entries.Count;
        }

        internal static void SyncListSelectionWithPagination(
            ReorderableList list,
            PaginationState pagination,
            ListPageCache cache
        )
        {
            if (cache.entries.Count == 0)
            {
                list.index = -1;
                pagination.selectedIndex = -1;
                return;
            }

            int relativeIndex = GetRelativeIndex(cache, pagination.selectedIndex);
            if (relativeIndex < 0)
            {
                relativeIndex = 0;
                pagination.selectedIndex = cache.entries[0].arrayIndex;
            }

            list.index = relativeIndex;
        }

        private void DrawListHeader(
            Rect rect,
            SerializedProperty keysProperty,
            ReorderableList list,
            PaginationState pagination,
            Func<ListPageCache> cacheProvider
        )
        {
            // Draw custom header background when inside WGroup to respect theming
            // This draws over Unity's default ReorderableList header background
            // Use opaque colors to fully cover Unity's default header styling
            if (_currentDrawInsideWGroup && Event.current.type == EventType.Repaint)
            {
                Color headerColor = EditorGUIUtility.isProSkin ? DarkHeaderColor : LightHeaderColor;
                // Temporarily reset GUI.color to prevent tinting from parent WGroup
                Color prevGuiColor = GUI.color;
                GUI.color = Color.white;
                EditorGUI.DrawRect(rect, headerColor);
                GUI.color = prevGuiColor;
            }

            ClampPaginationState(pagination, keysProperty.arraySize);
            ListPageCache cache = cacheProvider();
            SyncListSelectionWithPagination(list, pagination, cache);

            float navWidthFull = (PaginationButtonWidth * 4f) + (PaginationControlSpacing * 3f);
            float navWidthPrevNext = (PaginationButtonWidth * 2f) + PaginationControlSpacing;

            PaginationControlLayout layout = PaginationControlLayout.Full;
            bool showPageLabel = true;
            float navWidth = navWidthFull;
            float controlsWidth = ComputeControlsWidth(navWidth, showPageLabel);

            if (controlsWidth > rect.width)
            {
                showPageLabel = false;
                controlsWidth = ComputeControlsWidth(navWidth, showPageLabel);
            }

            if (controlsWidth > rect.width)
            {
                layout = PaginationControlLayout.PrevNext;
                navWidth = navWidthPrevNext;
                controlsWidth = ComputeControlsWidth(navWidth, showPageLabel);
            }

            if (controlsWidth > rect.width)
            {
                layout = PaginationControlLayout.None;
                navWidth = 0f;
                showPageLabel = false;
                controlsWidth = 0f;
            }

            if (controlsWidth <= 0f)
            {
                return;
            }

            Rect controlsRect = new(rect.xMax - controlsWidth, rect.y, controlsWidth, rect.height);

            int itemCount = keysProperty.arraySize;
            int totalPages = GetTotalPages(itemCount, pagination.pageSize);
            float navStartX = controlsRect.x;

            if (showPageLabel)
            {
                Rect pageLabelRect = new(
                    controlsRect.x,
                    controlsRect.y,
                    PaginationLabelWidth,
                    controlsRect.height
                );
                PaginationPageLabelContent.text = GetPaginationLabel(
                    pagination.pageIndex + 1,
                    totalPages
                );
                EditorGUI.LabelField(
                    pageLabelRect,
                    PaginationPageLabelContent,
                    EditorStyles.miniLabel
                );
                navStartX = pageLabelRect.xMax + (navWidth > 0f ? PaginationControlSpacing : 0f);
            }

            if (layout == PaginationControlLayout.None)
            {
                return;
            }

            switch (layout)
            {
                case PaginationControlLayout.Full:
                    Rect firstRect = new(
                        navStartX,
                        controlsRect.y,
                        PaginationButtonWidth,
                        controlsRect.height
                    );
                    Rect prevRect = new(
                        firstRect.xMax + PaginationControlSpacing,
                        controlsRect.y,
                        PaginationButtonWidth,
                        controlsRect.height
                    );
                    Rect nextRect = new(
                        prevRect.xMax + PaginationControlSpacing,
                        controlsRect.y,
                        PaginationButtonWidth,
                        controlsRect.height
                    );
                    Rect lastRect = new(
                        nextRect.xMax + PaginationControlSpacing,
                        controlsRect.y,
                        PaginationButtonWidth,
                        controlsRect.height
                    );

                    using (new EditorGUI.DisabledScope(pagination.pageIndex <= 0))
                    {
                        if (GUI.Button(firstRect, PaginationFirstContent, EditorStyles.miniButton))
                        {
                            SetPageIndex(pagination, 0, keysProperty, forceImmediateRefresh: true);
                            cache = cacheProvider();
                            SyncListSelectionWithPagination(list, pagination, cache);
                        }

                        if (GUI.Button(prevRect, PaginationPrevContent, EditorStyles.miniButton))
                        {
                            SetPageIndex(
                                pagination,
                                pagination.pageIndex - 1,
                                keysProperty,
                                forceImmediateRefresh: true
                            );
                            cache = cacheProvider();
                            SyncListSelectionWithPagination(list, pagination, cache);
                        }
                    }

                    using (new EditorGUI.DisabledScope(pagination.pageIndex >= totalPages - 1))
                    {
                        if (GUI.Button(nextRect, PaginationNextContent, EditorStyles.miniButton))
                        {
                            SetPageIndex(
                                pagination,
                                pagination.pageIndex + 1,
                                keysProperty,
                                forceImmediateRefresh: true
                            );
                            cache = cacheProvider();
                            SyncListSelectionWithPagination(list, pagination, cache);
                        }

                        if (GUI.Button(lastRect, PaginationLastContent, EditorStyles.miniButton))
                        {
                            SetPageIndex(
                                pagination,
                                totalPages - 1,
                                keysProperty,
                                forceImmediateRefresh: true
                            );
                            cache = cacheProvider();
                            SyncListSelectionWithPagination(list, pagination, cache);
                        }
                    }

                    break;

                case PaginationControlLayout.PrevNext:
                    Rect prevOnlyRect = new(
                        navStartX,
                        controlsRect.y,
                        PaginationButtonWidth,
                        controlsRect.height
                    );
                    Rect nextOnlyRect = new(
                        prevOnlyRect.xMax + PaginationControlSpacing,
                        controlsRect.y,
                        PaginationButtonWidth,
                        controlsRect.height
                    );

                    using (new EditorGUI.DisabledScope(pagination.pageIndex <= 0))
                    {
                        if (
                            GUI.Button(prevOnlyRect, PaginationPrevContent, EditorStyles.miniButton)
                        )
                        {
                            SetPageIndex(
                                pagination,
                                pagination.pageIndex - 1,
                                keysProperty,
                                forceImmediateRefresh: true
                            );
                            cache = cacheProvider();
                            SyncListSelectionWithPagination(list, pagination, cache);
                        }
                    }

                    using (new EditorGUI.DisabledScope(pagination.pageIndex >= totalPages - 1))
                    {
                        if (
                            GUI.Button(nextOnlyRect, PaginationNextContent, EditorStyles.miniButton)
                        )
                        {
                            SetPageIndex(
                                pagination,
                                pagination.pageIndex + 1,
                                keysProperty,
                                forceImmediateRefresh: true
                            );
                            cache = cacheProvider();
                            SyncListSelectionWithPagination(list, pagination, cache);
                        }
                    }

                    break;
            }

            return;

            float ComputeControlsWidth(float navigationWidth, bool includePageLabel)
            {
                float width = navigationWidth;
                if (includePageLabel)
                {
                    width += PaginationLabelWidth;
                    if (navigationWidth > 0f)
                    {
                        width += PaginationControlSpacing;
                    }
                }

                return width;
            }
        }

        private void DrawListFooter(
            Rect rect,
            ReorderableList list,
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            PaginationState pagination,
            Func<ListPageCache> cacheProvider
        )
        {
            if (Event.current.type == EventType.Repaint)
            {
                // Draw custom footer background when inside WGroup to respect theming
                // Use opaque colors to fully cover Unity's default footer styling
                if (_currentDrawInsideWGroup)
                {
                    Color footerColor = EditorGUIUtility.isProSkin
                        ? DarkHeaderColor
                        : LightHeaderColor;
                    // Temporarily reset GUI.color to prevent tinting from parent WGroup
                    Color prevGuiColor = GUI.color;
                    GUI.color = Color.white;
                    EditorGUI.DrawRect(rect, footerColor);
                    GUI.color = prevGuiColor;
                }
                else
                {
                    GUIStyle footerStyle =
                        ReorderableList.defaultBehaviours.footerBackground ?? "RL Footer";
                    footerStyle.Draw(rect, GUIContent.none, false, false, false, false);
                }
            }

            int itemCount = keysProperty.arraySize;
            int pageStart = Mathf.Clamp(pagination.pageIndex * pagination.pageSize, 0, itemCount);
            int pageEnd = Mathf.Min(pageStart + pagination.pageSize, itemCount);

            float padding = 4f;
            float buttonWidth = 26f;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float verticalCenter = rect.y + Mathf.Max(0f, (rect.height - lineHeight) * 0.5f);
            float labelHeight = lineHeight + 4f;
            float labelY = rect.y + Mathf.Max(0f, (rect.height - labelHeight) * 0.5f);
            float clearWidth = 80f;
            float sortWidth = 60f;

            GUIStyle footerLabelStyle = GetFooterLabelStyle();
            PaginationRangeContent.text =
                itemCount == 0 ? "Empty" : GetRangeLabel(pageStart + 1, pageEnd, itemCount);
            Vector2 rangeSize = footerLabelStyle.CalcSize(PaginationRangeContent);

            ListPageCache cache = cacheProvider();
            int selectedGlobalIndex = pagination.selectedIndex;
            int relativeSelected = GetRelativeIndex(cache, selectedGlobalIndex);

            bool canRemove =
                relativeSelected >= 0
                && selectedGlobalIndex >= 0
                && selectedGlobalIndex < keysProperty.arraySize;
            bool canClear = itemCount > 0;

            bool resolvedTypes = TryResolveKeyValueTypes(
                fieldInfo,
                out Type keyType,
                out Type valueType,
                out bool isSortedDictionary
            );

            object dictionaryInstance;
            if (!resolvedTypes || keyType == null || valueType == null)
            {
                dictionaryInstance = GetDictionaryInstance(dictionaryProperty);
                if (
                    TryResolveKeyValueTypesFromInstance(
                        dictionaryInstance,
                        out Type runtimeKeyType,
                        out Type runtimeValueType,
                        out bool runtimeSorted
                    )
                )
                {
                    keyType ??= runtimeKeyType;
                    valueType ??= runtimeValueType;
                    isSortedDictionary = runtimeSorted;
                }
            }
            else
            {
                dictionaryInstance = GetDictionaryInstance(dictionaryProperty);
            }

            Func<object, object, int> comparison = null;
            if (keyType != null && valueType != null)
            {
                if (isSortedDictionary)
                {
                    object comparerInstance = ResolveComparerInstance(
                        dictionaryProperty,
                        dictionaryInstance,
                        keyType
                    );
                    comparison = CreateComparisonDelegate(comparerInstance, keyType);
                }

                if (comparison == null && KeyTypeSupportsManualSorting(keyType))
                {
                    comparison = CreateManualKeyComparisonDelegate(keyType);
                }
            }
            bool needsSorting =
                comparison != null
                && keyType != null
                && keysProperty != null
                && itemCount > 1
                && !KeysAreSorted(keysProperty, keyType, comparison);

            string listKey = GetListKey(dictionaryProperty);
            bool canHashOrder =
                keyType != null && keysProperty != null && !string.IsNullOrEmpty(listKey);
            int currentOrderHash = 0;
            if (canHashOrder)
            {
                currentOrderHash = ComputeKeyOrderHash(keysProperty, keyType);
            }

            if (!needsSorting && canHashOrder)
            {
                _sortedOrderHashes[listKey] = currentOrderHash;
            }
            else if (
                needsSorting
                && canHashOrder
                && _sortedOrderHashes.TryGetValue(listKey, out int cachedOrderHash)
                && cachedOrderHash == currentOrderHash
            )
            {
                needsSorting = false;
            }

            bool showSort = needsSorting;
            if (showSort)
            {
                bool pageNeedsSorting = PageEntriesNeedSorting(
                    cache,
                    keysProperty,
                    keyType,
                    comparison
                );
                if (!pageNeedsSorting)
                {
                    showSort = false;
                }
            }
            bool sortEnabled = showSort;

            GUIContent clearAllContent = EditorGUIUtility.TrTextContent(
                "Clear All",
                "Remove every entry from the dictionary"
            );
            GUIContent removeContent = EditorGUIUtility.TrTextContent("-", "Remove selected entry");
            GUIContent sortContent = EditorGUIUtility.TrTextContent(
                "Sort",
                "Sort entries by key using natural ordering"
            );

            bool showRange = itemCount > 0 || PaginationRangeContent.text == "Empty";
            bool showClear = true;
            bool showRemove = true;

            float requiredWidth = CalculateRequiredWidth(
                showRange,
                showClear,
                showSort,
                showRemove
            );
            if (requiredWidth > rect.width && showRange)
            {
                showRange = false;
            }

            requiredWidth = CalculateRequiredWidth(showRange, showClear, showSort, showRemove);
            if (requiredWidth > rect.width && showClear)
            {
                showClear = false;
            }

            requiredWidth = CalculateRequiredWidth(showRange, showClear, showSort, showRemove);
            if (requiredWidth > rect.width && showSort)
            {
                showSort = false;
            }

            requiredWidth = CalculateRequiredWidth(showRange, showClear, showSort, showRemove);
            if (requiredWidth > rect.width && showRemove)
            {
                showRemove = false;
            }

            if (!showRange && !showClear && !showSort && !showRemove)
            {
                return;
            }

            Rect removeRect = default;
            Rect clearRect = default;
            Rect sortRect = default;

            float currentX = rect.xMax - padding;

            if (showRemove)
            {
                currentX -= buttonWidth;
                removeRect = new Rect(currentX, verticalCenter, buttonWidth, lineHeight);
                if (showClear || showSort || showRange)
                {
                    currentX -= PaginationControlSpacing;
                }
            }

            if (showClear)
            {
                currentX -= clearWidth;
                clearRect = new Rect(currentX, verticalCenter, clearWidth, lineHeight);
                if (showSort || showRange)
                {
                    currentX -= PaginationControlSpacing;
                }
            }

            if (showSort)
            {
                currentX -= sortWidth;
                sortRect = new Rect(currentX, verticalCenter, sortWidth, lineHeight);
                if (showRange)
                {
                    currentX -= PaginationControlSpacing;
                }
            }

            if (showRange)
            {
                float labelLeft = rect.x + padding;
                float labelWidth = Mathf.Max(0f, currentX - labelLeft);
                Rect labelRect = new(labelLeft, labelY, labelWidth, labelHeight);
                EditorGUI.LabelField(labelRect, PaginationRangeContent, footerLabelStyle);
            }

            if (showSort)
            {
                DrawFooterButton(
                    sortRect,
                    sortContent,
                    "Sort",
                    sortEnabled,
                    () =>
                    {
                        if (!sortEnabled)
                        {
                            return;
                        }

                        SortDictionaryEntries(
                            dictionaryProperty,
                            keysProperty,
                            valuesProperty,
                            keyType,
                            valueType,
                            comparison,
                            pagination,
                            list
                        );
                    }
                );
            }

            if (showClear)
            {
                DrawFooterButton(
                    clearRect,
                    clearAllContent,
                    "ClearAll",
                    canClear,
                    () =>
                    {
                        bool confirmed = EditorUtility.DisplayDialog(
                            "Clear Dictionary",
                            "Remove all entries from this dictionary?",
                            "Clear",
                            "Cancel"
                        );
                        if (confirmed)
                        {
                            ClearDictionary(
                                dictionaryProperty,
                                keysProperty,
                                valuesProperty,
                                pagination,
                                list
                            );
                        }
                    }
                );
            }

            if (showRemove)
            {
                DrawFooterButton(
                    removeRect,
                    removeContent,
                    "Remove",
                    canRemove,
                    () =>
                    {
                        RemoveEntryAtIndex(
                            selectedGlobalIndex,
                            list,
                            dictionaryProperty,
                            keysProperty,
                            valuesProperty,
                            pagination
                        );
                    }
                );
            }

            return;

            float CalculateRequiredWidth(
                bool includeRange,
                bool includeClear,
                bool includeSort,
                bool includeRemove
            )
            {
                float width = padding + padding;

                if (includeRange)
                {
                    width += rangeSize.x;
                }

                bool hasRightControls = includeClear || includeSort || includeRemove;
                if (includeRange && hasRightControls)
                {
                    width += PaginationControlSpacing;
                }

                float rightWidth = 0f;
                if (includeRemove)
                {
                    rightWidth += buttonWidth;
                }
                if (includeClear)
                {
                    if (rightWidth > 0f)
                    {
                        rightWidth += PaginationControlSpacing;
                    }
                    rightWidth += clearWidth;
                }
                if (includeSort)
                {
                    if (rightWidth > 0f)
                    {
                        rightWidth += PaginationControlSpacing;
                    }
                    rightWidth += sortWidth;
                }

                width += rightWidth;
                return width;
            }
        }

        internal void RemoveEntryAtIndex(
            int removeIndex,
            ReorderableList list,
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            PaginationState pagination
        )
        {
            if (removeIndex < 0 || removeIndex >= keysProperty.arraySize)
            {
                return;
            }

            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Remove Dictionary Entry");
            }

            keysProperty.DeleteArrayElementAtIndex(removeIndex);
            valuesProperty.DeleteArrayElementAtIndex(removeIndex);
            ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                dictionaryProperty,
                "RemoveEntry"
            );
            serializedObject.Update();
            SyncRuntimeDictionary(dictionaryProperty);
            foreach (Object target in serializedObject.targetObjects)
            {
                if (target != null)
                {
                    EditorUtility.SetDirty(target);
                }
            }
            ClampPaginationState(pagination, keysProperty.arraySize);

            if (keysProperty.arraySize > 0)
            {
                int clampedIndex = Mathf.Clamp(removeIndex, 0, keysProperty.arraySize - 1);
                pagination.selectedIndex = clampedIndex;
                int totalPages = GetTotalPages(keysProperty.arraySize, pagination.pageSize);
                int desiredPage = GetPageForIndex(clampedIndex, pagination.pageSize);
                pagination.pageIndex = Mathf.Clamp(desiredPage, 0, totalPages - 1);
            }
            else
            {
                pagination.selectedIndex = -1;
                pagination.pageIndex = 0;
            }

            GUI.changed = true;
            InvalidateKeyCache(GetListKey(dictionaryProperty));

            string cacheKey = GetListKey(dictionaryProperty);
            ListPageCache cache = EnsurePageCache(cacheKey, keysProperty, pagination);
            SyncListSelectionWithPagination(list, pagination, cache);

            EditorWindow focusedWindow = EditorWindow.focusedWindow;
            if (focusedWindow != null)
            {
                focusedWindow.Repaint();
            }
        }

        private void ClearDictionary(
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            PaginationState pagination,
            ReorderableList list
        )
        {
            if (keysProperty.arraySize <= 0)
            {
                return;
            }

            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Clear Dictionary Entries");
            }

            keysProperty.ClearArray();
            valuesProperty.ClearArray();
            ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                dictionaryProperty,
                "ClearEntries"
            );
            SyncRuntimeDictionary(dictionaryProperty);

            ClampPaginationState(pagination, keysProperty.arraySize);
            pagination.selectedIndex = -1;
            pagination.pageIndex = 0;
            GUI.changed = true;
            InvalidateKeyCache(GetListKey(dictionaryProperty));

            string cacheKey = GetListKey(dictionaryProperty);
            ListPageCache cache = EnsurePageCache(cacheKey, keysProperty, pagination);
            SyncListSelectionWithPagination(list, pagination, cache);

            EditorWindow focusedWindow = EditorWindow.focusedWindow;
            if (focusedWindow != null)
            {
                focusedWindow.Repaint();
            }
        }

        private void DrawFooterButton(
            Rect rect,
            GUIContent content,
            string actionKey,
            bool enabled,
            Action onClick
        )
        {
            GUIStyle style = SolidButtonStyles.GetSolidButtonStyle(actionKey, enabled);
            if (!enabled)
            {
                EditorGUI.DrawRect(rect, SolidButtonStyles.DisabledColor);
                GUI.Label(rect, content, style);
                return;
            }

            if (GUI.Button(rect, content, style))
            {
                onClick?.Invoke();
            }
        }

        private void ClampPaginationState(PaginationState pagination, int itemCount)
        {
            if (pagination.pageSize <= 0)
            {
                pagination.pageSize = DefaultPageSize;
            }

            pagination.pageSize = Mathf.Clamp(pagination.pageSize, 1, MaxPageSize);

            int totalPages = GetTotalPages(itemCount, pagination.pageSize);
            pagination.pageIndex = Mathf.Clamp(pagination.pageIndex, 0, totalPages - 1);
        }

        private static int GetTotalPages(int itemCount, int pageSize)
        {
            if (pageSize <= 0)
            {
                return 1;
            }

            if (itemCount <= 0)
            {
                return 1;
            }

            return Mathf.CeilToInt(itemCount / (float)pageSize);
        }

        private static int GetPageForIndex(int index, int pageSize)
        {
            if (pageSize <= 0)
            {
                return 0;
            }

            if (index <= 0)
            {
                return 0;
            }

            return index / pageSize;
        }

        private static void SetPageIndex(
            PaginationState pagination,
            int targetPage,
            SerializedProperty keysProperty,
            bool forceImmediateRefresh = false
        )
        {
            if (pagination.pageSize <= 0)
            {
                pagination.pageSize = DefaultPageSize;
            }

            pagination.pageSize = Mathf.Clamp(pagination.pageSize, 1, MaxPageSize);

            int totalPages = GetTotalPages(keysProperty.arraySize, pagination.pageSize);
            int clampedPage = Mathf.Clamp(targetPage, 0, totalPages - 1);
            if (pagination.pageIndex == clampedPage)
            {
                return;
            }

            pagination.pageIndex = clampedPage;
            pagination.selectedIndex = -1;
            GUI.changed = true;
            if (forceImmediateRefresh)
            {
                EditorWindow focusedWindow = EditorWindow.focusedWindow;
                if (focusedWindow != null)
                {
                    focusedWindow.Repaint();
                }
            }
        }

        private void DrawPendingEntryUI(
            ref float y,
            Rect fullPosition,
            PendingEntry pending,
            ReorderableList list,
            PaginationState pagination,
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            Type keyType,
            Type valueType
        )
        {
            string propertyPath = dictionaryProperty?.propertyPath ?? "(null)";
            int previousIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            try
            {
                AnimBool foldoutAnim = EnsurePendingFoldoutAnim(pending, propertyPath);
                float foldoutProgress;
                if (foldoutAnim != null)
                {
                    foldoutAnim.target = pending.isExpanded;
                    foldoutProgress = foldoutAnim.faded;

                    SerializableCollectionTweenDiagnostics.LogAnimBoolState(
                        "DrawPendingEntryUI",
                        propertyPath,
                        pending.isExpanded,
                        foldoutAnim.faded,
                        foldoutAnim.target ? 1f : 0f,
                        foldoutAnim.speed,
                        foldoutAnim.isAnimating
                    );
                }
                else
                {
                    foldoutProgress = pending.isExpanded ? 1f : 0f;

                    SerializableCollectionTweenDiagnostics.LogFoldoutProgressCalculation(
                        "DrawPendingEntryUI_NoAnim",
                        propertyPath,
                        false,
                        pending.isExpanded,
                        foldoutProgress,
                        false
                    );
                }

                float resolvedSectionPadding = ResolvePendingSectionPadding(dictionaryProperty);

                PendingSectionMetrics pendingMetrics = CalculatePendingSectionMetrics(
                    pending,
                    keyType,
                    valueType,
                    dictionaryProperty
                );
                float sectionHeight = pendingMetrics.EvaluateHeight(foldoutProgress);

                SerializableCollectionTweenDiagnostics.LogPendingSectionHeightCalc(
                    propertyPath,
                    pendingMetrics.CollapsedHeight,
                    pendingMetrics.ExpandedExtraHeight,
                    foldoutProgress,
                    sectionHeight
                );

                float rowHeight = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;

                Rect containerRect = new(fullPosition.x, y, fullPosition.width, sectionHeight);
                Color backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.18f, 0.18f, 0.18f, 1f)
                    : new Color(0.92f, 0.92f, 0.92f, 1f);
                if (Event.current.type == EventType.Repaint)
                {
                    // Temporarily reset GUI.color to prevent tinting from parent WGroup
                    Color prevGuiColor = GUI.color;
                    GUI.color = Color.white;
                    EditorGUI.DrawRect(containerRect, backgroundColor);
                    GUI.color = prevGuiColor;
                }

                GUI.BeginGroup(containerRect);

                float innerWidth = Mathf.Max(0f, containerRect.width - resolvedSectionPadding * 2f);
                float innerY = resolvedSectionPadding;

                Rect headerRect = new(resolvedSectionPadding, innerY, innerWidth, rowHeight);
                float resolvedToggleOffset = ResolvePendingFoldoutToggleOffset(dictionaryProperty);
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
                    PendingFoldoutLabelPadding
                    + ResolvePendingFoldoutLabelContentOffset(dictionaryProperty);
                Rect labelRect = new(
                    labelHitRect.x + labelContentOffset,
                    headerRect.y,
                    Mathf.Max(0f, labelHitRect.width - labelContentOffset),
                    headerRect.height
                );

                HasLastPendingHeaderRect = true;
                LastPendingHeaderRect = ConvertGroupRectToAbsolute(headerRect, containerRect);
                LastPendingFoldoutToggleRect = ConvertGroupRectToAbsolute(
                    foldoutToggleRect,
                    containerRect
                );

                Event currentEvent = Event.current;
                bool expanded = pending.isExpanded;

                // Log mouse events for debugging click handling in WGroup contexts
                Rect absoluteLabelHitRect = ConvertGroupRectToAbsolute(labelHitRect, containerRect);
                LastPendingLabelHitRect = labelHitRect;
                LastPendingAbsoluteLabelHitRect = absoluteLabelHitRect;
                bool mouseInLabelRect =
                    labelHitRect.Contains(currentEvent.mousePosition)
                    || absoluteLabelHitRect.Contains(currentEvent.mousePosition);
                bool labelClickToggled = false;
                SerializableCollectionTweenDiagnostics.LogMouseEvent(
                    "PendingLabelClick",
                    propertyPath,
                    GetEffectiveMouseEventType(currentEvent),
                    currentEvent.mousePosition,
                    absoluteLabelHitRect,
                    mouseInLabelRect
                );

                labelClickToggled = TryTogglePendingFoldoutLabel(
                    currentEvent,
                    labelHitRect,
                    absoluteLabelHitRect,
                    ref expanded
                );

                EditorGUI.BeginChangeCheck();
                bool arrowExpanded = EditorGUI.Foldout(
                    foldoutToggleRect,
                    expanded,
                    GUIContent.none,
                    toggleOnLabelClick: false
                );
                if (EditorGUI.EndChangeCheck() && !labelClickToggled)
                {
                    expanded = arrowExpanded;
                }

                GUIStyle pendingLabelStyle = GetPendingFoldoutLabelStyle();
                if (!EditorUi.Suppress)
                {
                    EditorGUIUtility.AddCursorRect(labelHitRect, MouseCursor.Link);
                }
                EditorGUI.LabelField(labelRect, PendingFoldoutContent, pendingLabelStyle);

                if (expanded != pending.isExpanded)
                {
                    SerializableCollectionTweenDiagnostics.LogExpandedStateChange(
                        propertyPath,
                        pending.isExpanded,
                        expanded,
                        foldoutProgress
                    );

                    pending.isExpanded = expanded;
                    foldoutAnim = EnsurePendingFoldoutAnim(pending, propertyPath);
                    if (foldoutAnim != null)
                    {
                        foldoutAnim.target = expanded;
                        foldoutProgress = foldoutAnim.faded;

                        SerializableCollectionTweenDiagnostics.LogAnimBoolState(
                            "ExpandedStateChange",
                            propertyPath,
                            expanded,
                            foldoutAnim.faded,
                            foldoutAnim.target ? 1f : 0f,
                            foldoutAnim.speed,
                            foldoutAnim.isAnimating
                        );

                        // Additional timing diagnostic to track if animation should be running
                        SerializableCollectionTweenDiagnostics.LogAnimBoolTiming(
                            "PostExpandChange",
                            propertyPath,
                            foldoutAnim,
                            expanded
                        );
                    }
                    else
                    {
                        foldoutProgress = expanded ? 1f : 0f;
                    }

                    sectionHeight = pendingMetrics.EvaluateHeight(foldoutProgress);
                    RequestRepaint();
                }

                containerRect.height = sectionHeight;

                float contentFade = Mathf.Clamp01(foldoutProgress);

                SerializableCollectionTweenDiagnostics.LogContentFadeApplication(
                    propertyPath,
                    contentFade,
                    contentFade > 0f || pending.isExpanded,
                    contentFade <= 0f && !pending.isExpanded
                );

                if (contentFade <= 0f && !pending.isExpanded)
                {
                    GUI.EndGroup();
                    y = containerRect.yMax;
                    LastPendingValueUsedFoldoutLabel = false;
                    LastPendingValueFoldoutOffset = 0f;
                    HasLastPendingFieldRects = false;
                    LastPendingKeyFieldRect = default;
                    LastPendingValueFieldRect = default;
                    return;
                }

                innerY += rowHeight + spacing;

                Color previousColor = GUI.color;
                bool adjustColor = !Mathf.Approximately(contentFade, 1f);
                if (adjustColor)
                {
                    GUI.color = new Color(
                        previousColor.r,
                        previousColor.g,
                        previousColor.b,
                        previousColor.a * contentFade
                    );
                }

                float keyHeight = pendingMetrics.KeyHeight;
                float indentOffset = previousIndentLevel * 15f;
                bool pendingValueSupportsFoldout = PendingValueSupportsFoldout(pending, valueType);
                float pendingValueFoldoutOffset = pendingValueSupportsFoldout
                    ? PendingExpandableValueFoldoutGutter
                    : 0f;
                Rect keyRect = new(
                    resolvedSectionPadding + indentOffset + pendingValueFoldoutOffset,
                    innerY,
                    Mathf.Max(0f, innerWidth - indentOffset - pendingValueFoldoutOffset),
                    keyHeight
                );
                object previousPendingKey = pending.key;
                using (new LabelWidthScope(PendingFieldLabelWidth))
                {
                    pending.key = DrawFieldForType(
                        keyRect,
                        "Key",
                        pending.key,
                        keyType,
                        pending,
                        false
                    );
                }
                innerY += keyHeight + spacing;
                if (!ValuesEqual(previousPendingKey, pending.key))
                {
                    InvalidatePendingDuplicateCache(pending);
                }
                Rect absoluteKeyRect = ConvertGroupRectToAbsolute(keyRect, containerRect);

                float valueHeight = pendingMetrics.ValueHeight;
                Rect valueRect = new(
                    resolvedSectionPadding + indentOffset,
                    innerY,
                    Mathf.Max(0f, innerWidth - indentOffset),
                    valueHeight
                );
                bool trackSimplePendingValue = IsSimplePendingFieldType(valueType);
                object previousPendingValue = trackSimplePendingValue ? pending.value : null;
                if (pendingValueFoldoutOffset > 0f)
                {
                    valueRect.x += pendingValueFoldoutOffset;
                    valueRect.width = Mathf.Max(0f, valueRect.width - pendingValueFoldoutOffset);
                }
                if (PendingValueContentLeftShift > 0f)
                {
                    float effectiveShift = PendingValueContentLeftShift;
                    if (pendingValueSupportsFoldout && PendingFoldoutValueLeftShiftReduction > 0f)
                    {
                        effectiveShift = Mathf.Max(
                            0f,
                            effectiveShift - PendingFoldoutValueLeftShiftReduction
                        );
                    }

                    if (effectiveShift > 0f)
                    {
                        float shift = Mathf.Min(
                            effectiveShift,
                            Mathf.Max(0f, valueRect.x - resolvedSectionPadding)
                        );
                        valueRect.x -= shift;
                        valueRect.width += shift;
                    }
                }
                if (pendingValueSupportsFoldout && PendingFoldoutValueRightShift > 0f)
                {
                    float shift = Mathf.Min(PendingFoldoutValueRightShift, valueRect.width);
                    valueRect.x += shift;
                    valueRect.width = Mathf.Max(0f, valueRect.width - shift);
                }
                using (new LabelWidthScope(PendingFieldLabelWidth))
                {
                    pending.value = DrawFieldForType(
                        valueRect,
                        "Value",
                        pending.value,
                        valueType,
                        pending,
                        true
                    );
                }
                if (trackSimplePendingValue && !ValuesEqual(previousPendingValue, pending.value))
                {
                    MarkPendingValueChanged(pending);
                }
                LastPendingValueUsedFoldoutLabel = pendingValueSupportsFoldout;
                LastPendingValueFoldoutOffset = pendingValueFoldoutOffset;
                if (
                    pendingValueSupportsFoldout
                    && pending.valueWrapperProperty is { isExpanded: false }
                )
                {
                    pending.valueWrapperProperty.isExpanded = true;
                }
                Rect absoluteValueRect = ConvertGroupRectToAbsolute(valueRect, containerRect);
                TrackPendingFieldRects(absoluteKeyRect, absoluteValueRect);
                innerY += valueHeight + spacing;

                bool keySupported = IsTypeSupported(keyType);
                bool valueSupported = IsTypeSupported(valueType);
                string pendingKeyString = pending.key as string;
                bool keyValid = KeyIsValid(keyType, pending.key);
                bool isBlankStringKey =
                    keyType == typeof(string) && string.IsNullOrWhiteSpace(pendingKeyString);
                bool canCommit = keySupported && valueSupported && (keyValid || isBlankStringKey);

                int existingIndex = FindExistingKeyIndex(
                    dictionaryProperty,
                    keysProperty,
                    keyType,
                    pending.key
                );
                string buttonLabel = existingIndex >= 0 ? "Overwrite" : "Add";
                bool entryAlreadyExists =
                    existingIndex >= 0
                    && EntryMatchesExisting(
                        keysProperty,
                        valuesProperty,
                        existingIndex,
                        keyType,
                        valueType,
                        pending
                    );
                if (entryAlreadyExists)
                {
                    canCommit = false;
                }

                Rect buttonsRect = new(resolvedSectionPadding, innerY, innerWidth, rowHeight);
                float resetWidth = 70f;
                Rect addRect = new(buttonsRect.x, buttonsRect.y, PendingAddButtonWidth, rowHeight);
                Rect resetRect = new(addRect.xMax + spacing, buttonsRect.y, resetWidth, rowHeight);

                float infoX = resetRect.xMax + spacing;
                float availableInfoWidth = Mathf.Max(0f, buttonsRect.xMax - infoX);
                string infoMessage;
                if (entryAlreadyExists)
                {
                    infoMessage = "Entry already exists with the same value.";
                }
                else
                {
                    infoMessage = GetPendingInfoMessage(
                        keySupported,
                        valueSupported,
                        keyValid || isBlankStringKey,
                        existingIndex,
                        keyType,
                        valueType
                    );
                    if (string.IsNullOrEmpty(infoMessage) && isBlankStringKey)
                    {
                        string descriptor = string.IsNullOrEmpty(pendingKeyString)
                            ? "empty"
                            : "whitespace-only";
                        infoMessage = $"Adding entry with {descriptor} string key.";
                    }
                }
                if (availableInfoWidth > 0f && !string.IsNullOrEmpty(infoMessage))
                {
                    Rect infoRect = new(infoX, buttonsRect.y, availableInfoWidth, rowHeight);
                    GUI.Label(infoRect, infoMessage, EditorStyles.miniLabel);
                }

                using (new EditorGUI.DisabledScope(!canCommit))
                {
                    string tooltip;
                    if (entryAlreadyExists)
                    {
                        tooltip = "Current item already exists in the dictionary.";
                    }
                    else if (existingIndex >= 0)
                    {
                        tooltip = "Overwrite the existing entry with this key.";
                    }
                    else if (isBlankStringKey)
                    {
                        string descriptor = string.IsNullOrEmpty(pendingKeyString)
                            ? "empty string"
                            : "whitespace-only string";
                        tooltip = $"Add a new entry using a {descriptor} key.";
                    }
                    else
                    {
                        tooltip = "Add a new entry to the dictionary.";
                    }

                    GUIContent addContent = EditorGUIUtility.TrTextContent(buttonLabel, tooltip);

                    string styleKey =
                        existingIndex >= 0 ? "Overwrite"
                        : isBlankStringKey ? "AddEmpty"
                        : "Add";
                    GUIStyle addStyle = SolidButtonStyles.GetSolidButtonStyle(
                        styleKey,
                        GUI.enabled
                    );

                    if (GUI.Button(addRect, addContent, addStyle))
                    {
                        CommitResult result = CommitEntry(
                            keysProperty,
                            valuesProperty,
                            keyType,
                            valueType,
                            pending,
                            existingIndex,
                            dictionaryProperty
                        );
                        if (result.added)
                        {
                            pending.key = GetDefaultValue(keyType);
                            pending.value = GetDefaultValue(valueType);
                            InvalidatePendingDuplicateCache(pending);
                            MarkPendingValueChanged(pending);
                            ReleasePendingWrapper(pending, false);
                            ReleasePendingWrapper(pending, true);
                        }

                        if (result.index >= 0)
                        {
                            int targetPage = GetPageForIndex(result.index, pagination.pageSize);
                            int totalPages = GetTotalPages(
                                keysProperty.arraySize,
                                pagination.pageSize
                            );
                            pagination.pageIndex = Mathf.Clamp(targetPage, 0, totalPages - 1);
                            pagination.selectedIndex = result.index;

                            string listKey = GetListKey(dictionaryProperty);
                            ListPageCache cache = EnsurePageCache(
                                listKey,
                                keysProperty,
                                pagination
                            );
                            SyncListSelectionWithPagination(list, pagination, cache);
                            InvalidatePendingDuplicateCache(listKey);
                        }

                        GUI.FocusControl(null);
                    }
                }

                bool isPendingDefault = PendingEntryIsAtDefault(pending, keyType, valueType);
                GUIContent resetContent = EditorGUIUtility.TrTextContent(
                    "Reset",
                    "Restore pending key/value to their defaults"
                );
                using (new EditorGUI.DisabledScope(isPendingDefault))
                {
                    GUIStyle resetStyle = SolidButtonStyles.GetSolidButtonStyle(
                        "Reset",
                        GUI.enabled
                    );
                    if (GUI.Button(resetRect, resetContent, resetStyle))
                    {
                        ResetPendingEntryToDefault(pending, keyType, valueType);
                        GUI.FocusControl(null);
                    }
                }

                if (adjustColor)
                {
                    GUI.color = previousColor;
                }

                GUI.EndGroup();

                y = containerRect.yMax;
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        private static float ResolvePendingFoldoutToggleOffset(
            SerializedProperty dictionaryProperty
        )
        {
            SerializedObject serializedObject = dictionaryProperty?.serializedObject;
            if (!TargetsUnityHelpersSettings(serializedObject))
            {
                return PendingFoldoutToggleOffset;
            }

            return PendingFoldoutToggleOffsetProjectSettings;
        }

        private static float ResolvePendingSectionPadding(SerializedProperty dictionaryProperty)
        {
            SerializedObject serializedObject = dictionaryProperty?.serializedObject;
            if (!TargetsUnityHelpersSettings(serializedObject))
            {
                return PendingSectionPadding;
            }

            return PendingSectionPaddingProjectSettings;
        }

        private static float ResolvePendingFoldoutLabelContentOffset(
            SerializedProperty dictionaryProperty
        )
        {
            SerializedObject serializedObject = dictionaryProperty?.serializedObject;
            if (TargetsUnityHelpersSettings(serializedObject))
            {
                return PendingFoldoutLabelContentOffset;
            }

            return PendingFoldoutLabelContentOffset - PendingFoldoutInspectorLabelShift;
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

        private static string GetPendingInfoMessage(
            bool keySupported,
            bool valueSupported,
            bool keyValid,
            int existingIndex,
            Type keyType,
            Type valueType
        )
        {
            if (!keySupported)
            {
                return BuildUnsupportedTypeMessage("Unsupported key type (", keyType);
            }

            if (!valueSupported)
            {
                return BuildUnsupportedTypeMessage("Unsupported value type (", valueType);
            }

            if (!keyValid)
            {
                return "Enter a valid key to continue.";
            }

            if (existingIndex >= 0)
            {
                return "Existing key will be overwritten.";
            }

            return "Ready to add entry.";
        }

        private static string BuildUnsupportedTypeMessage(string prefix, Type type)
        {
            using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                prefix.Length + (type?.Name?.Length ?? 0) + 8,
                out StringBuilder builder
            );
            builder.Clear();
            builder.Append(prefix);
            builder.Append(type?.Name ?? "Unknown");
            builder.Append(')');
            return builder.ToString();
        }

        private static bool PendingEntryIsAtDefault(
            PendingEntry pending,
            Type keyType,
            Type valueType
        )
        {
            object defaultKey = GetDefaultValue(keyType);
            object defaultValue = GetDefaultValue(valueType);
            return ValuesEqual(pending.key, defaultKey) && ValuesEqual(pending.value, defaultValue);
        }

        private static void ResetPendingEntryToDefault(
            PendingEntry pending,
            Type keyType,
            Type valueType
        )
        {
            pending.key = GetDefaultValue(keyType);
            InvalidatePendingDuplicateCache(pending);
            pending.value = GetDefaultValue(valueType);
            MarkPendingValueChanged(pending);
            ReleasePendingWrapper(pending, false);
            ReleasePendingWrapper(pending, true);
        }

        private static PendingSectionMetrics CalculatePendingSectionMetrics(
            PendingEntry pending,
            Type keyType,
            Type valueType,
            SerializedProperty dictionaryProperty
        )
        {
            float rowHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float keyHeight = GetPendingFieldHeight(pending, keyType, isValueField: false);
            float valueHeight = GetPendingFieldHeight(pending, valueType, isValueField: true);
            float resolvedSectionPadding = ResolvePendingSectionPadding(dictionaryProperty);
            float collapsedHeight = rowHeight + resolvedSectionPadding * 2f;
            float expandedExtraHeight =
                spacing + keyHeight + spacing + valueHeight + spacing + rowHeight;
            return new PendingSectionMetrics(
                collapsedHeight,
                expandedExtraHeight,
                keyHeight,
                valueHeight
            );
        }

        private static float GetPendingSectionHeight(
            PendingEntry pending,
            Type keyType,
            Type valueType,
            SerializedProperty dictionaryProperty
        )
        {
            PendingSectionMetrics metrics = CalculatePendingSectionMetrics(
                pending,
                keyType,
                valueType,
                dictionaryProperty
            );
            string propertyPath = dictionaryProperty?.propertyPath;
            float progress = GetPendingFoldoutProgress(pending, propertyPath);
            float height = metrics.EvaluateHeight(progress);

            SerializableCollectionTweenDiagnostics.LogPendingSectionHeightCalc(
                propertyPath ?? "(unknown)",
                metrics.CollapsedHeight,
                metrics.ExpandedExtraHeight,
                progress,
                height
            );

            return height;
        }

        private readonly struct PendingSectionMetrics
        {
            public float CollapsedHeight { get; }
            public float ExpandedExtraHeight { get; }
            public float KeyHeight { get; }
            public float ValueHeight { get; }

            public PendingSectionMetrics(
                float collapsedHeight,
                float expandedExtraHeight,
                float keyHeight,
                float valueHeight
            )
            {
                CollapsedHeight = collapsedHeight;
                ExpandedExtraHeight = expandedExtraHeight;
                KeyHeight = keyHeight;
                ValueHeight = valueHeight;
            }

            public float EvaluateHeight(float foldoutProgress)
            {
                return CollapsedHeight + ExpandedExtraHeight * Mathf.Clamp01(foldoutProgress);
            }
        }

        internal CommitResult CommitEntry(
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            Type keyType,
            Type valueType,
            object key,
            object value,
            SerializedProperty dictionaryProperty,
            int existingIndex = -1,
            bool isSortedDictionary = false
        )
        {
            PendingEntry pending = new()
            {
                key = key,
                value = value,
                isSorted = isSortedDictionary,
            };
            return CommitEntry(
                keysProperty,
                valuesProperty,
                keyType,
                valueType,
                pending,
                existingIndex,
                dictionaryProperty
            );
        }

        private CommitResult CommitEntry(
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            Type keyType,
            Type valueType,
            PendingEntry pending,
            int existingIndex,
            SerializedProperty dictionaryProperty
        )
        {
            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            bool isScriptableSingletonTarget =
                targets.Length > 0 && IsScriptableSingletonType(targets[0]);

            int keysArraySizeBefore = keysProperty.arraySize;
            PaletteSerializationDiagnostics.ReportCommitEntryStart(
                serializedObject,
                dictionaryProperty.propertyPath,
                keysArraySizeBefore,
                pending.key,
                pending.value,
                existingIndex
            );

            // For ScriptableSingleton targets, ApplyModifiedProperties doesn't work correctly.
            // We need to directly modify the dictionary via reflection and then save.
            if (isScriptableSingletonTarget)
            {
                return CommitEntryForScriptableSingleton(
                    targets,
                    dictionaryProperty,
                    keyType,
                    valueType,
                    pending,
                    existingIndex,
                    serializedObject
                );
            }

            if (targets.Length > 0)
            {
                string undoLabel =
                    existingIndex >= 0 ? "Overwrite Dictionary Entry" : "Add Dictionary Entry";
                Undo.RecordObjects(targets, undoLabel);
            }

            bool addedNewEntry = false;
            int affectedIndex;

            if (existingIndex >= 0)
            {
                SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(
                    existingIndex
                );
                SetPropertyValue(valueProperty, pending.value, valueType);
                RegisterPaletteManualEditForKey(dictionaryProperty, pending.key, keyType);
                affectedIndex = existingIndex;
            }
            else
            {
                int insertIndex = keysProperty.arraySize;
                keysProperty.InsertArrayElementAtIndex(insertIndex);
                valuesProperty.InsertArrayElementAtIndex(insertIndex);

                PaletteSerializationDiagnostics.ReportCommitEntryArrayInsert(
                    serializedObject,
                    dictionaryProperty.propertyPath,
                    insertIndex,
                    keysProperty.arraySize,
                    valuesProperty.arraySize
                );

                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(insertIndex);
                SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(
                    insertIndex
                );

                ClearArrayElement(keyProperty);
                ClearArrayElement(valueProperty);

                SetPropertyValue(keyProperty, pending.key, keyType);
                SetPropertyValue(valueProperty, pending.value, valueType);
                RegisterPaletteManualEditForKey(dictionaryProperty, pending.key, keyType);
                addedNewEntry = true;
                affectedIndex = insertIndex;
            }

            bool hadModifiedBefore = serializedObject.hasModifiedProperties;
            bool applyResult = ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                dictionaryProperty,
                "CommitEntry"
            );
            bool hasModifiedAfter = serializedObject.hasModifiedProperties;

            PaletteSerializationDiagnostics.ReportCommitEntryApplyResult(
                serializedObject,
                dictionaryProperty.propertyPath,
                applyResult,
                hadModifiedBefore,
                hasModifiedAfter
            );

            serializedObject.Update();
            SyncRuntimeDictionary(dictionaryProperty);

            if (targets.Length > 0)
            {
                foreach (Object target in targets)
                {
                    if (target != null)
                    {
                        EditorUtility.SetDirty(target);
                    }
                }
            }

            string listKey = GetListKey(dictionaryProperty);
            MarkListCacheDirty(listKey);
            InvalidateKeyCache(listKey);
            GUI.changed = true;

            // Re-fetch keys property to get updated array size after Update()
            SerializedProperty updatedKeysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            int finalKeysSize = updatedKeysProperty?.arraySize ?? -1;

            PaletteSerializationDiagnostics.ReportCommitEntryComplete(
                serializedObject,
                dictionaryProperty.propertyPath,
                addedNewEntry,
                affectedIndex,
                finalKeysSize
            );

            return new CommitResult { added = addedNewEntry, index = affectedIndex };
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
        /// Uses cached reflection for better performance.
        /// </summary>
        internal static void SaveScriptableSingleton(Object target)
        {
            if (target == null)
            {
                return;
            }

            // Find the Save method on ScriptableSingleton<T> using cached reflection
            Type type = target.GetType();
            MethodInfo saveMethod = null;
            while (type != null && saveMethod == null)
            {
                ReflectionHelpers.TryGetMethod(
                    type,
                    "Save",
                    out saveMethod,
                    new[] { typeof(bool) },
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                type = type.BaseType;
            }

            if (saveMethod != null)
            {
                saveMethod.Invoke(target, new object[] { true });
            }
        }

        /// <summary>
        /// Handles CommitEntry for ScriptableSingleton targets where ApplyModifiedProperties doesn't work.
        /// We directly modify the dictionary via its runtime interface, then save and refresh.
        /// </summary>
        private CommitResult CommitEntryForScriptableSingleton(
            Object[] targets,
            SerializedProperty dictionaryProperty,
            Type keyType,
            Type valueType,
            PendingEntry pending,
            int existingIndex,
            SerializedObject serializedObject
        )
        {
            bool addedNewEntry = false;
            int affectedIndex = existingIndex >= 0 ? existingIndex : -1;
            string propertyPath = dictionaryProperty.propertyPath;

            foreach (Object target in targets)
            {
                if (target == null)
                {
                    continue;
                }

                // Get the dictionary instance directly
                object dictionaryInstance = GetTargetObjectOfProperty(target, propertyPath);
                if (dictionaryInstance == null)
                {
                    continue;
                }

                // Use cached reflection to add/update the dictionary entry
                Type dictionaryType = dictionaryInstance.GetType();

                // Check if key already exists in the dictionary, even if existingIndex wasn't provided
                bool keyExists = existingIndex >= 0;
                if (!keyExists)
                {
                    if (
                        ReflectionHelpers.TryGetMethod(
                            dictionaryType,
                            "ContainsKey",
                            out MethodInfo containsKeyMethod,
                            new[] { keyType },
                            BindingFlags.Instance | BindingFlags.Public
                        )
                    )
                    {
                        keyExists = (bool)
                            containsKeyMethod.Invoke(dictionaryInstance, new[] { pending.key });
                    }
                }

                if (keyExists)
                {
                    // Update existing entry - use the indexer
                    // Use TryGetIndexerProperty with exact types to avoid AmbiguousMatchException
                    // when the type implements both IDictionary<TKey,TValue> and IDictionary
                    if (
                        ReflectionHelpers.TryGetIndexerProperty(
                            dictionaryType,
                            valueType,
                            new[] { keyType },
                            out PropertyInfo indexer
                        ) && indexer.CanWrite
                    )
                    {
                        Action<object, object, object[]> indexerSetter =
                            ReflectionHelpers.GetIndexerSetter(indexer);
                        indexerSetter(dictionaryInstance, pending.value, new[] { pending.key });
                    }
                }
                else
                {
                    // Add new entry - use Add method or indexer
                    if (
                        ReflectionHelpers.TryGetMethod(
                            dictionaryType,
                            "Add",
                            out MethodInfo addMethod,
                            new[] { keyType, valueType },
                            BindingFlags.Instance | BindingFlags.Public
                        )
                    )
                    {
                        addMethod.Invoke(dictionaryInstance, new[] { pending.key, pending.value });
                        addedNewEntry = true;

                        // Get the new index (count - 1 since we just added)
                        if (
                            ReflectionHelpers.TryGetProperty(
                                dictionaryType,
                                "Count",
                                out PropertyInfo countProp,
                                BindingFlags.Instance | BindingFlags.Public
                            )
                        )
                        {
                            Func<object, object> countGetter = ReflectionHelpers.GetPropertyGetter(
                                countProp
                            );
                            int count = (int)countGetter(dictionaryInstance);
                            affectedIndex = count - 1;
                        }
                    }
                    else
                    {
                        // Try indexer as fallback
                        // Use TryGetIndexerProperty with exact types to avoid AmbiguousMatchException
                        // when the type implements both IDictionary<TKey,TValue> and IDictionary
                        if (
                            ReflectionHelpers.TryGetIndexerProperty(
                                dictionaryType,
                                valueType,
                                new[] { keyType },
                                out PropertyInfo indexer
                            ) && indexer.CanWrite
                        )
                        {
                            Action<object, object, object[]> indexerSetter =
                                ReflectionHelpers.GetIndexerSetter(indexer);
                            indexerSetter(dictionaryInstance, pending.value, new[] { pending.key });
                            addedNewEntry = true;
                        }
                    }
                }

                // Sync the runtime dictionary to its serialized arrays
                if (dictionaryInstance is SerializableDictionaryBase baseDictionary)
                {
                    baseDictionary.EditorSyncSerializedArrays();
                }

                RegisterPaletteManualEditForKey(dictionaryProperty, pending.key, keyType);
                EditorUtility.SetDirty(target);

                // Save the ScriptableSingleton - call Save(true) via reflection
                // UnityHelpersSettings has its own SaveSettings method that does additional work
                if (target is UnityHelpersSettings unitySettings)
                {
                    unitySettings.SaveSettings();
                }
                else
                {
                    SaveScriptableSingleton(target);
                }
            }

            // Refresh the serialized object to show the changes
            serializedObject.Update();

            string listKey = GetListKey(dictionaryProperty);
            MarkListCacheDirty(listKey);
            InvalidateKeyCache(listKey);
            GUI.changed = true;

            // Re-fetch keys property to get updated array size after Update()
            SerializedProperty updatedKeysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            int finalKeysSize = updatedKeysProperty?.arraySize ?? -1;

            // If we updated an existing key but didn't have the existingIndex, find it now
            if (affectedIndex < 0 && !addedNewEntry && updatedKeysProperty != null)
            {
                affectedIndex = FindExistingKeyIndex(
                    dictionaryProperty,
                    updatedKeysProperty,
                    keyType,
                    pending.key
                );
            }

            PaletteSerializationDiagnostics.ReportCommitEntryComplete(
                serializedObject,
                dictionaryProperty.propertyPath,
                addedNewEntry,
                affectedIndex,
                finalKeysSize
            );

            return new CommitResult { added = addedNewEntry, index = affectedIndex };
        }

        private static bool TryResolveKeyValueTypes(
            FieldInfo fieldInfo,
            out Type keyType,
            out Type valueType,
            out bool isSortedDictionary
        )
        {
            keyType = null;
            valueType = null;
            isSortedDictionary = false;
            if (fieldInfo == null)
            {
                return false;
            }

            Type type = fieldInfo.FieldType;
            while (type != null)
            {
                if (type.IsGenericType)
                {
                    Type genericDefinition = type.GetGenericTypeDefinition();
                    if (
                        genericDefinition == typeof(SerializableSortedDictionary<,>)
                        || genericDefinition == typeof(SerializableSortedDictionary<,,>)
                        || genericDefinition == typeof(SerializableSortedDictionaryBase<,,>)
                    )
                    {
                        Type[] args = type.GetGenericArguments();
                        keyType = args[0];
                        valueType = args[1];
                        isSortedDictionary = true;
                        return true;
                    }

                    if (
                        genericDefinition == typeof(SerializableDictionary<,>)
                        || genericDefinition == typeof(SerializableDictionary<,,>)
                        || genericDefinition == typeof(SerializableDictionaryBase<,,>)
                    )
                    {
                        Type[] args = type.GetGenericArguments();
                        keyType = args[0];
                        valueType = args[1];
                        isSortedDictionary = false;
                        return true;
                    }
                }

                type = type.BaseType;
            }

            return false;
        }

        private static object GetDictionaryInstance(SerializedProperty dictionaryProperty)
        {
            if (dictionaryProperty == null)
            {
                return null;
            }

            try
            {
                SerializedObject serializedObject = dictionaryProperty.serializedObject;
                if (serializedObject == null)
                {
                    return null;
                }

                Object targetObject = serializedObject.targetObject;
                if (targetObject == null)
                {
                    return null;
                }

                return GetTargetObjectOfProperty(targetObject, dictionaryProperty.propertyPath);
            }
            catch (ArgumentNullException)
            {
                // SerializedObject may have been disposed, causing ArgumentNullException
                // when accessing targetObject on a disposed native object
                return null;
            }
            catch (ObjectDisposedException)
            {
                // SerializedObject has been explicitly disposed
                return null;
            }
        }

        private static bool TryResolveKeyValueTypesFromInstance(
            object dictionaryInstance,
            out Type keyType,
            out Type valueType,
            out bool isSortedDictionary
        )
        {
            keyType = null;
            valueType = null;
            isSortedDictionary = false;

            if (dictionaryInstance == null)
            {
                return false;
            }

            Type type = dictionaryInstance.GetType();
            while (type != null)
            {
                if (type.IsGenericType)
                {
                    Type genericDefinition = type.GetGenericTypeDefinition();
                    if (
                        genericDefinition == typeof(SerializableSortedDictionary<,>)
                        || genericDefinition == typeof(SerializableSortedDictionary<,,>)
                        || genericDefinition == typeof(SerializableSortedDictionaryBase<,,>)
                    )
                    {
                        Type[] args = type.GetGenericArguments();
                        keyType = args[0];
                        valueType = args[1];
                        isSortedDictionary = true;
                        return true;
                    }

                    if (
                        genericDefinition == typeof(SerializableDictionary<,>)
                        || genericDefinition == typeof(SerializableDictionary<,,>)
                        || genericDefinition == typeof(SerializableDictionaryBase<,,>)
                    )
                    {
                        Type[] args = type.GetGenericArguments();
                        keyType = args[0];
                        valueType = args[1];
                        isSortedDictionary = false;
                        return true;
                    }
                }

                type = type.BaseType;
            }

            return false;
        }

        private static object ResolveComparerInstance(
            SerializedProperty dictionaryProperty,
            object dictionaryInstance,
            Type keyType
        )
        {
            object instance = dictionaryInstance ?? GetDictionaryInstance(dictionaryProperty);
            if (instance == null)
            {
                return CreateDefaultComparer(keyType);
            }

            if (
                ReflectionHelpers.TryGetProperty(
                    instance.GetType(),
                    "Comparer",
                    out PropertyInfo comparerProperty,
                    BindingFlags.Public | BindingFlags.Instance
                )
            )
            {
                Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(comparerProperty);
                object comparerValue = getter(instance);
                if (comparerValue != null)
                {
                    return comparerValue;
                }
            }

            return CreateDefaultComparer(keyType);
        }

        private static object CreateDefaultComparer(Type keyType)
        {
            if (keyType == null)
            {
                return null;
            }

            Type comparerType = typeof(Comparer<>).MakeGenericType(keyType);
            if (
                ReflectionHelpers.TryGetProperty(
                    comparerType,
                    "Default",
                    out PropertyInfo defaultProperty,
                    BindingFlags.Public | BindingFlags.Static
                )
            )
            {
                Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(defaultProperty);
                return getter(null);
            }
            return null;
        }

        private static Func<object, object, int> CreateComparisonDelegate(
            object comparer,
            Type keyType
        )
        {
            object comparerInstance = comparer ?? CreateDefaultComparer(keyType);
            if (comparerInstance == null || keyType == null)
            {
                return null;
            }

            if (
                !ReflectionHelpers.TryGetMethod(
                    comparerInstance.GetType(),
                    "Compare",
                    out MethodInfo compareMethod,
                    new[] { keyType, keyType },
                    BindingFlags.Instance | BindingFlags.Public
                )
            )
            {
                return null;
            }

            return delegate(object left, object right)
            {
                object leftValue = ConvertKeyForComparison(left, keyType);
                object rightValue = ConvertKeyForComparison(right, keyType);
                object result = compareMethod.Invoke(
                    comparerInstance,
                    new[] { leftValue, rightValue }
                );
                return result != null ? Convert.ToInt32(result, CultureInfo.InvariantCulture) : 0;
            };
        }

        private static bool DrawRowFoldoutValue(
            Rect valueRect,
            SerializedProperty valueProperty,
            GUIContent valueLabel,
            float valueColumnWidth,
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
            renderedHeight = headerHeight;

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(headerRect, valueProperty, valueLabel, includeChildren: false);
            if (EditorGUI.EndChangeCheck())
            {
                changed = true;
            }

            if (!valueProperty.isExpanded || !valueProperty.hasVisibleChildren)
            {
                return changed;
            }

            float childY = headerRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty iterator = valueProperty.Copy();
            SerializedProperty endProperty = iterator.GetEndProperty();
            bool enterChildren = true;
            int baseDepth = valueProperty.depth;
            int previousIndent = EditorGUI.indentLevel;
            int baseIndent = Mathf.Max(0, previousIndent - 1);

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

                int relativeDepth = Mathf.Max(0, iterator.depth - (baseDepth + 1));
                EditorGUI.indentLevel = baseIndent + relativeDepth;

                float childLabelWidth = CalculateRowChildLabelWidth(valueColumnWidth, iterator);
                float childHeight = EditorGUI.GetPropertyHeight(iterator, true);
                Rect childRect = new(
                    valueRect.x + DictionaryRowChildHorizontalPadding,
                    childY,
                    Mathf.Max(0f, valueRect.width - DictionaryRowChildHorizontalPadding * 2f),
                    childHeight
                );

                using (new LabelWidthScope(childLabelWidth))
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.PropertyField(childRect, iterator, true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        changed = true;
                    }
                }

                TrackRowChildLayout(childRect, childLabelWidth);

                childY = childRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            }

            renderedHeight = Mathf.Max(
                headerHeight,
                (childY - EditorGUIUtility.standardVerticalSpacing) - valueRect.y
            );
            EditorGUI.indentLevel = previousIndent;
            return changed;
        }

        private static float CalculateRowChildLabelWidth(
            float valueColumnWidth,
            SerializedProperty property
        )
        {
            float ratioWidth = Mathf.Max(0f, valueColumnWidth * DictionaryRowChildLabelWidthRatio);
            float measuredWidth = ratioWidth;
            if (property != null)
            {
                RowChildLabelContent.text = property.displayName ?? string.Empty;
                RowChildLabelContent.image = null;
                RowChildLabelContent.tooltip = null;
                Vector2 labelSize = GetRowChildLabelStyle().CalcSize(RowChildLabelContent);
                measuredWidth = Mathf.Max(0f, labelSize.x + DictionaryRowChildLabelTextPadding);
            }

            float availableWidth = Mathf.Max(
                0f,
                valueColumnWidth - DictionaryRowChildHorizontalPadding * 2f
            );
            float maxAllowed = Mathf.Min(
                DictionaryRowChildLabelWidthMax,
                Mathf.Max(DictionaryRowChildLabelWidthMin, availableWidth)
            );

            float targetWidth = measuredWidth > 0f ? measuredWidth : ratioWidth;
            return Mathf.Clamp(targetWidth, DictionaryRowChildLabelWidthMin, maxAllowed);
        }

        private static GUIStyle GetRowChildLabelStyle()
        {
            if (_rowChildLabelStyle == null)
            {
                _rowChildLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    richText = false,
                    wordWrap = false,
                };
            }

            return _rowChildLabelStyle;
        }

        private static void TrackRowChildLayout(Rect childRect, float labelWidth)
        {
            HasLastRowChildContentRect = true;
            LastRowChildContentRect = childRect;

            if (!HasRowChildLabelWidthData)
            {
                HasRowChildLabelWidthData = true;
                LastRowChildMinLabelWidth = labelWidth;
                LastRowChildMaxLabelWidth = labelWidth;
                return;
            }

            LastRowChildMinLabelWidth = Mathf.Min(LastRowChildMinLabelWidth, labelWidth);
            LastRowChildMaxLabelWidth = Mathf.Max(LastRowChildMaxLabelWidth, labelWidth);
        }

        private static void TrackPendingFieldRects(Rect keyRect, Rect valueRect)
        {
            HasLastPendingFieldRects = true;
            LastPendingKeyFieldRect = keyRect;
            LastPendingValueFieldRect = valueRect;
        }

        private static Func<object, object, int> CreateManualKeyComparisonDelegate(Type keyType)
        {
            if (!KeyTypeSupportsManualSorting(keyType))
            {
                return null;
            }

            if (IsUnityObjectKey(keyType))
            {
                return CreateUnityObjectComparisonDelegate(keyType);
            }

            return CreateComparisonDelegate(null, keyType);
        }

        private static Func<object, object, int> CreateUnityObjectComparisonDelegate(Type keyType)
        {
            if (!IsUnityObjectKey(keyType))
            {
                return null;
            }

            return delegate(object left, object right)
            {
                Object leftObject = ConvertKeyToUnityObject(left);
                Object rightObject = ConvertKeyToUnityObject(right);
                return UnityObjectNameComparer<Object>.Instance.Compare(leftObject, rightObject);
            };
        }

        private static Object ConvertKeyToUnityObject(object key)
        {
            return key as Object;
        }

        internal static bool ShouldShowDictionarySortButton(
            SerializedProperty keysProperty,
            Type keyType,
            int itemCount,
            Func<object, object, int> comparison
        )
        {
            if (comparison == null || keyType == null || keysProperty == null || itemCount <= 1)
            {
                return false;
            }

            return !KeysAreSorted(keysProperty, keyType, comparison);
        }

        private static bool KeyTypeSupportsManualSorting(Type keyType)
        {
            if (keyType == null)
            {
                return false;
            }

            if (IsUnityObjectKey(keyType))
            {
                return true;
            }

            Type candidate = Nullable.GetUnderlyingType(keyType) ?? keyType;
            if (typeof(IComparable).IsAssignableFrom(candidate))
            {
                return true;
            }

            Type genericComparable = typeof(IComparable<>).MakeGenericType(candidate);
            return genericComparable.IsAssignableFrom(candidate);
        }

        private static bool IsUnityObjectKey(Type keyType)
        {
            return keyType != null && typeof(Object).IsAssignableFrom(keyType);
        }

        private static object ConvertKeyForComparison(object key, Type keyType)
        {
            if (key == null)
            {
                return null;
            }

            if (keyType.IsInstanceOfType(key))
            {
                return key;
            }

            if (keyType.IsEnum)
            {
                return Enum.ToObject(keyType, key);
            }

            try
            {
                return Convert.ChangeType(key, keyType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return key;
            }
        }

        internal static bool KeysAreSorted(
            SerializedProperty keysProperty,
            Type keyType,
            Func<object, object, int> comparison
        )
        {
            if (keysProperty == null || comparison == null)
            {
                return false;
            }

            int count = keysProperty.arraySize;
            if (count <= 1)
            {
                return true;
            }

            SerializedProperty firstProperty = keysProperty.GetArrayElementAtIndex(0);
            object previous = GetPropertyValue(firstProperty, keyType);

            for (int index = 1; index < count; index++)
            {
                SerializedProperty currentProperty = keysProperty.GetArrayElementAtIndex(index);
                object currentKey = GetPropertyValue(currentProperty, keyType);
                int compareResult = comparison(previous, currentKey);
                if (compareResult > 0)
                {
                    return false;
                }

                previous = currentKey;
            }

            return true;
        }

        internal static bool PageEntriesNeedSorting(
            ListPageCache cache,
            SerializedProperty keysProperty,
            Type keyType,
            Func<object, object, int> comparison
        )
        {
            if (
                cache == null
                || cache.entries == null
                || cache.entries.Count <= 1
                || keysProperty == null
                || comparison == null
                || keyType == null
            )
            {
                return false;
            }

            object previousKey = null;
            bool hasPrevious = false;

            for (int i = 0; i < cache.entries.Count; i++)
            {
                PageEntry entry = cache.entries[i];
                if (entry == null)
                {
                    continue;
                }

                int arrayIndex = entry.arrayIndex;
                if (arrayIndex < 0 || arrayIndex >= keysProperty.arraySize)
                {
                    continue;
                }

                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(arrayIndex);
                object currentKey = GetPropertyValue(keyProperty, keyType);
                if (!hasPrevious)
                {
                    previousKey = currentKey;
                    hasPrevious = true;
                    continue;
                }

                int compareResult = comparison(previousKey, currentKey);
                if (compareResult > 0)
                {
                    return true;
                }

                previousKey = currentKey;
            }

            return false;
        }

        internal void SortDictionaryEntries(
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            Type keyType,
            Type valueType,
            Func<object, object, int> comparison,
            PaginationState pagination,
            ReorderableList list
        )
        {
            if (comparison == null)
            {
                return;
            }

            int count = keysProperty.arraySize;
            if (count <= 1)
            {
                return;
            }

            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets.Length > 0)
            {
                Undo.RecordObjects(targets, "Sort Dictionary Keys");
            }

            PooledResource<List<string>> paletteKeysBeforeLease = default;
            PooledResource<List<string>> paletteKeysAfterSortLease = default;
            PooledResource<List<string>> paletteKeysSerializedAfterLease = default;

            try
            {
                bool logPaletteSort = PaletteSerializationDiagnostics.ShouldLogPaletteSort(
                    dictionaryProperty,
                    keyType
                );
                List<string> paletteKeysBefore = null;
                List<string> paletteKeysAfterSort = null;
                if (logPaletteSort)
                {
                    paletteKeysBeforeLease = Buffers<string>.GetList(
                        keysProperty?.arraySize ?? 0,
                        out paletteKeysBefore
                    );
                    CaptureStringKeyOrder(keysProperty, paletteKeysBefore);
                }

                object selectedKey = null;
                int selectedIndex = pagination.selectedIndex;
                if (selectedIndex >= 0 && selectedIndex < count)
                {
                    SerializedProperty selectedProperty = keysProperty.GetArrayElementAtIndex(
                        selectedIndex
                    );
                    selectedKey = GetPropertyValue(selectedProperty, keyType);
                }

                string listKey = null;
                using PooledResource<List<KeyValueSnapshot>> entriesLease =
                    Buffers<KeyValueSnapshot>.GetList(count, out List<KeyValueSnapshot> entries);
                {
                    for (int index = 0; index < count; index++)
                    {
                        SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                        SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(
                            index
                        );
                        KeyValueSnapshot snapshot = new()
                        {
                            key = GetPropertyValue(keyProperty, keyType),
                            value = GetPropertyValue(valueProperty, valueType),
                            originalIndex = index,
                        };
                        entries.Add(snapshot);
                    }

                    KeyValueSnapshotComparer comparer = new(comparison);
                    entries.Sort(comparer);
                    using PooledResource<List<int>> orderedIndicesLease = Buffers<int>.GetList(
                        entries.Count,
                        out List<int> orderedIndices
                    );
                    {
                        for (int index = 0; index < entries.Count; index++)
                        {
                            orderedIndices.Add(entries[index].originalIndex);
                        }

                        ApplyDictionarySliceOrder(keysProperty, valuesProperty, orderedIndices, 0);
                    }

                    if (logPaletteSort)
                    {
                        paletteKeysAfterSortLease = Buffers<string>.GetList(
                            entries.Count,
                            out paletteKeysAfterSort
                        );
                        CaptureStringKeyOrder(entries, paletteKeysAfterSort);
                    }

                    if (logPaletteSort)
                    {
                        paletteKeysSerializedAfterLease = Buffers<string>.GetList(
                            keysProperty?.arraySize ?? 0,
                            out List<string> paletteKeysSerializedAfter
                        );
                        CaptureStringKeyOrder(keysProperty, paletteKeysSerializedAfter);
                        PaletteSerializationDiagnostics.ReportDictionarySort(
                            dictionaryProperty,
                            paletteKeysBefore,
                            paletteKeysAfterSort,
                            paletteKeysSerializedAfter
                        );
                    }

                    ApplyModifiedPropertiesWithUndoFallback(
                        serializedObject,
                        dictionaryProperty,
                        "SortEntries"
                    );
                    SyncRuntimeDictionary(dictionaryProperty);

                    listKey = GetListKey(dictionaryProperty);
                    InvalidateKeyCache(listKey);
                    MarkListCacheDirty(listKey);
                    if (!string.IsNullOrEmpty(listKey))
                    {
                        _sortedOrderHashes[listKey] = ComputeKeyOrderHash(keysProperty, keyType);
                    }

                    int newSelectedIndex = -1;
                    if (selectedKey != null)
                    {
                        for (int index = 0; index < entries.Count; index++)
                        {
                            if (ValuesEqual(entries[index].key, selectedKey))
                            {
                                newSelectedIndex = index;
                                break;
                            }
                        }
                    }

                    if (newSelectedIndex >= 0)
                    {
                        pagination.selectedIndex = newSelectedIndex;
                        int totalPages = GetTotalPages(keysProperty.arraySize, pagination.pageSize);
                        int targetPage = GetPageForIndex(newSelectedIndex, pagination.pageSize);
                        pagination.pageIndex = Mathf.Clamp(targetPage, 0, totalPages - 1);
                    }
                    else
                    {
                        pagination.selectedIndex = Mathf.Min(
                            keysProperty.arraySize - 1,
                            pagination.selectedIndex
                        );
                        pagination.selectedIndex = Mathf.Max(pagination.selectedIndex, -1);
                        int totalPages = GetTotalPages(keysProperty.arraySize, pagination.pageSize);
                        int targetPage = GetPageForIndex(
                            Mathf.Max(pagination.selectedIndex, 0),
                            pagination.pageSize
                        );
                        pagination.pageIndex = Mathf.Clamp(targetPage, 0, totalPages - 1);
                    }
                }

                ListPageCache updatedCache = EnsurePageCache(listKey, keysProperty, pagination);
                SyncListSelectionWithPagination(list, pagination, updatedCache);
            }
            finally
            {
                paletteKeysSerializedAfterLease.Dispose();
                paletteKeysAfterSortLease.Dispose();
                paletteKeysBeforeLease.Dispose();
            }

            GUI.changed = true;

            EditorWindow focusedWindow = EditorWindow.focusedWindow;
            if (focusedWindow != null)
            {
                focusedWindow.Repaint();
            }
        }

        private static void CaptureStringKeyOrder(
            SerializedProperty keysProperty,
            List<string> destination
        )
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (keysProperty == null)
            {
                return;
            }

            int count = keysProperty.arraySize;
            for (int index = 0; index < count; index++)
            {
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                destination.Add(keyProperty?.stringValue ?? "<null>");
            }
        }

        private static void CaptureStringKeyOrder(
            List<KeyValueSnapshot> snapshots,
            List<string> destination
        )
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            if (snapshots == null)
            {
                return;
            }

            for (int index = 0; index < snapshots.Count; index++)
            {
                KeyValueSnapshot snapshot = snapshots[index];
                if (snapshot == null)
                {
                    destination.Add("<null>");
                    continue;
                }

                if (snapshot.key is string stringKey)
                {
                    destination.Add(stringKey);
                    continue;
                }

                destination.Add(snapshot.key?.ToString() ?? "<null>");
            }
        }

        private sealed class KeyValueSnapshot
        {
            public object key;
            public object value;
            public int originalIndex;
        }

        private sealed class KeyValueSnapshotComparer : IComparer<KeyValueSnapshot>
        {
            private readonly Func<object, object, int> _comparison;

            public KeyValueSnapshotComparer(Func<object, object, int> comparison)
            {
                _comparison = comparison;
            }

            public int Compare(KeyValueSnapshot x, KeyValueSnapshot y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                if (_comparison == null)
                {
                    return 0;
                }

                int comparisonResult = _comparison(x.key, y.key);
                if (comparisonResult != 0)
                {
                    return comparisonResult;
                }

                return x.originalIndex.CompareTo(y.originalIndex);
            }
        }

        private static void EnsureParallelArraySizes(
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty
        )
        {
            if (keysProperty == null || valuesProperty == null)
            {
                return;
            }

            if (keysProperty.arraySize == valuesProperty.arraySize)
            {
                return;
            }

            int size = Mathf.Min(keysProperty.arraySize, valuesProperty.arraySize);
            if (size < 0)
            {
                size = 0;
            }

            keysProperty.arraySize = size;
            valuesProperty.arraySize = size;
        }

        private void CacheValueType(string cacheKey, Type valueType)
        {
            if (string.IsNullOrEmpty(cacheKey) || valueType == null)
            {
                return;
            }

            if (_valueTypes.TryGetValue(cacheKey, out Type existing) && existing == valueType)
            {
                return;
            }

            _valueTypes[cacheKey] = valueType;
            MarkListCacheDirty(cacheKey);
        }

        private Type EnsureValueTypeCached(string cacheKey, SerializedProperty dictionaryProperty)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return null;
            }

            if (_valueTypes.TryGetValue(cacheKey, out Type cached) && cached != null)
            {
                return cached;
            }

            if (
                TryResolveKeyValueTypes(
                    fieldInfo,
                    out Type _,
                    out Type resolvedValueType,
                    out bool _
                )
                && resolvedValueType != null
            )
            {
                CacheValueType(cacheKey, resolvedValueType);
                return resolvedValueType;
            }

            object dictionaryInstance = GetDictionaryInstance(dictionaryProperty);
            if (
                TryResolveKeyValueTypesFromInstance(
                    dictionaryInstance,
                    out Type _,
                    out Type instanceValueType,
                    out bool _
                )
                && instanceValueType != null
            )
            {
                CacheValueType(cacheKey, instanceValueType);
                return instanceValueType;
            }

            return null;
        }

        internal string GetListKey(SerializedProperty property)
        {
            string propertyPath = property?.propertyPath;
            SerializedObject serializedObject = property != null ? property.serializedObject : null;
            if (
                propertyPath != null
                && string.Equals(_cachedPropertyPath, propertyPath, StringComparison.Ordinal)
                && ReferenceEquals(_cachedSerializedObject, serializedObject)
            )
            {
                return _cachedListKey;
            }

            string key = BuildPropertyCacheKey(property);
            _cachedPropertyPath = propertyPath;
            _cachedSerializedObject = serializedObject;
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
                int instanceId = targets[0].GetUnityObjectId();
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
                int id = targets[index] != null ? targets[index].GetUnityObjectId() : 0;
                keyBuilder.Append(id);
                if (index < targets.Length - 1)
                {
                    keyBuilder.Append(',');
                }
            }

            return keyBuilder.ToString();
        }

        internal static bool ApplyModifiedPropertiesWithUndoFallback(
            SerializedObject serializedObject
        )
        {
            return ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                propertyPath: null,
                operation: null,
                static so => so.ApplyModifiedProperties(),
                static so => so.ApplyModifiedPropertiesWithoutUndo()
            );
        }

        internal static bool ApplyModifiedPropertiesWithUndoFallback(
            SerializedObject serializedObject,
            SerializedProperty contextProperty,
            string operation
        )
        {
            string propertyPath = contextProperty != null ? contextProperty.propertyPath : null;
            return ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                propertyPath,
                operation,
                static so => so.ApplyModifiedProperties(),
                static so => so.ApplyModifiedPropertiesWithoutUndo()
            );
        }

        internal static bool ApplyModifiedPropertiesWithUndoFallback(
            SerializedObject serializedObject,
            Func<SerializedObject, bool> applyWithUndo,
            Func<SerializedObject, bool> applyWithoutUndo
        )
        {
            return ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                propertyPath: null,
                operation: null,
                applyWithUndo,
                applyWithoutUndo
            );
        }

        private static bool ApplyModifiedPropertiesWithUndoFallback(
            SerializedObject serializedObject,
            string propertyPath,
            string operation,
            Func<SerializedObject, bool> applyWithUndo,
            Func<SerializedObject, bool> applyWithoutUndo
        )
        {
            if (serializedObject == null)
            {
                return false;
            }

            Func<SerializedObject, bool> applyFunc =
                applyWithUndo ?? (static so => so.ApplyModifiedProperties());
            Func<SerializedObject, bool> fallbackFunc =
                applyWithoutUndo ?? (static so => so.ApplyModifiedPropertiesWithoutUndo());

            bool hadChangesBefore = serializedObject.hasModifiedProperties;
            if (applyFunc(serializedObject))
            {
                PaletteSerializationDiagnostics.ReportDrawerApplyResult(
                    serializedObject,
                    propertyPath,
                    operation,
                    PaletteSerializationDiagnostics.DrawerApplyResult.UndoPathSucceeded,
                    hadChangesBefore,
                    serializedObject.hasModifiedProperties
                );
                return true;
            }

            bool fallbackApplied = fallbackFunc(serializedObject);
            PaletteSerializationDiagnostics.ReportDrawerApplyResult(
                serializedObject,
                propertyPath,
                operation,
                fallbackApplied
                    ? PaletteSerializationDiagnostics.DrawerApplyResult.FallbackPathSucceeded
                    : PaletteSerializationDiagnostics.DrawerApplyResult.Failed,
                hadChangesBefore,
                serializedObject.hasModifiedProperties
            );
            return fallbackApplied;
        }

        private static void ApplyAndSyncPaletteRowChange(
            SerializedProperty dictionaryProperty,
            string operation
        )
        {
            if (
                dictionaryProperty == null
                || !PaletteSerializationDiagnostics.IsPaletteProperty(
                    dictionaryProperty.propertyPath
                )
            )
            {
                return;
            }

            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            bool applied = ApplyModifiedPropertiesWithUndoFallback(
                serializedObject,
                dictionaryProperty,
                operation
            );
            if (!applied)
            {
                return;
            }

            serializedObject.Update();
            SyncRuntimeDictionary(dictionaryProperty);
        }

        private static bool KeyIsValid(Type keyType, object keyValue)
        {
            if (keyType == typeof(string))
            {
                return !string.IsNullOrEmpty(keyValue as string);
            }

            if (typeof(Object).IsAssignableFrom(keyType))
            {
                return keyValue is Object obj && obj != null;
            }

            return keyValue != null || keyType.IsValueType;
        }

        private int FindExistingKeyIndex(
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            Type keyType,
            object keyValue
        )
        {
            string cacheKey = GetListKey(dictionaryProperty);
            KeyIndexCache cache = GetOrBuildKeyIndexCache(cacheKey, keysProperty, keyType);
            object lookupKey = keyValue ?? NullKeySentinel;
            return cache.indices.GetValueOrDefault(lookupKey, -1);
        }

        internal static bool EntryMatchesExisting(
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            int existingIndex,
            Type keyType,
            Type valueType,
            PendingEntry pending
        )
        {
            if (existingIndex < 0 || existingIndex >= keysProperty.arraySize || pending == null)
            {
                return false;
            }

            if (
                pending.lastDuplicateCheckIndex == existingIndex
                && pending.lastDuplicateCheckValueRevision == pending.valueRevision
            )
            {
                return pending.lastDuplicateCheckResult;
            }

            SerializedProperty existingKeyProperty = keysProperty.GetArrayElementAtIndex(
                existingIndex
            );
            SerializedProperty existingValueProperty = valuesProperty.GetArrayElementAtIndex(
                existingIndex
            );
            object existingKey = GetPropertyValue(existingKeyProperty, keyType);
            if (!ValuesEqual(existingKey, pending.key))
            {
                return false;
            }

            bool usedSerializedComparison = false;
            bool matches = false;
            if (
                !IsSimplePendingFieldType(valueType)
                && pending.valueWrapperProperty != null
                && pending.valueWrapperSerialized != null
            )
            {
                pending.valueWrapperSerialized.Update();
                existingValueProperty.serializedObject?.Update();
                if (
                    SerializedProperty.DataEquals(
                        existingValueProperty,
                        pending.valueWrapperProperty
                    )
                )
                {
                    matches = true;
                    usedSerializedComparison = true;
                }
                else
                {
                    usedSerializedComparison = false;
                }
            }

            if (!usedSerializedComparison)
            {
                object existingValue = GetPropertyValue(existingValueProperty, valueType);
                matches = ValuesEqual(existingValue, pending.value);
                if (
                    !matches
                    && valueType != null
                    && pending.value != null
                    && existingValueProperty != null
                )
                {
                    matches = SerializedValueEquals(
                        existingValueProperty,
                        pending.value,
                        valueType
                    );
                }
            }

            pending.lastDuplicateCheckIndex = existingIndex;
            pending.lastDuplicateCheckValueRevision = pending.valueRevision;
            pending.lastDuplicateCheckResult = matches;
            return matches;
        }

        private static bool SerializedValueEquals(
            SerializedProperty existingValueProperty,
            object pendingValue,
            Type valueType
        )
        {
            if (existingValueProperty == null || valueType == null)
            {
                return false;
            }

            PendingValueWrapper wrapper = ScriptableObject.CreateInstance<PendingValueWrapper>();
            try
            {
                object clone = CloneComplexValue(pendingValue, valueType);
                wrapper.SetValue(clone);
                SerializedObject serialized = new(wrapper);
                SerializedProperty wrapperProperty = wrapper.FindValueProperty(serialized);
                if (wrapperProperty == null)
                {
                    return false;
                }

                serialized.Update();
                existingValueProperty.serializedObject?.Update();
                return SerializedProperty.DataEquals(existingValueProperty, wrapperProperty);
            }
            finally
            {
                if (wrapper != null)
                {
                    Object.DestroyImmediate(wrapper);
                }
            }
        }

        private KeyIndexCache GetOrBuildKeyIndexCache(
            string cacheKey,
            SerializedProperty keysProperty,
            Type keyType
        )
        {
            if (_keyIndexCaches.TryGetValue(cacheKey, out KeyIndexCache cache))
            {
                if (cache.arraySize != keysProperty.arraySize)
                {
                    PopulateKeyIndexCache(cache, keysProperty, keyType);
                }
                return cache;
            }

            cache = new KeyIndexCache();
            PopulateKeyIndexCache(cache, keysProperty, keyType);
            _keyIndexCaches[cacheKey] = cache;
            return cache;
        }

        private static void PopulateKeyIndexCache(
            KeyIndexCache cache,
            SerializedProperty keysProperty,
            Type keyType
        )
        {
            cache.indices.Clear();
            cache.arraySize = keysProperty.arraySize;
            int count = keysProperty.arraySize;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty element = keysProperty.GetArrayElementAtIndex(i);
                object keyValue = GetPropertyValue(element, keyType) ?? NullKeySentinel;
                cache.indices.TryAdd(keyValue, i);
            }
        }

        private static int ComputeKeyOrderHash(SerializedProperty keysProperty, Type keyType)
        {
            if (keysProperty == null || keyType == null)
            {
                return 17;
            }

            return Objects.EnumerableHashCode(EnumerateKeyPairs(keysProperty, keyType));

            static IEnumerable<(int index, object keyValue)> EnumerateKeyPairs(
                SerializedProperty property,
                Type type
            )
            {
                int count = property.arraySize;
                for (int index = 0; index < count; index++)
                {
                    SerializedProperty keyProperty = property.GetArrayElementAtIndex(index);
                    yield return (index, GetPropertyValue(keyProperty, type));
                }
            }
        }

        internal void InvalidateKeyCache(string cacheKey)
        {
            _keyIndexCaches.Remove(cacheKey);
            InvalidatePendingDuplicateCache(cacheKey);
            MarkListCacheDirty(cacheKey);
            MarkDuplicateStateDirty(cacheKey);
            MarkNullKeyStateDirty(cacheKey);
        }

        private void MarkDuplicateStateDirty(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            if (_duplicateStates.TryGetValue(cacheKey, out DuplicateKeyState state))
            {
                state.MarkDirty();
            }
        }

        private void MarkNullKeyStateDirty(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            // Always create a state if one doesn't exist, so that the next RefreshNullKeyState
            // call will see IsDirty=true and perform a full refresh. This matches the behavior
            // of RefreshDuplicateState which always creates a state via GetOrAdd.
            NullKeyState state = _nullKeyStates.GetOrAdd(cacheKey);
            state.MarkDirty();
        }

        internal void InvalidatePendingDuplicateCache(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            if (_pendingEntries.TryGetValue(cacheKey, out PendingEntry pending))
            {
                InvalidatePendingDuplicateCache(pending);
            }
        }

        internal static bool IsTweeningEnabledForTests(bool isSortedDictionary)
        {
            return ShouldTweenPendingFoldout(isSortedDictionary);
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
        /// <param name="property">The serialized property for the dictionary.</param>
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

            string cacheKey = GetListKey(property);
            if (!_pendingEntries.TryGetValue(cacheKey, out PendingEntry pending) || pending == null)
            {
                return -1f;
            }

            return GetPendingFoldoutProgress(pending);
        }

        /// <summary>
        /// Gets the pending entry's expanded state and animation information for testing.
        /// </summary>
        /// <param name="property">The serialized property for the dictionary.</param>
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

            string cacheKey = GetListKey(property);
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

            string cacheKey = GetListKey(property);
            if (!_pendingEntries.TryGetValue(cacheKey, out PendingEntry pending) || pending == null)
            {
                return;
            }

            pending.isExpanded = expanded;
            AnimBool anim = EnsurePendingFoldoutAnim(pending, property.propertyPath);
            if (anim != null)
            {
                anim.target = expanded;
            }
        }

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
                    ? serializedObject.targetObject.GetUnityObjectId()
                    : 0;
            return new MainFoldoutCacheKey(instanceId, propertyPath);
        }

        private static bool ShouldTweenMainFoldout(bool isSortedDictionary)
        {
            return isSortedDictionary
                ? UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts()
                : UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
        }

        private static float GetMainFoldoutAnimationSpeed(bool isSortedDictionary)
        {
            return isSortedDictionary
                ? UnityHelpersSettings.GetSerializableSortedDictionaryFoldoutSpeed()
                : UnityHelpersSettings.GetSerializableDictionaryFoldoutSpeed();
        }

        private static AnimBool EnsureMainFoldoutAnim(
            SerializedObject serializedObject,
            string propertyPath,
            bool isExpanded,
            bool isSortedDictionary
        )
        {
            bool shouldTween = ShouldTweenMainFoldout(isSortedDictionary);
            float speed = GetMainFoldoutAnimationSpeed(isSortedDictionary);
            MainFoldoutCacheKey cacheKey = GetMainFoldoutCacheKey(serializedObject, propertyPath);

            SerializableCollectionTweenDiagnostics.LogTweenSettingsQuery(
                "EnsureMainFoldoutAnim",
                propertyPath ?? "(unknown)",
                isSortedDictionary,
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
                    isSortedDictionary,
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
            bool isSortedDictionary
        )
        {
            bool shouldTween = ShouldTweenMainFoldout(isSortedDictionary);
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
                isSortedDictionary
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
            bool isSortedDictionary
        )
        {
            return GetMainFoldoutProgress(
                serializedObject,
                propertyPath,
                isExpanded,
                isSortedDictionary
            );
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

        private static bool ShouldTweenPendingFoldout(bool isSortedDictionary)
        {
            return isSortedDictionary
                ? UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts()
                : UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
        }

        private static float GetPendingFoldoutAnimationSpeed(bool isSortedDictionary)
        {
            return isSortedDictionary
                ? UnityHelpersSettings.GetSerializableSortedDictionaryFoldoutSpeed()
                : UnityHelpersSettings.GetSerializableDictionaryFoldoutSpeed();
        }

        private static AnimBool CreatePendingFoldoutAnim(
            bool initialValue,
            bool isSortedDictionary,
            string propertyPath = null
        )
        {
            float speed = GetPendingFoldoutAnimationSpeed(isSortedDictionary);
            AnimBool anim = new(initialValue) { speed = speed };
            anim.valueChanged.AddListener(RequestRepaint);

            SerializableCollectionTweenDiagnostics.LogAnimBoolCreation(
                propertyPath ?? "(unknown)",
                initialValue,
                isSortedDictionary,
                speed
            );

            return anim;
        }

        private static AnimBool EnsurePendingFoldoutAnim(
            PendingEntry pending,
            string propertyPath = null
        )
        {
            if (pending == null)
            {
                return null;
            }

            bool shouldTween = ShouldTweenPendingFoldout(pending.isSorted);
            float speed = GetPendingFoldoutAnimationSpeed(pending.isSorted);

            SerializableCollectionTweenDiagnostics.LogTweenSettingsQuery(
                "EnsurePendingFoldoutAnim",
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
                pending.foldoutAnim = CreatePendingFoldoutAnim(
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

        private static void RequestRepaint()
        {
            // Always repaint all views to ensure animations work correctly
            // in both Inspector and SettingsProvider contexts
            InternalEditorUtility.RepaintAllViews();
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

            bool shouldTween = ShouldTweenPendingFoldout(pending.isSorted);

            // Always call EnsurePendingFoldoutAnim to properly clean up the AnimBool when
            // tweening is disabled. This ensures the foldoutAnim is set to null when shouldTween
            // is false, which is important for consistent state management.
            AnimBool anim = EnsurePendingFoldoutAnim(pending, propertyPath);

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
            if (anim.isAnimating)
            {
                RequestRepaint();
            }
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

        private static GUIStyle GetFooterLabelStyle()
        {
            if (_footerLabelStyle != null)
            {
                return _footerLabelStyle;
            }

            _footerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 10),
            };

            return _footerLabelStyle;
        }

        private static GUIStyle GetPendingFoldoutLabelStyle()
        {
            if (_pendingFoldoutLabelStyle == null)
            {
                _pendingFoldoutLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            // Use default editor label colors
            _pendingFoldoutLabelStyle.normal.textColor = EditorStyles.label.normal.textColor;
            _pendingFoldoutLabelStyle.hover.textColor = EditorStyles.label.hover.textColor;
            _pendingFoldoutLabelStyle.active.textColor = EditorStyles.label.active.textColor;
            _pendingFoldoutLabelStyle.focused.textColor = EditorStyles.label.focused.textColor;

            return _pendingFoldoutLabelStyle;
        }

        private static float GetPendingFieldHeight(
            PendingEntry pending,
            Type fieldType,
            bool isValueField
        )
        {
            PaletteValueRenderer paletteRenderer = ResolvePaletteValueRenderer(fieldType);
            if (paletteRenderer != null)
            {
                return paletteRenderer.Height;
            }

            float defaultHeight = EditorGUIUtility.singleLineHeight;
            if (
                pending == null
                || fieldType == null
                || fieldType == typeof(string)
                || fieldType.IsPrimitive
                || fieldType.IsEnum
                || typeof(Object).IsAssignableFrom(fieldType)
                || !TypeSupportsComplexEditing(fieldType)
            )
            {
                return defaultHeight;
            }

            PendingWrapperContext context = EnsurePendingWrapper(pending, fieldType, isValueField);
            if (context.Property == null)
            {
                return defaultHeight;
            }

            if (TypeSupportsComplexEditing(fieldType))
            {
                context.Property.isExpanded = true;
            }

            return EditorGUI.GetPropertyHeight(context.Property, true);
        }

        private static GUIContent GetUnsupportedTypeContent(Type type)
        {
            string typeName = type?.Name ?? "Unknown";
            if (!UnsupportedTypeMessageCache.TryGetValue(type, out string message))
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

        private static GUIContent GetSuppressedFieldValueContent(object current)
        {
            string text = string.Empty;
            if (current is Object unityObject)
            {
                if (unityObject != null)
                {
                    text = unityObject.name;
                }
            }
            else if (current != null)
            {
                text = current.ToString() ?? string.Empty;
            }

            SuppressedFieldValueContent.text = text;
            return SuppressedFieldValueContent;
        }

        internal static object DrawFieldForType(
            Rect rect,
            string label,
            object current,
            Type type,
            PendingEntry pending,
            bool isValueField
        )
        {
            ReusableFieldLabelContent.text = label;
            GUIContent content = ReusableFieldLabelContent;

            if (TryDrawPaletteValueField(rect, content, ref current, type))
            {
                return current;
            }

            if (TryDrawComplexTypeField(rect, content, ref current, type, pending, isValueField))
            {
                return current;
            }

            if (!IsTypeSupported(type))
            {
                EditorGUI.LabelField(rect, content, GetUnsupportedTypeContent(type));
                return current;
            }

            if (EditorUi.Suppress)
            {
                EditorGUI.LabelField(rect, content, GetSuppressedFieldValueContent(current));
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
                    label,
                    current is Vector2 v2 ? v2 : Vector2.zero
                );
            }

            if (type == typeof(Vector3))
            {
                return EditorGUI.Vector3Field(
                    rect,
                    label,
                    current is Vector3 v3 ? v3 : Vector3.zero
                );
            }

            if (type == typeof(Vector4))
            {
                return EditorGUI.Vector4Field(
                    rect,
                    label,
                    current is Vector4 v4 ? v4 : Vector4.zero
                );
            }

            if (type == typeof(Vector2Int))
            {
                // ReSharper disable once InconsistentNaming
                Vector2Int value = current is Vector2Int v2int ? v2int : default;
                Vector2Int newValue = EditorGUI.Vector2IntField(rect, label, value);
                return newValue;
            }

            if (type == typeof(Vector3Int))
            {
                // ReSharper disable once InconsistentNaming
                Vector3Int value = current is Vector3Int v3int ? v3int : default;
                Vector3Int newValue = EditorGUI.Vector3IntField(rect, label, value);
                return newValue;
            }

            if (type == typeof(Rect))
            {
                Rect value = current is Rect rectValue ? rectValue : default;
                return EditorGUI.RectField(rect, label, value);
            }

            if (type == typeof(RectInt))
            {
                RectInt value = current is RectInt rectInt ? rectInt : default;
                return EditorGUI.RectIntField(rect, label, value);
            }

            if (type == typeof(Bounds))
            {
                Bounds value = current is Bounds bounds ? bounds : default;
                return EditorGUI.BoundsField(rect, label, value);
            }

            if (type == typeof(BoundsInt))
            {
                BoundsInt value = current is BoundsInt boundsInt ? boundsInt : default;
                return EditorGUI.BoundsIntField(rect, label, value);
            }

            if (type == typeof(Color))
            {
                Color value = current is Color color ? color : Color.clear;
                return EditorGUI.ColorField(rect, label, value);
            }

            if (type == typeof(AnimationCurve))
            {
                AnimationCurve value = current as AnimationCurve ?? new AnimationCurve();
                return EditorGUI.CurveField(rect, label, value);
            }

            if (type.IsEnum)
            {
                Enum currentEnum = current as Enum ?? (Enum)Enum.ToObject(type, 0);
                return EditorGUI.EnumPopup(rect, content, currentEnum);
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
            PendingEntry pending,
            bool isValueField
        )
        {
            if (
                pending == null
                || !TypeSupportsComplexEditing(type)
                || typeof(Object).IsAssignableFrom(type)
                || type == typeof(string)
            )
            {
                return false;
            }

            PendingWrapperContext context = EnsurePendingWrapper(pending, type, isValueField);
            if (context.Property == null)
            {
                return false;
            }

            if (!context.Property.editable)
            {
                LogPendingWrapperNotEditable(type, isValueField);
                EditorGUI.HelpBox(
                    rect,
                    "Pending entry value cannot be edited. See console for details.",
                    MessageType.Warning
                );
                return true;
            }

            bool requiresSync =
                pending == null
                || (isValueField ? pending.valueWrapperDirty : pending.keyWrapperDirty);

            object targetValue = current ?? GetDefaultValue(type);
            if (requiresSync)
            {
                object clone = CloneComplexValue(targetValue, type);
                context.Wrapper.SetValue(clone);
                SyncPendingWrapperManagedReference(context, clone);
                if (pending != null)
                {
                    if (isValueField)
                    {
                        pending.valueWrapperDirty = false;
                        pending.valueWrapperSyncRevision = pending.valueRevision;
                    }
                    else
                    {
                        pending.keyWrapperDirty = false;
                    }
                }
            }
            else
            {
                context.Serialized.Update();
            }

            if (type.IsClass && context.Wrapper.GetValue() == null)
            {
                object defaultValue = GetDefaultValue(type);
                context.Wrapper.SetValue(defaultValue);
                SyncPendingWrapperManagedReference(context, defaultValue);
                if (pending != null)
                {
                    if (isValueField)
                    {
                        pending.valueWrapperDirty = false;
                    }
                    else
                    {
                        pending.keyWrapperDirty = false;
                    }
                }
            }

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = Mathf.Max(0, previousIndent - 1);
            try
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(rect, context.Property, content, includeChildren: true);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyModifiedPropertiesWithUndoFallback(context.Serialized);
                    context.Serialized.Update();
                    object updated = context.Wrapper.GetValue();
                    current = CloneComplexValue(updated, type);
                    MarkPendingWrapperDirty(pending, isValueField);
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndent;
            }

            return true;
        }

        private static void LogPendingWrapperNotEditable(Type type, bool isValueField)
        {
            if (type == null)
            {
                return;
            }

            if (!PendingWrapperNonEditableTypes.Add(type))
            {
                return;
            }

            string fieldLabel = isValueField ? "value" : "key";
            Debug.LogWarning(
                $"Pending entry {fieldLabel} type {type.FullName} is not editable because the temporary wrapper SerializedProperty is read-only. Verify the wrapper hide flags and ensure the type supports [System.Serializable] data."
            );
        }

        private static bool TryDrawPaletteValueField(
            Rect rect,
            GUIContent label,
            ref object current,
            Type type
        )
        {
            PaletteValueRenderer renderer = ResolvePaletteValueRenderer(type);
            if (renderer == null)
            {
                return false;
            }

            object working = EnsurePaletteValueInstance(current, type);
            renderer.Draw(rect, label, ref working);
            current = working;
            return true;
        }

        private static PaletteValueRenderer ResolvePaletteValueRenderer(Type type)
        {
            if (type == null || PaletteValueRenderers == null)
            {
                return null;
            }

            return PaletteValueRenderers.GetValueOrDefault(type);
        }

        private static void RegisterPaletteManualEditForKey(
            SerializedProperty dictionaryProperty,
            object key,
            Type keyType
        )
        {
            if (
                dictionaryProperty == null
                || keyType != typeof(string)
                || key is not string colorKey
                || string.IsNullOrWhiteSpace(colorKey)
                || !PaletteSerializationDiagnostics.IsPaletteProperty(
                    dictionaryProperty.propertyPath
                )
            )
            {
                return;
            }

            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] is UnityHelpersSettings)
                {
                    UnityHelpersSettings.RegisterPaletteManualEdit(
                        dictionaryProperty.propertyPath,
                        colorKey
                    );
                }
            }
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

        private static bool TypeSupportsComplexEditing(Type type)
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
                return TypeSupportsComplexEditing(type.GetElementType());
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return TypeSupportsComplexEditing(type.GetGenericArguments()[0]);
            }

            return type.IsSerializable;
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

        private static bool ValueTypeSupportsFoldout(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (typeof(Object).IsAssignableFrom(type))
            {
                return false;
            }

            if (PaletteValueRenderers != null && PaletteValueRenderers.ContainsKey(type))
            {
                return false;
            }

            if (IsSimplePendingFieldType(type))
            {
                return false;
            }

            return TypeSupportsComplexEditing(type);
        }

        private static bool PendingValueSupportsFoldout(PendingEntry pending, Type valueType)
        {
            if (pending == null || !ValueTypeSupportsFoldout(valueType))
            {
                return false;
            }

            PendingWrapperContext context = EnsurePendingWrapper(
                pending,
                valueType,
                isValueField: true
            );
            if (context.Property == null)
            {
                return false;
            }

            context.Property.isExpanded = true;
            return SerializedPropertySupportsFoldout(context.Property);
        }

        private static bool SerializedPropertySupportsFoldout(SerializedProperty property)
        {
            return property != null && property.hasVisibleChildren;
        }

        internal static PendingWrapperContext EnsurePendingWrapper(
            PendingEntry pending,
            Type type,
            bool isValueField
        )
        {
            if (pending == null || type == null)
            {
                return PendingWrapperContext.Empty;
            }

            PendingValueWrapper wrapper = isValueField ? pending.valueWrapper : pending.keyWrapper;
            SerializedObject serialized = isValueField
                ? pending.valueWrapperSerialized
                : pending.keyWrapperSerialized;
            SerializedProperty property = isValueField
                ? pending.valueWrapperProperty
                : pending.keyWrapperProperty;

            if (wrapper == null)
            {
                wrapper = ScriptableObject.CreateInstance<PendingValueWrapper>();
                wrapper.hideFlags = PendingWrapperHideFlags;
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
                ReleasePendingWrapper(pending, isValueField);
                return PendingWrapperContext.Empty;
            }

            if (isValueField)
            {
                pending.valueWrapper = wrapper;
                pending.valueWrapperSerialized = serialized;
                pending.valueWrapperProperty = property;
            }
            else
            {
                pending.keyWrapper = wrapper;
                pending.keyWrapperSerialized = serialized;
                pending.keyWrapperProperty = property;
            }

            serialized.Update();

            return new PendingWrapperContext(wrapper, serialized, property);
        }

        private static void SyncPendingWrapperManagedReference(
            PendingWrapperContext context,
            object newValue
        )
        {
            if (context.Property == null || context.Serialized == null)
            {
                return;
            }

            context.Serialized.Update();
            context.Property.managedReferenceValue = newValue;
            context.Serialized.ApplyModifiedPropertiesWithoutUndo();
            context.Serialized.Update();
        }

        private static void ReleasePendingWrapper(PendingEntry pending, bool isValueField)
        {
            if (pending == null)
            {
                return;
            }

            if (isValueField)
            {
                if (pending.valueWrapper != null)
                {
                    Object.DestroyImmediate(pending.valueWrapper);
                }

                pending.valueWrapper = null;
                pending.valueWrapperSerialized = null;
                pending.valueWrapperProperty = null;
                pending.valueWrapperDirty = true;
                pending.valueWrapperSyncRevision = pending.valueRevision;
            }
            else
            {
                if (pending.keyWrapper != null)
                {
                    Object.DestroyImmediate(pending.keyWrapper);
                }

                pending.keyWrapper = null;
                pending.keyWrapperSerialized = null;
                pending.keyWrapperProperty = null;
                pending.keyWrapperDirty = true;
            }
        }

        private static void MarkPendingWrapperDirty(PendingEntry pending, bool isValueField)
        {
            if (pending == null)
            {
                return;
            }

            if (isValueField)
            {
                pending.valueWrapperDirty = true;
                MarkPendingValueChanged(pending);
                pending.valueWrapperSyncRevision = pending.valueRevision;
            }
            else
            {
                pending.keyWrapperDirty = true;
                InvalidatePendingDuplicateCache(pending);
            }
        }

        private static void MarkPendingValueChanged(PendingEntry pending)
        {
            if (pending == null)
            {
                return;
            }
            unchecked
            {
                pending.valueRevision++;
            }

            InvalidatePendingDuplicateCache(pending);
        }

        private static void InvalidatePendingDuplicateCache(PendingEntry pending)
        {
            if (pending == null)
            {
                return;
            }

            pending.lastDuplicateCheckIndex = -1;
            pending.lastDuplicateCheckValueRevision = -1;
            pending.lastDuplicateCheckResult = false;
        }

        private static bool TryInvokeParameterlessConstructor(Type type, out object instance)
        {
            instance = null;
            if (type == null)
            {
                return false;
            }

            if (ParameterlessConstructorCache.TryGetValue(type, out Func<object> cached))
            {
                instance = cached();
                return instance != null;
            }

            if (UnsupportedParameterlessTypes.ContainsKey(type))
            {
                return false;
            }

            Func<object> factory = TryResolveConstructorFactory(type);
            if (factory != null)
            {
                ParameterlessConstructorCache[type] = factory;
                instance = factory();
                if (instance != null)
                {
                    return true;
                }
            }

            if (TryInstantiateWithoutConstructor(type, out instance))
            {
                return true;
            }

            UnsupportedParameterlessTypes[type] = 0;
            return false;
        }

        private static Func<object> TryResolveConstructorFactory(Type type)
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

        private static bool TryInstantiateWithoutConstructor(Type type, out object instance)
        {
            try
            {
                instance = Activator.CreateInstance(type, nonPublic: true);
                if (instance != null)
                {
                    ParameterlessConstructorCache[type] = () =>
                    {
                        try
                        {
                            return Activator.CreateInstance(type, nonPublic: true);
                        }
                        catch
                        {
                            return null;
                        }
                    };
                    return true;
                }
            }
            catch
            {
                // Continue to FormatterServices fallback
            }

            try
            {
                instance = FormatterServices.GetUninitializedObject(type);
                ParameterlessConstructorCache[type] = () =>
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
                return instance != null;
            }
            catch
            {
                instance = null;
                return false;
            }
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

        private static object CloneComplexValue(object source, Type type)
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

            if (type == null || type.IsValueType || typeof(Object).IsAssignableFrom(type))
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

        private static bool TryAssignComplexValue(
            SerializedProperty property,
            object value,
            Type valueType
        )
        {
            if (property == null)
            {
                return false;
            }

            Object[] targets = property.serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                return false;
            }

            string finalSegment = GetFinalPathSegment(property.propertyPath);
            bool applied = false;

            foreach (Object target in targets)
            {
                object parent = GetParentObject(target, property.propertyPath);
                if (parent == null)
                {
                    continue;
                }

                object clone = CloneComplexValue(value, valueType);
                if (SetPathComponentValue(parent, finalSegment, clone))
                {
                    applied = true;
                }
            }

            if (applied)
            {
                property.serializedObject.Update();
            }

            return applied;
        }

        private static object GetParentObject(Object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
            {
                return null;
            }

            string[] elements = propertyPath
                .Replace(".Array.data[", "[")
                .Split(PropertyPathSeparators, StringSplitOptions.RemoveEmptyEntries);

            object current = target;
            for (int index = 0; index < elements.Length - 1; index++)
            {
                current = GetPathComponentValue(current, elements[index]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static string GetFinalPathSegment(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return string.Empty;
            }

            string[] elements = propertyPath
                .Replace(".Array.data[", "[")
                .Split(PropertyPathSeparators, StringSplitOptions.RemoveEmptyEntries);
            return elements.Length > 0 ? elements[^1] : string.Empty;
        }

        private static object GetPathComponentValue(object source, string path)
        {
            if (source == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            int bracketIndex = path.IndexOf('[');
            if (bracketIndex < 0)
            {
                return GetMemberValue(source, path);
            }

            string elementName = path.Substring(0, bracketIndex);
            int index = ParseElementIndex(path, bracketIndex);
            if (index < 0)
            {
                return null;
            }

            object collection = GetMemberValue(source, elementName);
            if (collection is IList list)
            {
                return index < list.Count ? list[index] : null;
            }

            return null;
        }

        private static bool SetPathComponentValue(object source, string path, object value)
        {
            if (source == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            int bracketIndex = path.IndexOf('[');
            if (bracketIndex < 0)
            {
                return SetMemberValue(source, path, value);
            }

            string elementName = path.Substring(0, bracketIndex);
            int index = ParseElementIndex(path, bracketIndex);
            if (index < 0)
            {
                return false;
            }

            object collection = GetMemberValue(source, elementName);
            if (collection is IList list && index < list.Count)
            {
                list[index] = value;
                return true;
            }

            return false;
        }

        private static int ParseElementIndex(string path, int bracketIndex)
        {
            int endIndex = path.IndexOf(']', bracketIndex);
            if (endIndex < 0)
            {
                return -1;
            }

            string indexString = path.Substring(bracketIndex + 1, endIndex - bracketIndex - 1);
            return int.TryParse(indexString, out int result) ? result : -1;
        }

        private static object GetMemberValue(object source, string memberName)
        {
            if (source == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type type = source.GetType();

            FieldInfo field = type.GetField(memberName, ReflectionBindingFlags);
            if (field != null)
            {
                return field.GetValue(source);
            }

            PropertyInfo propertyInfo = type.GetProperty(memberName, ReflectionBindingFlags);
            if (propertyInfo != null)
            {
                return propertyInfo.GetValue(source);
            }

            return null;
        }

        private static bool SetMemberValue(object source, string memberName, object value)
        {
            if (source == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            Type type = source.GetType();

            FieldInfo field = type.GetField(memberName, ReflectionBindingFlags);
            if (field != null)
            {
                field.SetValue(source, value);
                return true;
            }

            PropertyInfo propertyInfo = type.GetProperty(memberName, ReflectionBindingFlags);
            if (propertyInfo != null && propertyInfo.CanWrite)
            {
                propertyInfo.SetValue(source, value);
                return true;
            }

            return false;
        }

        private static void CopyComplexValueToPropertyFields(
            SerializedProperty destination,
            object value,
            Type valueType
        )
        {
            if (destination == null || valueType == null)
            {
                return;
            }

            foreach (FieldInfo field in GetSerializableFields(valueType))
            {
                SerializedProperty child = destination.FindPropertyRelative(field.Name);
                if (child == null)
                {
                    continue;
                }

                object fieldValue = field.GetValue(value);
                SetPropertyValue(child, fieldValue, field.FieldType);
            }
        }

        internal sealed class PendingValueWrapper : ScriptableObject
        {
            private const string PropertyName = nameof(boxedValue);

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

        internal readonly struct PendingWrapperContext
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

        private readonly struct LabelWidthScope : IDisposable
        {
            private readonly float _previousWidth;

            public LabelWidthScope(float width)
            {
                _previousWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = width;
            }

            public void Dispose()
            {
                EditorGUIUtility.labelWidth = _previousWidth;
            }
        }

        internal readonly struct RowFoldoutKey : IEquatable<RowFoldoutKey>
        {
            public RowFoldoutKey(string cacheKey, int index)
            {
                CacheKey = cacheKey;
                Index = index;
            }

            public string CacheKey { get; }

            public int Index { get; }

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

        private static Dictionary<Type, PaletteValueRenderer> BuildPaletteValueRenderers()
        {
            Dictionary<Type, PaletteValueRenderer> renderers = new();
            Type settingsType = typeof(UnityHelpersSettings);
            TryRegisterDualColorPaletteRenderer(
                renderers,
                settingsType,
                nameof(UnityHelpersSettings.WButtonCustomColor),
                "Button",
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                "Text",
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
            );
            TryRegisterWEnumPaletteRenderer(renderers, settingsType);
            return renderers;
        }

        internal static void TryRegisterDualColorPaletteRenderer(
            IDictionary<Type, PaletteValueRenderer> renderers,
            Type containerType,
            string nestedTypeName,
            string firstLabel,
            string firstFieldName,
            string secondLabel,
            string secondFieldName
        )
        {
            Type nestedType = containerType.GetNestedType(
                nestedTypeName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (nestedType == null)
            {
                return;
            }

            FieldInfo firstField = nestedType.GetField(
                firstFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo secondField = nestedType.GetField(
                secondFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (firstField == null || secondField == null)
            {
                return;
            }

            PaletteValueRenderer renderer = PaletteValueRenderer.CreateDualColorRenderer(
                nestedType,
                firstField,
                EditorGUIUtility.TrTextContent(firstLabel),
                secondField,
                EditorGUIUtility.TrTextContent(secondLabel)
            );

            if (renderer != null)
            {
                renderers[nestedType] = renderer;
            }
        }

        internal static void TryRegisterWEnumPaletteRenderer(
            IDictionary<Type, PaletteValueRenderer> renderers,
            Type containerType
        )
        {
            Type nestedType = containerType.GetNestedType(
                nameof(UnityHelpersSettings.WEnumToggleButtonsCustomColor),
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (nestedType == null)
            {
                return;
            }

            FieldInfo selectedBackground = nestedType.GetField(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsSelectedBackground,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo selectedText = nestedType.GetField(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsSelectedText,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo inactiveBackground = nestedType.GetField(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsInactiveBackground,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            FieldInfo inactiveText = nestedType.GetField(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsInactiveText,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (
                selectedBackground == null
                || selectedText == null
                || inactiveBackground == null
                || inactiveText == null
            )
            {
                return;
            }

            PaletteValueRenderer renderer = PaletteValueRenderer.CreateWEnumRenderer(
                nestedType,
                selectedBackground,
                EditorGUIUtility.TrTextContent("Selected BG"),
                selectedText,
                EditorGUIUtility.TrTextContent("Selected Text"),
                inactiveBackground,
                EditorGUIUtility.TrTextContent("Inactive BG"),
                inactiveText,
                EditorGUIUtility.TrTextContent("Inactive Text")
            );
            if (renderer != null)
            {
                renderers[nestedType] = renderer;
            }
        }

        internal static object EnsurePaletteValueInstance(object current, Type type)
        {
            object instance = current;
            if (instance == null || !type.IsInstanceOfType(instance))
            {
                instance = GetDefaultValue(type);
            }

            return CloneComplexValue(instance, type) ?? instance;
        }

        internal delegate void PaletteValueDrawHandler(
            Rect rect,
            GUIContent label,
            ref object value
        );

        internal sealed class PaletteValueRenderer
        {
            private readonly Type _targetType;
            private readonly Func<float> _heightProvider;
            private readonly PaletteValueDrawHandler _drawHandler;

            private PaletteValueRenderer(
                Type targetType,
                Func<float> heightProvider,
                PaletteValueDrawHandler drawHandler
            )
            {
                _targetType = targetType;
                _heightProvider = heightProvider;
                _drawHandler = drawHandler;
            }

            public float Height => _heightProvider();

            public void Draw(Rect rect, GUIContent label, ref object value)
            {
                _drawHandler(rect, label, ref value);
            }

            public static PaletteValueRenderer CreateDualColorRenderer(
                Type targetType,
                FieldInfo firstField,
                GUIContent firstLabel,
                FieldInfo secondField,
                GUIContent secondLabel
            )
            {
                if (
                    targetType == null
                    || firstField == null
                    || secondField == null
                    || firstLabel == null
                    || secondLabel == null
                )
                {
                    return null;
                }

                return new PaletteValueRenderer(
                    targetType,
                    static () => EditorGUIUtility.singleLineHeight,
                    delegate(Rect rect, GUIContent label, ref object instance)
                    {
                        instance = EnsurePaletteValueInstance(instance, targetType);
                        Rect contentRect = EditorGUI.PrefixLabel(rect, label);
                        float spacing = EditorGUIUtility.standardVerticalSpacing;
                        float halfWidth = Mathf.Max(0f, (contentRect.width - spacing) * 0.5f);
                        Rect firstRect = new(
                            contentRect.x,
                            contentRect.y,
                            halfWidth,
                            EditorGUIUtility.singleLineHeight
                        );
                        Rect secondRect = new(
                            firstRect.xMax + spacing,
                            contentRect.y,
                            halfWidth,
                            EditorGUIUtility.singleLineHeight
                        );

                        Color first = (Color)(firstField.GetValue(instance) ?? Color.clear);
                        Color updatedFirst = EditorGUI.ColorField(firstRect, firstLabel, first);
                        if (updatedFirst != first)
                        {
                            firstField.SetValue(instance, updatedFirst);
                        }

                        Color second = (Color)(secondField.GetValue(instance) ?? Color.clear);
                        Color updatedSecond = EditorGUI.ColorField(secondRect, secondLabel, second);
                        if (updatedSecond != second)
                        {
                            secondField.SetValue(instance, updatedSecond);
                        }
                    }
                );
            }

            public static PaletteValueRenderer CreateWEnumRenderer(
                Type targetType,
                FieldInfo selectedBgField,
                GUIContent selectedBgLabel,
                FieldInfo selectedTextField,
                GUIContent selectedTextLabel,
                FieldInfo inactiveBgField,
                GUIContent inactiveBgLabel,
                FieldInfo inactiveTextField,
                GUIContent inactiveTextLabel
            )
            {
                if (
                    targetType == null
                    || selectedBgField == null
                    || selectedTextField == null
                    || inactiveBgField == null
                    || inactiveTextField == null
                )
                {
                    return null;
                }

                return new PaletteValueRenderer(
                    targetType,
                    static () =>
                        (EditorGUIUtility.singleLineHeight * 2f)
                        + EditorGUIUtility.standardVerticalSpacing,
                    delegate(Rect rect, GUIContent label, ref object instance)
                    {
                        instance = EnsurePaletteValueInstance(instance, targetType);
                        Rect contentRect = EditorGUI.PrefixLabel(rect, label);
                        float rowHeight = EditorGUIUtility.singleLineHeight;
                        float spacing = EditorGUIUtility.standardVerticalSpacing;
                        float halfWidth = Mathf.Max(0f, (contentRect.width - spacing) * 0.5f);

                        Rect selectedBgRect = new(
                            contentRect.x,
                            contentRect.y,
                            halfWidth,
                            rowHeight
                        );
                        Rect selectedTextRect = new(
                            selectedBgRect.xMax + spacing,
                            contentRect.y,
                            halfWidth,
                            rowHeight
                        );
                        Rect inactiveBgRect = new(
                            contentRect.x,
                            contentRect.y + rowHeight + spacing,
                            halfWidth,
                            rowHeight
                        );
                        Rect inactiveTextRect = new(
                            inactiveBgRect.xMax + spacing,
                            inactiveBgRect.y,
                            halfWidth,
                            rowHeight
                        );

                        Color selectedBg = (Color)(
                            selectedBgField.GetValue(instance) ?? Color.clear
                        );
                        Color updatedSelectedBg = EditorGUI.ColorField(
                            selectedBgRect,
                            selectedBgLabel,
                            selectedBg
                        );
                        if (updatedSelectedBg != selectedBg)
                        {
                            selectedBgField.SetValue(instance, updatedSelectedBg);
                        }

                        Color selectedText = (Color)(
                            selectedTextField.GetValue(instance) ?? Color.clear
                        );
                        Color updatedSelectedText = EditorGUI.ColorField(
                            selectedTextRect,
                            selectedTextLabel,
                            selectedText
                        );
                        if (updatedSelectedText != selectedText)
                        {
                            selectedTextField.SetValue(instance, updatedSelectedText);
                        }

                        Color inactiveBg = (Color)(
                            inactiveBgField.GetValue(instance) ?? Color.clear
                        );
                        Color updatedInactiveBg = EditorGUI.ColorField(
                            inactiveBgRect,
                            inactiveBgLabel,
                            inactiveBg
                        );
                        if (updatedInactiveBg != inactiveBg)
                        {
                            inactiveBgField.SetValue(instance, updatedInactiveBg);
                        }

                        Color inactiveText = (Color)(
                            inactiveTextField.GetValue(instance) ?? Color.clear
                        );
                        Color updatedInactiveText = EditorGUI.ColorField(
                            inactiveTextRect,
                            inactiveTextLabel,
                            inactiveText
                        );
                        if (updatedInactiveText != inactiveText)
                        {
                            inactiveTextField.SetValue(instance, updatedInactiveText);
                        }
                    }
                );
            }
        }

        internal static void SetPropertyValue(SerializedProperty property, object value, Type type)
        {
            if (property == null)
            {
                return;
            }

            if (property.isArray && TrySetArrayPropertyValue(property, value, type))
            {
                return;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.longValue = Convert.ToInt64(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = value is true;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = value as string ?? string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = value is Color color ? color : Color.clear;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = value as Object;
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = value is Rect rect ? rect : default;
                    break;
                case SerializedPropertyType.RectInt:
                    property.rectIntValue = value is RectInt rectInt ? rectInt : default;
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = value is Bounds bounds ? bounds : default;
                    break;
                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = value is BoundsInt boundsInt ? boundsInt : default;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = value is Vector2 vector2 ? vector2 : default;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = value is Vector3 vector3 ? vector3 : default;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = value is Vector4 vector4 ? vector4 : default;
                    break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = value is Quaternion quaternion
                        ? quaternion
                        : Quaternion.identity;
                    break;
                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = value is Vector2Int vector2Int
                        ? vector2Int
                        : default;
                    break;
                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = value is Vector3Int vector3Int
                        ? vector3Int
                        : default;
                    break;
                case SerializedPropertyType.Enum:
                    int enumNumeric = value != null ? Convert.ToInt32(value) : 0;
                    property.intValue = enumNumeric;
                    string enumName = value != null ? Enum.GetName(type, value) : null;
                    if (!string.IsNullOrEmpty(enumName))
                    {
                        int nameIndex = Array.IndexOf(property.enumNames, enumName);
                        property.enumValueIndex = nameIndex >= 0 ? nameIndex : enumNumeric;
                    }
                    else
                    {
                        property.enumValueIndex = enumNumeric;
                    }
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = value as AnimationCurve ?? new AnimationCurve();
                    break;
                case SerializedPropertyType.Hash128:
                    property.hash128Value = value is Hash128 hash ? hash : default;
                    break;
                case SerializedPropertyType.ManagedReference:
                    property.managedReferenceValue = value;
                    break;
                case SerializedPropertyType.Generic:
                    if (!TryAssignComplexValue(property, value, type))
                    {
                        CopyComplexValueToPropertyFields(property, value, type);
                    }
                    break;
                default:
                    throw new NotSupportedException(
                        $"Unsupported property type: {property.propertyType}"
                    );
            }
        }

        private static bool TrySetArrayPropertyValue(
            SerializedProperty property,
            object value,
            Type declaredType
        )
        {
            if (
                property == null
                || !property.isArray
                || property.propertyType == SerializedPropertyType.String
                || declaredType == typeof(string)
            )
            {
                return false;
            }

            IList listValue = value as IList;
            Type elementType = ResolveArrayElementType(declaredType);

            if (listValue == null)
            {
                property.arraySize = 0;
                return true;
            }

            property.arraySize = listValue.Count;
            for (int index = 0; index < listValue.Count; index++)
            {
                SerializedProperty elementProperty = property.GetArrayElementAtIndex(index);
                object elementValue = listValue[index];
                SetPropertyValue(elementProperty, elementValue, elementType);
            }

            return true;
        }

        private static Type ResolveArrayElementType(Type declaredType)
        {
            if (declaredType == null)
            {
                return null;
            }

            if (declaredType.IsArray)
            {
                return declaredType.GetElementType();
            }

            if (declaredType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(declaredType))
            {
                Type[] arguments = declaredType.GetGenericArguments();
                if (arguments.Length > 0)
                {
                    return arguments[0];
                }
            }

            return null;
        }

        internal static object GetPropertyValue(SerializedProperty property, Type type)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    long longValue = property.longValue;
                    if (type.IsEnum)
                    {
                        return Enum.ToObject(type, longValue);
                    }

                    if (type == typeof(int))
                    {
                        return (int)longValue;
                    }

                    return longValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    float floatValue = property.floatValue;
                    return type == typeof(double) ? (double)floatValue : floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return property.colorValue;
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue;
                case SerializedPropertyType.Rect:
                    return property.rectValue;
                case SerializedPropertyType.RectInt:
                    return property.rectIntValue;
                case SerializedPropertyType.Bounds:
                    return property.boundsValue;
                case SerializedPropertyType.BoundsInt:
                    return property.boundsIntValue;
                case SerializedPropertyType.Vector2:
                    return property.vector2Value;
                case SerializedPropertyType.Vector3:
                    return property.vector3Value;
                case SerializedPropertyType.Vector4:
                    return property.vector4Value;
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue;
                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue;
                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue;
                case SerializedPropertyType.Enum:
                    return Enum.ToObject(type, property.intValue);
                case SerializedPropertyType.AnimationCurve:
                    return property.animationCurveValue;
                case SerializedPropertyType.Hash128:
                    return property.hash128Value;
                case SerializedPropertyType.ManagedReference:
                    return property.managedReferenceValue;
                case SerializedPropertyType.Generic:
                    return CopyComplexValueFromProperty(property, type);
                default:
                    return null;
            }
        }

        private static object CopyComplexValueFromProperty(
            SerializedProperty source,
            Type valueType
        )
        {
            if (valueType == null)
            {
                return null;
            }

            if (source.propertyType == SerializedPropertyType.ManagedReference)
            {
                return CloneComplexValue(source.managedReferenceValue, valueType);
            }

            if (source.propertyType != SerializedPropertyType.Generic)
            {
                return null;
            }

            if (!TryCreateDefaultInstance(valueType, out object instance))
            {
                return null;
            }

            foreach (FieldInfo field in GetSerializableFields(valueType))
            {
                SerializedProperty child = source.FindPropertyRelative(field.Name);
                if (child == null)
                {
                    continue;
                }

                object fieldValue = GetPropertyValue(child, field.FieldType);
                field.SetValue(instance, fieldValue);
            }

            return instance;
        }

        private static IEnumerable<FieldInfo> GetSerializableFields(Type type)
        {
            if (type == null)
            {
                yield break;
            }

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type current = type;
            HashSet<string> yielded = new();

            while (current != null && current != typeof(object))
            {
                FieldInfo[] fields = current.GetFields(Flags);
                foreach (FieldInfo field in fields)
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    if (field.IsNotSerialized)
                    {
                        continue;
                    }

                    if (
                        !field.IsPublic
                        && !ReflectionHelpers.HasAttributeSafe<SerializeField>(field, inherit: true)
                    )
                    {
                        continue;
                    }

                    if (!yielded.Add($"{field.DeclaringType?.FullName}.{field.Name}"))
                    {
                        continue;
                    }

                    yield return field;
                }

                current = current.BaseType;
            }
        }

        private static void ClearArrayElement(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.longValue = default;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = default;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = default;
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = Color.clear;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = null;
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = default;
                    break;
                case SerializedPropertyType.RectInt:
                    property.rectIntValue = default;
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = default;
                    break;
                case SerializedPropertyType.BoundsInt:
                    property.boundsIntValue = default;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = default;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = default;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = default;
                    break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = Quaternion.identity;
                    break;
                case SerializedPropertyType.Vector2Int:
                    property.vector2IntValue = default;
                    break;
                case SerializedPropertyType.Vector3Int:
                    property.vector3IntValue = default;
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = 0;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = new AnimationCurve();
                    break;
                case SerializedPropertyType.Hash128:
                    property.hash128Value = default;
                    break;
                case SerializedPropertyType.ManagedReference:
                    property.managedReferenceValue = null;
                    break;
            }
        }

        internal static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is Object leftObj && right is Object rightObj)
            {
                if (leftObj == null && rightObj == null)
                {
                    return true;
                }

                if (leftObj == null || rightObj == null)
                {
                    return false;
                }
            }

            if (left == null || right == null)
            {
                if (ListsAreNullEquivalent(left, right))
                {
                    return true;
                }

                return left == right;
            }

            if (left is IList leftList && right is IList rightList)
            {
                if (leftList.Count != rightList.Count)
                {
                    return false;
                }

                for (int index = 0; index < leftList.Count; index++)
                {
                    object leftItem = leftList[index];
                    object rightItem = rightList[index];
                    if (!ValuesEqual(leftItem, rightItem))
                    {
                        return false;
                    }
                }

                return true;
            }

            Type leftType = left.GetType();
            Type rightType = right.GetType();
            if (leftType == rightType && ShouldPerformFieldwiseComparison(leftType))
            {
                return FieldwiseValuesEqual(left, right, leftType);
            }

            return left.Equals(right);
        }

        private static bool ShouldPerformFieldwiseComparison(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.IsPrimitive || type.IsEnum)
            {
                return false;
            }

            if (type == typeof(string) || typeof(Object).IsAssignableFrom(type))
            {
                return false;
            }

            return type.IsValueType;
        }

        private static bool FieldwiseValuesEqual(object left, object right, Type type)
        {
            foreach (FieldInfo field in GetSerializableFields(type))
            {
                object leftValue = field.GetValue(left);
                object rightValue = field.GetValue(right);
                if (!ValuesEqual(leftValue, rightValue))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ListsAreNullEquivalent(object left, object right)
        {
            if (left == null && right is IList rightList)
            {
                return rightList.Count == 0;
            }

            if (right == null && left is IList leftList)
            {
                return leftList.Count == 0;
            }

            return false;
        }

        internal static object GetDefaultValue(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type == typeof(string))
            {
                return string.Empty;
            }

            if (type == typeof(Color))
            {
                return Color.clear;
            }

            if (type == typeof(AnimationCurve))
            {
                return new AnimationCurve();
            }

            if (type.IsValueType)
            {
                return TryInvokeParameterlessConstructor(type, out object value) ? value : null;
            }

            if (TryCreateDefaultInstance(type, out object instance))
            {
                return instance;
            }

            return null;
        }

        internal struct CommitResult
        {
            public bool added;
            public int index;
        }

        private enum PaginationControlLayout
        {
            None,
            PrevNext,
            Full,
        }

        internal sealed class PendingEntry
        {
            public object key;
            public object value;
            public bool isExpanded;
            public AnimBool foldoutAnim;
            public bool isSorted;
            public PendingValueWrapper keyWrapper;
            public SerializedObject keyWrapperSerialized;
            public SerializedProperty keyWrapperProperty;
            public PendingValueWrapper valueWrapper;
            public SerializedObject valueWrapperSerialized;
            public SerializedProperty valueWrapperProperty;
            public bool keyWrapperDirty = true;
            public bool valueWrapperDirty = true;
            public int valueRevision;
            public int lastDuplicateCheckIndex = -1;
            public int lastDuplicateCheckValueRevision = -1;
            public bool lastDuplicateCheckResult;
            public int valueWrapperSyncRevision;
        }

        internal sealed class NullKeyInfo
        {
            public string tooltip = string.Empty;
        }

        internal sealed class NullKeyState
        {
            private readonly HashSet<int> _nullIndices = new();
            private readonly Dictionary<int, NullKeyInfo> _nullLookup = new();
            private readonly List<int> _scratch = new();
            private int _lastArraySize = -1;

            public bool HasNullKeys { get; private set; }
            public string WarningMessage { get; private set; } = string.Empty;

            public bool IsDirty => _lastArraySize < 0;

            public void MarkDirty()
            {
                _lastArraySize = -1;
            }

            public bool Refresh(SerializedProperty keysProperty, Type keyType)
            {
                _scratch.Clear();

                if (keysProperty == null || keyType == null || !TypeSupportsNullReferences(keyType))
                {
                    bool changed = HasNullKeys || _nullLookup.Count > 0;
                    if (changed)
                    {
                        _nullIndices.Clear();
                        _nullLookup.Clear();
                    }

                    HasNullKeys = false;
                    WarningMessage = string.Empty;
                    _lastArraySize = -1;
                    return changed;
                }

                int currentArraySize = keysProperty.arraySize;
                if (currentArraySize == _lastArraySize && !HasNullKeys && _nullIndices.Count == 0)
                {
                    return false;
                }

                _lastArraySize = currentArraySize;
                int count = currentArraySize;
                for (int index = 0; index < count; index++)
                {
                    SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                    object keyValue =
                        keyProperty != null ? GetPropertyValue(keyProperty, keyType) : null;
                    if (ReferenceEquals(keyValue, null))
                    {
                        _scratch.Add(index);
                    }
                }

                bool changedIndices = UpdateIndices();
                HasNullKeys = _nullIndices.Count > 0;
                WarningMessage = HasNullKeys ? BuildNullKeySummary(_nullIndices) : string.Empty;
                return changedIndices;
            }

            private bool UpdateIndices()
            {
                bool changed = _scratch.Count != _nullIndices.Count;

                if (!changed)
                {
                    foreach (int index in _scratch)
                    {
                        if (!_nullIndices.Contains(index))
                        {
                            changed = true;
                            break;
                        }
                    }
                }

                if (!changed)
                {
                    return false;
                }

                _nullIndices.Clear();
                _nullLookup.Clear();

                foreach (int index in _scratch)
                {
                    _nullIndices.Add(index);
                    _nullLookup[index] = new NullKeyInfo { tooltip = BuildNullKeyTooltip(index) };
                }

                return true;
            }

            private static string BuildNullKeyTooltip(int index)
            {
                using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                    64,
                    out StringBuilder builder
                );
                builder.Clear();
                builder.Append("Null key detected at index ");
                builder.Append(index);
                builder.Append(". Entry will be ignored at runtime.");
                return builder.ToString();
            }

            public bool TryGetInfo(int arrayIndex, out NullKeyInfo info)
            {
                return _nullLookup.TryGetValue(arrayIndex, out info);
            }
        }

        internal sealed class DuplicateKeyInfo
        {
            public string tooltip = string.Empty;
            public bool isPrimary;
        }

        internal sealed class DuplicateKeyState
        {
            private readonly Dictionary<int, DuplicateKeyInfo> _duplicateLookup = new();
            private readonly Dictionary<int, double> _duplicateAnimationStartTimes = new();
            private readonly List<int> _animationKeysScratch = new();
            private readonly List<int> _summaryIndicesScratch = new();
            private readonly Dictionary<object, List<int>> _groupingDictionary = new(
                new KeyEqualityComparer()
            );
            private readonly List<List<int>> _listPool = new();
            private StringBuilder _summaryBuilder;
            private bool _lastHadDuplicates;
            private int _lastArraySize = -1;
            private bool _animationsCompleted;

            public bool HasDuplicates { get; private set; }
            public string SummaryTooltip { get; private set; } = string.Empty;

            public bool IsEmpty => _duplicateLookup.Count == 0;

            public bool IsAnimating => HasDuplicates && !_animationsCompleted;

            public bool IsDirty => _lastArraySize < 0;

            public void MarkDirty()
            {
                _lastArraySize = -1;
            }

            public bool Refresh(SerializedProperty keysProperty, Type keyType)
            {
                if (keysProperty == null || keyType == null)
                {
                    bool previouslyDuplicated = _lastHadDuplicates;
                    _lastHadDuplicates = false;
                    _lastArraySize = -1;
                    _duplicateLookup.Clear();
                    HasDuplicates = false;
                    SummaryTooltip = string.Empty;
                    if (_duplicateAnimationStartTimes.Count > 0)
                    {
                        _duplicateAnimationStartTimes.Clear();
                    }
                    return previouslyDuplicated;
                }

                int currentArraySize = keysProperty.arraySize;
                if (currentArraySize == _lastArraySize && !_lastHadDuplicates && !HasDuplicates)
                {
                    return false;
                }

                _lastArraySize = currentArraySize;
                _duplicateLookup.Clear();
                HasDuplicates = false;
                SummaryTooltip = string.Empty;

                int count = currentArraySize;

                foreach (List<int> pooledList in _groupingDictionary.Values)
                {
                    pooledList.Clear();
                    _listPool.Add(pooledList);
                }
                _groupingDictionary.Clear();

                using PooledResource<StringBuilder> summaryBuilderLease = Buffers.GetStringBuilder(
                    Math.Max(count * 8, 64),
                    out StringBuilder summaryBuilder
                );
                _summaryBuilder = summaryBuilder;
                _summaryBuilder.Clear();

                for (int index = 0; index < count; index++)
                {
                    SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                    object keyValue =
                        keyProperty != null ? GetPropertyValue(keyProperty, keyType) : null;
                    object lookupKey = keyValue ?? NullKeySentinel;

                    if (!_groupingDictionary.TryGetValue(lookupKey, out List<int> indices))
                    {
                        if (_listPool.Count > 0)
                        {
                            indices = _listPool[_listPool.Count - 1];
                            _listPool.RemoveAt(_listPool.Count - 1);
                        }
                        else
                        {
                            indices = new List<int>(4);
                        }
                        _groupingDictionary[lookupKey] = indices;
                    }
                    indices.Add(index);
                }

                int duplicateGroupCount = 0;
                int displayedSummaryGroups = 0;

                foreach (KeyValuePair<object, List<int>> entry in _groupingDictionary)
                {
                    List<int> indices = entry.Value;
                    if (indices.Count <= 1)
                    {
                        continue;
                    }

                    duplicateGroupCount++;
                    HasDuplicates = true;
                    string formattedKey = FormatDuplicateKeyDisplay(entry.Key);
                    string tooltip = BuildDuplicateTooltip(formattedKey, indices);

                    for (int occurrence = 0; occurrence < indices.Count; occurrence++)
                    {
                        int arrayIndex = indices[occurrence];
                        DuplicateKeyInfo info = new()
                        {
                            tooltip = tooltip,
                            isPrimary = occurrence == 0,
                        };
                        _duplicateLookup[arrayIndex] = info;
                    }

                    if (displayedSummaryGroups < DuplicateSummaryDisplayLimit)
                    {
                        if (_summaryBuilder.Length > 0)
                        {
                            _summaryBuilder.AppendLine();
                        }
                        AppendDuplicateSummaryLine(formattedKey, indices);
                        displayedSummaryGroups++;
                    }
                }

                SummaryTooltip = BuildDuplicateSummaryText(
                    duplicateGroupCount,
                    displayedSummaryGroups
                );

                _summaryBuilder = null;

                UpdateAnimationTracking();

                bool changed = HasDuplicates != _lastHadDuplicates;
                _lastHadDuplicates = HasDuplicates;
                if (changed && HasDuplicates)
                {
                    _animationsCompleted = false;
                }
                return changed;
            }

            public float GetAnimationOffset(int arrayIndex, double currentTime, int cycleLimit)
            {
                if (!_duplicateAnimationStartTimes.TryGetValue(arrayIndex, out double startTime))
                {
                    startTime = currentTime;
                    _duplicateAnimationStartTimes[arrayIndex] = startTime;
                    _animationsCompleted = false;
                }

                return EvaluateDuplicateTweenOffset(arrayIndex, startTime, currentTime, cycleLimit);
            }

            public void CheckAnimationCompletion(double currentTime, int cycleLimit)
            {
                if (_animationsCompleted || !HasDuplicates || cycleLimit <= 0)
                {
                    return;
                }

                double cycleDuration = (2d * Math.PI) / DuplicateShakeFrequency;
                double maxDuration = cycleDuration * cycleLimit;

                bool allComplete = true;
                foreach (KeyValuePair<int, double> entry in _duplicateAnimationStartTimes)
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

            public bool TryGetInfo(int arrayIndex, out DuplicateKeyInfo info)
            {
                return _duplicateLookup.TryGetValue(arrayIndex, out info);
            }

            private void AppendDuplicateSummaryLine(string formattedKey, List<int> indices)
            {
                if (string.IsNullOrEmpty(formattedKey) || indices == null || indices.Count <= 1)
                {
                    return;
                }

                if (_summaryBuilder == null)
                {
                    return;
                }

                _summaryIndicesScratch.Clear();
                _summaryIndicesScratch.AddRange(indices);
                _summaryIndicesScratch.Sort();

                _summaryBuilder.Append("Duplicate key ");
                _summaryBuilder.Append(formattedKey);
                _summaryBuilder.Append(" at entries ");

                for (int index = 0; index < _summaryIndicesScratch.Count; index++)
                {
                    if (index > 0)
                    {
                        _summaryBuilder.Append(", ");
                    }

                    _summaryBuilder.Append(_summaryIndicesScratch[index] + 1);
                }
            }

            private string BuildDuplicateSummaryText(int duplicateGroupCount, int displayedGroups)
            {
                if (_summaryBuilder == null)
                {
                    return string.Empty;
                }

                if (duplicateGroupCount <= 0)
                {
                    return string.Empty;
                }

                if (displayedGroups == 0)
                {
                    if (duplicateGroupCount == 1)
                    {
                        return "Duplicate key detected. Resolve conflicts to prevent silent overwrites. The last entry wins at runtime.";
                    }

                    using PooledResource<StringBuilder> lease = Buffers.GetStringBuilder(
                        128,
                        out StringBuilder countBuilder
                    );
                    countBuilder.Clear();
                    countBuilder.Append(duplicateGroupCount);
                    countBuilder.Append(
                        " duplicate keys detected. Resolve conflicts to prevent silent overwrites. The last entry wins at runtime."
                    );
                    return countBuilder.ToString();
                }

                if (duplicateGroupCount > displayedGroups)
                {
                    if (_summaryBuilder.Length > 0)
                    {
                        _summaryBuilder.AppendLine();
                    }

                    int remainingGroups = duplicateGroupCount - displayedGroups;
                    if (remainingGroups == 1)
                    {
                        _summaryBuilder.Append("1 additional duplicate group omitted for brevity.");
                    }
                    else
                    {
                        _summaryBuilder.Append(remainingGroups);
                        _summaryBuilder.Append(" additional duplicate groups omitted for brevity.");
                    }
                }

                if (_summaryBuilder.Length > 0)
                {
                    _summaryBuilder.AppendLine();
                }

                _summaryBuilder.Append(
                    "Resolve conflicts to prevent silent overwrites. The last entry wins at runtime."
                );

                return _summaryBuilder.ToString();
            }

            private void UpdateAnimationTracking()
            {
                if (!HasDuplicates || _duplicateLookup.Count == 0)
                {
                    if (_duplicateAnimationStartTimes.Count > 0)
                    {
                        _duplicateAnimationStartTimes.Clear();
                    }

                    return;
                }

                double now = GetHighResolutionTime();

                foreach (int index in _duplicateLookup.Keys)
                {
                    _duplicateAnimationStartTimes.TryAdd(index, now);
                }

                if (_duplicateAnimationStartTimes.Count == 0)
                {
                    return;
                }

                _animationKeysScratch.Clear();

                foreach (KeyValuePair<int, double> entry in _duplicateAnimationStartTimes)
                {
                    if (!_duplicateLookup.ContainsKey(entry.Key))
                    {
                        _animationKeysScratch.Add(entry.Key);
                    }
                }

                foreach (int animationStartTime in _animationKeysScratch)
                {
                    _duplicateAnimationStartTimes.Remove(animationStartTime);
                }
            }
        }

        internal sealed class PaginationState
        {
            public int pageIndex;
            public int pageSize;
            public int selectedIndex = -1;
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

        internal sealed class KeyIndexCache
        {
            public readonly Dictionary<object, int> indices = new(new KeyEqualityComparer());
            public int arraySize;
        }

        internal sealed class KeyEqualityComparer : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return EqualsCore(x, y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                return GetHashCodeCore(obj);
            }

            private static bool EqualsCore(object x, object y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (ReferenceEquals(x, NullKeySentinel))
                {
                    x = null;
                }

                if (ReferenceEquals(y, NullKeySentinel))
                {
                    y = null;
                }

                if (x is Object xObj)
                {
                    if (y is Object yObj)
                    {
                        return xObj == yObj;
                    }

                    return false;
                }

                if (y is Object)
                {
                    return false;
                }

                if (x == null || y == null)
                {
                    return x == y;
                }

                return x.Equals(y);
            }

            private static int GetHashCodeCore(object obj)
            {
                if (ReferenceEquals(obj, NullKeySentinel) || obj == null)
                {
                    return 0;
                }

                if (obj is Object objObj && objObj == null)
                {
                    return 0;
                }

                return obj.GetHashCode();
            }
        }

        internal static void SyncRuntimeDictionary(SerializedProperty dictionaryProperty)
        {
            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            Object[] targets = serializedObject.targetObjects;
            string propertyPath = dictionaryProperty.propertyPath;

            foreach (Object target in targets)
            {
                object dictionaryInstance = GetTargetObjectOfProperty(target, propertyPath);
                bool isSerializableDictionaryBase =
                    dictionaryInstance is SerializableDictionaryBase;
                bool calledSave = false;
                bool isScriptableSingletonTarget = IsScriptableSingletonType(target);

                // For ScriptableSingleton targets, we must NOT call EditorAfterDeserialize()
                // immediately after ApplyModifiedProperties because the managed serialized fields
                // (_keys, _values) are not yet updated by Unity's serialization system.
                // Calling EditorAfterDeserialize would read stale data and overwrite our changes.
                // Instead, we use ForwardSyncFromSerializedProperties to read the current values
                // directly from the SerializedProperties and update the runtime dictionary.
                if (
                    isScriptableSingletonTarget
                    && dictionaryInstance is SerializableDictionaryBase baseDictionary
                )
                {
                    ForwardSyncFromSerializedProperties(dictionaryProperty, baseDictionary);
                    EditorUtility.SetDirty(target);
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
                else if (dictionaryInstance is SerializableDictionaryBase nonSingletonDictionary)
                {
                    nonSingletonDictionary.EditorAfterDeserialize();
                    EditorUtility.SetDirty(target);
                }
                else if (dictionaryInstance is ISerializationCallbackReceiver receiver)
                {
                    receiver.OnAfterDeserialize();
                    EditorUtility.SetDirty(target);
                }

                PaletteSerializationDiagnostics.ReportSyncRuntimeDictionary(
                    serializedObject,
                    propertyPath,
                    dictionaryInstance,
                    isSerializableDictionaryBase,
                    calledSave
                );
            }

            serializedObject.UpdateIfRequiredOrScript();
        }

        /// <summary>
        /// Performs a forward sync from SerializedProperties to the runtime dictionary.
        /// This is used for ScriptableSingleton targets where the managed fields are stale
        /// after ApplyModifiedProperties. Instead of reading from the managed fields (which
        /// would give us stale data), we read directly from the SerializedProperties which
        /// have the current values.
        /// </summary>
        private static void ForwardSyncFromSerializedProperties(
            SerializedProperty dictionaryProperty,
            SerializableDictionaryBase baseDictionary
        )
        {
            // First, call OnBeforeSerialize to ensure the managed arrays are in sync with runtime
            // (This writes runtime state to managed arrays, which is the opposite of what we want,
            // but it's necessary to prepare for the next step)
            // Actually, we need to update the managed arrays FROM the SerializedProperties first

            // The managed arrays (_keys and _values) need to be updated from the SerializedProperties.
            // We'll read all key-value pairs from the SerializedProperties and rebuild the runtime dictionary.
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (
                keysProperty == null
                || valuesProperty == null
                || !keysProperty.isArray
                || !valuesProperty.isArray
            )
            {
                return;
            }

            int count = Mathf.Min(keysProperty.arraySize, valuesProperty.arraySize);

            // We need to update the managed arrays so that EditorAfterDeserialize reads current data.
            // The cleanest way is to directly set the managed field values using reflection.
            Type dictionaryType = baseDictionary.GetType();
            Type baseType = dictionaryType;
            while (baseType != null && !baseType.IsGenericType)
            {
                baseType = baseType.BaseType;
            }

            if (baseType == null)
            {
                // Fallback: just call EditorAfterDeserialize and hope the timing works out
                baseDictionary.EditorAfterDeserialize();
                return;
            }

            // Get the key and value types from the generic type arguments
            Type[] genericArgs = baseType.GetGenericArguments();
            if (genericArgs.Length < 2)
            {
                baseDictionary.EditorAfterDeserialize();
                return;
            }

            Type keyType = genericArgs[0];
            Type valueType = genericArgs.Length >= 2 ? genericArgs[1] : null;

            // Get the _keys and _values fields
            FieldInfo keysField = FindFieldInHierarchy(
                dictionaryType,
                SerializableDictionarySerializedPropertyNames.Keys
            );
            FieldInfo valuesField = FindFieldInHierarchy(
                dictionaryType,
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keysField == null || valuesField == null)
            {
                baseDictionary.EditorAfterDeserialize();
                return;
            }

            // Create new arrays with the correct size
            Array keysArray = Array.CreateInstance(keyType, count);
            Array valuesArray = Array.CreateInstance(valueType, count);

            // Copy values from SerializedProperties to the arrays
            for (int i = 0; i < count; i++)
            {
                SerializedProperty keyProp = keysProperty.GetArrayElementAtIndex(i);
                SerializedProperty valueProp = valuesProperty.GetArrayElementAtIndex(i);

                object keyValue = GetPropertyValueBoxed(keyProp, keyType);
                object valueValue = GetPropertyValueBoxed(valueProp, valueType);

                if (keyValue != null || !keyType.IsValueType)
                {
                    keysArray.SetValue(keyValue, i);
                }
                if (valueValue != null || !valueType.IsValueType)
                {
                    valuesArray.SetValue(valueValue, i);
                }
            }

            // Set the managed fields directly
            keysField.SetValue(baseDictionary, keysArray);
            valuesField.SetValue(baseDictionary, valuesArray);

            // Now call EditorAfterDeserialize which will read from the updated managed fields
            baseDictionary.EditorAfterDeserialize();
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
                if (field != null)
                {
                    return field;
                }
                type = type.BaseType;
            }
            return null;
        }

        private static object GetPropertyValueBoxed(SerializedProperty property, Type targetType)
        {
            if (property == null)
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            return property.propertyType switch
            {
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Color => property.colorValue,
                SerializedPropertyType.ObjectReference => property.objectReferenceValue,
                SerializedPropertyType.Enum => property.enumValueIndex,
                SerializedPropertyType.Vector2 => property.vector2Value,
                SerializedPropertyType.Vector3 => property.vector3Value,
                SerializedPropertyType.Vector4 => property.vector4Value,
                SerializedPropertyType.Rect => property.rectValue,
                SerializedPropertyType.ArraySize => property.intValue,
                SerializedPropertyType.Character => (char)property.intValue,
                SerializedPropertyType.AnimationCurve => property.animationCurveValue,
                SerializedPropertyType.Bounds => property.boundsValue,
                SerializedPropertyType.Quaternion => property.quaternionValue,
                SerializedPropertyType.Vector2Int => property.vector2IntValue,
                SerializedPropertyType.Vector3Int => property.vector3IntValue,
                SerializedPropertyType.RectInt => property.rectIntValue,
                SerializedPropertyType.BoundsInt => property.boundsIntValue,
                SerializedPropertyType.ManagedReference => property.managedReferenceValue,
                _ => GetComplexPropertyValue(property, targetType),
            };
        }

        private static object GetComplexPropertyValue(SerializedProperty property, Type targetType)
        {
            // For complex types (structs, classes), we need to create an instance and populate its fields
            if (targetType == null || property == null)
            {
                return null;
            }

            try
            {
                object instance = CreateInstanceSafe(targetType);
                if (instance == null)
                {
                    return null;
                }

                // Iterate through the property's children and set field values
                SerializedProperty iterator = property.Copy();
                SerializedProperty endProperty = property.GetEndProperty();
                bool enterChildren = true;
                int depth = property.depth;

                while (
                    iterator.NextVisible(enterChildren)
                    && !SerializedProperty.EqualContents(iterator, endProperty)
                )
                {
                    enterChildren = false;
                    if (iterator.depth <= depth)
                    {
                        break;
                    }

                    string fieldName = iterator.name;
                    FieldInfo field = FindFieldInHierarchy(targetType, fieldName);
                    if (field != null)
                    {
                        object fieldValue = GetPropertyValueBoxed(iterator, field.FieldType);
                        field.SetValue(instance, fieldValue);
                    }
                }

                return instance;
            }
            catch
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }
        }

        private static object CreateInstanceSafe(Type type)
        {
            if (type == null)
            {
                return null;
            }

            try
            {
                // Try default constructor first
                return Activator.CreateInstance(type);
            }
            catch
            {
                // For types without default constructor, try FormatterServices
                try
                {
                    return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                        type
                    );
                }
                catch
                {
                    return null;
                }
            }
        }

        private static object GetTargetObjectOfProperty(object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
            {
                return null;
            }

            string path = propertyPath.Replace(".Array.data[", "[");
            string[] elements = path.Split('.');

            object current = target;
            Type currentType = current.GetType();

            foreach (string elementWithPotentialIndex in elements)
            {
                if (current == null)
                {
                    return null;
                }

                string element = elementWithPotentialIndex;
                int index = -1;
                int bracketIndex = element.IndexOf('[', StringComparison.Ordinal);
                if (bracketIndex >= 0)
                {
                    int endBracket = element.IndexOf(']', bracketIndex + 1);
                    if (endBracket < 0)
                    {
                        return null;
                    }

                    string indexString = element.Substring(
                        bracketIndex + 1,
                        endBracket - bracketIndex - 1
                    );
                    if (!int.TryParse(indexString, out index))
                    {
                        return null;
                    }

                    element = element.Substring(0, bracketIndex);
                }

                if (!string.IsNullOrEmpty(element))
                {
                    // Use cached reflection lookups for better performance
                    if (
                        ReflectionHelpers.TryGetField(
                            currentType,
                            element,
                            out FieldInfo field,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        )
                    )
                    {
                        Func<object, object> getter = ReflectionHelpers.GetFieldGetter(field);
                        current = getter(current);
                        currentType = current?.GetType();
                    }
                    else if (
                        ReflectionHelpers.TryGetProperty(
                            currentType,
                            element,
                            out PropertyInfo propertyInfo,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        )
                    )
                    {
                        Func<object, object> getter = ReflectionHelpers.GetPropertyGetter(
                            propertyInfo
                        );
                        current = getter(current);
                        currentType = current?.GetType();
                    }
                    else
                    {
                        return null;
                    }
                }

                if (index >= 0)
                {
                    if (current is IList list)
                    {
                        if (index >= list.Count)
                        {
                            return null;
                        }

                        current = list[index];
                        currentType = current?.GetType();
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            return current;
        }

        /// <summary>
        /// Gets a cached string representation of an integer.
        /// Delegates to <see cref="EditorCacheHelper.GetCachedIntString"/> for shared LRU caching.
        /// </summary>
        private static string GetCachedIntString(int value)
        {
            return EditorCacheHelper.GetCachedIntString(value);
        }

        /// <summary>
        /// Gets a cached pagination label in the format "Page X / Y".
        /// Delegates to <see cref="EditorCacheHelper.GetPaginationLabel"/> for shared LRU caching.
        /// </summary>
        private static string GetPaginationLabel(int currentPage, int totalPages)
        {
            return EditorCacheHelper.GetPaginationLabel(currentPage, totalPages);
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

        internal void InvokeClearDictionary(
            SerializedProperty dictionaryProperty,
            SerializedProperty keysProperty,
            SerializedProperty valuesProperty,
            PaginationState pagination,
            ReorderableList list
        )
        {
            ClearDictionary(dictionaryProperty, keysProperty, valuesProperty, pagination, list);
        }
    }
}
