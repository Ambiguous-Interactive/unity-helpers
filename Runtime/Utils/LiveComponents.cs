// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>Finds registered active components without searching loaded scenes.</summary>
    /// <typeparam name="T">The component type queried by its owners.</typeparam>
    /// <remarks>
    /// Use LiveComponentRegistration for attribute-based and third-party registration, or call Register and Unregister.
    /// Adapter registrations use exact runtime types; manual registrations use the closed generic type.
    /// Results follow registration order and exclude destroyed objects, inactive GameObjects and disabled Behaviours.
    /// Subsystem registration clears every entry. All operations require Unity's initialized main thread;
    /// workers observe an empty registry and cannot change registrations or caller buffers.
    /// </remarks>
    public static class LiveComponents<T>
        where T : Component
    {
        /// <summary>Gets the number of active registered components.</summary>
        public static int Count
        {
            get
            {
                if (
                    !LiveComponentRegistry.TryGetEntries(
                        typeof(T),
                        out List<LiveComponentRegistrationEntry> entries
                    )
                )
                {
                    return 0;
                }
                int count = 0;
                foreach (LiveComponentRegistrationEntry entry in entries)
                {
                    if (LiveComponentRegistry.IsActive(entry.component))
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        /// <summary>Adds manual ownership once; returns false for duplicate manual registrations or unavailable inputs.</summary>
        /// <remarks>Manual ownership is independent of an adapter and remains when that adapter is disabled.</remarks>
        public static bool Register(T component)
        {
            return LiveComponentRegistry.RegisterManual(typeof(T), component);
        }

        /// <summary>Removes manual ownership, including a destroyed managed reference.</summary>
        /// <remarks>An adapter may still own the same component.</remarks>
        public static bool Unregister(T component)
        {
            return LiveComponentRegistry.UnregisterManual(typeof(T), component);
        }

        /// <summary>Returns the first active registration, or null when none is available.</summary>
        public static bool TryGetFirst(out T found)
        {
            if (
                LiveComponentRegistry.TryGetEntries(
                    typeof(T),
                    out List<LiveComponentRegistrationEntry> entries
                )
            )
            {
                foreach (LiveComponentRegistrationEntry entry in entries)
                {
                    if (LiveComponentRegistry.IsActive(entry.component))
                    {
                        found = (T)entry.component;
                        return true;
                    }
                }
            }
            found = null;
            return false;
        }

        /// <summary>Returns the first active registration in a loaded scene.</summary>
        /// <remarks>The object's current scene is checked, so moving it needs no registration update.</remarks>
        public static bool TryGetIn(Scene scene, out T found)
        {
            if (!LiveComponentRegistry.CanAccess || !scene.IsValid() || !scene.isLoaded)
            {
                found = null;
                return false;
            }
            if (
                LiveComponentRegistry.TryGetEntries(
                    typeof(T),
                    out List<LiveComponentRegistrationEntry> entries
                )
            )
            {
                foreach (LiveComponentRegistrationEntry entry in entries)
                {
                    if (
                        LiveComponentRegistry.IsActive(entry.component)
                        && entry.component.gameObject.scene == scene
                    )
                    {
                        found = (T)entry.component;
                        return true;
                    }
                }
            }
            found = null;
            return false;
        }

        /// <summary>Replaces a caller list with active registrations in registration order.</summary>
        /// <remarks>A null destination is ignored; a sufficiently sized destination needs no new managed allocations.</remarks>
        public static void CopyTo(List<T> destination)
        {
            if (!LiveComponentRegistry.CanAccess || destination == null)
            {
                return;
            }
            destination.Clear();
            if (
                !LiveComponentRegistry.TryGetEntries(
                    typeof(T),
                    out List<LiveComponentRegistrationEntry> entries
                )
            )
            {
                return;
            }
            foreach (LiveComponentRegistrationEntry entry in entries)
            {
                if (LiveComponentRegistry.IsActive(entry.component))
                {
                    destination.Add((T)entry.component);
                }
            }
        }
    }
}
