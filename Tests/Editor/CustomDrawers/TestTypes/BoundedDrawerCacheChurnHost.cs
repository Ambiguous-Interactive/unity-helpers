// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
#if UNITY_EDITOR
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Supplies as many distinct serialized property paths as a bounded drawer cache needs to be
    /// driven past its bound, from a single inspected object.
    /// </summary>
    internal sealed class BoundedDrawerCacheChurnHost : ScriptableObject
    {
        public WGuid[] guids;
        public int[] slots;
    }
#endif
}
