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

Replaying the 30 task diffs against this selector proposes full native coverage for all 30.
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

```bash
npm run agent:check -- --tier iterate --base origin/main --plan --json
npm run agent:check -- --tier iterate --files Runtime/Core/Helper/WallMath.cs
npm run agent:check -- --tier pr --base origin/main --plan
npm run agent:check -- --tier full --base origin/main
```

The base uses a merge-base diff, then includes staged, unstaged, and untracked changes.
Both sides of renames and deleted paths remain visible. A commit does not erase task scope.
Explicit files replace discovery. Missing revisions fail rather than silently narrowing coverage.

Iteration runs changed-file preflight and the existing compiler iteration scopes. It also runs
contract controls whose inputs explicitly name a changed file, including declared dependencies.
This catches shared-resolver consumers without repeating the full PR aggregate. Broad input groups
remain CI coverage; they do not select every tooling suite during each edit. Retain the task's
focused behavior witness for cases outside those declared controls.
Serialization and threading source edits also use focused compiler iteration; their PR still
requires full compiler and native compatibility checks. Generator, assembly, and analyzer inputs
retain compiler escalation.
PR checks enable normal compiler analysis. Compatibility-sensitive and unknown inputs propose full native coverage.
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
retain Standalone and protobuf-only players. SINGLE_THREADED follows PR mode selection. Main and
default manual qualification retain full coverage. Every selected mode still requires passing native XML,
redaction, artifact upload, and confirmed cleanup.

The approved organization policy requires a static version axis to prove matching editor setup
and license return. The workflow retains that literal universe and uses
[matrix exclusions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idstrategymatrixexclude)
to remove complete unselected legs. Ordinary C# PRs run latest stable EditMode and PlayMode;
compatibility-sensitive and unknown changes retain all versions and players. Main and default
manual qualification retain the full matrix. An explicit manual version selects that named editor.
Documentation retains latest editor validation pending a qualified conditional gate. Job summaries
report selected versions and modes; a reduced run does not qualify omitted coverage.

The action pins now use the approved v1.17.1 release rather than the unapproved commit previously
labelled v1.17.1. Local Gates checks every action against the central approval registry at its
fetched main revision, separately from validating each pinned action's input declarations. A
targeted enrollment audit of the two license-acquiring workflows with the proposed readback
policy reports only two expired central exceptions (October 31). The current central analyzer also
rejects the three diagnostic aggregate branches; the
[policy update](https://github.com/Ambiguous-Interactive/ambiguous-organization-build-lock/pull/381)
must land before this consumer can pass enrollment. This is not a clean whole-repository audit. The external exceptions remain
unresolved.

Historical capture dispatch now requires successful runner preflight and artifact readback plus
skipped licensed jobs in each aggregate. Ordinary and untrusted PR branches retain their existing
requirements. The final qualification gate stays skipped for capture-only dispatch; reading old
evidence does not qualify new source.

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

For games, test pure calculations with fast host or EditMode tests, retain focused asset/lifecycle
integration cases, and use real players for AOT, serialization, and platform claims. Keep seeded
reproductions and separate performance campaigns from correctness. Do not disable domain reload
as a general test acceleration: static-state reset and event lifetime behavior require validation.

This approach follows [OpenAI harness guidance](https://openai.com/index/harness-engineering/),
[bounded delegation guidance](https://learn.chatgpt.com/docs/agent-configuration/subagents), and
[long-running agent handoff guidance](https://www.anthropic.com/research/effective-harnesses-for-long-running-agents).

## Qualification still required

Three compiler-command pairs completed with compiler inputs unchanged and verified by a content
hash before and after. Normal multi-configuration checks took 551.7, 808.1, and 749.0 seconds;
runtime iteration took 11.7, 4.9, and 16.4 seconds. Medians were 749.0 and 11.7 seconds. These
compare feedback scopes: normal runs all configurations and analysis, while iteration compiles
runtime sources. Other tooling work and host I/O continued during capture. Preflight, focused
tests, native execution, and review are excluded. The [retained measurements](./agent-ci-baseline.json)
include the input hash, commands, samples, and descriptive nearest-rank p95; three samples do not
establish tail latency. This is a component probe, not qualification of three complete task pilots.

Measure cold and warm feedback separately with at least three paired local runs. Compare CI
queue, setup, execution, and cleanup on comparable runners. Report median, p95, runner time,
and seat occupancy. The target is a 50% reduction in warm local feedback and ordinary PR elapsed
time; no such improvement is established by the selector tests.

Remaining rollout work includes three task pilots, current-head CI qualification, and conditional
native enrollment. Preserve supported public APIs and editor versions.
