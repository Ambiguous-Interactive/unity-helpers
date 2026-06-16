// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class LiquidProgressBar : VisualElement
    {
        public const string USSClassName = "liquid-progress-bar";
        public const string USSTrackColorVarName = "--lpb-track-color";
        public const string USSTrackThicknessVarName = "--lpb-track-thickness";
        public const string USSProgressColorVarName = "--lpb-progress-color";
        public const string USSBorderRadiusVarName = "--lpb-border-radius";

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
                _progress = Mathf.Clamp(value, 0.1095f, 1);
                MarkDirtyRepaint();
            }
        }

        private Color _trackColor = new(0.4f, 0.4f, 0.4f, 1f);
        public Color TrackColor
        {
            get => _trackColor;
            set
            {
                _trackColor = value;
                MarkDirtyRepaint();
            }
        }

        private float _trackThickness = 2f;
        public float TrackThickness
        {
            get => _trackThickness;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _trackThickness = Mathf.Max(0.01f, value);
                MarkDirtyRepaint();
            }
        }

        private Color _progressColor = new(0.3f, 0.7f, 1f, 1f);
        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                MarkDirtyRepaint();
            }
        }

        private float _borderRadius = 7f;
        public float BorderRadius
        {
            get => _borderRadius;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _borderRadius = Mathf.Max(0, value);
                MarkDirtyRepaint();
            }
        }

        private float _leadingEdgeCurvature = 0.6f;
        public float LeadingEdgeCurvature
        {
            get => _leadingEdgeCurvature;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _leadingEdgeCurvature = Mathf.Clamp01(value);
                MarkDirtyRepaint();
            }
        }

        private float _animationSpeed = 2.5f;
        public float AnimationSpeed
        {
            get => _animationSpeed;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _animationSpeed = Mathf.Max(0, value);
            }
        }

        private float _wobbleMagnitude = 0.3f;
        public float WobbleMagnitude
        {
            get => _wobbleMagnitude;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _wobbleMagnitude = Mathf.Clamp(value, 0f, 1f);
            }
        }

        private bool _animateLeadingEdge = true;
        public bool AnimateLeadingEdge
        {
            get => _animateLeadingEdge;
            set
            {
                if (_animateLeadingEdge == value)
                {
                    return;
                }

                _animateLeadingEdge = value;
                if (_animateLeadingEdge)
                {
                    if (panel != null)
                    {
                        StartAnimationUpdate();
                    }
                }
                else
                {
                    StopAnimationUpdate();
                    _wobbleOffset = 0;
                }
                MarkDirtyRepaint();
            }
        }

        private float _wobbleOffset;
        private IVisualElementScheduledItem _animationUpdateItem;

        public new class UxmlFactory : UxmlFactory<LiquidProgressBar, UxmlTraits> { }

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

            private readonly UxmlFloatAttributeDescription _trackThicknessAttribute = new()
            {
                name = "track-thickness",
                defaultValue = 2f,
            };

            private readonly UxmlColorAttributeDescription _progressColorAttribute = new()
            {
                name = "progress-color",
                defaultValue = new Color(0.3f, 0.7f, 1, 1),
            };

            private readonly UxmlFloatAttributeDescription _borderRadiusAttribute = new()
            {
                name = "border-radius",
                defaultValue = 7f,
            };

            private readonly UxmlFloatAttributeDescription _leadingEdgeCurvatureAttribute = new()
            {
                name = "leading-edge-curvature",
                defaultValue = 0.6f,
            };

            private readonly UxmlFloatAttributeDescription _animationSpeedAttribute = new()
            {
                name = "animation-speed",
                defaultValue = 2.5f,
            };

            private readonly UxmlFloatAttributeDescription _wobbleMagnitudeAttribute = new()
            {
                name = "wobble-magnitude",
                defaultValue = 0.3f,
            };

            private readonly UxmlBoolAttributeDescription _animateLeadingEdgeAttribute = new()
            {
                name = "animate-leading-edge",
                defaultValue = true,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);

                if (ve is not LiquidProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(LiquidProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }

                bar.Progress = _progressAttribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.TrackThickness = _trackThicknessAttribute.GetValueFromBag(bag, cc);
                bar.ProgressColor = _progressColorAttribute.GetValueFromBag(bag, cc);
                bar.BorderRadius = _borderRadiusAttribute.GetValueFromBag(bag, cc);
                bar.LeadingEdgeCurvature = _leadingEdgeCurvatureAttribute.GetValueFromBag(bag, cc);
                bar.AnimationSpeed = _animationSpeedAttribute.GetValueFromBag(bag, cc);
                bar.WobbleMagnitude = _wobbleMagnitudeAttribute.GetValueFromBag(bag, cc);
                bar.AnimateLeadingEdge = _animateLeadingEdgeAttribute.GetValueFromBag(bag, cc);

                if (bar.style.height.keyword == StyleKeyword.Auto || bar.style.height.value == 0)
                {
                    bar.style.height = 22;
                }

                if (bar.style.width.keyword == StyleKeyword.Auto || bar.style.width.value == 0)
                {
                    bar.style.width = 200;
                }
            }
        }

        public LiquidProgressBar()
        {
            AddToClassList(USSClassName);
#if UNITY_2022_1_OR_NEWER
            generateVisualContent += OnGenerateVisualContent;
#endif
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_animateLeadingEdge)
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
            if (!_animateLeadingEdge || panel == null || _animationUpdateItem != null)
            {
                return;
            }

            _animationUpdateItem = schedule.Execute(UpdateAnimation).Every(33);
        }

        private void StopAnimationUpdate()
        {
            _animationUpdateItem?.Pause();
            _animationUpdateItem = null;
        }

        private void UpdateAnimation(TimerState ts)
        {
            if (!_animateLeadingEdge || panel == null)
            {
                StopAnimationUpdate();
                _wobbleOffset = 0;
                MarkDirtyRepaint();
                return;
            }
            _wobbleOffset =
                Mathf.Sin(Time.realtimeSinceStartup * _animationSpeed * 4f) * _wobbleMagnitude;
            MarkDirtyRepaint();
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSTrackColorVarName),
                    out Color c
                )
            )
            {
                TrackColor = c;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSTrackThicknessVarName),
                    out float tt
                )
            )
            {
                TrackThickness = tt;
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
                    new CustomStyleProperty<float>(USSBorderRadiusVarName),
                    out float br
                )
            )
            {
                BorderRadius = br;
            }
        }

#if UNITY_2022_1_OR_NEWER
        // Painter2D vector API requires Unity 2022.1+; on 2021.3 this element renders no custom content.
        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            Painter2D painter = mgc.painter2D;
            Rect r = contentRect;

            if (r.width <= 0 || r.height <= 0)
            {
                return;
            }

            float barHeight = r.height;
            float halfHeight = barHeight / 2f;

            float outerRadius = Mathf.Min(_borderRadius, halfHeight, r.width / 2f);
            if (Mathf.Approximately(outerRadius, 0))
            {
                outerRadius = 0;
            }

            float trackThickness = _trackThickness;
            if (Mathf.Approximately(trackThickness, 0))
            {
                trackThickness = 0;
            }

            float halfTrackThickness = trackThickness / 2f;
            Rect fillRect = new(
                r.xMin + halfTrackThickness,
                r.yMin + halfTrackThickness,
                r.width - trackThickness,
                r.height - trackThickness
            );

            bool fillRectIsValid = fillRect is { width: > 0, height: > 0 };
            float innerRadius = Mathf.Max(0, outerRadius - halfTrackThickness);

            if (!Mathf.Approximately(trackThickness, 0))
            {
                painter.strokeColor = _trackColor;
                painter.fillColor = Color.clear;
                painter.lineWidth = trackThickness;
                painter.lineCap = LineCap.Butt;
                painter.lineJoin = LineJoin.Miter;

                painter.BeginPath();
                painter.MoveTo(new Vector2(r.xMin + outerRadius, r.yMin));
                painter.LineTo(new Vector2(r.xMax - outerRadius, r.yMin));
                if (outerRadius > 0)
                {
                    painter.Arc(
                        new Vector2(r.xMax - outerRadius, r.yMin + outerRadius),
                        outerRadius,
                        270f,
                        90f
                    );
                }

                painter.LineTo(new Vector2(r.xMax, r.yMax - outerRadius));
                if (outerRadius > 0)
                {
                    painter.Arc(
                        new Vector2(r.xMax - outerRadius, r.yMax - outerRadius),
                        outerRadius,
                        0f,
                        90f
                    );
                }

                painter.LineTo(new Vector2(r.xMin + outerRadius, r.yMax));
                if (outerRadius > 0)
                {
                    painter.Arc(
                        new Vector2(r.xMin + outerRadius, r.yMax - outerRadius),
                        outerRadius,
                        90f,
                        90f
                    );
                }

                painter.LineTo(new Vector2(r.xMin, r.yMin + outerRadius));
                if (outerRadius > 0)
                {
                    painter.Arc(
                        new Vector2(r.xMin + outerRadius, r.yMin + outerRadius),
                        outerRadius,
                        180f,
                        90f
                    );
                }
                painter.Stroke();
            }

            if (
                !fillRectIsValid
                || (
                    Mathf.Approximately(_progress, 0)
                    && Mathf.Approximately(_wobbleOffset, 0)
                    && Mathf.Approximately(_wobbleMagnitude, 0)
                )
            )
            {
                return;
            }

            painter.fillColor = _progressColor;
            float baseFillWidth = fillRect.width * _progress;
            float wobbleExtension = _wobbleOffset * fillRect.height * 0.3f;
            float currentTotalFillWidth = baseFillWidth + wobbleExtension;
            currentTotalFillWidth = Mathf.Clamp(currentTotalFillWidth, 0, fillRect.width);

            if (currentTotalFillWidth < 0.5f)
            {
                return;
            }

            painter.BeginPath();

            float capRadius = fillRect.height / 2f;
            if (Mathf.Approximately(capRadius, 0))
            {
                capRadius = 0;
            }

            bool drawPillShape = currentTotalFillWidth < fillRect.height && _progress < 0.99f;

            if (drawPillShape)
            {
                float pillActualRadius = Mathf.Min(currentTotalFillWidth / 2.0f, capRadius);
                if (Mathf.Approximately(pillActualRadius, 0))
                {
                    pillActualRadius = 0;
                }

                float straightSegmentWidth = Mathf.Max(
                    0,
                    currentTotalFillWidth - 2 * pillActualRadius
                );

                painter.MoveTo(new Vector2(fillRect.xMin + pillActualRadius, fillRect.yMin));
                if (straightSegmentWidth > 0)
                {
                    painter.LineTo(
                        new Vector2(
                            fillRect.xMin + pillActualRadius + straightSegmentWidth,
                            fillRect.yMin
                        )
                    );
                }

                if (pillActualRadius > 0)
                {
                    painter.Arc(
                        new Vector2(
                            fillRect.xMin + pillActualRadius + straightSegmentWidth,
                            fillRect.yMin + pillActualRadius
                        ),
                        pillActualRadius,
                        270f,
                        180f
                    );
                }
                else
                {
                    painter.LineTo(
                        new Vector2(fillRect.xMin + currentTotalFillWidth, fillRect.yMax)
                    );
                }

                if (straightSegmentWidth > 0)
                {
                    painter.LineTo(new Vector2(fillRect.xMin + pillActualRadius, fillRect.yMax));
                }

                if (pillActualRadius > 0)
                {
                    painter.Arc(
                        new Vector2(
                            fillRect.xMin + pillActualRadius,
                            fillRect.yMin + pillActualRadius
                        ),
                        pillActualRadius,
                        90f,
                        180f
                    );
                }
            }
            else
            {
                float animatedPeakX = fillRect.xMin + currentTotalFillWidth;
                painter.MoveTo(new Vector2(fillRect.xMin + innerRadius, fillRect.yMin));

                bool useLiquidEdge =
                    _progress < 0.995f
                    && _leadingEdgeCurvature > 0.01f
                    && animatedPeakX < fillRect.xMax - innerRadius - 0.1f
                    && currentTotalFillWidth > innerRadius * 2.1f;

                if (useLiquidEdge)
                {
                    float curveStartBaseX =
                        animatedPeakX - fillRect.height * _leadingEdgeCurvature * 0.4f;
                    curveStartBaseX = Mathf.Max(fillRect.xMin + innerRadius, curveStartBaseX);
                    painter.LineTo(new Vector2(curveStartBaseX, fillRect.yMin));

                    Vector2 peak = new(animatedPeakX, fillRect.yMin + fillRect.height / 2f);
                    float controlPointBulgeXFactor = fillRect.height * _leadingEdgeCurvature * 0.5f;
                    Vector2 cp1Top = new(
                        curveStartBaseX + (peak.x - curveStartBaseX) * 0.35f,
                        fillRect.yMin
                    );
                    Vector2 cp2Top = new(
                        peak.x + controlPointBulgeXFactor,
                        peak.y - fillRect.height * 0.25f
                    );
                    painter.BezierCurveTo(cp1Top, cp2Top, peak);

                    Vector2 cp1Bottom = new(
                        peak.x + controlPointBulgeXFactor,
                        peak.y + fillRect.height * 0.25f
                    );
                    Vector2 cp2Bottom = new(
                        curveStartBaseX + (peak.x - curveStartBaseX) * 0.35f,
                        fillRect.yMax
                    );
                    painter.BezierCurveTo(
                        cp1Bottom,
                        cp2Bottom,
                        new Vector2(curveStartBaseX, fillRect.yMax)
                    );
                }
                else
                {
                    painter.LineTo(new Vector2(animatedPeakX - innerRadius, fillRect.yMin));
                    if (innerRadius > 0)
                    {
                        painter.Arc(
                            new Vector2(animatedPeakX - innerRadius, fillRect.yMin + innerRadius),
                            innerRadius,
                            270f,
                            90f
                        );
                    }
                    else
                    {
                        painter.LineTo(new Vector2(animatedPeakX, fillRect.yMin));
                    }

                    painter.LineTo(new Vector2(animatedPeakX, fillRect.yMax - innerRadius));
                    if (innerRadius > 0)
                    {
                        painter.Arc(
                            new Vector2(animatedPeakX - innerRadius, fillRect.yMax - innerRadius),
                            innerRadius,
                            0f,
                            90f
                        );
                    }
                    else
                    {
                        painter.LineTo(new Vector2(animatedPeakX, fillRect.yMax));
                    }
                }

                painter.LineTo(new Vector2(fillRect.xMin + innerRadius, fillRect.yMax));
                if (innerRadius > 0)
                {
                    painter.Arc(
                        new Vector2(fillRect.xMin + innerRadius, fillRect.yMax - innerRadius),
                        innerRadius,
                        90f,
                        90f
                    );
                }

                painter.LineTo(new Vector2(fillRect.xMin, fillRect.yMin + innerRadius));
                if (innerRadius > 0)
                {
                    painter.Arc(
                        new Vector2(fillRect.xMin + innerRadius, fillRect.yMin + innerRadius),
                        innerRadius,
                        180f,
                        90f
                    );
                }
            }

            painter.ClosePath();
            painter.Fill();
        }
#endif
    }
}
