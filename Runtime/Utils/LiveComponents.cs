// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Discovers active components by component class, base class or interface.</summary>
    /// <typeparam name="T">The component or interface type to track.</typeparam>
    /// <remarks>
    /// No object setup or lifecycle changes are required. Valid first matches use the cache;
    /// misses, counts and copies reconcile loaded scenes, including DontDestroyOnLoad.
    /// Results retain first-discovery order and exclude inactive objects and disabled Behaviours.
    /// Main-thread operations observe same-frame changes. Workers return empty results and leave buffers unchanged.
    /// </remarks>
    public static class LiveComponents<T>
        where T : class
    {
        /// <summary>Gets the current number of active matching components.</summary>
        public static int Count
        {
            get
            {
                if (!Prepare())
                {
                    return 0;
                }
                Reconcile();
                int count = 0;
                foreach (Component component in Entries)
                {
                    if (LiveComponentRegistry.IsActive(component))
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        private static readonly bool Supported =
            typeof(T).IsInterface || typeof(Component).IsAssignableFrom(typeof(T));
        private static readonly List<Component> Entries = new();
        private static readonly List<T> Scratch = new();
        private static readonly List<GameObject> Roots = new();
        private static readonly HashSet<Component> Known = new(
            ReferenceIdentityComparer<Component>.Instance
        );
        private static readonly HashSet<Component> Seen = new(
            ReferenceIdentityComparer<Component>.Instance
        );
        private static bool _tracked;
        private static bool _cold = true;

        /// <summary>Returns true with the first active match, or false with a null output.</summary>
        public static bool TryGetFirst(out T found)
        {
            if (Prepare())
            {
                if (TryFind(default, false, out T cached))
                {
                    found = cached;
                    return true;
                }
                Reconcile();
                return TryFind(default, false, out found);
            }
            found = null;
            return false;
        }

        /// <summary>Returns true with the first active match in the scene, or false with a null output.</summary>
        public static bool TryGetIn(Scene scene, out T found)
        {
            if (Prepare() && scene.IsValid())
            {
                if (TryFind(scene, true, out T cached))
                {
                    found = cached;
                    return true;
                }
                Reconcile();
                return TryFind(scene, true, out found);
            }
            found = null;
            return false;
        }

        /// <summary>Replaces a caller list with all active matches in first-discovery order.</summary>
        /// <remarks>Null is ignored. Initial discovery, scene loading and buffer growth can allocate.</remarks>
        public static void CopyTo(List<T> destination)
        {
            if (!LiveComponentRegistry.CanAccess || destination == null)
            {
                return;
            }
            destination.Clear();
            if (!Prepare())
            {
                return;
            }
            Reconcile();
            foreach (Component component in Entries)
            {
                if (LiveComponentRegistry.IsActive(component))
                {
                    destination.Add((T)(object)component);
                }
            }
        }

        private static bool Prepare()
        {
            if (!LiveComponentRegistry.CanAccess || !Supported)
            {
                return false;
            }
            if (!_tracked)
            {
                LiveComponentRegistry.TrackType(Reset);
                _tracked = true;
            }
            return true;
        }

        private static bool TryFind(Scene scene, bool inScene, out T found)
        {
            foreach (Component component in Entries)
            {
                if (
                    LiveComponentRegistry.IsActive(component)
                    && (!inScene || component.gameObject.scene == scene)
                )
                {
                    found = (T)(object)component;
                    return true;
                }
            }
            found = null;
            return false;
        }

        private static void Reconcile()
        {
            Seen.Clear();
            bool loading = false;
            int sceneCount = SceneManager.sceneCount;
            for (int index = 0; index < sceneCount; ++index)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded)
                {
                    Visit(scene);
                }
                else
                {
                    loading = true;
                }
            }
            Scene persistent = LiveComponentRegistry.GetPersistentScene();
            if (persistent.IsValid() && persistent.isLoaded)
            {
                Visit(persistent);
            }
            if (_cold || loading)
            {
                Type searchType = typeof(T).IsInterface ? typeof(Component) : typeof(T);
                foreach (
                    UnityEngine.Object item in UnityObjectExtensions.FindObjectsOfTypeShim(
                        searchType,
                        true
                    )
                )
                {
                    if (
                        item is T
                        && item is Component component
                        && component.gameObject.scene.IsValid()
                        && !LiveComponentRegistry.IsAnchor(component.gameObject)
                    )
                    {
                        Observe(component);
                    }
                }
                _cold = false;
            }
            int write = 0;
            int count = Entries.Count;
            for (int read = 0; read < count; ++read)
            {
                Component component = Entries[read];
                if (component == null || !Seen.Contains(component))
                {
                    Known.Remove(component);
                    continue;
                }
                Entries[write++] = component;
            }
            if (write < count)
            {
                Entries.RemoveRange(write, count - write);
            }
            Scratch.Clear();
            Roots.Clear();
            Seen.Clear();
        }

        private static void Visit(Scene scene)
        {
            int rootCount = scene.rootCount;
            if (Roots.Capacity <= rootCount)
            {
                Roots.Capacity = rootCount + 1;
            }
            scene.GetRootGameObjects(Roots);
            foreach (GameObject root in Roots)
            {
                if (root == null || LiveComponentRegistry.IsAnchor(root))
                {
                    continue;
                }
                root.GetComponentsInChildren(true, Scratch);
                foreach (T item in Scratch)
                {
                    if (item is Component component && component != null)
                    {
                        Observe(component);
                    }
                }
            }
        }

        private static void Observe(Component component)
        {
            Seen.Add(component);
            if (Known.Add(component))
            {
                Entries.Add(component);
            }
        }

        private static void Reset()
        {
            Entries.Clear();
            Known.Clear();
            Seen.Clear();
            Scratch.Clear();
            Roots.Clear();
            _cold = true;
        }
    }
}
