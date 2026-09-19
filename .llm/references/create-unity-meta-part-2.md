# create-unity-meta - Part 2

## Split Content

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
Unity ignores such a directory at any depth. `Generator~` and `scripts/random-quality/testu01~` are examples.

`Samples~` is different: Package Manager copies its sample children into `Assets`, and the
package exporter also imports them. Commit `.meta` files for those children so references keep
their GUIDs. Only the `Samples~` container itself is exempt; a nested tooling directory ending
in `~` remains ignored. The pre-commit hook, agent preflight, and metadata linter must agree.

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
