// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * uGUI is absent from the engine references. This signature-only ColorBlock shim covers serialization; UI

 * component sources are excluded.

 */
namespace UnityEngine.UI
{
#pragma warning disable WUH023 // External reference-API shim, not a package value type.
    public struct ColorBlock
    {
        public static ColorBlock defaultColorBlock => default;

        public Color normalColor;
        public Color highlightedColor;
        public Color pressedColor;
        public Color selectedColor;
        public Color disabledColor;
        public float colorMultiplier;
        public float fadeDuration;
    }
#pragma warning restore WUH023
}
