# Context CI and Testing

## Pushing costs a full CI matrix -- batch before you push

**Every push to the remote triggers the whole CI matrix**, including four Unity editor versions in
editmode, playmode and gated IL2CPP standalone. That is expensive and slow. Treat a push as a
deliberate act, not the tail of every commit.

- **Commit locally as often as is useful; push once**, when a coherent unit of work is verified.
  Small, focused commits are still right -- it is the _pushing_ that is costly, not the committing.
- **Exhaust the local gates first.** In rough order of cost, all of them cheaper than one CI run:
  - `npm run typecheck:unity` -- compiles the real `Runtime/**`, `Editor/**` and `Tests/**` against
    Unity reference assemblies with the shipped analyzers loaded, in seconds. Catches `CS####` and `WPROTO###`.
    **Those reference assemblies are `UnityEngine.Modules` 2021.3.33, older than every editor CI
    runs, and that is the only version the package has ever published** -- so the pin cannot be
    moved and the gate prints what it is on every compile. Member signatures are safe to check
    here; anything resolved out of Unity's own metadata (attribute targets, defaults, serialization
    behaviour) has to be confirmed in a real editor, because the failure mode is a confident answer
    for a Unity nobody ships ([#553](https://github.com/Ambiguous-Interactive/unity-helpers/issues/553)).
    It compiles several asmdefs into ONE assembly with one reference list, where Unity compiles each
    against its own `overrideReferences`, so it CAN be more permissive about **references**:
    `JsonEncodedText.Encode` needs `System.Text.Encodings.Web`, which `TestCheck` holds for
    `Runtime/**` and no test asmdef declares, and it failed Unity with 25 x `CS0012`.
    `npm run lint:typecheck-asmdef-references` holds that statically, and `typecheck:tests` ends
    with a `--probe` leg rebuilding without the Runtime-only references so such a fixture fails HERE
    ([#598](https://github.com/Ambiguous-Interactive/unity-helpers/issues/598)).
    It builds the `Runtime/`, `Editor/` and PlayMode test trees four ways (`typecheck:unity:*`,
    `typecheck:editor:*`, `typecheck:tests:*`), because four different branches ship: the `WALLSTOP_PROTO` default, the legacy
    define-off fallback, `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` (`:odin`) and `SINGLE_THREADED`.
    Both `SINGLE_THREADED` (#533) and Odin swap **declarations**, not just call sites --
    `ReflectionHelpers` moves five caches between `ConcurrentDictionary` and `Dictionary`, and Odin
    changes the base class of `RuntimeSingleton<T>`, `ScriptableObjectSingleton<T>` and
    `AttributeEffect` -- so a change without the matching branch passes every unguarded local gate
    and costs a matrix run. That branch compiled nowhere until #347, which is how #275 shipped a
    compile break. Odin is paid with no NuGet package, so each shim declares only the base classes
    the sources alias. `typecheck:editor` adds 215 of the 223 files under `Editor/`, its `:odin` leg
    the only thing that compiles the nine editor drawers and three inspectors (#347). **Its
    `UnityEditor` half is `Unity3D.SDK` 2021.1.14 -- two minor versions BELOW the 2021.3 floor, and
    the newest ever published** -- so a 2021.2/2021.3 member reads as absent: #553 one notch worse.
    Exclude such a file rather than "fixing" the source; the eight exclusions and their
    compile shims are enumerated in the csproj. **These exclusions still lack complete local API binding checks**. The editor build runs a
    separate WUH013 audit over ten excluded runtime/editor subjects, including the dictionary and set
    drawers, with an in-compilation reporting control; this certifies only counting-loop diagnostics.
    Other changes still require real Unity verification. Copy the check
    project, drop those two `<Compile Remove>` lines and build that: the only `CS####` it should
    report are six `CS0154` on `managedReferenceValue`, the 2021.1.14 gap the exclusions exist
    for. Session 251 shipped a `CS0103` in both and cost the whole eight-leg matrix.
    `typecheck:editor-tests` is the FOURTH tree, `Tests/Editor/**`, and the only gate that compiles it
    ([#616](https://github.com/Ambiguous-Interactive/unity-helpers/issues/616)); two ways, default and
    `:odin`. It inherits the editor pin and so EditorCheck's exclusions -- 41 of 655 files, one line
    with its reason each in the csproj.
    `typecheck:integrations` is the FIFTH tree: `Runtime/Integrations/**`, the 19 Reflex/VContainer/Zenject files EVERY other project named in an `Exclude`, so no `WUH###` rule ever ran there and four `??`-on-a-`ScriptableObject` sites shipped; no DI package is on nuget.org, so it takes the Odin route -- three shims declaring only what those 19 name -- and builds default, `:legacy-reflex` and `:player` ([#687](https://github.com/Ambiguous-Interactive/unity-helpers/issues/687)).
  - `dotnet test -c Release -p:ProtobufNetOracle=v3` and then
    `dotnet test -c Release -p:ProtobufNetOracle=v2` in
    `Generator~/WallstopStudios.UnityHelpers.Proto.Generator.Tests` -- the real serializer sources
    against protobuf-net 3.2.56 and 2.4.9 in isolated processes. **`-c Release` is what CI runs and
    what the throughput gate needs**: the oracle is a precompiled release assembly whatever the
    configuration says, so an unoptimized run would be comparing one implementation's debug build
    against another's release build. Those assertions are skipped, loudly, outside Release; the
    allocation gates are configuration-independent and run either way.
  - `npm run agent:preflight:fix` then `npm run agent:preflight`.
  - `.venv/bin/mkdocs build --strict` when the change touches `docs/**` or `mkdocs.yml`. ~30 s, the
    exact command the Validate Documentation job runs, and the only local check that catches a link
    leaving the docs tree or a heading anchor that slugs differently under MkDocs than under
    GitHub -- `lint:docs` and `lint:markdown` pass both. Reference workflow files as inline code.
  - Relevant targeted checks for the files changed; `npm run validate:local` is the explicit
    repository-wide lint and contract aggregate when that broader evidence is warranted.
  - `npm run validate:prepush` as the final fast Git/config safety check.
  - The Unity MCP bridge, which compiles your working tree in a real editor **and runs real
    fixtures against it**. `Unity_RunCommand` cannot _name_ a package type -- its sandbox
    assembly does not reference them, and `using System.Reflection;` is refused -- but fully
    qualified reflection reaches everything, including generic package types and their private
    members. A timeout is an expired session, retry once; a NEW `.cs` file DOES reach the pipeline
    (#656). See [unity-mcp-fixture-runner](../skills/unity-mcp-fixture-runner.md)
    for the loop and its traps ([#435](https://github.com/Ambiguous-Interactive/unity-helpers/issues/435)).
- **When a change spans both suites, update both before pushing.** A packed-encoding change in
  session 175 updated the `Generator~` differentials, missed the Unity golden vectors in
  `Tests/Runtime/Serialization/`, and cost a full matrix run to discover. Grep for the affected byte
  literals in `Tests/` as well as `Generator~/`.
- **Superseding your own run reds the previous SHA's Unity Tests entry, and a QUEUED matrix is no
  cheaper**: every leg of the old run fails `require-current-pr-head` as stale whether or not it
  started. `Unity CI Success` re-resolves the head last and is not fooled. Batch anyway.

## Test Execution

Run Unity tests directly via Docker-in-Docker:

1. Check license: `pwsh -NoProfile -File scripts/unity/setup-license.ps1 -Check`
   - If exit code 1: warn user to run `npm run unity:setup-license`, then reach for the MCP bridge rather than skipping Unity entirely -- it needs no license and runs EditMode fixtures against your working tree. Docker legs and PlayMode stay skipped; continue with relevant non-Unity checks
2. Compile: `bash scripts/unity/compile.sh`
   - If output contains `Machine bindings don't match` or `No valid Unity Editor license found`: license issue, not code issue. Warn user, skip Unity tests, continue with relevant non-Unity checks
   - If compilation fails for other reasons: fix the code
3. Run `bash scripts/unity/run-tests.sh` (EditMode) and `bash scripts/unity/run-tests.sh --mode playmode` (PlayMode). A `--filter` that matches nothing now fails rather than reporting a clean run
4. Parse test results and fix any failures before marking work complete
5. Always run the relevant targeted non-Unity checks and the fast `npm run validate:prepush` safety check regardless of Unity license availability

See [unity-devcontainer-testing](../skills/unity-devcontainer-testing.md) for targeted test filters and troubleshooting.
