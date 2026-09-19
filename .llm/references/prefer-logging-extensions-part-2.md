# prefer-logging-extensions - Part 2

## Split Content

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
