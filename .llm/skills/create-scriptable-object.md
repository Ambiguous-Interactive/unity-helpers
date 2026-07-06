# Skill: Create ScriptableObject

<!-- trigger: scriptableobject, so, asset, data, config | Creating ScriptableObject data assets | Core -->

**Trigger**: When creating a new `ScriptableObject` class for data assets, configuration, or shared runtime state in this repository.

---

## Pre-Creation Checklist

1. **Determine file location**:
   - Runtime data assets → `Runtime/` folder tree (e.g., `Runtime/Tags/`, `Runtime/Settings/`)
   - Editor-only tools → `Editor/` folder tree
   - Tests → `Tests/Runtime/` or `Tests/Editor/` (mirror source structure)

2. **Determine ScriptableObject type**:
   - **Standard ScriptableObject**: One-off data containers, effect definitions, configuration presets
   - **ScriptableObjectSingleton<T>**: Global settings, metadata caches, shared configuration

3. **One file per ScriptableObject**:
   - Each class deriving from `ScriptableObject` MUST have its own dedicated `.cs` file
   - ❌ Multiple ScriptableObjects in the same file
   - ❌ Nested classes deriving from ScriptableObject
   - ✅ Create separate `MyEffectData.cs`, `GameSettings.cs` files
   - Enforced by pre-commit hook and CI/CD analyzer

---

## Basic ScriptableObject Template

```csharp
namespace WallstopStudios.UnityHelpers.{Subsystem}
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Brief description of what this asset represents.
    /// </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/{Category}/{Asset Name}")]
    public sealed class MyDataAsset : ScriptableObject
    {
        /// <summary>
        /// Description of the field's purpose.
        /// </summary>
        [SerializeField]
        private float _value;

        /// <summary>
        /// Public property with validation.
        /// </summary>
        public float Value => _value;
    }
}
```

---

## ScriptableObjectSingleton Template (Global Configuration)

Use `ScriptableObjectSingleton<T>` for settings or caches that should have exactly one instance loaded at runtime.

```csharp
namespace WallstopStudios.UnityHelpers.{Subsystem}
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Global configuration for {feature}.
    /// Automatically loaded from Resources at runtime.
    /// </summary>
    [ScriptableSingletonPath("Wallstop Studios/Unity Helpers")]
    [AllowDuplicateCleanup]
    [AutoLoadSingleton(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public sealed class MyGlobalSettings : ScriptableObjectSingleton<MyGlobalSettings>
    {
        [Header("Settings")]
        [Tooltip("Description of what this setting controls.")]
        [SerializeField]
        private bool _enableFeature = true;

        [SerializeField]
        [Min(0f)]
        private float _timeout = 5f;

        /// <summary>
        /// Gets whether the feature is enabled.
        /// </summary>
        public bool EnableFeature => _enableFeature;

        /// <summary>
        /// Gets the timeout in seconds.
        /// </summary>
        public float Timeout => _timeout;
    }
}
```

### Singleton Attributes

| Attribute                           | Purpose                                                          |
| ----------------------------------- | ---------------------------------------------------------------- |
| `[ScriptableSingletonPath("path")]` | Specifies the Resources subfolder for the singleton asset        |
| `[AllowDuplicateCleanup]`           | Enables automatic cleanup of duplicate singleton assets          |
| `[AutoLoadSingleton(LoadType)]`     | Triggers automatic loading at the specified initialization point |

---

## Inspector Attributes

Use the package's custom attributes to enhance the Unity Inspector experience:

### Field Visibility

```csharp
// Show field only when condition is met
[WShowIf(nameof(durationType), expectedValues: new object[] { ModifierDurationType.Duration })]
public float duration;

// Show field when boolean is true
[WShowIf(nameof(_advancedMode))]
public float advancedValue;

// Show field when value meets comparison
[WShowIf(nameof(_level), WShowIfComparison.GreaterThanOrEqual, 3)]
public string eliteTitle;

// Inverse condition (show when false/null)
[WShowIf(nameof(_overridePrefab), inverse: true)]
public GameObject defaultPrefab;
```

### Field Organization

```csharp
// Group related fields together
[WGroup("Movement Settings")]
public float speed;
public float acceleration;
[WGroupEnd]

// Read-only display
[WReadOnly]
public string computedId;

// Inline editor for nested ScriptableObjects
[WInLineEditor]
public EffectData nestedEffect;
```

### Validation

```csharp
// Mark field as required (must not be null)
[WNotNull]
public GameObject requiredPrefab;

// Dropdown from predefined values
[WValueDropDown(nameof(GetAvailableOptions))]
public string selectedOption;

private IEnumerable<string> GetAvailableOptions() => new[] { "Option1", "Option2" };

// Enum toggle buttons
[WEnumToggleButtons]
public MyEnum enumValue;
```

### Buttons

```csharp
// Add inspector button to invoke method
[WButton("Refresh Cache")]
private void RefreshCache()
{
    // Implementation
}

// Button with placement control
[WButton("Validate", WButtonGroupPlacement.Below)]
private void Validate()
{
    // Implementation
}
```

---

## OnValidate for Editor-Time Validation

Use `OnValidate()` to enforce constraints and update computed values when the asset is modified in the Editor:

```csharp
#if UNITY_EDITOR
private void OnValidate()
{
    // Clamp values
    _timeout = Mathf.Max(0f, _timeout);

    // Ensure list is initialized
    if (_items == null)
    {
        _items = new List<Item>();
    }

    // Update computed fields
    _cachedDescription = BuildDescription();

    // Mark dirty if changes were made programmatically
    UnityEditor.EditorUtility.SetDirty(this);
}
#endif
```

---

## Serialization Considerations

### JSON/Protobuf Compatibility

For ScriptableObjects that may be serialized to JSON or Protobuf:

```csharp
using System.Text.Json.Serialization;

public sealed class MySerializableData : ScriptableObject
{
    // Include in JSON serialization
    public string id;
    public float value;

    // Exclude Unity-specific references from JSON
    [JsonIgnore]
    public GameObject prefab;

    [JsonIgnore]
    public List<CosmeticEffectData> cosmetics = new();
}
```

### Unity Serialization

```csharp
// Use [SerializeField] for private fields that need serialization
[SerializeField]
private float _internalValue;

// Use [NonSerialized] for runtime-only cached data
[NonSerialized]
private readonly Lazy<ComputedData> _cached;

// Use [FormerlySerializedAs] when renaming fields to preserve data
[FormerlySerializedAs("oldFieldName")]
[SerializeField]
private float _newFieldName;
```

---

## CreateAssetMenu Organization

Follow the menu hierarchy pattern:

```csharp
// Top-level category for the package
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/{Feature}/{Asset Type}")]

// Examples:
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Attribute Effect")]
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Effects/Burning Behaviour")]
[CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Settings/Audio Settings")]
```

Optional parameters:

```csharp
[CreateAssetMenu(
    menuName = "Wallstop Studios/Unity Helpers/My Asset",
    fileName = "NewMyAsset",     // Default filename when creating
    order = 100                   // Menu position
)]
```

---

## Odin Inspector Compatibility

Runtime ScriptableObjects in this package use Unity bases unless the package-owned
Odin define enables guarded Sirenix bases. Use the package's own
attributes in runtime assets, and put Odin-specific drawers, editors, and tests in
the dedicated Odin integration folders.

```csharp
public sealed class MyAsset : ScriptableObject
{
    [WShowIf(nameof(showAdvanced))]
    [WGroup("Advanced")]
    public float advancedValue;
}
```

If a test or editor-only integration must compile against Odin/Sirenix types, follow
[integrate-odin-inspector](./integrate-odin-inspector.md) and
[test-odin-drawers](./test-odin-drawers.md). Gate that source with
`WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` and add only the directly used Sirenix DLLs
to the owning asmdef.

---

## Post-Creation Steps (MANDATORY)

1. **Generate meta file** (required — do not skip):

   ```bash
   ./scripts/generate-meta.sh <path-to-file.cs>
   ```

   > ⚠️ See [create-unity-meta](./create-unity-meta.md) for full details. This step is **mandatory** — every `.cs` file MUST have a corresponding `.meta` file.

2. **Format code**:

   ```bash
   dotnet tool run csharpier format .
   ```

3. **Verify no errors**:
   - Check IDE for compilation errors
   - Ensure `.asmdef` references are correct if adding new namespaces

4. **Update documentation** (MANDATORY for user-facing ScriptableObjects):
   - Add CHANGELOG entry in `### Added` section
   - Document the asset type in `docs/features/`
   - Add XML documentation (`///`) on all public members
   - Include usage examples in documentation
   - See [update-documentation](./update-documentation.md) for standards

---

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
