// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
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
    }
}
