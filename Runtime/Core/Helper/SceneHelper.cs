// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_6000_4_OR_NEWER
#define UNH_HAS_FIND_OBJECTS_BY_TYPE_INACTIVE_ONLY
#endif

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Extension;
    using UnityEngine;
    using UnityEngine.Events;
    using UnityEngine.SceneManagement;
    using Utils;
    using Object = UnityEngine.Object;
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.SceneManagement;
#endif

    /// <summary>
    /// Utilities for scene discovery, loading, and object retrieval in editor and runtime.
    /// </summary>
    public static class SceneHelper
    {
        /// <summary>
        /// Returns true if a scene with the given nonblank name or path is currently loaded.
        /// </summary>
        public static bool IsSceneLoaded(string sceneNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(sceneNameOrPath))
            {
                return false;
            }

            return TryGetLoadedScene(sceneNameOrPath.SanitizePath(), out _);
        }

        /// <summary>
        /// Finds all scene asset paths under the specified search folders (Editor only).
        /// </summary>
        public static string[] GetAllScenePaths(string[] searchFolders = null)
        {
#if UNITY_EDITOR
            searchFolders ??= Array.Empty<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", searchFolders);
            if (guids.Length == 0)
            {
                return Array.Empty<string>();
            }

            using PooledResource<List<string>> lease = Buffers<string>.GetList(
                guids.Length,
                out List<string> paths
            );
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return paths.ToArray();
#else
            return Array.Empty<string>();
#endif
        }

        /// <summary>
        /// Returns all enabled scenes included in Build Settings (Editor only).
        /// </summary>
        public static string[] GetScenesInBuild()
        {
#if UNITY_EDITOR
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0)
            {
                return Array.Empty<string>();
            }

            using PooledResource<List<string>> lease = Buffers<string>.GetList(
                scenes.Length,
                out List<string> paths
            );
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (scene.enabled)
                {
                    paths.Add(scene.path);
                }
            }
            return paths.ToArray();
#else
            return Array.Empty<string>();
#endif
        }

        /// <summary>
        /// Borrows a loaded scene or loads one additively and returns its first object of type <typeparamref name="T"/> with owned-scene cleanup.
        /// </summary>
        /// <remarks>Call on Unity's main thread. Disposal marshals back to the captured Unity context when needed.</remarks>
        public static async ValueTask<DeferredDisposalResult<T>> GetObjectOfTypeInScene<T>(
            string scenePath
        )
            where T : Object
        {
            DeferredDisposalResult<T[]> result = await GetAllObjectsOfTypeInScene<T>(scenePath);
            T value = result.result.Length == 0 ? default : result.result[0];
            return new DeferredDisposalResult<T>(value, result.DisposeAsync);
        }

        /// <summary>
        /// Borrows a loaded scene or loads one additively and returns all its objects of type <typeparamref name="T"/> with owned-scene cleanup.
        /// </summary>
        /// <remarks>Call on Unity's main thread. Disposal marshals back to the captured Unity context when needed.</remarks>
        public static async ValueTask<DeferredDisposalResult<T[]>> GetAllObjectsOfTypeInScene<T>(
            string scenePath
        )
            where T : Object
        {
            SynchronizationContext context = SynchronizationContext.Current;
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            SceneLoadScope sceneScope = new(scenePath, null);
            T[] result;
            try
            {
                Scene scene = await sceneScope.LoadTask;
                result =
                    scene.IsValid() && scene.isLoaded
                        ? FindObjectsInScene<T>(scene)
                        : Array.Empty<T>();
            }
            catch
            {
                await sceneScope.DisposeAsync();
                throw;
            }

            return new DeferredDisposalResult<T[]>(
                result,
                () => DisposeSceneAsync(sceneScope, context, mainThreadId)
            );
        }

        private static ValueTask DisposeSceneAsync(
            SceneLoadScope sceneScope,
            SynchronizationContext context,
            int mainThreadId
        )
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                return sceneScope.DisposeAsync();
            }
            if (context == null)
            {
                return new ValueTask(
                    Task.FromException(
                        new InvalidOperationException(
                            "Scene disposal requires the calling Unity main thread or its synchronization context."
                        )
                    )
                );
            }

            TaskCompletionSource<bool> completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            try
            {
                context.Post(
                    async _ =>
                    {
                        try
                        {
                            await sceneScope.DisposeAsync();
                            completion.TrySetResult(true);
                        }
                        catch (Exception exception)
                        {
                            completion.TrySetException(exception);
                        }
                    },
                    null
                );
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            return new ValueTask(completion.Task);
        }

        private static T[] FindObjectsInScene<T>(Scene scene)
            where T : Object
        {
#if UNH_HAS_FIND_OBJECTS_BY_TYPE_INACTIVE_ONLY
            T[] allObjects = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#else
            T[] allObjects = Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
#endif
            using PooledResource<List<T>> lease = Buffers<T>.GetList(
                allObjects.Length,
                out List<T> filtered
            );
            foreach (T obj in allObjects)
            {
                GameObject go = obj.GetGameObject();
                if (go != null && go.scene == scene)
                {
                    filtered.Add(obj);
                }
            }
            return filtered.Count == 0 ? Array.Empty<T>() : filtered.ToArray();
        }

        private static bool TryGetLoadedScene(string sceneNameOrPath, out Scene loadedScene)
        {
            for (int index = 0; index < SceneManager.sceneCount; ++index)
            {
                Scene candidate = SceneManager.GetSceneAt(index);
                if (
                    candidate.IsValid()
                    && candidate.isLoaded
                    && (
                        string.Equals(candidate.name, sceneNameOrPath, StringComparison.Ordinal)
                        || string.Equals(candidate.path, sceneNameOrPath, StringComparison.Ordinal)
                    )
                )
                {
                    loadedScene = candidate;
                    return true;
                }
            }
            loadedScene = default;
            return false;
        }

        private static string ResolveScenePath(string sceneNameOrPath)
        {
            string normalized = sceneNameOrPath.SanitizePath();
#if UNITY_EDITOR
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled)
                {
                    continue;
                }
                string buildPath = buildScene.path;
#else
            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; ++index)
            {
                string buildPath = SceneUtility.GetScenePathByBuildIndex(index);
#endif
                if (
                    string.Equals(buildPath, normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Path.ChangeExtension(buildPath, null),
                        normalized,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || Path.ChangeExtension(buildPath, null)
                        .EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Path.GetFileNameWithoutExtension(buildPath),
                        normalized,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return buildPath;
                }
            }
#if UNITY_EDITOR
            if (Path.IsPathRooted(normalized))
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrWhiteSpace(projectRoot))
                {
                    string rootPrefix = projectRoot.SanitizePath().TrimEnd('/') + "/";
                    if (normalized.StartsWith(rootPrefix, StringComparison.Ordinal))
                    {
                        return normalized.Substring(rootPrefix.Length);
                    }
                }
            }
#endif
            return normalized;
        }

        private static bool IsSceneAvailable(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return false;
            }

#if UNITY_EDITOR
            return AssetDatabase.GetMainAssetTypeAtPath(scenePath) == typeof(SceneAsset)
                || Application.CanStreamedLevelBeLoaded(scenePath);
#else
            return Application.CanStreamedLevelBeLoaded(scenePath);
#endif
        }

        /// <summary>
        /// A helper scope that ensures a target scene is loaded and provides an async disposal to unload it.
        /// </summary>
        public sealed class SceneLoadScope
        {
            /// <summary>
            /// Gets the loaded scene, an invalid scene for an unavailable input, or the load/callback failure.
            /// </summary>
            public Task<Scene> LoadTask => _loadCompletion.Task;

            private readonly TaskCompletionSource<Scene> _loadCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            private Scene? _openedScene;
            private UnityAction<Scene, LoadSceneMode> _onSceneLoaded;
            private TaskCompletionSource<bool> _disposalCompletion;
            private bool _eventAdded;
            private bool _disposed;

            /// <summary>
            /// Borrows any matching loaded scene or loads an available scene additively on Unity's main thread.
            /// </summary>
            public SceneLoadScope(string scenePath, UnityAction<Scene, LoadSceneMode> onSceneLoaded)
            {
                _onSceneLoaded = onSceneLoaded;
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    CompleteScene(default, LoadSceneMode.Additive);
                    return;
                }

                try
                {
                    string normalized = scenePath.SanitizePath();
                    if (TryGetLoadedScene(normalized, out Scene borrowed))
                    {
                        CompleteScene(
                            borrowed,
                            borrowed == SceneManager.GetActiveScene()
                                ? LoadSceneMode.Single
                                : LoadSceneMode.Additive
                        );
                        return;
                    }
                    string resolved = ResolveScenePath(normalized);
                    if (TryGetLoadedScene(resolved, out borrowed))
                    {
                        CompleteScene(
                            borrowed,
                            borrowed == SceneManager.GetActiveScene()
                                ? LoadSceneMode.Single
                                : LoadSceneMode.Additive
                        );
                        return;
                    }
                    if (!IsSceneAvailable(resolved))
                    {
                        CompleteScene(default, LoadSceneMode.Additive);
                        return;
                    }
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        _openedScene = EditorSceneManager.OpenScene(
                            resolved,
                            OpenSceneMode.Additive
                        );
                        CompleteScene(_openedScene.Value, LoadSceneMode.Additive);
                        return;
                    }
#endif
                    SceneManager.sceneLoaded += OnSceneLoaded;
                    _eventAdded = true;
                    LoadSceneParameters parameters = new(
                        LoadSceneMode.Additive,
                        LocalPhysicsMode.None
                    );
#if UNITY_EDITOR
                    _openedScene = EditorSceneManager.LoadSceneInPlayMode(resolved, parameters);
#else
                    _openedScene = SceneManager.LoadScene(resolved, parameters);
#endif
                    if (!_openedScene.Value.IsValid())
                    {
                        FailLoad(
                            new InvalidOperationException("Unity returned an invalid loaded scene.")
                        );
                    }
                    else if (_openedScene.Value.isLoaded)
                    {
                        CompleteScene(_openedScene.Value, LoadSceneMode.Additive);
                    }
                }
                catch (Exception exception)
                {
                    FailLoad(exception);
                }
            }

            /// <summary>
            /// Waits for loading and unloads only the scene opened by this scope; repeated calls share completion.
            /// </summary>
            /// <remarks>Call on Unity's main thread. Load, callback and unload failures remain observable.</remarks>
            public ValueTask DisposeAsync()
            {
                if (_disposalCompletion != null)
                {
                    return new ValueTask(_disposalCompletion.Task);
                }

                _disposalCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _disposed = true;
                _onSceneLoaded = null;
                _ = DisposeOwnedSceneAsync();
                return new ValueTask(_disposalCompletion.Task);
            }

            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (_openedScene.HasValue && scene == _openedScene.Value)
                {
                    CompleteScene(scene, mode);
                }
            }

            private void CompleteScene(Scene scene, LoadSceneMode mode)
            {
                DetachSceneLoadedHandler();
                UnityAction<Scene, LoadSceneMode> callback = _onSceneLoaded;
                _onSceneLoaded = null;
                try
                {
                    if (!_disposed && scene.IsValid())
                    {
                        callback?.Invoke(scene, mode);
                    }
                    _loadCompletion.TrySetResult(scene);
                }
                catch (Exception exception)
                {
                    _loadCompletion.TrySetException(exception);
                }
            }

            private void FailLoad(Exception exception)
            {
                DetachSceneLoadedHandler();
                _onSceneLoaded = null;
                _loadCompletion.TrySetException(exception);
            }

            private void DetachSceneLoadedHandler()
            {
                if (_eventAdded)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    _eventAdded = false;
                }
            }

            private async Task DisposeOwnedSceneAsync()
            {
                Exception failure = null;
                try
                {
                    await LoadTask;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                try
                {
                    DetachSceneLoadedHandler();
                    if (_openedScene.HasValue)
                    {
                        Scene opened = _openedScene.Value;
                        if (opened.IsValid() && opened.isLoaded)
                        {
#if UNITY_EDITOR
                            if (!Application.isPlaying)
                            {
                                if (!EditorSceneManager.CloseScene(opened, true))
                                {
                                    throw new InvalidOperationException(
                                        "Unity could not close the owned scene."
                                    );
                                }
                            }
                            else
#endif
                            {
                                AsyncOperation unload = SceneManager.UnloadSceneAsync(
                                    opened,
                                    UnloadSceneOptions.None
                                );
                                if (unload == null)
                                {
                                    throw new InvalidOperationException(
                                        "Unity did not start unloading the owned scene."
                                    );
                                }
                                await unload;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    failure =
                        failure == null ? exception : new AggregateException(failure, exception);
                }

                if (failure == null)
                {
                    _disposalCompletion.TrySetResult(true);
                }
                else
                {
                    _disposalCompletion.TrySetException(failure);
                }
            }
        }
    }
}
