// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class WButtonStyles
    {
        private static GUIStyle _groupStyle;
        private static GUIStyle _headerStyle;
        private static GUIStyle _baseButtonStyle;
        private static GUIStyle _baseMiniButtonStyle;
        private static GUIStyle _arrayHeaderStyle;
        private static GUIStyle _foldoutContainerExpanded;
        private static GUIStyle _foldoutContainerCollapsed;
        private static GUIStyle _foldoutHeaderStyle;
        private static GUIContent _topHeaderContent;
        private static GUIContent _bottomHeaderContent;
        private static readonly EditorCacheHelper.ColorComparer ColorEquality = new();

        /// <remarks>
        /// A key is a settings-authored colour pair, so a colour picker drag mints one entry per
        /// intermediate colour. Each entry owns three 1x1 textures created with
        /// <see cref="HideFlags.HideAndDontSave"/>, which nothing else releases -- so the bound has
        /// to destroy them, and a style may only be dropped together with the textures its
        /// <see cref="GUIStyle.normal"/> and siblings point at. Owning them per entry is what makes
        /// that atomic: no two entries share a texture, so evicting one can never blank another.
        /// The bound is far above the handful of palette entries a project authors, so an eviction
        /// only ever discards a colour the user dragged through.
        /// </remarks>
        private const int MaxColoredButtonStyles = 64;

        private static readonly Cache<ButtonStyleKey, ColoredButtonStyle> ColoredButtonStyles =
            CacheBuilder<ButtonStyleKey, ColoredButtonStyle>
                .NewBuilder()
                .MaximumSize(MaxColoredButtonStyles)
                .InitialCapacity(16)
                .KeyComparer(new ButtonStyleKeyComparer())
                .OnEviction(static (_, evicted, _) => evicted.Destroy())
                .TransferOwnershipOnRemoval()
                .Build();
        private static readonly Cache<ButtonStyleKey, ColoredButtonStyle> ColoredMiniButtonStyles =
            CacheBuilder<ButtonStyleKey, ColoredButtonStyle>
                .NewBuilder()
                .MaximumSize(MaxColoredButtonStyles)
                .InitialCapacity(16)
                .KeyComparer(new ButtonStyleKeyComparer())
                .OnEviction(static (_, evicted, _) => evicted.Destroy())
                .TransferOwnershipOnRemoval()
                .Build();

        internal const float ButtonHeight = 18f;

        internal static GUIStyle GroupStyle
        {
            get
            {
                _groupStyle ??= new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(1, 1, 0, 0),
                };
                return _groupStyle;
            }
        }

        internal static GUIStyle HeaderStyle
        {
            get
            {
                _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                };
                return _headerStyle;
            }
        }

        private static GUIStyle ButtonStyle
        {
            get
            {
                _baseButtonStyle ??= new GUIStyle(GUI.skin.button)
                {
                    wordWrap = true,
                    fontSize = 11,
                    richText = false,
                    fixedHeight = ButtonHeight,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(6, 6, 3, 4),
                    margin = new RectOffset(1, 1, 1, 3),
                };
                return _baseButtonStyle;
            }
        }

        private static GUIStyle MiniButtonStyle
        {
            get
            {
                _baseMiniButtonStyle ??= new GUIStyle(EditorStyles.miniButton)
                {
                    wordWrap = false,
                    fontSize = 10,
                    richText = false,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(4, 4, 2, 2),
                    margin = new RectOffset(1, 1, 1, 1),
                };
                return _baseMiniButtonStyle;
            }
        }

        internal static GUIStyle ArrayHeaderStyle
        {
            get
            {
                _arrayHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 10 };
                return _arrayHeaderStyle;
            }
        }

        internal static GUIStyle GetFoldoutContainerStyle(bool expanded)
        {
            _foldoutContainerExpanded ??= CreateFoldoutContainerStyle(expanded: true);
            _foldoutContainerCollapsed ??= CreateFoldoutContainerStyle(expanded: false);
            return expanded ? _foldoutContainerExpanded : _foldoutContainerCollapsed;
        }

        internal static GUIStyle FoldoutHeaderStyle
        {
            get
            {
                _foldoutHeaderStyle ??= new GUIStyle(EditorStyles.foldoutHeader)
                {
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(18, 4, 2, 2),
                    margin = new RectOffset(0, 1, 2, 1),
                };
                return _foldoutHeaderStyle;
            }
        }

        internal static Color GetFoldoutBackgroundColor(bool expanded)
        {
            if (EditorGUIUtility.isProSkin)
            {
                return expanded
                    ? new Color(0.23f, 0.23f, 0.25f, 1f)
                    : new Color(0.19f, 0.19f, 0.2f, 1f);
            }

            return expanded
                ? new Color(0.89f, 0.92f, 0.97f, 1f)
                : new Color(0.94f, 0.94f, 0.96f, 1f);
        }

        internal const float FoldoutContentSpacing = 4f;
        internal const float FoldoutIconOffset = 4f;

        internal static GUIContent TopGroupLabel
        {
            get
            {
                _topHeaderContent ??= new GUIContent("Actions");
                return _topHeaderContent;
            }
        }

        internal static GUIContent BottomGroupLabel
        {
            get
            {
                _bottomHeaderContent ??= new GUIContent("Additional Actions");
                return _bottomHeaderContent;
            }
        }

        internal static GUIStyle GetColoredButtonStyle(Color buttonColor, Color textColor)
        {
            GUIStyle baseStyle = ButtonStyle;
            ButtonStyleKey key = new(buttonColor, textColor);
            if (ColoredButtonStyles.TryGet(key, out ColoredButtonStyle cached))
            {
                return cached.style;
            }

            GUIStyle style = new(baseStyle)
            {
                fixedHeight = ButtonHeight,
                normal = { textColor = textColor },
                focused = { textColor = textColor },
                active = { textColor = textColor },
                hover = { textColor = textColor },
                onNormal = { textColor = textColor },
                onFocused = { textColor = textColor },
                onActive = { textColor = textColor },
                onHover = { textColor = textColor },
            };

            Texture2D normal = CreateSolidTexture(buttonColor);
            Texture2D hover = CreateSolidTexture(WButtonColorUtility.GetHoverColor(buttonColor));
            Texture2D active = CreateSolidTexture(WButtonColorUtility.GetActiveColor(buttonColor));

            style.normal.background = normal;
            style.focused.background = normal;
            style.onNormal.background = normal;
            style.onFocused.background = normal;

            style.hover.background = hover;
            style.onHover.background = hover;

            style.active.background = active;
            style.onActive.background = active;

            ColoredButtonStyles.Set(key, new ColoredButtonStyle(style, normal, hover, active));
            return style;
        }

        internal static GUIStyle GetColoredMiniButtonStyle(Color buttonColor, Color textColor)
        {
            GUIStyle baseStyle = MiniButtonStyle;
            ButtonStyleKey key = new(buttonColor, textColor);
            if (ColoredMiniButtonStyles.TryGet(key, out ColoredButtonStyle cached))
            {
                return cached.style;
            }

            GUIStyle style = new(baseStyle)
            {
                normal = { textColor = textColor },
                focused = { textColor = textColor },
                active = { textColor = textColor },
                hover = { textColor = textColor },
                onNormal = { textColor = textColor },
                onFocused = { textColor = textColor },
                onActive = { textColor = textColor },
                onHover = { textColor = textColor },
            };

            Texture2D normal = CreateSolidTexture(buttonColor);
            Texture2D hover = CreateSolidTexture(WButtonColorUtility.GetHoverColor(buttonColor));
            Texture2D active = CreateSolidTexture(WButtonColorUtility.GetActiveColor(buttonColor));

            style.normal.background = normal;
            style.focused.background = normal;
            style.onNormal.background = normal;
            style.onFocused.background = normal;

            style.hover.background = hover;
            style.onHover.background = hover;

            style.active.background = active;
            style.onActive.background = active;

            ColoredMiniButtonStyles.Set(key, new ColoredButtonStyle(style, normal, hover, active));
            return style;
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            Texture2D texture = new(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static GUIStyle CreateFoldoutContainerStyle(bool expanded)
        {
            RectOffset padding = expanded ? new RectOffset(4, 4, 4, 4) : new RectOffset(4, 4, 4, 3);
            GUIStyle style = new(EditorStyles.helpBox)
            {
                padding = padding,
                margin = new RectOffset(1, 1, 0, 0),
            };
            return style;
        }

        private readonly struct ButtonStyleKey : System.IEquatable<ButtonStyleKey>
        {
            internal ButtonStyleKey(Color buttonColor, Color textColor)
            {
                ButtonColor = buttonColor;
                TextColor = textColor;
            }

            private Color ButtonColor { get; }

            private Color TextColor { get; }

            public bool Equals(ButtonStyleKey other)
            {
                return ColorEquality.Equals(ButtonColor, other.ButtonColor)
                    && ColorEquality.Equals(TextColor, other.TextColor);
            }

            public override bool Equals(object obj)
            {
                return obj is ButtonStyleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Objects.HashCode(
                    EditorCacheHelper.GetColorHashCode(ButtonColor),
                    EditorCacheHelper.GetColorHashCode(TextColor)
                );
            }
        }

        private sealed class ButtonStyleKeyComparer : IEqualityComparer<ButtonStyleKey>
        {
            public bool Equals(ButtonStyleKey x, ButtonStyleKey y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(ButtonStyleKey obj)
            {
                return obj.GetHashCode();
            }
        }

        private sealed class ColoredButtonStyle
        {
            internal readonly GUIStyle style;

            private readonly Texture2D _normal;
            private readonly Texture2D _hover;
            private readonly Texture2D _active;

            internal ColoredButtonStyle(
                GUIStyle style,
                Texture2D normal,
                Texture2D hover,
                Texture2D active
            )
            {
                this.style = style;
                _normal = normal;
                _hover = hover;
                _active = active;
            }

            internal void Destroy()
            {
                DestroyTexture(_normal);
                DestroyTexture(_hover);
                DestroyTexture(_active);
            }

            private static void DestroyTexture(Texture2D texture)
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        internal static class TestHooks
        {
            /// <summary>
            /// Gets the number of colored button styles currently retained, for testing.
            /// </summary>
            internal static int ColoredButtonStyleCount => ColoredButtonStyles.Count;

            /// <summary>
            /// Gets the number of colored mini button styles currently retained, for testing.
            /// </summary>
            internal static int ColoredMiniButtonStyleCount => ColoredMiniButtonStyles.Count;

            /// <summary>
            /// Gets the bound both colored style caches evict at.
            /// </summary>
            internal static int MaxColoredButtonStyleCount => MaxColoredButtonStyles;

            /// <summary>
            /// Drops every cached colored style, destroying the textures each owns.
            /// </summary>
            internal static void ClearColoredStyleCaches()
            {
                ColoredButtonStyles.Clear();
                ColoredMiniButtonStyles.Clear();
            }
        }
    }
#endif
}
