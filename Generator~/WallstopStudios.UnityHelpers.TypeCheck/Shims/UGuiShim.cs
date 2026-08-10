// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the one uGUI type the serialization path needs.
//
// UnityEngine.UI ships as a Unity PACKAGE (com.unity.ugui), not part of the UnityEngine.Modules
// reference assemblies, and has no NuGet equivalent. Only ColorBlock is shimmed, because it is the
// only uGUI type reachable from Serializer -- the components (Image, Slider) are used by UI
// utilities with no serialization role, and those files are excluded instead of shimmed.
//
// Mirrors the real struct's public shape exactly. Getting a member wrong here would let a genuine
// error through, so this is deliberately the smallest surface that compiles the converter.
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
