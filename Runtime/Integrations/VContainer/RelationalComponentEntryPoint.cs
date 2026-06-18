// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.VContainer
{
    using System;
    using System.Collections.Generic;
    using global::VContainer.Unity;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Entry point registered with VContainer to hydrate relational attributes once the container is
    /// fully built.
    /// </summary>
    /// <remarks>
    /// This type is registered automatically by calling
    /// <see cref="RelationalComponentsBuilderExtensions"/>,
    /// and runs once on startup to assign relational fields across the active scene. It uses
    /// <see cref="AttributeMetadataCache"/> (when present) to quickly locate components that have
    /// relational attributes.
    /// </remarks>
    /// <example>
    /// <code>
    /// // LifetimeScope that enables relational assignments at scene start
    /// public sealed class GameLifetimeScope : LifetimeScope
    /// {
    ///     protected override void Configure(IContainerBuilder builder)
    ///     {
    ///         builder.RegisterRelationalComponents(
    ///             new RelationalSceneAssignmentOptions(includeInactive: true)
    ///         );
    ///     }
    /// }
    /// </code>
    /// </example>
    public sealed class RelationalComponentEntryPoint : IInitializable
    {
        private readonly IRelationalComponentAssigner _assigner;
        private readonly AttributeMetadataCache _metadataCache;
        private readonly RelationalSceneAssignmentOptions _options;

        public RelationalComponentEntryPoint(
            IRelationalComponentAssigner assigner,
            AttributeMetadataCache metadataCache,
            RelationalSceneAssignmentOptions options
        )
        {
            _assigner = assigner ?? throw new ArgumentNullException(nameof(assigner));
            _metadataCache = metadataCache;
            _options = options;
        }

        public void Initialize()
        {
            AttributeMetadataCache cache = _metadataCache;
            if (cache == null)
            {
                cache = AttributeMetadataCache.Instance;
                if (cache == null)
                {
                    return;
                }
            }

            using PooledResource<List<Type>> pooledTypes = Buffers<Type>.List.Get(
                out List<Type> relationalTypes
            );
            cache.CollectRelationalComponentTypes(relationalTypes);

            if (relationalTypes.Count == 0)
            {
                // Fallback: scan all components once and assign when type has relational fields
                bool includeInactiveAll = _options.IncludeInactive;
                Component[] allComponents = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                    includeInactiveAll
                );

                for (int i = 0; i < allComponents.Length; i++)
                {
                    Component c = allComponents[i];
                    if (c == null || c.gameObject.scene != SceneManager.GetActiveScene())
                    {
                        continue;
                    }

                    if (_assigner.HasRelationalAssignments(c.GetType()))
                    {
                        _assigner.Assign(c);
                    }
                }
                return;
            }

            bool includeInactive = _options.IncludeInactive;
            Scene activeScene = SceneManager.GetActiveScene();

            if (_options.UseSinglePassScan)
            {
                using PooledResource<HashSet<Type>> pooledSet = Buffers<Type>.HashSet.Get(
                    out HashSet<Type> relationalSet
                );
                for (int i = 0; i < relationalTypes.Count; i++)
                {
                    Type t = relationalTypes[i];
                    if (t != null)
                    {
                        relationalSet.Add(t);
                    }
                }

                Component[] all = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                    includeInactive
                );

                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i];
                    if (c == null || c.gameObject.scene != activeScene)
                    {
                        continue;
                    }

                    Type t = c.GetType();
                    while (t != null && typeof(Component).IsAssignableFrom(t))
                    {
                        if (relationalSet.Contains(t))
                        {
                            _assigner.Assign(c);
                            break;
                        }
                        t = t.BaseType;
                    }
                }
            }
            else
            {
                foreach (Type componentType in relationalTypes)
                {
                    if (componentType == null)
                    {
                        continue;
                    }

                    Object[] located = UnityObjectExtensions.FindObjectsOfTypeShim(
                        componentType,
                        includeInactive
                    );

                    foreach (Object t in located)
                    {
                        if (t is not Component component)
                        {
                            continue;
                        }

                        if (component.gameObject.scene != activeScene)
                        {
                            continue;
                        }

                        _assigner.Assign(component);
                    }
                }
            }

            // Safety net in Editor/tests: also walk scene roots to catch any missed
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                using PooledResource<List<GameObject>> rootGoBuffer = Buffers<GameObject>.List.Get(
                    out List<GameObject> roots
                );
                activeScene.GetRootGameObjects(roots);
                foreach (GameObject root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }
                    _assigner.AssignHierarchy(root, includeInactive);
                }
            }
#endif
        }
    }
}
#endif
