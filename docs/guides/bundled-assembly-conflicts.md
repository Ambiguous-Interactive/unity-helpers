# Bundled Assembly Conflicts

Unity Helpers ships four assemblies under `Runtime/Binaries/` that the .NET profile does not
provide: `System.Text.Json`, `System.Text.Encodings.Web`, `Microsoft.Bcl.AsyncInterfaces`, and
`System.IO.Pipelines`. Other packages ship some of the same names. This page explains what
happens when they collide and how to resolve it.

## Symptom

```text
error CS0012: The type 'JavaScriptEncoder' is defined in an assembly that is not referenced.
You must add a reference to assembly 'System.Text.Encodings.Web, Version=9.0.0.0, ...'.
```

The error is reported against this package's JSON converters, but the package is not the cause.

## Why it happens

Unity deduplicates precompiled assemblies **by simple name**: exactly one file wins for
`System.Text.Encodings.Web`, no matter how many copies the project contains. Which one wins is
Unity's choice, and the losers vanish from every compile set.

That is harmless when the winner behaves like ours, which is marked auto-referenced
(`isExplicitlyReferenced: 0`) so every assembly gets it. It breaks when the winner is marked
**explicitly referenced** (`isExplicitlyReferenced: 1`), because then:

- our copy lost the name and is gone, and
- the winning copy is never auto-referenced into anything.

The name ends up referenced by **zero** assemblies, and any type it provides becomes
unresolvable. `JavaScriptEncoder` appears in `System.Text.Json`'s public API surface
(`JsonSerializerOptions.Encoder`, `JsonWriterOptions.Encoder`), so every converter signature that
touches those options fails with `CS0012`.

`com.unity.ai.assistant` is the known case. It ships all four names under `Plugins/Shared/`, all
`isExplicitlyReferenced: 1`.

## Diagnosing it

Ask Unity which file won each name, from any editor script:

```csharp
string winner = UnityEditor.Compilation.CompilationPipeline
    .GetPrecompiledAssemblyPathFromAssemblyName("System.Text.Encodings.Web.dll");
```

If that path is not the one you expect, count how many assemblies actually reference it:

```csharp
UnityEditor.Compilation.Assembly[] assemblies = UnityEditor.Compilation.CompilationPipeline
    .GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor);
```

A healthy project references the name from nearly every assembly. A broken one references it from
none.

## Resolving it

`com.unity.ai.assistant` gates each of its copies behind an opt-out define. Add the ones you need
to **Project Settings → Player → Scripting Define Symbols**:

| Define                             | Drops                                    |
| ---------------------------------- | ---------------------------------------- |
| `EXCLUDE_TEXT_JSON`                | `System.Text.Json`                       |
| `EXCLUDE_TEXT_ENCODINGS_WEB`       | `System.Text.Encodings.Web`              |
| `EXCLUDE_BCL_ASYNCINTERFACES`      | `Microsoft.Bcl.AsyncInterfaces`          |
| `EXCLUDE_COMPILER_SERVICES_UNSAFE` | `System.Runtime.CompilerServices.Unsafe` |

Dropping the explicitly-referenced copy lets this package's auto-referenced copy win the name, and
the AI Assistant's own assemblies then resolve against it — they are the same 9.0.0.0 assemblies.
Verified on Unity `6000.4.6f1`: `System.Text.Encodings.Web` went from 0 of 245 assemblies to 221
of 221, and every `Unity.AI.Assistant.*` assembly still compiled.

Only add the defines for names that actually resolve to the wrong file. `System.Text.Json` frequently
resolves to this package's copy already, in which case `EXCLUDE_TEXT_JSON` changes nothing.

## Unity 6000.5 and newer

Unity `6000.5` began shipping `System.Text.Json`, `System.Text.Encodings.Web`, and
`Microsoft.Bcl.AsyncInterfaces` itself, from `Editor/Data/BCLExtensions/`, at version `8.0.0.0`.
This package's copies of those three are constrained to `!UNITY_6000_5_OR_NEWER` so the editor's
own copies win there and nothing competes for the name.

Below `6000.5` — including every `6000.0` through `6000.4` release — Unity ships none of them and
this package's copies are required. `System.IO.Pipelines` is never provided by Unity and ships
unconditionally.

## What this package deliberately does not ship

`System.Runtime.CompilerServices.Unsafe` is used across the runtime (`EnumExtensions`,
`ReflectionHelpers`, `Objects`, `AbstractRandom`, `RuntimeSingleton`) and is **not** bundled. Every
editor in the support matrix provides it, which the CI matrix demonstrates on every run: 2021.3,
2022.3, 6000.3, and 6000.5 all compile the package. Adding a copy would put a fourth source in play
for a contested name, and competing sources are the mechanism behind every failure on this page.

Both decisions — which assemblies are constrained and which are deliberately absent — are enforced
by `scripts/lint-bundled-assemblies.ps1`. That matters because the fix here is invisible: a NuGet
refresh that regenerates an importer without its constraint, or drops in a new DLL, would silently
restore the conflict. The linter fails on an unclassified DLL, so a refresh cannot proceed without a
conscious decision recorded here.
