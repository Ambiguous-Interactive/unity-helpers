// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System.Threading;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class PoolStateTestUtilities
    {
        internal static void ClearBuiltInTypeConfigurations()
        {
            lock (PoolPurgeSettings.ConfigLock)
            {
                PoolPurgeSettings.BuiltInTypeConfigurations.Clear();
                PoolPurgeSettings.BuiltInGenericTypeConfigurations.Clear();
            }

            Volatile.Write(ref PoolPurgeSettings._builtInDefaultsInitialized, 0);
        }

        internal static void ReinitializeBuiltInDefaults()
        {
            ClearBuiltInTypeConfigurations();
            PoolPurgeSettings.EnsureBuiltInDefaultsInitialized();
        }

        internal static void UnregisterLifecycleHooks()
        {
            if (Interlocked.Exchange(ref PoolPurgeSettings._lifecycleHooksRegistered, 0) == 0)
            {
                return;
            }

            Application.lowMemory -= PoolPurgeSettings.OnLowMemory;
            Application.focusChanged -= PoolPurgeSettings.OnFocusChanged;
            SceneManager.sceneUnloaded -= PoolPurgeSettings.OnSceneUnloaded;
        }

        internal static void ClearRegistry()
        {
            lock (GlobalPoolRegistry.RegistryLock)
            {
                GlobalPoolRegistry.RegisteredPools.Clear();
            }
            Volatile.Write(ref GlobalPoolRegistry._lastBudgetEnforcementTime, 0f);
        }
    }
}
