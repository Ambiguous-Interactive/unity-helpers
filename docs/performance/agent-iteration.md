# Agent iteration and CI feedback

## Root cause

Feedback time includes discovery, setup, validation, runner queues, license waits, and review.
Optimizing a test body alone can leave the critical path unchanged.

Retained session 260 evidence shows summed CI execution dropping by 6m19s while elapsed time
increased from 52m38s to 82m36s. The SINGLE_THREADED job queued for 41m30s.
These are historical observations, not a current CI baseline or a controlled speedup claim.
Session 190 removed duplicated workflow setup and checks that ran as many as five times per push.
Later session notes record tool migrations with slower startup; language choice alone is not evidence.

The November 14 completed local transcript contains 1,068 execution calls. Inputs mentioned CI
check queries 104 times, preflight 33 times, and full typechecks six times. Requested sleep totals
about 81 minutes. Those counts indicate opportunities to investigate; they do not measure waste.
Raw session records remain in ignored local storage. The complete local index covers 70 sessions
and 1,865,158,198 bytes at capture; it stores metadata rather than raw conversation content.

The [current 30-run baseline](./agent-ci-baseline.json) retains every sampled run ID, source SHA,
trigger, attempt, conclusion, elapsed time, and summed job time. Nineteen runs were cancelled and
eleven succeeded. Successful elapsed time has a median of 4,049 seconds (67m29s) and a nearest-rank
p95 of 5,756 seconds (95m56s). The concurrent mode-controller step accounts for 32,013 summed
seconds across successful runs, versus 294 seconds for license acquisition. These concurrent sums
are not elapsed time or CPU time. Runner placement and changing source prevent causal speedup claims.

Replaying the 30 task diffs at the initial selector revision `cdf59dba` selects all native modes
for all 30. This replay predates the latest-only required version policy; it does not mean older
editors remain on the required path.
For PRs, the replay uses each head's merge-base with retained main history; push runs use the first
parent. This is a scope check, not a timing experiment or an assertion that historical trigger bases
were identical. A last-commit-only replay incorrectly narrowed one multi-commit task and was rejected.
The recent workload therefore needs execution improvements as well as smaller, single-purpose PRs;
reduced selection alone cannot establish the latency target.

Run 43669278793's retained native timing receipts show the 6000.5 editor controller taking
1,168.5 seconds on DAD-MACHINE, with isolated EditMode taking 1,150.2 seconds. Its initial editor
compilation took 4m18s. The latest editor controller on ELI-MACHINE took 338.0 seconds, with isolated
EditMode taking 322.2 seconds. Different editor versions and cache states confound that comparison;
it does not establish a hardware or concurrency cause. Both receipts confirm successful workers
and healthy cleanup. Retain native worker phase evidence when investigating the remaining cost.

## Task checks

Use the shared selector to inspect the work before launching checks:

The selector and its regression suite run in typed Python through locked uv.
The Node entry point retains its CLI and imported functions for existing consumers.
Registry base selection calls the Python inventory directly; focused iteration reads the
same registry in process. Neither path starts native Unity work.

```bash
npm run agent:check -- --tier iterate --base origin/main --plan --json
npm run agent:check -- --tier iterate --files Runtime/Core/Helper/WallMath.cs
npm run agent:check -- --tier pr --base origin/main --plan
npm run agent:check -- --tier full --base origin/main
```

The base uses a merge-base diff, then includes staged, unstaged, and untracked changes.
Both sides of renames and deleted paths remain visible. A commit does not erase task scope.
Explicit files replace discovery. Missing revisions fail rather than silently narrowing coverage.

A November 16 Linux tmpfs comparison used seven alternating fresh-process pairs after two
warmups per arm and cell, using bootstrap uv 0.13.6 and Node 22.23.3. One-source iteration medians were 82.14 ms for the original Node
selector and 58.74 ms through uv; 100-source iteration was 72.06 ms and 52.31 ms.
One-source PR planning regressed from 18.18 ms to 49.57 ms because its original path needed
no Python worker. These include startup and compare identical parsed plans. They do not
establish whole-check, Windows or CI gains. The frozen original-plan corpus preserves 69
plans across all three tiers; executable controls cover Git changes, shallow clones,
Unicode paths, live ignored settings and failure propagation.

Iteration runs the compiler scope and contract controls needed for the changed task. Format each
changed file immediately; run changed-file preflight before publication, once the task is ready.
The `pr` tier includes that preflight. An edit-loop task without a compiler, documentation or owned
contract check falls back to preflight, so an unowned input cannot silently pass with no checks.
Fixture-only edits compile the named existing fixtures plus production and shared test sources.
Mixed production/shared changes, deleted fixtures and metadata-only scopes retain whole-scope compilation; shared test
changes check both runtime and editor fixtures. If a fixture needs another fixture's declarations,
use `npm run typecheck:iterate -- tests` or `editor-tests` for the complete tree.
Iteration also runs contract controls whose non-group input paths or patterns match a changed file,
including declared dependencies.
The typed registry selector owns pattern matching and dependency validation for both local iteration and CI.
This catches shared-resolver consumers without repeating the full PR aggregate. Broad input groups
remain CI coverage; they do not select every tooling suite during each edit. Retain the task's
focused behavior witness for cases outside those declared controls.
Serialization and threading source edits also use focused compiler iteration; their PR still
requires full compiler and native compatibility checks. Generator, assembly, and analyzer inputs
retain compiler escalation.
PR checks enable normal compiler analysis. Compatibility-sensitive and unknown inputs add player coverage on latest 6.x; older versions run asynchronously.
Full means all configured local lint, contract, source compiler, and compiler-control commands.
It does not mean Unity ran: native fixture validation and the hosted Unity matrix remain required.
Every selected command executes and reports its status and elapsed time. Generic result reuse is
not enabled. The existing compiler iteration command manages its own incremental build state;
the task runner does not add another cache over arbitrary validators.

The generic reuse experiment failed both cost and correctness checks. Its first source reader
took 82.5 seconds. A Git tree/diff variant took 4.13 seconds but missed raw line-ending changes
hidden by Git normalization. Adding raw-byte hashing took another 48.0 seconds for 9,053 files in
one observation, exceeding the measured runtime iteration cost.

Even raw source hashes did not capture ignored local settings. A real command first returned zero,
then should have returned seven after an ignored setting changed; the unchanged Git snapshot
reused the earlier success instead. The regression now requires actual execution and observes the
failure. This rejects generic caching rather than treating source equality as proof of every
validator's inputs. A future cache needs a complete input contract for each eligible check.

PR lint and contract jobs now use the shared planner's merge-base inventory. Registry entries
name input groups and required companion checks. Missing metadata runs unconditionally; unknown
paths select the complete registry. Invalid dependency graphs and missing input groups fail.
Documentation without an applicable contract suite reports that no validation was performed.
Main retains complete registry execution. The unsafe scanner retains its own positive controls
and always emits the required safety report. Tooling test suites run when their inputs change.
The two expensive unsafe-code and Unity mode-controller self-tests name their implementation,
fixture, configuration, and harness inputs. An unrelated formatter edit no longer selects either
self-test; changing their own implementation or shared harness still does. Unknown paths still
select the full registry.

The redundant formatting aggregate no longer runs in Repo Lint: its Prettier and final-newline
checks already have individual registry entries. Its manual compatibility command remains tested
by the existing hook regressions.

Native PR mode selection uses the same merge-base inventory and risk selector. Ordinary C# and
documentation changes run EditMode and PlayMode; compatibility-sensitive and unknown changes
retain Standalone and protobuf-only players. SINGLE_THREADED follows PR mode selection. The latest
PR and main jobs run both variants with one activation and a shared cap of five workers. Separate
source worktrees, Libraries, artifact paths and result gates preserve each variant. Main runs all
modes on latest 6.x. Default manual qualification retains the full supported matrix and split
jobs. To repeat the comparison, dispatch all modes for the latest editor with diagnostics and
additional acceptance disabled, then choose `split` or `combined` in `latest-mode-scheduling`.
Each selected mode requires passing native XML, redaction, artifact upload and confirmed cleanup.

The approved organization policy requires a static version axis to prove matching editor setup
and license return. The workflow retains that literal universe and uses
[matrix exclusions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idstrategymatrixexclude)
to remove complete unselected legs. Ordinary C# PRs run latest stable EditMode and PlayMode;
compatibility-sensitive and unknown changes add players on the same latest 6.x editor. Main also
blocks only on that latest editor. Older versions run independently on each main commit in a separate
`Unity Compatibility` workflow, one version at a time on the background runner. Its distinct `Unity Compatibility Success`
check cannot replace the required `Unity CI Success` result. Each immutable main source retains
its own run, so a later commit cannot cancel compatibility needed by a release. Re-run an existing
source run to recover a transient failure. Background work uses DAD-MACHINE only. ELI-MACHINE
is eligible for fast validation but is shared with other repositories. Older-version failures remain visible and release
publication requires all older versions for the same source.
Default manual Unity Tests qualification retains the full matrix. An explicit manual version selects
that named editor. Regenerate the compatibility workflow with
`node scripts/unity/generate-compatibility-workflow.js` after changing canonical Unity jobs; the
grouped-mode contract tests reject drift. Licensed setup, lock, result and cleanup steps have one
maintained source in `unity-tests.yml`.
Documentation retains latest editor validation pending a qualified conditional gate. Job summaries
report selected versions and modes; a reduced run does not qualify omitted coverage.

The action pins now use the approved v1.17.1 release rather than the unapproved commit previously
labelled v1.17.1. Local Gates checks every action against the central approval registry at its
fetched main revision, separately from validating each pinned action's input declarations. The
[central policy update](https://github.com/Ambiguous-Interactive/ambiguous-organization-build-lock/pull/381)
is merged. The full source-free consumer audit at `fc5aa9a7` inventories 42 workflow entries and
reports only the two pre-existing expired central exceptions (October 31). This is not a clean
enrollment result. Those exceptions remain unresolved; no exception was renewed or removed to
make this rollout pass.

Historical capture dispatch retains its original skipped qualification gates. The capture job
reports its own diagnostic result; it does not certify new native work. Ordinary PRs, pushes, and
default manual qualification keep their always-reporting native result requirements. The central
policy update admits only that exact diagnostic exclusion and preserves the original result script;
it does not attempt to sandbox arbitrary capture scripts.

The unsafe-code registry scan also creates its SARIF report; report upload checks that output
instead of executing the scanner a second time.

Central action discovery and pin resolution share a YAML node reader. Quoted, folded, flow, and
aliased references cannot bypass approval checks. The reader rejects duplicate keys and merges.
Its launchers select the existing locked Python metadata dependency group.

## Task and agent operation

Start with one bounded task: objective, acceptance criteria, affected systems, failing witness,
implementation, passing evidence, and remaining work. Read relevant guidance and history rather
than loading unrelated backlog and dependency audits for every edit.

Use one implementation owner by default. Delegate independent investigations or isolated changes
when useful. Parallel writers need separate worktrees and explicit ownership. One owner validates
each Unity project; never overlap aggregate builds against shared outputs. Wait for status changes
and summarize deltas instead of repeatedly retrieving whole CI and review histories.

Use a coordination session when several tasks share an editor or CI runner pool. It owns the
frozen heads, live run IDs, validation order, and handoffs; implementation sessions own bounded
tasks in separate worktrees. One validation owner per Unity project does not reserve organization
runner capacity. Coordinate validation campaigns across shared-pool consumers when authorized;
this repository's background-runner restriction alone cannot bound another repository's load.
Keep queue delay in the measurement, and distinguish reduced runner use from reduced feedback time.
The existing [organization admission issue](https://github.com/Ambiguous-Interactive/ambiguous-organization-build-lock/issues/53)
tracks cross-repository runner starvation and bounded admission. This pilot does not implement
that separate protocol or establish organization-wide queue fairness.

For games, test pure calculations with fast host or EditMode tests, retain focused asset/lifecycle
integration cases, and use real players for AOT, serialization, and platform claims. Keep seeded
reproductions and separate performance campaigns from correctness. Do not disable domain reload
as a general test acceleration: static-state reset and event lifetime behavior require validation.

This approach follows [OpenAI harness guidance](https://openai.com/index/harness-engineering/),
[bounded delegation guidance](https://learn.chatgpt.com/docs/agent-configuration/subagents), and
[long-running agent handoff guidance](https://www.anthropic.com/research/effective-harnesses-for-long-running-agents).

## Qualification and measurement

Three compiler-command pairs completed with compiler inputs unchanged and verified by a content
hash before and after. Normal multi-configuration checks took 551.7, 808.1, and 749.0 seconds;
runtime iteration took 11.7, 4.9, and 16.4 seconds. Medians were 749.0 and 11.7 seconds. These
compare feedback scopes: normal runs all configurations and analysis, while iteration compiles
runtime sources. Other tooling work and host I/O continued during capture. Preflight, focused
tests, native execution, and review are excluded. The [retained measurements](./agent-ci-baseline.json)
include the input hash, commands, samples, and descriptive nearest-rank p95; three samples do not
establish tail latency. This is a component probe, not qualification of three complete task pilots.

The local task pilots exposed missing end-to-end savings in the first implementation: runtime
warm feedback improved by 48.8%, editor feedback grew by 2.3%, and tooling improved by 63.4%.
Those observations remain in [the pilot record](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1206).
A second experiment at `9a380729` measured the revised edit loop in three pairs with changed source per task:

| Task            | Normal median | Iteration median | Reduction | Descriptive p95, normal → edit |
| --------------- | ------------- | ---------------- | --------- | ------------------------------ |
| Runtime fixture | 84.1 s        | 9.4 s            | 88.8%     | 106.8 s → 10.6 s               |
| Editor fixture  | 32.7 s        | 18.3 s           | 44.1%     | 50.2 s → 18.9 s                |
| PR template     | 39.6 s        | 1.0 s            | 97.4%     | 50.9 s → 1.0 s                 |

Each path includes the same task-specific behavior witness. The normal path includes preflight
and the applicable normal compiler or documentation checks. The edit path defers publication
preflight and full analyzer/variant coverage, using scoped compilation or owned controls.
Publication and native checks remain
separate. The editor pilot misses the individual 50% target. With three samples, descriptive p95
is the largest observation and does not establish population tail latency. No cold-machine claim
is made for the second experiment.

Workflow inventory controls own both YAML suffixes. Adding a workflow therefore checks operator
registration locally, including protection of licensed work from generic watchdog cancellation.
The full source-free organization audit remains necessary; source review alone missed an exact
aggregate-condition contract during this rollout.

### Latest-editor variant scheduling comparison

Both runs used `d6c4fab0` and all modes on Unity 6000.6.0f1. Package export remained a separate
job and is outside the test timing totals. The seven archives for each arm retained matching
assemblies, test identities and outcomes across 19 NUnit reports. All 11 source and Library
paths remained distinct. The combined controller peaked at five workers and passed both
variant gates and its central license return and cleanup.

| Test jobs                                                                                   | Runner placement                      | Occupied runner time | Dispatch to last test job | Lock lifecycles |
| ------------------------------------------------------------------------------------------- | ------------------------------------- | -------------------: | ------------------------: | --------------: |
| [Split](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/44192964317)    | Normal on ELI, SINGLE_THREADED on DAD |              1,413 s |                   1,887 s |               2 |
| [Combined](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/44197974376) | Both variants on ELI                  |                596 s |                   1,236 s |               1 |

The combined job occupied 58% fewer runner-seconds in this pair. Different hosts, cache and
queue conditions limit the timing comparison. The trial supports reducing licensed job churn.
It leaves fleet-wide latency and performance on DAD unmeasured.

### Native task pilots

Primary CI pairs freeze each task's head and compare complete tracked trees with the PR's
synthetic merge. The full control is the default manual all-version, all-mode qualification;
the selected run is the normal PR workflow. Both use the concurrent mode controller. Experiments
run serially, and main remains unchanged until all primary pairs finish. Subsequent integration
and landing runs are excluded from these measurements.

| Task            | Full elapsed                                                                               | Selected elapsed                                                                           | Occupied runners, full → selected | Lock reservations, full → selected |
| --------------- | ------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------ | --------------------------------- | ---------------------------------- |
| Runtime fixture | [1,754 s](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/43759528452) | [2,117 s](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/43762473530) | 2,932 s → 1,330 s                 | 2,464–2,510 s → 1,122–1,151 s      |
| Editor fixture  | [4,699 s](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/43769034819) | [1,732 s](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/43765967541) | 2,397 s → 1,279 s                 | 1,917–1,962 s → 1,075–1,102 s      |
| PR template     | [4,682 s](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/43776490945) | [2,828 s](https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/43784347690) | 2,382 s → 995 s                   | 1,924–1,969 s → 782–808 s          |

The runtime pair used head `727b05a0`. Occupied runner time fell 54.6%, but elapsed time rose
20.7%; this observation missed the wall-time target. The latest normal job's created-to-start
interval grew from 171 to 1,650 seconds. Host assignments also swapped: normal DAD to ELI and
SINGLE_THREADED ELI to DAD. Events, generated stamps, caches, and observed sibling load differ,
so the causal elapsed-time effect is inconclusive. Runner occupation and lock-reservation bounds
are not CPU time or exact license-seat consumption. This small cohort does not establish
population CI tail latency.

The editor pair used head `39b7e149`. Observed elapsed fell 63.1% and occupied runner time fell
46.6%; the full control's last 2021.3 job waited 4,004 seconds from creation to start. Both
ordinary C# task pairs together have task medians of 3,226.5 s full and 1,924.5 s selected,
an observed reduction of 40.4% that misses the 50% elapsed target. Their descriptive nearest-rank
p95 values are 4,699 s and 2,117 s: maxima of only two heterogeneous task observations.
The favorable editor pair does not replace the runtime miss or remove the shared-load confounders.
The [editor evidence](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1206#issuecomment-6903383028)
retains the complete paired values and source identities.

All four new runtime cases pass in canonical PlayMode XML on every full-control version and
selected latest. Each XML's start/end timestamps fall within its own run window; shard copies
are not counted twice. The selected artifact inventory contains exactly normal and
SINGLE_THREADED EditMode/PlayMode. Required result and cleanup gates pass; unselected Standalone
acceptance is an expected skip. Both latest normal PlayMode captures retain the existing native
post-test exit fault tracked in [#813](https://github.com/Ambiguous-Interactive/unity-helpers/issues/813).
These case passes do not certify clean engine exits. The
[paired evidence](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1206#issuecomment-6901962135)
retains source identities, counts, timing limits, and the missed target.

All four new editor culture cases also pass in canonical EditMode XML on all full-control
versions and selected latest. Exact full and selected artifact inventories contain 19 and four
artifacts. All nine native jobs have their required cleanup steps and applicable mode/export
gates explicitly present and passing. XML timestamps fall inside each run's own window.

The tooling pilot uses the PR template at head `cb3ef397`. This is an unknown-input control:
it retains all native modes on latest 6.x rather than selecting no Unity work. Its full control
produces 19 mode artifacts; all canonical XML files contain actual passing leaf cases, with
matching passed headers and no failed cases, suites, or failure elements. Artifact digests,
source identities, and run-window timestamps are verified independently. All six licensed jobs
have positive result/export and cleanup gates. The
[full-control evidence](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1206#issuecomment-6904609837)
retains those checks. The selected run retains seven latest-only mode artifacts, including
both Standalone variants and SINGLE_THREADED Standalone. The same independent XML checks pass
for all seven, and all three jobs have their positive result/export and cleanup gates. This
qualifies version reduction while preserving conservative mode selection. Observed elapsed fell
39.6%, missing the individual 50% target; occupied runner time fell 58.2%. The normal latest job
waited 2,372 seconds from creation to start. All 24 PR-event checks completed; PR creation to
last check took 2,832 seconds. The
[completed tooling evidence](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1206#issuecomment-6905199694)
retains artifact counts, XML hashes, source identity and gate inventories.

Across all three heterogeneous task pairs, elapsed medians are 4,682 s full and 2,117 s selected
(54.8% lower); descriptive nearest-rank p95 values are 4,699 s and 2,828 s. Occupied runner
medians are 2,397 s and 1,279 s (46.6% lower), with descriptive p95 values of 2,932 s and 1,330 s.
These three-task statistics do not replace the ordinary C# cohort's 40.4% elapsed reduction and
missed target. Each p95 is the largest of only three observations, not a population-tail estimate.
Shared-pool load, host, cache, event and generated-stamp differences prevent causal speedup claims.

Measure cold and warm feedback separately with at least three paired local runs. Compare CI
queue, setup, execution, and cleanup on comparable runners. Report median, p95, runner time,
and seat occupancy. The target is a 50% reduction in warm local feedback and ordinary PR elapsed
time; no such improvement is established by the selector tests.

Head-specific pilot acceptance, raw samples and CI qualification are tracked in
[the task record](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1206).
Conditional native enrollment remains tracked separately in the organization policy work.
Preserve supported public APIs and editor versions.

The selector timings above predate the later bridge-framing and caller-output restoration fixes.
The [portability qualification](./python-tooling-portability-qualification.json) records those
corrections; the corrected source was not timed again.
