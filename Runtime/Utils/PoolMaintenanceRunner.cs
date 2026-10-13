// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    [AddComponentMenu("")]
    internal sealed class PoolMaintenanceRunner : MonoBehaviour
    {
        private static PoolMaintenanceRunner _instance;

        private float _nextMaintenanceTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Initialize()
        {
            if (_instance != null)
            {
                return;
            }
            GameObject owner = new GameObject(nameof(PoolMaintenanceRunner));
            owner.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(owner);
            _instance = owner.AddComponent<PoolMaintenanceRunner>();
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextMaintenanceTime)
            {
                return;
            }
            _nextMaintenanceTime = now + GlobalPoolRegistry.DefaultBudgetEnforcementIntervalSeconds;
            GlobalPoolRegistry.RunMaintenance();
        }
    }
}
