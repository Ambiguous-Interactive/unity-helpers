# Session 182 — a real v2 oracle and a marker-safe configure retry

Branch: `agent/add-protobuf-v2-oracle`. Baseline: `main` at `f3b63b64`, package `3.5.1`.

## Why this was next

The paginated GitHub audit returned 39 open issues, zero open pull requests, and no draft,
in-progress, or dependency PR to continue. Gameplay, player, and persisted-save correctness put the
WallstopProto umbrella (#343) first. Its strongest bounded unmet promise was #371: the [implementation plan](../PLAN.md) claimed
protobuf-net v2 and v3 differential proof, but the tree contained only 3.2.56.

The complete priority order was:

1. Save/player serialization and common consumer shapes: #343, #371, #395, #280, #398, #399,
   #347, #388, #286, #397, #321, #289, #284, #288, #309, #383, #401, #386, #285, #281.
2. CI reliability, release assurance, and tooling: #393, #392, #411, #325, #415, #322, #323,
   #404, #400, #349, #378, #360, #340, #417, #346, #338.
3. Already fixed or superseded and suitable for issue closeout: #396, #406, #356.

The value-type adversarial sweep also found two real follow-ups, but neither displaced save
compatibility: `SerializableSetBase<T,TSet>` needs reference-null-safe handling and mutation-safe
struct access, while `MapMember` promises concrete struct dictionaries but emits reference-only
null/coalescing expressions. Those remain scoped work for #388 rather than being mixed into this
oracle change.

## The oracle that cannot impersonate itself

The official protobuf-net 2.4.9 `netstandard2.0` assembly is pinned under
`Generator~/ProtobufNetV2Oracle/`, an ignored-by-Unity directory that never enters the package
payload. Its README records the NuGet and DLL SHA-256 hashes, assembly identity, informational
version, source, and license.

Both oracle DLLs are named `protobuf-net`. A side-by-side alias inside one test process can be
collapsed by Unity or assembly resolution onto one physical binary and produce a convincing false
dual oracle. The test project instead selects one exact reference through `ProtobufNetOracle=v2|v3`,
uses version-specific output/intermediate directories, and CI starts two sequential `dotnet test`
processes. `OracleIdentityTests` fails unless each process loaded the expected assembly and
informational version.

The v2 run immediately disproved a PLAN assumption rather than merely adding another green badge:

- v2 omits default string map keys and values that v3 writes;
- v2 omits a default struct map value that v3 writes;
- v2 silently drops a null repeated element that v3 refuses;
- v2's runtime compiler cannot reliably prepare a contract containing a struct-valued map or a
  surrogated struct map value after the model freezes.

The shared domain remains byte-exact. Dedicated v2 fixtures pin both map encodings and cross-read
string maps in both directions. That test first failed because WallstopProto decoded v2's omitted
empty string map value as `null`; the generated reader now applies protobuf's `string.Empty` default.
A golden v2 struct-map payload proves the migration direction
WallstopProto needs: data written by v2 is accepted. A v2-compatible surrogate contract retains
scalar and repeated coverage without pretending the upstream v2 map compiler limitation is a
WallstopProto defect. WallstopProto keeps v3's explicit map defaults and null-element refusal because
silently dropping a collection element is data loss.

## CI improvement folded in

The main-branch audit found no repository-caused red check. PR #420's full Unity/IL2CPP matrix had
passed, while the merge commit's remaining `SINGLE_THREADED` jobs were still active with every
completed check green. Its history did expose #392's exact pre-test failure: the test invocation
already retried a known UPM cancellation, but the separate configure invocation did not.

`Invoke-UnityConfigurePass` now retries once only when both conditions hold: no fresh configure
marker exists, the log contains the existing exact cancellation/IPC signature, and no catastrophic
compile/startup signature follows it. A fresh marker is durable success even after a non-zero
shutdown. Before retry, the runner makes a best-effort copy of the first log and clears UPM state.
Ordinary and mixed UPM/compile failures remain fatal after one attempt. A data-driven,
cross-platform PowerShell test drives the shipping function without launching Unity and is gated by
Test Lint.

## Evidence

- protobuf-net 2.4.9 assembly version: `2.4.0.0`; informational version:
  `2.4.9.1+f4bacb1a94`.
- protobuf-net 3.2.56 assembly version: `3.0.0.0`; informational version:
  `3.2.56+dfdfce61a7`.
- The initial isolated v2 run found five failures in 300 tests; none was hidden. The corpus and
  documentation were corrected around the measured major-version differences.
- The final Release differential runs pass with no skips: 301/301 against protobuf-net 2.4.9 and
  300/300 against protobuf-net 3.2.56.
- The production generator rebuild succeeded with zero warnings and copied the exact analyzer payload
  consumed by Unity and the compile-only projects.
- `npm run typecheck:unity` passed the default runtime, legacy runtime, default test-assembly, and
  legacy test-assembly builds with zero warnings and errors.
- The configure retry behavioral suite covers marker precedence, two-attempt bounding, marker-path
  environment cleanup, best-effort first-log preservation, UPM cleanup, and pure or mixed
  compile-failure and case-varied native-fatal non-retry: 25/25 assertions pass. A final adversarial
  pass caught and removed a case-sensitivity mismatch between this retry gate and the canonical
  catastrophic-log scanner.
- The repository-wide CSharpier check passed across 1,699 files.
- `npm run validate:prepush` passed its complete repository content, contract-test, formatting,
  naming, hook, portability, devcontainer, and spelling chain.
- Exact baseline `main` SHA `f3b63b64` completed with 56 successful checks, 2 intentional skips,
  and no incomplete or failed checks, including the default-on Unity/IL2CPP proof from #420.
- The final independent adversarial re-review reported zero actionable findings after the
  case-insensitive catastrophic-pattern repair.
- The connected Unity MCP editor targets a different host project. A repository-root command timed
  out and was terminated, so MCP output is not counted as validation evidence for this checkout.
- Final formatter, repository contract, and pre-push evidence is recorded before publication; the
  remote matrix remains attached to the pull request itself.

## Next

After this dual-oracle PR is green, #395 is the next save-shape milestone. #388 should include both
the struct-backed serializable set repair and the newly demonstrated struct-map generator repair;
doing only the generic constraint would leave two copy/mutation defects behind.
