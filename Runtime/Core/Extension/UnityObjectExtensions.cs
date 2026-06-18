// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using UnityEngine;
    using Object = UnityEngine.Object;

    internal static class UnityObjectExtensions
    {
        internal static int GetUnityObjectId(this Object unityObject)
        {
            if (unityObject == null)
            {
                return 0;
            }

#if UNITY_6000_0_OR_NEWER
            return unityObject.GetEntityId();
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
#if UNITY_2022_2_OR_NEWER
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
#if UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType(
                type,
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
#else
            return Object.FindObjectsOfType(type, includeInactive);
#endif
        }
    }
}
