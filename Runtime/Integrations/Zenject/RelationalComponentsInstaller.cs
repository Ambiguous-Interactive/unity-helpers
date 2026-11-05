// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if ZENJECT_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Zenject
{
    using global::Zenject;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Zenject installer that wires up the relational component assigner and optional scene
    /// initializer.
    /// </summary>
    /// <remarks>
    /// Add this installer to your <c>SceneContext</c> to:
    /// - Bind <see cref="IRelationalComponentAssigner"/> as a singleton (if nobody else already did)
    /// - Optionally register <see cref="RelationalComponentSceneInitializer"/> to populate relational
    ///   fields after container construction
    /// - Optionally register <see cref="RelationalSceneLoadListener"/> so additively loaded scenes
    ///   receive the same hydration pass
    /// Serialized toggles expose these behaviours plus the shared
    /// <see cref="RelationalSceneAssignmentOptions"/> (include inactive objects, scan strategy).
    /// </remarks>
    /// <example>
    /// <code>
    /// // In your scene, add a SceneContext and this installer component.
    /// // Toggle "Assign Scene On Initialize" to run the one-time scene scan on startup.
    /// </code>
    /// </example>
    [AddComponentMenu("Wallstop Studios/Unity Helpers/Relational Components/Zenject Installer")]
    public sealed class RelationalComponentsInstaller : MonoInstaller
    {
        [SerializeField]
        [Tooltip(
            "When enabled the installer will register a scene-level initializer that populates relational"
                + " fields immediately after Zenject finishes constructing the container."
        )]
        private bool _assignSceneOnInitialize = true;

        [SerializeField]
        [Tooltip(
            "Include inactive GameObjects when scanning the active scene for relational fields during"
                + " initialization."
        )]
        private bool _includeInactiveObjects = true;

        [SerializeField]
        [Tooltip(
            "When enabled, registers a listener that assigns relational fields for additively loaded"
                + " scenes as they are loaded."
        )]
        private bool _listenForAdditiveScenes = true;

        public override void InstallBindings()
        {
            AttributeMetadataCache cacheInstance = AttributeMetadataCache.Instance;
            if (cacheInstance != null && !Container.HasBinding(typeof(AttributeMetadataCache)))
            {
                Container.Bind<AttributeMetadataCache>().FromInstance(cacheInstance);
            }

            if (!Container.HasBinding(typeof(IRelationalComponentAssigner)))
            {
                Container
                    .Bind<IRelationalComponentAssigner>()
                    .To<RelationalComponentAssigner>()
                    .AsSingle();
            }

            if (_assignSceneOnInitialize)
            {
                if (!Container.HasBinding(typeof(RelationalSceneAssignmentOptions)))
                {
                    Container.BindInstance(
                        new RelationalSceneAssignmentOptions(_includeInactiveObjects)
                    );
                }

                Container.BindInterfacesTo<RelationalComponentSceneInitializer>().AsSingle();
            }

            if (_listenForAdditiveScenes)
            {
                if (!Container.HasBinding(typeof(RelationalSceneAssignmentOptions)))
                {
                    Container.BindInstance(
                        new RelationalSceneAssignmentOptions(_includeInactiveObjects)
                    );
                }
                Container.BindInterfacesTo<RelationalSceneLoadListener>().AsSingle();
            }
        }
    }
}
#endif
