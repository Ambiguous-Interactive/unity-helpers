// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using System.ComponentModel;
    using Core.Helper;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class ArcedProgressBar : VisualElement
    {
        public enum FillDirection
        {
            Forward = 0,
            Reverse = 1,
        }

        public const string USSClassName = "arced-progress-bar";
        public const string USSTrackColorVarName = "--arc-track-color";
        public const string USSProgressColorVarName = "--arc-progress-color";
        public const string USSThicknessVarName = "--arc-thickness";

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
                MarkDirtyRepaint();
            }
        }

        private float _radius = 50f;

        public float Radius
        {
            get => _radius;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _radius = Mathf.Max(1f, value);
                UpdateSize();
                MarkDirtyRepaint();
            }
        }

        private float _thickness = 10f;

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

        private float _startAngleDegrees = -90f;

        public float StartAngleDegrees
        {
            get => _startAngleDegrees;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _startAngleDegrees = value;
                MarkDirtyRepaint();
            }
        }

        private float _endAngleDegrees = 90f;

        public float EndAngleDegrees
        {
            get => _endAngleDegrees;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _endAngleDegrees = value;
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

        private Color _progressColor = Color.cyan;

        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                MarkDirtyRepaint();
            }
        }

        public new class UxmlFactory : UxmlFactory<ArcedProgressBar, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _progressAttribute = new()
            {
                name = "progress",
                defaultValue = 0.5f,
            };

            private readonly UxmlFloatAttributeDescription _radiusAttribute = new()
            {
                name = "radius",
                defaultValue = 50f,
            };

            private readonly UxmlFloatAttributeDescription _thicknessAttribute = new()
            {
                name = "thickness",
                defaultValue = 10f,
            };

            private readonly UxmlFloatAttributeDescription _startAngleAttribute = new()
            {
                name = "start-angle",
                defaultValue = -90f,
            };

            private readonly UxmlFloatAttributeDescription _endAngleAttribute = new()
            {
                name = "end-angle",
                defaultValue = 90f,
            };

            private readonly UxmlEnumAttributeDescription<FillDirection> _fillDirectionAttribute =
                new() { name = "fill-direction", defaultValue = FillDirection.Forward };

            private readonly UxmlBoolAttributeDescription _roundedCapsAttribute = new()
            {
                name = "rounded-caps",
                defaultValue = true,
            };

            private readonly UxmlColorAttributeDescription _trackColorAttribute = new()
            {
                name = "track-color-attr",
                defaultValue = Color.gray,
            };

            private readonly UxmlColorAttributeDescription _progressColorAttribute = new()
            {
                name = "progress-color-attr",
                defaultValue = Color.cyan,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is not ArcedProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(ArcedProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }

                bar.Progress = _progressAttribute.GetValueFromBag(bag, cc);
                bar.Radius = _radiusAttribute.GetValueFromBag(bag, cc);
                bar.Thickness = _thicknessAttribute.GetValueFromBag(bag, cc);
                bar.StartAngleDegrees = _startAngleAttribute.GetValueFromBag(bag, cc);
                bar.EndAngleDegrees = _endAngleAttribute.GetValueFromBag(bag, cc);
                bar.Direction = _fillDirectionAttribute.GetValueFromBag(bag, cc);
                bar.RoundedCaps = _roundedCapsAttribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.ProgressColor = _progressColorAttribute.GetValueFromBag(bag, cc);
            }
        }

        public ArcedProgressBar()
        {
            AddToClassList(USSClassName);
#if UNITY_2022_1_OR_NEWER
            generateVisualContent += OnGenerateVisualContent;
#endif
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            UpdateSize();
        }

        private void UpdateSize()
        {
            float diameter = (_radius + _thickness / 2f) * 2f;
            style.width = diameter;
            style.height = diameter;
            style.minWidth = diameter;
            style.minHeight = diameter;
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
                _thickness = Mathf.Max(1f, thickVal);
            }

            MarkDirtyRepaint();
        }

#if UNITY_2022_1_OR_NEWER
        // Painter2D vector API requires Unity 2022.1+; on 2021.3 this element renders no custom content.
        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (_thickness <= 0)
            {
                return;
            }

            Painter2D painter = mgc.painter2D;
            Rect rect = contentRect;
            Vector2 center = rect.center;

            painter.lineWidth = _thickness;
            painter.lineCap = _roundedCaps ? LineCap.Round : LineCap.Butt;

            painter.strokeColor = _trackColor;
            painter.BeginPath();
            painter.Arc(center, _radius, _startAngleDegrees, _endAngleDegrees);
            painter.Stroke();

            if (Mathf.Approximately(_progress, 0f))
            {
                return;
            }

            float startAngle;
            ArcDirection direction;
            float sweepAngleDegrees;
            switch (_fillDirection)
            {
                case FillDirection.Forward:
                {
                    startAngle = _startAngleDegrees;
                    direction = ArcDirection.Clockwise;
                    sweepAngleDegrees =
                        _progress * (_endAngleDegrees - _startAngleDegrees).PositiveMod(360f);
                    break;
                }
                case FillDirection.Reverse:
                {
                    startAngle = _endAngleDegrees;
                    direction = ArcDirection.CounterClockwise;
                    sweepAngleDegrees =
                        -1 * _progress * (_endAngleDegrees - _startAngleDegrees).PositiveMod(360f);
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

            if (!Mathf.Approximately(sweepAngleDegrees, 0))
            {
                painter.strokeColor = _progressColor;
                painter.BeginPath();
                painter.Arc(center, _radius, startAngle, startAngle + sweepAngleDegrees, direction);
                painter.Stroke();
            }
        }
#endif
    }
}
