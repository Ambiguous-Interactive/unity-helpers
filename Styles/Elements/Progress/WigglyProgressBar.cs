// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using System.ComponentModel;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class WigglyProgressBar : VisualElement
    {
        public const string USSClassName = "wiggly-progress-bar";
        public const string USSTrackColorVarName = "--wiggly-track-color";
        public const string USSProgressColorVarName = "--wiggly-progress-color";
        public const string USSThicknessVarName = "--wiggly-thickness";

        public enum FillDirection
        {
            Forward = 0,
            Backward = 1,
        }

        public enum OrientationType
        {
            Horizontal = 0,
            Vertical = 1,
        }

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
                float newProgress = Mathf.Clamp01(value);
                if (!Mathf.Approximately(_progress, newProgress))
                {
                    _progress = newProgress;
                    MarkDirtyRepaint();
                }
            }
        }

        private float _amplitude = 10f;

        public float Amplitude
        {
            get => _amplitude;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _amplitude = Mathf.Max(0f, value);
                UpdateSize();
                MarkDirtyRepaint();
            }
        }

        private float _wavelength = 50f;

        public float Wavelength
        {
            get => _wavelength;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _wavelength = Mathf.Max(1f, value);
                MarkDirtyRepaint();
            }
        }

        private float _thickness = 5f;

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
                UpdateSize();
                MarkDirtyRepaint();
            }
        }

        private bool _roundedCaps = true;

        public bool RoundedCaps
        {
            get => _roundedCaps;
            set
            {
                _roundedCaps = value;
                MarkDirtyRepaint();
            }
        }

        private FillDirection _fillDirection = FillDirection.Forward;

        public FillDirection Direction
        {
            get => _fillDirection;
            set
            {
                _fillDirection = value;
                MarkDirtyRepaint();
            }
        }

        private Color _trackColor = Color.gray;

        public Color TrackColor
        {
            get => _trackColor;
            set
            {
                _trackColor = value;
                MarkDirtyRepaint();
            }
        }

        private Color _progressColor = new(0.2f, 0.6f, 1f);

        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                MarkDirtyRepaint();
            }
        }

        private int _segmentsPerWavelength = 20;

        public int SegmentsPerWavelength
        {
            get => _segmentsPerWavelength;
            set
            {
                _segmentsPerWavelength = Mathf.Max(4, value);
                MarkDirtyRepaint();
            }
        }

        private bool _animatePhase;

        public bool AnimatePhase
        {
            get => _animatePhase;
            set
            {
                if (_animatePhase == value)
                {
                    return;
                }

                _animatePhase = value;
                if (_animatePhase && panel != null)
                {
                    StartAnimationUpdate();
                }
                else
                {
                    StopAnimationUpdate();
                }

                MarkDirtyRepaint();
            }
        }

        private float _phaseSpeed = Mathf.PI;

        public float PhaseSpeed
        {
            get => _phaseSpeed;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _phaseSpeed = value;
            }
        }

        private bool _animateWithProgress;

        public bool AnimateWithProgress
        {
            get => _animateWithProgress;
            set
            {
                if (_animateWithProgress == value)
                {
                    return;
                }

                _animateWithProgress = value;
                MarkDirtyRepaint();
            }
        }

        private float _progressPhaseFactor = 1.0f;

        public float ProgressPhaseFactor
        {
            get => _progressPhaseFactor;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _progressPhaseFactor = value;
                MarkDirtyRepaint();
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
                UpdateSizeAndOrientation();
                MarkDirtyRepaint();
            }
        }

        private float _arcRadius;

        public float ArcRadius
        {
            get => _arcRadius;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _arcRadius = Mathf.Max(0, value);
                UpdateSizeAndOrientation();
                MarkDirtyRepaint();
            }
        }

        private bool _arcBottom = true;

        public bool ArcBottom
        {
            get => _arcBottom;
            set
            {
                if (_arcBottom == value)
                {
                    return;
                }

                _arcBottom = value;
                MarkDirtyRepaint();
            }
        }

        private float _length = 200f;

        public float Length
        {
            get => _length;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _length = Mathf.Max(0, value);
                UpdateSizeAndOrientation();
                MarkDirtyRepaint();
            }
        }
        private float _timeBasedPhaseOffset;
        private IVisualElementScheduledItem _animationUpdateItem;

        public new class UxmlFactory : UxmlFactory<WigglyProgressBar, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _progressAttribute = new()
            {
                name = "progress",
                defaultValue = 0.5f,
            };

            private readonly UxmlFloatAttributeDescription _amplitudeAttribute = new()
            {
                name = "amplitude",
                defaultValue = 10f,
            };

            private readonly UxmlFloatAttributeDescription _wavelengthAttribute = new()
            {
                name = "wavelength",
                defaultValue = 50f,
            };

            private readonly UxmlFloatAttributeDescription _thicknessAttribute = new()
            {
                name = "thickness",
                defaultValue = 5f,
            };

            private readonly UxmlBoolAttributeDescription _roundedCapsAttribute = new()
            {
                name = "rounded-caps",
                defaultValue = true,
            };

            private readonly UxmlEnumAttributeDescription<FillDirection> _fillDirectionAttribute =
                new() { name = "fill-direction", defaultValue = FillDirection.Forward };

            private readonly UxmlColorAttributeDescription _trackColorAttribute = new()
            {
                name = "track-color-attr",
                defaultValue = Color.gray,
            };

            private readonly UxmlColorAttributeDescription _progressColorAttribute = new()
            {
                name = "progress-color-attr",
                defaultValue = new Color(0.2f, 0.6f, 1.0f, 1.0f),
            };

            private readonly UxmlIntAttributeDescription _segmentsPerWavelengthAttribute = new()
            {
                name = "segments-per-wavelength",
                defaultValue = 20,
            };

            private readonly UxmlBoolAttributeDescription _animatePhaseAttribute = new()
            {
                name = "animate-phase",
                defaultValue = false,
            };

            private readonly UxmlFloatAttributeDescription _phaseSpeedAttribute = new()
            {
                name = "phase-speed",
                defaultValue = Mathf.PI,
            };

            private readonly UxmlBoolAttributeDescription _animateWithProgressAttribute = new()
            {
                name = "animate-with-progress",
                defaultValue = false,
            };

            private readonly UxmlFloatAttributeDescription _progressPhaseFactorAttribute = new()
            {
                name = "progress-phase-factor",
                defaultValue = 1.0f,
            };

            private readonly UxmlEnumAttributeDescription<OrientationType> _orientationAttribute =
                new() { name = "orientation", defaultValue = OrientationType.Horizontal };

            private readonly UxmlFloatAttributeDescription _arcRadiusAttribute = new()
            {
                name = "arc-radius",
                defaultValue = 0f,
            };

            private readonly UxmlBoolAttributeDescription _arcBottomAttribute = new()
            {
                name = "arc-bottom",
                defaultValue = true,
            };

            private readonly UxmlFloatAttributeDescription _lengthAttribute = new()
            {
                name = "length",
                defaultValue = 200f,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is not WigglyProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(WigglyProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }

                bar.Amplitude = _amplitudeAttribute.GetValueFromBag(bag, cc);
                bar.Thickness = _thicknessAttribute.GetValueFromBag(bag, cc);
                bar.Progress = _progressAttribute.GetValueFromBag(bag, cc);
                bar.Wavelength = _wavelengthAttribute.GetValueFromBag(bag, cc);
                bar.RoundedCaps = _roundedCapsAttribute.GetValueFromBag(bag, cc);
                bar.Direction = _fillDirectionAttribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.ProgressColor = _progressColorAttribute.GetValueFromBag(bag, cc);
                bar.SegmentsPerWavelength = _segmentsPerWavelengthAttribute.GetValueFromBag(
                    bag,
                    cc
                );
                bar.PhaseSpeed = _phaseSpeedAttribute.GetValueFromBag(bag, cc);
                bar.ProgressPhaseFactor = _progressPhaseFactorAttribute.GetValueFromBag(bag, cc);
                bar.AnimateWithProgress = _animateWithProgressAttribute.GetValueFromBag(bag, cc);
                bar.AnimatePhase = _animatePhaseAttribute.GetValueFromBag(bag, cc);
                bar.ArcBottom = _arcBottomAttribute.GetValueFromBag(bag, cc);
                bar.Orientation = _orientationAttribute.GetValueFromBag(bag, cc);
                bar.ArcRadius = _arcRadiusAttribute.GetValueFromBag(bag, cc);
                bar.ArcBottom = _arcBottomAttribute.GetValueFromBag(bag, cc);
                bar.Length = _lengthAttribute.GetValueFromBag(bag, cc);

                bar.UpdateSizeAndOrientation();
            }
        }

        public WigglyProgressBar()
        {
            AddToClassList(USSClassName);
#if UNITY_2022_1_OR_NEWER
            generateVisualContent += OnGenerateVisualContent;
#endif
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            UpdateSizeAndOrientation();
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_animatePhase)
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
            if (panel == null || _animationUpdateItem != null)
            {
                return;
            }

            _animationUpdateItem = schedule.Execute(UpdateAnimationState).Every(16);
        }

        private void StopAnimationUpdate()
        {
            _animationUpdateItem?.Pause();
            _animationUpdateItem = null;
        }

        private void UpdateAnimationState(TimerState ts)
        {
            if (!_animatePhase || _phaseSpeed == 0f)
            {
                return;
            }

            _timeBasedPhaseOffset += _phaseSpeed * (ts.deltaTime / 1000f);
            _timeBasedPhaseOffset %= 2f * Mathf.PI;
            MarkDirtyRepaint();
        }

        private void UpdateSize()
        {
            float requiredHeight = _amplitude * 2f + _thickness;
            style.height = requiredHeight;
            style.minHeight = requiredHeight;
        }

        private void UpdateSizeAndOrientation()
        {
            float perpendicularWaveHeight = _amplitude * 2f + _thickness;

            float finalPrimaryDimension = _length;
            float finalPerpendicularDimension = perpendicularWaveHeight;

            if (
                !Mathf.Approximately(_arcRadius, 0)
                && !float.IsInfinity(_arcRadius)
                && !float.IsNaN(_arcRadius)
                && _length > 0
            )
            {
                float radius = Mathf.Max(_arcRadius, 0.01f);
                float totalAngleRadians = _length / radius;
                if (totalAngleRadians > 0)
                {
                    float chordLength = 2 * radius * Mathf.Sin(totalAngleRadians / 2f);
                    float sagitta = radius * (1 - Mathf.Cos(totalAngleRadians / 2f));
                    if (!float.IsNaN(chordLength) && !float.IsNaN(sagitta))
                    {
                        finalPrimaryDimension = chordLength;
                        finalPerpendicularDimension = perpendicularWaveHeight + sagitta;
                    }
                }
            }

            switch (_orientation)
            {
                case OrientationType.Horizontal:
                {
                    style.width = finalPrimaryDimension;
                    style.minWidth = finalPrimaryDimension;
                    style.height = finalPerpendicularDimension;
                    style.minHeight = finalPerpendicularDimension;
                    break;
                }
                case OrientationType.Vertical:
                {
                    style.height = finalPrimaryDimension;
                    style.minHeight = finalPrimaryDimension;
                    style.width = finalPerpendicularDimension;
                    style.minWidth = finalPerpendicularDimension;
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
                    out Color trackCol
                )
            )
            {
                _trackColor = trackCol;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSProgressColorVarName),
                    out Color progressCol
                )
            )
            {
                _progressColor = progressCol;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSThicknessVarName),
                    out float thickVal
                )
            )
            {
                Thickness = thickVal;
            }

            MarkDirtyRepaint();
        }

#if UNITY_2022_1_OR_NEWER
        // Painter2D vector API requires Unity 2022.1+; on 2021.3 this element renders no custom content.
        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Painter2D painter = mgc.painter2D;
            Rect rect = contentRect;

            if (rect.width <= 0 || rect.height <= 0 || _thickness <= 0 || _wavelength <= 0)
            {
                return;
            }

            painter.lineWidth = _thickness;
            painter.lineCap = _roundedCaps ? LineCap.Round : LineCap.Butt;

            float totalPhaseOffset = 0f;
            if (_animatePhase)
            {
                if (_animateWithProgress)
                {
                    totalPhaseOffset += _progress * _progressPhaseFactor * 2f * Mathf.PI;
                }
                else
                {
                    totalPhaseOffset += _timeBasedPhaseOffset;
                }
            }

            float trackLength = _length;
            painter.strokeColor = _trackColor;
            DrawWavePath(painter, rect, 0f, trackLength, totalPhaseOffset, trackLength);

            if (!Mathf.Approximately(_progress, 0f))
            {
                painter.strokeColor = _progressColor;
                float fillLength = trackLength * _progress;

                switch (_fillDirection)
                {
                    case FillDirection.Forward:
                    {
                        DrawWavePath(painter, rect, 0f, fillLength, totalPhaseOffset, trackLength);
                        break;
                    }
                    case FillDirection.Backward:
                    {
                        float startDist = trackLength - fillLength;
                        DrawWavePath(
                            painter,
                            rect,
                            startDist,
                            trackLength,
                            totalPhaseOffset,
                            trackLength
                        );
                        break;
                    }
                    default:
                    {
                        throw new InvalidEnumArgumentException(
                            nameof(_fillDirection),
                            (int)_fillDirection,
                            typeof(FillDirection)
                        );
                    }
                }
            }
        }

        private void DrawWavePath(
            Painter2D painter,
            Rect elementRect,
            float startArcDistance,
            float endArcDistance,
            float phaseOffset,
            float totalTrackArcLength
        )
        {
            if (
                Mathf.Approximately(startArcDistance, endArcDistance)
                || endArcDistance < startArcDistance
                || totalTrackArcLength <= 0
            )
            {
                return;
            }

            painter.BeginPath();
            bool firstPoint = true;

            float pathSegmentArcLength = endArcDistance - startArcDistance;
            int totalSegments = Mathf.Max(
                2,
                Mathf.CeilToInt(pathSegmentArcLength / _wavelength * _segmentsPerWavelength)
            );
            float dArcDistance = pathSegmentArcLength / totalSegments;

            Vector2 arcCenter = Vector2.zero;
            float radius = Mathf.Max(_arcRadius, 0.01f);
            bool isStraight =
                Mathf.Approximately(_arcRadius, 0)
                || float.IsInfinity(radius)
                || float.IsNaN(radius);

            if (!isStraight)
            {
                switch (_orientation)
                {
                    case OrientationType.Horizontal:
                    {
                        float waveBaselineY;
                        if (!_arcBottom)
                        {
                            waveBaselineY = elementRect.yMin + _amplitude + _thickness / 2f;
                            arcCenter = new Vector2(elementRect.center.x, waveBaselineY + radius);
                        }
                        else
                        {
                            waveBaselineY = elementRect.yMax - (_amplitude + _thickness / 2f);
                            arcCenter = new Vector2(elementRect.center.x, waveBaselineY - radius);
                        }

                        break;
                    }
                    case OrientationType.Vertical:
                    {
                        float waveBaselineX;
                        if (!_arcBottom)
                        {
                            waveBaselineX = elementRect.xMin + _amplitude + _thickness / 2f;
                            arcCenter = new Vector2(waveBaselineX + radius, elementRect.center.y);
                        }
                        else
                        {
                            waveBaselineX = elementRect.xMax - (_amplitude + _thickness / 2f);
                            arcCenter = new Vector2(waveBaselineX - radius, elementRect.center.y);
                        }

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

            for (int i = 0; i <= totalSegments; i++)
            {
                float currentArcDist = startArcDistance + i * dArcDistance;
                currentArcDist = Mathf.Clamp(currentArcDist, startArcDistance, endArcDistance);

                Vector2 basePoint;
                Vector2 normalDirection;

                if (isStraight)
                {
                    switch (_orientation)
                    {
                        case OrientationType.Horizontal:
                        {
                            float baselineY = elementRect.yMin + _amplitude + _thickness / 2f;
                            basePoint = new Vector2(elementRect.xMin + currentArcDist, baselineY);
                            normalDirection = Vector2.up;
                            break;
                        }
                        case OrientationType.Vertical:
                        {
                            float baselineX = elementRect.xMin + _amplitude + _thickness / 2f;
                            basePoint = new Vector2(baselineX, elementRect.yMin + currentArcDist);
                            normalDirection = Vector2.left;
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
                else
                {
                    float totalAngleOfTrackRad = totalTrackArcLength / radius;
                    float startAngleOffsetRad = -totalAngleOfTrackRad / 2f;
                    float angleForCurrentPointRad = currentArcDist / radius;
                    float globalAngleRad = startAngleOffsetRad + angleForCurrentPointRad;

                    switch (_orientation)
                    {
                        case OrientationType.Horizontal:
                        {
                            basePoint.x = arcCenter.x + radius * Mathf.Sin(globalAngleRad);
                            basePoint.y =
                                arcCenter.y
                                + radius * Mathf.Cos(globalAngleRad) * (_arcBottom ? 1f : -1f);
                            break;
                        }
                        case OrientationType.Vertical:
                        {
                            basePoint.x =
                                arcCenter.x
                                + radius * Mathf.Cos(globalAngleRad) * (_arcBottom ? 1f : -1f);
                            basePoint.y = arcCenter.y + radius * Mathf.Sin(globalAngleRad);
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

                    normalDirection = (basePoint - arcCenter).normalized;
                    if (_arcBottom)
                    {
                        normalDirection *= -1f;
                    }
                }

                float waveAngle = currentArcDist / _wavelength * 2f * Mathf.PI + phaseOffset;
                float waveOffsetMagnitude = _amplitude * Mathf.Sin(waveAngle);
                Vector2 finalPoint = basePoint + normalDirection * waveOffsetMagnitude;

                if (firstPoint)
                {
                    painter.MoveTo(finalPoint);
                    firstPoint = false;
                }
                else
                {
                    painter.LineTo(finalPoint);
                }

                if (currentArcDist >= endArcDistance - 0.001f && i < totalSegments)
                {
                    break;
                }
            }
            painter.Stroke();
        }
#endif
    }
}
