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

<!-- doc-sample: compiles-editor -->

```csharp
string winner = UnityEditor.Compilation.CompilationPipeline
    .GetPrecompiledAssemblyPathFromAssemblyName("System.Text.Encodings.Web.dll");
```

If that path is not the one you expect, count how many assemblies actually reference it:

<!-- doc-sample: compiles-editor -->

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
the AI Assistant's own assemblies then resolve against it; they are the same 9.0.0.0 assemblies.
Verified on Unity `6000.4.6f1`: `System.Text.Encodings.Web` went from 0 of 245 assemblies to 221
of 221, and every `Unity.AI.Assistant.*` assembly still compiled.

Only add the defines for names that actually resolve to the wrong file. `System.Text.Json` frequently
resolves to this package's copy already, in which case `EXCLUDE_TEXT_JSON` changes nothing.

## Unity 6000.5 and newer

Unity `6000.5` began shipping `System.Text.Json`, `System.Text.Encodings.Web`, and
`Microsoft.Bcl.AsyncInterfaces` itself, from `Editor/Data/BCLExtensions/`, at version `8.0.0.0`.
This package's copies of those three are constrained to `!UNITY_6000_5_OR_NEWER` so the editor's
own copies win there and nothing competes for the name.

Below `6000.5` (including every `6000.0` through `6000.4` release), Unity ships none of them and
this package's copies are required. `System.IO.Pipelines` is never provided by Unity and ships
unconditionally.

## Unsafe and Immutable dependencies

`System.Runtime.CompilerServices.Unsafe` is used across the runtime (`EnumExtensions`,
`ReflectionHelpers`, `Objects`, `AbstractRandom`, `RuntimeSingleton`). The package already
contains an overridable Unsafe **6.1.2** assembly under `Runtime/Protobuf-Net/`, alongside
Immutable **9.0.8**. The Unsafe importer disables the Editor platform; the Immutable importer
enables it. Check the actual winning assembly path and references in the consuming editor.
Importer settings alone do not identify an assembly already loaded into a running domain.

Do not add another Unsafe DLL under `Runtime/Binaries/`. Unity also supplies this name,
and adding another competing source can change assembly resolution. The linter's deliberate
absence rule applies to `Runtime/Binaries/`; it does not inventory the separate protobuf
dependency directory.

The constraints and absence rules for `Runtime/Binaries/` are enforced
by `scripts/lint-bundled-assemblies.ps1`. That matters because the fix here is invisible: a NuGet
refresh that regenerates an importer without its constraint, or drops in a new DLL, would silently
restore the conflict. The linter fails on an unclassified DLL, so a refresh cannot proceed without a
conscious decision recorded here.

## Dependency upgrade candidates

Dependency updates need tests against Unity's actual assembly set before publication.
A NuGet package's .NET Standard target does not prove that its dependency versions
or APIs match every supported Unity editor and player.

The protobuf-net 3.4.40 candidate can be built with
`python3 scripts/build-protobuf-candidate.py --output /tmp/protobuf-candidate`.
It requires SDK 9.0.319 and produces isolated binaries and a build receipt. The
host differential project selects those binaries with `ProtobufNetOracle=candidate`
and `ProtobufNetCandidateRoot=/tmp/protobuf-candidate/assemblies`. The frozen v2/v3
lanes continue to use their original binaries.

This candidate requires newer Immutable and the existing Unsafe 6.1.2 version. Its host test
lane resolves those through NuGet; that result does not authorize bundling new
Unity assemblies. The package continues to ship its existing runtime dependencies
until the candidate passes the supported compiler, Unity, retained-save and native
health gates tracked in [#1052](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1052).
