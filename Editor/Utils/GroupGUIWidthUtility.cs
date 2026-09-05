// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Diagnostics helper for debugging WGroup theming restoration issues.
    /// </summary>
    public static class WGroupThemingDiagnostics
    {
        /// <summary>
        /// When true, enables diagnostic logging for WGroup theming operations.
        /// </summary>
        public static bool Enabled { get; set; } = false;

        private const string LogPrefix = "[WGroupTheming] ";

        internal static void LogCaptureColors(
            Color contentColor,
            Color guiColor,
            Color backgroundColor,
            bool isInsideWGroup,
            int scopeDepth
        )
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}CaptureWGroupColors: "
                    + $"contentColor={FormatColor(contentColor)}, "
                    + $"guiColor={FormatColor(guiColor)}, "
                    + $"bgColor={FormatColor(backgroundColor)}, "
                    + $"isInsideWGroup={isInsideWGroup}, "
                    + $"scopeDepth={scopeDepth}"
            );
        }

        internal static void LogExitTheming(
            Color beforeContentColor,
            Color beforeGuiColor,
            Color beforeBgColor,
            Color afterContentColor,
            Color afterGuiColor,
            Color afterBgColor
        )
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}ExitWGroupTheming ENTER: "
                    + $"BEFORE: content={FormatColor(beforeContentColor)}, "
                    + $"gui={FormatColor(beforeGuiColor)}, "
                    + $"bg={FormatColor(beforeBgColor)} | "
                    + $"AFTER: content={FormatColor(afterContentColor)}, "
                    + $"gui={FormatColor(afterGuiColor)}, "
                    + $"bg={FormatColor(afterBgColor)}"
            );
        }

        internal static void LogRestoreTheming(
            string phase,
            Color contentColor,
            Color guiColor,
            Color backgroundColor
        )
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}RestoreWGroupTheming {phase}: "
                    + $"content={FormatColor(contentColor)}, "
                    + $"gui={FormatColor(guiColor)}, "
                    + $"bg={FormatColor(backgroundColor)}"
            );
        }

        internal static void LogDrawFoldout(
            string propertyPath,
            bool wasInsideWGroup,
            Color currentContentColor,
            Color currentGuiColor,
            Color savedContentColor,
            Color savedGuiColor
        )
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}DrawFoldout: path={propertyPath}, "
                    + $"wasInsideWGroup={wasInsideWGroup}, "
                    + $"CURRENT: content={FormatColor(currentContentColor)}, "
                    + $"gui={FormatColor(currentGuiColor)} | "
                    + $"SAVED: content={FormatColor(savedContentColor)}, "
                    + $"gui={FormatColor(savedGuiColor)}"
            );
        }

        internal static void LogFoldoutStyleColors(string phase, GUIStyle foldout)
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(
                $"{LogPrefix}FoldoutStyle {phase}: "
                    + $"normal={FormatColor(foldout.normal.textColor)}, "
                    + $"onNormal={FormatColor(foldout.onNormal.textColor)}, "
                    + $"hover={FormatColor(foldout.hover.textColor)}"
            );
        }

        internal static string FormatColor(Color c)
        {
            return $"({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2})";
        }
    }

    internal static class GroupGUIWidthUtility
    {
        private static float _totalPadding;
        private static float _totalLeftPadding;
        private static float _totalRightPadding;
        private static int _scopeDepth;
        private static bool _isInsideWGroupPropertyDraw;
        private static WGroupThemeState? _currentThemeState;

        internal static float CurrentHorizontalPadding => _totalPadding;
        internal static float CurrentLeftPadding => _totalLeftPadding;
        internal static float CurrentRightPadding => _totalRightPadding;
        internal static int CurrentScopeDepth => _scopeDepth;

        internal static bool IsInsideWGroupPropertyDraw => _isInsideWGroupPropertyDraw;

        internal static WGroupThemeState? CurrentThemeState => _currentThemeState;

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        internal static void ResetForTests()
        {
            _totalPadding = 0f;
            _totalLeftPadding = 0f;
            _totalRightPadding = 0f;
            _scopeDepth = 0;
            _isInsideWGroupPropertyDraw = false;
            _currentThemeState = null;
        }

        internal static IDisposable PushWGroupPropertyContext()
        {
            return new WGroupPropertyContextScope();
        }

        internal static void ApplyCurrentThemeColors()
        {
            if (_currentThemeState.HasValue)
            {
                WGroupThemeState state = _currentThemeState.Value;
                GUI.color = state.GuiColor;
                GUI.contentColor = state.ContentColor;
                GUI.backgroundColor = state.BackgroundColor;
                return;
            }

            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
        }

        /// <summary>
        /// Creates a scope that temporarily exits WGroup theming context.
        /// Within this scope, IsInsideWGroup will return false and GUI colors will be reset to defaults.
        /// Use this for complex property drawers (like SerializableDictionary/SerializableHashSet)
        /// that have their own theming and don't work well with WGroup palette overrides.
        /// </summary>
        internal static IDisposable ExitWGroupTheming()
        {
            return new ExitWGroupThemingScope();
        }

        /// <summary>
        /// Creates a scope that temporarily restores WGroup theming colors.
        /// Use this INSIDE an ExitWGroupTheming scope when you need to draw specific elements
        /// (like foldout labels) that should still use WGroup palette colors because they
        /// render against the WGroup background.
        /// </summary>
        /// <param name="savedColors">The saved GUI colors to restore (from before exiting theming).</param>
        internal static IDisposable RestoreWGroupTheming(WGroupSavedColors savedColors)
        {
            return new RestoreWGroupThemingScope(savedColors);
        }

        /// <summary>
        /// Captures the current WGroup GUI colors before exiting theming.
        /// Call this BEFORE entering ExitWGroupTheming to save the colors for later restoration.
        /// If currently inside a WGroup with a palette, uses the theme state colors which
        /// represent the actual palette colors (since GUI.contentColor may have been modified).
        /// </summary>
        internal static WGroupSavedColors CaptureWGroupColors()
        {
            // Use WGroup theme state because nested scopes can modify GUI.contentColor.
            WGroupSavedColors colors;
            if (_currentThemeState.HasValue)
            {
                WGroupThemeState state = _currentThemeState.Value;
                colors = new WGroupSavedColors
                {
                    ContentColor = state.ContentColor,
                    Color = state.GuiColor,
                    BackgroundColor = state.BackgroundColor,
                };
            }
            else
            {
                colors = new WGroupSavedColors
                {
                    ContentColor = GUI.contentColor,
                    Color = GUI.color,
                    BackgroundColor = GUI.backgroundColor,
                };
            }

            WGroupThemingDiagnostics.LogCaptureColors(
                colors.ContentColor,
                colors.Color,
                colors.BackgroundColor,
                _currentThemeState != null,
                _scopeDepth
            );

            return colors;
        }

        /// <summary>
        /// Cached foldout arrow texture for drawing themed foldouts.
        /// </summary>
        private static Texture2D _foldoutArrowRight;

        /// <summary>
        /// Cached expanded foldout arrow texture for drawing themed foldouts.
        /// </summary>
        private static Texture2D _foldoutArrowDown;

        /// <summary>
        /// Draws a foldout with proper WGroup theming. Unity's built-in EditorGUI.Foldout
        /// uses pre-baked icon textures that don't respect GUI.contentColor, so this method
        /// manually draws the arrow icon with the correct tint color.
        /// </summary>
        /// <param name="position">The rectangle to draw the foldout in.</param>
        /// <param name="isExpanded">Current expansion state.</param>
        /// <param name="content">The label content to display.</param>
        /// <param name="toggleOnLabelClick">Whether clicking the label toggles the foldout.</param>
        /// <returns>The new expansion state.</returns>
        internal static bool DrawThemedFoldout(
            Rect position,
            bool isExpanded,
            GUIContent content,
            bool toggleOnLabelClick = true
        )
        {
            if (_foldoutArrowRight == null)
            {
                _foldoutArrowRight =
                    EditorGUIUtility.IconContent("d_forward@2x").image as Texture2D;
                if (_foldoutArrowRight == null)
                {
                    _foldoutArrowRight =
                        EditorGUIUtility.IconContent("d_forward").image as Texture2D;
                }

                if (_foldoutArrowRight == null)
                {
                    _foldoutArrowRight =
                        EditorGUIUtility.IconContent("forward@2x").image as Texture2D;
                }

                if (_foldoutArrowRight == null)
                {
                    _foldoutArrowRight = EditorGUIUtility.IconContent("forward").image as Texture2D;
                }
            }

            if (_foldoutArrowDown == null)
            {
                _foldoutArrowDown =
                    EditorGUIUtility.IconContent("d_icon dropdown@2x").image as Texture2D;
                if (_foldoutArrowDown == null)
                {
                    _foldoutArrowDown =
                        EditorGUIUtility.IconContent("d_icon dropdown").image as Texture2D;
                }

                if (_foldoutArrowDown == null)
                {
                    _foldoutArrowDown =
                        EditorGUIUtility.IconContent("icon dropdown@2x").image as Texture2D;
                }

                if (_foldoutArrowDown == null)
                {
                    _foldoutArrowDown =
                        EditorGUIUtility.IconContent("icon dropdown").image as Texture2D;
                }
            }

            if (_foldoutArrowRight == null && _foldoutArrowDown == null)
            {
                return EditorGUI.Foldout(position, isExpanded, content, toggleOnLabelClick);
            }

            float arrowSize = EditorGUIUtility.singleLineHeight;
            Rect arrowRect = new Rect(position.x, position.y, arrowSize, arrowSize);
            Rect labelRect = new Rect(
                position.x + arrowSize,
                position.y,
                position.width - arrowSize,
                position.height
            );

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (arrowRect.Contains(evt.mousePosition))
                {
                    isExpanded = !isExpanded;
                    evt.Use();
                    GUI.changed = true;
                }
                else if (toggleOnLabelClick && labelRect.Contains(evt.mousePosition))
                {
                    isExpanded = !isExpanded;
                    evt.Use();
                    GUI.changed = true;
                }
            }

            Texture2D arrowTexture = isExpanded ? _foldoutArrowDown : _foldoutArrowRight;
            if (arrowTexture != null)
            {
                float iconSize = Mathf.Min(arrowRect.width, arrowRect.height) * 0.7f;
                Rect iconRect = new Rect(
                    arrowRect.x + (arrowRect.width - iconSize) * 0.5f,
                    arrowRect.y + (arrowRect.height - iconSize) * 0.5f,
                    iconSize,
                    iconSize
                );

                // GUI.DrawTexture respects GUI.contentColor for tinting
                GUI.DrawTexture(iconRect, arrowTexture, ScaleMode.ScaleToFit);
            }

            EditorGUI.LabelField(labelRect, content);

            return isExpanded;
        }

        private static StyleState SaveFullStyleState(GUIStyle style)
        {
            if (style == null)
            {
                return default;
            }

            return new StyleState
            {
                NormalBackground = style.normal.background,
                FocusedBackground = style.focused.background,
                ActiveBackground = style.active.background,
                HoverBackground = style.hover.background,
                OnNormalBackground = style.onNormal.background,
                OnFocusedBackground = style.onFocused.background,
                OnActiveBackground = style.onActive.background,
                OnHoverBackground = style.onHover.background,
                NormalTextColor = style.normal.textColor,
                FocusedTextColor = style.focused.textColor,
                ActiveTextColor = style.active.textColor,
                HoverTextColor = style.hover.textColor,
                OnNormalTextColor = style.onNormal.textColor,
                OnFocusedTextColor = style.onFocused.textColor,
                OnActiveTextColor = style.onActive.textColor,
                OnHoverTextColor = style.onHover.textColor,
                IsValid = true,
            };
        }

        private static void RestoreFullStyleState(GUIStyle style, StyleState saved)
        {
            if (style == null || !saved.IsValid)
            {
                return;
            }

            style.normal.background = saved.NormalBackground;
            style.focused.background = saved.FocusedBackground;
            style.active.background = saved.ActiveBackground;
            style.hover.background = saved.HoverBackground;
            style.onNormal.background = saved.OnNormalBackground;
            style.onFocused.background = saved.OnFocusedBackground;
            style.onActive.background = saved.OnActiveBackground;
            style.onHover.background = saved.OnHoverBackground;
            style.normal.textColor = saved.NormalTextColor;
            style.focused.textColor = saved.FocusedTextColor;
            style.active.textColor = saved.ActiveTextColor;
            style.hover.textColor = saved.HoverTextColor;
            style.onNormal.textColor = saved.OnNormalTextColor;
            style.onFocused.textColor = saved.OnFocusedTextColor;
            style.onActive.textColor = saved.OnActiveTextColor;
            style.onHover.textColor = saved.OnHoverTextColor;
        }

        internal static IDisposable PushContentPadding(float horizontalPadding)
        {
            return new WidthPaddingScope(horizontalPadding);
        }

        internal static IDisposable PushContentPadding(
            float horizontalPadding,
            float leftPadding,
            float rightPadding
        )
        {
            return new WidthPaddingScope(horizontalPadding, leftPadding, rightPadding);
        }

        internal static Rect ApplyCurrentPadding(Rect rect)
        {
            float leftPadding = _totalLeftPadding;
            float rightPadding = _totalRightPadding;
            if (leftPadding <= 0f && rightPadding <= 0f)
            {
                return rect;
            }

            Rect adjusted = rect;
            adjusted.xMin += leftPadding;
            adjusted.xMax -= rightPadding;
            if (adjusted.width < 0f || float.IsNaN(adjusted.width))
            {
                adjusted.width = 0f;
            }

            return adjusted;
        }

        internal static float CalculateHorizontalPadding(GUIStyle containerStyle)
        {
            return CalculateHorizontalPadding(containerStyle, out _, out _);
        }

        internal static float CalculateHorizontalPadding(
            GUIStyle containerStyle,
            out float leftPadding,
            out float rightPadding
        )
        {
            if (containerStyle == null)
            {
                leftPadding = 0f;
                rightPadding = 0f;
                return 0f;
            }

            RectOffset padding = containerStyle.padding;
            if (padding == null)
            {
                leftPadding = 0f;
                rightPadding = 0f;
                return 0f;
            }

            int total = padding.left + padding.right;
            leftPadding = Mathf.Max(0f, padding.left);
            rightPadding = Mathf.Max(0f, padding.right);
            return Mathf.Max(0f, total);
        }

        private sealed class WidthPaddingScope : IDisposable
        {
            private readonly float _padding;
            private readonly float _leftPadding;
            private readonly float _rightPadding;
            private readonly bool _trackScopeDepth;
            private bool _disposed;

            internal WidthPaddingScope(float horizontalPadding)
            {
                float resolved = Mathf.Max(0f, horizontalPadding);
                float split = resolved * 0.5f;
                _padding = resolved;
                _leftPadding = split;
                _rightPadding = resolved - split;

                // Zero padding should not increase scope depth as it has no visual effect
                _trackScopeDepth = 0f < _padding;

                if (_trackScopeDepth)
                {
                    _scopeDepth++;
                    _totalPadding += _padding;
                    _totalLeftPadding += _leftPadding;
                    _totalRightPadding += _rightPadding;
                }
            }

            internal WidthPaddingScope(
                float horizontalPadding,
                float leftPadding,
                float rightPadding
            )
            {
                _leftPadding = Mathf.Max(0f, leftPadding);
                _rightPadding = Mathf.Max(0f, rightPadding);
                float combined = Mathf.Max(0f, horizontalPadding);
                if (combined <= 0f)
                {
                    combined = _leftPadding + _rightPadding;
                }

                // Zero padding should not increase scope depth as it has no visual effect
                if (combined <= 0f)
                {
                    _padding = 0f;
                    _leftPadding = 0f;
                    _rightPadding = 0f;
                    _trackScopeDepth = false;
                    return;
                }

                _padding = combined;
                float resolvedLeft = _leftPadding;
                float resolvedRight = _rightPadding;
                if (resolvedLeft <= 0f && resolvedRight <= 0f)
                {
                    float split = combined * 0.5f;
                    resolvedLeft = split;
                    resolvedRight = combined - split;
                }

                _leftPadding = resolvedLeft;
                _rightPadding = resolvedRight;
                _trackScopeDepth = true;

                _scopeDepth++;
                _totalPadding += _padding;
                _totalLeftPadding += _leftPadding;
                _totalRightPadding += _rightPadding;
            }

            public void Dispose()
            {
                if (_disposed || !_trackScopeDepth)
                {
                    return;
                }

                _disposed = true;

                _totalPadding = Mathf.Max(0f, _totalPadding - _padding);
                _totalLeftPadding = Mathf.Max(0f, _totalLeftPadding - _leftPadding);
                _totalRightPadding = Mathf.Max(0f, _totalRightPadding - _rightPadding);
                _scopeDepth = Mathf.Max(0, _scopeDepth - 1);
            }
        }

        /// <summary>
        /// Stores saved WGroup GUI colors for temporary restoration.
        /// </summary>
        internal struct WGroupSavedColors
        {
            public Color ContentColor;
            public Color Color;
            public Color BackgroundColor;
        }

        /// <summary>
        /// Saved state for a GUIStyle, used to restore EditorStyles after exiting WGroup theming.
        /// </summary>
        private struct StyleState
        {
            public Texture2D NormalBackground;
            public Texture2D FocusedBackground;
            public Texture2D ActiveBackground;
            public Texture2D HoverBackground;
            public Texture2D OnNormalBackground;
            public Texture2D OnFocusedBackground;
            public Texture2D OnActiveBackground;
            public Texture2D OnHoverBackground;
            public Color NormalTextColor;
            public Color FocusedTextColor;
            public Color ActiveTextColor;
            public Color HoverTextColor;
            public Color OnNormalTextColor;
            public Color OnFocusedTextColor;
            public Color OnActiveTextColor;
            public Color OnHoverTextColor;
            public bool IsValid;
        }

        internal readonly struct WGroupThemeState
        {
            public readonly Color GuiColor;
            public readonly Color ContentColor;
            public readonly Color BackgroundColor;

            internal WGroupThemeState(Color guiColor, Color contentColor, Color backgroundColor)
            {
                GuiColor = guiColor;
                ContentColor = contentColor;
                BackgroundColor = backgroundColor;
            }
        }

        private sealed class ExitWGroupThemingScope : IDisposable
        {
            private readonly bool _previousIsInsideWGroupPropertyDraw;
            private readonly Color _previousContentColor;
            private readonly Color _previousColor;
            private readonly Color _previousBackgroundColor;
            private readonly WGroupThemeState? _previousThemeState;

            // Saved EditorStyles states - WGroupColorScope modifies these globally
            private readonly StyleState _savedTextField;
            private readonly StyleState _savedNumberField;
            private readonly StyleState _savedObjectField;
            private readonly StyleState _savedPopup;
            private readonly StyleState _savedHelpBox;
            private readonly StyleState _savedFoldout;
            private readonly StyleState _savedLabel;
            private readonly StyleState _savedToggle;
            private readonly StyleState _savedMiniButton;
            private readonly StyleState _savedMiniButtonLeft;
            private readonly StyleState _savedMiniButtonMid;
            private readonly StyleState _savedMiniButtonRight;

            private bool _disposed;

            internal ExitWGroupThemingScope()
            {
                _previousIsInsideWGroupPropertyDraw = _isInsideWGroupPropertyDraw;
                _previousContentColor = GUI.contentColor;
                _previousColor = GUI.color;
                _previousBackgroundColor = GUI.backgroundColor;
                _previousThemeState = _currentThemeState;

                _savedTextField = SaveFullStyleState(EditorStyles.textField);
                _savedNumberField = SaveFullStyleState(EditorStyles.numberField);
                _savedObjectField = SaveFullStyleState(EditorStyles.objectField);
                _savedPopup = SaveFullStyleState(EditorStyles.popup);
                _savedHelpBox = SaveFullStyleState(EditorStyles.helpBox);
                _savedFoldout = SaveFullStyleState(EditorStyles.foldout);
                _savedLabel = SaveFullStyleState(EditorStyles.label);
                _savedToggle = SaveFullStyleState(EditorStyles.toggle);
                _savedMiniButton = SaveFullStyleState(EditorStyles.miniButton);
                _savedMiniButtonLeft = SaveFullStyleState(EditorStyles.miniButtonLeft);
                _savedMiniButtonMid = SaveFullStyleState(EditorStyles.miniButtonMid);
                _savedMiniButtonRight = SaveFullStyleState(EditorStyles.miniButtonRight);

                _currentThemeState = null;
                _isInsideWGroupPropertyDraw = false;

                Color skinTextColor = EditorGUIUtility.isProSkin
                    ? new Color(0.82f, 0.82f, 0.82f, 1f)
                    : new Color(0.09f, 0.09f, 0.09f, 1f);
                GUI.contentColor = skinTextColor;
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;

                // WGroupColorScope modifies backgrounds on EditorStyles.
                ResetStyleToSkinDefaults(EditorStyles.textField);
                ResetStyleToSkinDefaults(EditorStyles.numberField);
                ResetStyleToSkinDefaults(EditorStyles.objectField);
                ResetStyleToSkinDefaults(EditorStyles.popup);
                ResetStyleToSkinDefaults(EditorStyles.helpBox);
                ResetStyleToSkinDefaults(EditorStyles.foldout);
                ResetStyleToSkinDefaults(EditorStyles.label);
                ResetStyleToSkinDefaults(EditorStyles.boldLabel);
                ResetStyleToSkinDefaults(EditorStyles.toggle);
                ResetStyleToSkinDefaults(EditorStyles.miniButton);
                ResetStyleToSkinDefaults(EditorStyles.miniButtonLeft);
                ResetStyleToSkinDefaults(EditorStyles.miniButtonMid);
                ResetStyleToSkinDefaults(EditorStyles.miniButtonRight);
                ResetStyleToSkinDefaults(EditorStyles.miniLabel);

                skinTextColor = EditorGUIUtility.isProSkin
                    ? new Color(0.82f, 0.82f, 0.82f, 1f)
                    : new Color(0.09f, 0.09f, 0.09f, 1f);

                SetStyleTextColor(EditorStyles.label, skinTextColor);
                SetStyleTextColor(EditorStyles.boldLabel, skinTextColor);
                SetStyleTextColor(EditorStyles.toggle, skinTextColor);
                SetStyleTextColor(EditorStyles.foldout, skinTextColor);
                SetStyleTextColor(EditorStyles.miniLabel, skinTextColor);

                SetStyleTextColor(EditorStyles.textField, skinTextColor);
                SetStyleTextColor(EditorStyles.numberField, skinTextColor);
                SetStyleTextColor(EditorStyles.miniButton, skinTextColor);
                SetStyleTextColor(EditorStyles.miniButtonLeft, skinTextColor);
                SetStyleTextColor(EditorStyles.miniButtonMid, skinTextColor);
                SetStyleTextColor(EditorStyles.miniButtonRight, skinTextColor);
            }

            private static void SetStyleTextColor(GUIStyle style, Color color)
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

            private static void ResetStyleBackgrounds(GUIStyle style)
            {
                if (style == null)
                {
                    return;
                }

                style.normal.background = null;
                style.focused.background = null;
                style.active.background = null;
                style.hover.background = null;
                style.onNormal.background = null;
                style.onFocused.background = null;
                style.onActive.background = null;
                style.onHover.background = null;
            }

            private static void ResetStyleToSkinDefaults(GUIStyle style)
            {
                if (style == null)
                {
                    return;
                }

                style.normal.background = null;
                style.focused.background = null;
                style.active.background = null;
                style.hover.background = null;
                style.onNormal.background = null;
                style.onFocused.background = null;
                style.onActive.background = null;
                style.onHover.background = null;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                RestoreFullStyleState(EditorStyles.textField, _savedTextField);
                RestoreFullStyleState(EditorStyles.numberField, _savedNumberField);
                RestoreFullStyleState(EditorStyles.objectField, _savedObjectField);
                RestoreFullStyleState(EditorStyles.popup, _savedPopup);
                RestoreFullStyleState(EditorStyles.helpBox, _savedHelpBox);
                RestoreFullStyleState(EditorStyles.foldout, _savedFoldout);
                RestoreFullStyleState(EditorStyles.label, _savedLabel);
                RestoreFullStyleState(EditorStyles.toggle, _savedToggle);
                RestoreFullStyleState(EditorStyles.miniButton, _savedMiniButton);
                RestoreFullStyleState(EditorStyles.miniButtonLeft, _savedMiniButtonLeft);
                RestoreFullStyleState(EditorStyles.miniButtonMid, _savedMiniButtonMid);
                RestoreFullStyleState(EditorStyles.miniButtonRight, _savedMiniButtonRight);

                _currentThemeState = _previousThemeState;
                _isInsideWGroupPropertyDraw = _previousIsInsideWGroupPropertyDraw;

                GUI.contentColor = _previousContentColor;
                GUI.color = _previousColor;
                GUI.backgroundColor = _previousBackgroundColor;
            }
        }

        /// <summary>
        /// Temporarily restores WGroup GUI colors within an ExitWGroupTheming scope.
        /// Used for drawing elements like foldout labels that need WGroup theming
        /// because they render against the WGroup background.
        /// </summary>
        private sealed class RestoreWGroupThemingScope : IDisposable
        {
            private readonly Color _savedContentColor;
            private readonly Color _savedColor;
            private readonly Color _savedBackgroundColor;

            // Saved foldout style text colors - the foldout icon uses these
            private readonly Color _savedFoldoutNormalTextColor;
            private readonly Color _savedFoldoutOnNormalTextColor;
            private readonly Color _savedFoldoutHoverTextColor;
            private readonly Color _savedFoldoutOnHoverTextColor;
            private readonly Color _savedFoldoutActiveTextColor;
            private readonly Color _savedFoldoutOnActiveTextColor;
            private readonly Color _savedFoldoutFocusedTextColor;
            private readonly Color _savedFoldoutOnFocusedTextColor;

            private bool _disposed;

            internal RestoreWGroupThemingScope(WGroupSavedColors colors)
            {
                _savedContentColor = GUI.contentColor;
                _savedColor = GUI.color;
                _savedBackgroundColor = GUI.backgroundColor;

                GUIStyle foldout = EditorStyles.foldout;
                _savedFoldoutNormalTextColor = foldout.normal.textColor;
                _savedFoldoutOnNormalTextColor = foldout.onNormal.textColor;
                _savedFoldoutHoverTextColor = foldout.hover.textColor;
                _savedFoldoutOnHoverTextColor = foldout.onHover.textColor;
                _savedFoldoutActiveTextColor = foldout.active.textColor;
                _savedFoldoutOnActiveTextColor = foldout.onActive.textColor;
                _savedFoldoutFocusedTextColor = foldout.focused.textColor;
                _savedFoldoutOnFocusedTextColor = foldout.onFocused.textColor;

                WGroupThemingDiagnostics.LogRestoreTheming(
                    "ENTER (saving non-themed)",
                    _savedContentColor,
                    _savedColor,
                    _savedBackgroundColor
                );

                GUI.contentColor = colors.ContentColor;
                GUI.color = colors.Color;
                GUI.backgroundColor = colors.BackgroundColor;

                Color textColor = colors.ContentColor;
                foldout.normal.textColor = textColor;
                foldout.onNormal.textColor = textColor;
                foldout.hover.textColor = textColor;
                foldout.onHover.textColor = textColor;
                foldout.active.textColor = textColor;
                foldout.onActive.textColor = textColor;
                foldout.focused.textColor = textColor;
                foldout.onFocused.textColor = textColor;

                WGroupThemingDiagnostics.LogFoldoutStyleColors("after set", foldout);

                WGroupThemingDiagnostics.LogRestoreTheming(
                    "ENTER (restored WGroup colors)",
                    GUI.contentColor,
                    GUI.color,
                    GUI.backgroundColor
                );
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                WGroupThemingDiagnostics.LogRestoreTheming(
                    "EXIT (before restore)",
                    GUI.contentColor,
                    GUI.color,
                    GUI.backgroundColor
                );

                GUI.contentColor = _savedContentColor;
                GUI.color = _savedColor;
                GUI.backgroundColor = _savedBackgroundColor;

                GUIStyle foldout = EditorStyles.foldout;
                foldout.normal.textColor = _savedFoldoutNormalTextColor;
                foldout.onNormal.textColor = _savedFoldoutOnNormalTextColor;
                foldout.hover.textColor = _savedFoldoutHoverTextColor;
                foldout.onHover.textColor = _savedFoldoutOnHoverTextColor;
                foldout.active.textColor = _savedFoldoutActiveTextColor;
                foldout.onActive.textColor = _savedFoldoutOnActiveTextColor;
                foldout.focused.textColor = _savedFoldoutFocusedTextColor;
                foldout.onFocused.textColor = _savedFoldoutOnFocusedTextColor;

                WGroupThemingDiagnostics.LogRestoreTheming(
                    "EXIT (after restore)",
                    GUI.contentColor,
                    GUI.color,
                    GUI.backgroundColor
                );
            }
        }

        private sealed class WGroupPropertyContextScope : IDisposable
        {
            private readonly bool _previousValue;
            private bool _disposed;

            internal WGroupPropertyContextScope()
            {
                _previousValue = _isInsideWGroupPropertyDraw;
                _isInsideWGroupPropertyDraw = true;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _isInsideWGroupPropertyDraw = _previousValue;
            }
        }
    }
#endif
}
