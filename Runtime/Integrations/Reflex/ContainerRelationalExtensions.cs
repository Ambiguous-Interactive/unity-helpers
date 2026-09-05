// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Reflex
{
    using System;
    using System.Collections.Generic;
    using global::Reflex.Core;
    using global::Reflex.Injectors;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Extension methods that bridge Reflex containers with relational component assignment.
    /// </summary>
    public static class ContainerRelationalExtensions
    {
        /// <summary>
        /// Injects a component using Reflex and assigns its relational fields.
        /// </summary>
        /// <typeparam name="T">Component type.</typeparam>
        /// <param name="container">Reflex container.</param>
        /// <param name="component">Component instance to inject and hydrate.</param>
        /// <returns>The injected component instance.</returns>
        public static T InjectWithRelations<T>(this Container container, T component)
            where T : Component
        {
            if (component == null)
            {
                return null;
            }

            if (container != null)
            {
                AttributeInjector.Inject(component, container);
            }

            container.AssignRelationalComponents(component);
            return component;
        }

        /// <summary>
        /// Assigns relational component fields on the supplied component.
        /// </summary>
        /// <param name="container">Reflex container.</param>
        /// <param name="component">Component to hydrate.</param>
        public static void AssignRelationalComponents(this Container container, Component component)
        {
            if (component == null)
            {
                return;
            }

            IRelationalComponentAssigner assigner = ResolveAssigner(container);
            if (assigner != null)
            {
                assigner.Assign(component);
                return;
            }

            component.AssignRelationalComponents();
        }

        /// <summary>
        /// Assigns relational component fields across a GameObject hierarchy.
        /// </summary>
        /// <param name="container">Reflex container.</param>
        /// <param name="root">Hierarchy root.</param>
        /// <param name="includeInactiveChildren">
        /// When true, includes inactive children during assignment.
        /// </param>
        public static void AssignRelationalHierarchy(
            this Container container,
            GameObject root,
            bool includeInactiveChildren = true
        )
        {
            if (root == null)
            {
                return;
            }

            IRelationalComponentAssigner assigner = ResolveAssigner(container);
            if (assigner != null)
            {
                assigner.AssignHierarchy(root, includeInactiveChildren);
                return;
            }

            using PooledResource<List<Component>> pooledComponents = Buffers<Component>.List.Get(
                out List<Component> components
            );
            root.GetComponentsInChildren(includeInactiveChildren, components);
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                component.AssignRelationalComponents();
            }
        }

        /// <summary>
        /// Instantiates a component prefab, injects it, and assigns relational fields.
        /// </summary>
        /// <typeparam name="T">Component type contained by the prefab.</typeparam>
        /// <param name="container">Reflex container.</param>
        /// <param name="prefab">Component prefab.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <returns>The instantiated and hydrated component.</returns>
        public static T InstantiateComponentWithRelations<T>(
            this Container container,
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
            return container.InjectWithRelations(instance);
        }

        /// <summary>
        /// Instantiates a GameObject prefab, injects the hierarchy, and assigns relational fields.
        /// </summary>
        /// <param name="container">Reflex container.</param>
        /// <param name="prefab">GameObject prefab.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="includeInactiveChildren">
        /// When true, includes inactive children during relational assignment.
        /// </param>
        /// <returns>The instantiated GameObject.</returns>
        public static GameObject InstantiateGameObjectWithRelations(
            this Container container,
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
            container.InjectGameObjectWithRelations(instance, includeInactiveChildren);
            return instance;
        }

        /// <summary>
        /// Injects all components in a hierarchy and assigns relational fields.
        /// </summary>
        /// <param name="container">Reflex container.</param>
        /// <param name="root">Root GameObject.</param>
        /// <param name="includeInactiveChildren">
        /// When true, includes inactive children during relational assignment.
        /// </param>
        public static void InjectGameObjectWithRelations(
            this Container container,
            GameObject root,
            bool includeInactiveChildren = true
        )
        {
            if (root == null)
            {
                return;
            }

            if (container != null)
            {
                GameObjectInjector.InjectRecursive(root, container);
            }

            container.AssignRelationalHierarchy(root, includeInactiveChildren);
        }

        private static IRelationalComponentAssigner ResolveAssigner(Container container)
        {
            if (container == null)
            {
                return null;
            }

            if (container.HasBinding<IRelationalComponentAssigner>())
            {
                return container.Resolve<IRelationalComponentAssigner>();
            }

            return null;
        }
    }
}
#endif
