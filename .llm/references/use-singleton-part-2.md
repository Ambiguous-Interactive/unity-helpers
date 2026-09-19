# use-singleton - Part 2

## Split Content

## Automatic Instantiation with [AutoLoadSingleton]

Use `[AutoLoadSingleton]` to automatically instantiate singletons during Unity startup:

```csharp
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Utils;
using UnityEngine;

// Loads before splash screen (default)
[AutoLoadSingleton]
public sealed class BootstrapManager : RuntimeSingleton<BootstrapManager>
{
    protected override void Awake()
    {
        base.Awake();
        InitializeCore();
    }
}

// Loads at a specific phase
[AutoLoadSingleton(RuntimeInitializeLoadType.AfterSceneLoad)]
public sealed class PostSceneManager : RuntimeSingleton<PostSceneManager>
{
}
```

### Load Types

| Load Type               | When                        |
| ----------------------- | --------------------------- |
| `SubsystemRegistration` | Earliest, before subsystems |
| `AfterAssembliesLoaded` | After assemblies loaded     |
| `BeforeSplashScreen`    | Before splash (default)     |
| `BeforeSceneLoad`       | Before first scene          |
| `AfterSceneLoad`        | After first scene loaded    |

---

## Safe Instance Checking with HasInstance

Check if an instance exists without triggering creation:

```csharp
// ✅ Safe check - doesn't create instance
if (GameManager.HasInstance)
{
    GameManager.Instance.SaveProgress();
}

// ❌ Avoid - creates instance if it doesn't exist
if (GameManager.Instance != null)  // Instance property creates if missing!
{
    GameManager.Instance.SaveProgress();
}
```

### OnDestroy Pattern

```csharp
private void OnDestroy()
{
    // Safe cleanup during shutdown
    if (GameManager.HasInstance)
    {
        GameManager.Instance.UnregisterEntity(this);
    }
}
```

---

## Main Thread Requirements

Both singleton types require main thread access. Unity API calls must happen on the main thread.

```csharp
// ✅ Called from main thread (MonoBehaviour callbacks, coroutines)
void Update()
{
    GameManager.Instance.UpdateScore();  // OK
}

// ❌ Called from background thread
async Task ProcessAsync()
{
    await Task.Run(() =>
    {
        // This will throw!
        GameManager.Instance.UpdateScore();  // ERROR: Not on main thread
    });
}
```

The singletons use `UnityMainThreadGuard.EnsureMainThread()` internally to enforce this.

---

## Common Patterns

### Service Registry

```csharp
[AutoLoadSingleton]
public sealed class Services : RuntimeSingleton<Services>
{
    public IAudioService Audio { get; private set; }
    public IInputService Input { get; private set; }
    public ISaveService Save { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Audio = new AudioService();
        Input = new InputService();
        Save = new SaveService();
    }
}

// Usage
Services.Instance.Audio.PlaySound("click");
```

### Configuration with Defaults

```csharp
[ScriptableSingletonPath("Config")]
public sealed class DifficultySettings : ScriptableObjectSingleton<DifficultySettings>
{
    [SerializeField]
    private float _enemyHealthMultiplier = 1f;

    [SerializeField]
    private float _playerDamageMultiplier = 1f;

    public float EnemyHealthMultiplier => _enemyHealthMultiplier;
    public float PlayerDamageMultiplier => _playerDamageMultiplier;
}
```

### Event Bus Singleton

```csharp
public sealed class EventBus : RuntimeSingleton<EventBus>
{
    public event Action<int> OnScoreChanged;
    public event Action OnGameOver;

    public void RaiseScoreChanged(int newScore) => OnScoreChanged?.Invoke(newScore);
    public void RaiseGameOver() => OnGameOver?.Invoke();
}

// Usage
EventBus.Instance.OnScoreChanged += HandleScoreChanged;
EventBus.Instance.RaiseScoreChanged(100);
```

---
