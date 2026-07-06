# Skill: Manage Assembly Definitions

<!-- trigger: asmdef, assembly, split, precompiled, references | Assembly definition creation, splitting, and reference management | Feature -->

**Trigger**: When creating, modifying, or splitting Unity assembly definition (`.asmdef`) files.

---

## When to Use This Skill

Use this skill when:

- Creating a new `.asmdef` file for a new directory
- Splitting an existing assembly into smaller child assemblies
- Adding or modifying `precompiledReferences` in an `.asmdef`
- Debugging compilation errors related to missing assembly references (CS0012, CS0311)
- Moving test files between assembly boundaries

---

## Mandatory Rule: Always Run the Linter

After creating or modifying ANY `.asmdef` file, you **MUST** run:

```bash
pwsh -NoProfile -File scripts/lint-asmdef.ps1
```

This validates assembly references and Unity version-define expressions. Skipping this step risks introducing assembly drift that only surfaces during Unity compilation.

---

## Critical Rule: Direct Precompiled References

**When `overrideReferences` is `true`, each assembly must independently list ALL precompiled DLLs directly used by source compiled into that assembly.**

Unity's `overrideReferences: true` means the assembly can ONLY see the DLLs explicitly listed in its `precompiledReferences`. Precompiled references do not propagate transitively. Do not add Sirenix DLLs merely because an assembly references `WallstopStudios.UnityHelpers`; add them only when an `overrideReferences: true` assembly directly compiles Odin/Sirenix source. The runtime asmdef uses `overrideReferences: false` for its guarded Odin base aliases so no-Odin registry installs do not name missing Sirenix DLLs.

### Optional Odin Integration

This package uses the package-owned `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` symbol for Odin-specific source. Runtime singleton base classes use conditional aliases so Odin projects keep Odin serialization and non-Odin projects use Unity bases:

```csharp
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
using ScriptableObjectSingletonBase = Sirenix.OdinInspector.SerializedScriptableObject;
#else
using ScriptableObjectSingletonBase = UnityEngine.ScriptableObject;
#endif

public abstract class ScriptableObjectSingleton<T> : ScriptableObjectSingletonBase
```

Odin-specific drawers, inspectors, and test targets stay behind:

```csharp
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;

    public sealed class MyOdinDrawer : OdinAttributeDrawer<MyAttribute>
    {
    }
#endif
}
```

---

## Splitting Assemblies: Step-by-Step Checklist

When splitting a parent assembly into child assemblies:

1. **Audit the parent's `precompiledReferences`** — every DLL listed there is a candidate for child assemblies
2. **For each new child asmdef**, determine which precompiled DLLs it directly needs:
   - Search the child's source files for usage of types from each DLL
   - Check optional-dependency guards: Odin source must use `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR`, not the global `ODIN_INSPECTOR` symbol
3. **Copy required DLLs** from the parent's `precompiledReferences` to each child
4. **Include Sirenix DLLs only for direct Odin source** in that child assembly
5. **Verify compilation** in a context where `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` is defined by the `odininspector` package

### Quick Reference: Which DLLs to Include

| If the child assembly directly uses...                              | Include in `precompiledReferences`     |
| ------------------------------------------------------------------- | -------------------------------------- |
| `SerializedScriptableObject` or `SerializedMonoBehaviour`           | `Sirenix.Serialization.dll`            |
| Odin editor APIs (`OdinAttributeDrawer`, `OdinEditor`, etc.)        | `Sirenix.OdinInspector.Editor.dll`     |
| Odin attributes from the Sirenix package (`ShowIf`, `Button`, etc.) | `Sirenix.OdinInspector.Attributes.dll` |
| JSON serialization APIs                                             | `System.Text.Json.dll`                 |
| NUnit test framework                                                | `nunit.framework.dll`                  |

---

## Standard Test Assembly Template

When creating a new test assembly that uses types from the Runtime assembly:

```json
{
  "name": "WallstopStudios.UnityHelpers.Tests.Editor.{Feature}",
  "rootNamespace": "WallstopStudios.UnityHelpers.Tests.{Feature}",
  "references": [
    "UnityEditor.TestRunner",
    "UnityEngine.TestRunner",
    "WallstopStudios.UnityHelpers",
    "WallstopStudios.UnityHelpers.Editor",
    "WallstopStudios.UnityHelpers.Tests.Core",
    "WallstopStudios.UnityHelpers.Tests.Editor"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

**Note**: Add Sirenix DLLs to `precompiledReferences` only when an `overrideReferences: true` assembly directly compiles Odin-specific source. Test assemblies that include Odin test targets must also define `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` from the `odininspector` package in `versionDefines`.

---

## Diagnosing CS0012 / CS0311 Errors

When you see errors like:

```text
error CS0012: The type 'SerializedScriptableObject' is defined in an assembly that is not referenced.
You must add a reference to assembly 'Sirenix.Serialization, Version=1.0.0.0, ...'
```

or:

```text
error CS0311: The type 'X' cannot be used as type parameter 'T' in the generic type or method 'Y'.
There is no implicit reference conversion from 'X' to 'UnityEngine.ScriptableObject'.
```

**Root cause**: The consuming assembly has `overrideReferences: true` and directly compiles source that references `SerializedScriptableObject` or `SerializedMonoBehaviour`, but is missing `Sirenix.Serialization.dll` in its `precompiledReferences`.

**Fix**: Add `"Sirenix.Serialization.dll"` to the assembly's `precompiledReferences` array.

---

## Anti-Patterns

| Anti-Pattern                                            | Why It's Wrong                                       | Correct Approach                                                                            |
| ------------------------------------------------------- | ---------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| Creating child asmdef without checking direct DLL usage | Missing direct dependencies cause CS0012             | Audit child source and list needed DLLs                                                     |
| Assuming assembly references propagate precompiled DLLs | Unity does not propagate precompiled refs            | Each assembly lists its own precompiled DLLs                                                |
| Using global `ODIN_INSPECTOR` in package source         | A project-wide define can activate code without DLLs | Use `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` from `odininspector`                            |
| Adding unguarded Sirenix runtime source                 | Registry installs can fail without Odin              | Gate runtime Odin bases behind `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` with Unity fallbacks |

---

## Related Skills

- [integrate-optional-dependency](./integrate-optional-dependency.md) - Optional dependency patterns
- [integrate-odin-inspector](./integrate-odin-inspector.md) - Odin Inspector integration
- [create-csharp-file](./create-csharp-file.md) - C# file creation patterns
- [create-test](./create-test.md) - Test creation patterns
