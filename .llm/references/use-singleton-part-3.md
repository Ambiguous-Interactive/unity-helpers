# use-singleton - Part 3

## Split Content

## Common Mistakes

### ❌ Forgetting to Call Base Methods

```csharp
// ❌ Breaks singleton behavior
protected override void Awake()
{
    // Missing base.Awake()!
    Initialize();
}

// ✅ Always call base
protected override void Awake()
{
    base.Awake();  // Required!
    Initialize();
}
```

### ❌ Accessing Instance During OnDestroy

```csharp
// ❌ May create new instance during shutdown
private void OnDestroy()
{
    GameManager.Instance.Unregister(this);  // Dangerous!
}

// ✅ Check HasInstance first
private void OnDestroy()
{
    if (GameManager.HasInstance)
    {
        GameManager.Instance.Unregister(this);
    }
}
```

### ❌ Missing Asset for ScriptableObjectSingleton

```csharp
// ❌ No asset in Resources - returns null and logs warning
var settings = GameSettings.Instance;  // null if asset missing!

// ✅ Create asset first:
// 1. Tools > WallstopStudios > ScriptableObject Singleton Creator
// 2. Or manually create in Resources folder
```

### ❌ Scene-Local Singleton with [AutoLoadSingleton]

```csharp
// ❌ Conflicting: auto-loaded but won't persist
[AutoLoadSingleton]
public sealed class LevelManager : RuntimeSingleton<LevelManager>
{
    protected override bool Preserve => false;  // Destroyed on scene change!
}

// ✅ Either persist across scenes...
[AutoLoadSingleton]
public sealed class LevelManager : RuntimeSingleton<LevelManager>
{
    // Preserve defaults to true
}

// ✅ ...or don't auto-load scene-local singletons
public sealed class LevelManager : RuntimeSingleton<LevelManager>
{
    protected override bool Preserve => false;
}
```

---

## When to Use Each Singleton Type

| Use Case                 | Type                                           |
| ------------------------ | ---------------------------------------------- |
| Game managers, services  | `RuntimeSingleton<T>`                          |
| Audio/Input managers     | `RuntimeSingleton<T>`                          |
| Scene-specific managers  | `RuntimeSingleton<T>` with `Preserve => false` |
| Game settings/config     | `ScriptableObjectSingleton<T>`                 |
| Balance data             | `ScriptableObjectSingleton<T>`                 |
| Editor-configurable data | `ScriptableObjectSingleton<T>`                 |

---

## Related Skills

- [editor-singleton-patterns](../skills/editor-singleton-patterns.md) - Singleton asset management patterns for Editor code
