# prefer-logging-extensions - Part 3

## Split Content

## Migration Checklist

When converting existing code to use logging extensions:

- [ ] Add `using WallstopStudios.UnityHelpers.Core.Extension;`
- [ ] Replace `Debug.Log(...)` with `this.Log($"...")`
- [ ] Replace `Debug.LogWarning(...)` with `this.LogWarn($"...")`
- [ ] Replace `Debug.LogError(...)` with `this.LogError($"...")`
- [ ] Ensure all strings use `$"..."` interpolation syntax
- [ ] Remove manual class name prefixes from messages (method names are not auto-injected)
- [ ] Replace manual exception formatting with exception parameter
- [ ] Rename `ex` variables to `e`
- [ ] Skip static methods (continue using `Debug.Log`)
- [ ] **Verify compilation** - If PropertyDrawer extension methods fail to resolve, fall back to `Debug.Log*`

---

## Thread Safety

The logging extensions are **thread-safe**. When called from a background thread:

1. Logs are automatically dispatched to the Unity main thread
2. If main thread dispatch fails, logs are written with an offline fallback
3. No manual thread synchronization required

---

## Build Configuration

Every entry point is a [`[Conditional]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.conditionalattribute) method carrying the whole enabling set — `ENABLE_UBERLOGGING`, `DEVELOPMENT_BUILD`, `DEBUG`, `UNITY_EDITOR`, plus its own severity symbol. Unity defines the middle three itself, so the editor and development builds need no configuration.

In release builds the compiler removes **the entire call site**, receiver and arguments included, so a disabled call costs nothing at all — no `FormattableString`, and no evaluation of an expression like `MySingleton.Instance` that would otherwise have side effects.

That decision is made in the assembly that **calls** the method, not in this package, so a symbol has to be defined project-wide (Player Settings → Scripting Define Symbols) to take effect. There is no longer a file-scoped `#define` inside the package; a define that reached only this assembly would change nothing for your code.

**Granular logging defines**: individual levels can be enabled with `DEBUG_LOGGING`, `WARN_LOGGING`, and `ERROR_LOGGING` while the others stay compiled out.

---

## Related Skills

- [defensive-programming](../skills/defensive-programming.md) - Logging guidelines for defensive code
- [use-extension-methods](../skills/use-extension-methods.md) - Other extension methods available
- [high-performance-csharp](../skills/high-performance-csharp.md) - Performance considerations for logging
