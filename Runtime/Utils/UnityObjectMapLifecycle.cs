// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Events;
    using UnityEngine.SceneManagement;

    internal static class UnityObjectMapLifecycle
    {
        private static readonly List<WeakReference<IUnityObjectMapLifecycle>> Maps = new();
        private static readonly UnityAction<Scene> SceneUnloaded = OnSceneUnloaded;

        internal static void Register(IUnityObjectMapLifecycle map)
        {
            Unregister(map);
            Maps.Add(new WeakReference<IUnityObjectMapLifecycle>(map));
            SceneManager.sceneUnloaded -= SceneUnloaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }

        internal static void Unregister(IUnityObjectMapLifecycle map)
        {
            for (int i = Maps.Count - 1; 0 <= i; --i)
            {
                if (
                    !Maps[i].TryGetTarget(out IUnityObjectMapLifecycle target)
                    || ReferenceEquals(target, map)
                )
                {
                    Maps.RemoveAt(i);
                }
            }
            if (Maps.Count == 0)
            {
                SceneManager.sceneUnloaded -= SceneUnloaded;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForPlaySession()
        {
            foreach (WeakReference<IUnityObjectMapLifecycle> reference in Maps)
            {
                if (reference.TryGetTarget(out IUnityObjectMapLifecycle map))
                {
                    map.ResetForPlaySession();
                }
            }
            Maps.Clear();
            SceneManager.sceneUnloaded -= SceneUnloaded;
        }

        internal static void OnSceneUnloaded(Scene scene)
        {
            for (int i = Maps.Count - 1; 0 <= i; --i)
            {
                if (Maps[i].TryGetTarget(out IUnityObjectMapLifecycle map))
                {
                    map.PruneDestroyedKeys();
                }
                else
                {
                    Maps.RemoveAt(i);
                }
            }
            if (Maps.Count == 0)
            {
                SceneManager.sceneUnloaded -= SceneUnloaded;
            }
        }
    }
}
