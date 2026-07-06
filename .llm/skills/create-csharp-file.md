# Skill: Create C# File

<!-- trigger: create, new, file, class, csharp, cs | Creating any new .cs file | Core -->

**Trigger**: When creating any new `.cs` file in this repository.

---

## Pre-Creation Checklist

1. **Determine file location**:
   - Runtime code → `Runtime/` folder tree
   - Editor-only code → `Editor/` folder tree
   - Tests → `Tests/Runtime/` or `Tests/Editor/` (mirror source structure)

2. **One file per MonoBehaviour/ScriptableObject**:
   - Each class deriving from `MonoBehaviour` or `ScriptableObject` MUST have its own dedicated `.cs` file
   - This applies to **ALL code**: production (`Runtime/`, `Editor/`) AND tests (`Tests/`)
   - ❌ Multiple MonoBehaviours/ScriptableObjects in the same file
   - ❌ Test helper MonoBehaviours/ScriptableObjects defined inside test class files
   - ❌ Nested classes deriving from MonoBehaviour/ScriptableObject
   - ✅ Create separate `MyTestComponent.cs`, `TestHelperScriptableObject.cs` files
   - Enforced by pre-commit hook and CI/CD analyzer

---

## File Template

```csharp
// MIT License - Copyright (c) {CURRENT_YEAR} wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.{Subsystem}
{
#if CONDITIONAL_FEATURE
    using System;
#endif
    using System.Collections.Generic;
    using UnityEngine;

    public sealed class MyClass
    {
        // Implementation - let descriptive names speak for themselves
    }
}
```

### License Header (REQUIRED)

Every new C# file MUST include the MIT license header as the **first two lines**:

```csharp
// MIT License - Copyright (c) {CURRENT_YEAR} wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
```

**Critical**: Replace `{CURRENT_YEAR}` with the **actual current year** when creating the file:

- ✅ `// MIT License - Copyright (c) 2026 wallstop` (if current year is 2026)
- ❌ `// MIT License - Copyright (c) 2023 wallstop` (hardcoded past year)

The year reflects when the file was created, NOT when the project started. Use the current calendar year at the time of file creation.

See [license-headers](./license-headers.md) for full rules.

---

## Critical Rules

### 1. `using` Directives INSIDE Namespace

✅ **CORRECT**:

```csharp
namespace WallstopStudios.UnityHelpers.Core
{
    using System;
    using UnityEngine;

    public sealed class MyClass { }
}
```

❌ **INCORRECT**:

```csharp
using System;
using UnityEngine;

namespace WallstopStudios.UnityHelpers.Core
{
    public sealed class MyClass { }
}
```

### 2. NO Underscores in Method Names

- ✅ `GetValueWhenInputIsEmpty`
- ❌ `GetValue_When_Input_Is_Empty`
- Applies to ALL methods including tests

### 3. Explicit Types Over `var`

- ✅ `List<string> items = new List<string>();`
- ❌ `var items = new List<string>();`

### 4. Braces Required for All Control Structures

```csharp
// ✅ CORRECT
if (condition)
{
    DoSomething();
}

// ❌ INCORRECT
if (condition)
    DoSomething();
```

### 5. NEVER Use `#region`

- ❌ `#region Helper Methods`
- ❌ `#endregion`
- Organize code through class structure and file organization instead
- See [no-regions](./no-regions.md) for alternatives

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

### 8. Qualify `Object` References

```csharp
// ✅ CORRECT - Add using alias or fully qualify
using Object = UnityEngine.Object;

// or
UnityEngine.Object obj = ...;
```

### 9. Minimal Comments

Comments should explain **why**, never **what**. Rely on descriptive names and obvious call patterns.

- ✅ Comments explaining **why** a non-obvious approach is used
- ✅ Comments documenting Unity quirks or platform-specific behavior
- ✅ Brief notes on edge cases that aren't obvious from context
- ❌ Comments describing **what** readable code does
- ❌ Comments restating the method/variable name
- ❌ Commented-out code (use version control)
- ❌ TODO/FIXME without associated issue tracking
- ❌ Section dividers like `// ========= METHODS =========`

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
// ✅ CORRECT - #define at file top (C# requirement)
#if !ENABLE_UBERLOGGING && (DEVELOPMENT_BUILD || DEBUG || UNITY_EDITOR)
#define ENABLE_UBERLOGGING
#endif

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    // ...
}
```

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

```csharp
// ✅ CORRECT - Odin directive inside namespace
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        // Implementation
    }
#endif
}

// ❌ INCORRECT - Odin directive outside namespace
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        // Implementation
    }
}
#endif
```

See [integrate-optional-dependency](./integrate-optional-dependency.md) for complete patterns.

---

## Post-Creation Steps (MANDATORY)

1. **Generate meta file** (required — do not skip):

   ```bash
   ./scripts/generate-meta.sh <path-to-file.cs>
   ```

   > ⚠️ See [create-unity-meta](./create-unity-meta.md) for full details. This step is **mandatory** — every `.cs` file MUST have a corresponding `.meta` file.

2. **Format code**:

   ```bash
   dotnet tool run csharpier format .
   ```

3. **Spell-check** (cspell lints C# comments, XML docs, and log strings):

   ```bash
   npm run lint:spelling
   ```

   See [Rule 4: Spell-Check Every Change cspell Covers](./validate-before-commit.md#rule-4-spell-check-every-change-cspell-covers) for the failure-recovery decision tree.

4. **Add XML documentation** for all public types and members:

   ```csharp
   /// <summary>
   /// Brief description of the type or member.
   /// </summary>
   /// <param name="paramName">Description of parameter.</param>
   /// <returns>Description of return value.</returns>
   public int MyMethod(string paramName) { }
   ```

   > See [update-documentation](./update-documentation.md) for XML doc standards.

5. **Update CHANGELOG** for user-facing changes:
   - New features → `### Added` section
   - Bug fixes → `### Fixed` section
   - See [update-documentation](./update-documentation.md) for format

6. **Verify no errors**:
   - Check IDE for compilation errors
   - Ensure `.asmdef` references are correct if adding new namespaces

---

## Related Skills

- [high-performance-csharp](./high-performance-csharp.md) — Zero-allocation patterns (MANDATORY for all code)
- [defensive-programming](./defensive-programming.md) — Robust error handling (MANDATORY for all code)
- [create-test](./create-test.md) — Testing guidelines
- [update-documentation](./update-documentation.md) — Documentation standards
- [create-unity-meta](./create-unity-meta.md) — Meta file generation

---

## Naming Conventions Quick Reference

| Element               | Convention  | Example                     |
| --------------------- | ----------- | --------------------------- |
| Types, public members | PascalCase  | `SerializableDictionary`    |
| Fields, locals        | camelCase   | `keyValue`, `itemCount`     |
| Interfaces            | `I` prefix  | `IResolver`, `ISpatialTree` |
| Type parameters       | `T` prefix  | `TKey`, `TValue`            |
| Events                | `On` prefix | `OnValueChanged`            |
| Constants (public)    | PascalCase  | `DefaultCapacity`           |
