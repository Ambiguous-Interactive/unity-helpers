// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class UnityMainThreadDispatcherTestBootstrap
    {
        static UnityMainThreadDispatcherTestBootstrap()
        {
            DisableAutoCreation();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DisableAutoCreation()
        {
            UnityMainThreadDispatcher.SetAutoCreationEnabled(false);
            UnityMainThreadDispatcherTestHelper.DestroyDispatcherIfExists(
                immediate: !Application.isPlaying
            );
        }
    }
}
