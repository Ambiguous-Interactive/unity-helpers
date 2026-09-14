# Reflection Performance Benchmarks

Unity Helpers replaces ad-hoc reflection with cached delegates that favour expression lambdas on IL2CPP-safe platforms and fall back to dynamic IL emit or plain reflection where available. These benchmarks compare raw `System.Reflection` against the helpers for common access patterns.

Each run updates the table for the current operating system only. Sections that still show `_No benchmark data generated yet._` simply have not been executed on that platform.

## Windows

<!-- REFLECTION_PERFORMANCE_WINDOWS_START -->

Generated on 2026-09-14 05:37:47 UTC

### Strategy: Default (auto)

#### Boxed Access (object)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (boxed)</td><td align="right">26.97M</td><td align="right">4.87M</td><td align="right">5.54x</td></tr>
    <tr><td align="left">Instance Field Set (boxed)</td><td align="right">17.97M</td><td align="right">5.68M</td><td align="right">3.17x</td></tr>
    <tr><td align="left">Static Field Get (boxed)</td><td align="right">9.15M</td><td align="right">2.73M</td><td align="right">3.36x</td></tr>
    <tr><td align="left">Static Field Set (boxed)</td><td align="right">3.31M</td><td align="right">964.1K</td><td align="right">3.44x</td></tr>
    <tr><td align="left">Instance Property Get (boxed)</td><td align="right">5.27M</td><td align="right">26.16M</td><td align="right">0.20x</td></tr>
    <tr><td align="left">Instance Property Set (boxed)</td><td align="right">10.02M</td><td align="right">1.89M</td><td align="right">5.31x</td></tr>
    <tr><td align="left">Static Property Get (boxed)</td><td align="right">19.60M</td><td align="right">12.95M</td><td align="right">1.51x</td></tr>
    <tr><td align="left">Static Property Set (boxed)</td><td align="right">19.59M</td><td align="right">1.61M</td><td align="right">12.14x</td></tr>
    <tr><td align="left">Instance Method Invoke (boxed)</td><td align="right">25.20M</td><td align="right">1.84M</td><td align="right">13.71x</td></tr>
    <tr><td align="left">Static Method Invoke (boxed)</td><td align="right">7.41M</td><td align="right">2.61M</td><td align="right">2.84x</td></tr>
    <tr><td align="left">Constructor Invoke (boxed)</td><td align="right">23.17M</td><td align="right">1.00M</td><td align="right">23.09x</td></tr>
  </tbody>
</table>

#### Typed Access (no boxing)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">Baseline Delegate (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Delegate</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (typed)</td><td align="right">580.66M</td><td align="right">670.86M</td><td align="right">4.87M</td><td align="right">0.87x</td><td align="right">119.20x</td></tr>
    <tr><td align="left">Instance Field Set (typed)</td><td align="right">597.19M</td><td align="right">644.34M</td><td align="right">5.68M</td><td align="right">0.93x</td><td align="right">105.23x</td></tr>
    <tr><td align="left">Static Field Get (typed)</td><td align="right">618.28M</td><td align="right">709.36M</td><td align="right">2.73M</td><td align="right">0.87x</td><td align="right">226.87x</td></tr>
    <tr><td align="left">Static Field Set (typed)</td><td align="right">620.63M</td><td align="right">672.68M</td><td align="right">964.1K</td><td align="right">0.92x</td><td align="right">643.71x</td></tr>
    <tr><td align="left">Instance Property Get (typed)</td><td align="right">564.40M</td><td align="right">684.63M</td><td align="right">26.16M</td><td align="right">0.82x</td><td align="right">21.58x</td></tr>
    <tr><td align="left">Instance Property Set (typed)</td><td align="right">626.94M</td><td align="right">701.72M</td><td align="right">1.89M</td><td align="right">0.89x</td><td align="right">332.22x</td></tr>
    <tr><td align="left">Static Property Get (typed)</td><td align="right">618.67M</td><td align="right">683.45M</td><td align="right">12.95M</td><td align="right">0.91x</td><td align="right">47.76x</td></tr>
    <tr><td align="left">Static Property Set (typed)</td><td align="right">597.69M</td><td align="right">662.32M</td><td align="right">1.61M</td><td align="right">0.90x</td><td align="right">370.17x</td></tr>
    <tr><td align="left">Instance Method Invoke (typed)</td><td align="right">623.14M</td><td align="right">667.81M</td><td align="right">1.84M</td><td align="right">0.93x</td><td align="right">339.00x</td></tr>
    <tr><td align="left">Static Method Invoke (typed)</td><td align="right">577.86M</td><td align="right">657.41M</td><td align="right">2.61M</td><td align="right">0.88x</td><td align="right">221.54x</td></tr>
  </tbody>
</table>

### Strategy: Expressions

#### Boxed Access (object)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (boxed)</td><td align="right">26.82M</td><td align="right">4.68M</td><td align="right">5.73x</td></tr>
    <tr><td align="left">Instance Field Set (boxed)</td><td align="right">17.02M</td><td align="right">5.42M</td><td align="right">3.14x</td></tr>
    <tr><td align="left">Static Field Get (boxed)</td><td align="right">10.74M</td><td align="right">7.70M</td><td align="right">1.39x</td></tr>
    <tr><td align="left">Static Field Set (boxed)</td><td align="right">23.06M</td><td align="right">3.14M</td><td align="right">7.35x</td></tr>
    <tr><td align="left">Instance Property Get (boxed)</td><td align="right">27.42M</td><td align="right">10.48M</td><td align="right">2.62x</td></tr>
    <tr><td align="left">Instance Property Set (boxed)</td><td align="right">23.17M</td><td align="right">1.09M</td><td align="right">21.26x</td></tr>
    <tr><td align="left">Static Property Get (boxed)</td><td align="right">27.50M</td><td align="right">8.67M</td><td align="right">3.17x</td></tr>
    <tr><td align="left">Static Property Set (boxed)</td><td align="right">25.82M</td><td align="right">1.18M</td><td align="right">21.93x</td></tr>
    <tr><td align="left">Instance Method Invoke (boxed)</td><td align="right">25.81M</td><td align="right">1.84M</td><td align="right">14.04x</td></tr>
    <tr><td align="left">Static Method Invoke (boxed)</td><td align="right">7.47M</td><td align="right">2.49M</td><td align="right">3.00x</td></tr>
    <tr><td align="left">Constructor Invoke (boxed)</td><td align="right">23.42M</td><td align="right">1.11M</td><td align="right">21.01x</td></tr>
  </tbody>
</table>

#### Typed Access (no boxing)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">Baseline Delegate (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Delegate</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (typed)</td><td align="right">587.94M</td><td align="right">654.83M</td><td align="right">4.68M</td><td align="right">0.90x</td><td align="right">125.63x</td></tr>
    <tr><td align="left">Instance Field Set (typed)</td><td align="right">631.27M</td><td align="right">665.34M</td><td align="right">5.42M</td><td align="right">0.95x</td><td align="right">116.55x</td></tr>
    <tr><td align="left">Static Field Get (typed)</td><td align="right">598.87M</td><td align="right">697.71M</td><td align="right">7.70M</td><td align="right">0.86x</td><td align="right">77.75x</td></tr>
    <tr><td align="left">Static Field Set (typed)</td><td align="right">604.51M</td><td align="right">658.34M</td><td align="right">3.14M</td><td align="right">0.92x</td><td align="right">192.69x</td></tr>
    <tr><td align="left">Instance Property Get (typed)</td><td align="right">548.18M</td><td align="right">688.82M</td><td align="right">10.48M</td><td align="right">0.80x</td><td align="right">52.30x</td></tr>
    <tr><td align="left">Instance Property Set (typed)</td><td align="right">627.09M</td><td align="right">701.69M</td><td align="right">1.09M</td><td align="right">0.89x</td><td align="right">575.56x</td></tr>
    <tr><td align="left">Static Property Get (typed)</td><td align="right">608.80M</td><td align="right">683.92M</td><td align="right">8.67M</td><td align="right">0.89x</td><td align="right">70.19x</td></tr>
    <tr><td align="left">Static Property Set (typed)</td><td align="right">624.94M</td><td align="right">643.74M</td><td align="right">1.18M</td><td align="right">0.97x</td><td align="right">530.79x</td></tr>
    <tr><td align="left">Instance Method Invoke (typed)</td><td align="right">612.01M</td><td align="right">678.22M</td><td align="right">1.84M</td><td align="right">0.90x</td><td align="right">332.79x</td></tr>
    <tr><td align="left">Static Method Invoke (typed)</td><td align="right">586.74M</td><td align="right">652.85M</td><td align="right">2.49M</td><td align="right">0.90x</td><td align="right">235.86x</td></tr>
  </tbody>
</table>

### Strategy: Dynamic IL

#### Boxed Access (object)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (boxed)</td><td align="right">26.53M</td><td align="right">4.88M</td><td align="right">5.44x</td></tr>
    <tr><td align="left">Instance Field Set (boxed)</td><td align="right">17.71M</td><td align="right">5.41M</td><td align="right">3.27x</td></tr>
    <tr><td align="left">Static Field Get (boxed)</td><td align="right">10.73M</td><td align="right">8.12M</td><td align="right">1.32x</td></tr>
    <tr><td align="left">Static Field Set (boxed)</td><td align="right">22.09M</td><td align="right">3.34M</td><td align="right">6.62x</td></tr>
    <tr><td align="left">Instance Property Get (boxed)</td><td align="right">26.94M</td><td align="right">10.61M</td><td align="right">2.54x</td></tr>
    <tr><td align="left">Instance Property Set (boxed)</td><td align="right">23.35M</td><td align="right">872.4K</td><td align="right">26.76x</td></tr>
    <tr><td align="left">Static Property Get (boxed)</td><td align="right">27.34M</td><td align="right">4.01M</td><td align="right">6.81x</td></tr>
    <tr><td align="left">Static Property Set (boxed)</td><td align="right">4.04M</td><td align="right">671.3K</td><td align="right">6.01x</td></tr>
    <tr><td align="left">Instance Method Invoke (boxed)</td><td align="right">1.66M</td><td align="right">1.15M</td><td align="right">1.44x</td></tr>
    <tr><td align="left">Static Method Invoke (boxed)</td><td align="right">26.87M</td><td align="right">2.62M</td><td align="right">10.27x</td></tr>
    <tr><td align="left">Constructor Invoke (boxed)</td><td align="right">9.88M</td><td align="right">2.50M</td><td align="right">3.96x</td></tr>
  </tbody>
</table>

#### Typed Access (no boxing)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">Baseline Delegate (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Delegate</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (typed)</td><td align="right">596.30M</td><td align="right">702.60M</td><td align="right">4.88M</td><td align="right">0.85x</td><td align="right">122.21x</td></tr>
    <tr><td align="left">Instance Field Set (typed)</td><td align="right">602.42M</td><td align="right">653.76M</td><td align="right">5.41M</td><td align="right">0.92x</td><td align="right">111.31x</td></tr>
    <tr><td align="left">Static Field Get (typed)</td><td align="right">617.94M</td><td align="right">680.06M</td><td align="right">8.12M</td><td align="right">0.91x</td><td align="right">76.06x</td></tr>
    <tr><td align="left">Static Field Set (typed)</td><td align="right">621.50M</td><td align="right">646.50M</td><td align="right">3.34M</td><td align="right">0.96x</td><td align="right">186.31x</td></tr>
    <tr><td align="left">Instance Property Get (typed)</td><td align="right">558.13M</td><td align="right">676.35M</td><td align="right">10.61M</td><td align="right">0.83x</td><td align="right">52.58x</td></tr>
    <tr><td align="left">Instance Property Set (typed)</td><td align="right">610.32M</td><td align="right">699.18M</td><td align="right">872.4K</td><td align="right">0.87x</td><td align="right">699.61x</td></tr>
    <tr><td align="left">Static Property Get (typed)</td><td align="right">629.81M</td><td align="right">667.66M</td><td align="right">4.01M</td><td align="right">0.94x</td><td align="right">156.91x</td></tr>
    <tr><td align="left">Static Property Set (typed)</td><td align="right">596.33M</td><td align="right">633.30M</td><td align="right">671.3K</td><td align="right">0.94x</td><td align="right">888.31x</td></tr>
    <tr><td align="left">Instance Method Invoke (typed)</td><td align="right">601.44M</td><td align="right">678.52M</td><td align="right">1.15M</td><td align="right">0.89x</td><td align="right">523.14x</td></tr>
    <tr><td align="left">Static Method Invoke (typed)</td><td align="right">601.74M</td><td align="right">682.07M</td><td align="right">2.62M</td><td align="right">0.88x</td><td align="right">230.04x</td></tr>
  </tbody>
</table>

### Strategy: Reflection Fallback

#### Boxed Access (object)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (boxed)</td><td align="right">6.99M</td><td align="right">6.94M</td><td align="right">1.01x</td></tr>
    <tr><td align="left">Instance Field Set (boxed)</td><td align="right">5.65M</td><td align="right">2.22M</td><td align="right">2.55x</td></tr>
    <tr><td align="left">Static Field Get (boxed)</td><td align="right">7.79M</td><td align="right">7.94M</td><td align="right">0.98x</td></tr>
    <tr><td align="left">Static Field Set (boxed)</td><td align="right">6.40M</td><td align="right">6.33M</td><td align="right">1.01x</td></tr>
    <tr><td align="left">Instance Property Get (boxed)</td><td align="right">9.76M</td><td align="right">24.66M</td><td align="right">0.40x</td></tr>
    <tr><td align="left">Instance Property Set (boxed)</td><td align="right">1.17M</td><td align="right">2.02M</td><td align="right">0.58x</td></tr>
    <tr><td align="left">Static Property Get (boxed)</td><td align="right">23.87M</td><td align="right">6.23M</td><td align="right">3.83x</td></tr>
    <tr><td align="left">Static Property Set (boxed)</td><td align="right">2.77M</td><td align="right">2.80M</td><td align="right">0.99x</td></tr>
    <tr><td align="left">Instance Method Invoke (boxed)</td><td align="right">1.86M</td><td align="right">1.83M</td><td align="right">1.02x</td></tr>
    <tr><td align="left">Static Method Invoke (boxed)</td><td align="right">2.57M</td><td align="right">2.52M</td><td align="right">1.02x</td></tr>
    <tr><td align="left">Constructor Invoke (boxed)</td><td align="right">2.42M</td><td align="right">2.35M</td><td align="right">1.03x</td></tr>
  </tbody>
</table>

#### Typed Access (no boxing)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Scenario</th>
      <th align="right">Helper (ops/sec)</th>
      <th align="right">Baseline Delegate (ops/sec)</th>
      <th align="right">System.Reflection (ops/sec)</th>
      <th align="right">Speedup vs Delegate</th>
      <th align="right">Speedup vs Reflection</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Instance Field Get (typed)</td><td align="right">2.19M</td><td align="right">687.76M</td><td align="right">6.94M</td><td align="right">0.00x</td><td align="right">0.32x</td></tr>
    <tr><td align="left">Instance Field Set (typed)</td><td align="right">5.56M</td><td align="right">663.47M</td><td align="right">2.22M</td><td align="right">0.01x</td><td align="right">2.51x</td></tr>
    <tr><td align="left">Static Field Get (typed)</td><td align="right">7.94M</td><td align="right">692.45M</td><td align="right">7.94M</td><td align="right">0.01x</td><td align="right">1.00x</td></tr>
    <tr><td align="left">Static Field Set (typed)</td><td align="right">6.14M</td><td align="right">658.82M</td><td align="right">6.33M</td><td align="right">0.01x</td><td align="right">0.97x</td></tr>
    <tr><td align="left">Instance Property Get (typed)</td><td align="right">552.52M</td><td align="right">696.34M</td><td align="right">24.66M</td><td align="right">0.79x</td><td align="right">22.40x</td></tr>
    <tr><td align="left">Instance Property Set (typed)</td><td align="right">603.84M</td><td align="right">690.68M</td><td align="right">2.02M</td><td align="right">0.87x</td><td align="right">299.17x</td></tr>
    <tr><td align="left">Static Property Get (typed)</td><td align="right">611.56M</td><td align="right">660.91M</td><td align="right">6.23M</td><td align="right">0.93x</td><td align="right">98.18x</td></tr>
    <tr><td align="left">Static Property Set (typed)</td><td align="right">603.81M</td><td align="right">643.88M</td><td align="right">2.80M</td><td align="right">0.94x</td><td align="right">215.53x</td></tr>
    <tr><td align="left">Instance Method Invoke (typed)</td><td align="right">598.18M</td><td align="right">684.02M</td><td align="right">1.83M</td><td align="right">0.87x</td><td align="right">326.27x</td></tr>
    <tr><td align="left">Static Method Invoke (typed)</td><td align="right">579.89M</td><td align="right">659.73M</td><td align="right">2.52M</td><td align="right">0.88x</td><td align="right">230.09x</td></tr>
  </tbody>
</table>

<!-- REFLECTION_PERFORMANCE_WINDOWS_END -->

## macOS

<!-- REFLECTION_PERFORMANCE_MACOS_START -->

_No benchmark data generated yet._

<!-- REFLECTION_PERFORMANCE_MACOS_END -->

## Linux

<!-- REFLECTION_PERFORMANCE_LINUX_START -->

_No benchmark data generated yet._

<!-- REFLECTION_PERFORMANCE_LINUX_END -->

## Unknown / Other

<!-- REFLECTION_PERFORMANCE_UNKNOWN_START -->

_No benchmark data generated yet._

<!-- REFLECTION_PERFORMANCE_UNKNOWN_END -->
