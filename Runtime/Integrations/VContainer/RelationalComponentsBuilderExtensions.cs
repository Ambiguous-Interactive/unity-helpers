// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.VContainer
{
    using System;
    using global::VContainer;
    using global::VContainer.Unity;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tags;

    /// <summary>
    /// Convenience registration helpers for wiring relational component support into a
    /// <see cref="LifetimeScope"/>.
    /// </summary>
    /// <remarks>
    /// Registers the shared <see cref="IRelationalComponentAssigner"/> as a singleton and schedules a
    /// scene-wide entry point that hydrates all relational fields after the container has been built.
    /// Optionally wires <see cref="RelationalSceneLoadListener"/> so future additive scenes receive
    /// the same treatment.
    /// </remarks>
    public static class RelationalComponentsBuilderExtensions
    {
        /// <summary>
        /// Registers the relational component assigner and scene entry point with the supplied
        /// container builder.
        /// </summary>
        /// <param name="builder">The VContainer builder.</param>
        /// <param name="options">
        /// Optional settings to control how the active scene is scanned (e.g., include inactive
        /// objects). When <c>null</c>, <see cref="RelationalSceneAssignmentOptions.Default"/> is used.
        /// </param>
        /// <param name="enableAdditiveSceneListener">
        /// When true registers <see cref="RelationalSceneLoadListener"/> so additively loaded scenes
        /// are hydrated with the same options. Disable when you manage additive scenes manually.
        /// </param>
        /// <example>
        /// <code>
        /// using VContainer;
        /// using VContainer.Unity;
        /// using WallstopStudios.UnityHelpers.Integrations.VContainer;
        ///
        /// public sealed class GameLifetimeScope : LifetimeScope
        /// {
        ///     protected override void Configure(IContainerBuilder builder)
        ///     {
        ///         // Basic usage
        ///         builder.RegisterRelationalComponents();
        ///
        ///         // Or customize scanning options
        ///         builder.RegisterRelationalComponents(
        ///             new RelationalSceneAssignmentOptions(includeInactive: false)
        ///         );
        ///     }
        /// }
        /// </code>
        /// </example>
        public static void RegisterRelationalComponents(
            this IContainerBuilder builder,
            RelationalSceneAssignmentOptions? options = null,
            bool enableAdditiveSceneListener = true
        )
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Register the cache type even without an asset so container graph validation can construct dependents.
            AttributeMetadataCache cacheInstance = AttributeMetadataCache.Instance;
            if (cacheInstance != null)
            {
                builder.RegisterInstance(cacheInstance).As<AttributeMetadataCache>();
            }
            else
            {
                builder
                    .Register(_ => AttributeMetadataCache.Instance, Lifetime.Singleton)
                    .As<AttributeMetadataCache>();
            }

            RelationalSceneAssignmentOptions resolved =
                options ?? RelationalSceneAssignmentOptions.Default;

            builder
                .Register<RelationalComponentAssigner>(Lifetime.Singleton)
                .As<IRelationalComponentAssigner>()
                .AsSelf();

            builder.RegisterEntryPoint<RelationalComponentEntryPoint>().WithParameter(resolved);

            if (enableAdditiveSceneListener)
            {
                builder.RegisterEntryPoint<RelationalSceneLoadListener>().WithParameter(resolved);
            }
        }
    }
}
#endif
