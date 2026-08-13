# Session 184 — WallstopProto performance evidence and smaller Unity CI

Branch: `dev/codex/session-184-wproto-allocation-baseline`. Baseline: `main` at `a1bf3195`, package `3.5.1`.

## Why this was next

The WallstopProto implementation and package-contract port are on `main`, but the plan's release
acceptance still lacked allocation evidence and the existing Unity serialization benchmark did not
exercise WallstopProto. Its benchmark-only message types carried `[ProtoContract]` without
`[WProtoContract]`, so the facade correctly declined them and both columns measured protobuf-net.

The CI audit also re-measured the proposed “group by editor” rewrite against successful run
`31662793317`. Work outside `Run Unity Test Runner` was only 27–42 seconds per leg. Grouping every
mode would therefore save roughly four to five runner-minutes of wrapper work while changing job
identities, the fast-before-IL2CPP failure gate, artifact handling, and license cleanup evidence.
That rewrite remains a separately measured decision rather than being folded into this session.
The measurements and recommendation are recorded on issue #417 in comment `5276501928`.

## Performance evidence

`AllocationBenchmarkTests` runs the real generated runtime source over a representative contract
containing scalar, string, repeated, map, and nested members. It warms both serializers, reuses the
destination buffers, measures allocations with `GC.GetAllocatedBytesForCurrentThread`, records
throughput with `Stopwatch.GetTimestamp`, and runs in the existing Release generator job against
both protobuf-net 2.4.9 and 3.2.56.

The local Release baseline against protobuf-net 3.2.56 was:

| operation | WallstopProto | protobuf-net |
| --- | ---: | ---: |
| serialize | 0 B/op | 40 B/op |
| deserialize | 5,272 B/op | 4,112 B/op |

The write result is now a hard zero-allocation regression contract. The read ceiling records the
current cost of materializing the returned strings, array, dictionary, and nested object. The v3
lane also requires both warmed operations to be at least as fast as protobuf-net over 10,000
operations; the isolated v2 lane remains interoperability evidence rather than a v3 performance
claim.

The Unity `ProtoSerializationPerformanceTests` contracts are now dual-annotated. Its WallstopProto
column goes through the facade and its comparison column calls protobuf-net directly. The weekly
Release Unity lane can now supply the remaining editor/player throughput evidence; cold-start
measurement is still open.

The configured Unity MCP resolved the expected `D:/Code/Packages` project and an idle Unity
6000.4.6f1 editor. Runtime inspection found the loaded package assembly at
`Library/ScriptAssemblies/WallstopStudios.UnityHelpers.dll` with debugging flags `2` and
`DisableOptimizations = false`, independently confirming the editor loaded an optimized assembly.

## CI changes

- The weekly EditMode performance and thorough-Random suites now share one scoped Unity invocation.
  The explicit Performance+Random assembly list makes adding the `Fast` category safe, while the
  existing result verification and artifact upload now cover both suites. The combined pass has a
  110-minute editor watchdog, retaining ten minutes for cleanup under its 120-minute step limit.
- Self-hosted Unity test checkouts no longer request Git LFS; the repository has no LFS attributes or
  pointer files.
- The hosted matrix job now resolves each distinct asmdef profile once and carries `assemblies` plus
  `is-empty` in every fast, standalone, and SINGLE_THREADED matrix entry. Licensed self-hosted test
  legs no longer install Node or repeat asmdef discovery per Unity version. Direct module evidence
  found 33/6/6 integration assemblies for EditMode/PlayMode/standalone. The SINGLE_THREADED pair is
  disjoint: EditMode runs 24 editor-only assemblies (retaining the editor scheduling/race coverage)
  and PlayMode runs the three platform-neutral runtime/core assemblies. The targeted workflow
  contract pins this topology.
- Ephemeral Unity projects no longer install `com.unity.test-framework.performance`; every benchmark
  uses raw `Stopwatch` measurement and no asmdef references `Unity.PerformanceTesting`.
- Test Lint no longer repeats `lint:tests` and sync-script contracts already run by Local Gates. Its
  unique linter, watchdog, reporter, activation, configure, and catastrophic-pattern tests remain.
- Workflow contract coverage pins the single benchmark invocation and the removed dependencies.
- Developer `validate:prepush` now uses `validate:tests:fast`. The 42-repository/46-PowerShell
  agent-preflight fixture harness plus the pre-commit and pre-push integration harnesses moved to
  `validate:tests:hook-regressions`; the full `validate:tests` aggregate still runs both subsets in
  Local Gates. Four contract assertions prevent either CI coverage loss or local re-expansion.

Issue #425's reported multi-minute delay came from these exhaustive tests of the hooks, not the
hooks themselves. The actual pre-commit and pre-push entry points were already changed-file scoped.
The targeted sync-contract suite passed 153/153 after the split.

## Issue triage

Repository and merged-PR evidence closed three stale completed issues: #396 (`validate:tests` in
Local Gates) and #406 (rename-safe workflow guards) were completed by PR #409; #404 (the dual-mode
PlayMode/Core test compile gate) was completed by PR #413. The older one-line UPM changelog issue
#340 was closed as a duplicate of the newer, fuller #421. Each closure includes the implementing
files and merged PR so the external backlog remains authoritative.

Issue #398 now records the candidate's allocation numbers and stays open for read-path work plus
licensed Unity throughput/cold-start evidence. Issue #415 records that current `main` run
`31665440545` repeated the pre-checkout runner-setup failure across all eight fast legs; the update
deliberately does not claim the exact mutex exception because connector step evidence does not expose
the authenticated log text.

## Remaining release gate

This is performance acceptance evidence, not authorization to remove protobuf-net. The package is
still `3.5.1`, WallstopProto is only in the Unreleased changelog, and the rollout plan requires one
hybrid release before deleting the fallback DLLs. The next evidence is a licensed Release Unity run
for warm throughput plus a cold-start comparison, followed by that hybrid release.
