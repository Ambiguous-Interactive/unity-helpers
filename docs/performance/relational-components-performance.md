# Relational Component Performance Benchmarks

Relational component attributes (`[SiblingComponent]`, `[ParentComponent]`, `[ChildComponent]`) remove repetitive `GetComponent*` code. These benchmarks quantify the runtime cost of calling `Assign*Components` for common field shapes (single component, array, `List<T>`, and `HashSet<T>`) against hand-written lookups.

Numbers below are grouped by operating system. A section reading `pending` means nobody has run this suite on that platform, not that the attributes are slow there.

Parent and child queries use pooled typed buffers. A buffer containing `Component` appends through `List<Component>.AddRange`; buffers containing derived components retain typed enumeration. A `List<DerivedComponent>` does not implement `ICollection<Component>`, so passing it to `AddRange` misses the bulk-copy path and boxes its enumerator. Both transfer paths preserve query order and existing destination entries. The benchmark tables below predate this transfer change.

## Collection traversal comparison (2026-11-08)

The [raw samples, source hashes and process receipts](./relational-collection-evidence.json)
compare the previous implementation with the current collection path on Windows x64,
Unity 6000.6.7f1. Each cell uses seven rounds in each of three independent processes.
The table gives candidate time divided by baseline time, using the median of process
medians. A value below 1 is faster; ranges cover shallow and deep hierarchies.

| Candidates | Sealed fields, editor Mono | Sealed fields, Release IL2CPP |
| ---------: | -------------------------: | ----------------------------: |
|          0 |                  0.83–0.84 |                     0.89–0.92 |
|          1 |                  0.77–0.86 |                     0.92–0.94 |
|          4 |                  0.52–0.52 |                     0.45–0.46 |
|         16 |                  0.47–0.47 |                     0.36–0.36 |
|         64 |                  0.42–0.42 |                     0.30–0.31 |
|        500 |                  0.41–0.43 |                     0.28–0.28 |

These cells assign an array, list and set together, including the array allocation.
They use `SpriteRenderer` for sealed fields. The corresponding `BoxCollider`
fallback cells span 0.96–1.05 in editor Mono and 0.98–1.09 in IL2CPP; no universal
fallback speedup is claimed. A broader fallback rewrite was rejected after a 1.20×
regression in one deep nonsealed case; its negative example is in the evidence.

The fast path retains the native query for zero or one match and traverses breadth
first for larger results. Its fallback callbacks live in a separate method, so
bypassing that method does not create their closures. Three editor profiler controls
for warmed sealed list/set fields (0, 1 and 64 matches) observe zero allocation
events after verifying positive allocation and negative reuse controls. Arrays and
first-use storage are outside that allocation claim.

These are latest-editor observations, not Unity 2021.3 floor, WebGL, cold-plan,
retention or whole-audit qualification. Issue [#644](https://github.com/Ambiguous-Interactive/unity-helpers/issues/644)
remains open. Both arms' editor build logs contain the same temporary-allocator
teardown warning; the measured player logs do not. The historical tables below
predate these collection changes and use a different harness.

## Windows (Editor/Player)

<!-- RELATIONAL_COMPONENTS_WINDOWS_START -->

Last updated 2026-09-14 05:38 UTC on Windows 11 (10.0.26200).

Numbers capture repeated `Assign*Components` calls for one second per scenario.
Higher operations per second are better.

### Operations per second (higher is better)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Relational Ops/s</th>
      <th align="right">Manual Ops/s</th>
      <th align="right">Rel/Manual</th>
      <th align="right">Iterations</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Parent - Single</td><td align="right">1,239,795</td><td align="right">10,018,690</td><td align="right">0.12x</td><td align="right">1,240,000</td></tr>
    <tr><td align="left">Parent - Array</td><td align="right">746,056</td><td align="right">4,764,392</td><td align="right">0.16x</td><td align="right">750,000</td></tr>
    <tr><td align="left">Parent - List</td><td align="right">802,040</td><td align="right">7,396,326</td><td align="right">0.11x</td><td align="right">810,000</td></tr>
    <tr><td align="left">Parent - HashSet</td><td align="right">777,660</td><td align="right">3,955,524</td><td align="right">0.20x</td><td align="right">780,000</td></tr>
    <tr><td align="left">Child - Single</td><td align="right">665,079</td><td align="right">5,341,149</td><td align="right">0.12x</td><td align="right">670,000</td></tr>
    <tr><td align="left">Child - Array</td><td align="right">293,349</td><td align="right">3,229,543</td><td align="right">0.09x</td><td align="right">300,000</td></tr>
    <tr><td align="left">Child - List</td><td align="right">285,215</td><td align="right">3,783,105</td><td align="right">0.08x</td><td align="right">290,000</td></tr>
    <tr><td align="left">Child - HashSet</td><td align="right">300,276</td><td align="right">2,001,901</td><td align="right">0.15x</td><td align="right">310,000</td></tr>
    <tr><td align="left">Sibling - Single</td><td align="right">4,636,847</td><td align="right">28,450,407</td><td align="right">0.16x</td><td align="right">4,640,000</td></tr>
    <tr><td align="left">Sibling - Array</td><td align="right">1,242,712</td><td align="right">3,361,155</td><td align="right">0.37x</td><td align="right">1,250,000</td></tr>
    <tr><td align="left">Sibling - List</td><td align="right">1,339,612</td><td align="right">4,176,273</td><td align="right">0.32x</td><td align="right">1,340,000</td></tr>
    <tr><td align="left">Sibling - HashSet</td><td align="right">1,260,522</td><td align="right">2,255,225</td><td align="right">0.56x</td><td align="right">1,270,000</td></tr>
  </tbody>
</table>

<!-- RELATIONAL_COMPONENTS_WINDOWS_END -->

## macOS

<!-- RELATIONAL_COMPONENTS_MACOS_START -->

Pending: run the relational component benchmark suite on macOS to capture results.

<!-- RELATIONAL_COMPONENTS_MACOS_END -->

## Linux

<!-- RELATIONAL_COMPONENTS_LINUX_START -->

Pending: run the relational component benchmark suite on Linux to capture results.

<!-- RELATIONAL_COMPONENTS_LINUX_END -->

## Other Platforms

<!-- RELATIONAL_COMPONENTS_OTHER_START -->

Pending: run the relational component benchmark suite on the target platform to capture results.

<!-- RELATIONAL_COMPONENTS_OTHER_END -->

## Refreshing these numbers

Run `RelationalComponentBenchmarkTests.Benchmark` from Unity's Test Runner. It rewrites the section matching the operating system it ran on and leaves the others alone.

## Replaying collection comparisons in a player

The separate player compares complete child assignments (array, list, and set in
one call) and IntMap clear/refill cycles. It has 24 child scenarios: 0, 1, 4, 16,
64, or 500 candidates, shallow or deep, with sealed `SpriteRenderer` and nonsealed
`BoxCollider` fields. Ten IntMap cases include sparse tables and both sides of
the clearing threshold. It checks results and emits seven timing rounds per case.

1. Create a separate Windows Unity project and install the package from the source
   checkout to measure. Keep the project outside that checkout.
2. Copy [the player](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/scripts/benchmarks~/RelationalCollectionPlayer.cs) into
   `Assets`, and copy [the builder](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/scripts/benchmarks~/RelationalCollectionPlayerBuild.cs)
   into `Assets/Editor`.
3. Copy [the BoxCollider target](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/Tests/Core/TestTypes/RelationalChildCollectionTarget.cs)
   and [the SpriteRenderer target](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/Tests/Core/TestTypes/RelationalChildSealedCollectionTarget.cs)
   into `Assets`. Copy these two source files only; the player needs no test assembly.
4. Run Unity in batch mode with `-projectPath` pointing to that project and
   `-executeMethod WallstopStudios.UnityHelpers.Benchmarks.RelationalCollectionPlayerBuild.Build`.
   The builder selects a non-development IL2CPP Release build and writes
   `Build/CollectionBenchmark/CollectionBenchmark.exe` under the project.
5. Run that executable with `-batchmode -nographics -logFile player.log
-uhBenchmarkOutput samples.txt`. Require exit zero, the success message, and
   exactly 238 samples plus the environment line. Keep the build and player logs.
6. Freeze each arm's full player directory, record source and binary hashes, and
   alternate three independent baseline and candidate processes with other builds
   stopped. Compare process medians and the best process for each case. Never mix
   editor and player absolute times or replace a failed process with leaf samples.

The player warms each case 64 times, calibrates to at least 20 ms, and checks a
sleep control against the high-resolution timer. It includes delegate overhead
and array allocation in child assignments. This is a scoped workload, not a
qualification of every relation, backend, Unity version, or consumer workload.

The equivalent NUnit fixtures are
[RelationalChildCollectionTimingTests](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/Tests/Runtime/Performance/RelationalChildCollectionTimingTests.cs)
and [IntMapSparseClearTimingTests](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/Tests/Runtime/Performance/IntMapSparseClearTimingTests.cs).
They belong to the Performance category and do not add timing loops to ordinary
correctness CI. Their editor timings use a different harness; compare arms within
one harness and runtime.
