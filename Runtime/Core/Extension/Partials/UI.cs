// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    public static partial class UnityExtensions
    {
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static Func<float> ScreenDpiProvider
        {
            get => _screenDpiProvider ?? DefaultScreenDpiProvider;
            set => _screenDpiProvider = value;
        }

        private static readonly Func<float> DefaultScreenDpiProvider = () => Screen.dpi;
        private static Func<float> _screenDpiProvider;
#endif

        /// <summary>
        /// Sets all color states of a UI Slider to the same color.
        /// </summary>
        public static void SetColors(this Slider slider, Color color)
        {
            ColorBlock block = slider.colors;
            block.normalColor = color;
            block.highlightedColor = color;
            block.pressedColor = color;
            block.selectedColor = color;
            block.disabledColor = color;
            slider.colors = block;
        }

        /// <summary>
        /// Sets the left offset of a RectTransform.
        /// </summary>
        public static void SetLeft(this RectTransform rt, float left)
        {
            rt.offsetMin = new Vector2(left, rt.offsetMin.y);
        }

        /// <summary>
        /// Sets the right offset of a RectTransform.
        /// </summary>
        public static void SetRight(this RectTransform rt, float right)
        {
            rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
        }

        /// <summary>
        /// Sets the top offset of a RectTransform.
        /// </summary>
        public static void SetTop(this RectTransform rt, float top)
        {
            rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
        }

        /// <summary>
        /// Sets the bottom offset of a RectTransform.
        /// </summary>
        public static void SetBottom(this RectTransform rt, float bottom)
        {
            rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
        }

        /// <summary>Copies a rectangle's local bounds to a 2D box collider.</summary>
        /// <param name="rectangle">The rectangle whose local bounds are copied.</param>
        /// <param name="collider">The collider on the rectangle's GameObject to update.</param>
        /// <returns>
        /// <see langword="true" /> when both Unity objects are valid, share a transform, and the
        /// rectangle has finite, non-negative bounds. Failure leaves the collider unchanged.
        /// </returns>
        public static bool TrySyncBoxCollider2D(
            this RectTransform rectangle,
            BoxCollider2D collider
        )
        {
            if (rectangle == null || collider == null || collider.transform != rectangle.transform)
            {
                return false;
            }

            Rect rect = rectangle.rect;
            if (
                !IsFinite(rect.center)
                || !IsFinite(rect.size)
                || !(0f <= rect.size.x)
                || !(0f <= rect.size.y)
            )
            {
                return false;
            }

            collider.offset = rect.center;
            collider.size = rect.size;
            return true;
        }

        /// <summary>Scales a UI drag threshold for a display's pixel density.</summary>
        /// <param name="baseThreshold">The threshold at the reference density.</param>
        /// <param name="dpi">The display density in dots per inch.</param>
        /// <param name="referenceDpi">The density at which no scaling is applied.</param>
        /// <returns>
        /// A non-negative threshold that never falls below <paramref name="baseThreshold" />.
        /// Fractional results round to the nearest integer using midpoint-to-even and saturate at
        /// <see cref="int.MaxValue" />. Invalid density values leave the non-negative baseline
        /// unchanged.
        /// </returns>
        public static int CalculatePixelDragThreshold(
            int baseThreshold,
            float dpi,
            float referenceDpi = 160f
        )
        {
            int clampedBase = Mathf.Max(0, baseThreshold);
            if (
                !float.IsFinite(dpi)
                || !(0f < dpi)
                || !float.IsFinite(referenceDpi)
                || !(0f < referenceDpi)
            )
            {
                return clampedBase;
            }

            double scaled = (double)clampedBase * dpi / referenceDpi;
            if (int.MaxValue <= scaled)
            {
                return int.MaxValue;
            }

            double rounded = Math.Round(scaled, MidpointRounding.ToEven);
            if (int.MaxValue <= rounded)
            {
                return int.MaxValue;
            }

            return Math.Max(clampedBase, (int)rounded);
        }

        /// <summary>Applies a density-scaled drag threshold to an event system.</summary>
        /// <param name="eventSystem">The event system to update.</param>
        /// <param name="baseThreshold">The threshold at the reference density.</param>
        /// <param name="referenceDpi">The density at which no scaling is applied.</param>
        /// <returns><see langword="true" /> when the event system is valid.</returns>
        public static bool TryApplyPixelDragThresholdForCurrentDpi(
            this EventSystem eventSystem,
            int baseThreshold,
            float referenceDpi = 160f
        )
        {
            if (eventSystem == null)
            {
                return false;
            }

            eventSystem.pixelDragThreshold = CalculatePixelDragThreshold(
                baseThreshold,
                GetScreenDpi(),
                referenceDpi
            );
            return true;
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal static void ResetScreenDpiProvider()
        {
            _screenDpiProvider = null;
        }
#endif

        /// <summary>Tries to resolve a raycast hit or pointer position in world space.</summary>
        /// <param name="pointerEventData">The pointer event to resolve.</param>
        /// <param name="rectangle">The target rectangle.</param>
        /// <param name="worldPoint">The resolved world point, or the default vector on failure.</param>
        /// <returns>
        /// <see langword="true" /> when a valid raycast hit exists or the pointer reaches the
        /// rectangle's plane.
        /// </returns>
        /// <example>
        /// <code>
        /// if (eventData.TryGetWorldPoint(panel, out Vector3 point))
        /// {
        ///     marker.position = point;
        /// }
        /// </code>
        /// </example>
        public static bool TryGetWorldPoint(
            this PointerEventData pointerEventData,
            RectTransform rectangle,
            out Vector3 worldPoint
        )
        {
            if (pointerEventData == null || rectangle == null)
            {
                worldPoint = default;
                return false;
            }

            RaycastResult currentRaycast = pointerEventData.pointerCurrentRaycast;
            if (currentRaycast.isValid && IsFinite(currentRaycast.worldPosition))
            {
                worldPoint = currentRaycast.worldPosition;
                return true;
            }

            RaycastResult pressRaycast = pointerEventData.pointerPressRaycast;
            if (pressRaycast.isValid && IsFinite(pressRaycast.worldPosition))
            {
                worldPoint = pressRaycast.worldPosition;
                return true;
            }

            Vector2 screenPoint = ClampToScreen(pointerEventData.position);
            if (!TryResolveEventCamera(pointerEventData, rectangle, out Camera eventCamera))
            {
                worldPoint = default;
                return false;
            }

            if (
                !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    rectangle,
                    screenPoint,
                    eventCamera,
                    out Vector3 resolved
                ) || !IsFinite(resolved)
            )
            {
                worldPoint = default;
                return false;
            }

            worldPoint = resolved;
            return true;
        }

        /// <summary>Tries to resolve a raycast hit or pointer position in a rectangle's local space.</summary>
        /// <param name="pointerEventData">The pointer event to resolve.</param>
        /// <param name="rectangle">The target rectangle.</param>
        /// <param name="localPoint">The resolved local point, or the default vector on failure.</param>
        /// <returns>
        /// <see langword="true" /> when a valid raycast hit exists or the pointer reaches the
        /// rectangle's plane.
        /// </returns>
        /// <example>
        /// <code>
        /// if (eventData.TryGetLocalPoint(panel, out Vector2 point))
        /// {
        ///     handle.anchoredPosition = point;
        /// }
        /// </code>
        /// </example>
        public static bool TryGetLocalPoint(
            this PointerEventData pointerEventData,
            RectTransform rectangle,
            out Vector2 localPoint
        )
        {
            if (!pointerEventData.TryGetWorldPoint(rectangle, out Vector3 worldPoint))
            {
                localPoint = default;
                return false;
            }

            Vector3 local = rectangle.InverseTransformPoint(worldPoint);
            Vector2 resolved = new(local.x, local.y);
            if (!IsFinite(resolved))
            {
                localPoint = default;
                return false;
            }

            localPoint = resolved;
            return true;
        }

        private static bool TryResolveEventCamera(
            PointerEventData pointerEventData,
            RectTransform rectangle,
            out Camera eventCamera
        )
        {
            Canvas canvas = rectangle.GetComponentInParent<Canvas>();
            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = null;
                return true;
            }

            Camera enterEventCamera = pointerEventData.enterEventCamera;
            if (enterEventCamera != null)
            {
                eventCamera = enterEventCamera;
                return true;
            }

            Camera pressEventCamera = pointerEventData.pressEventCamera;
            if (pressEventCamera != null)
            {
                eventCamera = pressEventCamera;
                return true;
            }

            if (rootCanvas == null)
            {
                eventCamera = null;
                return true;
            }

            Camera worldCamera = rootCanvas.worldCamera;
            if (worldCamera == null)
            {
                eventCamera = null;
                return false;
            }

            eventCamera = worldCamera;
            return true;
        }

        private static Vector2 ClampToScreen(Vector2 point)
        {
            float x = float.IsNaN(point.x) ? 0f : Mathf.Clamp(point.x, 0f, Screen.width);
            float y = float.IsNaN(point.y) ? 0f : Mathf.Clamp(point.y, 0f, Screen.height);
            return new Vector2(x, y);
        }

        private static float GetScreenDpi()
        {
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            return ScreenDpiProvider();
#else
            return Screen.dpi;
#endif
        }

        private static bool IsFinite(Vector2 point)
        {
            return float.IsFinite(point.x) && float.IsFinite(point.y);
        }

        private static bool IsFinite(Vector3 point)
        {
            return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }
    }
}
