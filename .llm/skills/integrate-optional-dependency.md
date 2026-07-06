# Skill: Integrate Optional Dependency

<!-- trigger: odin, vcontainer, zenject, optional, define | Odin, VContainer, Zenject integration patterns | Feature -->

**Trigger**: When adding support for optional packages (Odin Inspector, VContainer, Zenject, Reflex, etc.) to this repository.

---

## When to Use This Skill

Use this skill when:

- Adding support for a new optional third-party package
- Creating conditional compilation patterns for optional features
- Organizing code that depends on packages that may or may not be installed
- Setting up test infrastructure for optional dependencies

For Odin Inspector-specific patterns, see [integrate-odin-inspector](./integrate-odin-inspector.md).
For testing Odin drawers specifically, see [test-odin-drawers](./test-odin-drawers.md).

---

## Overview

This skill covers patterns for integrating with optional third-party packages that may or may not be installed in a project. The key principle is: **the package should work without any optional dependency, but enhance functionality when one is present**.

---

## Core Patterns

### 1. Conditional Compilation Structure

All optional dependency code should be wrapped in conditional compilation directives **inside** the namespace:

**Correct**:

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using Sirenix.OdinInspector.Editor;
    using UnityEngine;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            // Implementation
        }
    }
#endif
}
```

**Incorrect** (directive outside namespace):

```csharp
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
        // Implementation
    }
}
#endif
```

### 2. File Organization

Each class integrating with an optional dependency should be in its own file:

```text
Editor/CustomDrawers/
├── Odin/                              # Odin-specific drawers
│   ├── IntDropDownOdinDrawer.cs
│   ├── WEnumToggleButtonsOdinDrawer.cs
│   └── WShowIfOdinDrawer.cs
├── IntDropDownDrawer.cs               # Standard Unity drawers
├── WEnumToggleButtonsDrawer.cs
└── WShowIfDrawer.cs
```

### 3. Shared Logic Extraction

When both standard Unity and optional dependency implementations share logic, extract it to a helper class:

**Correct**:

```csharp
// WButtonOdinInspectorHelper.cs - Shared logic
namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    internal static class WButtonOdinInspectorHelper
    {
        internal static void DrawInspectorGUI(Editor editor, /* params */)
        {
            // Shared implementation
        }
    }
#endif
}

// WButtonOdinMonoBehaviourInspector.cs - Thin wrapper
namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    [CustomEditor(typeof(SerializedMonoBehaviour), true)]
    public sealed class WButtonOdinMonoBehaviourInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            WButtonOdinInspectorHelper.DrawInspectorGUI(this, /* params */);
        }
    }
#endif
}
```

**Incorrect** (duplicate code across classes):

```csharp
// DON'T copy-paste the same 200 lines into multiple inspector classes
```

### 4. Common Cache Extraction

Caches used across multiple drawers/inspectors should be centralized:

**Correct**:

```csharp
// EditorCacheHelper.cs - Shared caching
public static class EditorCacheHelper
{
    private static readonly Dictionary<int, string> IntToStringCache = new();

    public static string GetCachedIntString(int value)
    {
        return IntToStringCache.GetOrAdd(value, v => v.ToString());
    }
}

// In drawers:
string display = EditorCacheHelper.GetCachedIntString(index);
```

**Incorrect** (duplicate caches):

```csharp
// Drawer1.cs
private static readonly Dictionary<int, string> IntToStringCache = new();

// Drawer2.cs
private static readonly Dictionary<int, string> IntToStringCache = new(); // Duplicate!

// Drawer3.cs
private static readonly Dictionary<int, string> IntToStringCache = new(); // Duplicate!
```

---

## Supported Optional Dependencies

| Package        | Define Symbol                           | File Location                                         |
| -------------- | --------------------------------------- | ----------------------------------------------------- |
| Odin Inspector | `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` | `Editor/CustomDrawers/Odin/`, `Editor/CustomEditors/` |
| VContainer     | `VCONTAINER`                            | `Runtime/Integrations/VContainer/`                    |
| Zenject        | `ZENJECT`                               | `Runtime/Integrations/Zenject/`                       |
| Reflex         | `REFLEX`                                | `Runtime/Integrations/Reflex/`                        |

### Dependency-Specific Skills

- **Odin Inspector**: See [integrate-odin-inspector](./integrate-odin-inspector.md) for detailed drawer patterns, property tree navigation, and shared utility architecture.

---

## Testing Optional Dependencies

### Test File Organization

For each optional dependency integration, create tests that:

1. Use the same conditional compilation
2. Are located in `Tests/Editor/CustomDrawers/{DependencyName}/` or similar
3. Have test types in separate files under `Tests/Editor/TestTypes/{DependencyName}/`

### Test Type Extraction

Test helper MonoBehaviours and ScriptableObjects **MUST** be in separate files:

**Correct**:

```text
Tests/Editor/
├── TestTypes/
│   └── Odin/
│       ├── OdinEnumToggleButtonsTarget.cs      # Each test SO/MB in own file
│       ├── OdinEnumToggleButtonsMonoBehaviour.cs
│       ├── OdinShowIfBoolTarget.cs
│       └── ...
├── CustomDrawers/
│   └── Odin/
│       ├── WEnumToggleButtonsOdinDrawerTests.cs  # Test class only
│       └── WShowIfOdinDrawerTests.cs
```

**Incorrect** (embedded test types):

```csharp
// WEnumToggleButtonsOdinDrawerTests.cs
[TestFixture]
public sealed class WEnumToggleButtonsOdinDrawerTests
{
    [Test]
    public void TestSomething() { }

    // WRONG - These should be in separate files!
    private sealed class OdinEnumToggleButtonsTarget : SerializedScriptableObject
    {
        public TestEnum testField;
    }

    private enum TestEnum { A, B, C }  // WRONG - Should be shared
}
```

### Shared Test Enums

Common enums used across multiple test files should be centralized:

```csharp
// Tests/Editor/TestTypes/SharedTestEnums.cs
namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    public enum TestModeEnum { ModeA, ModeB, ModeC }

    [Flags]
    public enum TestFlagsEnum { None = 0, Flag1 = 1, Flag2 = 2, Flag3 = 4 }

    public enum SmallTestEnum { One, Two, Three }
}
```

---

## Checklist for New Optional Dependency Integration

1. [ ] Create dedicated folder: `Editor/CustomDrawers/{DependencyName}/`
2. [ ] Place `#if` directives **inside** namespace
3. [ ] One class per file
4. [ ] Extract shared logic to helper classes
5. [ ] Extract shared caches to `EditorCacheHelper` or similar
6. [ ] Create test folder: `Tests/Editor/CustomDrawers/{DependencyName}/`
7. [ ] Create test types folder: `Tests/Editor/TestTypes/{DependencyName}/`
8. [ ] Extract all test MonoBehaviours/ScriptableObjects to separate files
9. [ ] Share test enums in `SharedTestEnums.cs`
10. [ ] Generate `.meta` files for all new files

---

## Anti-Patterns to Avoid

| Anti-Pattern              | Why It's Wrong                               | Correct Approach        |
| ------------------------- | -------------------------------------------- | ----------------------- |
| `#if` outside namespace   | Inconsistent with project style              | Put inside namespace    |
| Multiple classes per file | Hard to navigate, Unity serialization issues | One class per file      |
| Duplicate caches          | Memory waste, maintenance burden             | Centralize in helper    |
| Duplicate shared logic    | DRY violation, bug divergence                | Extract to helper class |
| Embedded test types       | Unity serialization errors                   | Separate files          |
| Duplicate test enums      | Maintenance burden                           | Shared enum file        |

---

## VContainer Integration Pattern

```csharp
namespace WallstopStudios.UnityHelpers.Runtime.Integrations.VContainer
{
#if VCONTAINER
    using global::VContainer;
    using global::VContainer.Unity;

    public sealed class UnityHelpersInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            // Register services
            builder.Register<IMyService, MyServiceImplementation>(Lifetime.Singleton);
        }
    }
#endif
}
```

---

## Zenject Integration Pattern

```csharp
namespace WallstopStudios.UnityHelpers.Runtime.Integrations.Zenject
{
#if ZENJECT
    using global::Zenject;

    public sealed class UnityHelpersInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            Container.Bind<IMyService>()
                .To<MyServiceImplementation>()
                .AsSingle();
        }
    }
#endif
}
```

---

## Reflex Integration Pattern

```csharp
namespace WallstopStudios.UnityHelpers.Runtime.Integrations.Reflex
{
#if REFLEX
    using global::Reflex.Core;

    public sealed class UnityHelpersInstaller : IInstaller
    {
        public void InstallBindings(ContainerBuilder builder)
        {
            builder.AddSingleton<IMyService, MyServiceImplementation>();
        }
    }
#endif
}
```

---

## Assembly Definition Configuration

When optional dependencies affect assembly definitions, use Version Defines:

```json
{
  "name": "WallstopStudios.UnityHelpers.Editor",
  "references": ["WallstopStudios.UnityHelpers"],
  "versionDefines": [
    {
      "name": "odininspector",
      "expression": "0.0.1",
      "define": "WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR"
    },
    {
      "name": "jp.hadashikick.vcontainer",
      "expression": "",
      "define": "VCONTAINER"
    },
    {
      "name": "com.svermeulen.extenject",
      "expression": "",
      "define": "ZENJECT"
    }
  ]
}
```

### Transitive Precompiled References (Critical)

When an assembly has `"overrideReferences": true`, it can ONLY see precompiled DLLs explicitly listed in its `precompiledReferences`. These references do NOT propagate transitively through assembly references.

**Example**: an assembly that directly compiles Odin test targets derived from `SerializedScriptableObject` must include `Sirenix.Serialization.dll` in its own `precompiledReferences` and gate those files with `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR`. Runtime conditional Odin base aliases are the only runtime Sirenix use; do not add Sirenix DLLs merely because an assembly references `WallstopStudios.UnityHelpers`.

**When splitting assemblies**: Always audit the parent assembly's `precompiledReferences` and propagate required DLLs to each child. See [manage-assembly-definitions](./manage-assembly-definitions.md) for the full checklist.

---

## Related Skills

- [integrate-odin-inspector](./integrate-odin-inspector.md) - Detailed Odin Inspector drawer patterns
- [test-odin-drawers](./test-odin-drawers.md) - Testing patterns for Odin drawers
- [add-inspector-attribute](./add-inspector-attribute.md) - Inspector attributes with Odin compatibility
- [create-property-drawer](./create-property-drawer.md) - Unity PropertyDrawer creation patterns
- [editor-caching-patterns](./editor-caching-patterns.md) - Centralized caching for editor code
