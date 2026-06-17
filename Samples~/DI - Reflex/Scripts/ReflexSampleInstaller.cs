// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.DI.Reflex
{
    using Reflex.Core;
    using Reflex.Extensions;
    using UnityEngine;

    /// <summary>
    /// Installs lightweight sample services so the scene demonstrates Reflex + relational wiring.
    /// </summary>
    public sealed class ReflexSampleInstaller : MonoBehaviour, IInstaller
    {
        [SerializeField]
        private Color _accentColor = new Color(0.156f, 0.768f, 0.972f, 1.0f);

        [SerializeField]
        private Color _inactiveColor = new Color(0.196f, 0.196f, 0.196f, 1.0f);

        [SerializeField]
        private Color _warningColor = new Color(0.949f, 0.419f, 0.270f, 1.0f);

        public void InstallBindings(ContainerBuilder builder)
        {
            // Reflex's factory-registration API changed at the 14.0.0 major bump
            // (AddSingleton(factory, contracts) -> RegisterFactory(factory, contracts,
            // Lifetime, Resolution)); REFLEX_14_0_OR_NEWER comes from this sample
            // asmdef's versionDefine so the sample builds against both. A Reflex
            // singleton factory is lazily resolved, matching Lifetime.Singleton/
            // Resolution.Lazy.
#if REFLEX_14_0_OR_NEWER
            builder.RegisterFactory(
                CreatePaletteService,
                new[] { typeof(ReflexPaletteService) },
                global::Reflex.Enums.Lifetime.Singleton,
                global::Reflex.Enums.Resolution.Lazy
            );
#else
            builder.AddSingleton(CreatePaletteService, typeof(ReflexPaletteService));
#endif
        }

        private ReflexPaletteService CreatePaletteService(Container container)
        {
            return new ReflexPaletteService(_accentColor, _inactiveColor, _warningColor);
        }
    }
}
