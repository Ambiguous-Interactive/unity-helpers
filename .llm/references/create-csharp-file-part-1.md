# create-csharp-file - Part 1

## Split Content

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

See [license-headers](../skills/license-headers.md) for full rules.

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
- See [no-regions](../skills/no-regions.md) for alternatives

### 5b. Member Ordering (#672) and Nested Types Go LAST

**One member ordering, enforced at 100%** by `npm run lint:nested-type-placement` (which also
enforces the nested-type rule below). Every tier is ordered `public` → `protected` → `internal` →
`private`, including `const`:

1. `const`
2. events
3. delegates
4. static properties
5. static fields
6. properties
7. fields
8. constructors
9. static methods
10. methods

Two details are deliberate and recorded in the issue so nobody "fixes" them back: static
properties come before static fields and properties before fields (the reverse of StyleCop's
SA1201 default), and `const` takes the accessibility ordering too. Events and delegates — which
the issue's list does not name — take one tier of their own immediately after `const`: declared
surface reads like the consts it accompanies.

```csharp
public sealed class Attribute
{
    public const int MaxValue = 100;          // 1. const (public before private)
    public event Action Changed;              // 2. events
    public static float DefaultScale { get; set; }   // 4. static properties
    internal static int Instances;            // 5. static fields
    public float CurrentValue => ...;         // 6. properties
    private readonly float _baseValue;        // 7. fields
    public Attribute(float baseValue) { ... } // 8. constructors
    public static Attribute Default() => ...; // 9. static methods
    private RemainingActions ApplyModificationsInOrder(...) { ... }  // 10. methods

    // nested type last (see below)
    private readonly struct RemainingActions
    {
        public readonly bool hasMultiplication;
    }
}
```

**Nested types go LAST** — after every member — or in their own file. Never between members. A
reader scrolling for a method should not have to step over a type declaration to find it, and a
nested type in the middle reads as the start of a new file's worth of content. Owner review, PR
\#574. `--fix` moves what it can, and refuses a type whose move would take it across a `#if`
boundary into a different build. The backlog it was written for -- 459 sites across 177 files --
is swept to zero ([#575](https://github.com/Ambiguous-Interactive/unity-helpers/issues/575)); the
member-ordering sweep that adopted the full rule across 2224 files is #672.
