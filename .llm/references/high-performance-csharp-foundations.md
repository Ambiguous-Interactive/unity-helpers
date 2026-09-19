# High-Performance C# Foundations

## Core Philosophy

**Every code path should be allocation-free in steady state** -- runtime gameplay, editor tooling
and inspectors (called every frame while visible), bug fixes, and test utilities that may run
thousands of iterations.

Unity's Boehm collector scans the whole heap on every collection, stutters the frame, and never
compacts. At 60 FPS, 1 KB/frame is **3.6 MB/minute** of garbage. See
[gc-architecture-unity](../skills/gc-architecture-unity.md).

**Never duplicate code - build abstractions.** Extract repetitive patterns into lightweight reusable
ones; prefer `readonly struct` or `static` methods for zero allocation; apply SOLID; compose complex
behavior from simple pieces.

---

## Abstraction Guidelines

### When to Abstract

- **Two or more occurrences** - If you write similar code twice, extract it
- **Complex logic** - Encapsulate non-obvious algorithms behind clear interfaces
- **Cross-cutting concerns** - Logging, caching, validation patterns

### How to Abstract (Zero Allocation)

```csharp
// ✅ Value-type abstraction - no heap allocation
public readonly struct ValidationResult
{
    public readonly bool IsValid;
    public readonly string ErrorMessage;

    public ValidationResult(bool isValid, string errorMessage = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }
}

// ✅ Static utility methods - no allocation
public static class CollectionExtensions
{
    public static bool TryGetFirst<T>(this IList<T> list, out T result)
    {
        if (0 < list.Count)
        {
            result = list[0];
            return true;
        }
        result = default;
        return false;
    }
}

// ✅ Generic constraint-based abstraction. The counting loop is correct here and
// nowhere else nearby: IList<T> is an interface, so foreach would box its enumerator.
public static void ProcessAll<T>(IList<T> items) where T : IProcessable
{
    for (int i = 0; i < items.Count; i++)
    {
        items[i].Process();
    }
}
```

### Abstraction Anti-Patterns

```csharp
// ❌ Class when struct suffices - unnecessary allocation
public class ValidationResult { }

// ❌ Closure-capturing delegate factory
public Func<T> CreateGetter<T>(T value) => () => value;  // Allocates!

// ❌ Over-abstraction - adds complexity without value
public interface IStringProvider { string GetString(); }
public class ConstantStringProvider : IStringProvider { ... }  // Just use the string!
```

---

## Aggressive Inlining for Hot Paths

Mark frequently-called small methods -- `GetHashCode`, `Equals`, the comparison operators:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool Equals(FastVector2Int other)
{
    return _hash == other._hash && x == other.x && y == other.y;
}
```

---

## String Building Best Practices

String operations are a common source of allocations. Choose the right approach based on context:

| Context                   | Recommended Approach        | Example                       |
| ------------------------- | --------------------------- | ----------------------------- |
| Hot paths (Update, loops) | `StringBuilder` via pooling | `Buffers.StringBuilder.Get()` |
| Two strings               | Direct `+` is fine          | `firstName + lastName`        |
| 3+ parts, non-hot path    | String interpolation        | `$"{name}: {value}"`          |
| Building in loops         | **Always** `StringBuilder`  | See below                     |
| Format with many args     | `StringBuilder`             | Avoids `params` allocation    |

```csharp
// ❌ BAD: Concatenation in loop - O(n^2) allocations!
string result = "";
foreach (Item item in items)
{
    result += item.Name;  // New string each iteration!
}

// ✅ GOOD: StringBuilder with pooling - zero allocation
using PooledResource<StringBuilder> lease = Buffers.StringBuilder.Get(out StringBuilder sb);
foreach (Item item in items)
{
    sb.Append(item.Name);
}
string result = sb.ToString();
```

---

## Editor Tooling Requirements

Editor code runs every frame when inspectors are visible. Apply ALL performance patterns:

```csharp
// ✅ Cache everything, pool temporaries. The wrong shape is
// `GetOptions().ToList()` plus `.ToArray()` on every OnGUI call.
private static readonly GUIContent TitleContent = new GUIContent("Option");
private string[] _cachedOptions;
private int _cachedOptionsHash;

public override void OnInspectorGUI()
{
    int currentHash = ComputeOptionsHash();
    if (_cachedOptions == null || _cachedOptionsHash != currentHash)
    {
        using PooledResource<List<string>> lease = Buffers<string>.List.Get(
            out List<string> options
        );
        GetOptions(options);
        _cachedOptions = options.ToArray();  // Only allocate when data changes
        _cachedOptionsHash = currentHash;
    }

    int selected = EditorGUILayout.Popup(TitleContent, current, _cachedOptions);
}
```

**Cache GUIContent, GUIStyle, and computed values:**

```csharp
private static readonly GUIContent Label = new GUIContent("Label", "Tooltip");
private static readonly GUIStyle BoxStyle = new GUIStyle("box");
private static readonly Color HighlightColor = new Color(0.3f, 0.6f, 1f);
```

---
