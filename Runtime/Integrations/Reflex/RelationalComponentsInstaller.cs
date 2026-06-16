// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Reflex
{
    using global::Reflex.Core;
    using global::Reflex.Enums;
    using global::Reflex.Extensions;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Reflex installer that binds relational component services and optionally hydrates scenes.
    /// </summary>
    [AddComponentMenu("Wallstop Studios/Relational Components/Reflex Installer")]
    public sealed class RelationalComponentsInstaller : MonoBehaviour, IInstaller
    {
        [SerializeField]
        [Tooltip(
            "When enabled, relational fields within the scene are assigned immediately after the container is built."
        )]
        private bool _assignSceneOnInitialize = true;

        [SerializeField]
        [Tooltip("Include inactive GameObjects when scanning for relational assignments.")]
        private bool _includeInactiveObjects = true;

        [SerializeField]
        [Tooltip(
            "Registers an additive scene listener that hydrates relational fields for scenes loaded additively."
        )]
        private bool _listenForAdditiveScenes = true;

        [SerializeField]
        [Tooltip(
            "Use a single-pass scan when assigning relational fields for improved performance."
        )]
        private bool _useSinglePassScan = true;

        /// <inheritdoc />
        public void InstallBindings(ContainerBuilder builder)
        {
            AttributeMetadataCache cacheInstance = AttributeMetadataCache.Instance;
            if (cacheInstance != null && !builder.HasBinding(typeof(AttributeMetadataCache)))
            {
                builder.RegisterValue(cacheInstance, new[] { typeof(AttributeMetadataCache) });
            }

            if (!builder.HasBinding(typeof(IRelationalComponentAssigner)))
            {
                builder.RegisterType(
                    typeof(RelationalComponentAssigner),
                    new[]
                    {
                        typeof(IRelationalComponentAssigner),
                        typeof(RelationalComponentAssigner),
                    },
                    Lifetime.Singleton,
                    Resolution.Lazy
                );
            }

            RelationalSceneAssignmentOptions options = new(
                _includeInactiveObjects,
                _useSinglePassScan
            );
            if (!builder.HasBinding(typeof(RelationalSceneAssignmentOptions)))
            {
                builder.RegisterValue(options);
            }

            Scene installerScene = gameObject.scene;

            builder.OnContainerBuilt += container =>
            {
                RelationalSceneAssignmentOptions assignmentOptions = options;
                if (container.HasBinding<RelationalSceneAssignmentOptions>())
                {
                    assignmentOptions = container.Resolve<RelationalSceneAssignmentOptions>();
                }

                RelationalReflexSceneBootstrapper.ConfigureScene(
                    container,
                    installerScene,
                    assignmentOptions,
                    _assignSceneOnInitialize,
                    _listenForAdditiveScenes
                );
            };
        }
    }
}
#endif
