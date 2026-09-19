# defensive-programming - Part 1

## Split Content

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
| **Serializer input/decode failures** | `Serializer.ProtoDeserialize<T>(corrupt)` throws `SerializationFailureException` | Save/network data is load-bearing — silent `default(T)` corrupts state. See [Serialization Safety](../skills/serialization-safety.md). |

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

Return success as `bool` and put the produced value in an `out` parameter, including private
sampling helpers. Do not return a value with `out bool success`. If a compatibility wrapper
consumes a degraded value after failure, document that private output explicitly and keep the
public `Try` contract responsible for clearing a failed result.

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
