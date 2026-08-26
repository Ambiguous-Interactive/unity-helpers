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
