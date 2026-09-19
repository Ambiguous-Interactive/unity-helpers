# use-discriminated-union - Part 3

## Split Content

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
