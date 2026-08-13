# Implementation Plan

> Retconned. Sections for already-implemented work (the BatchedEditorTestBase migration,
> ForceUpdate fix, SpriteSheetExtractor discovery, parallel-pixel, shared-fixture infra from
> sessions 134-156) have been removed from the active plan and condensed into
> [Foundation already in place](#foundation-already-in-place). Released work is summarized in
> [Shipped and Retired](#shipped-and-retired); older historical sections keep their original
> branch/run context where relevant. Baseline: `main` at `f3b63b64`, package `3.5.1`.

## Contents

- [Shipped and Retired](#shipped-and-retired) — what came out of the plan and why
- [Test Suite Runtime Reduction](#test-suite-runtime-reduction--goal-met-section-retired) — **GOAL MET** (all 14 legs under 5.3 min; retired)
- [CI Throughput](#ci-throughput) — **MEASURED OUT, nothing in-repo left** (#279 / #326 / #329 / #330 / #337 / #353 / #363 all closed; the only remaining lever is a third self-hosted runner, which is an org capacity decision)
- [Design Item: In-tree v2/v3-wire-compatible AOT-native serializer (WallstopProto)](#design-item-in-tree-v2v3-wire-compatible-aot-native-serializer-wallstopproto) — **HIGH PRIORITY, IN PROGRESS** (steps 1-5 and the package-contract port landed through session 181 / #420, whose full PR matrix passed. Session 182 added the isolated protobuf-net 2.4.9 oracle promised by #371; session 183 closed the collection matrix (#395) and the value-type sweep (#388). Performance acceptance and removal of the runtime protobuf-net fallback remain.)
- [Backlog: Auto-Loading Cache Feature](#backlog-auto-loading-cache-feature) (not started)
- [DxKit Rebrand: Unity Helpers → DxKit](#dxkit-rebrand-unity-helpers--dxkit) — **NOT STARTED** (brand, docs site, editor UI, assets)

---

## Shipped and Retired

Sections retired after verification against the published artifacts, so the active plan below is
only open work.

- **Release Reliability / 3.4.0 publish recovery** — closed. npm now lists `3.3.0`, `3.4.0`, `3.5.0`,
  `3.5.1`; git tags and GitHub releases match. The exporter bug, the redaction of Unity licensing
  material in CI logs, and the `unitypackage-smoke` gate that catches `Samples~ -> Samples` compile
  breaks all landed and have survived two releases since.
- **#333 — the MCP bridge resolved the wrong Unity project, and nothing ever asked.** An endpoint is
  a host:port; a bridge is bound to one editor. The old bash/PowerShell probe asked only "does
  something here speak MCP", which the wrong editor answers truthfully, and the default endpoint was
  one developer's LAN address hard-coded in two scripts. Replaced with the siblings' Node entry point
  (`npm run unity:mcp:{bridge,configure,probe}`) plus the layer none of DxMessaging / IshoBoy /
  DoxReloaded / qora-redux has: `GetProjectRoot` after the handshake, pinned into `.env.local` by
  `configure`, hard failure on mismatch. This repo now owns port **9007** (9003 DxMessaging, 9004
  IshoBoy, 9010 DoxReloaded, 9020 qora-redux) and `UNH-MCP-PORT` pins it. **Watch after merge:** the
  reference script shipped with qora's port and a server key that would have renamed every `Unity_*`
  tool — re-check both if it is ever re-synced from upstream.
- **Gameplay issue backlog, session 164.** Six issues were open; four were already fixed on `main`
  and nobody had closed them. #282 (NativePcgRandom), #318 (thread-pool drain), and #327 (batchmode
  reflection crash) were verified against the shipped code and tests and closed with evidence.
  #314 and #339 were each **half** fixed — the detection without the remedy, and the runtime without
  the drawers — and both halves ship in PR #344 along with #342's watchdog recovery. The lesson worth
  keeping: a CHANGELOG entry that is *accurate* about what landed
  ("enums with negative values were **slow** to name") can still leave the issue's actual severity
  ("Critical", the Inspector throws) untouched. Re-read the issue, not the commit.
- **#350 / #345 / #331 + the spatial-tree "flake", session 165.** Four fixes, three lessons.

  *A guard in the wrong place is not a guard.* `#if` **inside** a logger body empties the body and leaves the call — so a
  release build still evaluated `Singleton.Instance` (creating it) and still built a
  `FormattableString` per call; `[Conditional]` on the method removes the call site instead. A pinned
  SHA **restated** in a test is not a pin — the `uses:` line is, and restating it only guaranteed
  that every Dependabot bump desynchronized eight copies, loudly in one and silently in the policy
  checkout. Both were replaced by deriving the thing from where it actually lives. The reusable
  technique: `[Conditional]` resolves at the **call site's file position**, so a file-scoped `#undef`
  reproduces a release build inside an editor test — paired with a control fixture, that is the only
  way to test stripping from inside a test run.

  *A rare-looking test failure can be a common production bug wearing a disguise.* The one red Unity
  leg looked like an octree boundary flake — 999 of 1000, unseeded random, passing on every other
  version. It was neither the octree nor a flake: the generator validated the **offset** and stored
  `center + offset`, and that addition rounds. The same defect sat in the **public**
  `Helpers.GetRandomPointInCircle`/`InSphere`, where it is not rare at all — at world coordinate 1e6
  with radius 0.05, **half** the returned points are outside the circle they were sampled from. Rule
  worth keeping: when a random test fails at a boundary, check whether the *generator's* invariant
  and the *system's* predicate are computed over the same values. And be suspicious of a passing
  assertion that carries an epsilon — the four `radius + 1e-5f` tolerances are what hid this for as
  long as they existed.

  *An issue closed in the CHANGELOG can still be open in substance* — second session running. #331
  asked for three things and had received one; the missing two were the recorded decision on
  `System.Runtime.CompilerServices.Unsafe` and the contract test that stops a NuGet refresh silently
  reintroducing the assembly conflict. Re-read the issue's asks as a list and find each one in the
  code, rather than reading the commit that claimed it.
- **#319 — `DurableFile`, session 166.** `Serializer`'s five JSON write paths truncated the
  destination before producing output, so an interrupted save destroyed the previous one. Now staged
  and swapped. Two defects in the implementation the issue offered, both found by *running* the
  fixture: .NET's `FileMode.Append` is a seek-to-end at open, **not** `O_APPEND` (four threads × 50
  records kept 155 of 200 — the concurrency guarantee the issue relied on does not exist on .NET), and
  a failed staging attempt deleted a staged file another writer owned. Both fixed; the fixture is
  22/22 under a `dotnet` harness compiling the real sources, and a mutation to naive
  `File.WriteAllText` fails the durability test. Lesson worth keeping: when an upstream contribution
  states an OS-level guarantee, the test for *that* guarantee is the first one to run.
- **`SemaphoreLease`, session 166.** `SemaphoreSlim` waits are now `using (sem.Acquire())` /
  `using (await sem.AcquireAsync(ct))` / `TryAcquire(timeout, out lease)` via
  `Runtime/Core/Threading/SemaphoreLease.cs`. Disposal is tracked: disposing twice returns one
  permit, because a stray `Release()` raises the count above the semaphore maximum and admits a
  second caller to a one-at-a-time section. **Superseded in session 169** — it is now a
  `readonly struct` carrying a `DisposalLease`, so copying it is safe and the "a copy cannot be
  tracked" caveat is gone. See below.
- **#358 — every struct-based `IDisposable` is now disposed exactly once, session 169.** A struct
  that tracks disposal in its own field cannot survive being copied: the copy carries its own copy
  of the flag. Proven as data corruption, not theory — a test where a second renter's write lands
  in the first renter's list. The remedy is `DisposalLease`, a `(slot, generation)` handle held **by
  value** with the generation in a table outside the struct, so every copy reads the same one and
  only the copy holding the current generation claims. This is the slotmap pattern (DOTS
  `Entity{Index, Version}`, Rust `slotmap`, Vulkan handles).

  *Three lessons worth keeping.* **A "is it already back in the pool?" check is not enough** — the
  dangerous duplicate arrives *after* the instance was rented again, when it is not in the pool, and
  the release callback then clears a buffer its current renter is using. **A rented-instance set
  would trade the bug for a leak**, turning a forgotten `using` from "the GC takes it" into a
  permanent retention — worse than the defect. **Measure allocation before claiming zero**: the
  first attempt (a heap lease per pooled instance) was 0 B on the pooled hot path but 48 B on the
  public constructor and 80 B on the disposed-pool path, and only measurement caught it. The
  shipped design is 0 B on every path, with the residual 32 B on a disposed pool proven to be the
  produced object itself (0 B with a non-allocating producer; a bare `new List<int>()` is 32 B).

  *And two test-shaped lessons.* Four `AssetDatabaseBatchScopeTests` asserted the defect **as the
  contract** — a double disposal of one scope taking two scopes off the batch depth, ending a
  still-live scope's batch. A suite can encode a bug as intended behavior, complete with a comment
  explaining why it is fine ("protection against negative"). Read what an assertion *asserts*, not
  what its message says it does.

  **"Concurrent" is not "contended", and a `Barrier` is a de-synchronizer.** The first race test
  passed against a compare-and-swap mutated into a plain read/compare/write. It claimed both copies
  on one thread, and the test that did use threads spread their wake-ups with `Barrier.SignalAndWait`
  so a two-instruction window never overlapped. Racers must spin on a release flag, and winners must
  be summed across thousands of trials rather than asserted per trial — then the mutation fails at
  3054 winners for 3000 leases. A concurrency test that has never been run against a broken
  implementation is not evidence.
- **#367 — the array pools charged 32 bytes for every rent, session 170.** `WallstopArrayPool<T>`
  and `WallstopFastArrayPool<T>` stored idle arrays in a `ConcurrentStack`, which holds each item in a
  freshly allocated node. Measured 32.00 B/cycle on both against 0.00 for `WallstopGenericPool` over
  the same window — a pool charging the caller the allocation they reached for a pool to avoid, on
  paths three PRNGs and `IListExtensions` use.

  *The lock was measured, not assumed.* Against `ConcurrentStack`, an array-backed `Stack` behind a
  monitor is 18% slower on one thread (32.9 vs 40.0 Mops/s) and **1.9x faster on four contending for
  one bucket** (25.4 vs 13.6), while removing ~1.3 GB/s of garbage at that rate. A correct lock-free
  array stack needs per-slot claiming and a bounded capacity; the naive `Interlocked` index has a
  window where a popper reads a slot the pusher has not written. Lesson worth keeping: "lock-free"
  is not automatically the fast choice — under contention a two-instruction critical section behind a
  monitor beat the CAS retry loop, and the allocation was the whole reason the pool existed.

  *Two things fell out of sharing the storage.* The `SINGLE_THREADED` and threaded bodies of both
  pools collapsed into one implementation each (net −71 lines despite a new file), and
  `WallstopFastArrayPool.Release` stopped indexing a `List<T>` with no lock while a concurrent `Get`
  could be growing it — safe on x86 by accident of `List<T>`'s layout, unsound on every ARM player,
  because nothing ordered the publication of a resized backing array against its contents.

- **Inspector indent handling was not exception-safe, session 170.** Nineteen hand-written
  `EditorGUI.indentLevel` push/pop pairs across thirteen files, none with a `try`/`finally`. The
  point worth keeping: **an IMGUI body throwing is ordinary, not exceptional** — Unity unwinds a
  drawer with `GUIUtility.ExitGUI()` every time a control opens an object picker — so a bare
  `indentLevel++` … `indentLevel--` leaks on a normal interaction, not just on a bug. All nineteen
  now route through `Editor/Utils/IndentLevelScope.cs`, a `readonly struct` scope carrying a
  `DisposalLease` (Unity's own `EditorGUI.IndentLevelScope` is a class and allocates every repaint).
  Two techniques worth reusing: **restore the saved level rather than decrementing**, which heals a
  nested drawer that leaked one of its own; and **`using` declarations**, which let a nineteen-site
  sweep land without re-indenting a single existing block.

- **CI Failure Remediation: Standalone IL2CPP + PlayMode** — closed as a gate. Every Unity leg has been
  green since PR `272`. Its one still-open thread, the protobuf-net AOT failure under IL2CPP, is the
  subject of the WallstopProto design item below; the rest was root-cause history for fixes that have
  shipped. Recover it from git history if a regression needs the original notes.

---

## CI Throughput

Re-measured on Unity Tests run `31130259492` (`main` at `114bdab8`, green, 2026-08-06), superseding
the older numbers from `30227874347`. Numbers first, because the intuition about where CI time goes
turned out to be wrong a second time.

**Wall clock 88 min. Sum of job durations 70.9 min. Two self-hosted runners.** The packing floor for
16 Unity jobs is therefore **35.5 min**, so 52.5 min — 60% of the run — is a runner sitting idle:

| Cost | Amount | Status |
| --- | ---: | --- |
| Idle with legs queued and both runners free | ~52 min (60% of wall clock) | Addressed by removing `max-parallel`; confirm on the next run |
| Per-job setup + teardown | ~30 s per job | Negligible; grouping has no prize left |
| Library cache restore + save, all legs | (was ~21 min per run) | FIXED (#279, #329, #330) |

### Two premises this plan recorded as settled that the same run disproves

- **"The org build-lock serializes ALL Unity CI legs" is false as of 2026-08-06.** `Acquire
  organization Unity lock` took **2–4 s on all 15 licensed legs** of `31130259492`. Nothing waited on
  it. That premise is load-bearing under "CI-level sharding does not cut wall clock", under #337, and
  under the barrier between the standalone and SINGLE_THREADED tiers — all three deserve
  re-examination now that the seat is not the constraint. Re-measure the acquire step before relying
  on it again either way; a busy sibling could make it true on some other day.
- **The idle time is not (only) barrier transitions.** It happens *within* a tier too:
  `ELI-MACHINE` finished a leg at +9.4 min and started its next at +22.8 with four legs already
  queued. That is `max-parallel`, which gates matrix *eligibility* rather than execution, so every
  completion needed a fresh self-hosted dispatch. The cap is removed and the contract inverted to
  forbid it; the next run's leg-to-leg gaps are the confirmation, and if they do not shrink the
  commit should be reverted rather than kept on theory.

Sibling contention was ruled out for that window: no Ambiguous-Interactive repo (DxMessaging,
DoxReloaded, IshoBoy, qora-redux) touched the self-hosted runners while ours sat idle.

### Done

- **A runner OS-kill strands the Unity seat, session 167.** Two standalone IL2CPP legs on
  `ELI-MACHINE` were killed by the OS mid-`Run Unity Test Runner` (`Out of memory.`), 5.8 and 6.5 min
  in. An OS kill marks **no step failed**, leaves every later step `pending` — `Return Unity license`
  and `Release organization Unity lock` included, `if: always()` notwithstanding — and uploads **no
  logs at all** (`BlobNotFound`). The seat is then stranded until the central reaper runs, and the
  re-run is refused with `belongs to an earlier run attempt`, so one kill costs two outages.
  Host-specific: `DAD-MACHINE` 7 standalone legs / 0 failures, `ELI-MACHINE` 5 / 2, and the same
  2022.3 leg that failed twice on `ELI-MACHINE` passed on `DAD-MACHINE` with identical code. Both
  hosts have 64 GB and ~1.5 TB free, so it is not capacity per box; both kills followed a long
  unbroken chain of Unity jobs on that host (seven in 34 min before the first, with an IL2CPP build
  ending **one second** earlier). Runner diagnostics now print free memory, free commit charge, and
  any already-resident Unity/il2cpp process, because none of that was recorded and all of it had to
  be inferred. Tracked in #353.

- **Build-lock v1.11.0 breaks an input contract; it has nothing to do with truncation, sessions 167
  and 178.** Dependabot's group bump moves the six `ambiguous-organization-build-lock` actions off
  v1.9.1 and reddens **every** Unity leg at `Require confirmed Unity cleanup` while the tests
  themselves pass. It merged in `bf05d620` and did exactly that (8986 passed / 0 failed on 2022.3
  playmode, eight red legs).

  **The RCA recorded here through session 167 was wrong, and it is worth knowing why it was
  convincing.** `return-log-truncated` is the classifier's *pre-written default* — `run()` appends a
  full set of fail-closed defaults to `GITHUB_OUTPUT` before it reads anything, so they stand
  whenever the action throws for any reason. The reason code therefore named a cause that had not
  happened, and `classification=false` was the default rather than a verdict. The benign Unity
  shutdown-crash warning is real, and also appears on green v1.9.1 runs, which is what made the
  coincidence look like causation.

  What actually threw: the new pin adds a sixth input, `return-log-digest`, `required: true`, and
  `parseInputs` throws when it is absent. Our local `.github/actions/return-unity-license` passes the
  five the old pin declared. The tell is duration — `Classify redundant return evidence` failed in
  **49 ms**, with no log read. **Reverting is provable rather than hopeful:** `f92d4b0b` is Unity
  Tests success on push, `bf05d620` is failure, identical package code.

  **The general failure mode is now closed.** `npm run test:build-lock-action-inputs` reads every
  pinned build-lock call's `with:` block against the inputs that action declares *at its own pin*, in
  both directions, and needs no Unity, license or matrix — so it runs on the Dependabot pull request
  itself, which is the only place this can be caught before merge. So the standing "watch: any future
  bump must run on a credentialed branch" is enforced by a check rather than by memory.

  **Why this repository and no other.** DxMessaging, IshoBoy, DoxReloaded and qora-redux all pass
  `return-log-digest` already — they call the *central* `return-unity-license` action. unity-helpers
  is the last consumer returning its own license, which is why the bump broke it alone. Until that
  migration lands the pins cannot move at all, holding back every lock-action fix. Tracked in **#411**
  with the concrete sequence; blocked on **#325** (the central action computes
  `<tool_cache>/u6-v3[/_ci-managed-editors]/<version>/Editor/Unity.exe` and this repository's editors
  live at `C:\Unity\Editors\`). Closer than it reads: `ensure-editor.ps1` already implements the
  `_ci-managed-editors` sub-root under that exact name, and `UNITY_EDITOR_INSTALL_ROOT` is already a
  single knob honored by all five scripts that resolve, install, maintain or bootstrap an editor.
- **Most contract tests never run in CI, session 167.** `test-sync-script-contracts.ps1` is reachable
  only via `validate:tests` → `validate:prepush`, a local pre-push hook, so #334 was green while
  breaking `release publish tag preparation sets up Node before npm publication checks`. Added to
  `test-lint.yml`; the rest of the `validate:tests` chain is still hook-only. Two defects fixed in that
  suite: a **restated** `actions/setup-node` SHA (now derived from `release.yml`, the same lesson #351
  applied to the build-lock pins), and five ordering assertions whose `IndexOf(a) -lt IndexOf(b)` form
  passed **vacuously** when `a` was absent — proven by renaming a step and watching 132/132 stay green.

- **`max-parallel: 2` was the idle time, session 166.** It gates matrix *eligibility*, not execution
  — a self-hosted runner takes one job at a time regardless — so every completion needed a fresh
  self-hosted dispatch, and that dispatch is what costs minutes. Removed from all three
  `unity-tests.yml` tiers and from `unity-benchmarks.yml`; the contract is inverted to forbid a cap.
  Confirmed on PR #352's own run: all eight fast-tier legs eligible at +0.3 min instead of two at a
  time, and **leg-to-leg handoff on one runner of 0.0 min, twice**, against 13.4 min in the baseline.
- **Every Dependabot PR was permanently red, session 166.** `runner-preflight` was the one job
  reaching for organization secrets without first asking whether the run has any; Dependabot's
  separate secret store made `BUILD_LOCK_READER_APP_ID` empty and the availability action failed
  closed, while `matrix-config` had already skipped the licensed tiers. Fixed with a
  credential-detection step plus an asserting no-credentials branch in `Unity CI Success`. **Watch
  after merge:** #334 needs a re-run to pick up the fixed workflow.
- **#279 / #329 / #330 — the Library cache is gone, because the Library never needed to leave the
  runner.** `actions/checkout` runs `git clean -ffdx` at the top of every job and `-x` means
  gitignored, so the generated project under `.artifacts/` — `Library` and all — was deleted at the
  start of every job and downloaded back from `actions/cache`. Measured across the 60 most recent
  Unity Tests runs: **109 min of restore and 119 min of save**, ~21 min per protected-branch run, all
  of it on the single serialized organization Unity seat. The project now lives at
  `<RUNNER_WORKSPACE>\unity-workspace`, one directory above the checkout, where `git clean` cannot
  reach it; both cache steps are deleted. This closes the three issues as one change: there is no
  post-cache step left to be slow (#279), no gzip fallback to replace with `zstd` (#329), and nothing
  to measure about what the standalone Library costs to compress (#330). Cost traded in: local disk,
  bounded by an LRU prune below a free-space floor, with free space now printed in the runner
  diagnostics every run.
- **The stuck-job watchdog was never functional.** #315 through #328 it reported success on every
  cycle while exiting at step 3: `orgs/{owner}/actions/runners` needs `admin:org` and `GITHUB_TOKEN`
  403s, the repo-scoped fallback does not list org-level runners, and the handler exited 0. It now
  queries with the build-lock reader App and fails closed on an unreadable inventory. **Watch after
  merge:** if the App lacks organization self-hosted runner read, the workflow goes red every five
  minutes naming that grant — that is intended, and the remedy is a permissions change.
- **#342 — the watchdog now also recovers a run reported `cancelled` with every step green.** The
  signature from #342 is specific and needs no runner inventory: a job with `conclusion: cancelled`
  whose step list is non-empty and whose every step succeeded. A deliberate cancel cannot match,
  because its in-flight step is itself cancelled. Recovery is `POST actions/runs/{id}/rerun` on
  `GITHUB_TOKEN`'s existing `actions: write` — the read-only build-lock App is not used and must not
  be widened for it. Bounded twice: `run_attempt <= MAX_RERUN_ATTEMPT` escalates a re-run that lands
  in the same state to a human, and `MAX_RERUNS_PER_DAY` caps per run id in its own state file
  (distinct from the cancel state file, whose reader falls back to a `.reruns` key and would
  otherwise spend the cancel budget). The phase runs **before** the queued-run scan, because that
  scan exits the whole step on a clean queue — the normal state. Four contract assertions in
  `test-unity-workflow-matrix-contract.ps1` pin all of it. **Watch after merge:** the step summary
  gains a "Cancelled with every step green (re-run)" section; if it fires on runs that were
  deliberately cancelled, the non-empty-step-list guard is not doing its job.
- **#331 — the bundled-assembly `CS0012` was misdiagnosed.** Not competing with Unity 6000. Unity
  dedups precompiled assemblies by simple name, and `com.unity.ai.assistant` wins
  `System.Text.Encodings.Web` with an `isExplicitlyReferenced` copy that nothing auto-references
  (measured 0 of 245 assemblies). Consumer remedy documented in the
  [bundled-assembly conflict guide](./docs/guides/bundled-assembly-conflicts.md). Separately, the version boundary is 6000.5, not
  6000.0 — #328's constraint broke compilation on every 6000.0–6000.4 editor.
- **#332's second-order gap — a superseded run reported failure.** `matrix-config` resolves
  supersession once, before dispatch, so it cannot see a push that lands while legs sit in the
  queue for the single Unity seat. Those legs wake up, fail their own `require-current-pr-head`
  guards, and `Unity CI Success` reports the run red. Measured on run `31020762387`: six legs
  failed, all six on `Stale pull request run`, zero on a test. `Unity CI Success` now re-resolves
  the head itself — it runs last and on a hosted runner, so it sees the head as of the moment the
  verdict is written. Supersession still waives only the four licensed results, and the probe
  fails open.
- **#332's second-order gap — a superseded run reported failure.** `matrix-config` resolves
  supersession once, before dispatch, so it cannot see a push that lands while legs sit in the
  queue for the single Unity seat. Those legs wake up, fail their own `require-current-pr-head`
  guards, and `Unity CI Success` reports the run red. Measured on run `31020762387`: six legs
  failed, all six on `Stale pull request run`, zero on a test. `Unity CI Success` now re-resolves
  the head itself — it runs last and on a hosted runner, so it sees the head as of the moment the
  verdict is written. Supersession still waives only the four licensed results, and the probe
  fails open.
- **#332 — superseded runs no longer hold the self-hosted queue.** The per-leg
  `require-current-pr-head` guards fire only after dispatch, so a stale iteration queued eight legs
  for the single Unity seat to do nothing, while the successor run waited behind a concurrency group
  that (correctly) cannot cancel in progress. `matrix-config` now resolves it once on a hosted
  runner and skips the licensed tiers; a superseded run finishes in seconds. Measured before the
  fix: run `30963202461` still had two legs queued 45 min after the head moved, and #328 merged with
  no Unity validation at all.

### Open, ordered by measured value

Sessions 168 and 169 closed this list by measuring it. **Nothing in-repo is left.** The one
remaining lever is not a code change:

1. **A third runner — the only lever on wall clock, and it is not ours alone.** Five repositories
   share two self-hosted Windows hosts. Both "idle" windows in run `31231780930` were this repo's
   runners serving IshoBoy and DoxReloaded. Nothing inside this workflow recovers that time; the
   decision is organization-level capacity, and it should be made against sibling job volume rather
   than this repo's numbers.

### Closed by measurement in session 169

- **#363 (per-leg fixed overhead) — closed, and its framing corrected.** This plan derived
  "~1.5 min/leg of fixed overhead" by subtracting NUnit duration from job duration, then listed the
  suspects as a *set of steps* — checkout, project generation, editor launch, lock acquire, license
  return, cleanup. Broken down per step on run `31242039625` with each leg's own `results.xml`:

  | | 2021.3 editmode | 2022.3 standalone |
  | --- | ---: | ---: |
  | Job total | 379 s | 450 s |
  | `Run Unity Test Runner` | 347 s | 416 s |
  | NUnit duration | 291.1 s | 296.0 s |
  | **Inside the step, not NUnit** | **55.9 s** | **120.0 s** |
  | **Every other step combined** | **32 s** | **34 s** |

  **91-92% of a leg is one step**, and the overhead is *inside* it — Unity launching, generating the
  ephemeral project, importing assets, reloading domains, and for standalone building the IL2CPP
  player (which is what makes 120 s twice the fast tier's 56 s). The orchestration steps total ~33 s
  with `Checkout` the largest at 6 s; grouping legs to amortize that would save seconds and give up
  the isolation keeping two same-workflow jobs off one Unity seat. The number to watch is the
  non-NUnit share of `Run Unity Test Runner`, not the step list.

- **#337 (parallel Unity legs on one host) — closed, premise false, second confirmation.** Its
  "they are all on one host, already serialized" premise fails twice over: the org lock takes 2-4 s
  on every leg, and the fourteen legs already split across `DAD-MACHINE` and `ELI-MACHINE`, observed
  running Unity simultaneously. With the slowest leg at 450 s and 92% of it one step, two editors on
  one host would only contend for cores against the siblings already sharing those runners.

### Closed by measurement in session 168

- **#337 (parallel Unity legs on one host) — closed, premise false.** The org lock does not
  serialize legs: 2-4 s acquires on all eight fast legs, and `DAD-MACHINE` and `ELI-MACHINE` were
  observed running Unity **simultaneously** (01:05:04 → 01:08:51 overlap on `31231780930`) — direct
  proof rather than inference. And with the slowest leg at 314.1 s there is little to win: two
  editors on one host would contend for cores against the siblings already sharing it.
- **#353 (13.9 min inter-leg idle) — closed, and its measurement corrected.** The runners were not
  idle; they were serving other repositories. Its packing-floor arithmetic assumed our jobs were the
  only claim on those hosts and is not valid. Any future throughput number must include sibling jobs
  on the same runner names.
- **#326 (the tier barrier) — closed earlier by measurement.** 1 second between tiers on
  `31221618477`, against 10.1 min before `max-parallel` was removed. Retiring the
  `unity-tests-single-threaded` → `unity-tests-standalone` edge would buy nothing and would give up
  a contract that keeps two same-workflow jobs off one Unity seat.

### Step-timeout budget (audited, this session)

The step clocks were sized against fear, not measurement. Across the 60 most recent Unity Tests runs
(~430 leg instances), the slowest `Run Unity Test Runner` has **ever** been is 9.1 min:

| Tier | p50 | p95 | max | old cap | new cap |
| --- | ---: | ---: | ---: | ---: | ---: |
| editmode | 276s | 458s | 547s | 90 min | 40 min |
| playmode | 115s | 184s | 205s | 90 min | 40 min |
| standalone | 301s | 447s | 524s | 150 min | 60 min |
| single-threaded | 274s | 465s | 471s | 90 min | 40 min |

The cap is not a performance number, it is **how long a hang holds the single organization Unity
seat** — a 91-minute lock wait behind a stuck leg is measured (run `30965190533`). 40/60 keeps >4x
headroom over the worst case ever observed and is pinned by
`test-unity-workflow-matrix-contract.ps1` so it cannot drift back up unnoticed.

## Test Suite Runtime Reduction — GOAL MET, section retired

**The target was "every test mode < 10 min, ideally < 5 min". Every one of the fourteen legs is
under 5.3 minutes, and thirteen of fourteen are under 5.** Measured from the NUnit `results.xml` of
each leg of Unity Tests run [`31221618477`](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/31221618477)
(`main` at `766b7c26`, green, 0 failures across 14 legs), which is the authoritative signal this
section always said to re-rank from:

| Leg | Tests | NUnit duration |
| --- | ---: | ---: |
| 2021.3 editmode | 4653 | 237.7 s |
| 2022.3 editmode | 4653 | 193.6 s |
| 6000.3 editmode | 4653 | 187.9 s |
| 6000.5 editmode | 4653 | 210.6 s |
| 2021.3 playmode | 8743 | 91.7 s |
| 2022.3 playmode | 8743 | 100.4 s |
| 6000.3 playmode | 8741 | 122.1 s |
| 6000.5 playmode | 8741 | 119.0 s |
| 2021.3 standalone | 8740 | 203.3 s |
| 2022.3 standalone | 8740 | 199.1 s |
| 6000.3 standalone | 8738 | **314.1 s** (slowest) |
| 6000.5 standalone | 8738 | 220.2 s |
| 6000.3 editmode SINGLE_THREADED | 4636 | 260.4 s |
| 6000.3 playmode SINGLE_THREADED | 8661 | 123.4 s |

**Total NUnit execution across the run: 43.1 min.** The 40+ min SINGLE_THREADED EditMode run this
section opened with is now **4.3 min**.

The ranked steps that remain unstarted — the `BatchedEditorTestBase` migration backlog, more
logic/IO splits, `[assembly: Parallelizable]` on pure assemblies, right-sizing 10k+ correctness
loops, de-duplicating the IMGUI drawer matrices — are **not worth doing for wall clock**, because
there is no longer a mode over the target to pull down. Do them if they make a fixture easier to
read or a flake easier to find, not for speed, and re-open this section only if a future B1 report
shows a mode back over 10 min.

### What the leg time is actually spent on now — MEASURED, session 169

The per-leg overhead has now been broken down per step (#363, run `31242039625`), and it is **not**
a set of steps that could be grouped or cached. It is one step:

| | 2021.3 editmode | 2022.3 standalone |
| --- | ---: | ---: |
| Job total | 379 s | 450 s |
| `Run Unity Test Runner` | 347 s (91.6%) | 416 s (92.4%) |
| NUnit duration | 291.1 s | 296.0 s |
| **Inside the step, not NUnit** | **55.9 s** | **120.0 s** |
| **Every other step combined** | **32 s** | **34 s** |

The ~33 s of orchestration (largest: `Checkout` at 6 s; `Acquire organization Unity lock` 2-4 s) is
at its floor. The 56-120 s is Unity launching, generating the ephemeral project, importing assets and
reloading domains, plus the IL2CPP player build on standalone. Neither is ours to cut, and both are
already paid once per leg rather than once per fixture. **This section is closed**; the only number
worth watching is the non-NUnit share of `Run Unity Test Runner` growing.

### Still-true operational facts (kept; the rest of this section was retired)

- **Unity editor tests cannot run in parallel in-process** (main-thread APIs). `[Parallelizable]`
  helps **only** pure, non-Unity `[Test]` methods, and never on editor/Unity-object fixtures. See the
  [test parallelization rules](.llm/skills/test-parallelization-rules.md).
- **Never run the full EditMode suite via the Unity MCP bridge** — it takes hours and is CI's job.
  Targeted `groupNames` filters only; the bridge now refuses an untargeted one.
- **What the MCP editor cannot verify:** 2021.3-specific behavior, IL2CPP/AOT, DI fixtures (Reflex /
  VContainer / Zenject are absent), and full-run cross-test timing. Those four are CI-gated only.
- **The org build lock does NOT serialize Unity legs.** This section previously listed that as a
  verified hard constraint; it is false and three conclusions rested on it. `Acquire organization
  Unity lock` takes 2-4 s on every leg, and two hosts have been observed running Unity
  simultaneously. See [CI Throughput](#ci-throughput).

---

## Backlog: Auto-Loading Cache Feature

**Status: NOT STARTED (verified — none of `LoadingCache`, `AsyncLoadingCache`, `KeyedLock`,
`LoadingCacheBuilder`, `CacheLoader`, `RefreshAfterWrite` exist).** Demoted below the runtime work; the
existing `Cache.cs` / `CacheBuilder.cs` / `CacheOptions.cs` / `CacheStatistics.cs` (the modify-targets) do
exist, so the premise holds.

Caffeine-style `LoadingCache<TKey,TValue>` + `AsyncLoadingCache<TKey,TValue>`: per-key locking
(no-op under `SINGLE_THREADED`), refresh-ahead (`RefreshAfterWrite` returns stale while refreshing in
background), bulk `GetAll`, async via `ValueTask<TValue>`.

### Surface

```csharp
public delegate TValue CacheLoader<in TKey, out TValue>(TKey key);
public delegate ValueTask<TValue> AsyncCacheLoader<in TKey, TValue>(TKey key, CancellationToken ct);

public class KeyedLock<TKey> where TKey : notnull {            // Runtime/Core/DataStructure/KeyedLock.cs
    public IDisposable Lock(TKey key);
    public ValueTask<IDisposable> LockAsync(TKey key, CancellationToken ct = default);
}
public class LoadingCache<TKey, TValue> {                       // Get/GetOrDefault/Invalidate/InvalidateAll/Refresh
}
public class AsyncLoadingCache<TKey, TValue> {                  // GetAsync/GetAllAsync/Invalidate/InvalidateAll/RefreshAsync
}
```

### Implementation order

1. `KeyedLock<TKey>` + tests → 2. `RefreshAfterWrite` in `CacheOptions`/`CacheBuilder` → 3. `LoadingCache`
(+ internal `Cache.IsRefreshEligible()`) → 4. `BuildLoading` + `LoadingCachePresets` + tests → 5.
`AsyncLoadingCache` + `BuildAsyncLoading` + presets + tests → 6. `CacheStatistics.RefreshCount`,
CHANGELOG, `.meta` files.

### Design decisions

| Question | Decision |
| --- | --- |
| Per-key locking under `SINGLE_THREADED`? | No-op (no locks acquired) |
| Refresh failure behavior? | Keep stale value (Caffeine behavior) |
| `ValueTask` vs `Task`? | `ValueTask<TValue>` for `GetAsync` (cache-hit optimal); `Task<TValue>` internal |
| Bulk loading on `GetAll`? | Yes — optional bulk loader, falls back to individual loads |
| Statistics? | Add `RefreshCount` to `CacheStatistics` |
| Null loader on `BuildLoading`? | Throw `ArgumentNullException` (loader mandatory) |

New files: `KeyedLock.cs`, `LoadingCache.cs`, `AsyncLoadingCache.cs`, `LoadingCachePresets.cs`,
`AsyncLoadingCachePresets.cs` + matching tests under `Tests/Runtime/Core/DataStructure/`.

---

## Design Item: In-tree v2/v3-wire-compatible AOT-native serializer (WallstopProto)

**Status:** HIGH PRIORITY, **in progress**. Steps 1-4 have landed (sessions 171-175); steps 5-6 are open.
Session 175's PR closed a ten-finding review round -- nine real defects, one a wrong comment -- and is
green on all 51 checks including the full 16-leg Unity matrix. The defects are recorded against the
steps they belong to below; the pattern across them is that **each lived in a gap between two axes the
differential suite already covered separately** (an abstract fixture and a collection fixture but never
an abstract collection; a hooked contract and a map contract but never a hooked map value). One --
a writer nesting-depth double-decrement -- could not have been caught by byte differentials at all,
because the bytes are correct either way; it is now pinned by asserting writer state directly. This is
the chosen permanent fix for the protobuf-net IL2CPP failures (supersedes the AOT-hint and precompile
approaches in
[§A](#a-standalone-il2cpp--protobuf-net-aot-structvaluecheckert-is-the-open-majority)). It is a large
initiative, but it is **not** independent of consumer code — see the next subsection.

### Encoding policy: interoperate with protobuf-net, do not imitate it (owner directive)

**The requirement is that protobuf-net can read what this package writes, and vice versa. It is
NOT that the two produce identical bytes.** Where proto3 has a better answer, take it.

Settled by measurement, not preference. protobuf-net 3.2.56 decodes a **packed** run into a field it
declares unpacked, exactly, for every packable element type (`int`, `long`, `ulong`, `bool`, enum,
`float`, `double`, `short`) and into both arrays and `List<T>`. So packing is free interoperability
and a large size win:

| elements | packed | unpacked | saved |
| --- | --- | --- | --- |
| 10 | 12 | 20 | 40% |
| 100 | 102 | 200 | 49% |
| 1000 | 1875 | 2872 | 35% |

Repeated scalar members are therefore written **packed** (session 175), which protobuf-net at
CompatibilityLevel 200 does not do. Strings, byte arrays and messages are never packed: a
length-delimited element carries its own length, so a packed run of them cannot be parsed at all.

**The consequence for the test suite is the part to be careful about.** Byte-identity with the
oracle is no longer the contract for packable repeated members, and those differentials now assert
**bidirectional interop** -- each serializer reads what the other writes -- plus literal expectations
for the exact packed bytes so a change is still a visible diff. That is a stronger claim than byte
equality, because identical output only ever exercised two encoders while this exercises both
decoders. Byte-identity remains the contract everywhere else.

Before diverging again, measure the same way: confirm protobuf-net **reads** the new form, for every
affected type, and show the win. A divergence that is not measured against the oracle is a
compatibility break, however sensible it looks.

**Internals are entirely free** -- data structures, buffer reuse, when things are resolved -- since
none of it is observable on the wire.

### Non-negotiable: consumer code is a first-class client (owner directive)

WallstopProto is not an internal implementation detail of this package. A game that depends on
`com.wallstop-studios.unity-helpers` must be able to annotate **its own** types and get the same
IL2CPP-safe serialization the package gets, with no manual registration. Three consequences, all
binding:

1. **The whole surface is `public`.** `WProtoWriter`, `WProtoReader`, `WProtoSizes`, `WProtoZigZag`,
   `WProtoWireType`, `IWProtoFormatter<T>` and every attribute ship as public API. A consumer must be
   able to hand-write an `IWProtoFormatter<T>` for a type the generator cannot see (one from a
   third-party assembly, for instance) and register it.
2. **The generator runs on the consumer's compilation**, not only this package's. That is the property
   that makes `Deque<TheirStruct>` work under IL2CPP and the reason the precompiled-model and
   v2-downgrade options are dead — neither can close a generic over a type that does not exist at
   package build time.
3. **Consumer diagnostics are part of the deliverable.** A contract the generator cannot serialize must
   fail the consumer's build with a message naming the type, the member and the fix — never an opaque
   `ExecutionEngineException` at runtime in a shipped player.

### Non-negotiable: lifecycle hooks and explicit member names (owner directive)

Both were missing from this plan's conformance checklist and both are load-bearing.

- **Pre/post (de)serialization hooks are mandatory, not optional.** The inventory found **15** of them
  already in `Runtime/`. Without them `Deque`, `CyclicBuffer`, `SerializableDictionary`,
  `SerializableHashSet`, `SerializableSortedDictionary` and `DotNetRandom` all deserialize into
  half-built objects — a dictionary that never rebuilds from its parallel key/value arrays looks
  empty, not broken. Consumer types get the same four hooks.
- **Explicit member/message names are mandatory.** `Name` is carried on `[WProtoMember]` and
  `[WProtoContract]`. It is never written to the wire — protobuf identifies fields by number alone —
  but it decouples the schema, diagnostics and payload dumps from the C# identifier, so renaming a
  field in C# does not silently rename it for every consumer of the schema.

**Hook ordering is a contract, pinned by `WProtoFormatterContractTests`:** before-serialization runs as
the first statement of `Measure` and is **not** repeated in `Write` (a hook that rents pooled scratch
would otherwise leak); after-serialization is the last statement of `Write`; before-deserialization runs
after the instance exists and before any member is assigned; after-deserialization runs only on a
**successful** read, because rebuilding derived state from half-populated members produces a
plausible-looking wrong object instead of a reported failure.

**How generated code reaches a private hook.** Hooks and backing fields are usually private, and
reflection is off the table (it is the entire reason this project exists). The formatter is therefore
emitted as a **nested type of the contract**, which has access to its enclosing type's private members.
That requires the contract to be `partial`; a non-partial contract with a private hook is a generator
diagnostic telling the consumer to add `partial` or widen the hook, never a silent skip.

### Problem (one paragraph)

protobuf-net v3's runtime engine is fundamentally incompatible with IL2CPP: it constructs serializers by
reflectively instantiating `internal` closed generics (`ConcreteStub<T>`, `ListSerializer<T>`,
`StructValueChecker<T>`) via `MakeGenericType`, which IL2CPP cannot AOT-compile, and it calls the
IL2CPP-unsupported `RuntimeParameterInfo::GetTypeModifiers` icall during model build. This is
maintainer-confirmed (protobuf-net#699/#792/#1025/#1190); the real protobuf-net AOT story ("generators") is
still not shipped. No `link.xml`/`[Preserve]`/`AotHelper` manifest can fix it (the failing generics are
`internal` and reflectively reached). Yet we **require** runtime protobuf serialization in shipped IL2CPP
players (player data) **and** must stay byte/wire-compatible with data already written by protobuf-net —
**v2 *and* v3 both**, because this is a library and clients in the wild have persisted data with either. At
CompatibilityLevel 200 v2 and v3 usually emit the same wire format, but session 182 proved they are
**not identical at every edge**: v2 omits default string map keys/values and a default struct map value that v3
writes, and v2 silently drops a null repeated element that v3 refuses. The v2 oracle also caught and drove a
reader fix: an omitted string map value must become `string.Empty`, not `null`. The conformance suite therefore
uses a **dual oracle** (protobuf-net 2.4.9 **and** 3.2.56), requires byte identity on the shared domain,
and pins cross-read migration behavior plus the exact divergence wherever the majors disagree.

### Why an in-tree source generator is the *right* answer (not a fallback)

| Option | IL2CPP runtime | v3 wire-compat | Covers arbitrary **consumer** generic element types (`Deque<TheirStruct>`) | Verdict |
| --- | --- | --- | --- | --- |
| AOT hints / `[Preserve]` / `link.xml` | ❌ (internal reflective generics) | ✅ | ❌ | **Dead** (proven) |
| Downgrade to protobuf-net v2.4.x + precompiled model | ⚠️ (linker-version-sensitive) | ✅ (also Level 200) | ❌ (precompiled model is closed at package build) | EOL, partial |
| Migrate to Google.Protobuf / MemoryPack | ✅ | ❌ (`[ProtoInclude]`/surrogate encoding won't round-trip) | ✅ | Breaks existing data |
| **In-tree source-generated serializer (WallstopProto)** | ✅ (zero runtime reflection) | ✅ (reproduces Level 200 exactly) | ✅ (**generator runs at the consumer's build**) | **Chosen** |

The decisive property: a Roslyn source generator emits static serializer code **at the compilation that
references the closed type** — including the *consumer's* assembly. So `Deque<ConsumerStruct>` gets a real,
statically-dispatched serializer in the consumer's build, which IL2CPP AOT-compiles normally. This is the
**only** approach that solves the redistributable-generics problem; precompile and v2-downgrade cannot, by
construction. It also keeps the perf wins (generated straight-line code beats reflection/ref-emit on cold
start and matches it warm), and lets us **delete the vendored `protobuf-net.dll` + `protobuf-net.Core.dll`
from the runtime entirely** (they remain only in the *test* assembly, as the conformance oracle).

### Goals / non-goals

**Goals**
- Byte-for-byte wire compatibility with protobuf-net **2.4.9 and 3.2.56** @ CompatibilityLevel 200 on
  their shared domain, with v2-to-WallstopProto migration reads pinned for the known major-version
  divergences. Both vendored oracles run in isolated processes, and a committed golden-byte corpus keeps
  the guarantee after an oracle is removed.
- Zero runtime reflection / zero `MakeGenericType` / zero ref-emit → clean IL2CPP + future NativeAOT.
- Drop-in for the existing `Serializer.ProtoSerialize<T>` / `ProtoDeserialize<T>` facade (no consumer API
  break; same method signatures).
- Support consumer-defined `[ProtoContract]` types and consumer closed generics with no per-consumer manual
  registration (generator-driven).
- Equal-or-better throughput and allocation vs protobuf-net v3 warm; dramatically better cold-start.

**Non-goals (initial release)**
- Full `.proto` schema import/export, gRPC, `DynamicType`, `AsReference` (graph/reference tracking),
  runtime-mutable models, `Serializer.Merge` onto pre-populated graphs. (Enumerate any of these the package
  actually uses during the inventory phase; the grep found none of `AsReference`/`DynamicType`.)
- CompatibilityLevel 240/300 encodings (not used; default is 200).

### Architecture (MemoryPack/LightProto-style, adapted to protobuf wire format)

1. **Roslyn `IIncrementalGenerator`** (`WallstopStudios.UnityHelpers.Proto.Generator`, netstandard2.0 DLL,
   shipped as a Unity `RoslynAnalyzer`-labelled asset so it runs on the package **and** consumer assemblies).
   - Discovers types annotated `[WProtoContract]` and surrogate registrations. **DECIDED, session 176
     (owner):** WallstopProto reads its **own attributes only**. `[ProtoContract]` is not read, and a
     consumer migrating a contract adds the WallstopProto attributes beside the protobuf-net ones with
     the same field numbers. Reusing protobuf-net's attributes would have made the two serializers
     indistinguishable at the declaration, so a feature protobuf-net supports and this one does not --
     `AsReference`, `DynamicType`, `DataFormat`, `ImplicitFields` -- would read as supported and
     silently mean something else.

     **The consequence to hold on to:** step 5's "remove the runtime protobuf-net DLLs" and a
     consumer's `[ProtoContract]` cannot both survive, because the attributes ship inside those
     assemblies. Removing them is a **compile** break in consumer code, not a behaviour change, so it
     is a major-version act and the migration has to be complete first. Until then the missing
     annotation is silent -- the type simply keeps using protobuf-net -- which is what the migration
     diagnostic on the follow-up issue is for.
   - For each type, emits a `partial` static formatter implementing `IWProtoFormatter<T>`:
     `void Write(ref WProtoWriter w, in T value)` + `T Read(ref WProtoReader r)`, as straight-line code over
     the type's `[ProtoMember]` fields (tag + wire-type constants computed at generate time).
   - For generic `[ProtoContract]` types, emits an **open-generic** formatter `Formatter<T>` whose element
     access goes through `WProtoFormatterProvider.Get<TElement>()` — static generic dispatch, AOT-friendly.
2. **`WProtoFormatterProvider`** — a static registry mapping `T` → `IWProtoFormatter<T>`. Generated
   per-assembly `[ModuleInitializer]`/`RuntimeInitializeOnLoadMethod` registration code populates it for every
   discovered closed type (incl. consumer closed generics seen at the consumer's build). Built-in formatters
   for primitives, `string`, `byte[]`, enums, `Nullable<T>`, `List<T>`/arrays/dictionaries, and the
   `bcl.proto` types (DateTime/TimeSpan/Guid/Decimal at Level 200).
3. **`WProtoWriter` / `WProtoReader`** — `ref struct` over `Span<byte>` / `IBufferWriter<byte>`: varint
   (little-endian base-128), zigzag, fixed32/64, length-delimited with back-patched lengths (two-pass or length-prefixed
   scratch), tag read/skip-unknown-field. This is the hand-written wire core (~1 file, heavily tested).
4. **Facade integration** — `Serializer.ProtoSerialize<T>`/`ProtoDeserialize<T>` (Serializer.cs lines
   ~2125/~1665) dispatch to `WProtoFormatterProvider.Get<T>()` instead of `ProtoBuf.Serializer`. The existing
   special-collection wrapper marshalling (Deque/CyclicBuffer/SparseSet → `*ProtoWrapper<T>`) is preserved
   conceptually but the wrappers become generated formatters.

### Wire-format conformance requirements (the exhaustive checklist — Level 200)

Reproduce protobuf-net v3 *exactly* for:

- **Field key** `(fieldNumber << 3) | wireType`; wire types 0=varint, 1=64-bit, 2=length-delimited, 5=32-bit.
- **Scalars (DataFormat.Default — the package sets no `DataFormat`):** int32/int64/uint32/uint64/bool/enum →
  varint; float → fixed32; double → fixed64; string/bytes → length-delimited. (No ZigZag/FixedSize/TwosComp
  observed; confirm in inventory and emit accordingly if any appear.)
- **Repeated fields:** protobuf-net default is **unpacked** (repeated `tag+value`) unless `IsPacked=true`
  (none found). Match this — do **not** pack by default (differs from proto3).
- **`[ProtoInclude(tag, subType)]` inheritance** (AbstractRandom, tags 100–116): the concrete subtype is
  written as a length-delimited field at its include tag on the base message, containing the subtype's own
  members; base members written at their tags. **The include comes FIRST, before the base's own members,
  whatever its tag number** — measured session 175, and the opposite of what this line used to claim.
  Confirmed with an include at tag 3 sitting ahead of base members at tags 1 and 5. Each level repeats the
  pattern recursively: its own subtype include, then its own declared members, never the base's. An
  all-default subtype **still writes its include** (`A20600`), or its type identity is lost. See #390 for
  the full hex, including the payload that sends protobuf-net into an uncatchable stack overflow.
- **Surrogates:** apply the registered real→surrogate transform at write, surrogate→real at read; the
  surrogate's `[ProtoMember]` tags define the wire shape (FastVector3Int's deliberate out-of-order
  3=hash/4=z must be preserved — ProtobufUnitySurrogates.cs ~370).
- **`Nullable<T>`:** present iff `HasValue` (e.g. `AbstractRandom._cachedGaussian` `double?`,
  `UnityRandom._seed` `int?`); absent field on read → `null`.
- **`IgnoreListHandling` (Deque)** and **`OverwriteList=true`** (Serializable{HashSet,Dictionary,SortedDictionary}):
  reproduce as message-shaped (not list) and replace-on-read semantics respectively.
- **`bcl.proto` Level 200:** DateTime/TimeSpan → bcl sub-message (zigzag value + scale enum [+ kind]); Guid →
  bcl `{lo: fixed64, hi: fixed64}`; Decimal → bcl `{lo: uint64, hi: uint32, signScale: uint32}`. Only
  implement the ones actually used (inventory).
- **Default-value omission:** protobuf-net omits members equal to their declared/implicit default unless
  `IsRequired`/explicit default differs — match its emit rules precisely (this is a common byte-diff source).
- **`IsRequired` forces a value, never a reference.** Measured session 174: a required `int` at 0 and a
  required **struct** sub-message at `default` are both written (`08 00`, `2A 00`), while a required
  **null** `string`, `byte[]` or message reference is still absent. Reading "required" as "always
  present" writes an empty string where protobuf-net wrote nothing, and hands a nested formatter's
  `Measure` a null to dereference.
- **A struct sub-message is always present.** The opposite of the scalar rule: `default(Point)` on a
  message-typed member emits `12 00`, while a null reference member emits nothing.
- **Field ordering:** protobuf-net writes members in ascending field number. Match. **Includes are the exception** and are written before every member — see the inheritance bullet above.
- **Empty is not absent.** An empty-but-non-null `string` or `byte[]` **is** written, as tag + a zero
  length (`0A 00`); only `null` is omitted. Measured, not assumed — the first differential run predicted
  omission for both and protobuf-net disagreed.
- **Negative zero does not survive.** protobuf-net's default-omission test is `value == 0`, and
  `-0.0 == 0.0`, so a `-0f`/`-0d` member is dropped and reads back as `+0`. Also measured. Reproduce it
  (wire compatibility beats fidelity here), and document it, because it is a silent data change.
- **Lifecycle hooks** (see the owner directive above): four attributes, fixed ordering, and
  after-deserialization must not run on a failed read.
- **Explicit `Name`** on members and contracts: carried, never written to the wire.

### Frozen conformance inventory (session 171)

Measured across `Runtime/`, `Editor/` and `Tests/`. This is the surface the port must reproduce.

| | Count |
| --- | ---: |
| `[ProtoContract]` types in `Runtime/` | 65 (`Editor/` has none) |
| `[ProtoMember]` in `Runtime/` | 171 |
| `[ProtoInclude]` | 17, all on `AbstractRandom`, contiguous tags 100-116 |
| Surrogate registrations | 16, all in `ProtobufUnitySurrogates.cs` |
| Lifecycle callbacks | 15 |
| `OverwriteList = true` | 15 members |
| `SkipConstructor` / `IgnoreListHandling` | 5 types / 7 types |

**What is confirmed absent** — the non-goals list above is accurate, and then some: no `AsReference`, no
`DynamicType`, no `Serializer.Merge`, no `IExtensible`, no `[ProtoEnum]`, no `ImplicitFields`, no
`[ProtoPartialMember]`, no `CompatibilityLevel`, no `DataFormat`, no `IsPacked`.

**Four findings that change the plan:**

1. **No BCL sub-messages are needed at all.** No `DateTime`, `TimeSpan`, `decimal` or `System.Guid`
   appears on any shipped `[ProtoMember]`; the package ships `WGuid` as two `long`s (tags 1/2), which is
   *not* the bcl `{lo: fixed64, hi: fixed64}` shape. `bcl.proto` support drops out of the critical path
   entirely — only two test-only perf fixtures use `System.Guid`.
2. **There is one enum in the whole surface and it has no negative values.** `ModificationAction`
   (`int`, 0/1/2). The negative-enum varint sign-extension case does not exist here.
3. **Seven types never reach protobuf-net through the facade.** `Deque`, `CyclicBuffer`, `SparseSet` and
   the four `Serializable{HashSet,SortedSet,Dictionary,SortedDictionary}` are intercepted by `Serializer`
   and marshalled through `*ProtoWrapper` POCOs, so their on-wire shape is the wrapper's, and their own
   `[ProtoContract]`/`[ProtoAfterDeserialization]` are dead through the facade but **live** if a consumer
   calls `ProtoBuf.Serializer` directly. Both encodings have to be decided on, not just the facade one.
4. ~~**The dual-oracle strategy has no second oracle.**~~ **CLOSED, session 182 (#371).** The official
   protobuf-net 2.4.9 assembly is pinned under `Generator~`, outside every Unity/package payload. The
   v2 and shipped 3.2.56 assemblies share the same simple name, so the test project runs once per oracle
   in separate processes and asserts the exact physical assembly identity before any differential can pass.

**Two quirks that must survive byte-for-byte:** `FastVector3Int` declares its tags out of source order
(1, 2, **4**, **3** — `z` is 4, the cached hash is 3) and its surrogate deliberately mirrors that; and
`ResolutionSurrogate.refreshRate` (tag 3) is written but **discarded on read** for Unity ≥ 2022.2, so any
byte-diff oracle has to expect that asymmetry rather than flag it.

### Consumer extensibility & IL2CPP generics (the key advantage, spelled out)

- A consumer adds `[ProtoContract] struct Foo {…}` and serializes `Deque<Foo>`. The generator runs on the
  **consumer's** assembly, sees `Foo` and the `Deque<Foo>` usage, emits `Formatter<Foo>` + registers
  `Deque<Foo>`'s closed formatter. IL2CPP AOT-compiles them because they're real static code referenced from
  the consumer's call site — **no runtime `MakeGenericType`, no manifest.**
- Fallback: if a closed type is somehow unseen at build, `Get<T>()` throws a *clear, actionable* exception at
  the call site (naming the type + how to annotate), not an opaque `ExecutionEngineException`.

### Differential conformance test strategy (how we *prove* byte-compat)

- Keep vendored protobuf-net **2.4.9 and 3.2.56** in test-only locations as a **dual oracle**. Run the
  same test project in two isolated processes because both DLLs have the simple name `protobuf-net` and
  side-by-side aliases can silently collapse onto one physical assembly. Require byte identity where the
  majors agree; where they do not, pin both exact encodings and prove migration reads rather than claiming
  equality that the reference implementations do not have.
- For every supported type, a property-style corpus (edge values: empty, max/min, negative, large
  collections, nested, polymorphic across all 17 PRNG subtypes, null/non-null Nullable, default-valued
  members): assert `WProto.Serialize(x)` **bytewise ==** `ProtoBuf.Serializer.Serialize(x)`, **and**
  cross-deserialize both directions (`WProto.Deserialize(protobufNetBytes)` and vice-versa) → value-equal.
- Reuse/upgrade the existing 48 `ProtobufSerializationTests` as the behavioral floor; add the byte-equality
  layer on top. A *golden corpus* of pre-serialized v3 bytes (committed as fixtures) guards against future
  drift even if the vendored oracle is later removed.
- These tests run on Mono (editmode/playmode) where protobuf-net v3 works; the **runtime** ships only WProto,
  so the IL2CPP standalone leg exercises WProto exclusively → the leg that is red today goes green.

### Migration & rollout

1. ~~**Inventory & freeze** the contract surface.~~ **DONE, session 171.** See
   [Frozen conformance inventory](#frozen-conformance-inventory-session-171).
2. ~~**Wire core** (`WProtoWriter`/`Reader`) + unit tests.~~ **DONE, session 171.** Shipped with
   `WProtoSizes`, `WProtoZigZag`, `WProtoWireType`, `IWProtoFormatter<T>` and the seven attributes, all
   public. Proven byte-exact against the vendored protobuf-net 3.2.56 across **90 differential cases,
   90 matches**, with those bytes committed as a golden corpus in `WProtoWireFormatTests` so the
   guarantee outlives the oracle this migration deletes. Seven mutations were run against the suite and
   all seven fail it.
   ~~**Before step 4, the generator needs a nested-message recursion bound.**~~ **DONE, session 172.**
   `WProtoReader.MaxNestingDepth` is 64, shared with group skipping, and `TryReadMessage` refuses
   past it. Failure propagates through **return values**, not through the outermost reader's
   `Malformed` flag -- a nested refusal never reaches the root, which is what made the first bomb
   test pass with the bound removed.

3. ~~**Hand-written formatters** for the package's own closed types + differential tests green on
   Mono.~~ **DONE, session 172.** `WProtoFormatterProvider` (a static field on a closed generic,
   no `Type`-keyed dictionary, no `MakeGenericType`) plus formatters for `FastVector2Int`,
   `FastVector3Int`, `WGuid` and `RandomState`, each nested in a now-`partial` contract. **644
   differential checks, 644 matches** against protobuf-net 3.2.56, byte-equal plus
   cross-deserialization both ways, with golden vectors committed so the IL2CPP leg -- which cannot
   run the oracle at all -- is covered too. Eleven mutations, all caught after two vacuous tests
   were repaired.

   **Two encoding rules were guessed wrong before the oracle was consulted**, and both are now on
   the conformance checklist: protobuf-net emits in **ascending field number**, not declaration
   order (so `FastVector3Int`, tagged 1/2/**4**/**3**, writes its cached hash before `z`), and a
   **null** `byte[]` is omitted while an **empty** one is written as `2A 00`.

   **Cached derived members are recomputed on read, not trusted from the wire.** For any payload
   protobuf-net could have written the two agree; for a tampered one this refuses to hand back an
   object whose `GetHashCode` disagrees with its `Equals`. The generator must do the same for any
   member the contract derives.

   The wire core carries no length back-patching by design: `WProtoSizes` measures a sub-message so
   its prefix can be written up front, which is why `IWProtoFormatter<T>.Measure` must predict
   `Write` exactly.

   **Still hand-written, not yet ported:** the remaining 61 `[ProtoContract]` types, the 17
   `[ProtoInclude]` PRNG subtypes, the 16 surrogates, and the seven wrapper-marshalled collections.
   Those are step 4's job, not another hand-written batch -- the four ported here exist to prove the
   model, and porting more by hand would be work the generator throws away.
4. ~~**Source generator.**~~ **DONE, session 173.** `Generator~/` holds an `ISourceGenerator` that
   emits a nested `WProtoFormatter` for every `[WProtoContract]`, plus a per-assembly registrar; the
   built DLL ships at `Runtime/Analyzers/` with the `RoslynAnalyzer` label. **27 tests**, 24 of them
   driving the generator's real output (the test project references it as an analyzer, exactly as
   Unity does) and 11 pinning the diagnostics. Five mutations run, all caught after one vacuous test
   was repaired.

   **Three things were measured rather than assumed.**

   *`IIncrementalGenerator` is not an option.* This section specified one. Unity 2021.3 -- in the CI
   matrix -- ships Roslyn 3.9, where that interface does not exist and an analyzer compiled against
   Microsoft.CodeAnalysis 4.x will not load at all. The generator targets 3.8 and the v2
   `ISourceGenerator`, which every host from 3.8 up runs.

   *The consumer story works, and now has a number behind it.* With the analyzer under `Runtime/`,
   Unity ran it over `WallstopStudios.UnityHelpers`, `.Editor`, `.Tests.Runtime`, **`Assembly-CSharp`
   and `Assembly-CSharp-Editor`** -- a marker type emitted into all five. That is the property the
   precompiled-model and v2-downgrade options could never have, confirmed instead of argued.

   *A hook cannot mutate a struct contract.* `IWProtoFormatter<T>` takes `in T`, so calling a
   lifecycle hook on a struct copies it first and every mutation lands on the copy. Silent, and
   invisible from outside. It is `WPROTO010` now, not a footnote.

   **Shipping the binary is its own problem, and the sibling project had already solved it.**
   Review feedback pointed at DxMessaging, whose analyzer-shipping note states the placement
   rule this session had arrived at by measurement -- and whose issue #229 is the mirror image: an
   analyzer under an **editor-only** asmdef reaches no consumer runtime code, because no runtime
   assembly may reference an editor assembly. Three things came back:

   - *Reproducible bytes need two settings, not one.* `ContinuousIntegrationBuild=true` normalizes
     source paths (without it the same sources built from two directories differ, which is what made
     the first byte gate fail) **and** makes SourceLink stamp the git commit into
     `AssemblyInformationalVersion`, so the bytes change every commit unless
     `IncludeSourceRevisionInInformationalVersion` is `false`. Taking one and not the other reddened
     CI twice. Now verified three ways: two commits, a fresh clone at a different path, and the
     shipped DLL against both.
   - *The build binplaces itself.* `AnalyzerPayloadOutputDir` defaults to `Runtime/Analyzers`, so
     building the generator is refreshing the shipped DLL; CI redirects it and the test project sets
     `CopyAnalyzerPayload=false`, since a run that refreshed the artifact it validates could never
     fail.
   - *A placement drift-guard* (`scripts/tests/test-analyzer-placement.js`): label present, every
     platform disabled, governing asmdef not editor-only, and no second copy anywhere Unity imports
     -- a stray build output gets an unlabelled auto-`.meta` and shadows the real analyzer, so the
     generator stops running silently.

   **Deliberately out of scope at that point, each a diagnostic rather than a guess:** a member whose
   type is another contract, and generic contracts. The first was settled in session 174 below;
   generic contracts followed in session 175's step 4h.

4b. ~~**Nested contracts.**~~ **DONE, session 174 (#380).** The question was how a parent produces a
   child's length prefix. With the old writer the only answer was to measure the child a second time
   inside `Write`, which runs the child's before-serialization hook once per enclosing level while
   its after-serialization hook still runs once. **Measured at 4 before-hooks against 1 after-hook**
   for a value three levels down -- so a hook that rents pooled scratch leaks one rental per level,
   which is the exact pattern the hook contract's wording exists to protect.

   `WProtoWriter.TryWriteMessage` now writes the key, the prefix and the payload as one operation,
   reserving the prefix at its **minimum** width and shifting the payload only when the finished
   length needs a wider varint. Every value is measured **exactly once per serialization at every
   depth**, so the hook contract holds verbatim rather than being rewritten, and a sub-message under
   128 bytes moves no bytes at all.

   *The alternatives were measured, not argued* (shared host, so read the ratios, not the ns).
   Back-patching is 1.2-1.5x faster than re-measuring for sub-messages under ~512 bytes -- which is
   every contract this package ships -- and pulls further ahead as depth grows (1.35x at twelve
   levels, where re-measuring is quadratic in depth). It is **2-3x slower above ~8 KB per
   sub-message**, because a wide prefix costs one memmove of the payload per enclosing level while
   re-measuring a bulk member is O(1). Both allocate 0 B/op. That trade was taken deliberately: the
   small case is the per-frame one, the large case is a save. The measured crossover and the
   size-hint optimization that would remove it are recorded in the follow-up issue.

   **Two encoding rules came from the oracle, and one is the opposite of the scalar rule.** A null
   reference sub-message is omitted, but a **struct** sub-message is written even when every member
   equals its default (`12 00`) -- protobuf-net emits it unconditionally. A `Nullable<TStruct>`
   follows `HasValue`, like every other nullable.

   **Lifting the refusal makes a cyclic object graph expressible, and measurement walks the graph.**
   `WProtoSizes.MessageSize` carries the reader's nesting bound and reports passing it by name; there
   is no value it could return instead, because a cyclic message has no finite encoded size, and the
   alternative is a stack overflow, which cannot be caught. The writer carries the same bound for a
   formatter that writes without measuring first.

   **Eight mutations run, all caught.** The first pass was worthless and said so only under
   inspection: restoring the generator's source without rebuilding left the previous mutation live in
   the shipped analyzer, so three later mutations were scored against the wrong binary and one
   genuine survivor -- a faulted nested write reported as success -- was recorded as caught. Lesson
   worth keeping: **a mutation harness that rebuilds an artifact must rebuild it on the restore path
   too**, and a mutation whose failing tests look unrelated to the mutation is the tell.
4c. ~~**Repeated fields.**~~ **DONE, session 175 (#386).** A `[WProtoMember]` may be a single-dimension
   array or any type implementing `ICollection<T>` exactly once with a public parameterless
   constructor and a public `Add` -- `List<T>`, `HashSet<T>`, `SortedSet<T>`, `Collection<T>` and
   consumer types. That closes ~28 of the 192 `[ProtoMember]`s in the inventory, every one of which
   the generator previously refused.

   *Five encoding rules, all measured against the vendored oracle before any code was written, and
   three of them the opposite of the scalar rule.* A repeated field is **unpacked** -- one key per
   element, protobuf-net's default and the reverse of proto3's. **Every element is written**,
   defaults included: a member holding `0` is omitted, an element holding `0` encodes as `08 00`.
   **Null and empty are the same bytes**, so an empty collection with no constructor value behind it
   reads back as `null` -- a silent data change, reproduced deliberately. **Reading appends** to the
   constructor's collection unless `OverwriteList` is set, and an **absent** field leaves it alone
   either way, because nothing distinguishes absent from empty. And a **null element has no
   encoding at all**; protobuf-net raises `NullReferenceException` on one, so the generated code
   raises too, naming the contract and the member.

   *Reading is deliberately more permissive than the oracle, in the one direction that is safe.*
   Every reader accepts the **packed** form for a member it writes unpacked, because protobuf-net
   does. It also accepts unpacked-then-packed for one field, which protobuf-net **refuses** with
   `Invalid wire-type (String)` -- the encoding allows either order, and leniency on read never
   loses data. `WProtoReader.TryReadPackedRun` spends no nesting level: a packed run holds
   primitives with no field keys, so it cannot recurse, and charging it would refuse an array at the
   bottom of a message protobuf-net accepts.

   **A collection is not assumed to be a reference type** (owner directive, #388). Nothing about
   `ICollection<T>` requires a class, and an inline or pooled buffer is a natural struct. Emitting
   `member != null` for one is not redundant, it is `CS0019` -- so presence is a flag rather than a
   null test, the read loop assigns its accumulator back because every `Add` landed on a copy, and
   iteration binds to the concrete enumerator so the struct is not boxed per serialization. Mutation
   M13 -- emit the guard unconditionally -- fails the build.

   *The differential moved out of Unity.* protobuf-net is what does not work under IL2CPP, so a
   differential run in the Unity assembly could only ever execute on the Mono legs. The vendored
   oracle is now referenced by the `Generator~` test project instead: **130+ corpus values, byte-equal
   and cross-decoded both directions**, runnable in three seconds from `dotnet test` and already
   gated by the WallstopProto Generator workflow. The Unity fixture carries golden bytes copied out
   of that oracle, which is what the IL2CPP legs -- which cannot run it -- verify.

   **Seventeen mutations run, all caught.** The harness gained two guards, both from being wrong:
   restoring a file with `mv` gives it the *original* mtime, so MSBuild skipped the rebuild and left
   the last mutation live in the shipped analyzer (found because it reddened four unrelated tests);
   and three anchors went stale during a refactor and were reported as `ANCHOR-FAILED` rather than as
   passes. `touch` on the restore path, plus a post-run baseline assertion, close both.

   **A type that is both a message and a collection is refused, not guessed at (`WPROTO012`).**
   Eight of this package's own contracts are exactly that shape -- `Deque`, `CyclicBuffer`,
   `SparseSet`, `BitSet` and the four `Serializable*` collections all carry
   `[ProtoContract(IgnoreListHandling = true)]` today. Each reading silently discards what the other
   keeps: as a repeated field the `[WProtoMember]`s vanish, as a message the elements do.
   protobuf-net picks list handling and needs a flag to be told otherwise, which is why that flag
   exists on all eight. `[WProtoContract(IgnoreListHandling = true)]` is now honoured, and the
   unflagged case is a build error naming both readings rather than a coin toss over a wire
   contract.

   **Still refused, each with a diagnostic rather than a guess:** map-shaped members (#387 --
   a protobuf map is a repeated *sub-message*, a different encoding), collections with no accessible
   `Add` (`LinkedList<T>`, `ReadOnlyCollection<T>`), collections that are not `ICollection<T>`
   (`Queue<T>`, `Stack<T>`), interface-typed members, and generic contracts (#385).
   *(All of those are served as of session 183 / #395 except a consumer's **own** collection
   interface, which stays refused because protobuf-net's answer to it is a runtime cast failure.)*

   **Review round correction:** a repeated member typed as a **type parameter** silently dropped packed
   runs -- the whole collection came back `null`, which is the "silently short collection" the
   non-generic path was explicitly written to avoid. Packability cannot be decided at generate time, so
   it moved to `WProtoGeneric<T>.Packable`, false when `T` is itself length-delimited (otherwise the
   packed case steals a single string element). Bound measured, not assumed: protobuf-net **refuses** a
   second packed run after a loose element, so the interleaving test asserts packed-then-loose and no
   further.

4d. ~~**`[WProtoInclude]` polymorphism.**~~ **DONE, session 175 (#390).** A member typed as a base
   round-trips as its concrete subtype. Dispatch is a chain of runtime type tests over the declared
   subtypes -- static code IL2CPP compiles like any other, which is the whole point, since
   protobuf-net's answer to the same problem is reflective and is exactly what does not survive that
   compiler. Abstract bases are supported; that is the shape `AbstractRandom` has.

   **This plan had the ordering rule backwards, and the oracle said so.** The line above used to say
   the include lands in ascending field order. It does not: **the include is written before the
   contract's own members, whatever its tag number** -- confirmed twice, once at tag 100 ahead of
   members 1 and 2, and once at tag **3** ahead of members 1 and 5, which is what rules out "large
   tags happen to sort last". Every other member obeys ascending order; includes are the exception.

   **An include names a DIRECT subtype.** protobuf-net refuses a grandchild declared on the
   grandparent with `Unexpected sub-type`, so a deeper type is declared on the type it actually
   derives from. That measurement deleted a depth-sorting mechanism this session had written to
   handle the arrangement the oracle rejects -- and the mutation harness is what exposed it, by
   surviving.

   **The survivor it left behind was the real finding.** A subtype nothing declares reaches its
   nearest *annotated* ancestor's formatter, whose dispatch chain matches it and writes it under that
   ancestor's tag -- a level of type identity gone from saved data with nothing to report it. Every
   non-sealed reference contract now carries a runtime-type guard and refuses by name; a sealed class
   or a struct cannot be subclassed and pays nothing. An unrecognized include tag in a *payload* is
   the opposite case and is still skipped as an unknown field, so a save from a newer build loads.

   **A crash in the oracle, worth recording.** A payload naming two sibling subtypes at once sends
   protobuf-net 3.2.56 into unbounded recursion between `ReadBaseType` and `ReadSubType` and takes
   the process down with a stack overflow, which cannot be caught. Reproduced from a plain
   `Serializer.Deserialize`. This reader takes the last include and cannot recurse -- an untrusted
   save file is attacker-controlled input.

   **Twenty-two mutations run, all caught**, after two survivors and one equivalent mutation were
   repaired. Remaining before the facade swap: map-shaped members (#387), the 16 surrogates (#391),
   and generic contracts (#385).

4e. ~~**Map-shaped members.**~~ **DONE, session 175 (#387).** A `Dictionary<TKey, TValue>`,
   `SortedDictionary<TKey, TValue>` or any single `IDictionary<K,V>` implementation with a public
   parameterless constructor and a settable indexer is written as a protobuf **map**: a repeated
   *entry message*, key at field 1 and value at field 2. That is a different shape from a repeated
   value, which is why a dictionary could not ride the collection path and why the order of the two
   questions in `Member.Create` is itself a wire-format decision.

   *The entry obeys ordinary omission*, which is the rule that would have been guessed wrong:
   `{"a": 0}` encodes as `0A 03 0A 01 61` -- key only -- because the value equals its default. An
   empty-string key is still written; only null is absent.

   *A missing key decodes to the key type's proto default*, and for a string that is `""`, not
   `null`. Measured. Decoding it as null throws `ArgumentNullException` **inside
   `Dictionary<string, V>`** -- an unhandled exception out of a reader handed ordinary bytes, found
   by a test written before the behaviour was assumed. Keys are restricted to what protobuf permits
   (integral, `bool`, `string`); a `byte[]` or message key is refused.

   *A repeated key is last-wins*, applied through the indexer rather than `Add`, which would throw on
   the second occurrence of a key a hostile payload repeated.

   **Twenty-nine mutations run, all caught.** Remaining before the facade swap: the 16 surrogates
   (#391) and generic contracts (#385) -- and #385 is now known to need a **public interface change**,
   see below.

4f. **Generic contracts (#385) need `IWProtoFormatter<T>` to carry a wire type.** The design this plan
   recorded -- "an open-generic formatter whose element access goes through
   `WProtoFormatterProvider.Get<TElement>()`" -- **cannot work**, and the oracle shows why in four
   lines: `Box<int>.Value` encodes as `08 01` (varint), `Box<double>` as `09 …` (fixed64),
   `Box<string>` as `0A …` (length-delimited). **The field key itself changes with `T`.** A single
   emitted `TryWriteTag(1, LengthDelimited)` is wrong for every scalar closure, and `Get<T>()` cannot
   fix it because the interface has no way to say "I am a varint field".

   Emitting one formatter per closed construction *outside* the contract would fix the bytes and lose
   private access -- the property the whole generator is built on. So the remaining option is to give
   the formatter interface a wire type, add real built-in formatters for the scalars, and restate what
   `Measure` means for a non-length-delimited formatter. That is public surface and needs deciding
   rather than assuming; the full argument is on #385.

   **Review round corrections, both measured.** (1) A map **value** that is a contract ran its
   before-serialization hook **twice**: the write path sized each entry up front, and sizing a contract
   calls its `Measure`. The emitter justified this on the grounds that "an entry is two scalars with no
   lifecycle hooks", which `Dictionary<int, SomeContract>` is not. Entries now write the payload first
   and back-fill the length, via `WProtoWriter.TryBeginLengthDelimited`/`TryCloseLengthDelimited` --
   the same reserve-and-shift `TryWriteMessage` already used, extracted rather than duplicated. (2) The
   key filter's comment claimed keys were restricted to what the protobuf **spec** permits; protobuf-net
   does not enforce that, and `float`, `double` and `enum` keys all encode. Our bytes already matched the
   oracle exactly, so the permissiveness stayed and the comment was corrected -- parity with
   protobuf-net is the contract here, not spec conformance. `ExoticKeyContract` pins those bytes.

4g. ~~**Surrogates.**~~ **DONE, session 175 (#391).** `[assembly: WProtoSurrogate(typeof(Real),
   typeof(Surrogate))]` gives a wire shape to a type nobody owns -- Unity's `Vector3`, `Color`,
   `Bounds`. Any member of the real type, plain, repeated or a map value, is written as the
   surrogate and converted back on read. Measured: a surrogated member is **byte-identical** to a
   member of the surrogate type, including `0A 00` for a default struct, so this is a substitution
   rather than a new encoding.

   *The attribute is assembly-level, and that is a design decision rather than a convenience.* The
   real type usually lives in an assembly that cannot reference this one, so it could not carry the
   attribute; and assembly attributes are the one thing a generator can enumerate cheaply across
   **every reference**, which is what lets a consumer's build find the surrogates this package ships.
   Walking every namespace of every reference to find annotated types would cost more than the whole
   generator.

   **The mutation harness caught the property that matters.** Deleting the referenced-assembly scan
   left the whole suite green: every surrogate fixture declared its attribute in the same assembly as
   its contracts, so the cross-assembly path -- the entire point -- was never exercised. The test
   that closes it drives a synthetic **consumer** compilation whose surrogate comes from a reference.
   Without that, a game would have discovered at build time that none of its `Vector3`s serialize.

   **Thirty-two mutations run, all caught.** The 16 registrations in `ProtobufUnitySurrogates` still
   have to be written as `[WProtoContract]` surrogates, but the mechanism they need now exists.

   **Review round correction:** pairs were never validated -- `SurrogateMap.ConvertsBothWays` existed
   and was **dead code**. `WPROTO016` now refuses a surrogate that is not itself a `[WProtoContract]`
   (which otherwise compiles and finds no formatter at runtime) and `WPROTO017` refuses a pair that
   cannot convert both ways (which otherwise surfaces as a cast error inside generated code). Only the
   compilation's **own** attributes are checked, so a consumer is never blamed for a pair they cannot
   edit. All twelve of this package's own pairs were verified against both checks before the gate landed.

4h. ~~**Generic contracts.**~~ **DONE, session 175 (#385).** A `[WProtoContract]` may be generic and
   its members may be typed as its own parameters. That is the shape every one of the package's own
   generic collections has -- `SerializableDictionary<TKey, TValue>`, `SerializableHashSet<T>`,
   `CyclicBuffer<T>`, `Deque<T>`, `SparseSet<T>` -- so nothing else could be ported without it.

   **The design this plan recorded could not have worked, and the oracle said so in four lines.** It
   specified an open-generic formatter routing element access through `Get<TElement>()`. But
   `Box<int>.Value` is `08 01`, `Box<double>` is `09 ...`, `Box<string>` is `0A ...` -- **the field
   key itself changes with the closure**, and `IWProtoFormatter<T>` had no way to say "I am a varint
   field". A single emitted tag constant is wrong for every closure but one.

   **The fix is additive, not breaking**, which was the second correction: a *separate*
   `IWProtoScalarFormatter<T>` carries the wire type, and `WProtoGeneric<T>` -- a closed generic
   IL2CPP compiles ahead of time -- makes the whole per-field decision at the closure. Nothing
   already written changes; `IWProtoFormatter<T>` keeps its shape and `Measure` keeps its single
   meaning.

   **Closed constructions are discovered from source**, because a registrar cannot register an open
   generic and `MakeGenericType` is the one call IL2CPP cannot compile. The generator scans the
   compilation for constructions of each generic contract and registers one formatter each -- which
   is precisely the property that makes a consumer's own `Deque<TheirStruct>` work, and the reason
   this approach was chosen over a precompiled model in the first place.

   **Still refused: a contract nested INSIDE a generic type** (`WPROTO009`, re-purposed). Emission
   would work, but registration would not -- `Holder<T>.Inner` is not itself generic, so there is no
   construction of it to discover, and its formatter would be emitted and never registered. A build
   error beats `Get<Holder<int>.Inner>()` throwing in a shipped player.

   **Thirty-six mutations run, all caught.**

   **Review round corrections.** (1) `IsRequired` never reached a member typed as a type parameter, so a
   required member still dropped its default. What "required" does depends on the closure -- a required
   `int` at 0 is written, a required `null` string is not (both measured) -- so it is passed to
   `WProtoGeneric<T>` as a runtime argument. (2) The closed-construction scan looked only at **direct**
   type arguments, so `Box<Wrapper<T>>` counted as closed and was emitted into a registrar that cannot
   name it -- failing the **consumer's** build. The check is recursive now and also covers arrays,
   pointers and the containing type, since `Outer<T>.Inner<int>` has only closed arguments of its own.

4i. ~~**Immutable contracts.**~~ **DONE, session 175 (#394).** A `readonly` field or a get-only
   property is no longer refused. **Thirty of this package's serialized fields are `readonly`** --
   `FastVector2Int.x`, `Line2D.from`, `ImmutableBitSet._bits`, `RandomState._gaussian` and the rest
   -- so this was the single largest blocker to porting, and it was mis-diagnosed here as needing an
   owner decision between "drop `readonly`", "add setters" and "hand-write the formatter".

   **None of those was necessary.** C# allows a readonly field to be assigned only by a constructor
   of its declaring type -- a nested formatter is not enough -- but the generator *already reopens
   the contract as `partial`*, and a constructor emitted there **is** one. So it emits a private
   constructor taking every member, and the formatter builds the value once the last one is read.
   The type keeps the immutability its author chose, and gains no public surface. A
   `WProtoConstruct` marker as the first parameter makes the signature impossible to collide with a
   constructor the author already wrote.

   *Two consequences, both stated rather than hidden.* A `[WProtoBeforeDeserialization]` hook runs
   **after** construction, because for a type whose members are its construction there is no earlier
   moment -- and nothing is assigned after it, since nothing can be. And immutable members combined
   with `[WProtoInclude]` is `WPROTO015`: one needs the instance built once the last member is read,
   the other replaces the instance when an include arrives, and refusing beats picking.

   **Thirty-eight mutations caught, plus a deliberate control that survived** -- a harmless edit
   added to prove the harness was not simply reporting everything as caught. It was not; it reported
   the control as SURVIVED, and a second genuine survivor alongside it exposed the missing case: an
   immutable **reference** contract with a readonly collection, where the seed dereferences an
   instance that does not exist until construction.

   **Review round correction:** `WPROTO011` still demanded a parameterless constructor from every
   reference contract, including one that builds itself. A contract with a member that cannot be
   assigned after construction never calls `new T()` -- it holds every value in a local and uses the
   constructor the generator emits -- so the canonical immutable class (one parameterized constructor,
   all-`readonly` members) was rejected for a reason that had stopped applying to it. The relaxation is
   gated on `constructAtEnd`, and a mutable class with no parameterless constructor is still an error.

5. **Facade swap** behind a define (`WALLSTOP_PROTO`) for one release: WProto on IL2CPP, protobuf-net on Mono,
   differential-tested equal — then flip default and **remove the runtime protobuf-net DLLs**.

   **The mechanism landed in session 175.** `Serializer.ProtoSerialize` / `ProtoDeserialize` call
   `WProtoFacade`, which serves a type only when a formatter exists for **exactly** that declared
   type and declines otherwise. That makes the swap **opt-in per type**: annotating a contract moves
   it, and everything else keeps working unchanged. Porting is therefore incremental and each type
   is individually verifiable, rather than one change that moves 65 contracts at once and can only be
   tested in aggregate. The subtype case declines deliberately -- a formatter is per declared type,
   and serving a subtype through its base would drop what the subtype declares.

   **Two facade semantics were corrected in the session 175 review round, and both matter for the
   port.** A `null` root now returns the empty payload instead of reaching the generated `Measure`,
   which runs the before-serialization hook as its first statement and dereferenced it (measured:
   protobuf-net writes zero bytes for a null root). And a **failed read now throws** rather than
   declining: "no formatter for this type" and "this type's formatter refused the payload" are
   different answers, and reporting both as "not mine" sent a rejected payload on to protobuf-net --
   which under IL2CPP is the path that cannot run at all, so the real error would have surfaced as a
   reflection failure somewhere unrelated.

   **What is left is porting, not design.** Every generator mechanism now exists and is byte-verified:
   scalars, nested contracts, collections, maps, polymorphism, surrogates, generic contracts. The
   remaining work is adding `[WProtoContract]`/`[WProtoMember]` to the 61 unported contracts, the 17
   `[ProtoInclude]` PRNG subtypes and the 16 surrogate registrations, then flipping the define.

   **Thirty contracts ported, session 176, and the port is now machine-checked rather than reviewed.**
   `None`, `Line2D`, `Line3D`, `Range<T>`, `SerializableNullable<T>`, `SerializableType`,
   `SerializableList<T>`, `DisjointSet`, `BitSet`, `AttributeModification`,
   `PeriodicEffectDefinition`, `SerializableDictionary.Cache<T>`, and `AbstractRandom` with all
   seventeen generators.

   *Three classes of contract are deliberately left alone*, and the reasons are in code rather than
   in this file. A type protobuf-net reaches through a **surrogate** never uses its own contract
   (`Parabola`, `ImmutableBitSet`, `FastVector2Int`, `FastVector3Int`), so annotating it would create
   a second, unreachable wire shape. A type `Serializer` marshals through a **wrapper** is served by
   the wrapper's contract, which is already annotated (`Deque`, `CyclicBuffer`, `SparseSet` and the
   four `Serializable*` collections) -- annotating the original would make `WProtoFacade` answer
   first, with message bytes where the wrapper writes items-plus-capacity. And `WGuid` and
   `RandomState` keep hand-written formatters. **That leaves nothing unported without a stated
   reason**, which is what the two new gates enforce.

   **Two gates replace reviewing sixty contracts by eye.** `ContractMirrorTests` parses `Runtime/`
   and fails when a `[ProtoContract]` has no exactly-corresponding WallstopProto annotation -- same
   field numbers, same `IgnoreListHandling`, same `SkipConstructor`, same includes, same hooks -- or
   when it is not listed with a reason. `PackageContractShapeTests` drives a stand-in for each ported
   contract through protobuf-net 3.2.56 in about a second. The two are linked: annotating a contract
   without adding a stand-in fails the mirror gate, so a port cannot skip saying what its bytes are.
   Both run in the `WallstopProto Generator` workflow, unlike `validate:tests` (#396).

   **Three generator defects, each found by measurement rather than by reading the code.**

   *A subtype serialized under its own declared type wrote only its own members.* protobuf-net always
   writes from the outermost contract in the chain, so `Serialize<Alpha>` and `Serialize<Base>`
   produce identical bytes. Registering the own-members formatter produced the include's payload
   alone, which protobuf-net then read as the **base's** fields -- `AlphaOnly` arriving as `Id`, no
   error anywhere. That is `AbstractRandom`'s exact shape. The entry point registered for a subtype is
   now a formatter delegating to the root of its chain; the own-members formatter stays and is what
   the root reaches for, named directly rather than through the provider, which would now find the
   entry point and recurse forever.

   *`SkipConstructor` was declared, documented and ignored* -- worse than absent, because it reads as
   handled. Five generators carry it and it is load-bearing on every one: the parameterless
   constructor seeds a live generator from a fresh `Guid`, and the hook that rebuilds from the saved
   seed returns early when one already exists, so running the constructor hands back a generator on a
   **random stream** instead of the saved one. The instance now comes from a private constructor
   emitted into the contract's own `partial` declaration, where protobuf-net allocates uninitialized
   through reflection. Field initializers and base constructors still run, which makes the object more
   initialized than protobuf-net's and never less. It is inert on a type that declares no constructor,
   because emitting one there would delete the implicit parameterless one and break `new Theirs()` in
   a consumer's source.

   *A closed generic named only as `new Box<int>()` was never registered.* The type syntax of an
   object creation binds to a constructor, so `GetTypeInfo` returns nothing and the closure went
   undiscovered -- silent at build time, `InvalidOperationException` from the first save in a shipped
   player, and the most natural spelling there is.

   *A closure of a generic contract declared in ANOTHER assembly was never registered at all* --
   found by the Unity leg, which is what the new fixture was added for. The scan only ever looked for
   closures of contracts declared in the same compilation, and a consumer's closure never is:
   `Deque<TheirStruct>` cannot appear in this package's sources, because the struct does not exist
   yet. **That is the property this whole approach was chosen over a precompiled model for**, and it
   was not implemented. The scan now runs from the closures rather than from the references -- asking
   "is the type this construction closes a contract" costs one attribute lookup per constructed
   generic already in the syntax, where walking every namespace of every reference would cost more
   than the generator. Two guards: the formatter must be accessible from the consumer, and it must
   exist, since a reference compiled without the analyzer carries the attribute and no formatter.

   *A subtype its base does not declare with `[WProtoInclude]`* now fails the build (`WPROTO018`)
   instead of throwing at run time.

   **Review round.** The stand-in for `AbstractRandom` declared tags 1-3 where the real base declares
   1-5, so nothing pinned the byte reservoir's encoding -- it passed both new gates and covered less
   than either implied. The instance is fixed, and so is the hole: a stand-in must now declare at
   least the field numbers of every contract mapped to it, a superset rather than an exact match
   because one stand-in deliberately serves many contracts.

   **Verified locally on all three gates**: `npm run typecheck:unity` (the real `Runtime/**` with the
   shipped analyzer), 209 `dotnet test` cases in `Generator~`, and a real Unity 6000.4.6f1 editor via
   the MCP bridge reporting 39 of 39 package assemblies fresh with an empty console.

   ~~**Still to port:** the wrapper-marshalled collections.~~ **DONE, session 179 (#402).** The seven
   have **two** encodings chosen by position -- the wrapper's at the root, an ordinary repeated field
   or map as a member -- and both are in save files that already exist. A **root marshal**
   (`[assembly: WProtoRootMarshal(real, formatter)]`) is that distinction expressed once, served from
   a second registry the member path cannot see, so "root only" is a property of the design rather
   than a rule someone has to remember.

   *The fix stated on the issue was the wrong way round.* Moving `Serializer`'s interception ahead of
   the facade and asking the facade about the *wrapper* routes it through a `MakeGenericType` and an
   `Activator.CreateInstance` first -- the step IL2CPP cannot run. Serving the real type directly
   leaves `Serializer` untouched and deletes the reflection instead of reordering it.

   **Verified end to end.** PR #413 settled at 52 green checks, zero failures: the eight-leg Unity
   matrix, all four gated standalone **IL2CPP** legs, and the new fixture confirmed executed by name
   in `results.xml` on two editors (8991 and 8993 cases, `failed="0"`) rather than inferred from a
   green tick — the editmode legs run only the `Tests.Editor.*` assemblies, so three earlier green
   runs proved nothing about it.

   *Byte-identity is not the contract here, and asserting it wasted four tests.* A packable repeated
   member is written packed and protobuf-net writes it unpacked, per the encoding policy above. The
   differentials pin the bytes literally and assert agreement in **both** directions; the one that
   matters is that the marshal reads what the shipped wrapper path wrote, which is what every save
   file contains.

   **The facade now serves a declared base type, session 177 (#403), and the port was inert without
   it.** `CanServe` required the runtime type to equal the declared one. A generator is held as
   `AbstractRandom` — the declared type this package's own documentation recommends — so all
   seventeen ported generators still took the protobuf-net path on every realistic call, which is
   the path that does not run under IL2CPP. Serving them drops nothing, because the root formatter
   dispatches on the runtime type and writes the whole chain; that is measured against protobuf-net
   3.2.56 rather than argued, at three depths.

   *The question is asked of the formatter, not of a registry.* `IWProtoPolymorphicFormatter.CanWrite`
   is emitted from the same include list the dispatch chain is emitted from, so the two cannot drift,
   and the answer arrives **before** any hook runs — the refusal for an undeclared subtype is an
   exception thrown from inside `Measure`, whose first statement is the before-serialization hook, so
   catching it instead would leave that hook run with no matching after-serialization hook.

   *Three of six entry points had never called the facade at all*, which is the same class of hole
   and was found by looking rather than by a failure: `ProtoSerialize(input, ref buffer)` — the
   allocation-free one a caller serializing every frame uses — `ProtoDeserialize<T>(data, Type)`, and
   every call passing `forceRuntimeType: true`, which asks for exactly the dispatch a generated
   formatter performs.

   **`WALLSTOP_PROTO` is default-on, session 181.** The runtime asmdef derives it from the supported
   Unity version, so both Package Manager and source/`.unitypackage` installs take the hybrid facade
   path without project-wide scripting settings. `npm run typecheck:unity` builds both the shipped
   default and legacy define-off fallback, and a PlayMode regression calls every public Serializer
   shape through a marker formatter.

   **The interface case is closed, session 180 (#403).** A declared type of `IRandom` has no
   members, so nothing about it says which contract answers.
   `[assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]` is that sentence, and
   it states an answer that already shipped -- `ResolveProtobufRootType` resolves `IRandom` to
   `AbstractRandom` by scanning the interface's assembly, so the bytes do not move and the scan is
   no longer needed to find them.

   *It is root-only, like a root marshal, but for a different reason.* A marshal hides from the
   member path because its seven types have two encodings chosen by position. A declared root hides
   because an interface-typed member has no protobuf counterpart for the root adapter's message.
   `WProtoDeclaredRootProvider` is visible to the facade and invisible to `WProtoGeneric<T>`, so a
   declared root cannot escape into a generic member or marshalled collection element.

   *The read side was the blocker and it is answered by two refusals asked before a byte moves*,
   because `TryDeserialize` has no second chance -- a formatter that answers and then rejects throws
   by design. An implementation outside the root's chain is refused by `CanWrite`, which the facade
   already consults on both sides. A consumer who named their own root is refused by `CanServe`:
   `Serializer.RegisterProtobufRoot` now **claims** the declared type in the same registry, and a
   claim outranks a declaration whichever registration ran first -- generated registrars share one
   unordered Unity phase, so last-wins would make the override depend on assembly load order.

   *The mutation that survived is the one worth recording.* Deleting the generator's emission
   entirely left all fourteen tests green, because the precedence test registered the shipped pair
   by hand and satisfied the registrar's own assertion before it ran. A fixture that registers what
   another test asserts was registered proves nothing about either.

   **Step 5's default flip shipped in #420, session 181.** `#414` names closures that will throw,
   `#407` supplies the unported-contract worklist, and `#419` reports cross-assembly root conflicts.
   Local typechecks prove both branches and the release-payload staging gate proves source and UPM
   installs receive the same default. The full PR matrix passed, including every supported Unity editor,
   all four IL2CPP legs, `SINGLE_THREADED`, and export smoke.
6. **CI:** standalone IL2CPP leg green with WProto; the consumer annotation story is documented in
   the README. Performance acceptance and removal of the runtime protobuf-net fallback remain.

### Tracked follow-ups and closeout record

- **#416 — shipped in #420.** Session 179 made root marshals and a generic contract's own
  formatter decline before writing when an element has no WallstopProto formatter. The remaining
  nested case still treated every registered message formatter as serviceable: an outer generic
  contract claimed a closure whose inner formatter declined, then threw during measurement.
  `WProtoGeneric<T>.CanEncode` now propagates the inner conditional service decision at every depth.
- **#419 — shipped in #420.** `WPROTO031` reports different declared roots across assemblies,
  including a conflict that exists entirely between two references. The warning names the declared
  type, both roots, and both assemblies; identical declarations remain quiet.
- **#407 — shipped in #420.** `WPROTO030` marks each protobuf-net contract that has not gained a
  WallstopProto contract as informational by default, supports promotion through a Unity ruleset or
  `.editorconfig`, and can be suppressed on a deliberately legacy declaration.
- **#394 — shipped in #420 across sessions 176-181.** Every shipped protobuf
  contract is now either mirrored by a generated contract or recorded as one of twelve intentional
  surrogate/root-marshal/hand-written formatter exceptions. `ContractMirrorTests` prevents either
  list from drifting, and `WPROTO030` supplies the consumer migration worklist needed before the
  default flip.
- **#415** — the watchdog global-mutex host failure remains tracked, but it did not recur on the
  exact-head main audit for session 181: all eighteen Unity workflow jobs passed. Treat a recurrence
  as runner infrastructure unless a job reaches repository checkout.
- **#393** — hit again in session 177, on `RuntimeSingletonTests` this time. A second, unrelated
  fixture rules out the "reduce the frame count" option and makes the shared-handling option the
  indicated one; the awkward part is that `LogAssert.Expect` cannot express a nondeterministic
  message, so what has to be argued is the *scope* of `ignoreFailingMessages`, not a pattern.

- **#395 — implemented in session 183, pending merge.** `LinkedList`, `Queue`, `Stack`,
  `ReadOnlyCollection`, `ReadOnlyDictionary` and every interface-typed sequence or dictionary member
  are served. The three problems that shared `WPROTO003` are three fields on a `CollectionForm`
  rather than three branches: a per-type fill method, an accumulate-and-construct commit, and the
  concrete implementation an interface resolves to. What a consumer's member **holds** after a round
  trip is treated as part of the contract and matches protobuf-net -- `List<T>` for the sequence
  interfaces, `HashSet<T>` for `ISet<T>`, `Dictionary<K,V>` for both dictionary interfaces.

  *The v2/v3 collection matrix is the second measured major-version divergence, after session 182's
  map defaults.* protobuf-net **2.4.9 has no serializer at all** for `Queue<T>` or `Stack<T>` -- its
  model build throws, so one such member poisons a whole contract -- and it writes `ISet<T>` and
  `IReadOnlyDictionary<K,V>` and then throws reading either back. **3.2.56** refuses to read a
  `ReadOnlyCollection<T>` or `ReadOnlyDictionary<K,V>` it wrote itself. WallstopProto serves all of
  them on both, so the differential fixtures are split along the measured lines rather than along
  what looks tidy, and the shapes with no oracle are pinned by golden bytes.

  *A consumer's own collection interface stays refused*, and that is now a measured decision: both
  majors write it and then throw `InvalidCastException` on read, because they fill a `List<T>` that
  is not one. A build error naming the member beats a cast failure in a shipped player.
  `IReadOnlySet<T>` has the same failure in protobuf-net and is **not** refused here, because
  `HashSet<T>` satisfies it.

  **The gap worth recording:** the fixture that covers a contract with an include and one built by a
  constructor did not exist for any collection form whose commit is not an assignment. Writing it
  found an older defect immediately -- the map reader's entry-value local was named `valueN`, which
  is what an immutable contract calls the local holding member N, so **any** immutable contract with
  a dictionary member failed the consumer's build with `CS0136`. A second and third path through the
  same emitted code is worth a fixture even when the first one is thoroughly covered.
- **#388 — implemented in session 183, pending merge.** The generator's map path made the same
  reference-type assumption the repeated path had already been fixed for: it accepted a struct
  dictionary and then emitted `member != null` and `read.Member ?? new Member()` for it, both
  `CS0019` inside code the consumer never wrote. `SerializableSetBase<T, TSet>`'s `where TSet : class`
  is relaxed, and the two places it was load-bearing use `is null`, which a value type answers as a
  constant false. The remaining audit found nothing: the facade's collection interception dispatches
  on concrete types rather than interface tests, and the JSON converters carry no `class` constraint.

  **The lesson the existing test missed.** `ACollectionImplementedAsAStructIsAcceptedLikeAnyOther`
  asserted only that the generator reported no diagnostic, while its own comment said the failure
  mode is "code that does not compile" -- and the generator reports nothing at all about `CS0019`.
  It compiles the generated source now, which is what the map version was written to do from the
  start and what caught the defect.
- **#399 — measured in session 183, and it is an owner decision rather than an implementation.**
  The issue asks for arbitrary-dimension arrays, jagged arrays and nested collections
  (`int[][]`, `int[,]`, `List<int[]>`, `List<List<int>>`). **Neither protobuf-net major supports any
  of them**, measured against both vendored oracles: 3.2.56 refuses with
  `NotSupportedException: Nested or jagged lists, arrays and maps are not supported`, 2.4.9 with the
  same message plus a separate one for multi-dimensional arrays. Both refuse at *write*, so there is
  no payload either can produce and none it could be asked to read.

  That matters because it takes the question out of the encoding-policy rule above. The directive is
  "interoperate with protobuf-net, do not imitate it", and it is enforced by requiring that any
  divergence be measured to *read* on the other side. **That check cannot be satisfied here at all**:
  supporting these shapes means inventing a wire contract this package alone owns, most naturally a
  repeated *wrapper sub-message* per inner collection, which is what a proto3 schema would use. So
  `WPROTO003` on `int[][]` and `List<List<int>>` is currently the **correct** answer rather than a
  gap, and it is pinned by `AnUnsupportedMemberTypeIsAnError`.

  `byte[][]` and `List<byte[]>` are the exception and already work on both sides, because `byte[]` is
  a length-delimited scalar rather than a repeated field -- which is exactly why the generator tests
  `Shape.IsByteArray` before anything else.

  **What #399 needs next is a decision, not code:** whether this package owns an encoding protobuf-net
  cannot read. The rest of the issue (every stdlib type, and the contract surveys of the wallstop and
  Ambiguous-Interactive repositories) is independent of that decision and can proceed either way.
- **#371 — implemented in session 182, pending merge.** CI runs all generator differentials once
  against protobuf-net 2.4.9 and once against 3.2.56 in isolated processes. The v2 run exposed and now
  pins the three major-version divergences described above instead of silently treating v3 as both oracles,
  and fixed WallstopProto's `null` result for v2's omitted empty string map value.
- **#392 — configure-pass mitigation implemented in session 182, pending merge.** A configure invocation
  with no fresh success marker retries once only when its log carries the known UPM cancellation/IPC
  signature. It attempts to preserve the first log and clears UPM state; a fresh marker wins, and ordinary compile
  failures remain single-attempt failures.

### Performance goals

These remain release-level acceptance work, not evidence claimed by the default-on or dual-oracle
sessions. Measure them through the performance asmdef and Unity Profiler MCP in a licensed editor.

- Warm throughput ≥ protobuf-net v3 (generated code, no per-call model lookup beyond a static field read).
- Cold start ≫ better (no model build / no ref-emit).
- Zero allocations on the write path for `Span`/`IBufferWriter` overloads; pooled scratch for length
  back-patching. Validate with the existing performance test asmdef + the Unity Profiler MCP.

### Risks & mitigations

- **Subtle wire mismatches** (default omission, packed/unpacked, bcl scale) → the byte-level differential
  suite + golden corpus catch every one before release; develop step 3 against the oracle, never blind.
- **Generator complexity / Unity analyzer packaging** → validate the `RoslynAnalyzer` round-trip early
  (a trivial generated type) on all three Unity versions (2021/2022/6000) before building the real generator.
- **Scope creep** (AsReference/DynamicType/gRPC) → explicitly out of scope; assert none are used; fail the
  build if a contract uses an unsupported feature (generator diagnostic).
- **Maintenance** of a serializer → bounded: the wire format is frozen (Level 200), the corpus pins behavior,
  and we own the AOT story end-to-end instead of tracking an upstream that has no IL2CPP path.

### Effort & sequencing

Large (multi-week). Independent of the test-runtime/PlayMode tracks; lands on its own branch. Ship value
incrementally: steps 1–3 already deliver an IL2CPP-safe serializer for the package's own types (covers the
player-data case for built-in contracts); steps 4–5 generalize to consumer types and remove the dependency.

---

## DxKit Rebrand: Unity Helpers → DxKit

**Status: NOT STARTED.** New workstream, independent of the release/test/serializer tracks above;
lands on its own branch. This is a **display-name + brand** change — the package id
`com.wallstop-studios.unity-helpers`, root namespace `WallstopStudios.UnityHelpers`, Unity min
`2021.3`, and release-version continuity are **invariants**; the rebrand must use the current manifest
version rather than resetting or hard-coding one, and existing projects upgrade with zero code changes.

### Context

`Unity Helpers` is being rebranded to **DxKit** ("DxKit — Developer Toolkit for Unity"), joining the
`Dx` product family (DxMessaging, DxCommandTerminal). The rebrand spans four surfaces: (1) brand
identity + copy, (2) the GitHub Pages docs/landing site, (3) the in-editor tool UIs, and (4) store /
README brand assets. The design source of truth is the five canvases in `design-system/`:
`DxKit Brand Foundation`, `Identity & Messaging`, `Landing`, `Editor Tools`, `Brand Assets` (`.dc.html`).

Alongside the visual work, the copy must shed "LLM-isms" — emoji section markers, hype adjectives,
and manufactured headings — in favor of a **technical, precise, numbers-first voice**. The current
the [README](./README.md) is the worst offender (90+ emoji; "Professional" three times; "Hidden Gems Worth
Discovering"; "Why Teams Choose Unity Helpers"; "Batteries-Included"), and the design system's own
copy has a few internal contradictions and one bad number to fix.

### Locked owner decisions

| Decision | Choice |
| --- | --- |
| Editor-tools redesign | **Full UI Toolkit rewrite** — all 15 IMGUI windows + Project Settings page + all inspector drawers, under one DxKit theme |
| Landing page | **MkDocs home** — Material custom homepage template (`docs/overrides/home.html`) + DxKit-themed `custom.css`; reuse Material's built-in light/dark toggle |
| "10-15x faster RNG" claim | **Run the CI benchmark, cite real captured numbers** (per-RNG multiplier); currently unsubstantiated in-repo |
| Canonical org/URL | **`Ambiguous-Interactive/unity-helpers`** everywhere (repo + Pages + package.json); rewrite README/mkdocs/design-system links `wallstop` to `Ambiguous-Interactive` |
| Package id / namespace | **Unchanged** |

### Data-backed number ledger (verified against the codebase)

Use these substantiated numbers in all copy. `scripts/sync-doc-counts.ps1` /
`generate-doc-metadata.ps1` machine-checks tests/PRNGs/tools counts — feed rebrand copy through it;
do not hand-write counts it manages.

| Claim | Verdict | Use in copy |
| --- | --- | --- |
| Tests | CONFIRMED, 11,632 attributes across 1,024 files | **11,000+** (sync-managed) |
| Extension methods | CONFIRMED, 263 | **200+** (sync-managed) |
| Editor tools | 17 windows / 22 MenuItems | **20+** (defensible on MenuItems; sync-managed) |
| Serialization converters | **OVERSTATED**, real 46 custom (47 registered); 46 files in `Runtime/Core/Serialization/JsonConverters/` | **46** (fix all "49" occurrences) |
| Seedable RNGs | CONFIRMED, 17 `IRandom` + 1 struct = 18 | **15+** (sync-managed; could raise) |
| 10-15x faster RNG | **UNSUBSTANTIATED in-repo** (`perf-results/baseline.json` is an empty `"metrics": []` stub) | Replace with **captured CI number** (W7) |
| Spatial trees | CONFIRMED, `QuadTree2D`, `KDTree2D/3D`, `RTree2D/3D`, `OctTree3D` | keep |
| BitSet/Trie/Heap/ring buffer | CONFIRMED (`CyclicBuffer` = ring buffer) | keep |
| Relational + VContainer/Zenject/Reflex | CONFIRMED (`Runtime/Integrations/`) | keep |
| Odin | "similar to / migration guide", not full compat | keep as-worded; never say "full Odin compatibility" |

### Design tokens (single source of truth for every surface)

**Dark (default):** `--bg #0d1117`, `--bg2 #0a0e13`, `--panel #11161d`, `--panel2 #0c1016`,
`--border #222b36`, `--border2 #1d2531`, `--text #e9eef4`, `--muted #9aa3af`, `--faint #6b7480`,
`--accent #ec4661` (ember), `--accent-ink #0d1117`, `--amber #f4a836`, `--hair #1b222d`.
Syntax: str `#7fb88a`, type `#7fa6d8`, kw `#6b7585`, fn `#f4a836`, com `#5a6573`, attr `#ec4661`.

**Light ("Press"):** `--bg #f4f1ea`, `--panel #ffffff`, `--text #1b1813`, `--muted #5f5a4f`,
`--accent #c5374c`, `--amber #b07d1f` (full light map in `DxKit Landing.dc.html` `themes.light`).

**Type:** Space Grotesk (display), IBM Plex Sans (body), JetBrains Mono (code).

**Logo — "Signal" mark:** hexagon ember outline (`#ec4661`, stroke ~4.5) enclosing a smaller solid
amber hexagon (`#f4a836`) with a dark center dot. Canonical SVG `design-system/dxkit-banner.svg`;
inline variants across the `.dc.html` files (nav/footer/icon use `viewBox="0 0 96 96"`).

### Voice and copy rules (the right-sizing spec)

**Cut** (LLM-isms, all present in the current [README](./README.md)): emoji section markers; "Professional" /
"Professional-Grade"; "Hidden Gems Worth Discovering"; "Why Teams Choose Unity Helpers";
"Batteries-Included Extensions"; "Top Time-Savers"; "First Time Here?"; "Math That Should Be
Built-In"; "supercharge"; "production-ready"; and the swagger tagline **"The utilities Unity left
out."** Reduce gratuitous em-dashes in prose; keep middot separators only in UI chrome (mono-labels),
not body text.

**Keep / adopt:** state what it does, show the call, give the number. Canonical positioning headline
**"A Unity toolkit, built over years of shipping games."**; sub **"The systems you stop rewriting
every project."** "Easy to use, and fast." is endorsed (design "Ship" column) — keep.

**Right-size the design-system `.dc.html` copy too** (it preaches the voice but breaks it in places):

1. `DxKit Brand Foundation.dc.html` still uses **"The utilities Unity left out."** as the hero
   tagline (lines 88/129/169) — `Identity & Messaging` section 04 explicitly lists it under **Cut**.
   Resolve every surface to the locked positioning headline.
2. Stale **v3.1.0** in mocks is synchronized from `package.json` (currently **3.5.1**) across Brand
   Foundation, Landing nav, and Brand Assets badges.
3. **"49 built-in converters"** (Identity Pillar 3, Landing feature grid) becomes **46**.
4. Pillar-7 menu string "Tools > Wallstop Studios > Unity Helpers" becomes "Tools > Wallstop Studios
   > DxKit" (see menu-path rename, W4).
5. Trim filler: "Every piece earns its place by saving real work. Here's what you reach for most."
   (Landing) and "just calmer and easier to read" (Editor Tools subtitle) become tighter factual lines.
6. Keep the package id in `openupm add com.wallstop-studios.unity-helpers`; switch repo links to
   `Ambiguous-Interactive/unity-helpers`.

### Workstreams

The rebrand runs as 8 workstreams. W0-W2 and W6 are copy/config (low risk, land first). W3 (site)
and W5 (assets) are visual. **W4 (editor UI) is the dominant effort.** W7 (benchmarks) unblocks the
RNG number. W8 is the standing guardrail. Every task follows the [AI agent guidelines](.llm/context.md): format/lint after
each file, `.meta` for every new asset, `using` inside namespace, no `var`/`#region`/nullable-refs,
XML `<summary>` on public members, CHANGELOG for user-facing changes, `npm run agent:preflight:fix`
before staging, tests for every code change. **Never `git add`/`git commit`** — owner stages.

#### W0 — Right-size the design-system copy (source cleanup)

Apply the six fixes in "Voice and copy rules" to the five `design-system/*.dc.html` files so the
brand source is internally consistent before it seeds downstream copy.

- Files: `design-system/DxKit Brand Foundation.dc.html`, `DxKit Identity & Messaging.dc.html`,
  `DxKit Landing.dc.html`, `DxKit Editor Tools.dc.html`, `DxKit Brand Assets.dc.html`.
- **Red-Green:** grepping the design-system dir shows **zero** occurrences of "The utilities Unity
  left out", "v3.1.0", "49" (converters), and "wallstop/unity-helpers"; the positioning headline
  appears consistently; converters read 46.
- These are `.dc.html` design canvases (not shipped) — no Unity `.meta` churn beyond existing.

#### W1 — Brand foundation: tokens, mark, cspell, banner-sync contract

1. **Register brand vocabulary** in `cspell.json` (`package-terms`): `DxKit`, `dxkit`, `Dx`,
   `DxMessaging`, `DxCommandTerminal`, plus any new tokens (`Archivo`, `Sora` only if used). Without
   this, every doc/code touch fails spelling CI (a PostToolUse cspell hook fires on each edit).
2. **Canonical SVG mark:** promote `design-system/dxkit-banner.svg` (800x200) to the shipped banner.
   **Rename the shipped asset to `docs/images/dxkit-banner.svg`** (filenames currently bake the old
   name). This touches the banner-sync contract — see task 3.
3. **Preserve the version-embed contract.** `scripts/sync-banner-version.ps1` +
   `.github/workflows/sync-banner-version.yml` require the banner SVG to contain the exact
   `package.json` version and validate on push. The DxKit banner currently renders v3.1.0
   and must render the synced version token in a form the script's regex updates. Steps:
   - Read `scripts/sync-banner-version.ps1` regex; ensure the new SVG's version string matches it.
   - Update the hardcoded path `docs/images/unity-helpers-banner.svg` to `dxkit-banner.svg` in
     `sync-banner-version.ps1`, `sync-banner-version.yml` (paths trigger + validation), and
     `.github/workflows/release-prepare.yml` (the `git add` of the banner).
   - Update the banner entry in `scripts/sync-doc-counts.ps1` `$targetFiles`.
4. **Icon/favicon source:** export the 96-viewBox mark as `docs/images/dxkit-icon.svg` for mkdocs
   `theme.logo`/`favicon` and README/store use.
- **Red-Green:** `pwsh scripts/sync-banner-version.ps1 -Check` passes on the renamed banner;
  `npm run lint:spelling` passes with `DxKit` present in a doc; `git grep -l unity-helpers-banner`
  returns only historical/artifact copies.

#### W2 — Copy rebrand: package metadata, README, docs prose, count-sync

1. **`package.json`:** `displayName` "Unity Helpers" to "DxKit"; `description` "Treasure chest of
   Unity developer tools" to "Developer Toolkit for Unity". Leave `name`, `version`, `unity`,
   `author`, and the already-canonical `Ambiguous-Interactive` URLs. Sample `displayName`s
   ("DI - VContainer", etc.) unaffected.
2. **[README](./README.md)** — rewrite to the DxKit voice (biggest copy file, ~59 KB):
   - H1 "Unity Helpers" to DxKit banner + "DxKit"; replace AI-disclosure/marketing intro with the
     positioning headline + sub + install one-liner + trust strip (11,000+ tests, zero deps, IL2CPP
     and WebGL, 200+ ext methods, OpenUPM/NPM/Git). Model section order on `DxKit Landing.dc.html` /
     `Identity & Messaging`.
   - Delete emoji headings; rename sections: "Top Time-Savers" to "What's inside"; "Hidden Gems" and
     "Why Teams Choose" removed/merged into a factual "What's inside" grid; "Batteries-Included
     Extensions" to "Also in the box". Keep Install / Compatibility / Core Features / DI Integrations
     / Performance / Documentation Index / Contributing / License.
   - Keep every code sample and real number; fix 49 to 46; RNG multiplier becomes the W7 number.
3. **Docs prose** under `docs/` — retitle and de-LLM the mirrored surfaces: the
   [documentation landing page](./docs/index.md) (front-matter `title`, banner ref, tagline, feature-grid),
   the [README mirror](./docs/readme.md),
   `docs/overview/*`, `docs/features/*`. Update `site_name`, `site_description`, and
   `_config.yml title/description/logo` (Jekyll legacy — see W3 for keep/remove).
4. **`llms.txt`** — rewrite header to DxKit; it's in the count-sync target list.
5. **Run `pwsh scripts/sync-doc-counts.ps1`** after copy lands so counts + the banner re-stamp from
   the codebase. Then `npm run lint:doc-counts` must pass.
- **Red-Green:** scan the [README](./README.md) and `docs/` for
  `hidden gems|why teams choose|supercharge|production-ready|batteries` and require 0 hits;
  `rg "\b49\b.*converter" -i` returns 0; `npm run lint:doc-counts`, `lint:spelling`,
  `lint:markdown`, `lint:docs` green.

#### W3 — Docs site overhaul (MkDocs Material to DxKit)

Live site = **MkDocs Material** (`deploy-pages.yml` runs `mkdocs build --strict`; `site/` uploaded).
Jekyll (`_config.yml`, `Gemfile`, the [legacy root index](./index.md), `_includes/`, `assets/`) is **orphaned**, and the
`docs:serve`/`docs:build` npm scripts still call dead `bundle exec jekyll`.

1. **Retheme `mkdocs.yml`:** `site_name` to DxKit; palette `primary/accent` teal/deep-orange to
   custom (Material's named palette can't hit `#ec4661`, so drive colors via `custom.css` variables
   and set scheme `slate` default + `default` light toggle); `theme.logo`/`favicon` to
   `docs/images/dxkit-icon.svg`; `font.text: IBM Plex Sans`, `font.code: JetBrains Mono` (Space
   Grotesk display font injected via CSS `@font-face` in `custom.css`, since Material has only
   text+code slots).
2. **DxKit-theme `docs/stylesheets/custom.css`:** define `--md-primary-fg-color` etc. mapped to the
   token palette for both `[data-md-color-scheme="slate"]` (dark) and `default` (light "Press");
   style admonitions/code/nav to match panels/borders.
3. **Custom homepage:** add `docs/overrides/home.html` (Material supports per-page `template:`
   front-matter). In the [documentation landing page](./docs/index.md), set `template: home.html` and rebuild the landing from
   `DxKit Landing.dc.html`: hero (headline + sub + install copy-field), code window (`Player.cs`
   sample), trust strip, logging spotlight, 6-card feature grid, "Also in the box", install cards
   (OpenUPM recommended / Git URL / NPM / Source), footer. **Reuse Material's built-in palette
   toggle** instead of the mock's bespoke JS. Inline SVG mark; no external assets beyond fonts.
   `docs/overrides/` is currently empty — this is where the template lands.
4. **Reconcile Jekyll:** recommend **removing** the orphaned Jekyll stack (`_config.yml`, `Gemfile`,
   the [legacy root index](./index.md), `_includes/`, `assets/css/`) and the stale `docs:serve`/`docs:build` scripts, or
   (if kept for the org root page) re-theme + rebrand it. Removal is cleaner; confirm nothing else
   consumes it. `_config.yml` is in the count-sync target list — drop it there if removed.
5. **Wiki mirror** (`deploy-wiki.yml`) inherits docs changes automatically — verify after.
- **Red-Green:** `mkdocs build --strict` succeeds; local `mkdocs serve` shows DxKit palette + landing
  home + working light/dark toggle; `validate-docs.yml` green; no `wallstop/` link survives (W6).

#### W4 — Editor UI redesign (full UI Toolkit rewrite) — dominant effort

Current: 17 windows (15 IMGUI, 2 UI Toolkit), a ~4200-line IMGUI Project Settings page, and all
inspector drawers IMGUI. No shared theme; **no Hub launcher exists**; "Singleton Creator" is a
headless utility with no window. Only token infra today is
`Editor/Styles/DropDowns/WDropdownVariables.uss` + `Editor/Styles/WDropDownStyleLoader.cs` — the seed
to generalize.

**W4.0 — DxKit editor theme foundation (do first; everything depends on it):**

- `Editor/Styles/DxKit/DxKitTokens.uss` — CSS custom properties for the full token palette, dark
  (default) + light blocks keyed off `EditorGUIUtility.isProSkin` (mirror `WDropdownVariables.uss`).
  Add `DxKitControls.uss` for shared control classes (panel, tab strip, mono-label, primary/ghost
  button, toggle, code block, checkerboard preview, danger zone).
- Generalize `WDropDownStyleLoader` to `Editor/Styles/DxKit/DxKitTheme.cs`: `Apply(VisualElement)` +
  `ClassNames` constants + font loading (fonts under `Editor/Styles/DxKit/Fonts/` with `.meta`).
- Base window `Editor/Tools/DxKit/DxKitEditorWindow.cs : EditorWindow` — `CreateGUI()` builds root,
  applies theme, provides the standard tab-strip/title chrome from the mock.
- **Reuse (do not reinvent)** UI-agnostic logic already extracted:
  `Editor/CustomDrawers/Utils/ShowIfConditionEvaluator.cs`, `EnumToggleButtonsShared.cs`,
  `DropDownShared.cs`, `ValidationShared.cs`, `InLineEditorShared.cs`,
  `Editor/Utils/WButton/WButtonColorUtility.cs`, `WButtonInvocationController.cs`,
  `WButtonMetadataCache.cs`, `WButtonStateRepository.cs`. Rewrites replace only the IMGUI drawing.
- Template: `.llm/code-samples/editor/EditorWindowTemplate.cs`.

**W4.1 — Hub launcher (net-new flagship, from `DxKit Editor Tools.dc.html`):**

- `Editor/Tools/DxKit/DxKitHubWindow.cs` — tabbed launcher: search field ("Search N tools", Cmd+K),
  category groups (SPRITES & TEXTURES / ANIMATION / ATLAS + VALIDATION + AUTOMATION), tool cards
  (icon, name, one-line desc) that open each tool.
- **Menu-path rename** (breaking UX): `Tools/Wallstop Studios/Unity Helpers/...` to
  `Tools/Wallstop Studios/DxKit/...` across all 22 `[MenuItem]` entries; add
  `Tools/Wallstop Studios/DxKit/Hub`. Keep old paths as aliases for one release. Confirm with owner
  if muscle-memory continuity matters.

**W4.2 — Rewrite windows to UI Toolkit (grouped by design fidelity):**

Flagships mocked in the design (highest-fidelity target): **Sprite Cropper**
(`Editor/Sprites/SpriteCropper.cs` — before/after checkerboard preview, padding/threshold, danger
zone), **Animation Event Editor** (`Editor/AnimationEventEditor.cs` — timeline + markers + events
table), **Animation Creator** (`Editor/Sprites/AnimationCreatorWindow.cs` — regex parse, clip rows),
**Sprite Atlas Generator** (`Editor/Sprites/ScriptableSpriteAtlasEditor.cs` — source/packing/scan).
Then remaining IMGUI windows: `SpriteSheetExtractor` (largest, ~221 IMGUI calls — budget
accordingly), `SpritePivotAdjuster`, `SpriteSettingsApplierWindow`, `TextureSettingsApplierWindow`,
`TextureResizerWizard`, `FitTextureSizeWindow`, `ImageBlurTool`, `PrefabChecker`, `AnimationCopier`,
`UnityMethodAnalyzerWindow`, `MultiFileSelectorPersistenceWindow`. The 2 existing UI Toolkit windows
(`AnimationViewerWindow`, `SpriteSheetAnimationCreator`) get re-skinned to the shared theme (drop
their isolated USS in favor of `DxKitTokens`).

- New window **Singleton Creator** (wrap the headless
  `Editor/Utils/ScriptableObjectSingletonCreator.cs`) if the design's card is to be actionable.
- **Project Settings page** `Editor/Settings/UnityHelpersSettings.cs` (~4200 lines IMGUI) to UI
  Toolkit `SettingsProvider` themed to DxKit. Large; schedule near the end. Keep `SettingsScope`/path
  stable to not orphan saved prefs.

**W4.3 — Inspector drawers to UI Toolkit (`CreatePropertyGUI`/`CreateInspectorGUI`):**

Rewrite attribute rendering to themed UI Toolkit matching the "Inspector Attributes" mock (WGroup
foldout, WEnumToggleButtons grid, WShowIf indented reveal, WButton):
`Editor/CustomDrawers/WShowIfPropertyDrawer.cs`, `WEnumToggleButtonsDrawer.cs`,
`Editor/CustomEditors/WButtonInspector.cs` + `Editor/Utils/WButton/WButtonGUI.cs`,
`Editor/Utils/WGroup/WGroupGUI.cs`, and related drawers (`SerializableDictionary/Set/Nullable/Type`,
`IntDropDown`, `StringInList`, `WValueDropDown`, `WGuid`, `WNotNull`, `ValidateAssignment`,
`WInLineEditor`). Keep the Odin bridge drawers working (Odin path stays IMGUI unless separately
scoped). Reuse the `Utils/*Shared` logic classes unchanged.

**W4 verification (red-green, science-driven):**

- Attributes/logic unchanged in `Runtime/`, so existing 11,000+ tests stay green; run
  `bash scripts/unity/run-tests.sh --mode all` after each window/drawer batch.
- Where a rewrite risks behavior drift, write a **characterization test first** (red) against current
  behavior, then rewrite to green. New windows/drawers get UI Toolkit smoke tests under `Tests/Editor/`.
- **Visual QA via MCP:** capture each rewritten window with the Unity MCP editor and compare against
  the mock. MCP verification of `.cs` changes needs a clean rebuild (incremental recompile goes
  stale) — use `CleanBuildCache` before capture.
- `.meta` for every new `.uss`/`.uxml`/`.cs`/font (`./scripts/generate-meta.sh` then
  `npm run agent:preflight:fix`); `.asmdef` refs verified; `dotnet tool run csharpier format .`.

#### W5 — Brand assets (README/store) + store metadata

From `DxKit Brand Assets.dc.html`, produce shippable assets (SVG preferred; export PNG where stores
require raster):

- README banner **800x200** to `docs/images/dxkit-banner.svg` (W1; version-synced).
- Store icon **160x160** to `docs/images/dxkit-icon.svg` (rounded-rect + mark).
- Store card **420x280** (listing thumbnail) and store cover/key-art **1950x1300** under
  `docs/images/store/` with `.meta`. Cover embeds the `Enemy.cs` sample + log lines from the mock.
- Rename old-name-baked asset files (`docs/images/editor-tools/project-settings-unity-helpers.png`,
  `docs/images/inspector/unity-helper-settings.png`, etc.) and update every reference; regenerate the
  editor-tool screenshots after W4 lands (they show the new UIs).
- **Red-Green:** README + docs render the DxKit banner; `npm run lint:docs` (link check) green; no
  dangling image refs.

#### W6 — Org/URL canonicalization (`wallstop` to `Ambiguous-Interactive`)

`package.json` already uses `Ambiguous-Interactive/unity-helpers` + `ambiguous-interactive.github.io`
(keep). Everything else diverges to `wallstop/...`:

- Rewrite repo links `github.com/wallstop/unity-helpers` to
  `github.com/Ambiguous-Interactive/unity-helpers` in the [README](./README.md), `mkdocs.yml` (`repo_url`,
  `repo_name`, and `magiclink` `user: wallstop`/`repo: unity-helpers`), all `docs/`, `llms.txt`, and
  the design-system `.dc.html` (Landing nav/footer/GitHub links, Git-URL install string).
- Keep OpenUPM/NPM ids on the package id `com.wallstop-studios.unity-helpers` (unchanged).
- Ops (outside code, owner action): the actual GitHub repo transfer/redirect and Pages source; add a
  redirect note if the repo physically moves.
- **Red-Green:** `rg -n "wallstop/unity-helpers" --glob '!node_modules' --glob '!.git'` returns 0
  hits (outside historical `.artifacts/`/worktree mirrors); `npm run lint:docs` green.

#### W7 — Substantiate the RNG performance claim (data-backed)

The "10-15x faster than `UnityEngine.Random`" line has no committed evidence
(`perf-results/baseline.json` is `"metrics": []`). Benchmark harness:
`.github/workflows/unity-benchmarks.yml` + `scripts/unity/lib/render-perf-deltas.js`.

- Trigger the benchmarks workflow (or run the bench locally via the Docker Unity path) to produce
  real per-RNG numbers vs `UnityEngine.Random`; commit the captured `perf-results` output.
- Derive the honest headline multiplier from measured data (per-RNG range). Update the [README](./README.md),
  [random-generator guide](./docs/features/utilities/random-generators.md),
  [documentation landing page](./docs/index.md), `llms.txt`, and the design-system
  copy with the measured figure. If the measured range differs from 10-15x, the copy changes to match
  the data — the number follows the benchmark, not the other way around.
- **Red-Green:** `perf-results/` contains non-empty metrics; every RNG-speed claim in copy traces to
  a committed number.

#### W8 — Verification, guardrails, and sequencing

**Standing checks after every change (per the [AI agent guidelines](.llm/context.md)):** `dotnet tool run csharpier format .`;
`node scripts/run-prettier.js --write -- <file>`; `npm run lint:spelling`; `npm run lint:markdown`;
`npm run lint:docs`; `npm run lint:yaml`; `npm run lint:doc-counts`; `pwsh scripts/lint-tests.ps1`;
`npm run agent:preflight:fix` before staging; `npm run validate:prepush` before push. Unity:
`bash scripts/unity/compile.sh` then `run-tests.sh --mode all`.

**New guardrail to add:** extend a lint (or the doc-count target list) so shipped copy can't regress
into LLM-isms — e.g. a regex check that fails on the banned phrase list ("Hidden Gems", "Why Teams
Choose", "supercharge", emoji headings) in the [README](./README.md) and `docs/`.

**CHANGELOG:** add a user-facing "Rebranded to DxKit (display name only; package id unchanged)"
entry; brand/CI-only changes stay out per the CHANGELOG rule.

**Suggested landing order (low-risk copy/config first, big rewrite last):**
W0, W1, W2, W6, W7, W3, W5, W4, W8 (W8 runs continuously). W4 can proceed in parallel once W4.0 (theme
foundation) exists; land it window-by-window behind the Hub.

### Open items to confirm at execution time (not blockers)

- Editor **menu-path rename** to `.../DxKit/...` — breaking muscle-memory; keep legacy aliases for one
  release? (recommended: rename + alias).
- **Jekyll removal** vs keep (W3.4) — confirm no org-root page depends on it.
- Whether to raise sync-managed display counts (RNGs 15+ to 17+, converters phrasing) now that real
  numbers are known.

### Critical files index

- Design source: `design-system/*.dc.html`, `design-system/dxkit-banner.svg`
- Brand/sync: `cspell.json`, `docs/images/unity-helpers-banner.svg` (to `dxkit-banner.svg`),
  `scripts/sync-banner-version.ps1`, `.github/workflows/sync-banner-version.yml`,
  `.github/workflows/release-prepare.yml`, `scripts/sync-doc-counts.ps1`,
  `scripts/generate-doc-metadata.ps1`
- Copy: [README](./README.md), `package.json`, `llms.txt`, [documentation landing page](./docs/index.md),
  [README mirror](./docs/readme.md), `docs/`
- Site: `mkdocs.yml`, `docs/stylesheets/custom.css`, `docs/overrides/home.html` (new),
  `.github/workflows/deploy-pages.yml`, `deploy-wiki.yml`; legacy Jekyll `_config.yml`, `Gemfile`,
  [legacy root index](./index.md), `_includes/`, `assets/`
- Editor theme (new): `Editor/Styles/DxKit/DxKitTokens.uss`, `DxKitControls.uss`, `DxKitTheme.cs`,
  `Editor/Tools/DxKit/DxKitEditorWindow.cs`, `DxKitHubWindow.cs`
- Editor reuse: `Editor/Styles/WDropDownStyleLoader.cs`,
  `Editor/Styles/DropDowns/WDropdownVariables.uss`, `Editor/CustomDrawers/Utils/`,
  `Editor/Utils/WButton/`, `Editor/Utils/WGroup/`
- Editor rewrite targets: the 17 windows under `Editor/`, `Editor/Sprites/`, `Editor/Tools/`;
  `Editor/Settings/UnityHelpersSettings.cs`; drawers under `Editor/CustomDrawers/`,
  `Editor/CustomEditors/`
- Benchmarks: `.github/workflows/unity-benchmarks.yml`, `scripts/unity/lib/render-perf-deltas.js`,
  `perf-results/baseline.json`
