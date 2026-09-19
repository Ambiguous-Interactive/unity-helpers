# use-discriminated-union - Part 1

## Split Content

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
