# integrate-optional-dependency - Part 1

## Split Content

**Trigger**: When adding support for optional packages (Odin Inspector, VContainer, Zenject, Reflex, etc.) to this repository.

---

## When to Use This Skill

Use this skill when:

- Adding support for a new optional third-party package
- Creating conditional compilation patterns for optional features
- Organizing code that depends on packages that may or may not be installed
- Setting up test infrastructure for optional dependencies

For Odin Inspector-specific patterns, see [integrate-odin-inspector](../skills/integrate-odin-inspector.md).
For testing Odin drawers specifically, see [test-odin-drawers](../skills/test-odin-drawers.md).

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

- **Odin Inspector**: See [integrate-odin-inspector](../skills/integrate-odin-inspector.md) for detailed drawer patterns, property tree navigation, and shared utility architecture.

---
