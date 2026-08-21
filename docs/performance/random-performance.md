# Random Number Generator Performance Benchmarks

> Auto-generated via RandomPerformanceTests.Benchmark. Run the test to refresh these summary and detail tables.

State repair for a generator restored from JSON or protobuf happens in its constructor or the shared
after-deserialization callback. This includes each generator's state, the shared bit/byte reservoirs
and GUID scratch buffer. Repair is not repeated in a draw method, so malformed/default serialized
state is repaired before the first draw without adding a guard to every later draw. PhotonSpin's
one-time warmup priming also happens there, leaving only its block-boundary check in the draw path.

For statistical batteries, the repository's
`Generator~/WallstopStudios.UnityHelpers.RandomQuality` host emits a reproducible little-endian byte
stream from an explicit generator, GUID seed and byte count. Long PractRand/TestU01 runs belong in
scheduled reporting with pinned tools and expected weak-generator failures; they are not suitable as
nondeterministic pull-request gates.

<!-- RANDOM_BENCHMARKS_START -->

## Summary (fastest first)

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Random</th>
      <th align="right">NextUint (ops/s)</th>
      <th align="left">Speed</th>
      <th align="left">Quality</th>
      <th align="left">Notes</th>
    </tr>
  </thead>
  <tbody>
    <tr><td>LinearCongruentialGenerator</td><td align="right">1,323,700,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="5">Poor</td><td>Numerical Recipes &#39;quick and dirty&#39; LCG (a=1664525, c=1013904223, m=2^32) returning the raw state, so bit k has period only 2^(k+1) -- measured linear complexity of bit k is 2^k+1, and bit 0 simply alternates. Cosmetic use only.</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">1,283,400,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="6">Experimental</td><td>Single-word chaotic generator; author notes period 2^64 but provides no formal test results—treat as experimental.</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">1,054,900,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="3">Good</td><td>Empirical PractRand testing to 32GB shows strong diffusion; designed as a chaotic ARX mixer rather than a proven statistically optimal generator.</td></tr>
    <tr><td>SplitMix64</td><td align="right">1,052,300,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="2">Very Good</td><td>Well-known SplitMix64 mixer; passes TestU01 BigCrush and PractRand up to large data sizes in literature. <a href="https://prng.di.unimi.it/splitmix64.c">Vigna 2014</a></td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">923,200,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Six-word ARX-style generator tuned for all-around use; author reports TestU01 BigCrush passes. Not independently verified -- the upstream repository is offline.</td></tr>
    <tr><td>PcgRandom</td><td align="right">897,900,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>PCG XSH RR 64/32 variant; passes TestU01 BigCrush and PractRand in published results. <a href="https://www.pcg-random.org/paper.html">O&#39;Neill 2014</a></td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">754,700,000</td><td data-sort-value="4">Fast</td><td data-sort-value="4">Fair</td><td>xoroshiro128+, returning the low 32 bits -- the half its authors document as linear. Measured: bit 0 has linear complexity exactly 128, so NextBool is predictable from 128 draws. Prefer Xoshiro128StarStar or PcgRandom where single bits matter. <a href="https://prng.di.unimi.it/xoroshiro128plus.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>IllusionFlow</td><td align="right">754,500,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Five-word rotate/xor/add generator driven by a 32-bit Weyl counter; author reports PractRand to 64 GB with no anomalies. Not independently verified -- the upstream repository is offline.</td></tr>
    <tr><td>RomuDuo</td><td align="right">750,100,000</td><td data-sort-value="4">Fast</td><td data-sort-value="3">Good</td><td>ROMU multiplier with a modified duo update; returns the low 32 bits of the 64-bit product. Not the published romuDuo or romuDuoJr, so their measured results do not transfer. <a href="https://romu-random.org/code.c">Overton 2020</a></td></tr>
    <tr><td>StormDropRandom</td><td align="right">705,200,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>Large-state ARX generator over a 1024-word (4 KB) ring buffer with two 32-bit control words; author reports strong PractRand results. Not independently verified -- the upstream repository is offline.</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">602,100,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="4">Fair</td><td>Classic 32-bit xorshift; known to fail portions of TestU01 and PractRand, acceptable for lightweight effects only. <a href="https://doi.org/10.18637/jss.v008.i14">Marsaglia 2003</a></td></tr>
    <tr><td>WyRandom</td><td align="right">440,500,000</td><td data-sort-value="2">Slow</td><td data-sort-value="2">Very Good</td><td>Wyhash-based generator; published testing shows it clears BigCrush/PractRand with wide seed coverage. <a href="https://github.com/wangyi-fudan/wyhash">Wang Yi 2019</a></td></tr>
    <tr><td>SquirrelRandom</td><td align="right">414,000,000</td><td data-sort-value="2">Slow</td><td data-sort-value="3">Good</td><td>Hash-based generator built on Squirrel3; good equidistribution for table lookups but not extensively tested beyond moderate ranges. <a href="https://youtu.be/LWFzPP8ZbdU?t=2673">Squirrel Eiserloh</a></td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">261,100,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="1">Excellent</td><td>SHISHUA-inspired generator; independent testing (PractRand 128GB) by author indicates excellent distribution properties.</td></tr>
    <tr><td>UnityRandom</td><td align="right">87,600,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="4">Fair</td><td>Mirrors UnityEngine.Random, documented by Unity as Xorshift 128; suitable for legacy compatibility but not high-stakes simulation. <a href="https://docs.unity3d.com/ScriptReference/Random.html">UnityEngine.Random</a></td></tr>
    <tr><td>SystemRandom</td><td align="right">64,700,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Reimplements the classic .NET System.Random algorithm (Knuth subtractive lagged-Fibonacci, mod 2^31-1) so its sequence is serializable and stable across runtimes. Fails modern statistical batteries. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
    <tr><td>DotNetRandom</td><td align="right">55,900,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Wraps System.Random, which on Mono and for seeded .NET Core is Knuth&#39;s subtractive lagged-Fibonacci generator (mod 2^31-1), not an LCG. The sequence is runtime-dependent, so do not rely on it for cross-platform determinism. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
  </tbody>
</table>

## Detailed Metrics

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Random</th>
      <th align="right">NextBool</th>
      <th align="right">Next</th>
      <th align="right">NextUint</th>
      <th align="right">NextFloat</th>
      <th align="right">NextDouble</th>
      <th align="right">NextUint (Range)</th>
      <th align="right">NextInt (Range)</th>
    </tr>
  </thead>
  <tbody>
    <tr><td>LinearCongruentialGenerator</td><td align="right">785,200,000</td><td align="right">538,800,000</td><td align="right">1,323,700,000</td><td align="right">184,300,000</td><td align="right">296,300,000</td><td align="right">582,900,000</td><td align="right">498,700,000</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">787,300,000</td><td align="right">528,500,000</td><td align="right">1,283,400,000</td><td align="right">184,300,000</td><td align="right">297,900,000</td><td align="right">529,000,000</td><td align="right">458,200,000</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">788,200,000</td><td align="right">537,400,000</td><td align="right">1,054,900,000</td><td align="right">183,800,000</td><td align="right">293,200,000</td><td align="right">479,700,000</td><td align="right">421,200,000</td></tr>
    <tr><td>SplitMix64</td><td align="right">795,900,000</td><td align="right">537,200,000</td><td align="right">1,052,300,000</td><td align="right">184,400,000</td><td align="right">297,700,000</td><td align="right">482,500,000</td><td align="right">439,100,000</td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">767,800,000</td><td align="right">526,500,000</td><td align="right">923,200,000</td><td align="right">182,300,000</td><td align="right">293,100,000</td><td align="right">449,800,000</td><td align="right">404,100,000</td></tr>
    <tr><td>PcgRandom</td><td align="right">249,800,000</td><td align="right">527,300,000</td><td align="right">897,900,000</td><td align="right">181,000,000</td><td align="right">274,700,000</td><td align="right">447,700,000</td><td align="right">405,000,000</td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">761,000,000</td><td align="right">359,000,000</td><td align="right">754,700,000</td><td align="right">157,900,000</td><td align="right">192,400,000</td><td align="right">422,100,000</td><td align="right">376,900,000</td></tr>
    <tr><td>IllusionFlow</td><td align="right">779,300,000</td><td align="right">529,200,000</td><td align="right">754,500,000</td><td align="right">183,000,000</td><td align="right">281,600,000</td><td align="right">439,200,000</td><td align="right">390,000,000</td></tr>
    <tr><td>RomuDuo</td><td align="right">784,600,000</td><td align="right">359,400,000</td><td align="right">750,100,000</td><td align="right">156,100,000</td><td align="right">188,700,000</td><td align="right">437,700,000</td><td align="right">384,600,000</td></tr>
    <tr><td>StormDropRandom</td><td align="right">776,600,000</td><td align="right">523,600,000</td><td align="right">705,200,000</td><td align="right">180,600,000</td><td align="right">252,400,000</td><td align="right">393,500,000</td><td align="right">356,700,000</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">783,300,000</td><td align="right">534,100,000</td><td align="right">602,100,000</td><td align="right">184,100,000</td><td align="right">283,200,000</td><td align="right">474,500,000</td><td align="right">383,500,000</td></tr>
    <tr><td>WyRandom</td><td align="right">749,500,000</td><td align="right">363,000,000</td><td align="right">440,500,000</td><td align="right">159,000,000</td><td align="right">185,300,000</td><td align="right">290,000,000</td><td align="right">277,800,000</td></tr>
    <tr><td>SquirrelRandom</td><td align="right">756,300,000</td><td align="right">382,700,000</td><td align="right">414,000,000</td><td align="right">158,800,000</td><td align="right">197,800,000</td><td align="right">355,500,000</td><td align="right">309,500,000</td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">713,300,000</td><td align="right">220,800,000</td><td align="right">261,100,000</td><td align="right">119,900,000</td><td align="right">115,800,000</td><td align="right">217,600,000</td><td align="right">214,200,000</td></tr>
    <tr><td>UnityRandom</td><td align="right">628,900,000</td><td align="right">76,700,000</td><td align="right">87,600,000</td><td align="right">59,700,000</td><td align="right">38,800,000</td><td align="right">81,800,000</td><td align="right">81,900,000</td></tr>
    <tr><td>SystemRandom</td><td align="right">146,200,000</td><td align="right">145,700,000</td><td align="right">64,700,000</td><td align="right">131,300,000</td><td align="right">138,600,000</td><td align="right">58,700,000</td><td align="right">57,800,000</td></tr>
    <tr><td>DotNetRandom</td><td align="right">544,600,000</td><td align="right">53,100,000</td><td align="right">55,900,000</td><td align="right">44,700,000</td><td align="right">26,700,000</td><td align="right">53,400,000</td><td align="right">51,700,000</td></tr>
  </tbody>
</table>
<!-- RANDOM_BENCHMARKS_END -->

## Not yet benchmarked

`Xoshiro128StarStar` and `Xoshiro256StarStar` are absent from the tables above: the generated block
is rewritten only by a benchmark run, and none has happened since they were added. They appear on
the next scheduled run of the [Unity Benchmarks workflow](../../.github/workflows/unity-benchmarks.yml).

Their measured statistical standing does not depend on that run. Both clear the bit-plane linearity
gate on every output bit, which `XoroShiroRandom` does not — see
[Random Generators](../features/utilities/random-generators.md).
