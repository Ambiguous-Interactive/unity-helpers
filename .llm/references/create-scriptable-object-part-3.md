# create-scriptable-object - Part 3

## Split Content

## Complete Examples

### Effect Behavior (Condensed)

```csharp
namespace WallstopStudios.UnityHelpers.Tags
{
    using UnityEngine;

    /// <summary>
    /// Custom effect behaviour that spawns a particle effect while active.
    /// </summary>
    [CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Effects/Particle Behaviour")]
    public sealed class ParticleBehavior : EffectBehavior
    {
        [Header("Visual Settings")]
        [SerializeField]
        [Tooltip("Particle prefab to spawn when effect is applied.")]
        private GameObject _particlePrefab;

        [SerializeField]
        [Min(0f)]
        private float _scale = 1f;

        [NonSerialized]
        private GameObject _spawnedInstance;

        public override void OnApply(EffectBehaviorContext context)
        {
            if (_particlePrefab == null) return;
            Transform parent = context.Target.transform;
            _spawnedInstance = Object.Instantiate(_particlePrefab, parent.position, parent.rotation, parent);
            _spawnedInstance.transform.localScale = Vector3.one * _scale;
        }

        public override void OnRemove(EffectBehaviorContext context)
        {
            if (_spawnedInstance != null)
            {
                Object.Destroy(_spawnedInstance);
                _spawnedInstance = null;
            }
        }
    }
}
```

### Singleton Settings (Condensed)

```csharp
namespace WallstopStudios.UnityHelpers.Settings
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Global audio settings singleton. Loaded from Resources/Wallstop Studios/Unity Helpers/.
    /// </summary>
    [ScriptableSingletonPath("Wallstop Studios/Unity Helpers")]
    [AllowDuplicateCleanup]
    [AutoLoadSingleton(RuntimeInitializeLoadType.AfterSceneLoad)]
    public sealed class AudioSettings : ScriptableObjectSingleton<AudioSettings>
    {
        [Header("Volume")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 1f;
        [SerializeField] [Range(0f, 1f)] private float _musicVolume = 0.8f;
        [SerializeField] [Range(0f, 1f)] private float _sfxVolume = 1f;

        [Header("Advanced")]
        [SerializeField] private bool _enableSpatialAudio = true;

        [WShowIf(nameof(_enableSpatialAudio))]
        [SerializeField] [Min(1f)] private float _maxDistance = 50f;

        public float MasterVolume => _masterVolume;
        public float MusicVolume => _musicVolume;
        public float SfxVolume => _sfxVolume;
        public bool EnableSpatialAudio => _enableSpatialAudio;
        public float MaxDistance => _enableSpatialAudio ? _maxDistance : 0f;

        public void ApplySettings() => AudioListener.volume = _masterVolume;

#if UNITY_EDITOR
        private void OnValidate() => _maxDistance = Mathf.Max(1f, _maxDistance);
#endif
    }
}
```

---

## Quick Reference: Common Patterns

| Pattern                          | When to Use                                       |
| -------------------------------- | ------------------------------------------------- |
| `ScriptableObject`               | Data assets, effect definitions, presets          |
| `ScriptableObjectSingleton<T>`   | Global settings, caches, runtime configuration    |
| `EffectBehavior` (abstract base) | Custom effect lifecycle hooks                     |
| `[CreateAssetMenu]`              | User-creatable assets from Project window         |
| `[JsonIgnore]`                   | Exclude Unity references from JSON serialization  |
| `OnValidate()`                   | Editor-time validation and constraint enforcement |
