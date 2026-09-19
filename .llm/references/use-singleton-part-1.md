# use-singleton - Part 1

## Split Content

**Trigger**: When implementing global manager classes, service locators, or shared configuration objects in Unity.

---

## RuntimeSingleton&lt;T&gt; for MonoBehaviour Singletons

Use `RuntimeSingleton<T>` for MonoBehaviour-based singletons that need to exist in the scene.

### Basic Implementation

```csharp
using WallstopStudios.UnityHelpers.Utils;

public sealed class GameManager : RuntimeSingleton<GameManager>
{
    public int Score { get; set; }
    public bool IsPaused { get; set; }

    public void StartGame()
    {
        Score = 0;
        IsPaused = false;
    }
}

// Usage from anywhere
GameManager.Instance.StartGame();
Debug.Log($"Score: {GameManager.Instance.Score}");
```

### The `Preserve` Property

By default, `RuntimeSingleton<T>` persists across scene loads via `DontDestroyOnLoad`. Override `Preserve` to change this behavior:

```csharp
// ✅ Persists across scenes (default)
public sealed class AudioManager : RuntimeSingleton<AudioManager>
{
    // Preserve defaults to true - survives scene changes
}

// ✅ Scene-local singleton - destroyed on scene change
public sealed class LevelManager : RuntimeSingleton<LevelManager>
{
    protected override bool Preserve => false;  // Stay scene-local
}
```

### Lifecycle Methods

Override lifecycle methods as needed (always call base):

```csharp
public sealed class GameServices : RuntimeSingleton<GameServices>
{
    protected override void Awake()
    {
        base.Awake();  // Always call base first!
        InitializeServices();
    }

    protected override void Start()
    {
        base.Start();  // Always call base first!
        StartServices();
    }

    protected override void OnDestroy()
    {
        CleanupServices();
        base.OnDestroy();  // Call base last
    }
}
```

---

## ScriptableObjectSingleton&lt;T&gt; for Configuration

Use `ScriptableObjectSingleton<T>` for global configuration, settings, or data that should be edited in the Unity Editor.

### Basic Implementation

```csharp
using WallstopStudios.UnityHelpers.Utils;
using UnityEngine;

public sealed class GameSettings : ScriptableObjectSingleton<GameSettings>
{
    [SerializeField]
    private float _masterVolume = 1f;

    [SerializeField]
    private int _targetFrameRate = 60;

    public float MasterVolume => _masterVolume;
    public int TargetFrameRate => _targetFrameRate;
}

// Usage from anywhere
float volume = GameSettings.Instance.MasterVolume;
```

### Asset Creation

Create the ScriptableObject asset:

1. **Automatic**: Use the "ScriptableObject Singleton Creator" tool (Tools > WallstopStudios)
2. **Manual**: Right-click in Project > Create > [Your Type Name]

Assets are loaded from `Resources/` folder. Default lookup order:

1. Custom path specified via `[ScriptableSingletonPath]`
2. `Resources/<TypeName>/`
3. `Resources/<TypeName>` (exact name match)
4. Global Resources search

---

## Custom Asset Paths with [ScriptableSingletonPath]

Specify where the singleton asset should be loaded from:

```csharp
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Utils;

[ScriptableSingletonPath("Settings/Audio")]
public sealed class AudioSettings : ScriptableObjectSingleton<AudioSettings>
{
    // Asset should be at: Resources/Settings/Audio/AudioSettings.asset
}

[ScriptableSingletonPath("Config")]
public sealed class GameConfig : ScriptableObjectSingleton<GameConfig>
{
    // Asset should be at: Resources/Config/GameConfig.asset
}
```

---
