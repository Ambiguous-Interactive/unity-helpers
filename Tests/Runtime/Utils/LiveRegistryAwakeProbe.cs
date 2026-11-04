// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    public sealed class LiveRegistryAwakeProbe : MonoBehaviour
    {
        internal bool foundSelf;
        internal int observedCount;

        private void Awake()
        {
            List<LiveRegistryAwakeProbe> matches = new();
            LiveComponents<LiveRegistryAwakeProbe>.CopyTo(matches);
            foundSelf = matches.Contains(this);
            observedCount = matches.Count;
        }
    }
}
