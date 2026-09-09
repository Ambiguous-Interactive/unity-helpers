// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Serialization;
    using UnityEngine.UI;

    [DisallowMultipleComponent]
    public sealed class MatchColliderToSprite : MonoBehaviour
    {
        public event Action colliderUpdated;

        public Func<Sprite> spriteOverrideProducer;

        [FormerlySerializedAs("_spriteRenderer")]
        public SpriteRenderer spriteRenderer;

        [FormerlySerializedAs("_image")]
        public Image image;

        [FormerlySerializedAs("_collider")]
        public PolygonCollider2D polygonCollider;

        /// <summary>Traces opaque art instead of copying Unity's generated physics shape.</summary>
        [Tooltip(
            "The opaque region is a prop: keep the generated shape. For a map or mask where growing the opaque region is wrong, trace the art."
        )]
        public bool traceExactly;

        /// <summary>Gets or sets the minimum nonzero alpha considered opaque when tracing art.</summary>
        [Range(0, 255)]
        public int alphaThreshold = 1;

        /// <summary>Gets or sets the minimum contour area in traced pixels, for both islands and holes.</summary>
        [Min(0)]
        public float minimumTracedArea;

        internal Sprite _lastHandled;
        private bool _lastTraceExactly;
        private int _lastAlphaThreshold;
        private float _lastMinimumTracedArea;

        /// <summary>Resolves component references in the Editor; player callers rebuild the collider.</summary>
        public void OnValidate()
        {
#if UNITY_EDITOR
            if (polygonCollider == null)
            {
                TryGetComponent(out polygonCollider);
            }
            if (spriteRenderer == null)
            {
                TryGetComponent(out spriteRenderer);
            }
            if (spriteRenderer == null && image == null)
            {
                TryGetComponent(out image);
            }
#else
            RebuildCollider();
#endif
        }

        /// <summary>Synchronously rebuilds the collider from the current sprite and settings.</summary>
        /// <remarks>Editor callers must record Undo for the component and collider before invoking this command.</remarks>
        public void RebuildCollider()
        {
            if (polygonCollider == null && !TryGetComponent(out polygonCollider))
            {
                return;
            }

            try
            {
                _lastHandled = ResolveSprite();
                _lastTraceExactly = traceExactly;
                _lastAlphaThreshold = alphaThreshold;
                _lastMinimumTracedArea = minimumTracedArea;
                if (_lastHandled == null)
                {
                    polygonCollider.pathCount = 0;
                    return;
                }

                if (traceExactly)
                {
                    if (
                        !SpriteMaskReader.TryRead(
                            _lastHandled,
                            out SpriteAlphaMask mask,
                            out string error,
                            (byte)Mathf.Clamp(alphaThreshold, 0, 255)
                        )
                    )
                    {
                        Debug.LogWarning(
                            $"{nameof(MatchColliderToSprite)}: {error} The existing collider was preserved.",
                            this
                        );
                        return;
                    }
                    if (
                        !SpriteBoundaryTracer.TryTrace(
                            mask,
                            out List<Vector2[]> paths,
                            minimumTracedArea
                        )
                    )
                    {
                        Debug.LogWarning(
                            $"{nameof(MatchColliderToSprite)}: Invalid trace settings. The existing collider was preserved.",
                            this
                        );
                        return;
                    }
                    polygonCollider.pathCount = paths.Count;
                    for (int pathIndex = 0; pathIndex < paths.Count; ++pathIndex)
                    {
                        polygonCollider.SetPath(pathIndex, paths[pathIndex]);
                    }
                    return;
                }

                polygonCollider.points = Array.Empty<Vector2>();
                int physicsShapes = _lastHandled.GetPhysicsShapeCount();
                polygonCollider.pathCount = physicsShapes;
                using PooledResource<List<Vector2>> bufferResource = Buffers<Vector2>.List.Get(
                    out List<Vector2> buffer
                );
                for (int i = 0; i < physicsShapes; ++i)
                {
                    buffer.Clear();
                    _ = _lastHandled.GetPhysicsShape(i, buffer);
                    polygonCollider.SetPath(i, buffer);
                }
            }
            finally
            {
                colliderUpdated?.Invoke();
            }
        }

        private void Awake()
        {
            if (enabled)
            {
                RebuildCollider();
            }
        }

        private void Update()
        {
            Sprite current = ResolveSprite();
            if (
                _lastHandled == current
                && _lastTraceExactly == traceExactly
                && _lastAlphaThreshold == alphaThreshold
                && _lastMinimumTracedArea.Equals(minimumTracedArea)
            )
            {
                return;
            }

            RebuildCollider();
        }

        private Sprite ResolveSprite()
        {
            if (spriteOverrideProducer != null)
            {
                return spriteOverrideProducer();
            }
            if (spriteRenderer != null || TryGetComponent(out spriteRenderer))
            {
                return spriteRenderer.sprite;
            }
            if (image != null || TryGetComponent(out image))
            {
                return image.sprite;
            }
            return null;
        }
    }
}
