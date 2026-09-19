# create-csharp-file - Part 2

## Split Content

### 6. NEVER Use Nullable Reference Types

- ❌ `string?`, `object?`, `List<string>?`, `MyClass?`
- ❌ `#nullable enable`
- ❌ Null-forgiving operator `!` (e.g., `value!`)
- ✅ `int?`, `float?`, `bool?` — Nullable VALUE types are OK

### 7. Unity Object Null Checks

For `UnityEngine.Object`-derived types (`GameObject`, `Component`, `MonoBehaviour`, etc.):

- ❌ `gameObject?.SetActive(true)` — Bypasses Unity's null check
- ❌ `component ?? fallback` — Bypasses Unity's null check
- ❌ `_cached ??= GetComponent<T>()` — Bypasses Unity's null check
- ❌ `ReferenceEquals(gameObject, null)` — Bypasses Unity's null check
- ✅ `if (gameObject != null) gameObject.SetActive(true)`
- ✅ `component != null ? component : fallback`
- ✅ `if (_cached == null) _cached = GetComponent<T>()`

**The one legitimate `ReferenceEquals`, and the rule that comes with it.** The two operators ask
different questions: `ReferenceEquals(x, null)` asks _was anything handed in_, and `x == null` asks
_is it gone_ — true for a destroyed object as well as for a null reference. Code that tracks Unity
objects it did not create needs both, because an item destroyed while checked out is still the entry
in the tracking list: removing it must not be guarded by `== null`, and re-using it must be.

When you need that distinction, **name it** — an inline `ReferenceEquals` reads as a bug to every
reader and every reviewer, and an inline `== null` reads as an ordinary null check and is not one:

```csharp
private static bool WasHandedIn(T candidate) => !ReferenceEquals(candidate, null);

private static bool IsGone(T candidate) => candidate == null;
```

`where T : UnityEngine.Object` is what makes the distinction expressible: `T` is then always a
reference type, so there is no value-type case. See `TrackedObjectPool<T>` for the worked example.

### 8. Qualify `Object` References

```csharp
// ✅ CORRECT - Add using alias or fully qualify
using Object = UnityEngine.Object;

// or
UnityEngine.Object obj = ...;
```

### 9. Minimal Comments

**Aim for zero.** A comment is an exception, not a habit: the target is code whose class, method
and variable names make it unnecessary, and the first move when a comment feels needed is a better
name, not a better sentence. Spell names out -- `attributeMetadataCache`, not `attrCache`; prefer
the descriptive form over the short one everywhere
([#635](https://github.com/Ambiguous-Interactive/unity-helpers/issues/635)).

When one does survive that test, it explains **why**, never **what**, and stays
extremely minimal: one short sentence, common words, active voice
([Simplified Technical English](../skills/ship-changes.md#step-9b-open-the-pull-request-yourself)).
If it takes two sentences, either the code needs a better name or the reason
belongs in a commit message, the PR, or an issue.

- ✅ Comments explaining **why** a non-obvious approach is used
- ✅ Comments documenting Unity quirks or platform-specific behavior
- ✅ Brief notes on edge cases that aren't obvious from context
- ❌ Comments describing **what** readable code does
- ❌ Comments restating the method/variable name
- ❌ Commented-out code (use version control)
- ❌ TODO/FIXME without associated issue tracking
- ❌ Section dividers like `// ========= METHODS =========`

A non-doc comment **inside a type or a member** that spans more than one line uses the block form,
so a reader can see where it ends without counting slashes. `npm run lint:comment-block-form`
enforces this across `Runtime/`, `Editor/`, `Tests/`, and `Generator~/`, with no baseline exemptions:

```csharp
/*
    Written as one block because it spans more than one line.
    A run of `//` lines is not the house style.
*/
```

Two things are not comments for this purpose. XML documentation stays `///` -- every public member
still needs its `<summary>`. And the **two-line file license header** stays exactly as it is: it is
a fixed banner every file carries, `scripts/lint-license-headers.ps1` matches it literally, and
rewriting it as a block would be a 1,900-file diff that changes nothing a reader cares about.

```csharp
// ❌ BAD - States the obvious
// Increment the counter
counter++;

// ❌ BAD - Restates the name
// Gets the active enemies
public void GetActiveEnemies(List<Enemy> result) { }

// ✅ GOOD - Explains why (non-obvious behavior)
// Unity's null-check operator doesn't work with destroyed objects
if (gameObject != null) { }

// ✅ GOOD - Documents a constraint not obvious from code
// Must be called after Awake() completes across all objects
public void Initialize() { }
```

### 10. Preprocessor Directives: `#define` vs `#if`

**`#define` directives** MUST be placed at the **top of the file** before any tokens. This is a C# language requirement (error CS1032):

```csharp
// ✅ CORRECT - #define / #undef at file top (C# requirement)
#define ENABLE_UBERLOGGING

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    // ...
}
```

`#undef` obeys the same rule, and it undefines a compiler-supplied symbol for that file only —
which is how `ConditionalLoggingStrippedTests` reproduces a release build inside an editor test run
(a `[Conditional]` call is resolved against the symbols in effect at the **call site's file
position**).

Note the package itself no longer carries a file-scoped `#define ENABLE_UBERLOGGING`: the logging
gate moved onto the methods as `[Conditional]`, so the decision belongs to the calling assembly.

**`#if` conditional blocks** (without `#define`) should be placed **inside** the namespace for consistency:

✅ **CORRECT**:

```csharp
namespace WallstopStudios.UnityHelpers.Core
{
#if SINGLE_THREADED
    using System.Collections.Generic;
#else
    using System.Collections.Concurrent;
#endif

    public sealed class MyCache { }
}
```

❌ **INCORRECT**:

```csharp
#if SINGLE_THREADED
using System.Collections.Generic;
#else
using System.Collections.Concurrent;
#endif

namespace WallstopStudios.UnityHelpers.Core
{
    public sealed class MyCache { }
}
```

**Exception**: Unity-standard defines like `UNITY_EDITOR`, `UNITY_2021_3_OR_NEWER` may wrap entire file contents when necessary.

**Third-party package defines** (`WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR`, `VCONTAINER`, `ZENJECT`, etc.) should also be placed inside the namespace:
