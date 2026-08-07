# Skill: Prefer Logging Extensions

<!-- trigger: log, logging, debug, Debug.Log, LogWarning, LogError | Unity logging in UnityEngine.Object classes | Core -->

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

## Exception Handling

### Use Exception Overloads

Pass exceptions directly to the logging methods. **Never manually format exception properties**:

```csharp
// ✅ CORRECT - Pass exception directly
try
{
    LoadAsset(path);
}
catch (Exception e)
{
    this.LogError($"Failed to load asset at {path}", e);
}

// ✅ CORRECT - Exception with context
try
{
    ProcessData(data);
}
catch (InvalidOperationException e)
{
    this.LogWarn($"Processing failed for {data.Id}, retrying", e);
    Retry(data);
}
catch (Exception e)
{
    this.LogError($"Unrecoverable error processing {data.Id}", e);
}
```

### Anti-Patterns: Manual Exception Formatting

```csharp
// ❌ BAD - Manual exception type
this.LogError($"Error: {e.GetType().Name}");

// ❌ BAD - Manual exception message
this.LogError($"Failed: {e.Message}");

// ❌ BAD - Manual stack trace
this.LogError($"Error occurred:\n{e.StackTrace}");

// ❌ BAD - Manual full exception formatting
this.LogError($"Exception: {e.GetType()}: {e.Message}\n{e.StackTrace}");

// ❌ BAD - ToString on exception
this.LogError($"Error: {e}");
this.LogError($"Error: {e.ToString()}");

// ✅ CORRECT - Just pass the exception parameter
this.LogError($"Failed to complete operation", e);
```

### Exception Variable Naming

Prefer `e` over `ex` for exception variable names:

```csharp
// ✅ PREFERRED - Short, conventional
catch (Exception e)
{
    this.LogError($"Operation failed", e);
}

// ❌ AVOID - Longer, less conventional in this codebase
catch (Exception ex)
{
    this.LogError($"Operation failed", ex);
}
```

---

## Complete Examples

### Good Examples

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

public sealed class EnemySpawner : MonoBehaviour
{
    [SerializeField] private GameObject _enemyPrefab;
    [SerializeField] private Transform[] _spawnPoints;

    private void Start()
    {
        // ✅ Simple informational log
        this.Log($"Spawner initialized with {_spawnPoints.Length} spawn points");
    }

    public void SpawnEnemy(int spawnPointIndex)
    {
        // ✅ Warning for edge case
        if (spawnPointIndex < 0 || spawnPointIndex >= _spawnPoints.Length)
        {
            this.LogWarn($"Invalid spawn point index: {spawnPointIndex}");
            return;
        }

        try
        {
            Transform spawnPoint = _spawnPoints[spawnPointIndex];
            GameObject enemy = Instantiate(_enemyPrefab, spawnPoint.position, spawnPoint.rotation);
            this.Log($"Spawned enemy at {spawnPoint.position}");
        }
        catch (Exception e)
        {
            // ✅ Error with exception passed directly
            this.LogError($"Failed to spawn enemy at index {spawnPointIndex}", e);
        }
    }
}
```

### Bad Examples (Anti-Patterns)

```csharp
public sealed class BadLoggingExample : MonoBehaviour
{
    private void Start()
    {
        // ❌ Using Debug.Log instead of extension
        Debug.Log("BadLoggingExample started");

        // ❌ Manual class name prefix
        this.Log($"[BadLoggingExample] Initialization complete");

        // ❌ Plain string without interpolation
        this.Log("Started");  // Won't work correctly
    }

    public void ProcessData(Data data)
    {
        try
        {
            // Process...
        }
        catch (Exception ex)  // ❌ Using "ex" instead of "e"
        {
            // ❌ Manual exception formatting
            this.LogError($"Error: {ex.GetType().Name}: {ex.Message}");

            // ❌ Including stack trace manually
            this.LogError($"Stack trace: {ex.StackTrace}");

            // ❌ ToString on exception in message
            this.LogError($"Full error: {ex}");
        }
    }

    // ❌ Can't use this.Log in static methods
    public static void StaticMethod()
    {
        // this.Log($"Static log");  // Won't compile
        Debug.Log("Use Debug.Log for static methods");  // ✅ OK here
    }
}
```

---

## Migration Checklist

When converting existing code to use logging extensions:

- [ ] Add `using WallstopStudios.UnityHelpers.Core.Extension;`
- [ ] Replace `Debug.Log(...)` with `this.Log($"...")`
- [ ] Replace `Debug.LogWarning(...)` with `this.LogWarn($"...")`
- [ ] Replace `Debug.LogError(...)` with `this.LogError($"...")`
- [ ] Ensure all strings use `$"..."` interpolation syntax
- [ ] Remove manual class name prefixes from messages (method names are not auto-injected)
- [ ] Replace manual exception formatting with exception parameter
- [ ] Rename `ex` variables to `e`
- [ ] Skip static methods (continue using `Debug.Log`)
- [ ] **Verify compilation** - If PropertyDrawer extension methods fail to resolve, fall back to `Debug.Log*`

---

## Thread Safety

The logging extensions are **thread-safe**. When called from a background thread:

1. Logs are automatically dispatched to the Unity main thread
2. If main thread dispatch fails, logs are written with an offline fallback
3. No manual thread synchronization required

---

## Build Configuration

Every entry point is a [`[Conditional]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.conditionalattribute) method carrying the whole enabling set — `ENABLE_UBERLOGGING`, `DEVELOPMENT_BUILD`, `DEBUG`, `UNITY_EDITOR`, plus its own severity symbol. Unity defines the middle three itself, so the editor and development builds need no configuration.

In release builds the compiler removes **the entire call site**, receiver and arguments included, so a disabled call costs nothing at all — no `FormattableString`, and no evaluation of an expression like `MySingleton.Instance` that would otherwise have side effects.

That decision is made in the assembly that **calls** the method, not in this package, so a symbol has to be defined project-wide (Player Settings → Scripting Define Symbols) to take effect. There is no longer a file-scoped `#define` inside the package; a define that reached only this assembly would change nothing for your code.

**Granular logging defines**: individual levels can be enabled with `DEBUG_LOGGING`, `WARN_LOGGING`, and `ERROR_LOGGING` while the others stay compiled out.

---

## Related Skills

- [defensive-programming](./defensive-programming.md) - Logging guidelines for defensive code
- [use-extension-methods](./use-extension-methods.md) - Other extension methods available
- [high-performance-csharp](./high-performance-csharp.md) - Performance considerations for logging
