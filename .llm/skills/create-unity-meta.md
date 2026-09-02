# Skill: Create Unity Meta File

<!-- trigger: meta, unity, asset, guid, file, folder | After creating ANY new file or folder | Core -->

**Trigger**: **MANDATORY** — After creating ANY new file or folder in the Unity package.

> ⚠️ **CRITICAL**: This skill is NOT optional. Every file and folder you create MUST have a corresponding `.meta` file generated immediately. Failure to generate meta files breaks Unity asset references and causes build failures.

---

## Why Meta Files Are Required

Unity requires a corresponding `.meta` file for every asset. Missing `.meta` files cause:

- Unity generating new ones with different GUIDs (breaks all references)
- Broken prefab, scene, and script references
- Lost inspector settings and serialized data
- Build failures and runtime errors

**This is a blocking requirement** — do not proceed with other tasks until meta files are generated.

---

## Exception: Dot Folders (Hidden Folders)

**Do NOT generate `.meta` files** for any folder or file inside a folder whose name starts with `.` (dot/hidden folders). Unity automatically ignores all dot folders, so meta files are unnecessary and would clutter the repository.

Common dot folders in this repository:

- `.llm/` — LLM agent instructions and skills
- `.github/` — GitHub workflows and configuration
- `.git/` — Git repository data
- `.vscode/` — VS Code workspace settings

**General rule**: If the path contains `/.` (a folder component starting with a dot), do NOT generate a meta file.

---

## Command

```bash
./scripts/generate-meta.sh <path-to-file-or-folder>
```

---

## Examples

```bash
# For a new C# script
./scripts/generate-meta.sh Runtime/Core/NewFeature/MyNewClass.cs

# For a new folder (create parent folders' meta files first)
./scripts/generate-meta.sh Runtime/Core/NewFeature

# For documentation
./scripts/generate-meta.sh docs/features/new-feature.md

# For assembly definitions
./scripts/generate-meta.sh Runtime/NewAssembly.asmdef

# For shaders
./scripts/generate-meta.sh Shaders/NewShader.shader

# For UI Toolkit files
./scripts/generate-meta.sh Editor/Styles/NewStyle.uss
```

---

## When to Generate

Generate a `.meta` file whenever you create:

| File Type                         | Importer Used                       |
| --------------------------------- | ----------------------------------- |
| `.cs`                             | MonoImporter                        |
| `.asmdef`                         | AssemblyDefinitionImporter          |
| `.asmref`                         | AssemblyDefinitionReferenceImporter |
| `.shader`                         | ShaderImporter                      |
| `.compute`                        | ComputeShaderImporter               |
| `.shadergraph`, `.shadersubgraph` | ScriptedImporter                    |
| `.uss`, `.uxml`                   | UI Toolkit importers                |
| `.mat`                            | NativeFormatImporter                |
| `.asset`                          | NativeFormatImporter                |
| `.prefab`                         | PrefabImporter                      |
| `.unity`                          | DefaultImporter                     |
| `.png`, `.jpg`, `.tga`, etc.      | TextureImporter                     |
| `.wav`, `.mp3`, `.ogg`, etc.      | AudioImporter                       |
| `.fbx`, `.obj`, `.dae`, etc.      | ModelImporter                       |
| `.ttf`, `.otf`                    | TrueTypeFontImporter                |
| `.md`, `.txt`, `.json`, `.xml`    | TextScriptImporter                  |
| `package.json`                    | PackageManifestImporter             |
| directories                       | DefaultImporter (folderAsset)       |

---

## Files and Directories That Do NOT Need Meta Files

Beyond dot folders (covered above), certain tooling artifacts, OS metadata, and editor temp files must be excluded from meta file requirements. These are configured in the `$excludeDirs`, `$excludeFilePatterns`, and `$excludeDirPatterns` arrays in [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1).

| Category           | Examples                                                                    | Why Excluded                          |
| ------------------ | --------------------------------------------------------------------------- | ------------------------------------- |
| Tooling cache dirs | `.pytest_cache`, `__pycache__`, `.mypy_cache`, `node_modules`, `obj`, `bin` | Generated artifacts, not Unity assets |
| OS metadata files  | `.DS_Store`, `Thumbs.db`                                                    | OS-specific, not Unity assets         |
| Git placeholders   | `.gitkeep`                                                                  | Convention file, not a Unity asset    |
| Compiled bytecode  | `*.pyc`, `*.pyo`                                                            | Build artifacts                       |
| Editor temp files  | `*.swp`, `*.swo`, `*.tmp`                                                   | Transient editor files                |
| Lock files         | `package-lock.json`, `Gemfile.lock`                                         | Dependency lock files                 |
| Unity sample dirs  | `Samples~`                                                                  | Unity ignores `~` suffix folders      |

**When adding new tooling** (Python tools, linters, build systems) that creates cache or artifact directories inside scanned source roots (`Runtime/`, `Editor/`, `Tests/`, `docs/`, `scripts/`, etc.), you **must** add exclusions to [lint-meta-files.ps1](../../scripts/lint-meta-files.ps1) and update the corresponding tests at [test-lint-meta-exclusions.sh](../../scripts/tests/test-lint-meta-exclusions.sh).

---

## Important Rules

1. **Never skip meta file generation** — Every file and folder needs one. This is mandatory, not optional.
2. **Generate immediately** — Run the script right after creating the file/folder, before any other tasks
3. **Generate in creation order** — Parent folders before children
4. **Use the script** — Don't manually create meta files (proper GUIDs and importer settings)
5. **Don't modify existing meta files** — Changing GUIDs breaks references
6. **Verify generation** — Confirm the `.meta` file was created successfully
7. **Include scripts** — Files in `scripts/` (`.sh`, `.ps1`, `.py`) also need `.meta` files. This is a common oversight that causes CI failures.
8. **A meta that exists is not a meta that works.** See below — this one cost a full CI matrix.

---

## An editor-assembly MonoBehaviour cannot be `AddComponent`'d -- and a missing file HIDES that

Measured across two CI matrices in session 244, because the first diagnosis was wrong and shipped.

**Unity refuses to add a MonoBehaviour it can identify as an editor script** -- one whose script
lives in an Editor-only assembly:

```text
Can't add script behaviour 'RegularMonoBehaviour' because it is an editor script.
```

The catch is the word _identify_. Unity classifies by the `MonoScript`, and a `MonoScript` binds by
FILE NAME -- so a type nested in a fixture, or sharing a file with one, has no `MonoScript` and
**escapes the policy**: `AddComponent` works. Twelve tests relied on that loophole without knowing
it. The one-type-per-file sweep (#666) gave each double a correctly-named file, Unity could finally
classify them, and all twelve went red on every editor version -- under the even less helpful
variant `Can't add script behaviour while compiling`, with nothing compiling.

**So an `AddComponent`-able test double belongs in a runtime-capable test assembly** (`Tests.Core`,
`Tests/Runtime/**` -- 167 doubles there add fine), never in an Editor-only one. A MonoBehaviour
that stays in an editor assembly is either never added to a GameObject, or exists to test the
refusal itself, as `RegularMonoBehaviour` does.

`npm run lint:editor-assembly-monobehaviours` enforces this, statically, in Repo Lint: it resolves
each `.cs` to its owning `.asmdef`, and reports every concrete MonoBehaviour whose assembly sets
`includePlatforms: ["Editor"]`. A nested type, or one sharing a file, is reported too and told it
has no `MonoScript` -- it escapes Unity's policy only until somebody gives it a correctly-named
file. The 14 that legitimately live there are listed in `EDITOR_ASSEMBLY_BY_DESIGN` with a reason
and a FROZEN `AddComponent` site count, so the first new call site reds the entry rather than
hiding behind it ([#678](https://github.com/Ambiguous-Interactive/unity-helpers/issues/678)).

Ruled out with measurements, in order of how convincing each looked: the Unity-written stub meta
(repairing 147 of them changed nothing -- see below), duplicate GUIDs (zero repo-wide), a missing
trailing newline (371 tracked metas lack one, most working).

**The stub meta is still wrong, just not for this reason.** Unity auto-writes
`fileFormatVersion` + `guid` and nothing else the moment it notices a new script; the committed
convention is the full `MonoImporter:` block (246 of 251 editor test metas carry it), which
`./scripts/generate-meta.sh` emits and the stub omits. Generate with the script, and check:

```bash
for m in <new .cs.meta files>; do grep -q "^MonoImporter:" "$m" || echo "MISSING BLOCK: $m"; done
```

------------------------------ | -------------------: |
| Pre-existing editor test metas | 246/251 and 98/118 |
| The stubs Unity wrote | **2/148** |

12 tests failed, identically on all four editor versions. Everything cheap said the code was fine:
`isCompiling=False`, the type compiled and loaded, `LoadAssetAtPath` returned the `MonoScript` and
`GetClass()` the right type. Only `AddComponent` disagreed. The 22 shipped MonoBehaviours that also
lack the block are all in **Runtime** assemblies, which add fine, so the defect only shows where an
Editor assembly meets a stub.

**So generate every meta with `./scripts/generate-meta.sh` and never let Unity's stub be the
committed one.** The script emits the block; check it after generating:

```bash
for m in <new .cs.meta files>; do grep -q "^MonoImporter:" "$m" || echo "MISSING BLOCK: $m"; done
```

Two things that look like the cause and are not, both ruled out by measurement: duplicate GUIDs
(zero repo-wide) and a missing trailing newline (371 tracked metas lack one, most of them working).

---

## Workflow for New Feature

```bash
# 1. Create folder structure
mkdir -p Runtime/Core/NewFeature

# 2. Generate meta for folder IMMEDIATELY
./scripts/generate-meta.sh Runtime/Core/NewFeature

# 3. Create the file (via create_file tool or editor)

# 4. Generate meta for file IMMEDIATELY
./scripts/generate-meta.sh Runtime/Core/NewFeature/MyClass.cs

# 5. Format code
dotnet tool run csharpier format .
```

---

## Files Unity Must Not See At All

A `.meta` says "Unity, track this asset". Some files need the opposite, and there is exactly one way
to say it: **put them in a directory whose name ends with `~`, and give that directory no `.meta`.**
Unity ignores such a directory at any depth. `Samples~` and `Generator~` are the existing ones.

This matters most for **native source**. A `.c`, `.cpp` or `.h` anywhere in this repository is
native plugin source as far as Unity is concerned -- the repository IS the package, so there is no
`Assets/` boundary to hide behind. Unity hands the file to IL2CPP, the generated C++ fails to find
its includes, `GameAssembly.dll` is never produced, and every gated standalone leg fails with:

```text
Editor build produced invalid unity-helpers test player output at ...\UhTestPlayer.exe
(missing GameAssembly.dll (IL2CPP native compile/link did not complete); build exit code 3)
```

which names the player, not the file that broke it. The real cause is further up the log:

```text
il2cppOutput\cpp\<yours>.c(19): fatal error C1083: Cannot open include file: '<yours>.h'
```

`.cs` is exempt for a different reason -- a script outside any asmdef is skipped with a warning --
so C# in `scripts/` is fine and native source in `scripts/` is not. Measured: all four gated
standalone legs, twice, for one 50-line TestU01 driver.

Both meta gates know this rule: `$excludeDirPatterns` in `scripts/lint-meta-files.ps1` is its source
of truth, and `Test-MetaRequiredPath` in `scripts/agent-preflight.ps1` mirrors it. They drifted once.

---

## Checklist Before Proceeding

After creating any file or folder, verify:

- [ ] Meta file exists: `ls -la <path>.meta`
- [ ] Meta file is not empty and contains valid GUID
- [ ] Parent folder meta files also exist
- [ ] `npm run agent:preflight:fix` passes before task completion
