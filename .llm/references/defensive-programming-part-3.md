# defensive-programming - Part 3

## Split Content

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
- [ ] No `File.Exists`/`Directory.Exists` probe is the only guard on the action that follows it
- [ ] Failure cleanup deletes only files this call created
- [ ] State restoration uses a `readonly struct` `IDisposable` scope, not `try`/`finally`
- [ ] No `Dispose()` can throw
- [ ] Negative-capable hashes and indices go through `WallMath.PositiveMod`
- [ ] Counters mutated around consumer-implemented calls restore in a `finally`; size casts are range-guarded even when unreachable; width comparisons are directional (`1 < n`)

For Editor-specific defensive patterns, see [defensive-editor-programming](../skills/defensive-editor-programming.md).

---

## Related Skills

- [defensive-editor-programming](../skills/defensive-editor-programming.md) - Editor-specific defensive patterns
- [high-performance-csharp](../skills/high-performance-csharp.md) - Performance patterns (applies alongside defensive patterns)
- [create-editor-tool](../skills/create-editor-tool.md) - Editor-specific patterns
- [create-test](../skills/create-test.md) - Test edge cases and error conditions
