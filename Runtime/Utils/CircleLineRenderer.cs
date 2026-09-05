// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0649
namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using Core.Attributes;
    using Core.Extension;
    using Core.Helper;
    using Core.Random;
    using UnityEngine;

    [RequireComponent(typeof(LineRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    [DisallowMultipleComponent]
    public sealed class CircleLineRenderer : MonoBehaviour
    {
        public float minLineWidth = 0.005f;
        public float maxLineWidth = 0.02f;
        public int numSegments = 4;
        public int baseSegments = 4;
        public float updateRateSeconds = 0.1f;
        public Color color = Color.grey;

        // Bound inspector-driven vertex allocation to keep periodic rendering from exhausting memory.
        private const int MaxRenderSegments = 4096;

        public Vector3 Offset
        {
            get => _offset;
            set => transform.localPosition = _offset = value;
        }

        [SiblingComponent]
        private CircleCollider2D _collider;

        [SiblingComponent]
        private LineRenderer[] _lineRenderers;

        private Vector3 _offset;

        private Coroutine _update;

        private readonly Dictionary<int, Vector3[]> _cachedSegments = new();

        private void Awake()
        {
            this.AssignSiblingComponents();
        }

        private void OnEnable()
        {
            if (_update != null)
            {
                StopCoroutine(_update);
            }
            _update = this.StartFunctionAsCoroutine(Render, updateRateSeconds);
        }

        private void OnDisable()
        {
            if (_update != null)
            {
                StopCoroutine(_update);
                _update = null;
            }
        }

        private void OnValidate()
        {
            if (numSegments <= 2)
            {
                this.LogWarn($"Invalid number of segments {numSegments}.");
            }

            if (updateRateSeconds <= 0)
            {
                this.LogWarn($"Invalid update rate {updateRateSeconds}.");
            }

            if (maxLineWidth < minLineWidth)
            {
                this.LogWarn(
                    $"MaxLineWidth {maxLineWidth} smaller than MinLineWidth {minLineWidth}."
                );
            }
        }

        private void Update()
        {
            if (_lineRenderers == null || _collider == null)
            {
                return;
            }

            bool colliderEnabled = _collider.enabled;
            foreach (LineRenderer lineRenderer in _lineRenderers)
            {
                if (lineRenderer != null)
                {
                    lineRenderer.enabled = colliderEnabled;
                }
            }
        }

        private void Render()
        {
            if (_lineRenderers == null)
            {
                return;
            }

            // Normalize authored settings each tick so invalid values cannot repeatedly throw from the coroutine.
            float lowWidth = Mathf.Min(minLineWidth, maxLineWidth);
            float highWidth = Mathf.Max(minLineWidth, maxLineWidth);
            int segments = Mathf.Clamp(numSegments, 0, MaxRenderSegments);
            float radius = _collider != null ? _collider.radius : 0f;

            foreach (LineRenderer lineRenderer in _lineRenderers)
            {
                if (lineRenderer == null)
                {
                    continue;
                }

                if (!lineRenderer.enabled || segments < 3)
                {
                    lineRenderer.positionCount = 0;
                    continue;
                }

                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
                lineRenderer.loop = true;
                lineRenderer.positionCount = segments;

                float lineWidth = Mathf.Approximately(lowWidth, highWidth)
                    ? lowWidth
                    : PRNG.Instance.NextFloat(lowWidth, highWidth);

                lineRenderer.startWidth = lineWidth;
                lineRenderer.endWidth = lineWidth;
                lineRenderer.useWorldSpace = false;
                float distanceMultiplier = radius;

                float angle = 360f / segments;
                float offsetRadians = PRNG.Instance.NextFloat(angle);
                float currentOffset = offsetRadians;
                if (!_cachedSegments.TryGetValue(segments, out Vector3[] positions))
                {
                    positions = new Vector3[segments];
                    _cachedSegments[segments] = positions;
                }

                Array.Clear(positions, 0, segments);
                for (int i = 0; i < segments; ++i)
                {
                    positions[i] =
                        new Vector3(
                            Mathf.Cos(Mathf.Deg2Rad * currentOffset),
                            Mathf.Sin(Mathf.Deg2Rad * currentOffset)
                        ) * distanceMultiplier;
                    currentOffset += angle % 360f;
                }

                lineRenderer.SetPositions(positions);
            }
        }
    }
}
