# Relational Component Performance Benchmarks

Relational component attributes (`[SiblingComponent]`, `[ParentComponent]`, `[ChildComponent]`) remove repetitive `GetComponent*` code. These benchmarks quantify the runtime cost of calling `Assign*Components` for common field shapes (single component, array, `List<T>`, and `HashSet<T>`) against hand-written lookups.

Numbers below are grouped by operating system. A section reading `pending` means nobody has run this suite on that platform, not that the attributes are slow there.

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
