// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    /// <summary>
    /// Exposes a configurable offset from the attached transform that can also be consumed by
    /// sprites that expect a logical center point separate from the transform origin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CenterPointOffset : MonoBehaviour
    {
        /// <summary>
        /// Gets the world-space center derived from the transform position and <see cref="offset"/>.
        /// </summary>
        public Vector2 CenterPoint
        {
            get
            {
                Transform localTransform = transform;
                Vector2 scaledOffset = offset * localTransform.localScale;
                return (Vector2)localTransform.position + scaledOffset;
            }
        }

        /// <summary>
        /// Offset applied relative to the local scale for computing <see cref="CenterPoint"/>.
        /// </summary>
        public Vector2 offset = Vector2.zero;

        /// <summary>
        /// When enabled, indicates associated sprite logic should use the computed offset center.
        /// </summary>
        public bool spriteUsesOffset = true;
    }
}
