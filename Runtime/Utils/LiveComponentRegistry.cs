// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class LiveComponentRegistry
    {
        internal static bool CanAccess =>
            UnityMainThreadGuard.IsInitialized && UnityMainThreadGuard.IsMainThread;

        private static readonly List<Action> Resets = new();
        private static GameObject _persistentSceneAnchor;

        internal static void TrackType(Action reset)
        {
            Resets.Add(reset);
        }

        internal static bool IsActive(Component component)
        {
            if (component == null || !component.gameObject.scene.IsValid())
            {
                return false;
            }
#if UNITY_EDITOR
            if (Application.isPlaying && !Application.IsPlaying(component))
            {
                return false;
            }
#endif
            return component.gameObject.activeInHierarchy
                && (!(component is Behaviour behaviour) || behaviour.enabled);
        }

        internal static Scene GetPersistentScene()
        {
            if (!Application.isPlaying)
            {
                return default;
            }
            if (_persistentSceneAnchor == null)
            {
                _persistentSceneAnchor = new GameObject(nameof(LiveComponentRegistry));
                _persistentSceneAnchor.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(_persistentSceneAnchor);
            }
            return _persistentSceneAnchor.scene;
        }

        internal static bool IsAnchor(GameObject root)
        {
            return ReferenceEquals(root, _persistentSceneAnchor);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void OnSubsystemRegistration()
        {
            foreach (Action reset in Resets)
            {
                reset();
            }
        }
    }
}
