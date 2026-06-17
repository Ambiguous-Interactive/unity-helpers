// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Reflex
{
    using global::Reflex.Core;
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
            // Reflex's ContainerBuilder registration API changed at the 14.0.0 major
            // bump: <14 exposes AddSingleton/AddTransient/AddScoped (lifetime encoded
            // in the method name, no Resolution concept); >=14 replaced those with
            // RegisterType/RegisterValue + the Lifetime/Resolution enums. The asmdef's
            // versionDefine sets REFLEX_14_0_OR_NEWER only for a UPM Reflex >= 14.0.0,
            // so both APIs compile (CI's pinned 14.3.0 and a 13.x vendored copy alike).
            // AddSingleton(type, contracts) is the exact equivalent of
            // RegisterType(type, contracts, Singleton, Lazy) -- a Reflex singleton is
            // lazily resolved by default -- and AddSingleton(instance, contracts) of
            // RegisterValue(instance, contracts).
            AttributeMetadataCache cacheInstance = AttributeMetadataCache.Instance;
            if (cacheInstance != null && !builder.HasBinding(typeof(AttributeMetadataCache)))
            {
#if REFLEX_14_0_OR_NEWER
                builder.RegisterValue(cacheInstance, new[] { typeof(AttributeMetadataCache) });
#else
                builder.AddSingleton(cacheInstance, typeof(AttributeMetadataCache));
#endif
            }

            if (!builder.HasBinding(typeof(IRelationalComponentAssigner)))
            {
#if REFLEX_14_0_OR_NEWER
                builder.RegisterType(
                    typeof(RelationalComponentAssigner),
                    new[]
                    {
                        typeof(IRelationalComponentAssigner),
                        typeof(RelationalComponentAssigner),
                    },
                    global::Reflex.Enums.Lifetime.Singleton,
                    global::Reflex.Enums.Resolution.Lazy
                );
#else
                builder.AddSingleton(
                    typeof(RelationalComponentAssigner),
                    typeof(IRelationalComponentAssigner),
                    typeof(RelationalComponentAssigner)
                );
#endif
            }

            RelationalSceneAssignmentOptions options = new(
                _includeInactiveObjects,
                _useSinglePassScan
            );
            if (!builder.HasBinding(typeof(RelationalSceneAssignmentOptions)))
            {
#if REFLEX_14_0_OR_NEWER
                builder.RegisterValue(options);
#else
                builder.AddSingleton(options);
#endif
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
