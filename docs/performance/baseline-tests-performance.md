# Performance Baseline Tests

Use these tables as reference measurements for spatial queries, random generation, pooling and serialization. Profile your own workload before choosing an API for speed.

## Baseline Philosophy

The baseline column is a generous time budget used to flag large changes. A failure needs investigation; device load, runtime changes and workload differences can affect the result. Passing the budget does not establish a speedup or a frame-time guarantee.

## Test Categories

- **Spatial Trees**: QuadTree2D, KdTree2D, KdTree3D, OctTree3D, RTree2D construction and query performance
- **PRNG**: Random number generation throughput for PcgRandom, XoroShiroRandom, SplitMix64, RomuDuo
- **Pooling**: Collection pool rent/return overhead for List, HashSet, Dictionary, StringBuilder, SystemArrayPool
- **Serialization**: JSON and Protobuf serialization/deserialization throughput

<!-- CURRENT_BENCHMARK_RESULTS_START -->

## Current benchmark measurements

No successful complete Unity Benchmarks run is available for publication yet. The committed
measurements below are historical and are not results from the current candidate.

<!-- CURRENT_BENCHMARK_RESULTS_END -->

<a id="publishing-current-measurements"></a>

## Read the measurements

The current measurements section identifies the measured package commit, Unity version, test
mode and publication time when a complete run is available. Use those identities when comparing
results with your project. The historical report below comes from an older run and cannot describe
the performance of the current package on your device.

The package publishes complete benchmark results together with their raw test output. A partial
or failed run leaves the last complete report in place. A published result can still show a
regression: publication records the observation, rather than promising that every measured change
is faster.

The separate [comparison baseline](https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/perf-results/baseline.json)
currently contains no accepted metrics. Do not treat the tables as an accepted performance promise.

<!-- BASELINE_PERFORMANCE_START -->

## Historical performance baseline report

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

Dispatch **Unity Benchmarks** to execute the current benchmark profile and publish its complete
measurements to this guide. To inspect the historical baseline test report locally:

1. Open Unity Test Runner
2. Navigate to `PerformanceBaselineTests`
3. Run `GeneratePerformanceBaselineReport` explicitly (it is marked `[Explicit]`)
4. Results are output to the console; they do not replace the committed current measurements

## Interpreting Results

- **Time (ms)**: Actual measured time for the operation
- **Baseline (ms)**: Maximum allowed time before test failure
- **% of Baseline**: How much of the baseline budget was used (lower is better)
- **Status**: Pass if within baseline, Fail if exceeded

## Refreshing these numbers

Run `PerformanceBaselineTests.GeneratePerformanceBaselineReport` from Unity's Test Runner.

## Compare changes in your project

A baseline budget is a coarse regression check. It does not show that one implementation is
faster than another. For that decision, compare both implementations in the same player with the
same inputs, and check that they produce the same results before measuring them.

1. Choose representative inputs, including small, typical and peak sizes. Measure reads and writes
   separately when both matter to your game.
2. Warm each implementation before comparing steady-state work. Measure first use separately if
   startup latency matters.
3. Alternate measurement order and keep repeated samples. A single fast sample can reflect
   scheduling, garbage collection or an idle device rather than a useful improvement.
4. Include acquisition, clearing, conversions, useful work and cleanup in the measured operation.
   Measuring only an inner loop can miss the cost your caller actually pays.
5. Check allocation and retained memory separately from elapsed time. A warm operation can allocate
   nothing while keeping a large pooled buffer alive.
6. Repeat on the Unity versions, scripting backends and devices you ship. Editor, host, Mono,
   IL2CPP and WebGL results are not interchangeable.

If repeated samples are unstable, treat the comparison as inconclusive and investigate the
measurement conditions. Do not report missing or unsupported memory counters as zero allocation.
Include an operation known to allocate when checking that your allocation measurement works.

<a id="calibrated-paired-evidence"></a>

## Understand paired reports

Some package benchmarks report calibrated, paired measurements alongside the simple tables.
They warm both implementations, select a common iteration count, alternate measurement order and
retain repeated samples. The reports include a throughput ratio, a confidence interval and sample
spread.

| Report field               | How to use it                                                                                                                                                              |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Throughput ratio           | A ratio above one favors the candidate when the report defines the ratio as reference time divided by candidate time. Check the report's direction before interpreting it. |
| Confidence interval        | An interval crossing one does not establish which implementation is faster.                                                                                                |
| Sample spread              | Large variation weakens the comparison even when the median looks favorable.                                                                                               |
| Inconclusive result        | Calibration, stability or other required measurements were insufficient; it is not a speed claim.                                                                          |
| Runtime and input metadata | Compare like with like: the same backend, build configuration, workload and input sizes.                                                                                   |

A favorable timing report does not establish source compatibility, allocation cost, retained memory,
startup cost or build size. Check the properties your application needs before adopting a change.
See [enumeration](./enumeration-audit.md), [pooled resource ownership](./ownership-audit.md) and
[random generation](./random-performance.md) for feature-specific choices and limits.
