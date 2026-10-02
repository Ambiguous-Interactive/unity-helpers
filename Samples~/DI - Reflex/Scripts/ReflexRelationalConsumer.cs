// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.DI.Reflex
{
    using Reflex.Attributes;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Simple MonoBehaviour that receives a Reflex dependency and relational components at runtime.
    /// </summary>
    public sealed class ReflexRelationalConsumer : MonoBehaviour
    {
        [Inject]
        private ReflexPaletteService _paletteService;

        [SiblingComponent]
        private SpriteRenderer _spriteRenderer;

        [ChildComponent(OnlyDescendants = true)]
        private ParticleSystem[] _childParticles;

        private void Awake()
        {
            WarmUpParticles();
        }

        private void OnEnable()
        {
            ApplyAccentColor();
        }

        private void OnDisable()
        {
            ApplyInactiveColor();
        }

        public void ApplyAccentColor()
        {
            if (_spriteRenderer != null && _paletteService != null)
            {
                _spriteRenderer.color = _paletteService.AccentColor;
            }
        }

        public void ApplyInactiveColor()
        {
            if (_spriteRenderer != null && _paletteService != null)
            {
                _spriteRenderer.color = _paletteService.InactiveColor;
            }
        }

        public void FlashWarningColor()
        {
            if (_spriteRenderer != null && _paletteService != null)
            {
                _spriteRenderer.color = _paletteService.WarningColor;
            }
        }

        private void WarmUpParticles()
        {
            if (_childParticles == null)
            {
                return;
            }

            for (int i = 0; i < _childParticles.Length; ++i)
            {
                ParticleSystem system = _childParticles[i];
                if (system == null)
                {
                    continue;
                }

                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
