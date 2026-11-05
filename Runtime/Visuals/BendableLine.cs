// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Draws and animates a sprite along a quadratic curve without a tween dependency.</summary>
    /// <remarks>Endpoints are world XY positions; Control is local. Use transforms in the XY plane. The component owns its mesh and material.</remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Wallstop Studios/Unity Helpers/Bendable Line")]
    public sealed class BendableLine : MonoBehaviour
    {
        /// <summary>The largest accepted number of curve segments.</summary>
        public const int MaximumSegments = 4096;

        private const double PlaneSlopeSquaredTolerance = 1e-12;
        private const float Zero = 0f;
        private const int MinimumSegments = 1;
        private const float MaximumCurveIntensity = 5f;
        private const double MinimumElapsedSeconds = 0d;
        private const string MainTextureProperty = "_MainTex";
        private const string UvBoundsProperty = "_UvBounds";

        private const string ShaderResource = "WallstopStudios/UnityHelpers/BendableLine";

        private static readonly int MainTextureId = Shader.PropertyToID(MainTextureProperty);

        private static readonly int UvBoundsId = Shader.PropertyToID(UvBoundsProperty);

        /// <summary>The sprite stretched along the curve, including unreadable rectangular atlas sprites.</summary>
        public Sprite Sprite
        {
            get => _sprite;
            set
            {
                _sprite = value;
                _spriteChanged = true;
                _dirty = true;
            }
        }

        /// <summary>The optional world start position; null leaves this transform in place.</summary>
        public Vector2? Start
        {
            get => _start;
            set
            {
                _start = NormalizePosition(value);
                _dirty = true;
            }
        }

        /// <summary>The animated endpoint in local coordinates, or null when no endpoint is assigned.</summary>
        public Vector2? CurrentEnd => _end.HasValue ? _currentEnd : null;

        /// <summary>The optional world endpoint; changes animate over AnimationDuration.</summary>
        public Vector2? End
        {
            get => _end;
            set
            {
                Vector2? next = NormalizePosition(value);
                if (_end == next)
                {
                    return;
                }
                _end = next;
                _endAnimationStart = _currentEnd;
                _endAnimationTime = Time.timeAsDouble;
                _animateEnd =
                    _hasCurrentEnd
                    && next.HasValue
                    && 0f < _animationDuration
                    && isActiveAndEnabled;
                _dirty = true;
            }
        }

        /// <summary>The local quadratic control point before applying CurveIntensity.</summary>
        public Vector2 Control
        {
            get => _control;
            set
            {
                _control = WallMath.IsFinite(value) ? value : Vector2.zero;
                _dirty = true;
            }
        }

        /// <summary>The bend strength from a straight line at zero to the authored control at one.</summary>
        public float CurveIntensity
        {
            get => _curveIntensity;
            set
            {
                _curveIntensity = float.IsFinite(value)
                    ? Mathf.Clamp(value, Zero, MaximumCurveIntensity)
                    : 0f;
                _dirty = true;
            }
        }

        /// <summary>The nonnegative line width in local units.</summary>
        public float Thickness
        {
            get => _thickness;
            set
            {
                _thickness = float.IsFinite(value) ? Mathf.Max(Zero, value) : 0f;
                _dirty = true;
            }
        }

        /// <summary>The curve subdivision count, clamped from one through MaximumSegments.</summary>
        public int Segments
        {
            get => _segments;
            set
            {
                _segments = Mathf.Clamp(value, MinimumSegments, MaximumSegments);
                _dirty = true;
            }
        }

        /// <summary>The scaled seconds for endpoint and draw animations; nonpositive or nonfinite values settle immediately.</summary>
        public float AnimationDuration
        {
            get => _animationDuration;
            set => _animationDuration = float.IsFinite(value) ? Mathf.Max(Zero, value) : 0f;
        }

        /// <summary>The renderer sorting layer identifier.</summary>
        public int SortingLayerId
        {
            get => _sortingLayerId;
            set
            {
                if (SortingLayer.IsValid(value))
                {
                    _sortingLayerId = value;
                    _dirty = true;
                }
            }
        }

        /// <summary>The renderer order within its sorting layer.</summary>
        public int SortingOrder
        {
            get => _sortingOrder;
            set
            {
                _sortingOrder = Mathf.Clamp(value, short.MinValue, short.MaxValue);
                _dirty = true;
            }
        }

        /// <summary>The visible fraction of the line, clamped between zero and one.</summary>
        public float Progress
        {
            get => _progress;
            set
            {
                _progress = float.IsFinite(value) ? Mathf.Clamp01(value) : 0f;
                _animateProgress = false;
                _dirty = true;
            }
        }

        [SerializeField]
        private Sprite _sprite;

        [SerializeField]
        private Vector2 _control;

        [SerializeField, Range(0f, 5f)]
        private float _curveIntensity = 1f;

        [SerializeField, Min(0f)]
        private float _thickness = 0.1f;

        [SerializeField, Range(1, MaximumSegments)]
        private int _segments = 50;

        [SerializeField, Min(0f)]
        private float _animationDuration = 0.25f;

        [SerializeField]
        private int _sortingLayerId;

        [SerializeField]
        private int _sortingOrder;
        private Vector2? _start;
        private Vector2? _end;
        private Vector2 _currentEnd;
        private Vector2 _endAnimationStart;
        private double _endAnimationTime;
        private double _progressAnimationTime;
        private double _animationPausedAt;
        private float _progressAnimationStart;
        private float _progressTarget;
        private float _progress;
        private bool _animateEnd;
        private bool _animateProgress;
        private bool _animationsPaused;
        private bool _dirty = true;
        private bool _spriteChanged = true;
        private bool _hasSpriteMapping;
        private bool _hasCurrentEnd;
        private Vector2 _uvOrigin;
        private Vector2 _uvRight;
        private Vector2 _uvUp;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;
        private Texture2D _examinedTexture;
        private Vector3[] _vertices;
        private Vector2[] _uv;
        private int[] _triangles;
        private Matrix4x4 _previousTransform;

        private static Vector2? NormalizePosition(Vector2? value)
        {
            if (!value.HasValue)
            {
                return null;
            }
            Vector2 position = value.Value;
            return WallMath.IsFinite(position) ? value : null;
        }

        /// <summary>Animates drawing toward the end or back to the start, optionally restarting at the opposite extreme.</summary>
        /// <remarks>Animation timing pauses while the line cannot be drawn.</remarks>
        public void AnimateLine(bool forward = true, bool resetProgress = false)
        {
            if (resetProgress)
            {
                _progress = forward ? 0f : 1f;
            }
            _progressAnimationStart = _progress;
            _progressTarget = forward ? 1f : 0f;
            _progressAnimationTime = Time.timeAsDouble;
            _animateProgress = isActiveAndEnabled && 0f < _animationDuration;
            if (!_animateProgress)
            {
                _progress = _progressTarget;
            }
            _dirty = true;
        }

        /// <summary>Updates the geometry immediately and reports whether a valid line can be drawn.</summary>
        /// <remarks>Null endpoints, unsupported sprite meshes, missing resources and nonfinite geometry clear the owned mesh.</remarks>
        public bool TryRefresh()
        {
            if (!EnsureResources())
            {
                PauseAnimations();
                return false;
            }
            if (_start.HasValue)
            {
                Vector3 position = transform.position;
                Vector2 requestedStart = _start.Value;
                transform.position = new Vector3(requestedStart.x, requestedStart.y, position.z);
            }
            if (SortingLayer.IsValid(_sortingLayerId))
            {
                _renderer.sortingLayerID = _sortingLayerId;
            }
            _renderer.sortingOrder = Mathf.Clamp(_sortingOrder, short.MinValue, short.MaxValue);
            Texture2D texture = _sprite != null ? _sprite.texture : null;
            if (
                _spriteChanged
                || _examinedTexture != texture
                || (_hasSpriteMapping && texture == null)
            )
            {
                _examinedTexture = texture;
                _hasSpriteMapping = TryReadSpriteMapping();
                Texture2D mappedTexture = _hasSpriteMapping ? _examinedTexture : null;
                _material.SetTexture(MainTextureId, mappedTexture);
                _spriteChanged = false;
            }
            _previousTransform = transform.localToWorldMatrix;
            float determinant = _previousTransform.determinant;
            double rightDepthSquared = (double)_previousTransform.m20 * _previousTransform.m20;
            double upDepthSquared = (double)_previousTransform.m21 * _previousTransform.m21;
            double rightLengthSquared =
                (double)_previousTransform.m00 * _previousTransform.m00
                + (double)_previousTransform.m10 * _previousTransform.m10
                + rightDepthSquared;
            double upLengthSquared =
                (double)_previousTransform.m01 * _previousTransform.m01
                + (double)_previousTransform.m11 * _previousTransform.m11
                + upDepthSquared;
            if (
                !_end.HasValue
                || !_hasSpriteMapping
                || _sprite == null
                || _sprite.texture == null
                || determinant == 0f
                || !float.IsFinite(determinant)
                || PlaneSlopeSquaredTolerance * rightLengthSquared < rightDepthSquared
                || PlaneSlopeSquaredTolerance * upLengthSquared < upDepthSquared
            )
            {
                _mesh.Clear();
                _hasCurrentEnd = false;
                PauseAnimations();
                _dirty = false;
                return false;
            }
            ResumeAnimations();
            Vector2 requestedEnd = _end.Value;
            float depth = transform.position.z;
            Vector3 worldEnd = new(requestedEnd.x, requestedEnd.y, depth);
            Vector2 targetEnd = transform.InverseTransformPoint(worldEnd);
            float endpointFraction = AnimationFraction(_endAnimationTime);
            _currentEnd = _animateEnd
                ? Vector2.Lerp(_endAnimationStart, targetEnd, endpointFraction)
                : targetEnd;
            _animateEnd = _animateEnd && endpointFraction < 1f;
            if (_animateProgress)
            {
                float fraction = AnimationFraction(_progressAnimationTime);
                _progress = Mathf.Lerp(_progressAnimationStart, _progressTarget, fraction);
                _animateProgress = fraction < 1f;
            }
            bool built = RebuildMesh();
            _hasCurrentEnd = built;
            if (!built)
            {
                _mesh.Clear();
                PauseAnimations();
            }
            _dirty = false;
            return built;
        }

        private void PauseAnimations()
        {
            if (!_animationsPaused)
            {
                _animationPausedAt = Time.timeAsDouble;
                _animationsPaused = true;
            }
        }

        private void ResumeAnimations()
        {
            if (!_animationsPaused)
            {
                return;
            }
            double now = Time.timeAsDouble;
            if (_animateEnd)
            {
                double elapsed = _animationPausedAt - _endAnimationTime;
                double boundedElapsed = Math.Max(MinimumElapsedSeconds, elapsed);
                _endAnimationTime = now - boundedElapsed;
            }
            if (_animateProgress)
            {
                double elapsed = _animationPausedAt - _progressAnimationTime;
                double boundedElapsed = Math.Max(MinimumElapsedSeconds, elapsed);
                _progressAnimationTime = now - boundedElapsed;
            }
            _animationsPaused = false;
        }

        private float AnimationFraction(double startTime)
        {
            if (!(Zero < _animationDuration))
            {
                return 1f;
            }
            double elapsed = Time.timeAsDouble - startTime;
            float fraction = (float)(elapsed / _animationDuration);
            return Mathf.Clamp01(fraction);
        }

        private bool EnsureResources()
        {
            if (_filter == null)
            {
                _filter = GetComponent<MeshFilter>();
            }
            if (_renderer == null)
            {
                _renderer = GetComponent<MeshRenderer>();
            }
            if (_filter == null || _renderer == null)
            {
                return false;
            }
            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderResource);
                if (shader == null)
                {
                    return false;
                }
                _material = new Material(shader) { hideFlags = HideFlags.DontSave };
                _renderer.sharedMaterial = _material;
                _spriteChanged = true;
            }
            if (_mesh == null)
            {
                _mesh = new Mesh { name = nameof(BendableLine), hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
                _filter.sharedMesh = _mesh;
            }
            return true;
        }

        private bool TryReadSpriteMapping()
        {
            if (
                _sprite == null
                || _sprite.associatedAlphaSplitTexture != null
                || _sprite.texture == null
            )
            {
                return false;
            }
            if (!_sprite.packed)
            {
                Rect rectangle = _sprite.rect;
                Texture2D texture = _sprite.texture;
                float originX = rectangle.xMin / texture.width;
                float originY = rectangle.yMin / texture.height;
                float uvWidth = rectangle.width / texture.width;
                float uvHeight = rectangle.height / texture.height;
                _uvOrigin = new Vector2(originX, originY);
                _uvRight = new Vector2(uvWidth, Zero);
                _uvUp = new Vector2(Zero, uvHeight);
                float maximumX = originX + uvWidth;
                float maximumY = originY + uvHeight;
                Vector4 uvBounds = new(originX, originY, maximumX, maximumY);
                _material.SetVector(UvBoundsId, uvBounds);
                return true;
            }
            if (_sprite.packingMode != SpritePackingMode.Rectangle)
            {
                return false;
            }
            Vector2[] vertices = _sprite.vertices;
            Vector2[] uv = _sprite.uv;
            if (vertices.Length != 4 || uv.Length != 4)
            {
                return false;
            }
            Vector2 minimum = vertices[0];
            Vector2 maximum = minimum;
            foreach (Vector2 vertex in vertices)
            {
                minimum = Vector2.Min(minimum, vertex);
                maximum = Vector2.Max(maximum, vertex);
            }
            if (!(minimum.x < maximum.x) || !(minimum.y < maximum.y))
            {
                return false;
            }
            int corners = 0;
            for (int index = 0; index < 4; ++index)
            {
                Vector2 vertex = vertices[index];
                Vector2 coordinate = uv[index];
                if (!WallMath.IsFinite(vertex) || !WallMath.IsFinite(coordinate))
                {
                    return false;
                }
                if (vertex.x == minimum.x && vertex.y == minimum.y)
                {
                    _uvOrigin = uv[index];
                    corners |= 1;
                }
                else if (vertex.x == maximum.x && vertex.y == minimum.y)
                {
                    _uvRight = uv[index];
                    corners |= 2;
                }
                else if (vertex.x == minimum.x && vertex.y == maximum.y)
                {
                    _uvUp = uv[index];
                    corners |= 4;
                }
                else if (vertex.x == maximum.x && vertex.y == maximum.y)
                {
                    corners |= 8;
                }
            }
            if (corners != 15)
            {
                return false;
            }
            _uvRight -= _uvOrigin;
            _uvUp -= _uvOrigin;
            Vector2 uvMinimum = uv[0];
            Vector2 uvMaximum = uvMinimum;
            foreach (Vector2 coordinate in uv)
            {
                uvMinimum = Vector2.Min(uvMinimum, coordinate);
                uvMaximum = Vector2.Max(uvMaximum, coordinate);
            }
            Vector4 bounds = new(uvMinimum.x, uvMinimum.y, uvMaximum.x, uvMaximum.y);
            _material.SetVector(UvBoundsId, bounds);
            float unitsPerPixel = 1f / _sprite.pixelsPerUnit;
            Vector2 fullMinimum = -_sprite.pivot * unitsPerPixel;
            Vector2 fullSize = _sprite.rect.size * unitsPerPixel;
            Vector2 meshSize = maximum - minimum;
            _uvOrigin +=
                (fullMinimum.x - minimum.x) / meshSize.x * _uvRight
                + (fullMinimum.y - minimum.y) / meshSize.y * _uvUp;
            _uvRight *= fullSize.x / meshSize.x;
            _uvUp *= fullSize.y / meshSize.y;
            return WallMath.IsFinite(_uvOrigin)
                && WallMath.IsFinite(_uvRight)
                && WallMath.IsFinite(_uvUp);
        }

        private bool RebuildMesh()
        {
            int segments = Mathf.Clamp(_segments, MinimumSegments, MaximumSegments);
            int vertexCount = (segments + 1) * 2;
            if (_vertices == null || _vertices.Length != vertexCount)
            {
                _vertices = new Vector3[vertexCount];
                _uv = new Vector2[vertexCount];
                _triangles = new int[segments * 6];
                for (int index = 0; index < segments; ++index)
                {
                    int vertex = index * 2;
                    int triangle = index * 6;
                    _triangles[triangle] = vertex;
                    _triangles[triangle + 1] = vertex + 2;
                    _triangles[triangle + 2] = vertex + 1;
                    _triangles[triangle + 3] = vertex + 1;
                    _triangles[triangle + 4] = vertex + 2;
                    _triangles[triangle + 5] = vertex + 3;
                }
            }
            Vector2 midpoint = _currentEnd * 0.5f;
            Vector2 control = Vector2.LerpUnclamped(midpoint, _control, _curveIntensity);
            if (!WallMath.IsFinite(control) || !WallMath.IsFinite(_currentEnd))
            {
                return false;
            }
            Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int index = 0; index <= segments; ++index)
            {
                float t = (float)index / segments * _progress;
                float remaining = 1f - t;
                Vector2 position = 2f * remaining * t * control + t * t * _currentEnd;
                Vector2 tangent = remaining * control + t * (_currentEnd - control);
                if (tangent.x == 0f && tangent.y == 0f)
                {
                    tangent = _currentEnd;
                }
                double lengthSquared =
                    (double)tangent.x * tangent.x + (double)tangent.y * tangent.y;
                double length = Math.Sqrt(lengthSquared);
                double halfWidth = _thickness * 0.5;
                Vector2 normal = Vector2.zero;
                if (length != 0)
                {
                    float normalX = (float)(-tangent.y / length * halfWidth);
                    float normalY = (float)(tangent.x / length * halfWidth);
                    normal = new Vector2(normalX, normalY);
                }
                Vector2 left = position - normal;
                Vector2 right = position + normal;
                if (!WallMath.IsFinite(left) || !WallMath.IsFinite(right))
                {
                    return false;
                }
                int vertex = index * 2;
                _vertices[vertex] = left;
                _vertices[vertex + 1] = right;
                _uv[vertex] = _uvOrigin + t * _uvUp;
                _uv[vertex + 1] = _uvOrigin + _uvRight + t * _uvUp;
                Vector2 edgeMinimum = Vector2.Min(left, right);
                Vector2 edgeMaximum = Vector2.Max(left, right);
                minimum = Vector2.Min(minimum, edgeMinimum);
                maximum = Vector2.Max(maximum, edgeMaximum);
            }
            double width = (double)maximum.x - minimum.x;
            double height = (double)maximum.y - minimum.y;
            if (float.MaxValue < width || float.MaxValue < height)
            {
                return false;
            }
            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.uv = _uv;
            _mesh.triangles = _triangles;
            float centerX = (float)(((double)maximum.x + minimum.x) * 0.5);
            float centerY = (float)(((double)maximum.y + minimum.y) * 0.5);
            float boundsWidth = (float)width;
            float boundsHeight = (float)height;
            Vector3 boundsCenter = new(centerX, centerY);
            Vector3 boundsSize = new(boundsWidth, boundsHeight);
            _mesh.bounds = new Bounds(boundsCenter, boundsSize);
            return true;
        }

        private void OnEnable()
        {
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (
                _dirty
                || _animateEnd
                || _animateProgress
                || _previousTransform != transform.localToWorldMatrix
                || (_hasSpriteMapping && (_sprite == null || _sprite.texture == null))
                || (_sprite != null && _material != null && _examinedTexture != _sprite.texture)
            )
            {
                TryRefresh();
            }
        }

        private void OnDisable()
        {
            _animateEnd = false;
            _animateProgress = false;
            _animationsPaused = false;
            _progress = 0f;
            if (_mesh != null)
            {
                _mesh.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_filter != null && _filter.sharedMesh == _mesh)
            {
                _filter.sharedMesh = null;
            }
            if (_renderer != null && _renderer.sharedMaterial == _material)
            {
                _renderer.sharedMaterial = null;
            }
            if (Application.isPlaying)
            {
                Destroy(_mesh);
                Destroy(_material);
            }
            else
            {
                DestroyImmediate(_mesh);
                DestroyImmediate(_material);
            }
        }

        private void OnValidate()
        {
            CurveIntensity = _curveIntensity;
            Thickness = _thickness;
            Segments = _segments;
            AnimationDuration = _animationDuration;
            SortingOrder = _sortingOrder;
            _spriteChanged = true;
        }
    }
}
