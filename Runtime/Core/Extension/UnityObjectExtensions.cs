// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if !UNITY_2021 && !UNITY_2022 && !UNITY_2023
#define UNH_HAS_ENTITY_ID
#define UNH_HAS_FIND_OBJECTS_BY_TYPE_NO_SORT_MODE
#endif

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using UnityEngine;
    using Object = UnityEngine.Object;

    internal static class UnityObjectExtensions
    {
        internal static long GetUnityObjectId(this Object unityObject)
        {
            if (unityObject == null)
            {
                return 0;
            }

#if UNH_HAS_ENTITY_ID
            return unchecked((long)EntityId.ToULong(unityObject.GetEntityId()));
#else
            return unityObject.GetInstanceID();
#endif
        }

        // FindObjectsByType was added in 2022.2 (2021.3.18). Unity exposes only major.minor compile
        // symbols, so the new API is gated behind UNITY_2022_2_OR_NEWER and the pre-2022.2 fallback
        // keeps the legacy API (which only emits a deprecation warning on 6000.0+, a version that
        // always takes the gated branch). Only the includeInactive overloads have callers today.
        internal static T[] FindObjectsOfTypeShim<T>(bool includeInactive)
            where T : Object
        {
#if UNH_HAS_FIND_OBJECTS_BY_TYPE_NO_SORT_MODE
            return Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude
            );
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
#else
            return Object.FindObjectsOfType<T>(includeInactive);
#endif
        }

        internal static Object[] FindObjectsOfTypeShim(Type type, bool includeInactive)
        {
#if UNH_HAS_FIND_OBJECTS_BY_TYPE_NO_SORT_MODE
            return Object.FindObjectsByType(
                type,
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude
            );
#elif UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType(
                type,
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
#else
            return Object.FindObjectsOfType(type, includeInactive);
#endif
        }

        // FindAnyObjectByType (2022.2+) is the single-object companion to FindObjectsByType and
        // the non-obsolete replacement for FindObjectOfType. It returns an arbitrary matching
        // object without the InstanceID sort the deprecated API implied; every caller here only
        // needs "any one" (singleton discovery), so the unsorted variant is faithful and faster.
        internal static T FindObjectOfTypeShim<T>(bool includeInactive = false)
            where T : Object
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindAnyObjectByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude
            );
#else
            return Object.FindObjectOfType<T>(includeInactive);
#endif
        }
    }
}
