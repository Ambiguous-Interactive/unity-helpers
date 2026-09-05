// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using Core.Attributes;
    using Core.Helper;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Polygon collider optimizer. Removes points from the collider polygon with
    /// the given reduction Tolerance
    /// </summary>
    [AddComponentMenu("2D Collider Optimization/ Polygon Collider Optimizer")]
    [RequireComponent(typeof(PolygonCollider2D))]
    public sealed class PolygonCollider2DOptimizer : MonoBehaviour
    {
        public double tolerance;

        [SiblingComponent]
#pragma warning disable CS0649
        private PolygonCollider2D _collider;
#pragma warning restore CS0649

        [SerializeField]
        private List<Path> _originalPaths = new();

        public void Refresh()
        {
            OnValidate();
        }

        private void OnValidate()
        {
            if (_collider == null)
            {
                this.AssignRelationalComponents();
            }

            // Keep original paths so repeated optimization does not accumulate simplification errors.
            if (_originalPaths.Count == 0)
            {
                for (int i = 0; i < _collider.pathCount; ++i)
                {
                    Vector2[] current = _collider.GetPath(i);
                    List<Vector2> points = new(current);
                    // Unity may omit a duplicated closing point; preserve the authored closed-loop shape.
                    if (0 < points.Count)
                    {
                        Vector2 first = points[0];
                        Vector2 last = points[^1];
                        if (first != last)
                        {
                            points.Add(first);
                        }
                    }
                    Path path = new(points);
                    _originalPaths.Add(path);
                }
            }

            if (tolerance <= 0)
            {
                for (int i = 0; i < _originalPaths.Count; ++i)
                {
                    _collider.SetPath(i, _originalPaths[i].points);
                }
                return;
            }

            for (int i = 0; i < _originalPaths.Count; ++i)
            {
                List<Vector2> path = _originalPaths[i].points;
                List<Vector2> updatedPath = LineHelper.SimplifyPrecise(path, tolerance);
                _collider.SetPath(i, updatedPath);
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
            }
#endif
        }

        [Serializable]
        private sealed class Path
        {
            public List<Vector2> points = new();

            public Path() { }

            public Path(IEnumerable<Vector2> points)
            {
                this.points.AddRange(points);
            }
        }
    }
}
