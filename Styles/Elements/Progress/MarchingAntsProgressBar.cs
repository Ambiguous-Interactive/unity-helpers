// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class MarchingAntsProgressBar : VisualElement
    {
        public enum OrientationType
        {
            Horizontal = 0,
            Vertical = 1,
        }

        public const string USSClassName = "marching-ants-progress-bar";
        public const string USSTrackClassName = USSClassName + "__track";
        public const string USSFillContainerClassName = USSClassName + "__fill-container";
        public const string USSFillClassName = USSClassName + "__fill";
        public const string USSTrackColorVarName = "--ants-track-color";
        public const string USSProgressColorVarName = "--ants-progress-color";
        public const string USSThicknessVarName = "--ants-thickness";
        public const string USSBorderRadiusVarName = "--ants-border-radius";
        public const string USSDashOnVarName = "--ants-dash-on";
        public const string USSDashOffVarName = "--ants-dash-off";

        private readonly VisualElement _trackElement;
        private readonly VisualElement _fillContainer;
        private readonly VisualElement _fillElement;
        private float _progress = 0.5f;
        public float Progress
        {
            get => _progress;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _progress = Mathf.Clamp01(value);
                UpdateFillContainerSize();
            }
        }

        private Color _trackColor = new(0.4f, 0.4f, 0.4f, 1f);
        public Color TrackColor
        {
            get => _trackColor;
            set
            {
                _trackColor = value;
                _trackElement?.MarkDirtyRepaint();
            }
        }

        private Color _progressColor = Color.white;
        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                _fillElement?.MarkDirtyRepaint();
            }
        }

        private float _thickness = 3f;
        public float Thickness
        {
            get => _thickness;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _thickness = Mathf.Max(1f, value);
                UpdateTrackAndFillElements();
            }
        }

        private float _borderRadius = 5f;
        public float BorderRadius
        {
            get => _borderRadius;
            set
            {
                _borderRadius = Mathf.Max(0, value);
                UpdateTrackAndFillElements();
            }
        }

        private OrientationType _orientation = OrientationType.Horizontal;
        public OrientationType Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation == value)
                {
                    return;
                }

                _orientation = value;
                UpdateFillContainerSize();
                UpdateTrackAndFillElements();
            }
        }

        private float _dashOnLength = 4f;
        public float DashOnLength
        {
            get => _dashOnLength;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _dashOnLength = Mathf.Max(1f, value);
                UpdateTrackAndFillElements();
            }
        }

        private float _dashOffLength = 4f;
        public float DashOffLength
        {
            get => _dashOffLength;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _dashOffLength = Mathf.Max(1f, value);
                UpdateTrackAndFillElements();
            }
        }

        private bool _animate = true;
        public bool Animate
        {
            get => _animate;
            set
            {
                if (_animate == value)
                {
                    return;
                }

                _animate = value;
                if (_animate)
                {
                    StartAnimationUpdate();
                }
                else
                {
                    StopAnimationUpdate();
                }
            }
        }

        private float _animationSpeed = 40f;
        public float AnimationSpeed
        {
            get => _animationSpeed;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _animationSpeed = value;
            }
        }

        private float _currentDashOffset;
        private IVisualElementScheduledItem _animationUpdateItem;
        private readonly List<Vector2> _pathPoints = new();
        private bool _pathDirty = true;
        private Rect _lastKnownRect = Rect.zero;

        public new class UxmlFactory : UxmlFactory<MarchingAntsProgressBar, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _progressAttribute = new()
            {
                name = "progress",
                defaultValue = 0.5f,
            };

            private readonly UxmlColorAttributeDescription _trackColorAttribute = new()
            {
                name = "track-color",
                defaultValue = new Color(0.4f, 0.4f, 0.4f, 1),
            };

            private readonly UxmlColorAttributeDescription _progressColorAttribute = new()
            {
                name = "progress-color",
                defaultValue = Color.white,
            };

            private readonly UxmlFloatAttributeDescription _thicknessAttribute = new()
            {
                name = "thickness",
                defaultValue = 3f,
            };

            private readonly UxmlFloatAttributeDescription _borderRadiusAttribute = new()
            {
                name = "border-radius",
                defaultValue = 5f,
            };

            private readonly UxmlEnumAttributeDescription<OrientationType> _orientationAttribute =
                new() { name = "orientation", defaultValue = OrientationType.Horizontal };

            private readonly UxmlFloatAttributeDescription _dashOnLengthAttribute = new()
            {
                name = "dash-on",
                defaultValue = 4f,
            };

            private readonly UxmlFloatAttributeDescription _dashOffLengthAttribute = new()
            {
                name = "dash-off",
                defaultValue = 4f,
            };

            private readonly UxmlBoolAttributeDescription _animateAttribute = new()
            {
                name = "animate",
                defaultValue = true,
            };

            private readonly UxmlFloatAttributeDescription _animationSpeedAttribute = new()
            {
                name = "animation-speed",
                defaultValue = 40f,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);

                if (ve is not MarchingAntsProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(MarchingAntsProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }
                bar.Thickness = _thicknessAttribute.GetValueFromBag(bag, cc);
                bar.BorderRadius = _borderRadiusAttribute.GetValueFromBag(bag, cc);
                bar.DashOnLength = _dashOnLengthAttribute.GetValueFromBag(bag, cc);
                bar.DashOffLength = _dashOffLengthAttribute.GetValueFromBag(bag, cc);
                bar.Orientation = _orientationAttribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.ProgressColor = _progressColorAttribute.GetValueFromBag(bag, cc);
                bar.AnimationSpeed = _animationSpeedAttribute.GetValueFromBag(bag, cc);
                bar.Animate = _animateAttribute.GetValueFromBag(bag, cc);
                bar.Progress = _progressAttribute.GetValueFromBag(bag, cc);
                if (
                    !bar.style.height.Equals(StyleKeyword.Initial)
                    && bar.style.height.value == 0
                    && bar.style.height.keyword == StyleKeyword.None
                )
                {
                    bar.style.height = 20;
                }
                if (
                    !bar.style.width.Equals(StyleKeyword.Initial)
                    && bar.style.width.value == 0
                    && bar.style.width.keyword == StyleKeyword.None
                )
                {
                    bar.style.width = 200;
                }

                bar._pathDirty = true;
                bar.schedule.Execute(() => bar.UpdateFillElementSize(bar.contentRect))
                    .ExecuteLater(0);
                bar.UpdateFillContainerSize();
            }
        }

        public MarchingAntsProgressBar()
        {
            AddToClassList(USSClassName);
            _trackElement = new VisualElement { name = "track", pickingMode = PickingMode.Ignore };
            _trackElement.AddToClassList(USSTrackClassName);
            _trackElement.style.position = Position.Absolute;
            _trackElement.style.left = 0;
            _trackElement.style.top = 0;
            _trackElement.style.width = Length.Percent(100);
            _trackElement.style.height = Length.Percent(100);
#if UNITY_2022_1_OR_NEWER
            _trackElement.generateVisualContent += DrawTrackOrFill;
#endif
            Add(_trackElement);
            _fillContainer = new VisualElement
            {
                name = "fill-container",
                pickingMode = PickingMode.Ignore,
            };
            _fillContainer.AddToClassList(USSFillContainerClassName);
            _fillContainer.style.overflow = Overflow.Hidden;
            _fillContainer.style.position = Position.Absolute;
            _fillContainer.style.left = 0;
            _fillContainer.style.top = 0;
            _fillContainer.style.width = Length.Percent(100);
            _fillContainer.style.height = Length.Percent(100);
            Add(_fillContainer);
            _fillElement = new VisualElement { name = "fill", pickingMode = PickingMode.Ignore };
            _fillElement.AddToClassList(USSFillClassName);
            _fillElement.style.position = Position.Absolute;
            _fillElement.style.left = 0;
            _fillElement.style.top = 0;
#if UNITY_2022_1_OR_NEWER
            _fillElement.generateVisualContent += DrawTrackOrFill;
#endif
            _fillContainer.Add(_fillElement);
            RegisterCallbacks();
            UpdateFillContainerSize();
        }

        private void RegisterCallbacks()
        {
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.newRect == evt.oldRect && evt.newRect == _lastKnownRect)
            {
                return;
            }

            if (_lastKnownRect != evt.newRect)
            {
                _pathDirty = true;
                _lastKnownRect = evt.newRect;
                UpdateFillElementSize(evt.newRect);
                UpdateTrackAndFillElements();
                UpdateFillContainerSize();
            }
        }

        private void UpdateFillElementSize(Rect trackRect)
        {
            if (_fillElement == null)
            {
                return;
            }
            _fillElement.style.width = trackRect.width;
            _fillElement.style.height = trackRect.height;
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_animate)
            {
                StartAnimationUpdate();
            }
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            StopAnimationUpdate();
        }

        private void StartAnimationUpdate()
        {
            if (!_animate || panel == null || _animationUpdateItem != null)
            {
                return;
            }

            _animationUpdateItem = schedule.Execute(UpdateAnimation).Every(16);
        }

        private void StopAnimationUpdate()
        {
            _animationUpdateItem?.Pause();
            _animationUpdateItem = null;
        }

        private void UpdateAnimation(TimerState ts)
        {
            if (!_animate || panel == null || _animationSpeed == 0f)
            {
                StopAnimationUpdate();
                return;
            }

            float totalPatternLength = _dashOnLength + _dashOffLength;
            if (totalPatternLength <= 0)
            {
                return;
            }

            _currentDashOffset += _animationSpeed * (ts.deltaTime / 1000f);
            _currentDashOffset =
                (_currentDashOffset % totalPatternLength + totalPatternLength) % totalPatternLength;
            _fillElement?.MarkDirtyRepaint();
        }

        private void UpdateTrackAndFillElements()
        {
            _pathDirty = true;
            _trackElement?.MarkDirtyRepaint();
            _fillElement?.MarkDirtyRepaint();
        }

        private void UpdateFillContainerSize()
        {
            if (_fillContainer == null)
            {
                return;
            }

            switch (_orientation)
            {
                case OrientationType.Horizontal:
                {
                    _fillContainer.style.width = Length.Percent(_progress * 100f);
                    _fillContainer.style.height = Length.Percent(100f);
                    _fillContainer.style.top = 0;
                    _fillContainer.style.bottom = StyleKeyword.Auto;
                    break;
                }
                case OrientationType.Vertical:
                {
                    _fillContainer.style.width = Length.Percent(100f);
                    _fillContainer.style.height = Length.Percent(_progress * 100f);
                    _fillContainer.style.top = StyleKeyword.Auto;
                    _fillContainer.style.bottom = 0;
                    break;
                }
                default:
                {
                    throw new InvalidEnumArgumentException(
                        nameof(_orientation),
                        (int)_orientation,
                        typeof(OrientationType)
                    );
                }
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSTrackColorVarName),
                    out Color tc
                )
            )
            {
                TrackColor = tc;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSProgressColorVarName),
                    out Color pc
                )
            )
            {
                ProgressColor = pc;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSThicknessVarName),
                    out float th
                )
            )
            {
                Thickness = th;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSBorderRadiusVarName),
                    out float br
                )
            )
            {
                BorderRadius = br;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSDashOnVarName),
                    out float don
                )
            )
            {
                DashOnLength = don;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSDashOffVarName),
                    out float doff
                )
            )
            {
                DashOffLength = doff;
            }
        }

#if UNITY_2022_1_OR_NEWER
        // Painter2D vector API requires Unity 2022.1+; on 2021.3 this element renders no custom content.
        private void DrawTrackOrFill(MeshGenerationContext mgc)
        {
            Painter2D painter = mgc.painter2D;
            VisualElement targetElement = mgc.visualElement;
            Rect rect = targetElement.contentRect;

            Color color;
            float dashOffset;
            bool isFillElement = targetElement == _fillElement;
            if (isFillElement)
            {
                color = _progressColor;
                dashOffset = _currentDashOffset;
            }
            else
            {
                color = _trackColor;
                dashOffset = 0f;
            }

            if (
                rect.width <= 0
                || rect.height <= 0
                || _thickness <= 0
                || _dashOnLength + _dashOffLength <= 0
            )
            {
                return;
            }

            if (_pathDirty || _pathPoints.Count == 0 || _lastKnownRect != contentRect)
            {
                _lastKnownRect = contentRect;
                if (_lastKnownRect is { width: > 0, height: > 0 })
                {
                    CalculatePathPoints(_lastKnownRect, _borderRadius);
                    _pathDirty = false;
                }
            }
            if (_pathPoints.Count < 2)
            {
                return;
            }

            painter.strokeColor = color;
            painter.lineWidth = _thickness;
            painter.lineCap = LineCap.Butt;
            float patternLength = _dashOnLength + _dashOffLength;
            bool isDrawingDash;
            float remainingInSegment;
            float wrappedOffset = (dashOffset % patternLength + patternLength) % patternLength;
            if (wrappedOffset >= _dashOnLength)
            {
                isDrawingDash = false;
                remainingInSegment = _dashOffLength - (wrappedOffset - _dashOnLength);
            }
            else
            {
                isDrawingDash = true;
                remainingInSegment = _dashOnLength - wrappedOffset;
            }

            if (Mathf.Approximately(remainingInSegment, 0))
            {
                isDrawingDash = !isDrawingDash;
                remainingInSegment = isDrawingDash ? _dashOnLength : _dashOffLength;
            }

            painter.BeginPath();
            for (int i = 0; i < _pathPoints.Count - 1; i++)
            {
                Vector2 p1 = _pathPoints[i];
                Vector2 p2 = _pathPoints[i + 1];
                Vector2 segmentVector = p2 - p1;
                float segmentLength = segmentVector.magnitude;
                if (Mathf.Approximately(segmentLength, 0))
                {
                    continue;
                }

                Vector2 segmentDir = segmentVector / segmentLength;
                float distanceCoveredOnSegment = 0f;
                while (distanceCoveredOnSegment < segmentLength)
                {
                    float lengthToProcess = Mathf.Min(
                        remainingInSegment,
                        segmentLength - distanceCoveredOnSegment
                    );

                    Vector2 currentDrawStart = p1 + segmentDir * distanceCoveredOnSegment;
                    Vector2 currentDrawEnd = currentDrawStart + segmentDir * lengthToProcess;
                    if (isDrawingDash)
                    {
                        painter.MoveTo(currentDrawStart);
                        painter.LineTo(currentDrawEnd);
                    }

                    distanceCoveredOnSegment += lengthToProcess;
                    remainingInSegment -= lengthToProcess;
                    if (Mathf.Approximately(remainingInSegment, 0))
                    {
                        isDrawingDash = !isDrawingDash;
                        remainingInSegment = isDrawingDash ? _dashOnLength : _dashOffLength;
                    }
                }
            }
            painter.Stroke();
        }
#endif

        private void CalculatePathPoints(Rect r, float borderRadius)
        {
            _pathPoints.Clear();
            const int segmentsPerCorner = 8;
            float radius = Mathf.Min(borderRadius, r.height / 2f, r.width / 2f);
            if (radius < 0.01f)
            {
                radius = 0;
            }

            Vector2 currentPoint = new(r.xMin + radius, r.yMin);
            _pathPoints.Add(currentPoint);
            currentPoint = new Vector2(r.xMax - radius, r.yMin);
            _pathPoints.Add(currentPoint);
            if (radius > 0)
            {
                AddArcPoints(
                    _pathPoints,
                    new Vector2(r.xMax - radius, r.yMin + radius),
                    radius,
                    270f,
                    90f,
                    segmentsPerCorner
                );
            }

            currentPoint = new Vector2(r.xMax, r.yMax - radius);
            _pathPoints.Add(currentPoint);
            if (radius > 0)
            {
                AddArcPoints(
                    _pathPoints,
                    new Vector2(r.xMax - radius, r.yMax - radius),
                    radius,
                    0f,
                    90f,
                    segmentsPerCorner
                );
            }

            currentPoint = new Vector2(r.xMin + radius, r.yMax);
            _pathPoints.Add(currentPoint);
            if (radius > 0)
            {
                AddArcPoints(
                    _pathPoints,
                    new Vector2(r.xMin + radius, r.yMax - radius),
                    radius,
                    90f,
                    90f,
                    segmentsPerCorner
                );
            }

            currentPoint = new Vector2(r.xMin, r.yMin + radius);
            _pathPoints.Add(currentPoint);
            if (radius > 0)
            {
                AddArcPoints(
                    _pathPoints,
                    new Vector2(r.xMin + radius, r.yMin + radius),
                    radius,
                    180f,
                    90f,
                    segmentsPerCorner
                );
            }

            if (_pathPoints.Count > 0)
            {
                _pathPoints.Add(_pathPoints[0]);
            }
        }

        private static void AddArcPoints(
            List<Vector2> points,
            Vector2 center,
            float radius,
            float startAngleDeg,
            float sweepAngleDeg,
            int segments
        )
        {
            float startRad = startAngleDeg * Mathf.Deg2Rad;
            float endRad = (startAngleDeg + sweepAngleDeg) * Mathf.Deg2Rad;
            float angleStep = (endRad - startRad) / Mathf.Max(1, segments);
            for (int i = 1; i <= segments; i++)
            {
                float currentRad = startRad + i * angleStep;
                points.Add(
                    new Vector2(
                        center.x + radius * Mathf.Cos(currentRad),
                        center.y + radius * Mathf.Sin(currentRad)
                    )
                );
            }
        }
    }
}
