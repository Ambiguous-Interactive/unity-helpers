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

        internal AuthoredAssetObjectIndex(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            try
            {
                IsImported = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath));
                if (!IsImported)
                {
                    return;
                }
                if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    Scene scene = SceneManager.GetSceneByPath(assetPath);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
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
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        AddHierarchy(root);
                    }
                    return;
                }

                foreach (Object instance in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    Add(instance);
                    if (instance is GameObject root)
                    {
                        AddHierarchy(root);
                    }
                }
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
                    && !EditorSceneManager.CloseScene(_openedScene, removeScene: true)
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

        private void AddHierarchy(GameObject root)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                Add(component);
            }
        }

        private void Add(Object instance)
        {
            if (instance == null)
            {
                return;
            }
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(instance);
            if (id.targetObjectId != 0)
            {
                _objects[unchecked((long)id.targetObjectId)] = instance;
            }
        }
    }
#endif
}
