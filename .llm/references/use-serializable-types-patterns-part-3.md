# use-serializable-types-patterns - Part 3

## Split Content

## Common Pitfalls

### Using Standard Dictionary

```csharp
// Won't serialize in Unity Inspector or save files
[SerializeField]
private Dictionary<string, int> scores;  // Not serializable!
```

### Use SerializableDictionary

```csharp
[SerializeField]
private SerializableDictionary<string, int> scores = new();  // Works!
```

### Forgetting to Initialize

```csharp
[SerializeField]
private SerializableDictionary<string, int> _scores;  // Might be null!

void Start()
{
    _scores["test"] = 1;  // NullReferenceException
}
```

### Always Initialize

```csharp
[SerializeField]
private SerializableDictionary<string, int> _scores = new();  // Safe

void Start()
{
    _scores["test"] = 1;  // Works
}
```

### Nullable in SerializeField

```csharp
[SerializeField]
private int? _optionalValue;  // Unity ignores this!
```

### Use SerializableNullable

```csharp
[SerializeField]
private SerializableNullable<int> _optionalValue = new();  // Works!
```

### Modifying During Iteration

```csharp
// ConcurrentModificationException risk
foreach (var key in _dictionary.Keys)
{
    if (ShouldRemove(key))
    {
        _dictionary.Remove(key);  // Dangerous!
    }
}
```

### Collect Keys First

```csharp
// Safe iteration with modification
List<string> toRemove = new();
foreach (var key in _dictionary.Keys)
{
    if (ShouldRemove(key))
    {
        toRemove.Add(key);
    }
}
foreach (string key in toRemove)
{
    _dictionary.Remove(key);
}
```

---

## Performance Tips

### Pre-size Collections When Possible

```csharp
// If you know approximate size
SerializableDictionary<string, int> dict = new(expectedCount);
SerializableHashSet<string> set = new(expectedCount);
```

### Use TryGetValue

```csharp
// Avoid double lookup
if (_dictionary.TryGetValue(key, out var value))
{
    // Use value directly
}

// Instead of
if (_dictionary.ContainsKey(key))
{
    var value = _dictionary[key];  // Second lookup
}
```

### Cache Frequently Accessed Values

```csharp
// If accessing same key repeatedly in a frame
private string _cachedKey;
private int _cachedValue;

public int GetValue(string key)
{
    if (key == _cachedKey)
    {
        return _cachedValue;
    }

    if (_dictionary.TryGetValue(key, out int value))
    {
        _cachedKey = key;
        _cachedValue = value;
        return value;
    }

    return 0;
}
```

---

## Related Skills

- [Serializable Types](../skills/use-serializable-types.md) - Type definitions and API reference
- [Serialization](../skills/use-serialization.md) - JSON and Protobuf serialization details
- [Data Structures](../skills/use-data-structures.md) - Other available data structures
- [GC Architecture](../skills/gc-architecture-unity.md) - Memory management considerations
