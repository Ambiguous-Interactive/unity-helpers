# Synchronous Editor test scopes

Use synchronous NUnit setup and teardown for fixtures that only create transient Unity objects
and perform synchronous assertions. Keep coroutine lifecycle fixtures when frame boundaries,
deferred destruction, asset reloads or scene transitions are part of the subject.

`UnityObjectTrackingTestBase` is available from the packaged, test-only
`WallstopStudios.UnityHelpers.TestSupport` assembly. It tracks owned transient objects, destroys
them immediately in reverse order, tolerates duplicates and objects already destroyed, and
refuses persistent assets in the Editor. It leaves the caller's logging policy alone. The
[testing guide](../contributing/testing-patterns.md#reuse-the-packaged-test-helpers) describes
assembly references and allocation controls.

Four existing fixtures retain all 106 case identities and assertions. `WShowIfPropertyDrawerTests`,
`WGroupLayoutBuilderTests` and `SpriteAnimationHelpersTests` use an EditMode wrapper around the
public tracking scope; `AnimationEventHelpersTests` needs no object lifecycle. Seven independent
controls cover cleanup, duplicate/destroyed objects, nulls, active-scope enforcement, indexed and
iterator ranges, logging policy, and persistent-asset refusal.

## Local comparison

Three alternating fresh-process pairs use Unity 6000.6.7f1 Editor Mono on Windows x64. The second
pair reverses arm order. Both arms include the same seven controls. Identical temporary
one-time loggers print each loaded fixture's base type and module identity; the controller rejects
the wrong base or missing identities. The temporary loggers are absent from the shipped fixtures.
No other owned compiler, documentation build or benchmark overlaps the captures.

| Fixture                       | Cases | Before median | After median | Paired after/before range |
| ----------------------------- | ----: | ------------: | -----------: | ------------------------: |
| `AnimationEventHelpersTests`  |     4 |    129.643 ms |    92.619 ms |               0.338–0.839 |
| `WGroupLayoutBuilderTests`    |    10 |    159.769 ms |   140.593 ms |               0.365–1.086 |
| `WShowIfPropertyDrawerTests`  |    57 |    180.523 ms |   115.555 ms |               0.256–0.995 |
| `SpriteAnimationHelpersTests` |    35 |    109.203 ms |    64.658 ms |               0.503–1.002 |
| Combined existing fixtures    |   106 |    537.912 ms |   415.062 ms |               0.338–0.991 |

The combined row is the median of each process's sum, rather than the sum of row medians.
Its median difference is about 0.123 seconds. WGroup is slower in two of three pairs; one
sprite-helper pair is effectively flat. The aggregate improvement is small in absolute terms
and includes substantial variation between processes. All six processes pass all 113 cases,
return raw exit zero and reach both stream ends without a drain stop.

Fixture duration includes lifecycle and runner work outside case bodies. These are local fixture
observations, not complete editor startup times or an explanation of a different CI version's
scheduling. Report slower cells and all paired ratios alongside aggregate results.

## CI scope

The original Unity 2021.3.45f1 artifact for run 43290504332 / job 131435887943 contains a 57-case
WShowIf fixture lasting 137.335653 seconds, while its case bodies sum to 0.168794 seconds. WGroup's
ten cases occupy 23.030214 seconds versus 0.106894 seconds in bodies. A fixture already using plain
NUnit also has substantial fixture/body separation, so replacing a common base alone does not
establish the complete cause. That shard's wall clock is 749.9 seconds; merged NUnit duration is
not a single process's elapsed time. The [eight-minute CI target](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1190)
remains a separate acceptance criterion.

## Replay

The [replay archive](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/native-runtime-qualification.zip) contains the experiment and consumer controls.

The archive retains frozen C# inputs, source hashes, both instrumented fixture variants, final
restore sources, controllers, complete NUnit trees and process receipts. Overlay the named arm's
four fixture files onto the frozen source snapshot before replaying. Each process must pass all
113 cases, reach both stream ends without a drain stop, and load the expected bases. Preserve new
replay identities and failures separately. A previous pre-test IL-postprocessor pipe failure is
tracked on [#343](https://github.com/Ambiguous-Interactive/unity-helpers/issues/343#issuecomment-6839715181);
later passes do not explain or repair it.

## Additional synchronous ownership scopes

A second cohort migrates twelve synchronous fixtures, retaining all 347 original cases. Ten
independent scope controls also run in each process. Managed resources are disposed in reverse
registration order before Unity objects; duplicate resources use reference identity. Cleanup
continues after failures, and callbacks cannot reopen or register into the closing scope.

Three alternating fresh-process pairs on Unity 6000.6.7f1 Editor Mono pass all 357 cases in all
six processes. Each process returns raw exit zero and reaches both stream ends without a drain
stop. Temporary one-time loggers verify each fixture's loaded base and module identity. Source
hashes remain unchanged during each capture; those loggers are absent from shipped fixtures.
No other owned build or benchmark overlaps these captures.

| Existing cohort        |     Before |      After | After/before |
| ---------------------- | ---------: | ---------: | -----------: |
| Pair 1                 | 2.973847 s | 3.153851 s |        1.061 |
| Pair 2, reversed order | 2.959021 s | 2.984900 s |        1.009 |
| Pair 3                 | 2.815570 s | 2.761018 s |        0.981 |
| Median process sum     | 2.959021 s | 2.984900 s |        1.009 |

These totals exclude the ten scope controls. The aggregate is flat to slightly slower; this
cohort establishes synchronous cleanup behavior, not an iteration-time improvement. Individual
fixture results and slower observations remain in the replay data. Historical Unity 2021 data
for this cohort accounts for only about fourteen seconds and cannot explain the full shard
time. Current Unity 2021/2022 qualification and the eight-minute CI target remain required.

An earlier cold-batch baseline also included `StringInListDrawerTests`: five cases failed because
Editor styles were uninitialized outside a GUI context. That fixture remains unchanged and is
excluded from both measured arms; [#1193](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1193)
tracks this failure. Another attempt lost its controller and child processes during
startup, without a final receipt or NUnit result. [#1195](https://github.com/Ambiguous-Interactive/unity-helpers/issues/1195)
tracks that unknown cause; later successful
captures do not resolve it. Compiler failures during harness preparation are retained separately
from timing observations.

The [additional replay archive](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/local-pool-runtime-qualification.zip) retains these attempts,
exact instrumented inputs, final source snapshots, process receipts and complete NUnit trees.

## Dropdown fixture comparison (withdrawn)

`StringInListDrawerTests` and `WValueDropDownDrawerTests` were evaluated with synchronous object scopes.
The migration was not retained after the strict requalification below; both keep `CommonTestBase`.
All 120 case identities and assertions remain, alongside ten unchanged scope ownership controls.
Both fixtures initialize real IMGUI styles before their style-dependent assertions, including in
an isolated cold batch process. Production drawers are unchanged.

Six serial fresh Windows x64 Unity 6000.6.7f1 Editor Mono processes use ABBAAB order.
Each passes all 130 cases. Actual per-fixture scope reports match the captured assembly module
identity; selected sources remain stable and all process receipts return raw zero, both stream
ends and no drain stop. Temporary reporters are absent from the shipped fixtures.

| Pair | String ratio | Dropdown ratio | Combined ratio | Unchanged control ratio |
| ---- | -----------: | -------------: | -------------: | ----------------------: |
| 1    |       0.9776 |         0.8273 |         0.9451 |                  1.0118 |
| 2    |       0.9893 |         0.9722 |         0.9857 |                  1.7310 |
| 3    |       0.9349 |         0.6919 |         0.8736 |                  0.6820 |

Ratios are candidate/baseline fixture durations. The geometric mean of the six target ratios
is 0.8919 and every combined pair improves, but these observations are withdrawn.
Review found that the setup helper leaked `LogAssert.ignoreFailingMessages`: the common
base reset it after cases while the synchronous base preserved it. The arms therefore
used different logging policies. The numeric threshold alone does not qualify this change.
The absolute savings are tens of milliseconds. The unchanged control varies substantially:
these captures establish no whole-suite speedup or CI wall-clock target.

The [complete durations](./dropdown-scopes-20261114.csv) and
[qualification record](./dropdown-scopes-20261114.json) retain every process, case identity,
source hash, assembly identity and process receipt. Two preparation attempts are excluded:
one failed compilation because the extracted setup helper lacked its NUnit import; another
passed 130 cases but omitted required identity reports from its output. Neither contributes
timing observations.

## Strict dropdown requalification (rejected)

A new six-process ABBAAB campaign uses identical strict logging in both arms. Each fixture
asserts that `LogAssert.ignoreFailingMessages` stays false, then restores its caller's policy.
All 130 original timed identities pass in every process. The additional normal-inspector
regression remains in final source and is excluded equally from the timed arms.
Loaded scope, strict-policy and module witnesses match captured assemblies; selected sources
remain stable and every process returns raw zero with both stream ends and no drain stop.

| Pair | String ratio | Dropdown ratio | Combined ratio | Unchanged control ratio |
| ---- | -----------: | -------------: | -------------: | ----------------------: |
| 1    |       1.0215 |         1.0543 |         1.0287 |                  4.5328 |
| 2    |       0.9714 |         0.8129 |         0.9369 |                  1.5419 |
| 3    |       0.9591 |         0.8485 |         0.9331 |                  0.6821 |

The geometric mean of the six target ratios is 0.9405, but the first combined pair regresses.
The preregistered gate required every combined pair to improve, so the migration is rejected.
The unchanged control also varies substantially. Neither this campaign nor the withdrawn one
establishes a whole-suite speedup. The fixtures retain their original common base, all original
assertions, real style initialization, strict logging and the normal-inspector regression.

The [strict durations](./dropdown-scopes-strict-20261114.csv) and
[qualification record](./dropdown-scopes-strict-20261114.json) retain every process,
case identity, source and assembly identity, logging witness and receipt summary.

The IMGUI executor restores its caller's log policy after every default and strict run, including mouse events and exhausted budgets. It does not enable broad log suppression while pumping events. Default offscreen passes tolerate only the exact native cursor-rectangle error from that pass; strict passes tolerate none. Native controls cover both incoming flag values and confirm unexpected errors remain visible on default and strict runs.

### Native log-scope qualification

The November 14 inspection used Unity **6000.6.7f1** with the actually loaded Test Framework **1.8.0**.
Its schema-3 framework snapshot identifies `UnityEngine.TestRunner` by module ID
`fca9cbc6-e5dc-46b6-8b76-59f7d0825373`. `LogScope.Current` and its private `m_Lock` are present;
`AllLogs` and `FailingLogs` are lists implementing `IList`, and `LogEvent.IsHandled` is writable.
The retained source digests are `a274f8220926da3f0165fa4848c3a851c43f945c8f0d0c9ec627198dd9d24a42`
for `LogScope.cs` and `731b21dde017bc5acc2e69f040bc65580ee59f74dfb5646cf4d0528d757034a6`
for `LogEvent.cs`.

All 1,624 custom-drawer cases pass with the scoped cursor-error handling. The 21 executor controls
cover both incoming policies, mouse events, exhausted budgets, unrelated unexpected errors, and
strict rejection of the cursor message itself. The final 92-case asset/framework cohort also passes,
including real blocked writes, persisted retries, and unrelated failures for similarly named assets.
These observations qualify the loaded framework above; other supported editor/framework combinations
remain checked by their own CI runs.
