// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using UnityEngine;

    [ExecuteAlways]
    public class LiveRegistryTestComponent : MonoBehaviour, ILiveRegistryTestComponent
    {
        internal int enableCalls;
        internal int disableCalls;
        internal int destroyCalls;

        private void OnEnable()
        {
            ++enableCalls;
        }

        private void OnDisable()
        {
            ++disableCalls;
        }

        private void OnDestroy()
        {
            ++destroyCalls;
        }
    }
}
