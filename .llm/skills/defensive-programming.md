# Skill: Defensive Programming

<!-- trigger: defensive, null, validate, error, exception | ALL code - never throw, handle gracefully | Core -->

**Trigger**: When writing ANY production code (Runtime OR Editor). ALL code in this repository MUST follow defensive programming practices.

---

## Core Philosophy

**Assume nothing. Handle everything. Never throw.**

Production code—including editor tooling—must be **resilient to any state**. Users, Unity, serialization, and external systems can all produce unexpected inputs. Our APIs must:

1. **Never throw exceptions** from public APIs (except for fundamentally invalid usage)
2. **Maintain internal consistency** even when given bad data
3. **Fail gracefully** with sensible defaults or no-ops
4. **Log problems** for debugging without disrupting execution

---

## Exception Philosophy

### When Exceptions Are Acceptable

Exceptions should ONLY be thrown for:

| Scenario                             | Example                                                                          | Why It's OK                                                                                                                    |
| ------------------------------------ | -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **Programmer error (debug only)**    | `Debug.Assert(index >= 0)`                                                       | Catches bugs during development                                                                                                |
| **Fundamentally impossible states**  | Constructor receives negative capacity                                           | API contract violation                                                                                                         |
| **Security violations**              | Unauthorized access to protected resources                                       | Must fail loudly                                                                                                               |
| **Serializer input/decode failures** | `Serializer.ProtoDeserialize<T>(corrupt)` throws `SerializationFailureException` | Save/network data is load-bearing — silent `default(T)` corrupts state. See [Serialization Safety](./serialization-safety.md). |

### When Exceptions Are FORBIDDEN

| Scenario                    | Bad                                       | Good                                                                                           |
| --------------------------- | ----------------------------------------- | ---------------------------------------------------------------------------------------------- |
| Null input to public method | `throw new ArgumentNullException()`       | Return `default`, empty, or `false`                                                            |
| Index out of range          | `throw new IndexOutOfRangeException()`    | Clamp, return `false`, or no-op                                                                |
| Type mismatch               | `throw new InvalidCastException()`        | Use `TryXxx` pattern or return `default`                                                       |
| Missing resource            | `throw new FileNotFoundException()`       | Return `null`, log warning                                                                     |
| Deserialization failure     | `throw new JsonException()`               | `Serializer.TryXxx` or wrap `SerializationFailureException` (see `serialization-safety` skill) |
| Invalid enum value          | `throw new ArgumentOutOfRangeException()` | Use `default` case, log warning                                                                |

---

## Defensive Patterns

### 1. Guard Clauses with Graceful Returns

```csharp
// THROWS - Bad for production
public void ProcessItems(List<Item> items)
{
    if (items == null)
    {
        throw new ArgumentNullException(nameof(items));
    }
    // Process...
}

// GRACEFUL - Returns safely
public void ProcessItems(List<Item> items)
{
    if (items == null || items.Count == 0)
    {
        return; // No-op for invalid input
    }
    // Process...
}

// GRACEFUL with logging (when debugging matters)
public void ProcessItems(List<Item> items)
{
    if (items == null)
    {
        Debug.LogWarning($"[{nameof(MyClass)}] ProcessItems called with null list");
        return;
    }
    // Process...
}
```

### 2. TryXxx Pattern for Failable Operations

```csharp
// Return success/failure, never throw
public bool TryGetValue(string key, out TValue value)
{
    value = default;

    if (string.IsNullOrEmpty(key))
    {
        return false;
    }

    if (!_dictionary.TryGetValue(key, out value))
    {
        return false;
    }

    return true;
}

// For complex operations
public bool TryParse(string json, out MyData result, out string error)
{
    result = default;
    error = null;

    if (string.IsNullOrEmpty(json))
    {
        error = "JSON string is null or empty";
        return false;
    }

    try
    {
        result = JsonUtility.FromJson<MyData>(json);
        return result != null;
    }
    catch (Exception ex)
    {
        error = ex.Message;
        return false;
    }
}
```

### 3. Safe Indexing

```csharp
// THROWS on invalid index
public T Get(int index)
{
    return _items[index]; // IndexOutOfRangeException!
}

// GRACEFUL - Returns default for invalid index
public T Get(int index)
{
    if (index < 0 || index >= _items.Count)
    {
        return default;
    }
    return _items[index];
}

// TryGet pattern for callers who need to know
public bool TryGet(int index, out T value)
{
    if (index < 0 || index >= _items.Count)
    {
        value = default;
        return false;
    }
    value = _items[index];
    return true;
}
```

### 4. Null-Safe Unity Object Handling

```csharp
// Safe component access
public void UpdateTarget()
{
    if (_targetTransform == null)
    {
        return; // Target destroyed or not assigned
    }

    _targetTransform.position = _newPosition;
}

// Safe GetComponent with caching
public T GetCachedComponent<T>() where T : Component
{
    if (_cachedComponent == null)
    {
        _cachedComponent = GetComponent<T>();
    }
    return _cachedComponent; // May still be null - caller handles
}

// Safe child access
public Transform GetChildSafe(int index)
{
    if (transform == null)
    {
        return null;
    }

    if (index < 0 || index >= transform.childCount)
    {
        return null;
    }

    return transform.GetChild(index);
}
```

### 5. Enum Safety

```csharp
// THROWS for undefined values
public string GetDisplayName(MyEnum value)
{
    return value switch
    {
        MyEnum.Option1 => "First",
        MyEnum.Option2 => "Second",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

// GRACEFUL - Handles undefined values
public string GetDisplayName(MyEnum value)
{
    return value switch
    {
        MyEnum.Option1 => "First",
        MyEnum.Option2 => "Second",
        _ => value.ToString() // Fallback to enum name
    };
}

// GRACEFUL with logging for debugging
public string GetDisplayName(MyEnum value)
{
    switch (value)
    {
        case MyEnum.Option1:
            return "First";
        case MyEnum.Option2:
            return "Second";
        default:
            Debug.LogWarning($"[{nameof(MyClass)}] Unhandled enum value: {value}");
            return value.ToString();
    }
}
```

### 6. Collection Operations

```csharp
// Safe iteration (collection may be modified)
public void ProcessAll()
{
    int count = _items.Count;
    for (int i = 0; i < count && i < _items.Count; i++)
    {
        Item item = _items[i];
        if (item == null)
        {
            continue; // Skip null entries
        }
        Process(item);
    }
}

// Safe dictionary access
public TValue GetOrDefault(TKey key, TValue defaultValue = default)
{
    if (key == null)
    {
        return defaultValue;
    }

    if (_dictionary.TryGetValue(key, out TValue value))
    {
        return value;
    }

    return defaultValue;
}

// Safe removal
public bool TryRemove(TKey key)
{
    if (key == null)
    {
        return false;
    }

    return _dictionary.Remove(key);
}
```

---

## Internal State Consistency

### Invariant Maintenance

Always ensure internal state remains valid, regardless of input:

```csharp
public sealed class BoundedQueue<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _tail;
    private int _count;
    private readonly int _capacity;

    public void Enqueue(T item)
    {
        // Maintain invariant: count never exceeds capacity
        if (_count >= _capacity)
        {
            // Option 1: Overwrite oldest (circular buffer behavior)
            _head = (_head + 1) % _capacity;
        }
        else
        {
            _count++;
        }

        _buffer[_tail] = item;
        _tail = (_tail + 1) % _capacity;
    }

    public bool TryDequeue(out T result)
    {
        if (_count == 0)
        {
            result = default;
            return false;
        }

        result = _buffer[_head];
        _buffer[_head] = default; // Clear reference
        _head = (_head + 1) % _capacity;
        _count--;

        // Invariant: indices always valid
        Debug.Assert(_head >= 0 && _head < _capacity);
        Debug.Assert(_tail >= 0 && _tail < _capacity);
        Debug.Assert(_count >= 0 && _count <= _capacity);

        return true;
    }
}
```

### State Repair After Deserialization

```csharp
public void OnAfterDeserialize()
{
    // Repair any inconsistent state from serialization
    RepairInternalState();
}

private void RepairInternalState()
{
    // Ensure collections are non-null
    _items ??= new List<Item>();
    _lookup ??= new Dictionary<string, Item>();

    // Rebuild lookup from items (source of truth)
    _lookup.Clear();
    for (int i = 0; i < _items.Count; i++)
    {
        Item item = _items[i];
        if (item == null || string.IsNullOrEmpty(item.Id))
        {
            continue;
        }
        _lookup[item.Id] = item;
    }

    // Clamp numeric values
    _currentIndex = Mathf.Clamp(_currentIndex, 0, Mathf.Max(0, _items.Count - 1));
}
```

---

## Logging Guidelines

### When to Log

| Level              | Use For                            | Example                           |
| ------------------ | ---------------------------------- | --------------------------------- |
| `Debug.Log`        | Development-only diagnostics       | "Cache rebuilt with 42 entries"   |
| `Debug.LogWarning` | Unexpected but handled state       | "Null item skipped in collection" |
| `Debug.LogError`   | Serious issues that need attention | "Failed to load required asset"   |
| `Debug.Assert`     | Invariant violations (dev only)    | "Index must be non-negative"      |

### Logging Best Practices

```csharp
// Include context for debugging
Debug.LogWarning($"[{nameof(MyComponent)}] Skipping null target in {nameof(ProcessTargets)}");

// Include relevant data
Debug.LogError($"[Serializer] Failed to deserialize type {typeof(T).Name} from {json?.Length ?? 0} chars");

// Don't log in hot paths
public void Update()
{
    // Never log every frame unless explicitly debugging
}

// Use conditional logging for hot paths
[System.Diagnostics.Conditional("DEBUG_VERBOSE")]
private void LogVerbose(string message)
{
    Debug.Log(message);
}
```

---

## Quick Checklist

Before submitting production code, verify:

- [ ] No exceptions thrown from public APIs (except true programmer errors)
- [ ] All null inputs handled gracefully
- [ ] All index access bounds-checked
- [ ] All dictionary access uses TryGetValue
- [ ] All enum switches have default case
- [ ] All Unity Objects null-checked before use
- [ ] Internal state maintains invariants after any operation
- [ ] Warnings logged for unexpected-but-handled states
- [ ] No excessive logging in frequently-called code

For Editor-specific defensive patterns, see [defensive-editor-programming](./defensive-editor-programming.md).

---

## Related Skills

- [defensive-editor-programming](./defensive-editor-programming.md) - Editor-specific defensive patterns
- [high-performance-csharp](./high-performance-csharp.md) - Performance patterns (applies alongside defensive patterns)
- [create-editor-tool](./create-editor-tool.md) - Editor-specific patterns
- [create-test](./create-test.md) - Test edge cases and error conditions
