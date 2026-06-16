// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using Core.Random;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class GlitchProgressBar : VisualElement
    {
        public const string USSClassName = "glitch-progress-bar";
        public const string USSNormalColorVarName = "--gpb-normal-color";
        public const string USSGlitchColor1VarName = "--gpb-glitch-color1";
        public const string USSGlitchColor2VarName = "--gpb-glitch-color2";
        public const string USSTrackColorVarName = "--gpb-track-color";

        private float _progress = 0.5f;
        private float _visualProgress;
        public float Progress
        {
            get => _progress;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _targetProgress = Mathf.Clamp01(value);
                float newTarget = Mathf.Clamp01(value);
                if (Mathf.Approximately(_targetProgress, newTarget))
                {
                    return;
                }

                _targetProgress = newTarget;
                if (panel != null && _animationUpdateItem == null)
                {
                    StartAnimationUpdate();
                }
            }
        }

        private Color _normalColor = Color.green;
        public Color NormalColor
        {
            get => _normalColor;
            set
            {
                _normalColor = value;
                MarkDirtyRepaint();
            }
        }

        private Color _glitchColor1 = Color.red;
        public Color GlitchColor1
        {
            get => _glitchColor1;
            set
            {
                _glitchColor1 = value;
                MarkDirtyRepaint();
            }
        }

        private Color _glitchColor2 = Color.blue;
        public Color GlitchColor2
        {
            get => _glitchColor2;
            set
            {
                _glitchColor2 = value;
                MarkDirtyRepaint();
            }
        }

        private Color _trackColor = Color.black;
        public Color TrackColor
        {
            get => _trackColor;
            set
            {
                _trackColor = value;
                MarkDirtyRepaint();
            }
        }

        public float glitchIntensity = 0.1f;
        public float glitchFrequency = 0.15f;
        public int glitchDurationFrames = 3;
        public float progressAnimationSpeed = 5f;

        private bool _isGlitching;
        private int _glitchTimer;
        private float _targetProgress;
        private IVisualElementScheduledItem _animationUpdateItem;

        private readonly IRandom _random;

        public new class UxmlFactory : UxmlFactory<GlitchProgressBar, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _progressAttribute = new()
            {
                name = "progress",
                defaultValue = 0.5f,
            };

            private readonly UxmlColorAttributeDescription _normalColorAttribute = new()
            {
                name = "normal-color",
                defaultValue = Color.green,
            };

            private readonly UxmlColorAttributeDescription _glitchColor1Attribute = new()
            {
                name = "glitch-color1",
                defaultValue = Color.red,
            };

            private readonly UxmlColorAttributeDescription _glitchColor2Attribute = new()
            {
                name = "glitch-color2",
                defaultValue = Color.blue,
            };

            private readonly UxmlColorAttributeDescription _trackColorAttribute = new()
            {
                name = "track-color",
                defaultValue = Color.black,
            };

            private readonly UxmlFloatAttributeDescription _glitchIntensityAttribute = new()
            {
                name = "glitch-intensity",
                defaultValue = 0.1f,
            };

            private readonly UxmlFloatAttributeDescription _glitchFrequencyAttribute = new()
            {
                name = "glitch-frequency",
                defaultValue = 0.2f,
            };

            private readonly UxmlIntAttributeDescription _glitchDurationFramesAttribute = new()
            {
                name = "glitch-duration-frames",
                defaultValue = 3,
            };

            private readonly UxmlFloatAttributeDescription _progressAnimationSpeedAttribute = new()
            {
                name = "progress-animation-speed",
                defaultValue = 5f,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is not GlitchProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(GlitchProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }

                bar._targetProgress = _progressAttribute.GetValueFromBag(bag, cc);
                bar._visualProgress = bar._targetProgress;
                bar._progress = bar._targetProgress;

                bar.NormalColor = _normalColorAttribute.GetValueFromBag(bag, cc);
                bar.GlitchColor1 = _glitchColor1Attribute.GetValueFromBag(bag, cc);
                bar.GlitchColor2 = _glitchColor2Attribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.glitchIntensity = _glitchIntensityAttribute.GetValueFromBag(bag, cc);
                bar.glitchFrequency = _glitchFrequencyAttribute.GetValueFromBag(bag, cc);
                bar.glitchDurationFrames = _glitchDurationFramesAttribute.GetValueFromBag(bag, cc);
                bar.progressAnimationSpeed = _progressAnimationSpeedAttribute.GetValueFromBag(
                    bag,
                    cc
                );

                if (bar.style.height.keyword == StyleKeyword.Auto || bar.style.height.value == 0)
                {
                    bar.style.height = 20;
                }

                if (bar.style.width.keyword == StyleKeyword.Auto || bar.style.width.value == 0)
                {
                    bar.style.width = 200;
                }
            }
        }

        public GlitchProgressBar()
            : this(PRNG.Instance) { }

        public GlitchProgressBar(IRandom random)
        {
            _random = random ?? PRNG.Instance;
            AddToClassList(USSClassName);
#if UNITY_2022_1_OR_NEWER
            generateVisualContent += OnGenerateVisualContent;
#endif
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            _visualProgress = _progress;
            _targetProgress = _progress;
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (!Mathf.Approximately(_visualProgress, _targetProgress) || _isGlitching)
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

            _animationUpdateItem = schedule.Execute(UpdateState).Every(16);
        }

        private void StopAnimationUpdate()
        {
            _animationUpdateItem?.Pause();
            _animationUpdateItem = null;
        }

        private void UpdateState(TimerState ts)
        {
            bool needsRepaint = false;

            if (!Mathf.Approximately(_visualProgress, _targetProgress))
            {
                _visualProgress = Mathf.Lerp(
                    _visualProgress,
                    _targetProgress,
                    ts.deltaTime / 1000f * progressAnimationSpeed
                );
                _progress = _visualProgress;
                if (Mathf.Abs(_visualProgress - _targetProgress) < 0.001f)
                {
                    _visualProgress = _targetProgress;
                    _progress = _targetProgress;
                }
                needsRepaint = true;
            }

            if (_isGlitching)
            {
                _glitchTimer--;
                if (_glitchTimer <= 0)
                {
                    _isGlitching = false;
                }
                needsRepaint = true;
            }
            else
            {
                bool progressIsChanging = !Mathf.Approximately(_visualProgress, _targetProgress);
                if (progressIsChanging && _random.NextFloat() < glitchFrequency)
                {
                    _isGlitching = true;
                    _glitchTimer = glitchDurationFrames;
                    needsRepaint = true;
                }
            }

            if (needsRepaint)
            {
                MarkDirtyRepaint();
            }

            if (
                Mathf.Approximately(_visualProgress, _targetProgress)
                && !_isGlitching
                && _animationUpdateItem != null
            ) { }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSNormalColorVarName),
                    out Color normCol
                )
            )
            {
                NormalColor = normCol;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSGlitchColor1VarName),
                    out Color g1Col
                )
            )
            {
                GlitchColor1 = g1Col;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSGlitchColor2VarName),
                    out Color g2Col
                )
            )
            {
                GlitchColor2 = g2Col;
            }

            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSTrackColorVarName),
                    out Color trackCol
                )
            )
            {
                TrackColor = trackCol;
            }

            MarkDirtyRepaint();
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

            painter.fillColor = _trackColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(r.xMin, r.yMin));
            painter.LineTo(new Vector2(r.xMax, r.yMin));
            painter.LineTo(new Vector2(r.xMax, r.yMax));
            painter.LineTo(new Vector2(r.xMin, r.yMax));
            painter.ClosePath();
            painter.Fill();

            if (Mathf.Approximately(_visualProgress, 0))
            {
                return;
            }

            float fillWidth = r.width * _visualProgress;

            if (_isGlitching)
            {
                int numSegments = _random.Next(2, 5);
                float segmentHeight = r.height / numSegments;

                for (int i = 0; i < numSegments; i++)
                {
                    float offsetX = (_random.NextFloat() - 0.5f) * 2f * r.width * glitchIntensity;
                    float offsetY =
                        (_random.NextFloat() - 0.5f) * 2f * r.height * glitchIntensity * 0.3f;

                    float currentSegmentY = r.yMin + i * segmentHeight;

                    offsetX = Mathf.Clamp(offsetX, -r.width * 0.2f, r.width * 0.2f);
                    offsetY = Mathf.Clamp(offsetY, -r.height * 0.1f, r.height * 0.1f);

                    Rect glitchRect = new(
                        r.xMin + offsetX,
                        currentSegmentY + offsetY,
                        fillWidth,
                        segmentHeight
                    );

                    glitchRect.xMin = Mathf.Max(glitchRect.xMin, r.xMin);
                    glitchRect.yMin = Mathf.Max(glitchRect.yMin, r.yMin);
                    glitchRect.xMax = Mathf.Min(glitchRect.xMax, r.xMax);
                    glitchRect.yMax = Mathf.Min(glitchRect.yMax, r.yMax);

                    if (glitchRect is { width: > 0, height: > 0 })
                    {
                        painter.fillColor = _random.NextBool() ? _glitchColor1 : _glitchColor2;
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(glitchRect.xMin, glitchRect.yMin));
                        painter.LineTo(new Vector2(glitchRect.xMax, glitchRect.yMin));
                        painter.LineTo(new Vector2(glitchRect.xMax, glitchRect.yMax));
                        painter.LineTo(new Vector2(glitchRect.xMin, glitchRect.yMax));
                        painter.ClosePath();
                        painter.Fill();
                    }
                }
            }
            else
            {
                painter.fillColor = _normalColor;
                painter.BeginPath();
                painter.MoveTo(new Vector2(r.xMin, r.yMin));
                painter.LineTo(new Vector2(r.xMin + fillWidth, r.yMin));
                painter.LineTo(new Vector2(r.xMin + fillWidth, r.yMax));
                painter.LineTo(new Vector2(r.xMin, r.yMax));
                painter.ClosePath();
                painter.Fill();
            }
        }
#endif
    }
}
