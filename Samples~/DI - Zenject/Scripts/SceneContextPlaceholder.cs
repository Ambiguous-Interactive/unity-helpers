// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.DI.Zenject
{
    using global::Zenject;
    using UnityEngine;
#if ZENJECT_PRESENT
    using Zenject;
#endif

    /// <summary>
    /// Placeholder that turns into a real Zenject SceneContext once Zenject/Extenject is installed.
    /// If Zenject is not present, it logs a helpful warning in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneContextPlaceholder : MonoBehaviour
    {
#if ZENJECT_PRESENT
        private void Reset()
        {
            EnsureSceneContext();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                EnsureSceneContext();
            }
        }

        private void EnsureSceneContext()
        {
            if (!TryGetComponent(out SceneContext _))
            {
                gameObject.AddComponent<SceneContext>();
            }
        }
#else
        private void OnValidate()
        {
            Debug.LogWarning(
                "SceneContextPlaceholder: Zenject/Extenject not installed. Import package to enable DI initialization.",
                this
            );
        }
#endif
    }
}
