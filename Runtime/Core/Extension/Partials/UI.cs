// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    public static partial class UnityExtensions
    {
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

        /// <summary>Tries to resolve the pointer on a rectangle's world-space plane.</summary>
        /// <param name="pointerEventData">The pointer event to resolve.</param>
        /// <param name="rectangle">The target rectangle.</param>
        /// <param name="worldPoint">The resolved world point, or the default vector on failure.</param>
        /// <returns><see langword="true" /> when the pointer reaches the rectangle's plane.</returns>
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

        /// <summary>Tries to resolve the pointer in a rectangle's local coordinate space.</summary>
        /// <param name="pointerEventData">The pointer event to resolve.</param>
        /// <param name="rectangle">The target rectangle.</param>
        /// <param name="localPoint">The resolved local point, or the default vector on failure.</param>
        /// <returns><see langword="true" /> when the pointer reaches the rectangle's plane.</returns>
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
