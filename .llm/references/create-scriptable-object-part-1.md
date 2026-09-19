# create-scriptable-object - Part 1

## Split Content

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
// Group related fields together. [WGroupEnd] binds to the member BELOW it, so it goes on the
// last field you want in the group -- not on its own line after that field.
[WGroup("Movement Settings", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
public float speed;

[WGroupEnd("Movement Settings")]
public float acceleration;

// Read-only display, outside the group
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
