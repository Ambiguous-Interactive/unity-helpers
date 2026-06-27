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

    /// <summary>
    /// Listens for additive scene loads and assigns relational fields for components in the
    /// newly loaded scene using the configured options.
    /// </summary>
    public sealed class RelationalSceneLoadListener : IInitializable, IDisposable
    {
        private readonly IRelationalComponentAssigner _assigner;
        private readonly AttributeMetadataCache _metadataCache;
        private readonly RelationalSceneAssignmentOptions _options;

        public RelationalSceneLoadListener(
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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        internal void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Additive-only, matching this class's summary and the Reflex sibling. A single-mode
            // LoadScene replaces the scene (the scoped container is normally torn down with it), so
            // re-assigning here is wrong -- and a listener that outlived its container would re-emit
            // another fixture's required-field [Error] into whatever later test triggered the load
            // (e.g. UnityMainThreadDispatcherTests.StartSceneOnlyOneInstance's LoadScene(0)).
            if (mode != LoadSceneMode.Additive)
            {
                return;
            }

            if (!scene.IsValid())
            {
                return;
            }

            AttributeMetadataCache cache = _metadataCache ?? AttributeMetadataCache.Instance;
            if (cache == null)
            {
                return;
            }

            using PooledResource<List<Type>> pooledTypes = Buffers<Type>.List.Get(
                out List<Type> relationalTypes
            );
            cache.CollectRelationalComponentTypes(relationalTypes);

            bool includeInactive = _options.IncludeInactive;
            if (relationalTypes.Count == 0)
            {
                // Fallback: scan all components once and assign when type has relational fields
                Component[] all = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                    includeInactive
                );

                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i];
                    if (c == null || c.gameObject.scene != scene)
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

            if (_options.UseSinglePassScan)
            {
                AssignBySinglePass(scene, relationalTypes, includeInactive);
            }
            else
            {
                AssignByTypePass(scene, relationalTypes, includeInactive);
            }

            // Safety net in Editor/tests: walk scene roots to catch any missed components
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                AssignBySceneRoots(scene, includeInactive);

                // In EditMode, object registration can lag a frame after scene creation.
                // Schedule a follow-up pass to catch late-registered components.
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (scene.IsValid())
                    {
                        AssignBySceneRoots(scene, includeInactive);
                    }
                };
            }
#endif
        }

        private void AssignByTypePass(
            Scene target,
            List<Type> relationalTypes,
            bool includeInactive
        )
        {
            using PooledResource<List<GameObject>> rootGoBuffer = Buffers<GameObject>.List.Get(
                out List<GameObject> roots
            );
            target.GetRootGameObjects(roots);
            if (roots.Count == 0)
            {
                return;
            }

            using PooledResource<HashSet<Type>> typeSetPool = Buffers<Type>.HashSet.Get(
                out HashSet<Type> relationalSet
            );
            for (int i = 0; i < relationalTypes.Count; i++)
            {
                Type relationalType = relationalTypes[i];
                if (relationalType != null)
                {
                    relationalSet.Add(relationalType);
                }
            }

            using PooledResource<List<Component>> componentBuffer = Buffers<Component>.List.Get(
                out List<Component> components
            );

            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }

                root.GetComponentsInChildren(includeInactive, components);
                for (int j = 0; j < components.Count; j++)
                {
                    Component component = components[j];
                    if (component == null || component.gameObject.scene != target)
                    {
                        continue;
                    }

                    Type current = component.GetType();
                    while (current != null && typeof(Component).IsAssignableFrom(current))
                    {
                        if (relationalSet.Contains(current))
                        {
                            _assigner.Assign(component);
                            break;
                        }
                        current = current.BaseType;
                    }
                }

                components.Clear();
            }
        }

        private void AssignBySinglePass(
            Scene target,
            List<Type> relationalTypes,
            bool includeInactive
        )
        {
            using PooledResource<HashSet<Type>> pooledSet = Buffers<Type>.HashSet.Get(
                out HashSet<Type> relationalSet
            );
            foreach (Type t in relationalTypes)
            {
                if (t != null)
                {
                    relationalSet.Add(t);
                }
            }

            Component[] all = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                includeInactive
            );

            foreach (Component component in all)
            {
                if (component == null || component.gameObject.scene != target)
                {
                    continue;
                }

                Type t = component.GetType();
                while (t != null && typeof(Component).IsAssignableFrom(t))
                {
                    if (relationalSet.Contains(t))
                    {
                        _assigner.Assign(component);
                        break;
                    }
                    t = t.BaseType;
                }
            }
        }

        private void AssignBySceneRoots(Scene target, bool includeInactive)
        {
            int rootCount = target.rootCount;
            if (rootCount == 0)
            {
                return;
            }
            using PooledResource<List<GameObject>> rootGoBuffer = Buffers<GameObject>.List.Get(
                out List<GameObject> roots
            );
            target.GetRootGameObjects(roots);
            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }
                _assigner.AssignHierarchy(root, includeInactive);
            }
        }
    }
}
#endif
