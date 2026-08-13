# Session 185 — WallstopProto startup evidence and benchmark contracts

Branch: `dev/codex/session-185-wproto-cold-start-ci-contracts`. Baseline: `main` at `654e0719`,
package `3.5.1`.

## Why this was next

The exhaustive GitHub audit found 34 open issues, no open or draft pull request, and no prior-session
branch that still needed completion. #343 remains the highest gameplay-impacting item: arbitrary
consumer save contracts need an AOT-safe serializer in shipped players. Sessions 184 and earlier had
closed the implementation and allocation portions of its release gate, but cold-start evidence was
still prose and the licensed Unity benchmark lane still repeated hosted discovery work on every
self-hosted leg.

This session therefore advances one coherent release-gate slice: make generated registration plus
first API use measurable in the real Unity performance assembly, then make the scheduled lane select
and retain those measurements without stale-result ambiguity.

## Controlled startup benchmark

`ProtoSerializationStartupPerformanceTests` runs nine rounds over 27 API-specific generic closures of
one representative contract, plus one warmup closure. The contract contains scalar, string, repeated,
map, and nested members. A distinct marker type is used for serialize, WallstopProto deserialize, and
protobuf-net deserialize in each round, and serializer order alternates by round to reduce order bias.

The generated registrar records its own elapsed time on its first invocation, before fixture code can
repeat it. Each WallstopProto sample adds that one-time eager-registration cost to an API-specific
closure's first use. Input preparation uses protobuf-net only for the WallstopProto-read closure and
WallstopProto only for the protobuf-net-read closure, preventing either timed implementation from
warming its own type model. That boundary makes the result reproducible and isolates serializer-owned
startup from process launch or Unity import time. It asserts every field of both restored object graphs
but carries no timing threshold that could flake on a shared runner.

The explicit closed-contract inventory is also a generator contract. An initial live run correctly
failed with `Served=false`: an open `StartupContract<TMarker>` declaration alone gives the generator
no closed type to register. Listing every benchmark closure through `typeof(StartupContract<Marker>)` made
all 28 generated registrations visible without runtime reflection or hand registration.

The configured Unity MCP refreshed the package in the Unity 6000.4.6f1 editor and invoked the test
fixture from `WallstopStudios.UnityHelpers.Tests.Runtime.Performance.dll`. The loaded assembly had
already been independently verified with debugging flags `2` and `DisableOptimizations = false`.
The optimized-editor medians were:

| Operation | WallstopProto | protobuf-net | Speedup |
| --- | ---: | ---: | ---: |
| One-time assembly registration + median first serialize | 4,243.2 us | 7,575.2 us | 1.79x |
| One-time assembly registration + median first deserialize | 4,325.2 us | 7,522.6 us | 1.74x |

## Benchmark CI contracts

- The hosted `matrix-config` job now resolves the exact benchmark profiles once with the shared
  asmdef-discovery module. EditMode receives Performance plus Random; PlayMode receives Performance.
  Each Unity-version entry carries `assemblies` and its exact result identity, so licensed self-hosted
  legs no longer install Node or repeat repository discovery.
- Result assembly validates downloads in a temporary directory before touching canonical artifacts.
  Only a successful canonical full matrix with the exact expected identities, metrics in every result,
  and no removed baseline keys can replace the report or advance `baseline.json`. Pinned and failed
  XML is retained under `partial-results-*` without replacing the last complete report; a successful
  canonical run with missing identities or metrics fails closed and uploads those diagnostics even
  though later commit steps are skipped.
- `export-unitypackage.sh` passes `-releaseCodeOptimization`. Export smoke now compiles the same
  optimized release payload assumed by the performance acceptance work.
- The workflow contract pins centralized benchmark discovery, absence of per-leg discovery, fresh
  complete-matrix baseline updates, non-empty metrics, and the exporter optimization flag.
- Stale Unity workflow comments that still claimed one permanently serialized organization seat
  now match the measured evidence: tier handoff is about one second, while a hang can strand a runner
  and delay queued licensed work.

## Local validation

- CSharpier: clean for the new C# fixture.
- Default test typecheck: build succeeded with zero warnings and zero errors.
- Test lint: no issues in the new fixture (72 pre-existing repository-wide performance advisories).
- Unity workflow matrix contract: passed.
- Perf-tool policy self-tests: six matrix completeness cases plus removed-baseline-key refusal passed.
- `actionlint`: passed for both touched workflows.
- `shellcheck`: passed for the unitypackage exporter.
- Prettier: passed for the workflows and plan.
- Unity MCP: all generated closures served; both round trips passed; optimized-editor results above.

The licensed EditMode/PlayMode workflow remains the branch-level verification before merge. During
local validation, the supposedly reduced `validate:prepush` still exceeded several minutes because
it expanded into every repository-wide content check and the 39-suite `validate:tests:fast` bundle.
The pre-push command now runs only the roughly one-second Git/config safety check. The former
complete local aggregate remains available as `validate:local`, while Local Gates retains the full
behavioral suite. Five consecutive npm-wrapper measurements completed in 0.787-1.441 seconds. Five
actual new-branch pre-push hook runs, including the full pushed C# tree guard, completed in
0.804-1.500 seconds against the explicit 30-second ceiling.

The final changed-file preflight found one separate staging defect: because `progress/` is ignored,
including its intentionally force-added session log in a formatter's bulk `git add` caused Git to
reject the entire restage. Auto-fixes now use `git add --update` for paths already present in the
index and retain the ordinary explicit-add path for genuinely new `.meta` companions. A focused
ignored-staged-log regression plus all existing preflight cases pass (143/143).

## Remaining release gate

This evidence does not authorize removing protobuf-net. WallstopProto is still Unreleased in package
`3.5.1`, and the rollout plan requires a hybrid release before deleting the runtime fallback. The
branch's licensed Release benchmark supplies EditMode/PlayMode registration-inclusive first-use and warm
evidence. The serializer-owned evidence deliberately excludes Unity import and shared JIT work. Once
the licensed lane is green, the next release action is the hybrid package, followed by fallback
removal in a later release.
