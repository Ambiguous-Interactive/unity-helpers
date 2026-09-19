# prefer-logging-extensions - Part 1

## Split Content

**Trigger**: When writing `Debug.Log`, `Debug.LogWarning`, or `Debug.LogError` statements inside non-static methods of classes deriving from `UnityEngine.Object` (MonoBehaviour, ScriptableObject, etc.).

---

## Core Principle

**Prefer `this.Log()`, `this.LogWarn()`, and `this.LogError()` over direct `Debug.Log*` calls.** These extension methods automatically inject metadata (timestamp, class name, GameObject name, context) into log messages, eliminating manual formatting boilerplate.

---

## When to Use

Use the logging extensions when **ALL** of these conditions are met:

| Condition                            | Explanation                                        |
| ------------------------------------ | -------------------------------------------------- |
| Inside a non-static method           | Extension methods require `this` context           |
| Class inherits from `Object`         | MonoBehaviour, ScriptableObject, EditorWindow, etc |
| Need structured logging with context | Automatic metadata injection                       |

### Applicable Base Classes

- `MonoBehaviour`
- `ScriptableObject`
- `EditorWindow`
- `Editor`
- `PropertyDrawer`
- Any other `UnityEngine.Object` derivative

---

## When NOT to Use

| Scenario                       | Use Instead                                    |
| ------------------------------ | ---------------------------------------------- |
| Static methods                 | `Debug.Log()` (no `this` available)            |
| Non-Object classes (POCO)      | `Debug.Log()` or custom logging                |
| Pure C# classes                | `Debug.Log()` or inject logger                 |
| Performance-critical hot paths | Consider disabling logging entirely            |
| One-off quick debug prints     | `Debug.Log()` is acceptable during development |

---

## Known Edge Cases and Limitations

### PropertyDrawer Extension Method Resolution

While `PropertyDrawer` inherits from `GUIDrawer` which inherits from `UnityEngine.Object`, the extension methods may fail to resolve in certain scenarios:

1. **Internal methods in complex PropertyDrawers** - In large PropertyDrawer classes with many internal/private methods, the C# compiler may fail to resolve the extension method, producing errors like:

   ```text
   error CS1929: 'MyPropertyDrawer' does not contain a definition for 'LogWarn'
   and the best extension method overload requires a receiver of type 'Object'
   ```

2. **When to fall back to Debug.Log** - If you encounter this error in a PropertyDrawer:
   - First, verify the `using WallstopStudios.UnityHelpers.Core.Extension;` directive is present
   - If the error persists, use `Debug.LogWarning()` or `Debug.LogError()` instead
   - This is acceptable because PropertyDrawer instances don't have GameObject context anyway

```csharp
// If this.LogWarn fails in a PropertyDrawer, fall back to:
Debug.LogWarning("Unable to generate a unique value for this set element type.");
```

### Other Editor Classes with Potential Issues

- **AssetPostprocessor** - Not an Object derivative; always use `Debug.Log`
- **Static utility classes** - No `this` context; always use `Debug.Log`
- **Nested classes within PropertyDrawers** - May have resolution issues; test and fall back if needed

---

## API Quick Reference

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

// Basic logging - REQUIRES FormattableString ($"...")
this.Log($"Player spawned at {position}");
this.LogWarn($"Health below threshold: {health}");
this.LogError($"Failed to load asset: {assetPath}");

// With exception (pass exception directly, never format manually)
this.Log($"Operation completed with issues", e);
this.LogWarn($"Retrying after failure", e);
this.LogError($"Critical failure", e);

// Control formatting
this.Log($"Raw output", e, pretty: false);

// Per-object logging control
this.EnableLogging();
this.DisableLogging();

// Global logging control
this.GlobalEnableLogging();
this.GlobalDisableLogging();
WallstopStudiosLogger.SetGlobalLoggingEnabled(false);

// Check if logging is enabled
bool enabled = WallstopStudiosLogger.IsGlobalLoggingEnabled();
```

---

## Required: String Interpolation

The logging methods **REQUIRE** a `FormattableString` parameter. This means you **MUST** use string interpolation (`$"..."`):

```csharp
// ✅ CORRECT - FormattableString with $"..."
this.Log($"Player {playerId} joined game");
this.LogWarn($"Cache miss for key: {key}");
this.LogError($"Unexpected state: {currentState}");

// ❌ WRONG - Plain string literal (won't compile or will use wrong overload)
this.Log("Player joined game");
this.LogWarn("Cache miss");
this.LogError("Unexpected state");
```

---

## Automatic Metadata Injection

The logging extensions automatically inject contextual metadata. **Do NOT manually include** class names or other context that the logger already provides:

```csharp
// ❌ BAD - Manual metadata (redundant, clutters message)
this.Log($"[{nameof(PlayerController)}] [{nameof(OnSpawn)}] Player spawned at {position}");
this.LogError($"[PlayerController.HandleDamage] Error processing damage: {amount}");
Debug.Log($"[{GetType().Name}] Processing complete");

// ✅ GOOD - Let the logger handle metadata
this.Log($"Player spawned at {position}");
this.LogError($"Error processing damage: {amount}");
this.Log($"Processing complete");
```

The logger automatically includes:

- Class name
- Context object reference (clickable in Unity console)
- Thread-safe main thread routing
- Timestamp and other decorators

---
