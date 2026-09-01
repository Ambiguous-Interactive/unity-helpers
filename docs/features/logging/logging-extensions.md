# Unity Logging Extensions & Tag Formatter

Bring structured, color-coded logs to any Unity project without sprinkling `Debug.Log` everywhere. `WallstopStudiosLogger` adds extension methods (`this.Log`, `this.LogWarn`, `this.LogError`, `this.LogDebug`) that automatically capture component metadata, thread info, timestamps, and user-defined tags rendered by `UnityLogTagFormatter`.

- **Thread-safe:** Logs are marshaled back to the Unity main thread when required (via `UnityMainThreadDispatcher` / `UnityMainThreadGuard`).
- **Readable output:** Pretty mode prefixes `time|GameObject[Component]` when logging on the main thread and inserts `|thread|` only when background workers emit messages, keeping logs deterministic without extra noise.
- **Tag formatter:** Apply rich text decorations inline (`$"{name:b,color=cyan}"`) without string concatenation. Tags deduplicate automatically and can be stacked in any order.

> These helpers live in `Runtime/Core/Extension/WallstopStudiosLogger.cs` and `Runtime/Core/Helper/Logging/UnityLogTagFormatter.cs`. Tests at `Tests/Runtime/Extensions/LoggingExtensionTests.cs` demonstrate every supported scenario.

---

## Sample Scene

- Import the `Logging – Tag Formatter` package sample and open `Samples~/Logging - Tag Formatter/Scenes/LoggingDemo.unity`.
- Press Play to use the on-screen toggles (global logging, component logging, pretty output) and emit Info/Warn/Error logs that showcase the decorators.
- Review `LoggingDemoBootstrap` (decorator registration) and `LoggingDemoController` (runtime toggles + `this.Log*` usage) to copy the patterns into your project.

---

## Quick Start

<!-- doc-sample: compiles -->

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Extension;

public sealed class EnemyHUD : MonoBehaviour
{
    private void Start()
    {
        string mode = Application.isEditor ? "Test" : "Live";
        int hp = 42;

        this.Log(
            $"Player {"Rogue-17":b,color=orange} :: HP {hp:color=#FF4444} ({mode:italic})"
        );
    }
}
```

- Pass interpolated strings directly; the formatter applies tags before Unity renders the message.
- Use `pretty: false` if you only want the decorated text without the timestamp (or optional thread) prefix.
- Call `this.LogWarn`, `this.LogError`, or `this.LogDebug` for severity-specific output; all overloads accept `Exception e` to append stack traces.

### Enabling logging in builds

Logging is on wherever Unity defines `UNITY_EDITOR`, `DEVELOPMENT_BUILD`, or `DEBUG`, so the editor and development builds need no configuration. To keep it in a release build, define `ENABLE_UBERLOGGING` (or the per-severity `DEBUG_LOGGING` / `WARN_LOGGING` / `ERROR_LOGGING`) in **Player Settings → Scripting Define Symbols**.

The define has to be project-wide. Each entry point is a [`[Conditional]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.conditionalattribute) method, and the compiler decides whether to keep a call by looking at the symbols of the assembly that **calls** it, not the ones this package was compiled with. That is what makes a disabled call genuinely free:

```csharp
// Logging off: this whole line is removed, so Instance is never read and no
// FormattableString is built. The singleton is not created.
CoroutineHandler.Instance.Log($"Finished unloading {scene.name}.");
```

The same applies to `Helpers.LogNotAssigned` and `ValidateAssignments`, whose only observable effect is a log; with logging off, `ValidateAssignments` no longer performs its reflection walk. Use `AreAnyAssignmentsInvalid` when you need the answer in every build configuration.

---

## Default Tag Reference

| Tag syntax                              | Effect                          | Notes                                                 |
| --------------------------------------- | ------------------------------- | ----------------------------------------------------- |
| `:b`, `:bold`, `:!`                     | Wraps value in `<b>`            | Editor-only (uses Unity rich text)                    |
| `:i`, `:italic`, `:_`                   | Wraps value in `<i>`            | Editor-only                                           |
| `:json`                                 | Serializes value via `ToJson()` | Works in player builds                                |
| `:#color`, `:color=name`, `:color=#hex` | Wraps with `<color=...>`        | Named colors resolve to `UnityEngine.Color` constants |
| `:42`, `:size=42`                       | Wraps with `<size=42>`          | Integers 1–100 (or any positive int)                  |

- Combine tags using commas: `$"{stats:json,b,color=yellow}"` emits bold, colored JSON.
- Tags are applied in priority order and deduplicate automatically, so repeating `:b` has no effect.

---

## Custom Decorations

Register project-specific tags at startup (for example, in an `InitializeOnLoad` editor script or a runtime bootstrapper):

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;
using WallstopStudios.UnityHelpers.Core.Helper.Logging;

[InitializeOnLoad]
internal static class LoggingBootstrap
{
    static LoggingBootstrap()
    {
        UnityLogTagFormatter formatter = WallstopStudiosLogger.LogInstance;

        formatter.AddDecoration(
            predicate: tag => tag.StartsWith("stat:", StringComparison.OrdinalIgnoreCase),
            format: (tag, value) =>
            {
                string label = tag.Substring("stat:".Length);
                return $"<color=#7AD7FF>[{label}]</color> {value}";
            },
            tag: "StatLabel",
            priority: -10 // run before built-ins
        );
    }
}
```

Key APIs:

- `AddDecoration(string match, Func<object,string> format, string tag, int priority = 0, bool editorOnly = false, bool force = false)`
- `AddDecoration(Func<string,bool> predicate, Func<string,object,string> format, string tag, int priority = 0, bool editorOnly = false, bool force = false)`
- `RemoveDecoration(string tag, out Decoration removed)` to swap or disable decorators at runtime.
- `UnityLogTagFormatter.Separator (',')` controls how stacked tags are parsed.

Use negative priorities for “outer” wrappers (run earlier) and higher numbers for final passes. Setting `force: true` replaces existing tags with the same name.

---

## Extension Method Cheat Sheet

| API                                                                                                      | Description                                                                                       |
| -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
| `component.Log(FormattableString, Exception e = null, bool pretty = true, bool stackTrace = true)`       | Sends an info log through the formatter. `[Conditional]` on `ENABLE_UBERLOGGING`/`DEBUG_LOGGING`. |
| `component.LogWarn(...)`, `component.LogError(...)`, `component.LogDebug(...)`                           | Severity-specific variants with the same signature.                                               |
| `component.GenericToString()`                                                                            | Serializes all public fields/properties into JSON (used by the formatter when you pass `:json`).  |
| `component.EnableLogging()` / `component.DisableLogging()`                                               | Per-object toggle. Disabled components are skipped without allocations.                           |
| `component.GlobalEnableLogging()` / `component.GlobalDisableLogging()` / `SetGlobalLoggingEnabled(bool)` | Global kill switch suitable for in-game consoles or dev toggles.                                  |
| `WallstopStudiosLogger.IsGlobalLoggingEnabled()`                                                         | Query current state (useful for tooling UIs).                                                     |

Additional behavior:

- **Thread routing:** If a log originates off the main thread, the extension tries `UnityMainThreadDispatcher.TryDispatchToMainThread` first. If unavailable, it falls back to `UnityMainThreadGuard.TryPostToMainThread` and, if that fails, emits an “offline” log with a `[WallstopMainThreadLogger:*]` prefix.
- **Pretty output:** Keeps logs uniform (`timestamp|GameObject[Component]|message` on the main thread, inserting `|thread|` only for worker threads). Pass `pretty: false` when emitting data the Unity console already decorates (for example, performance CSV dumps).
- **Context awareness:** Unity context objects are forwarded to `Debug.Log*`, preserving click-to-focus navigation even when logs originate from pooled helper classes.
- **`stackTrace: false` for a diagnostic that repeats:** Unity captures a managed stack trace for every log whose type is configured `ScriptOnly`, which is the default for all three severities. Measured on `6000.4.6f1`, that capture is **178.4 µs** of a 178.4 µs call, against **13.3 µs** for the same message with the trace suppressed (13.4x, paid per call). Pass `stackTrace: false` for anything logged once per object at load or once per frame; the message, its context and its click-to-focus all survive. Keep the default everywhere else: a one-off error is worth a stack.

---

## Best Practices

1. **Register tags once**: Use static constructors or `[RuntimeInitializeOnLoadMethod]` to register project-wide tags. Avoid allocating per-frame delegates.
2. **Prefer interpolation**: `$"{health:json}"` keeps minimal formatting allocations compared to `string.Format`.
3. **Use `stackTrace: false` for repeated diagnostics**: A message that already names its component, field and type gains nothing from a stack that is the same internal path every time, and the capture is 13.4x the cost of the log. This is what a relational field finding nothing now does ([#564](https://github.com/Ambiguous-Interactive/unity-helpers/issues/564)).
4. **Use `pretty: false` for exporters**: When writing to files or parsing logs, disable prefixes to simplify downstream tooling.
5. **Gate release builds**: If you plan to leave logging enabled in production, define `ENABLE_UBERLOGGING` (or `DEBUG_LOGGING` / `WARN_LOGGING` / `ERROR_LOGGING`) project-wide and make sure log volume is acceptable (or wrap noisy calls in your own `#define`s). Leaving them undefined costs nothing at all; the calls are not compiled.
6. **Use the tests**: `Tests/Runtime/Extensions/LoggingExtensionTests.cs` covers every default tag and stacking scenario. Copy those patterns when adding new decorations to ensure behavior stays deterministic.

---

## Related Topics

- [Unity Main Thread Dispatcher](./unity-main-thread-dispatcher.md): Ensures background logs can find the main thread safely.
- [Helper Utilities Overview](../utilities/helper-utilities.md): Highlights other runtime helpers.
