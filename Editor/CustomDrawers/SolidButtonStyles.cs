// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Shared solid-color button styling used by the serialized dictionary and set inspectors.
    /// Keeps the palette consistent between drawers (add/overwrite/remove/reset, etc).
    /// </summary>
    internal static class SolidButtonStyles
    {
        private static readonly Color ThemeRemoveColor = new(0.92f, 0.29f, 0.33f, 1f);
        private static readonly Color ThemeAddColor = new(0.25f, 0.68f, 0.38f, 1f);
        private static readonly Color ThemeOverwriteColor = new(0.98f, 0.82f, 0.27f, 1f);
        private static readonly Color ThemeResetColor = new(0.7f, 0.7f, 0.7f, 1f);
        private static readonly Color ThemeSortColor = new(0.24f, 0.52f, 0.88f, 1f);
        private static readonly Color ThemeDisabledColor = new(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Dictionary<
            (string action, bool enabled),
            GUIStyle
        > ButtonStyleCache = new();
        private static readonly Dictionary<Color, Texture2D> ColorTextureCache = new();
        private static readonly RectOffset ZeroMargin = new(0, 0, 0, 0);
        private static readonly RectOffset StandardPadding = new(8, 8, 3, 3);

        internal static Color DisabledColor => ThemeDisabledColor;

        internal static GUIStyle GetSolidButtonStyle(string action, bool enabled)
        {
            if (string.IsNullOrEmpty(action))
            {
                action = "Default";
            }

            (string, bool) cacheKey = (action, enabled);
            if (ButtonStyleCache.TryGetValue(cacheKey, out GUIStyle cached))
            {
                return cached;
            }

            GUIStyle baseStyle = new(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                margin = ZeroMargin,
                padding = StandardPadding,
            };

            Color baseColor = enabled ? GetActionColor(action) : ThemeDisabledColor;
            Color hoverColor = enabled ? AdjustValue(baseColor, 0.08f) : baseColor;
            Color pressedColor = enabled ? AdjustValue(baseColor, -0.08f) : baseColor;
            Color textColor = enabled
                ? GetLegibleTextColor(baseColor)
                : AdjustAlpha(GetLegibleTextColor(baseColor), 0.6f);

            Texture2D normalTexture = GetSolidTexture(baseColor);
            Texture2D hoverTexture = GetSolidTexture(hoverColor);
            Texture2D pressedTexture = GetSolidTexture(pressedColor);

            baseStyle.normal.background = normalTexture;
            baseStyle.hover.background = hoverTexture;
            baseStyle.active.background = pressedTexture;
            baseStyle.focused.background = normalTexture;
            baseStyle.onNormal.background = normalTexture;
            baseStyle.onHover.background = hoverTexture;
            baseStyle.onActive.background = pressedTexture;
            baseStyle.onFocused.background = normalTexture;

            baseStyle.normal.textColor = textColor;
            baseStyle.hover.textColor = textColor;
            baseStyle.active.textColor = textColor;
            baseStyle.focused.textColor = textColor;
            baseStyle.onNormal.textColor = textColor;
            baseStyle.onHover.textColor = textColor;
            baseStyle.onActive.textColor = textColor;
            baseStyle.onFocused.textColor = textColor;

            ButtonStyleCache[cacheKey] = baseStyle;
            return baseStyle;
        }

        private static Color GetActionColor(string action)
        {
            switch (action)
            {
                case "Add":
                    return ThemeAddColor;
                case "Overwrite":
                case "AddEmpty":
                    return ThemeOverwriteColor;
                case "Reset":
                    return ThemeResetColor;
                case "Remove":
                case "ClearAll":
                    return ThemeRemoveColor;
                case "Sort":
                    return ThemeSortColor;
                default:
                    return ThemeResetColor;
            }
        }

        private static Color AdjustValue(Color color, float delta)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            v = Mathf.Clamp01(v + delta);
            Color result = Color.HSVToRGB(h, s, v);
            result.a = color.a;
            return result;
        }

        private static Color AdjustAlpha(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        private static Color GetLegibleTextColor(Color background)
        {
            return ColorContrast.ReadableTextColor(background);
        }

        private static Texture2D GetSolidTexture(Color color)
        {
            if (ColorTextureCache.TryGetValue(color, out Texture2D cached))
            {
                return cached;
            }

            Texture2D texture = new(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();

            ColorTextureCache[color] = texture;
            return texture;
        }
    }
}
