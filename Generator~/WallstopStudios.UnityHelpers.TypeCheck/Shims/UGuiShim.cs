// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * uGUI is absent from the engine references. This signature-only ColorBlock shim covers serialization; UI

 * component sources are excluded.

 */
namespace UnityEngine.UI
{
    public struct ColorBlock
    {
        public Color normalColor;
        public Color highlightedColor;
        public Color pressedColor;
        public Color selectedColor;
        public Color disabledColor;
        public float colorMultiplier;
        public float fadeDuration;

        public static ColorBlock defaultColorBlock => default;
    }
}
