# create-unity-meta - Part 1

## Split Content

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

| Category             | Examples                                                                    | Why Excluded                          |
| -------------------- | --------------------------------------------------------------------------- | ------------------------------------- |
| Tooling cache dirs   | `.pytest_cache`, `__pycache__`, `.mypy_cache`, `node_modules`, `obj`, `bin` | Generated artifacts, not Unity assets |
| OS metadata files    | `.DS_Store`, `Thumbs.db`                                                    | OS-specific, not Unity assets         |
| Git placeholders     | `.gitkeep`                                                                  | Convention file, not a Unity asset    |
| Compiled bytecode    | `*.pyc`, `*.pyo`                                                            | Build artifacts                       |
| Editor temp files    | `*.swp`, `*.swo`, `*.tmp`                                                   | Transient editor files                |
| Lock files           | `package-lock.json`, `Gemfile.lock`                                         | Dependency lock files                 |
| Ignored tooling dirs | `Generator~`, `scripts/tools~`                                              | These assets are never imported       |

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
