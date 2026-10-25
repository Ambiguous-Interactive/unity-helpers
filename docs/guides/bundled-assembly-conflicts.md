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

The error can appear in a JSON converter even when the missing reference comes from duplicate
DLL ownership or the caller's assembly definition. Identify the selected DLL and the failing
assembly before changing imports.

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

Then inspect which compiled assemblies reference that name. This Editor snippet logs each
caller's full reference path. It matches filenames after normalizing separators, so a relative
selected path can be compared with the absolute paths in the compiler's reference list:

<!-- doc-sample: compiles-editor -->

```csharp
using System;
using System.IO;
using UnityEditor.Compilation;
using UnityEngine;

const string assemblyFileName = "System.Text.Encodings.Web.dll";
string selected = CompilationPipeline.GetPrecompiledAssemblyPathFromAssemblyName(assemblyFileName);
Debug.Log($"Selected encodings assembly: {selected}");

foreach (UnityEditor.Compilation.Assembly assembly in
    CompilationPipeline.GetAssemblies(AssembliesType.Editor))
{
    foreach (string reference in assembly.compiledAssemblyReferences)
    {
        string fileName = Path.GetFileName(reference.Replace('\\', '/'));
        if (string.Equals(fileName, assemblyFileName, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"Referenced by {assembly.name}: {reference}");
        }
    }
}
```

Compare the logged paths with the selected file. Registry packages may use a `Library/PackageCache`
physical path for an asset shown under `Packages` in the Project window. If the selected path is
empty, Unity did not select a matching assembly in the current compilation context.

Check the assembly that reports the error. If its asmdef enables **Override References**, it must
explicitly list the precompiled DLLs it uses; an auto-referenced plugin does not override that
assembly's explicit reference policy. Inspect the selected plugin's platform and define constraints
as well. Editor references do not establish player references for a different target.

If Unity selects another package's explicitly referenced copy, follow that package's supported
exclusion policy below. Do not add an extra DLL with the same name to fix a missing reference.

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
the AI Assistant's own assemblies then resolve against it; the winning assemblies must expose the APIs that each consumer requires.
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
Immutable **10.0.13**. The Unsafe importer disables the Editor platform; the Immutable importer
enables it. Check the actual winning assembly path and references in the consuming editor.
Importer settings alone do not identify an assembly already loaded into a running domain.

Do not add another Unsafe DLL under `Runtime/Binaries/`. Unity also supplies this name,
and adding another competing source can change assembly resolution. The linter's deliberate
absence rule applies to `Runtime/Binaries/`; it does not inventory the separate protobuf
dependency directory.

When reporting a conflict, include the Unity version, target, failing assembly, selected DLL path
and the relevant plugin/asmdef settings. Do not include credentials or unrelated project data.

## Bundled dependency versions

The four BCL assemblies under `Runtime/Binaries/` use official NuGet **10.0.13**
.NET Standard 2.0 assets. Their package source commit is
`4099f83977f4d29604a9aeb971fb0a7bc1d5f699`. DLL/XML provenance is recorded in the
[bundled BCL inventory](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/Runtime/Binaries/README.md).
The existing Unity-version constraints and importer GUIDs remain unchanged.
Newer Unity versions continue to use their own JSON, encodings and async-interface assemblies.

Protobuf-net **3.4.40** uses the package's AOT patch, Immutable **10.0.13** and
Unsafe **6.1.2**. These versions identify the bundled files; they do not override Unity's assembly
selection. On newer editors the selected JSON-related assemblies can come from Unity instead.

If you maintain the package's bundled assemblies, use the
[dependency rebuild instructions](../project/contributing.md#rebuild-bundled-serialization-dependencies).
Game projects do not need to rebuild the package or run its historical compatibility oracles to
resolve a duplicate-reference error.
