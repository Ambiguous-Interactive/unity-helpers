// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Reflex
{
    using System;
    using System.Collections.Generic;
    using global::Reflex.Core;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class RelationalReflexSceneBootstrapper
    {
        public static void ConfigureScene(
            Container container,
            Scene scene,
            RelationalSceneAssignmentOptions options,
            bool assignSceneOnInitialize,
            bool listenForAdditiveScenes
        )
        {
            if (container == null)
            {
                return;
            }

            IRelationalComponentAssigner assigner = ResolveAssigner(container);
            if (assigner == null)
            {
                return;
            }

            AttributeMetadataCache metadataCache = ResolveMetadata(container);

            if (assignSceneOnInitialize)
            {
                AssignScene(scene, assigner, metadataCache, options);
            }

            if (listenForAdditiveScenes)
            {
                RelationalSceneLoadListener listener = new(assigner, metadataCache, options, scene);
                listener.Activate();
            }
        }

        internal static void AssignScene(
            Scene scene,
            IRelationalComponentAssigner assigner,
            AttributeMetadataCache metadataCache,
            RelationalSceneAssignmentOptions options
        )
        {
            if (!scene.IsValid() || assigner == null)
            {
                return;
            }

            AttributeMetadataCache cache = metadataCache ?? AttributeMetadataCache.Instance;
            if (cache == null)
            {
                AssignBySceneRoots(scene, assigner, options.IncludeInactive);
                return;
            }

            using PooledResource<List<Type>> typeBuffer = Buffers<Type>.List.Get(
                out List<Type> relationalTypes
            );
            cache.CollectRelationalComponentTypes(relationalTypes);

            if (relationalTypes.Count == 0)
            {
                AssignBySceneRoots(scene, assigner, options.IncludeInactive);
                return;
            }

            if (options.UseSinglePassScan)
            {
                AssignBySinglePass(scene, assigner, relationalTypes, options.IncludeInactive);
            }
            else
            {
                AssignByTypePass(scene, assigner, relationalTypes, options.IncludeInactive);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                AssignBySceneRoots(scene, assigner, options.IncludeInactive);
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (scene.IsValid())
                    {
                        AssignBySceneRoots(scene, assigner, options.IncludeInactive);
                    }
                };
            }
#endif
        }

        private static void AssignByTypePass(
            Scene target,
            IRelationalComponentAssigner assigner,
            List<Type> relationalTypes,
            bool includeInactive
        )
        {
            using PooledResource<List<GameObject>> rootBuffer = Buffers<GameObject>.List.Get(
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
                Type type = relationalTypes[i];
                if (type != null)
                {
                    relationalSet.Add(type);
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
                            assigner.Assign(component);
                            break;
                        }
                        current = current.BaseType;
                    }
                }

                components.Clear();
            }
        }

        private static void AssignBySinglePass(
            Scene target,
            IRelationalComponentAssigner assigner,
            List<Type> relationalTypes,
            bool includeInactive
        )
        {
            using PooledResource<HashSet<Type>> pooledSet = Buffers<Type>.HashSet.Get(
                out HashSet<Type> relationalSet
            );
            for (int i = 0; i < relationalTypes.Count; i++)
            {
                Type type = relationalTypes[i];
                if (type != null)
                {
                    relationalSet.Add(type);
                }
            }

            Component[] allComponents = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                includeInactive
            );

            for (int i = 0; i < allComponents.Length; i++)
            {
                Component component = allComponents[i];
                if (component == null || component.gameObject.scene != target)
                {
                    continue;
                }

                Type current = component.GetType();
                while (current != null && typeof(Component).IsAssignableFrom(current))
                {
                    if (relationalSet.Contains(current))
                    {
                        assigner.Assign(component);
                        break;
                    }
                    current = current.BaseType;
                }
            }
        }

        private static void AssignBySceneRoots(
            Scene target,
            IRelationalComponentAssigner assigner,
            bool includeInactive
        )
        {
            if (!target.IsValid())
            {
                return;
            }

            using PooledResource<List<GameObject>> rootBuffer = Buffers<GameObject>.List.Get(
                out List<GameObject> roots
            );
            target.GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }

                assigner.AssignHierarchy(root, includeInactive);
            }
        }

        private static IRelationalComponentAssigner ResolveAssigner(Container container)
        {
            if (container.HasBinding<IRelationalComponentAssigner>())
            {
                return container.Resolve<IRelationalComponentAssigner>();
            }
            return null;
        }

        private static AttributeMetadataCache ResolveMetadata(Container container)
        {
            if (container.HasBinding<AttributeMetadataCache>())
            {
                return container.Resolve<AttributeMetadataCache>();
            }
            return AttributeMetadataCache.Instance;
        }

        private sealed class RelationalSceneLoadListener
        {
            private readonly IRelationalComponentAssigner _assigner;
            private readonly AttributeMetadataCache _metadataCache;
            private readonly RelationalSceneAssignmentOptions _options;
            private readonly Scene _originScene;
            private bool _isActive;

            public RelationalSceneLoadListener(
                IRelationalComponentAssigner assigner,
                AttributeMetadataCache metadataCache,
                RelationalSceneAssignmentOptions options,
                Scene originScene
            )
            {
                _assigner = assigner;
                _metadataCache = metadataCache;
                _options = options;
                _originScene = originScene;
            }

            public void Activate()
            {
                if (_isActive)
                {
                    return;
                }

                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _isActive = true;
            }

            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (mode != LoadSceneMode.Additive || !_isActive)
                {
                    return;
                }

                AssignScene(scene, _assigner, _metadataCache, _options);
            }

            private void OnSceneUnloaded(Scene scene)
            {
                if (!_isActive)
                {
                    return;
                }

                if (scene == _originScene)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    SceneManager.sceneUnloaded -= OnSceneUnloaded;
                    _isActive = false;
                }
            }
        }
    }
}
#endif
