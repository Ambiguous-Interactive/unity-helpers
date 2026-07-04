# Relational Component Performance Benchmarks

Relational component attributes (`[SiblingComponent]`, `[ParentComponent]`, `[ChildComponent]`) remove repetitive `GetComponent*` code. These benchmarks quantify the runtime cost of calling `Assign*Components` for common field shapes (single component, array, `List<T>`, and `HashSet<T>`) against hand-written lookups.

**How to refresh these tables:**

1. Open Unity’s Test Runner (EditMode/PlayMode as appropriate for your setup).
2. Run `RelationalComponentBenchmarkTests.Benchmark` inside `Tests/Runtime/Performance`.
3. The test logs the tables to the console and rewrites the section that matches the current operating system.

The script executes the benchmark test in batch mode, captures the markdown tables to `BenchmarkLogs/RelationalBenchmark.log`, and preserves the raw `TestResults.xml` when `-KeepResults` is specified.

## Windows (Editor/Player)

<!-- RELATIONAL_COMPONENTS_WINDOWS_START -->

Last updated 2026-05-08 04:09 UTC on Windows 11 (10.0.26200) 64bit.

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
    <tr><td align="left">Parent - Single</td><td align="right">727,869</td><td align="right">5,749,952</td><td align="right">0.13x</td><td align="right">730,000</td></tr>
    <tr><td align="left">Parent - Array</td><td align="right">445,375</td><td align="right">3,343,147</td><td align="right">0.13x</td><td align="right">450,000</td></tr>
    <tr><td align="left">Parent - List</td><td align="right">491,299</td><td align="right">4,194,708</td><td align="right">0.12x</td><td align="right">500,000</td></tr>
    <tr><td align="left">Parent - HashSet</td><td align="right">479,674</td><td align="right">2,949,101</td><td align="right">0.16x</td><td align="right">480,000</td></tr>
    <tr><td align="left">Child - Single</td><td align="right">496,367</td><td align="right">3,564,321</td><td align="right">0.14x</td><td align="right">500,000</td></tr>
    <tr><td align="left">Child - Array</td><td align="right">187,832</td><td align="right">2,392,107</td><td align="right">0.08x</td><td align="right">190,000</td></tr>
    <tr><td align="left">Child - List</td><td align="right">193,706</td><td align="right">2,594,739</td><td align="right">0.07x</td><td align="right">200,000</td></tr>
    <tr><td align="left">Child - HashSet</td><td align="right">194,977</td><td align="right">1,728,196</td><td align="right">0.11x</td><td align="right">200,000</td></tr>
    <tr><td align="left">Sibling - Single</td><td align="right">3,797,711</td><td align="right">14,448,211</td><td align="right">0.26x</td><td align="right">3,800,000</td></tr>
    <tr><td align="left">Sibling - Array</td><td align="right">687,153</td><td align="right">2,578,904</td><td align="right">0.27x</td><td align="right">690,000</td></tr>
    <tr><td align="left">Sibling - List</td><td align="right">838,603</td><td align="right">3,380,690</td><td align="right">0.25x</td><td align="right">840,000</td></tr>
    <tr><td align="left">Sibling - HashSet</td><td align="right">826,435</td><td align="right">1,872,130</td><td align="right">0.44x</td><td align="right">830,000</td></tr>
  </tbody>
</table>

<!-- RELATIONAL_COMPONENTS_WINDOWS_END -->

## macOS

<!-- RELATIONAL_COMPONENTS_MACOS_START -->

Pending — run the relational component benchmark suite on macOS to capture results.

<!-- RELATIONAL_COMPONENTS_MACOS_END -->

## Linux

<!-- RELATIONAL_COMPONENTS_LINUX_START -->

Pending — run the relational component benchmark suite on Linux to capture results.

<!-- RELATIONAL_COMPONENTS_LINUX_END -->

## Other Platforms

<!-- RELATIONAL_COMPONENTS_OTHER_START -->

Pending — run the relational component benchmark suite on the target platform to capture results.

<!-- RELATIONAL_COMPONENTS_OTHER_END -->
