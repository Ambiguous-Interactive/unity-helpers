# create-enum - Part 2

## Split Content

## Examples

### Simple Enum

```csharp
public enum LogLevel
{
    [Obsolete("Use a specific LogLevel value instead of Unset.")]
    Unset = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5,
}
```

### Enum with Custom Display Names

```csharp
public enum DamageType
{
    [Obsolete("Use a specific DamageType value instead of None.")]
    None = 0,

    [EnumDisplayName("Physical Damage")]
    Physical = 1,

    [EnumDisplayName("Fire Damage")]
    Fire = 2,

    [EnumDisplayName("Ice Damage")]
    Ice = 3,

    [EnumDisplayName("Lightning Damage")]
    Lightning = 4,
}
```

---

## Common Mistakes

❌ **Missing explicit values** (FORBIDDEN):

```csharp
public enum State { None, Active, Inactive }
```

❌ **Partial explicit values** (FORBIDDEN):

```csharp
public enum State { None = 0, Active, Inactive }  // Active & Inactive are implicit!
```

❌ **Zero value without `[Obsolete]`**:

```csharp
public enum State { None = 0, Active = 1 }  // Missing [Obsolete] on None!
```

❌ **Non-zero first value**:

```csharp
public enum State { Active = 1, Inactive = 2 }  // No zero/default value!
```

❌ **Generic obsolete message**:

```csharp
[Obsolete("Don't use")]  // Not helpful - specify the enum name!
None = 0,
```

❌ **Error-causing obsolete**:

```csharp
[Obsolete("Use a specific value", true)]  // true = compilation error!
None = 0,
```

---

## Exceptions (When `[Obsolete]` May Be Omitted)

In rare cases, the `[Obsolete]` attribute on the zero value may be omitted:

| Exception                    | Example                                           | Reason                                    |
| ---------------------------- | ------------------------------------------------- | ----------------------------------------- |
| `None` is semantically valid | `[Flags]` where `None` means "no flags set"       | `None` is a legitimate, intentional value |
| Boolean-like enum            | `TriState { Unknown = 0, False = 1, True = 2 }`   | `Unknown` may be a valid state            |
| External API compatibility   | Matching external library or protocol enum        | Must match external definition            |
| Zero is the expected default | `Priority { Normal = 0, High = 1, Critical = 2 }` | `Normal` is intentionally the default     |

**When omitting `[Obsolete]`:**

1. Document WHY in a code comment
2. Ensure zero value has a meaningful name (not just `None` or `Unknown`)
3. Still use explicit integer values for ALL members

```csharp
// Priority.Normal is intentionally the default - most operations are normal priority
public enum Priority
{
    Normal = 0,    // Valid default - not obsolete
    High = 1,
    Critical = 2,
    Urgent = 3,
}
```
