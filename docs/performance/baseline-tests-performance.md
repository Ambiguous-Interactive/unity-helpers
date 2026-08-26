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
