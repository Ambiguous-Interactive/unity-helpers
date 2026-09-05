// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.VContainer
{
    using System;
    using System.Collections.Generic;
    using global::VContainer;
    using global::VContainer.Unity;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Convenience extensions that bridge VContainer resolution/injection with relational component
    /// assignment.
    /// </summary>
    /// <remarks>
    /// These helpers are safe to call whether or not the integration is registered with the
    /// container. If an <see cref="IRelationalComponentAssigner"/> binding is available, it is used
    /// to hydrate attributed fields; otherwise the call falls back to
    /// <see cref="WallstopStudios.UnityHelpers.Core.Attributes.RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>
    /// so you never end up with null references.
    /// </remarks>
    /// <example>
    /// <code>
    /// // In a LifetimeScope
    /// using VContainer;
    /// using VContainer.Unity;
    /// using WallstopStudios.UnityHelpers.Integrations.VContainer;
    ///
    /// public sealed class GameLifetimeScope : LifetimeScope
    /// {
    ///     protected override void Configure(IContainerBuilder builder)
    ///     {
    ///         // Registers IRelationalComponentAssigner and the scene entry point
    ///         builder.RegisterRelationalComponents();
    ///     }
    /// }
    ///
    /// // In a MonoBehaviour that is created at runtime
    /// using UnityEngine;
    /// using VContainer;
    /// using WallstopStudios.UnityHelpers.Integrations.VContainer;
    ///
    /// public sealed class Spawner : MonoBehaviour
    /// {
    ///     [Inject] private IObjectResolver _resolver;
    ///
    ///     [SerializeField] private Enemy _enemyPrefab;
    ///
    ///     public Enemy Spawn(Transform parent)
    ///     {
    ///         Enemy instance = Instantiate(_enemyPrefab, parent);
    ///         // Ensures DI injection then relational assignment for fields like [Child], [Sibling], etc.
    ///         return _resolver.BuildUpWithRelations(instance);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static class ObjectResolverRelationalExtensions
    {
        /// <summary>
        /// Injects <paramref name="component"/> with VContainer and assigns its relational fields.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="resolver">The VContainer object resolver.</param>
        /// <param name="component">The component instance to inject and hydrate.</param>
        /// <returns>The same component instance.</returns>
        public static T InjectWithRelations<T>(this IObjectResolver resolver, T component)
            where T : Component
        {
            if (component == null)
            {
                return null;
            }

            // Use Inject for compatibility with VContainer 1.16.x
            resolver?.Inject(component);
            resolver.AssignRelationalComponents(component);
            return component;
        }

        /// <summary>
        /// Assigns all relational fields on a component using the container's registered
        /// <see cref="IRelationalComponentAssigner"/> if present, with a safe fallback to the
        /// non-DI assignment path.
        /// </summary>
        /// <param name="resolver">The VContainer object resolver (may be null).</param>
        /// <param name="component">The component to hydrate.</param>
        /// <remarks>
        /// Use this after calling <c>resolver.Inject(component)</c> if the component wasn't created
        /// by VContainer. When the assigner is not bound, the call uses
        /// <c>component.AssignRelationalComponents()</c> so behavior remains consistent.
        /// </remarks>
        /// <example>
        /// <code>
        /// _resolver.Inject(controller);
        /// _resolver.AssignRelationalComponents(controller);
        /// </code>
        /// </example>
        public static void AssignRelationalComponents(
            this IObjectResolver resolver,
            Component component
        )
        {
            if (component == null)
            {
                return;
            }

            if (TryResolveAssigner(resolver, out IRelationalComponentAssigner assigner))
            {
                assigner.Assign(component);
            }
            else
            {
                component.AssignRelationalComponents();
            }
        }

        /// <summary>
        /// Assigns relational fields for all components in a hierarchy.
        /// </summary>
        /// <param name="resolver">The VContainer object resolver.</param>
        /// <param name="root">The root GameObject to scan.</param>
        /// <param name="includeInactiveChildren">
        /// Include inactive objects when scanning children. Defaults to <c>true</c>.
        /// </param>
        /// <remarks>
        /// If <see cref="IRelationalComponentAssigner"/> is bound, it is used; otherwise the
        /// method iterates the hierarchy and calls
        /// <c>AssignRelationalComponents()</c> on each component.
        /// </remarks>
        /// <example>
        /// <code>
        /// // After building a dynamic hierarchy
        /// _resolver.AssignRelationalHierarchy(parentGameObject, includeInactiveChildren: false);
        /// </code>
        /// </example>
        public static void AssignRelationalHierarchy(
            this IObjectResolver resolver,
            GameObject root,
            bool includeInactiveChildren = true
        )
        {
            if (root == null)
            {
                return;
            }

            if (TryResolveAssigner(resolver, out IRelationalComponentAssigner assigner))
            {
                assigner.AssignHierarchy(root, includeInactiveChildren);
                return;
            }

            using PooledResource<List<Component>> componentBuffer = Buffers<Component>.List.Get(
                out List<Component> components
            );
            root.GetComponentsInChildren(includeInactiveChildren, components);
            foreach (UnityEngine.Component componentsElement in components)
            {
                componentsElement.AssignRelationalComponents();
            }
        }

        /// <summary>
        /// Injects a component with VContainer and then hydrates its relational fields.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="resolver">The VContainer object resolver.</param>
        /// <param name="component">The component instance to build up.</param>
        /// <returns>The same component instance for fluent usage.</returns>
        /// <example>
        /// <code>
        /// Enemy enemy = Instantiate(enemyPrefab);
        /// enemy = _resolver.BuildUpWithRelations(enemy);
        /// </code>
        /// </example>
        public static T BuildUpWithRelations<T>(this IObjectResolver resolver, T component)
            where T : Component
        {
            if (component == null)
            {
                return null;
            }

            // Use Inject for compatibility with VContainer 1.16.x
            resolver?.Inject(component);
            resolver.AssignRelationalComponents(component);
            return component;
        }

        /// <summary>
        /// Instantiates a prefab that has component <typeparamref name="T"/> on the root, injects it
        /// using VContainer and assigns relational fields.
        /// </summary>
        /// <typeparam name="T">Component type on the prefab root.</typeparam>
        /// <param name="resolver">The VContainer object resolver.</param>
        /// <param name="prefab">Prefab that contains <typeparamref name="T"/>.</param>
        /// <param name="parent">Optional parent transform for the new instance.</param>
        /// <returns>The instantiated component with DI and relational fields populated.</returns>
        public static T InstantiateComponentWithRelations<T>(
            this IObjectResolver resolver,
            T prefab,
            Transform parent = null
        )
            where T : Component
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            T instance = UnityEngine.Object.Instantiate(prefab, parent);
            return resolver.BuildUpWithRelations(instance);
        }

        /// <summary>
        /// Instantiates a GameObject prefab, injects its hierarchy with VContainer, then assigns
        /// relational fields for all components beneath the root.
        /// </summary>
        /// <param name="resolver">The VContainer object resolver.</param>
        /// <param name="prefab">GameObject prefab to instantiate.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="includeInactiveChildren">Whether to include inactive children when assigning.</param>
        /// <returns>The instantiated GameObject.</returns>
        public static GameObject InstantiateGameObjectWithRelations(
            this IObjectResolver resolver,
            GameObject prefab,
            Transform parent = null,
            bool includeInactiveChildren = true
        )
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
            resolver.InjectGameObjectWithRelations(instance, includeInactiveChildren);
            return instance;
        }

        /// <summary>
        /// Injects all components on <paramref name="root"/> and its children, then assigns
        /// relational fields for the hierarchy.
        /// </summary>
        /// <param name="resolver">The VContainer object resolver.</param>
        /// <param name="root">Root GameObject to inject and hydrate.</param>
        /// <param name="includeInactiveChildren">Whether to include inactive children when assigning.</param>
        public static void InjectGameObjectWithRelations(
            this IObjectResolver resolver,
            GameObject root,
            bool includeInactiveChildren = true
        )
        {
            if (root == null)
            {
                return;
            }

            resolver?.InjectGameObject(root);
            resolver.AssignRelationalHierarchy(root, includeInactiveChildren);
        }

        private static bool TryResolveAssigner(
            IObjectResolver resolver,
            out IRelationalComponentAssigner assigner
        )
        {
            if (resolver != null && resolver.TryResolve(out assigner))
            {
                return assigner != null;
            }

            assigner = null;
            return false;
        }
    }
}
#endif
