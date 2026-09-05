# Performance Baseline Tests

These tests serve as automated CI regression guards. They verify that critical operations complete within acceptable time bounds, detecting performance regressions before they reach production.

## Baseline Philosophy

Baselines are set generously (2-3x expected typical performance) to account for CI environment variability while still catching significant regressions. A test failure indicates a performance regression that needs investigation.

## Test Categories

- **Spatial Trees**: QuadTree2D, KdTree2D, KdTree3D, OctTree3D, RTree2D construction and query performance
- **PRNG**: Random number generation throughput for PcgRandom, XoroShiroRandom, SplitMix64, RomuDuo
- **Pooling**: Collection pool rent/return overhead for List, HashSet, Dictionary, StringBuilder, SystemArrayPool
- **Serialization**: JSON and Protobuf serialization/deserialization throughput

<!-- BASELINE_PERFORMANCE_START -->

## Performance Baseline Report

Generated: 2026-01-12 01:36:55 UTC

### Spatial Trees

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Test</th>
      <th align="right">Iterations</th>
      <th align="right">Time (ms)</th>
      <th align="right">Baseline (ms)</th>
      <th align="right">% of Baseline</th>
      <th align="left">Status</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">QuadTree2DRangeQuery</td><td align="right">1K</td><td align="right">27</td><td align="right">200</td><td align="right">13.5%</td><td align="left">Pass</td></tr>
    <tr><td align="left">QuadTree2DBoundsQuery</td><td align="right">1K</td><td align="right">29</td><td align="right">200</td><td align="right">14.5%</td><td align="left">Pass</td></tr>
    <tr><td align="left">KdTree2DRangeQuery</td><td align="right">1K</td><td align="right">27</td><td align="right">200</td><td align="right">13.5%</td><td align="left">Pass</td></tr>
    <tr><td align="left">KdTree2DNearestNeighbor</td><td align="right">1K</td><td align="right">32</td><td align="right">200</td><td align="right">16.0%</td><td align="left">Pass</td></tr>
    <tr><td align="left">RTree2DRangeQuery</td><td align="right">1K</td><td align="right">2479</td><td align="right">200</td><td align="right">1239.5%</td><td align="left">FAIL</td></tr>
    <tr><td align="left">OctTree3DRangeQuery</td><td align="right">1K</td><td align="right">15</td><td align="right">200</td><td align="right">7.5%</td><td align="left">Pass</td></tr>
    <tr><td align="left">KdTree3DRangeQuery</td><td align="right">1K</td><td align="right">33</td><td align="right">200</td><td align="right">16.5%</td><td align="left">Pass</td></tr>
    <tr><td align="left">QuadTree2DConstruction</td><td align="right">1</td><td align="right">2</td><td align="right">500</td><td align="right">0.4%</td><td align="left">Pass</td></tr>
    <tr><td align="left">KdTree2DConstruction</td><td align="right">1</td><td align="right">2</td><td align="right">500</td><td align="right">0.4%</td><td align="left">Pass</td></tr>
    <tr><td align="left">RTree2DConstruction</td><td align="right">1</td><td align="right">1</td><td align="right">500</td><td align="right">0.2%</td><td align="left">Pass</td></tr>
  </tbody>
</table>

### PRNG

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Test</th>
      <th align="right">Iterations</th>
      <th align="right">Time (ms)</th>
      <th align="right">Baseline (ms)</th>
      <th align="right">% of Baseline</th>
      <th align="left">Status</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">PcgRandomNextInt</td><td align="right">1M</td><td align="right">1</td><td align="right">500</td><td align="right">0.2%</td><td align="left">Pass</td></tr>
    <tr><td align="left">PcgRandomNextFloat</td><td align="right">1M</td><td align="right">5</td><td align="right">500</td><td align="right">1.0%</td><td align="left">Pass</td></tr>
    <tr><td align="left">XoroShiroRandomNextInt</td><td align="right">1M</td><td align="right">1</td><td align="right">500</td><td align="right">0.2%</td><td align="left">Pass</td></tr>
    <tr><td align="left">SplitMix64NextInt</td><td align="right">1M</td><td align="right">1</td><td align="right">500</td><td align="right">0.2%</td><td align="left">Pass</td></tr>
    <tr><td align="left">RomuDuoNextInt</td><td align="right">1M</td><td align="right">1</td><td align="right">500</td><td align="right">0.2%</td><td align="left">Pass</td></tr>
  </tbody>
</table>

### Pooling

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Test</th>
      <th align="right">Iterations</th>
      <th align="right">Time (ms)</th>
      <th align="right">Baseline (ms)</th>
      <th align="right">% of Baseline</th>
      <th align="left">Status</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">ListPooling</td><td align="right">100K</td><td align="right">239504</td><td align="right">200</td><td align="right">119752.0%</td><td align="left">FAIL</td></tr>
    <tr><td align="left">HashSetPooling</td><td align="right">100K</td><td align="right">16503</td><td align="right">200</td><td align="right">8251.5%</td><td align="left">FAIL</td></tr>
    <tr><td align="left">DictionaryPooling</td><td align="right">100K</td><td align="right">16997</td><td align="right">200</td><td align="right">8498.5%</td><td align="left">FAIL</td></tr>
    <tr><td align="left">SystemArrayPool</td><td align="right">100K</td><td align="right">8</td><td align="right">200</td><td align="right">4.0%</td><td align="left">Pass</td></tr>
    <tr><td align="left">StringBuilderPooling</td><td align="right">100K</td><td align="right">16456</td><td align="right">200</td><td align="right">8228.0%</td><td align="left">FAIL</td></tr>
  </tbody>
</table>

### Serialization

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Test</th>
      <th align="right">Iterations</th>
      <th align="right">Time (ms)</th>
      <th align="right">Baseline (ms)</th>
      <th align="right">% of Baseline</th>
      <th align="left">Status</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">JsonSerialize</td><td align="right">10K</td><td align="right">43</td><td align="right">500</td><td align="right">8.6%</td><td align="left">Pass</td></tr>
    <tr><td align="left">JsonDeserialize</td><td align="right">10K</td><td align="right">64</td><td align="right">500</td><td align="right">12.8%</td><td align="left">Pass</td></tr>
    <tr><td align="left">JsonRoundTrip</td><td align="right">10K</td><td align="right">113</td><td align="right">1000</td><td align="right">11.3%</td><td align="left">Pass</td></tr>
    <tr><td align="left">ProtobufSerialize</td><td align="right">10K</td><td align="right">1169</td><td align="right">500</td><td align="right">233.8%</td><td align="left">FAIL</td></tr>
    <tr><td align="left">ProtobufDeserialize</td><td align="right">10K</td><td align="right">12</td><td align="right">500</td><td align="right">2.4%</td><td align="left">Pass</td></tr>
    <tr><td align="left">ProtobufRoundTrip</td><td align="right">10K</td><td align="right">1728</td><td align="right">1000</td><td align="right">172.8%</td><td align="left">FAIL</td></tr>
  </tbody>
</table>

### Summary

19 passed, 7 failed out of 26 tests.

<!-- BASELINE_PERFORMANCE_END -->

## Running the Tests

These tests run automatically during CI to catch regressions. To generate fresh benchmark results:

1. Open Unity Test Runner
2. Navigate to `PerformanceBaselineTests`
3. Run `GeneratePerformanceBaselineReport` explicitly (it is marked `[Explicit]`)
4. Results will be output to the console and can be copied to this document

## Interpreting Results

- **Time (ms)**: Actual measured time for the operation
- **Baseline (ms)**: Maximum allowed time before test failure
- **% of Baseline**: How much of the baseline budget was used (lower is better)
- **Status**: Pass if within baseline, Fail if exceeded

## Refreshing these numbers

Run `PerformanceBaselineTests.GeneratePerformanceBaselineReport` from Unity's Test Runner.

## Calibrated paired evidence

The generous baseline budgets above detect large regressions. They do not establish that an
optimization meets the acceptance criteria in [issue #636](https://github.com/Ambiguous-Interactive/unity-helpers/issues/636).
Scheduled aggregate reports are advisory. Their renderer never writes the canonical baseline,
including after a regression, and refuses empty, malformed, duplicate, or incomplete metric sets.
A positive cost against a zero baseline is a regression. The previous automatic baseline update
option is rejected.

`BenchmarkProtocol.MeasureCalibrated` now provides the first part of the stronger protocol:

- Correctness runs before warmup and again after the retained timing samples.
- Each arm warms separately for at least 100 ms and three executions. Calibration doubles the
  common iteration count until both arms take at least 20 ms; inability to calibrate is inconclusive.
- Eight predeclared `ABBABAAB` batches retain 32 observations per arm. Each slot begins with heap
  settling, consumes a checksum, and must take at least 10 ms. There are no acceptance retries.
- Immutable raw milliseconds, paired log ratios, geometric throughput ratio, median, p95, MAD,
  and within-arm spread accompany a 95% interval from 2,000 deterministic bootstrap repetitions.
  Bootstrap resamples whole counterbalanced batches to preserve within-batch correlation.
- Environment metadata records commit/base commit, Unity version, backend, build configuration,
  OS, CPU, process width, platform, and whether execution is inside the Editor. Unavailable code
  generation and optimization settings remain `unknown`. Seed, iteration count, and measured
  warmup durations/executions accompany the samples.

`IntMapPerformanceTests` emits the full raw record as `INTMAP_PAIRED_SAMPLES` JSON in its NUnit
output through an explicit JSON writer that needs no runtime serializer generation. Incomplete
counterbalanced batches cannot claim timing acceptance and report an undefined interval as JSON
`null`. Existing fixtures that still call `MeasurePaired` retain their older advisory protocol;
this change does not make their tables calibrated evidence.

For the IntMap player experiment, manually dispatch **Unity Tests** with `acceptance=intmap`
and a supported `unity-version`. Normal selected tests remain mandatory. The extra test runs in
a fresh Release IL2CPP player against `Dictionary` compiled in the same candidate, so its commit
and reference commit metadata are identical. The artifact retains all four workload records.
The verifier derives ratios, spreads and the bootstrap interval from the raw samples. It reports
`inconclusive` for unstable arms, `meets-hit-margin` for stable results with at least 1.3× at both
hit-only sizes and a favorable interval, or `below-hit-margin`. This is the timing decision for
[#578](https://github.com/Ambiguous-Interactive/unity-helpers/issues/578); it is not full #636
acceptance or proof of unmeasured allocation and code-size properties. No IntMap player result
has been claimed before this workflow actually runs.

The encoded timing improvement predicate requires at least 5% less runtime (throughput ratio
at least `1 / 0.95`), an entirely favorable interval, and stable arms. The allocation-change timing
predicate requires the entire runtime interval to stay within 5% of the reference. These are timing
predicates only: they cannot accept a change without green instrument controls, identical semantics,
and verified allocation, retention, and code-size evidence. Unsupported counters are explicitly
labeled; an absent measurement never becomes zero.

Issue #636 remains open. Required follow-up includes calibrated allocating/non-allocating and
fast/slow player canaries, workload-specific retention probes and build-size measurements, the
full acceptance policy including declared tradeoffs, and explicit post-merge promotion after
20 clean floor/latest Mono/IL2CPP player calibration repetitions. There is currently no automatic
promotion command. Analyzer findings, changed-branch coverage, mutation, replay/minimization, and
touched-group CI tiers also remain separate requirements. No new measured performance numbers or
nonempty baseline are claimed by this infrastructure change.
