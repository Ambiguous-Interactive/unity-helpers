// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using Extension;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;
#if UNITY_EDITOR
    using UnityEditor;
#endif

#if UNITY_EDITOR
    using UnityEditor.SceneManagement;
#endif
    /// <summary>
    /// Helpers for working with UnityEngine.Object, components, and GameObjects.
    /// </summary>
    public static partial class Helpers
    {
        /// <summary>
        /// Finds and caches an instance of <typeparamref name="T"/> on a GameObject with the given tag.
        /// </summary>
        /// <param name="component">Context for logging.</param>
        /// <param name="tag">Unity tag to search.</param>
        /// <param name="log">If true, logs a warning when not found.</param>
        /// <returns>The component instance if found; otherwise default.</returns>
        /// <example>
        /// <code><![CDATA[
        /// var audio = this.Find<AudioManager>("AudioManager");
        /// ]]></code>
        /// </example>
        public static T Find<T>(this Object component, string tag, bool log = true)
            where T : Object
        {
            if (ObjectsByTag.TryGetValue(tag, out Object value))
            {
                if (value != null && value is T typed)
                {
                    return typed;
                }

                _ = ObjectsByTag.Remove(tag);
            }

            GameObject gameObject = GameObject.FindGameObjectWithTag(tag);
            if (gameObject == null)
            {
                if (log)
                {
                    component.LogWarn($"Could not find {tag}.");
                }

                return default;
            }

            if (gameObject.TryGetComponent(out T instance))
            {
                ObjectsByTag[tag] = instance;
                return instance;
            }

            if (log)
            {
                component.LogWarn(
                    $"Failed to find {typeof(T).Name} on {tag} (name: {gameObject.name}), id [{gameObject.GetUnityObjectId()}]."
                );
            }

            return default;
        }

        /// <summary>
        /// Finds and caches an instance of <typeparamref name="T"/> on a GameObject with the given tag.
        /// </summary>
        public static T Find<T>(string tag, bool log = true)
            where T : MonoBehaviour
        {
            if (ObjectsByTag.TryGetValue(tag, out Object value))
            {
                if (value is T typed && typed != null)
                {
                    return typed;
                }

                _ = ObjectsByTag.Remove(tag);
            }

            GameObject gameObject = GameObject.FindGameObjectWithTag(tag);
            if (gameObject == null)
            {
                if (log)
                {
                    LogObject.Log($"Could not find {tag}.");
                }

                return default;
            }

            if (gameObject.TryGetComponent(out T instance))
            {
                ObjectsByTag[tag] = instance;
                return instance;
            }

            if (log)
            {
                LogObject.Log($"Failed to find {typeof(T).Name} on {tag}");
            }

            return default;
        }

        /// <summary>
        /// Manually sets the cached instance for a tag.
        /// </summary>
        public static void SetInstance<T>(string tag, T instance)
            where T : MonoBehaviour
        {
            ObjectsByTag[tag] = instance;
        }

        /// <summary>
        /// Clears the cached instance for a tag if it matches the provided instance.
        /// </summary>
        public static void ClearInstance<T>(string tag, T instance)
            where T : MonoBehaviour
        {
            if (ObjectsByTag.TryGetValue(tag, out Object existing) && existing == instance)
            {
                _ = ObjectsByTag.Remove(tag);
            }
        }

        /// <summary>
        /// Returns true if the object has a component of type <typeparamref name="T"/>.
        /// </summary>
        public static bool HasComponent<T>(this Object unityObject)
            where T : Object
        {
            return unityObject switch
            {
                GameObject go => go.HasComponent<T>(),
                Component component => component.HasComponent<T>(),
                _ => false,
            };
        }

        /// <summary>
        /// Returns true if the component's GameObject has a component of type <typeparamref name="T"/>.
        /// </summary>
        public static bool HasComponent<T>(this Component component)
            where T : Object
        {
            return component.TryGetComponent<T>(out _);
        }

        /// <summary>
        /// Returns true if the GameObject has a component of type <typeparamref name="T"/>.
        /// </summary>
        public static bool HasComponent<T>(this GameObject gameObject)
            where T : Object
        {
            return gameObject.TryGetComponent<T>(out _);
        }

        /// <summary>
        /// Returns true if the object has a component of the specified type.
        /// </summary>
        public static bool HasComponent(this Object unityObject, Type type)
        {
            return unityObject switch
            {
                GameObject go => go.TryGetComponent(type, out _),
                Component component => component.TryGetComponent(type, out _),
                _ => false,
            };
        }

        /// <summary>
        /// Enables or disables all components of type <typeparamref name="T"/> on this component and its child hierarchy.
        /// </summary>
        public static void EnableRecursively<T>(
            this Component component,
            bool enabled,
            Func<T, bool> exclude = null
        )
            where T : Behaviour
        {
            if (component == null)
            {
                return;
            }

            using PooledResource<List<T>> componentBuffer = Buffers<T>.List.Get(
                out List<T> components
            );
            component.GetComponents(components);
            foreach (T behaviour in components)
            {
                if (behaviour != null && !(exclude?.Invoke(behaviour) ?? false))
                {
                    behaviour.enabled = enabled;
                }
            }

            Transform transform = component as Transform ?? component.transform;
            if (transform == null)
            {
                return;
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                child.EnableRecursively(enabled, exclude);
            }
        }

        /// <summary>
        /// Enables or disables all renderers of type <typeparamref name="T"/> on this component and its child hierarchy.
        /// </summary>
        public static void EnableRendererRecursively<T>(
            this Component component,
            bool enabled,
            Func<T, bool> exclude = null
        )
            where T : Renderer
        {
            if (component == null)
            {
                return;
            }

            T behavior = component as T ?? component.GetComponent<T>();
            if (behavior != null && !(exclude?.Invoke(behavior) ?? false))
            {
                behavior.enabled = enabled;
            }

            Transform transform = component as Transform;
            if (transform == null)
            {
                transform = component.transform;
                if (transform == null)
                {
                    return;
                }
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                child.EnableRendererRecursively(enabled, exclude);
            }
        }

        /// <summary>
        /// Destroys all direct child GameObjects. Uses DestroyImmediate in editor (edit mode), Destroy at runtime.
        /// </summary>
        public static void DestroyAllChildrenGameObjects(this GameObject gameObject)
        {
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                gameObject.EditorDestroyAllChildrenGameObjects();
            }
            else
#endif
            {
                gameObject.PlayDestroyAllChildrenGameObjects();
            }
        }

        /// <summary>
        /// Destroys all components of type <typeparamref name="T"/> on the GameObject.
        /// </summary>
        public static void DestroyAllComponentsOfType<T>(this GameObject gameObject)
            where T : Component
        {
            using PooledResource<List<T>> componentBuffer = Buffers<T>.List.Get(
                out List<T> components
            );
            gameObject.GetComponents(components);
            foreach (T component in components)
            {
                component.SmartDestroy();
            }
        }

        /// <summary>
        /// Destroys an object using DestroyImmediate in editor edit mode, otherwise Destroy (optionally delayed).
        /// Avoids deleting assets on disk; unloads asset objects instead.
        /// </summary>
        public static void SmartDestroy(this Object obj, float? afterTime = null)
        {
            if (obj == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (Application.isEditor && !Application.isPlaying)
            {
                // If this is an asset object, unload it so a fresh instance can be loaded next time.
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    Resources.UnloadAsset(obj);
                    return;
                }

                Object.DestroyImmediate(obj);
                return;
            }
#endif

            if (afterTime.HasValue)
            {
                Object.Destroy(obj, afterTime.Value);
            }
            else
            {
                Object.Destroy(obj);
            }
        }

        /// <summary>
        /// Immediately destroys all child GameObjects that match the predicate.
        /// </summary>
        public static void DestroyAllChildrenGameObjectsImmediatelyConditionally(
            this GameObject gameObject,
            Func<GameObject, bool> acceptancePredicate
        )
        {
            gameObject.InternalDestroyAllChildrenGameObjects(toDestroy =>
            {
                if (!acceptancePredicate(toDestroy))
                {
                    return;
                }

                Object.DestroyImmediate(toDestroy);
            });
        }

        /// <summary>
        /// Destroys all child GameObjects that match the predicate (play-mode safe).
        /// </summary>
        public static void DestroyAllChildGameObjectsConditionally(
            this GameObject gameObject,
            Func<GameObject, bool> acceptancePredicate
        )
        {
            gameObject.InternalDestroyAllChildrenGameObjects(toDestroy =>
            {
                if (!acceptancePredicate(toDestroy))
                {
                    return;
                }

                toDestroy.Destroy();
            });
        }

        /// <summary>
        /// Immediately destroys all direct child GameObjects.
        /// </summary>
        public static void DestroyAllChildrenGameObjectsImmediately(this GameObject gameObject) =>
            gameObject.InternalDestroyAllChildrenGameObjects(go => Object.DestroyImmediate(go));

        /// <summary>
        /// Destroys all direct child GameObjects using Destroy (play mode safe).
        /// </summary>
        public static void PlayDestroyAllChildrenGameObjects(this GameObject gameObject) =>
            gameObject.InternalDestroyAllChildrenGameObjects(go => go.Destroy());

        /// <summary>
        /// Destroys all direct child GameObjects using Destroy (editor utility).
        /// </summary>
        public static void EditorDestroyAllChildrenGameObjects(this GameObject gameObject) =>
            gameObject.InternalDestroyAllChildrenGameObjects(go => go.Destroy());

        private static void InternalDestroyAllChildrenGameObjects(
            this GameObject gameObject,
            Action<GameObject> destroyFunction
        )
        {
            for (int i = gameObject.transform.childCount - 1; 0 <= i; --i)
            {
                destroyFunction(gameObject.transform.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Returns true if the GameObject represents a prefab asset or prefab stage content (Editor), or is not in a scene (Runtime).
        /// </summary>
        public static bool IsPrefab(this GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            Scene scene = gameObject.scene;
#if UNITY_EDITOR
            if (
                scene.rootCount == 1
                && string.Equals(scene.name, gameObject.name, StringComparison.Ordinal)
            )
            {
                return true;
            }

            return PrefabUtility.GetPrefabAssetType(gameObject) switch
            {
                PrefabAssetType.NotAPrefab => false,
                PrefabAssetType.MissingAsset => scene.rootCount == 0,
                _ => true,
            };
#else
            return scene.rootCount == 0;
#endif
        }

        /// <summary>
        /// Returns true if the component's GameObject is a prefab (see <see cref="IsPrefab(UnityEngine.GameObject)"/>).
        /// </summary>
        public static bool IsPrefab(this Component component)
        {
            if (component == null)
            {
                return false;
            }

            return component.gameObject.IsPrefab();
        }

        /// <summary>
        /// Gets a component if present; otherwise adds and returns a new component of type <typeparamref name="T"/>.
        /// </summary>
        public static T GetOrAddComponent<T>(this GameObject unityObject)
            where T : Component
        {
            if (!unityObject.TryGetComponent(out T instance))
            {
                instance = unityObject.AddComponent<T>();
            }

            return instance;
        }

        /// <summary>
        /// Gets a component if present; otherwise adds and returns a new component of the specified type.
        /// </summary>
        public static Component GetOrAddComponent(this GameObject unityObject, Type componentType)
        {
            if (!unityObject.TryGetComponent(componentType, out Component instance))
            {
                instance = unityObject.AddComponent(componentType);
            }

            return instance;
        }

        /// <summary>
        /// Modifies a prefab asset by opening its contents and saving the result (Editor only). If not a prefab, applies the modification to the instance.
        /// </summary>
        public static void ModifyAndSavePrefab(GameObject prefab, Action<GameObject> modifyAction)
        {
            if (prefab == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                string assetPath = AssetDatabase.GetAssetPath(prefab);
                GameObject content = PrefabUtility.LoadPrefabContents(assetPath);

                if (content == null)
                {
                    Debug.LogError($"Unable to load {prefab} as a prefab", prefab);
                    return;
                }

                modifyAction(content);
                _ = PrefabUtility.SaveAsPrefabAsset(content, assetPath);
                PrefabUtility.UnloadPrefabContents(content);
            }
            else
            {
                modifyAction(prefab);
                PrefabStage stage = PrefabStageUtility.GetPrefabStage(prefab);
                if (stage)
                {
                    _ = EditorSceneManager.MarkSceneDirty(stage.scene);
                }
            }
#endif
        }
    }
}
