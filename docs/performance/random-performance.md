# Random Number Generator Performance Benchmarks

> Auto-generated via RandomPerformanceTests.Benchmark. Run the test to refresh these summary and detail tables.

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
    <tr><td>LinearCongruentialGenerator</td><td align="right">1,323,700,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="5">Poor</td><td>Minimal standard LCG; fails spectral tests and exhibits lattice artifacts beyond small dimensions.</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">1,283,400,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="6">Experimental</td><td>Single-word chaotic generator; author notes period 2^64 but provides no formal test results—treat as experimental.</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">1,054,900,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="3">Good</td><td>Empirical PractRand testing to 32GB shows strong diffusion; designed as a chaotic ARX mixer rather than a proven statistically optimal generator.</td></tr>
    <tr><td>SplitMix64</td><td align="right">1,052,300,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="2">Very Good</td><td>Well-known SplitMix64 mixer; passes TestU01 BigCrush and PractRand up to large data sizes in literature. <a href="https://prng.di.unimi.it/splitmix64.c">Vigna 2014</a></td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">923,200,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Six-word ARX-style generator tuned for all-around use; passes TestU01 BigCrush per upstream reference implementation. <a href="https://github.com/wileylooper/flurryburst">Will Stafford Parsons (wileylooper)</a></td></tr>
    <tr><td>PcgRandom</td><td align="right">897,900,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>PCG XSH RR 64/32 variant; passes TestU01 BigCrush and PractRand in published results. <a href="https://www.pcg-random.org/paper.html">O&#39;Neill 2014</a></td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">754,700,000</td><td data-sort-value="4">Fast</td><td data-sort-value="2">Very Good</td><td>xoshiro128** variant; authors recommend for general-purpose use and report clean BigCrush performance with jump functions. <a href="https://prng.di.unimi.it/xoshiro128starstar.c">Blackman &amp; Vigna 2019</a></td></tr>
    <tr><td>IllusionFlow</td><td align="right">754,500,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Hybridized PCG + xorshift design; upstream PractRand 64GB passes with no anomalies per author.</td></tr>
    <tr><td>RomuDuo</td><td align="right">750,100,000</td><td data-sort-value="4">Fast</td><td data-sort-value="2">Very Good</td><td>ROMU family member (RomuDuo); authors report strong BigCrush results with minor low-bit weaknesses in some rotations.</td></tr>
    <tr><td>StormDropRandom</td><td align="right">705,200,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>20-word ARX generator derived from SHISHUA; author reports excellent PractRand performance and long periods.</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">602,100,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="4">Fair</td><td>Classic 32-bit xorshift; known to fail portions of TestU01 and PractRand, acceptable for lightweight effects only. <a href="https://doi.org/10.18637/jss.v008.i14">Marsaglia 2003</a></td></tr>
    <tr><td>WyRandom</td><td align="right">440,500,000</td><td data-sort-value="2">Slow</td><td data-sort-value="2">Very Good</td><td>Wyhash-based generator; published testing shows it clears BigCrush/PractRand with wide seed coverage. <a href="https://github.com/wangyi-fudan/wyhash">Wang Yi 2019</a></td></tr>
    <tr><td>SquirrelRandom</td><td align="right">414,000,000</td><td data-sort-value="2">Slow</td><td data-sort-value="3">Good</td><td>Hash-based generator built on Squirrel3; good equidistribution for table lookups but not extensively tested beyond moderate ranges. <a href="https://youtu.be/LWFzPP8ZbdU?t=2673">Squirrel Eiserloh</a></td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">261,100,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="1">Excellent</td><td>SHISHUA-inspired generator; independent testing (PractRand 128GB) by author indicates excellent distribution properties.</td></tr>
    <tr><td>UnityRandom</td><td align="right">87,600,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="4">Fair</td><td>Mirrors UnityEngine.Random (Xorshift196 + additive); suitable for legacy compatibility but not high-stakes simulation. <a href="https://docs.unity3d.com/ScriptReference/Random.html">UnityEngine.Random</a></td></tr>
    <tr><td>SystemRandom</td><td align="right">64,700,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Thin wrapper over System.Random; inherits same LCG weaknesses and fails modern statistical batteries. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
    <tr><td>DotNetRandom</td><td align="right">55,900,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Linear congruential generator (mod 2^31) with known correlation failures; unsuitable for high-quality simulations. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
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
