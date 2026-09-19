# no-regions - Part 2

## Split Content

## Examples

### Forbidden Patterns

```csharp
// All of these are FORBIDDEN:

#region Fields
private int _count;
private string _name;
#endregion

#region Public Methods
public void DoSomething() { }
#endregion

#region Private Helpers
private void Helper() { }
#endregion

#region Unity Lifecycle
private void Awake() { }
private void Update() { }
#endregion

#region Interface Implementation
// IDisposable implementation
#endregion
```

### Acceptable Alternatives

```csharp
// Just organize code naturally without regions:

public sealed class MyClass : IDisposable
{
    private int _count;
    private string _name;

    public void DoSomething()
    {
        // Implementation
    }

    public void Dispose()
    {
        // Cleanup
    }

    private void Helper()
    {
        // Implementation
    }
}
```

---

## Edge Cases

### Third-Party Generated Code

If you must include generated code that contains regions, isolate it in clearly marked generated files. Prefer regenerating without regions if the tool supports it.

### Copying Code From External Sources

When copying code from external sources that uses regions, remove the regions during the copy process. This is non-negotiable.

### Legacy Code

There is no legacy exception. If you encounter regions in existing code, remove them when you modify that file.

---

## Git Hook Enforcement

The pre-commit hook will reject any commit containing `#region` or `#endregion`. If you see this error:

```text
Error: C# regions (#region/#endregion) are forbidden in this codebase.
The following files contain regions:
  Runtime/Core/MyClass.cs:15: #region Helper Methods
  Runtime/Core/MyClass.cs:45: #endregion

Remove all #region and #endregion directives before committing.
See .llm/skills/no-regions.md for guidance on code organization alternatives.
```

The solution is to remove the regions, not bypass the hook.

---

## Related Skills

- [create-csharp-file](../skills/create-csharp-file.md) - C# file creation standards
- [high-performance-csharp](../skills/high-performance-csharp.md) - Performance patterns
- [validate-before-commit](../skills/validate-before-commit.md) - Pre-commit validation
