// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Styles.Elements.Progress
{
    using System.ComponentModel;
    using UnityEngine;
    using UnityEngine.UIElements;

#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    public sealed partial class RegularProgressBar : VisualElement
    {
        public const string USSClassName = "regular-progress-bar";
        public const string USSTrackClassName = USSClassName + "__track";
        public const string USSFillClassName = USSClassName + "__fill";

        public const string USSTrackColorVarName = "--rpb-track-color";
        public const string USSProgressColorVarName = "--rpb-progress-color";
        public const string USSBorderRadiusVarName = "--rpb-border-radius";
        public const string USSThicknessVarName = "--rpb-thickness";

        private readonly VisualElement _trackElement;
        private readonly VisualElement _fillElement;

        public enum OrientationType
        {
            Horizontal = 0,
            Vertical = 1,
        }

        public enum FillDirection
        {
            Forward = 0,
            Reverse = 1,
        }

        private float _progress = 0.5f;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("progress")]
#endif
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
                UpdateFillVisuals();
            }
        }

        private Color _trackColor = new(0.3f, 0.3f, 0.3f, 1f);

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("track-color-attr")]
#endif
        public Color TrackColor
        {
            get => _trackColor;
            set
            {
                _trackColor = value;
                if (_trackElement != null)
                {
                    _trackElement.style.backgroundColor = _trackColor;
                }
            }
        }

        private Color _progressColor = new(0.2f, 0.7f, 0.2f, 1f);

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("progress-color-attr")]
#endif
        public Color ProgressColor
        {
            get => _progressColor;
            set
            {
                _progressColor = value;
                if (_fillElement != null)
                {
                    _fillElement.style.backgroundColor = _progressColor;
                }
            }
        }

        private OrientationType _orientation = OrientationType.Horizontal;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("orientation")]
#endif
        public OrientationType Orientation
        {
            get => _orientation;
            set
            {
                _orientation = value;
                UpdateOrientationVisuals();
                UpdateFillVisuals();
            }
        }

        private FillDirection _fillDirection = FillDirection.Forward;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("direction")]
#endif
        public FillDirection Direction
        {
            get => _fillDirection;
            set
            {
                _fillDirection = value;
                UpdateOrientationVisuals();
                UpdateFillVisuals();
            }
        }

        private float _borderRadius = 3f;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("border-radius")]
#endif
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
                ApplyBorderRadius();
            }
        }

        private float _thickness = 15f;

#if UNITY_6000_0_OR_NEWER
        [UxmlAttribute("thickness")]
#endif
        public float Thickness
        {
            get => _thickness;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return;
                }
                _thickness = Mathf.Max(1, value);
                UpdateThicknessVisuals();
            }
        }

#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<RegularProgressBar, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _progressAttribute = new()
            {
                name = "progress",
                defaultValue = 0.5f,
            };

            private readonly UxmlColorAttributeDescription _trackColorAttribute = new()
            {
                name = "track-color-attr",
                defaultValue = new Color(0.3f, 0.3f, 0.3f, 1f),
            };

            private readonly UxmlColorAttributeDescription _progressColorAttribute = new()
            {
                name = "progress-color-attr",
                defaultValue = new Color(0.2f, 0.7f, 0.2f, 1f),
            };

            private readonly UxmlEnumAttributeDescription<OrientationType> _orientationAttribute =
                new() { name = "orientation", defaultValue = OrientationType.Horizontal };

            private readonly UxmlEnumAttributeDescription<FillDirection> _fillAttribute = new()
            {
                name = "direction",
                defaultValue = FillDirection.Forward,
            };

            private readonly UxmlFloatAttributeDescription _borderRadiusAttribute = new()
            {
                name = "border-radius",
                defaultValue = 3f,
            };

            private readonly UxmlFloatAttributeDescription _thicknessAttribute = new()
            {
                name = "thickness",
                defaultValue = 15f,
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is not RegularProgressBar bar)
                {
                    Debug.LogError(
                        $"Initialization failed, expected {nameof(RegularProgressBar)}, found {ve?.GetType()}.)"
                    );
                    return;
                }

                bar.Progress = _progressAttribute.GetValueFromBag(bag, cc);
                bar.TrackColor = _trackColorAttribute.GetValueFromBag(bag, cc);
                bar.ProgressColor = _progressColorAttribute.GetValueFromBag(bag, cc);
                bar.Thickness = _thicknessAttribute.GetValueFromBag(bag, cc);
                bar.Orientation = _orientationAttribute.GetValueFromBag(bag, cc);
                bar.BorderRadius = _borderRadiusAttribute.GetValueFromBag(bag, cc);
                bar.Direction = _fillAttribute.GetValueFromBag(bag, cc);
            }
        }
#endif

        public RegularProgressBar()
        {
            AddToClassList(USSClassName);

            _trackElement = new VisualElement { name = "track" };
            _trackElement.AddToClassList(USSTrackClassName);
            Add(_trackElement);

            _fillElement = new VisualElement { name = "fill" };
            _fillElement.AddToClassList(USSFillClassName);
            _trackElement.Add(_fillElement);

            _trackElement.style.width = Length.Percent(100);
            _trackElement.style.height = Length.Percent(100);
            _trackElement.style.overflow = Overflow.Hidden;

            _fillElement.style.position = Position.Absolute;

            TrackColor = _trackColor;
            ProgressColor = _progressColor;
            Thickness = _thickness;
            Orientation = _orientation;
            BorderRadius = _borderRadius;

            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
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
                TrackColor = trackCol;
            }
            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<Color>(USSProgressColorVarName),
                    out Color progressCol
                )
            )
            {
                ProgressColor = progressCol;
            }
            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSBorderRadiusVarName),
                    out float radius
                )
            )
            {
                BorderRadius = radius;
            }
            if (
                customStyle.TryGetValue(
                    new CustomStyleProperty<float>(USSThicknessVarName),
                    out float thicknessVal
                )
            )
            {
                Thickness = thicknessVal;
            }
        }

        private void UpdateThicknessVisuals()
        {
            style.height = _thickness;
            style.minHeight = _thickness;
        }

        private void UpdateOrientationVisuals()
        {
            UpdateThicknessVisuals();

            switch (_orientation)
            {
                case OrientationType.Horizontal:
                {
                    switch (_fillDirection)
                    {
                        case FillDirection.Forward:
                        {
                            _fillElement.style.right = StyleKeyword.Auto;
                            _fillElement.style.left = 0;
                            _fillElement.style.top = 0;
                            _fillElement.style.bottom = 0;
                            _fillElement.style.height = Length.Percent(100);
                            break;
                        }
                        case FillDirection.Reverse:
                        {
                            _fillElement.style.left = StyleKeyword.Auto;
                            _fillElement.style.top = 0;
                            _fillElement.style.bottom = 0;
                            _fillElement.style.right = 0;
                            _fillElement.style.height = Length.Percent(100);
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

                    break;
                }
                case OrientationType.Vertical:
                {
                    switch (_fillDirection)
                    {
                        case FillDirection.Forward:
                        {
                            _fillElement.style.left = 0;
                            _fillElement.style.top = StyleKeyword.Auto;
                            _fillElement.style.bottom = 0;
                            _fillElement.style.right = 0;
                            _fillElement.style.width = Length.Percent(100);
                            break;
                        }
                        case FillDirection.Reverse:
                        {
                            _fillElement.style.left = 0;
                            _fillElement.style.top = 0;
                            _fillElement.style.bottom = StyleKeyword.Auto;
                            _fillElement.style.right = 0;
                            _fillElement.style.width = Length.Percent(100);
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

        private void ApplyBorderRadius()
        {
            if (_trackElement != null)
            {
                _trackElement.style.borderTopLeftRadius = _borderRadius;
                _trackElement.style.borderTopRightRadius = _borderRadius;
                _trackElement.style.borderBottomLeftRadius = _borderRadius;
                _trackElement.style.borderBottomRightRadius = _borderRadius;
            }
            if (_fillElement != null)
            {
                _fillElement.style.borderTopLeftRadius = _borderRadius;
                _fillElement.style.borderTopRightRadius = _borderRadius;
                _fillElement.style.borderBottomLeftRadius = _borderRadius;
                _fillElement.style.borderBottomRightRadius = _borderRadius;
            }
        }

        private void UpdateFillVisuals()
        {
            if (_fillElement == null)
            {
                return;
            }

            float clampedProgress = Mathf.Clamp01(_progress);

            switch (_orientation)
            {
                case OrientationType.Horizontal:
                {
                    _fillElement.style.width = Length.Percent(clampedProgress * 100f);
                    _fillElement.style.height = Length.Percent(100);
                    break;
                }
                case OrientationType.Vertical:
                {
                    _fillElement.style.height = Length.Percent(clampedProgress * 100f);
                    _fillElement.style.width = Length.Percent(100);
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
    }
}
