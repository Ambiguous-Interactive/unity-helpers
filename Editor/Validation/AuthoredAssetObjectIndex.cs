// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;

    internal sealed class AuthoredAssetObjectIndex : IDisposable
    {
        internal bool IsImported { get; private set; }

        internal bool Failed { get; private set; }

        private readonly Dictionary<long, Object> _objects = new();
        private Scene _openedScene;
        private bool _removeOpenedScene;

        internal AuthoredAssetObjectIndex(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            try
            {
                IsImported = !string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(assetPath));
                if (!IsImported)
                {
                    return;
                }
                if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    Scene scene = SceneManager.GetSceneByPath(assetPath);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        _removeOpenedScene = !scene.IsValid();
                        Scene previousActive = SceneManager.GetActiveScene();
                        _openedScene = EditorSceneManager.OpenScene(
                            assetPath,
                            OpenSceneMode.Additive
                        );
                        scene = _openedScene;
                        if (previousActive.IsValid() && previousActive.isLoaded)
                        {
                            SceneManager.SetActiveScene(previousActive);
                        }
                    }
                    if (scene.isDirty)
                    {
                        Failed = true;
                        return;
                    }
                    List<Object> components = new();
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        components.AddRange(root.GetComponentsInChildren<Component>(true));
                    }
                    AddAll(components);
                    return;
                }

                AddAll(AssetDatabase.LoadAllAssetsAtPath(assetPath));
            }
            catch (Exception)
            {
                Failed = true;
                _objects.Clear();
            }
        }

        public void Dispose()
        {
            _objects.Clear();
            try
            {
                if (
                    _openedScene.IsValid()
                    && _openedScene.isLoaded
                    && !EditorSceneManager.CloseScene(_openedScene, removeScene: _removeOpenedScene)
                )
                {
                    Debug.LogWarning(
                        $"Could not close the scene opened for authored asset inspection: {_openedScene.path}"
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not close the scene opened for authored asset inspection: {exception.Message}"
                );
            }
            finally
            {
                _openedScene = default;
            }
        }

        internal bool TryGetValue(long fileId, out Object instance)
        {
            return _objects.TryGetValue(fileId, out instance);
        }

        /// <summary>Indexes each live authored object with one batch ID lookup.</summary>
        /// <remarks>
        /// Prefab loading already includes descendants. Repeating hierarchy walks made deep
        /// prefabs costlier. Paired 8/32/96-node Editor probes reduced index time and allocation
        /// events; small and flat full-scan timings were inconclusive. See PR #1115 for the captures.
        /// </remarks>
        private void AddAll(IReadOnlyList<Object> instances)
        {
            List<Object> valid = new(instances.Count);
            for (int index = 0; index < instances.Count; ++index)
            {
                Object instance = instances[index];
                if (instance != null)
                {
                    valid.Add(instance);
                }
            }
            if (valid.Count == 0)
            {
                return;
            }
            Object[] objects = valid.ToArray();
            GlobalObjectId[] ids = new GlobalObjectId[objects.Length];
            GlobalObjectId.GetGlobalObjectIdsSlow(objects, ids);
            for (int index = 0; index < objects.Length; ++index)
            {
                if (ids[index].targetObjectId != 0)
                {
                    _objects[unchecked((long)ids[index].targetObjectId)] = objects[index];
                }
            }
        }
    }
#endif
}
