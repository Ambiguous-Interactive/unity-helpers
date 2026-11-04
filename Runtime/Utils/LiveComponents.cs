// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Finds explicitly registered active components without searching loaded scenes.</summary>
    /// <typeparam name="T">The component type registered by its owners.</typeparam>
    /// <remarks>
    /// Call Register in OnEnable and Unregister in OnDisable and OnDestroy, or derive from LiveComponent.
    /// Queries return registration order and exclude destroyed objects, inactive GameObjects and disabled Behaviours.
    /// Each closed generic type has independent registrations. Registration does not discover other objects,
    /// including objects whose lifecycle callbacks have not run. Keep scene searches where that distinction matters.
    /// Subsystem registration clears all entries for a new play session. All operations require Unity's
    /// initialized main thread; other threads observe an empty registry and cannot change it or caller buffers.
    /// </remarks>
    public static class LiveComponents<T>
        where T : Component
    {
        /// <summary>Gets the number of active registered components.</summary>
        public static int Count
        {
            get
            {
                if (!CanAccess)
                {
                    return 0;
                }
                PruneDestroyed();
                int count = 0;
                foreach (T component in Entries)
                {
                    if (IsActive(component))
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        private static bool CanAccess =>
            UnityMainThreadGuard.IsInitialized && UnityMainThreadGuard.IsMainThread;

        private static readonly List<T> Entries = new();

        static LiveComponents()
        {
            LiveComponentRegistry.RegisterReset(static () =>
            {
                Entries.Clear();
                Entries.TrimExcess();
            });
        }

        /// <summary>Registers a live component once; returns false for duplicates or unavailable inputs.</summary>
        /// <remarks>Inactive registrations become queryable when their GameObject and Behaviour are enabled.</remarks>
        public static bool Register(T component)
        {
            if (!CanAccess || component == null)
            {
                return false;
            }
            PruneDestroyed();
            foreach (T registered in Entries)
            {
                if (ReferenceEquals(registered, component))
                {
                    return false;
                }
            }
            Entries.Add(component);
            return true;
        }

        /// <summary>Removes a registration, including a destroyed managed reference.</summary>
        public static bool Unregister(T component)
        {
            if (!CanAccess || ReferenceEquals(component, null))
            {
                return false;
            }
            for (int index = 0; index < Entries.Count; ++index)
            {
                if (ReferenceEquals(Entries[index], component))
                {
                    Entries.RemoveAt(index);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Returns the first active registration, or null when none is available.</summary>
        public static bool TryGetFirst(out T found)
        {
            if (!CanAccess)
            {
                found = null;
                return false;
            }
            PruneDestroyed();
            foreach (T component in Entries)
            {
                if (IsActive(component))
                {
                    found = component;
                    return true;
                }
            }
            found = null;
            return false;
        }

        /// <summary>Returns the first active registration in a loaded scene.</summary>
        /// <remarks>The object's current scene is checked, so moving a registered object needs no registration update.</remarks>
        public static bool TryGetIn(Scene scene, out T found)
        {
            if (!CanAccess || !scene.IsValid() || !scene.isLoaded)
            {
                found = null;
                return false;
            }
            PruneDestroyed();
            foreach (T component in Entries)
            {
                if (IsActive(component) && component.gameObject.scene == scene)
                {
                    found = component;
                    return true;
                }
            }
            found = null;
            return false;
        }

        /// <summary>Replaces a caller list with the active registrations in registration order.</summary>
        /// <remarks>A null destination is ignored. A sufficiently sized destination needs no new managed allocations.</remarks>
        public static void CopyTo(List<T> destination)
        {
            if (!CanAccess || destination == null)
            {
                return;
            }
            PruneDestroyed();
            destination.Clear();
            foreach (T component in Entries)
            {
                if (IsActive(component))
                {
                    destination.Add(component);
                }
            }
        }

        private static bool IsActive(T component)
        {
            return component is Behaviour behaviour
                ? behaviour.isActiveAndEnabled
                : component.gameObject.activeInHierarchy;
        }

        private static void PruneDestroyed()
        {
            int write = 0;
            int count = Entries.Count;
            for (int read = 0; read < count; ++read)
            {
                T component = Entries[read];
                if (component != null)
                {
                    Entries[write++] = component;
                }
            }
            if (write < count)
            {
                Entries.RemoveRange(write, count - write);
            }
        }
    }
}
