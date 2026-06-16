// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using System.ComponentModel;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class CircularProgressBar : VisualElement
    {
        public enum StartPointLocation
        {
            Top = 0,
            Right = 1,
            Bottom = 2,
            Left = 3,
        }

        public enum FillDirection
        {
            Clockwise = 0,
            CounterClockwise = 1,
        }

        public const string USSClassName = "circular-progress-bar";
        public const string USSTrackColorVarName = "--track-color";
        public const string USSProgressColorVarName = "--progress-color";
        public const string USSThicknessVarName = "--thickness";

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

        private float _startPoint = GetStartAngleInDegrees(StartPointLocation.Top);
        public float StartAt
        {
            get => _startPoint;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _startPoint = value % 360f;
                MarkDirtyRepaint();
            }
        }

        private FillDirection _fillDirection = FillDirection.Clockwise;
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

        private Color _progressColor = Color.green;
        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                MarkDirtyRepaint();
            }
        }

        public new class UxmlFactory : UxmlFactory<CircularProgressBar, UxmlTraits> { }

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

            private readonly UxmlFloatAttributeDescription _startPointAttribute = new()
            {
                name = "start-at",
                defaultValue = GetStartAngleInDegrees(StartPointLocation.Top),
            };

            private readonly UxmlEnumAttributeDescription<FillDirection> _fillDirectionAttribute =
                new() { name = "direction", defaultValue = FillDirection.Clockwise };

            private readonly UxmlColorAttributeDescription _trackColorAttribute = new()
            {
                name = "track-color-attr",
                defaultValue = Color.gray,
            };

            private readonly UxmlColorAttributeDescription _progressColorAttribute = new()
            {
                name = "progress-color-attr",
                defaultValue = Color.green,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is not CircularProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(CircularProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }

                bar.Progress = _progressAttribute.GetValueFromBag(bag, cc);
                bar.Radius = _radiusAttribute.GetValueFromBag(bag, cc);
                bar.Thickness = _thicknessAttribute.GetValueFromBag(bag, cc);
                bar.StartAt = _startPointAttribute.GetValueFromBag(bag, cc);
                bar.Direction = _fillDirectionAttribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.ProgressColor = _progressColorAttribute.GetValueFromBag(bag, cc);
            }
        }

        public CircularProgressBar()
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
            Painter2D painter = mgc.painter2D;
            Rect rect = contentRect;

            float drawRadius = _radius;
            Vector2 center = rect.center;

            painter.strokeColor = _trackColor;
            painter.lineWidth = _thickness;
            painter.BeginPath();
            painter.Arc(center, drawRadius, 0f, 360f);
            painter.Stroke();

            if (Mathf.Approximately(_progress, 0))
            {
                return;
            }

            ArcDirection direction;
            float sweepAngleDegrees;

            switch (_fillDirection)
            {
                case FillDirection.Clockwise:
                {
                    direction = ArcDirection.Clockwise;
                    sweepAngleDegrees = _progress * 360f;
                    break;
                }
                case FillDirection.CounterClockwise:
                {
                    direction = ArcDirection.CounterClockwise;
                    sweepAngleDegrees = -1 * _progress * 360f;
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

            painter.strokeColor = _progressColor;
            painter.lineWidth = _thickness;
            painter.BeginPath();
            painter.Arc(
                center,
                drawRadius,
                _startPoint,
                _startPoint + sweepAngleDegrees,
                direction
            );
            painter.Stroke();
        }
#endif

        public static float GetStartAngleInDegrees(StartPointLocation location)
        {
            return location switch
            {
                StartPointLocation.Top => -90f,
                StartPointLocation.Right => 0f,
                StartPointLocation.Bottom => 90f,
                StartPointLocation.Left => 180f,
                _ => 90f,
            };
        }
    }
}
