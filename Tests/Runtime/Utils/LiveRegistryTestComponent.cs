// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [ExecuteAlways]
    [RequireComponent(typeof(LiveComponentRegistration))]
    public sealed class LiveRegistryTestComponent : MonoBehaviour
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
