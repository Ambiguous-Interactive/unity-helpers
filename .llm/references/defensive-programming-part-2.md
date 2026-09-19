# defensive-programming - Part 2

## Split Content

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

### 7. Filesystem, Scope, and Index Safety

Four rules, with worked examples in
[forbidden-patterns](./forbidden-patterns.md#filesystem-scope-and-index-patterns):

- A `File.Exists` probe is a hint, never the only guard — each branch must fall through to the other
  on the exception the race actually produces.
- Failure cleanup deletes only files this call created. Make ownership decidable with one exclusive
  open (`FileMode.Create` + `FileShare.None`).
- Restore state with a `readonly struct` `IDisposable` scope and `using`, not `try`/`finally`.
- `Dispose()` never throws. It runs from a `finally`, so a throw replaces the caller's real
  exception with one about teardown.
- Map negative-capable hashes and indices with `WallMath.PositiveMod`, never `Math.Abs` or
  `& int.MaxValue`.

### 8. Calling Foreign Code, and the Casts Around It

Three rules for any method that hands control to an interface a consumer implements — a formatter, a
visitor, a callback. Worked examples in
[forbidden-patterns](./forbidden-patterns.md#foreign-call-and-cast-patterns):

- Restore shared counters in a `finally`, even when the callee's contract forbids throwing.
- Guard the cast, not just the invariant: an unreachable negative behind `(uint)` is a memory-safety
  failure, not a wrong number.
- Compare directionally (`1 < n`), not by equality (`n != 1`), when only one direction is actionable.

---
