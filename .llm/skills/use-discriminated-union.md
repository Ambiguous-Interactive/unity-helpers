# Skill: Use Discriminated Unions (OneOf Types)

<!-- trigger: oneof, union, result, either, variant | OneOf/Result types, type-safe unions | Feature -->

**Trigger**: When a value can be one of several distinct types and you need type-safe handling without inheritance hierarchies.

---

## What Are Discriminated Unions?

A discriminated union (also called a "tagged union" or "sum type") is a type that can hold exactly one value from a fixed set of possible types. Unlike inheritance, the types don't need to share a common base class.

**Key benefits:**

- **Type safety** - Compiler ensures all cases are handled
- **No allocations** - `FastOneOf` is a `readonly struct`
- **No boxing** - Unlike `object` or interfaces
- **Explicit handling** - Forces consideration of all possible types

---

## Available Types

| Type                        | Description                    |
| --------------------------- | ------------------------------ |
| `FastOneOf<T0, T1>`         | Two-type discriminated union   |
| `FastOneOf<T0, T1, T2>`     | Three-type discriminated union |
| `FastOneOf<T0, T1, T2, T3>` | Four-type discriminated union  |
| `None`                      | Represents absence of a value  |

---

## Namespace

```csharp
using WallstopStudios.UnityHelpers.Core.OneOf;
```

---

## Basic Usage: Two-Type Union

```csharp
using WallstopStudios.UnityHelpers.Core.OneOf;

// A result that is either a value or an error message
public FastOneOf<int, string> ParseNumber(string input)
{
    if (int.TryParse(input, out int value))
    {
        return value;  // Implicitly converts to FastOneOf
    }
    return $"Invalid number: {input}";  // Returns error string
}

// Usage
FastOneOf<int, string> result = ParseNumber("42");

// Pattern matching with Match()
string message = result.Match(
    value => $"Parsed: {value}",
    error => $"Error: {error}"
);
```

---

## Pattern Matching with Match()

The `Match()` method forces handling of all possible types:

```csharp
FastOneOf<Player, Enemy, NPC> entity = GetEntity();

// Must provide a handler for each type
string description = entity.Match(
    player => $"Player: {player.Name}",
    enemy => $"Enemy: {enemy.Type}",
    npc => $"NPC: {npc.DialogueId}"
);
```

### Side Effects with Switch()

Use `Switch()` when you don't need a return value:

```csharp
FastOneOf<DamageEvent, HealEvent> healthEvent = GetHealthEvent();

healthEvent.Switch(
    damage => ApplyDamage(damage.Amount),
    heal => ApplyHeal(heal.Amount)
);
```

---

## Conditional Extraction with TryGet

Use `TryGetT0()`, `TryGetT1()`, etc. for conditional extraction:

```csharp
FastOneOf<SuccessResult, ErrorResult> result = DoOperation();

// Check for specific type
if (result.TryGetT0(out SuccessResult success))
{
    Debug.Log($"Operation succeeded: {success.Data}");
}
else if (result.TryGetT1(out ErrorResult error))
{
    Debug.LogError($"Operation failed: {error.Message}");
}
```

### Type Checking Properties

```csharp
FastOneOf<int, string> value = GetValue();

// Check which type is active
if (value.IsT0)
{
    int number = value.AsT0;  // Safe - we checked IsT0
}
else if (value.IsT1)
{
    string text = value.AsT1;  // Safe - we checked IsT1
}
```

⚠️ **Warning**: Using `AsT0`, `AsT1`, etc. without checking throws `InvalidOperationException`:

```csharp
FastOneOf<int, string> value = "hello";
int number = value.AsT0;  // ❌ Throws InvalidOperationException!
```

---

## The None Type

`None` represents the absence of a value, useful for optional results:

```csharp
using WallstopStudios.UnityHelpers.Core.OneOf;

// A method that may or may not find a result
public FastOneOf<Item, None> FindItem(string id)
{
    if (_items.TryGetValue(id, out Item item))
    {
        return item;
    }
    return None.Default;  // No item found
}

// Usage
FastOneOf<Item, None> result = FindItem("sword_01");

result.Switch(
    item => EquipItem(item),
    none => Debug.Log("Item not found")
);
```

### None vs Nullable

| Approach             | Pros                          | Cons                       |
| -------------------- | ----------------------------- | -------------------------- |
| `T?` (nullable)      | Simple, built-in              | Only works for value types |
| `FastOneOf<T, None>` | Works with any type, explicit | Slightly more verbose      |

---

## Result/Error Handling Pattern

### Basic Result Type

```csharp
// Define a custom error type
public readonly struct ParseError
{
    public string Message { get; }
    public int Position { get; }

    public ParseError(string message, int position)
    {
        Message = message;
        Position = position;
    }
}

// Return either success or error
public FastOneOf<Config, ParseError> LoadConfig(string path)
{
    try
    {
        string json = File.ReadAllText(path);
        Config config = JsonUtility.FromJson<Config>(json);
        return config;  // Success
    }
    catch (Exception ex)
    {
        return new ParseError(ex.Message, 0);  // Error
    }
}

// Usage
FastOneOf<Config, ParseError> result = LoadConfig("config.json");

result.Switch(
    config => ApplyConfig(config),
    error => Debug.LogError($"Config error at {error.Position}: {error.Message}")
);
```

### Multiple Error Types

```csharp
public readonly struct NotFoundError
{
    public string Path { get; }
    public NotFoundError(string path) => Path = path;
}

public readonly struct ValidationError
{
    public string Field { get; }
    public string Message { get; }
    public ValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }
}

// Return success or one of multiple error types
public FastOneOf<UserData, NotFoundError, ValidationError> LoadUser(string id)
{
    if (!_users.TryGetValue(id, out UserData user))
    {
        return new NotFoundError(id);
    }

    if (string.IsNullOrEmpty(user.Name))
    {
        return new ValidationError("Name", "Name cannot be empty");
    }

    return user;
}

// Handle all cases
string message = LoadUser("123").Match(
    user => $"Loaded: {user.Name}",
    notFound => $"User not found: {notFound.Path}",
    validation => $"Invalid {validation.Field}: {validation.Message}"
);
```

---

## Transforming Values with Map()

Transform the contained value while preserving the union structure:

```csharp
FastOneOf<int, string> original = 42;

// Map transforms each type independently
FastOneOf<double, int> transformed = original.Map(
    intValue => intValue * 2.0,      // Transform int to double
    strValue => strValue.Length      // Transform string to int
);
```

---

## Four-Type Union Example

```csharp
// Represent different input events
public FastOneOf<KeyPress, MouseClick, TouchEvent, GamepadInput> inputEvent;

// Handle all input types
inputEvent.Switch(
    key => HandleKeyPress(key),
    mouse => HandleMouseClick(mouse),
    touch => HandleTouchEvent(touch),
    gamepad => HandleGamepadInput(gamepad)
);
```

---

## Comparison and Equality

`FastOneOf` implements `IEquatable` with zero-allocation equality:

```csharp
FastOneOf<int, string> a = 42;
FastOneOf<int, string> b = 42;
FastOneOf<int, string> c = "hello";

bool equal = a == b;       // true
bool notEqual = a == c;    // false

// Works in collections
var set = new HashSet<FastOneOf<int, string>> { a, b, c };
// Contains only 2 items (a and b are equal)
```

---

## Complete Example

A full `MonoBehaviour` state machine built on `FastOneOf`, and the four-type variant, are in
[discriminated-union-examples](./discriminated-union-examples.md).

---

## Common Mistakes

### ❌ Using AsT\* Without Checking

```csharp
FastOneOf<int, string> value = "hello";

// ❌ Throws InvalidOperationException
int number = value.AsT0;

// ✅ Check first
if (value.IsT0)
{
    int number = value.AsT0;
}

// ✅ Or use TryGet
if (value.TryGetT0(out int number))
{
    // Use number
}

// ✅ Or use Match (best approach)
value.Match(
    number => ProcessNumber(number),
    text => ProcessText(text)
);
```

### ❌ Ignoring Cases in Match

```csharp
// Match forces you to handle all cases - this won't compile
FastOneOf<int, string, bool> value = 42;

// ❌ Missing bool handler - compiler error!
// value.Match(
//     n => n.ToString(),
//     s => s
// );

// ✅ Handle all cases
value.Match(
    n => n.ToString(),
    s => s,
    b => b.ToString()
);
```

---

## When to Use Discriminated Unions

✅ **Use for:**

- Result/error handling patterns
- State machines with distinct state data
- API responses with different shapes
- Replacing loosely-typed `object` parameters
- Replacing complex inheritance hierarchies

❌ **Don't use for:**

- Simple optional values (use nullable `T?` for value types)
- Types that share common behavior (use interfaces)
- More than 4 distinct types (consider refactoring)
- Performance-critical inner loops (prefer direct type checks)
