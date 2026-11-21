// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals.UGUI
{
    using System;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Receives uGUI raycasts without drawing content.
    /// </summary>
    [AddComponentMenu("Wallstop Studios/Unity Helpers/Non-Drawing Graphic")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class NonDrawingGraphic : Graphic, ICanvasRaycastFilter
    {
        /// <summary>
        /// Gets or sets the shape of the hit area in the graphic's local rectangle.
        /// </summary>
        public HitShape Shape
        {
            get => _shape;
            set => _shape = value;
        }

        [SerializeField]
        private HitShape _shape = HitShape.Rect;

        /// <inheritdoc />
        public override void SetMaterialDirty() { }

        /// <inheritdoc />
        public override void SetVerticesDirty() { }

        /// <summary>
        /// Tests whether the screen point projects inside the selected hit area.
        /// </summary>
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    screenPoint,
                    eventCamera,
                    out Vector2 localPoint
                )
            )
            {
                return false;
            }

            Rect rect = rectTransform.rect;
            if (_shape != HitShape.InscribedCircle)
            {
                return rect.Contains(localPoint);
            }

            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            Vector2 offset = localPoint - rect.center;
            return 0f < radius && offset.sqrMagnitude <= radius * radius;
        }

        /// <inheritdoc />
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
        }

        /// <summary>
        /// Defines the area in which the target receives raycasts.
        /// </summary>
        public enum HitShape
        {
            /// <summary>Uninitialized shape, treated as a rectangle.</summary>
            [Obsolete("Select Rect or InscribedCircle explicitly.")]
            Unset = 0,

            /// <summary>The whole local rectangle.</summary>
            Rect = 1,

            /// <summary>The centered circle whose diameter is the smaller rectangle side.</summary>
            InscribedCircle = 2,
        }
    }
}
