# editor-caching-patterns - Part 1

## Split Content

**Trigger**: When implementing caching in Unity Editor tools, inspectors, or property drawers to minimize allocations.

---

## When to Use This Skill

Use this skill when:

- Creating or modifying PropertyDrawers, Custom Inspectors, or EditorWindows
- Optimizing editor code that runs frequently (every frame)
- Implementing bounded caches with eviction policies
- Sharing cache resources across multiple editor components

---

## Why Caching Matters

Inspectors and drawers run **every frame**. Without caching:

- `new GUIContent()` allocates every frame
- String operations create garbage
- Dictionary lookups may create allocations
- Texture creation is expensive

---

## Basic Caching Patterns

### Static Caches

```csharp
// CORRECT - Static caches
private static readonly Dictionary<string, float> HeightCache = new(StringComparer.Ordinal);
private static readonly GUIContent ReusableContent = new();

// CORRECT - Pool expensive objects
private static readonly WallstopGenericPool<List<string>> ListPool = new(
    () => new List<string>(),
    onRelease: list => list.Clear()
);

// INCORRECT - Allocates every frame
public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    GUIContent content = new GUIContent("Label"); // Allocation!
    List<string> items = new List<string>();       // Allocation!
}
```

### Reusable GUIContent

```csharp
private static readonly GUIContent ReusableContent = new();

public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
{
    // Reuse instead of allocating new
    ReusableContent.text = "My Label";
    ReusableContent.tooltip = "My Tooltip";
    ReusableContent.image = null;

    EditorGUI.LabelField(position, ReusableContent);
}
```

**Anti-pattern — local GUIContent in OnGUI**:

```csharp
// WRONG - Allocates per frame
GUIContent buttonContent = new(displayValue);
EditorGUI.DropdownButton(fieldRect, buttonContent, FocusType.Keyboard);

// WRONG - Allocates per frame in header/foldout
GUIContent headerLabel = new GUIContent(label.text + " (detail)", label.tooltip);
EditorGUI.Foldout(foldoutRect, property.isExpanded, headerLabel, true);
```

This applies to all GUIContent used in per-frame rendering. `GenericMenu.AddItem` allocations are acceptable since they only run on user click.

---

## Shared Caches (DRY Principle)

When multiple drawers/inspectors need the same cache, use `EditorCacheHelper`:

```csharp
// CORRECT - Use shared cache helper
using WallstopStudios.UnityHelpers.Editor.Core.Helper;

// In any drawer:
string display = EditorCacheHelper.GetCachedIntString(index);
string pageLabel = EditorCacheHelper.GetPaginationLabel(page, total);
Texture2D solidTex = EditorCacheHelper.GetSolidTexture(color);

// INCORRECT - Duplicating caches across multiple files
// Drawer1.cs
private static readonly Dictionary<int, string> IntToStringCache = new();

// Drawer2.cs (duplicate!)
private static readonly Dictionary<int, string> IntToStringCache = new();
```

### Available EditorCacheHelper Methods

| Method                                              | Purpose                                   |
| --------------------------------------------------- | ----------------------------------------- |
| `GetCachedIntString(int)`                           | Cached integer-to-string conversion       |
| `GetPaginationLabel(int page, int total)`           | Cached "Page X / Y" strings               |
| `GetSolidTexture(Color)`                            | Cached 1x1 solid color textures           |
| `AddToBoundedCache<K,V>(cache, key, value, max)`    | Add to bounded LRU cache with eviction    |
| `TryGetFromBoundedLRUCache<K,V>(cache, key, out v)` | Get from LRU cache (updates access order) |

---

## Bounded LRU Caching Pattern

For custom bounded caches, use `EditorCacheHelper` LRU methods to prevent unbounded memory growth:

```csharp
private static readonly Dictionary<string, MyValue> MyCache = new();
private const int MaxCacheSize = 500;

public static MyValue GetOrCreate(string key)
{
    // LRU read - updates access order so frequently-used items stay cached
    if (EditorCacheHelper.TryGetFromBoundedLRUCache(MyCache, key, out MyValue cached))
    {
        return cached;
    }

    MyValue value = CreateValue(key);

    // LRU add - evicts least-recently-used when at capacity
    EditorCacheHelper.AddToBoundedCache(MyCache, key, value, MaxCacheSize);

    return value;
}
```

### LRU vs FIFO

**LRU (Least Recently Used)** is preferred over FIFO because it keeps frequently-accessed items in cache longer. Both reads and writes update an item's "recency", preventing hot items from being evicted.

### When to Use Bounded Caches

| Use Case              | Recommendation                       |
| --------------------- | ------------------------------------ |
| Integer-to-string     | Unbounded (limited key space)        |
| Path-to-asset lookups | Bounded - paths can grow unboundedly |
| Type-to-metadata      | Unbounded (limited types in project) |
| User input caching    | Bounded - unpredictable input space  |
| Color-to-texture      | Bounded (many possible colors)       |

---
