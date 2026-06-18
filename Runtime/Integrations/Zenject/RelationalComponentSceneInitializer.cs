// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if ZENJECT_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Zenject
{
    using System;
    using System.Collections.Generic;
    using global::Zenject;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Scene-level entry point that assigns relational component fields immediately after Zenject
    /// finishes injecting the container.
    /// </summary>
    /// <remarks>
    /// Registered automatically when you add <see cref="RelationalComponentsInstaller"/> to a
    /// <c>SceneContext</c> or bind it manually. Uses <see cref="AttributeMetadataCache"/> when
    /// available to discover component types that contain relational attributes, then hydrates those
    /// fields across the active scene.
    /// </remarks>
    /// <example>
    /// <code>
    /// // In SceneContext, add RelationalComponentsInstaller to enable scene-wide assignment
    /// [AddComponentMenu("Installers/Game Installer")]
    /// public sealed class GameInstaller : MonoInstaller
    /// {
    ///     public override void InstallBindings()
    ///     {
    ///         // Your app bindings here
    ///     }
    /// }
    /// </code>
    /// </example>
    public sealed class RelationalComponentSceneInitializer : IInitializable
    {
        private readonly IRelationalComponentAssigner _assigner;
        private readonly AttributeMetadataCache _metadataCache;
        private readonly RelationalSceneAssignmentOptions _options;

        public RelationalComponentSceneInitializer(
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
            AttributeMetadataCache cache = _metadataCache ?? AttributeMetadataCache.Instance;
            if (cache == null)
            {
                return;
            }

            using PooledResource<List<Type>> pooledTypes = Buffers<Type>.List.Get(
                out List<Type> relationalTypes
            );
            cache.CollectRelationalComponentTypes(relationalTypes);

            if (relationalTypes.Count == 0)
            {
                // Fallback: scan all components in the active scene and assign when type has relational fields
                bool includeInactiveAll = _options.IncludeInactive;
                Scene active = SceneManager.GetActiveScene();
                Component[] allComponents = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                    includeInactiveAll
                );

                for (int i = 0; i < allComponents.Length; i++)
                {
                    Component c = allComponents[i];
                    if (c == null || c.gameObject.scene != active)
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
                foreach (Type relationalType in relationalTypes)
                {
                    if (relationalType != null)
                    {
                        relationalSet.Add(relationalType);
                    }
                }

                Component[] all = UnityObjectExtensions.FindObjectsOfTypeShim<Component>(
                    includeInactive
                );

                foreach (Component c in all)
                {
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

                    UnityEngine.Object[] located = UnityObjectExtensions.FindObjectsOfTypeShim(
                        componentType,
                        includeInactive
                    );

                    foreach (UnityEngine.Object candidate in located)
                    {
                        if (candidate is not Component component)
                        {
                            continue;
                        }

                        if (component == null || component.gameObject.scene != activeScene)
                        {
                            continue;
                        }

                        _assigner.Assign(component);
                    }
                }
            }

            // Safety net in Editor/tests: also walk scene roots to ensure coverage
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
