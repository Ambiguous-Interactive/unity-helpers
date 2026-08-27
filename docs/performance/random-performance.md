# Random Number Generator Performance Benchmarks

State repair for a generator restored from JSON or protobuf happens in its constructor or the shared
after-deserialization callback. This includes each generator's state, the shared bit/byte reservoirs
and GUID scratch buffer. Repair is not repeated in a draw method, so malformed/default serialized
state is repaired before the first draw without adding a guard to every later draw. PhotonSpin's
one-time warmup priming also happens there, leaving only its block-boundary check in the draw path.

## How the Speed column is measured

The ops/s columns are each generator measured on its own, one after another. That answers "how fast
is this generator here" and it is the wrong instrument for "which generator is faster": the roster
takes minutes to walk, and anything that changed on the machine in between lands on whichever
generator was being measured at the time.

The **Speed** column is measured differently. Every generator is compared against `IllusionFlow`
(what `PRNG.Instance` returns) in a fixed `ABBABAAB` batch, so each of the four readings that make
up a ratio sits next to the reading it is divided by, and both generators occupy the same mean
position in the batch. A drift that is linear across the batch cancels rather than being attributed
to one side. The four raw readings per side are kept, and their spread is what decides whether the
ratio is worth publishing: if the machine moved more than 3% between adjacent cycles, that
generator falls back to the un-paired number and the run says so.

So a Speed bucket is a claim about the generators; an ops/s figure is a claim about this machine on
that day.

## Two batteries, because they ask different questions

**PractRand** streams until something fails and reports the depth: a generator is "clean through
8GB" or it is not. **TestU01 SmallCrush** runs fifteen fixed statistics and reports a p-value for
each. A generator can be clean at 8GB of PractRand and still land a decisive p-value here, so
neither stands in for the other.

Both run against the same byte stream, from the same host, so a difference between them is a
difference between the batteries rather than between two ways of producing bytes.

Reading a SmallCrush result takes one piece of context: with fifteen statistics, a perfectly good
generator lands one p-value outside `[0.001, 0.9990]` roughly one run in seven. The threshold that
separates noise from signal is not close: the recorded weak control reports `eps` (below 1e-300),
while noise sits around 1e-4. Anything below **1e-10** is treated as a failure; anything above it is
reported and ignored. `IllusionFlow` produced a single 7.2e-4 at the manifest seed and was clean on
two other seeds, which is what that rule is for.

Whole-inventory SmallCrush, 2026-08-26, seed `00010203-0405-0607-0809-0a0b0c0d0e0f`, 908 MB and
6.3 s of CPU per generator:

- **Every generator rated `Good` or better passed.**
- Four recorded-weak generators failed decisively, most of their statistics at `eps`:
  `LinearCongruentialGenerator`, `WDoomRandom`, `WaveSplatRandom`, `XorShiftRandom`.
- Three recorded-weak generators passed: `DotNetRandom`, `SquirrelRandom`, `SystemRandom`.
  SmallCrush is the shallower instrument, so that is inconclusive rather than a contradiction of
  their rating.

For statistical batteries, the repository's
`Generator~/WallstopStudios.UnityHelpers.RandomQuality` host emits a reproducible little-endian byte
stream from an explicit generator, GUID seed and byte count. Long PractRand/TestU01 runs belong in
scheduled reporting with pinned tools and expected weak-generator failures; they are not suitable as
nondeterministic pull-request gates.

> The `NextUlong`, `NextLong` and `NextDouble` columns below predate the one-advance-per-64-bit-draw
> change to `BlastCircuitRandom`, `RomuDuo`, `SplitMix64`, `WyRandom` and `Xoshiro256StarStar`, which
> is measured at 2.49x on Unity 6000.4.6f1 (Mono). They refresh the next time
> `RandomPerformanceTests.Benchmark` runs.

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
    <tr><td>LinearCongruentialGenerator</td><td align="right">1,323,700,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="5">Poor</td><td>Numerical Recipes &#39;quick and dirty&#39; LCG (a=1664525, c=1013904223, m=2^32) returning the raw state, so bit k has period only 2^(k+1); measured linear complexity of bit k is 2^k+1, and bit 0 simply alternates. Cosmetic use only.</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">1,283,400,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="6">Experimental</td><td>Single-word chaotic generator; author notes period 2^64 but provides no formal test results, so treat it as experimental.</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">1,054,900,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="3">Good</td><td>Chaotic ARX mixer rather than a proven statistically optimal generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails.</td></tr>
    <tr><td>SplitMix64</td><td align="right">1,052,300,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="2">Very Good</td><td>Well-known SplitMix64 mixer; passes TestU01 BigCrush and PractRand up to large data sizes in literature. <a href="https://prng.di.unimi.it/splitmix64.c">Vigna 2014</a></td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">923,200,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Six-word ARX-style generator tuned for all-around use. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports TestU01 BigCrush passes; that run cannot be checked because the upstream repository is offline.</td></tr>
    <tr><td>PcgRandom</td><td align="right">897,900,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>PCG XSH RR 64/32 variant; passes TestU01 BigCrush and PractRand in published results. <a href="https://www.pcg-random.org/paper.html">O&#39;Neill 2014</a></td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">754,700,000</td><td data-sort-value="4">Fast</td><td data-sort-value="3">Good</td><td>xoroshiro128+, returning the high 32 bits, the half its authors recommend. The discarded low half is linear (bit 0 has linear complexity exactly 128); no output bit of the returned half is. A 64-bit draw costs two state advances, because a + scrambler has no strong 64-bit word to return. <a href="https://prng.di.unimi.it/xoroshiro128plus.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>IllusionFlow</td><td align="right">754,500,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Five-word rotate/xor/add generator driven by a 32-bit Weyl counter, and the generator PRNG.Instance returns. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 64GB; that run cannot be checked because the upstream repository is offline.</td></tr>
    <tr><td>RomuDuo</td><td align="right">750,100,000</td><td data-sort-value="4">Fast</td><td data-sort-value="3">Good</td><td>Published romuDuo: the ROMU multiplier with the ROTL(y,36) + ROTL(y,15) - x update. NextUint returns the low 32 bits of the 64-bit word. Throughput figures in this table predate that update rule and have not been re-measured. <a href="https://romu-random.org/code.c">Overton 2020</a></td></tr>
    <tr><td>StormDropRandom</td><td align="right">705,200,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>Large-state ARX generator over a 1024-word (4 KB) ring buffer with two 32-bit control words. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author&#39;s own results cannot be checked because the upstream repository is offline.</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">602,100,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="4">Fair</td><td>Classic 32-bit xorshift; known to fail portions of TestU01 and PractRand, acceptable for lightweight effects only. <a href="https://doi.org/10.18637/jss.v008.i14">Marsaglia 2003</a></td></tr>
    <tr><td>WyRandom</td><td align="right">440,500,000</td><td data-sort-value="2">Slow</td><td data-sort-value="2">Very Good</td><td>Wyhash-based generator; published testing shows it clears BigCrush/PractRand with wide seed coverage. <a href="https://github.com/wangyi-fudan/wyhash">Wang Yi 2019</a></td></tr>
    <tr><td>SquirrelRandom</td><td align="right">414,000,000</td><td data-sort-value="2">Slow</td><td data-sort-value="4">Fair</td><td>Hash-based generator built on Squirrel3. Measured: fails PractRand 0.95 FPF-14+6/16 at 1GB, reproducibly across four seeds. Good equidistribution for the table lookups it was designed for; not a general-purpose stream. <a href="https://youtu.be/LWFzPP8ZbdU?t=2673">Squirrel Eiserloh</a></td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">261,100,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="1">Excellent</td><td>SHISHUA-inspired generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 128GB; that run cannot be checked because the upstream repository is offline.</td></tr>
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

## Generators added since the last benchmark run

The tables above are rewritten only by a benchmark run, so a generator added since the last one is
absent until the `.github/workflows/unity-benchmarks.yml` workflow next runs.
Absence here says nothing about quality: statistical standing is measured separately, by the
bit-plane linearity gate on every pull request and by the scheduled PractRand battery. See
[Random Generators](../features/utilities/random-generators.md) for the current ratings.

The battery runs **both stream widths**. `NextUlong` is no longer `NextUint` rearranged: five
generators answer a 64-bit draw from one raw word, so half of it reaches a caller only through
`NextDouble`, `NextLong` and `NextUlong(max)` and appears in no 32-bit draw. Even the generators
that do build `NextUlong` from two `NextUint` draws pack them high-word-first and write
little-endian, so their 64-bit stream is the 32-bit one with each adjacent word pair swapped.
`SystemRandom` is the proof that this is not a redundant measurement: it fails the 32-bit battery at
exactly 8GB and is clean through 8GB at 64-bit. Every "clean through 8GB" above is the 32-bit
figure; the 64-bit outcomes are recorded per generator in
`scripts/random-quality/expected-outcomes.json`.

## Refreshing these numbers

Run `RandomPerformanceTests.Benchmark` from Unity's Test Runner, or let the weekly `Unity Benchmarks` workflow do it. Both rewrite the tables in place.
