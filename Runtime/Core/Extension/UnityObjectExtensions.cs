// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_6000_4_OR_NEWER
#define UNH_HAS_ENTITY_ID_TO_ULONG
#define UNH_HAS_FIND_OBJECTS_BY_TYPE_INACTIVE_ONLY
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

#if UNH_HAS_ENTITY_ID_TO_ULONG
            return unchecked((long)EntityId.ToULong(unityObject.GetEntityId()));
#else
            return unityObject.GetInstanceID();
#endif
        }

        internal static T[] FindObjectsOfTypeShim<T>(bool includeInactive)
            where T : Object
        {
#if UNH_HAS_FIND_OBJECTS_BY_TYPE_INACTIVE_ONLY
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
#if UNH_HAS_FIND_OBJECTS_BY_TYPE_INACTIVE_ONLY
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

        // Singleton discovery needs any matching object, so an InstanceID sort is unnecessary.
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
