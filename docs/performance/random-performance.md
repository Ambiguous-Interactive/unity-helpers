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
(the speed baseline, measured in the same `ABBABAAB` batch) so each of the four readings that make
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
    <tr><td>LinearCongruentialGenerator</td><td align="right">1,010,900,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="5">Poor</td><td>Numerical Recipes &#39;quick and dirty&#39; LCG (a=1664525, c=1013904223, m=2^32) returning the raw state, so bit k has period only 2^(k+1) -- measured linear complexity of bit k is 2^k+1, and bit 0 simply alternates. Cosmetic use only.</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">829,000,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="6">Experimental</td><td>Single-word chaotic generator; author notes period 2^64 but provides no formal test results—treat as experimental.</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">742,700,000</td><td data-sort-value="4">Fast</td><td data-sort-value="4">Fair</td><td>Classic 32-bit xorshift; known to fail portions of TestU01 and PractRand, acceptable for lightweight effects only. <a href="https://doi.org/10.18637/jss.v008.i14">Marsaglia 2003</a></td></tr>
    <tr><td>RomuDuo</td><td align="right">702,200,000</td><td data-sort-value="4">Fast</td><td data-sort-value="3">Good</td><td>Published romuDuo: the ROMU multiplier with the ROTL(y,36) + ROTL(y,15) - x update. NextUint returns the low 32 bits of the 64-bit word. <a href="https://romu-random.org/code.c">Overton 2020</a></td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">704,000,000</td><td data-sort-value="4">Fast</td><td data-sort-value="3">Good</td><td>xoroshiro128+, returning the high 32 bits -- the half its authors recommend. The discarded low half is linear (bit 0 has linear complexity exactly 128); no output bit of the returned half is. A 64-bit draw costs two state advances, because a + scrambler has no strong 64-bit word to return. <a href="https://prng.di.unimi.it/xoroshiro128plus.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>SplitMix64</td><td align="right">701,500,000</td><td data-sort-value="4">Fast</td><td data-sort-value="2">Very Good</td><td>Well-known SplitMix64 mixer; passes TestU01 BigCrush and PractRand up to large data sizes in literature. <a href="https://prng.di.unimi.it/splitmix64.c">Vigna 2014</a></td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">605,200,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Six-word ARX-style generator tuned for all-around use. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports TestU01 BigCrush passes; that run cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">594,900,000</td><td data-sort-value="4">Fast</td><td data-sort-value="3">Good</td><td>Chaotic ARX mixer rather than a proven statistically optimal generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails.</td></tr>
    <tr><td>PcgRandom</td><td align="right">590,800,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>PCG XSH RR 64/32 variant; passes TestU01 BigCrush and PractRand in published results. <a href="https://www.pcg-random.org/paper.html">O&#39;Neill 2014</a></td></tr>
    <tr><td>Sfc64Random</td><td align="right">527,500,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="2">Very Good</td><td>sfc64 (Small Fast Chaotic): three 64-bit words plus a draw counter, seeded by the canonical twelve-draw warm-up. NextUint returns the upper half of the output word, where every mixed bit arrives. <a href="https://gist.github.com/imneme/f1f7821f07cf76504a97f6537c818083">O&#39;Neill 2018 (Doty-Humphrey&#39;s SFC)</a></td></tr>
    <tr><td>Xoshiro256StarStar</td><td align="right">503,500,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>xoshiro256** 1.0; the ** scrambler leaves no weak bit, and the native 64-bit word means NextUlong costs one state advance instead of the two every other 64-bit generator here needs. <a href="https://prng.di.unimi.it/xoshiro256starstar.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>StormDropRandom</td><td align="right">501,200,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>Large-state ARX generator over a 1024-word (4 KB) ring buffer with two 32-bit control words. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author&#39;s own results cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>IllusionFlow</td><td align="right">500,500,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>Five-word rotate/xor/add generator driven by a 32-bit Weyl counter. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 64GB; that run cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>Xoshiro128StarStar</td><td align="right">482,000,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>xoshiro128** 1.1; the ** scrambler leaves no weak bit, so NextBool and low-bit masks are as strong as the full word. Native 32-bit output, so NextUint discards nothing. <a href="https://prng.di.unimi.it/xoshiro128starstar.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>SquirrelRandom</td><td align="right">351,100,000</td><td data-sort-value="2">Slow</td><td data-sort-value="4">Fair</td><td>Hash-based generator built on Squirrel3. Measured: fails PractRand 0.95 FPF-14+6/16 at 1GB, reproducibly across four seeds. Good equidistribution for the table lookups it was designed for; not a general-purpose stream. <a href="https://youtu.be/LWFzPP8ZbdU?t=2673">Squirrel Eiserloh</a></td></tr>
    <tr><td>WyRandom</td><td align="right">299,500,000</td><td data-sort-value="2">Slow</td><td data-sort-value="2">Very Good</td><td>Wyhash-based generator; published testing shows it clears BigCrush/PractRand with wide seed coverage. <a href="https://github.com/wangyi-fudan/wyhash">Wang Yi 2019</a></td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">246,000,000</td><td data-sort-value="2">Slow</td><td data-sort-value="1">Excellent</td><td>SHISHUA-inspired generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 128GB; that run cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>WDoomRandom</td><td align="right">195,400,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Index-into-array generator over a fixed 1024-entry table of 32-bit values. One entry serves one NextUint, so the period is exactly 1024 draws. Deterministic by design, not statistically random.</td></tr>
    <tr><td>UnityRandom</td><td align="right">113,800,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="4">Fair</td><td>Mirrors UnityEngine.Random, documented by Unity as Xorshift 128; suitable for legacy compatibility but not high-stakes simulation. <a href="https://docs.unity3d.com/ScriptReference/Random.html">UnityEngine.Random</a></td></tr>
    <tr><td>SystemRandom</td><td align="right">59,200,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Reimplements the classic .NET System.Random algorithm (Knuth subtractive lagged-Fibonacci, mod 2^31-1) so its sequence is serializable and stable across runtimes. Fails modern statistical batteries. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
    <tr><td>DotNetRandom</td><td align="right">47,000,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Wraps System.Random, which on Mono and for seeded .NET Core is Knuth&#39;s subtractive lagged-Fibonacci generator (mod 2^31-1), not an LCG. The sequence is runtime-dependent, so do not rely on it for cross-platform determinism. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
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
    <tr><td>LinearCongruentialGenerator</td><td align="right">717,900,000</td><td align="right">507,500,000</td><td align="right">1,010,900,000</td><td align="right">180,700,000</td><td align="right">275,300,000</td><td align="right">380,700,000</td><td align="right">302,600,000</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">712,700,000</td><td align="right">413,800,000</td><td align="right">829,000,000</td><td align="right">175,400,000</td><td align="right">246,700,000</td><td align="right">350,100,000</td><td align="right">304,100,000</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">711,000,000</td><td align="right">474,500,000</td><td align="right">742,700,000</td><td align="right">173,200,000</td><td align="right">235,000,000</td><td align="right">326,500,000</td><td align="right">304,100,000</td></tr>
    <tr><td>RomuDuo</td><td align="right">700,500,000</td><td align="right">454,500,000</td><td align="right">702,200,000</td><td align="right">169,600,000</td><td align="right">414,100,000</td><td align="right">324,500,000</td><td align="right">303,600,000</td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">715,800,000</td><td align="right">455,400,000</td><td align="right">704,000,000</td><td align="right">169,500,000</td><td align="right">222,700,000</td><td align="right">326,700,000</td><td align="right">285,900,000</td></tr>
    <tr><td>SplitMix64</td><td align="right">727,600,000</td><td align="right">455,500,000</td><td align="right">701,500,000</td><td align="right">168,800,000</td><td align="right">413,000,000</td><td align="right">324,800,000</td><td align="right">301,800,000</td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">731,100,000</td><td align="right">412,000,000</td><td align="right">605,200,000</td><td align="right">162,700,000</td><td align="right">197,800,000</td><td align="right">282,800,000</td><td align="right">283,200,000</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">628,900,000</td><td align="right">454,800,000</td><td align="right">594,900,000</td><td align="right">169,200,000</td><td align="right">396,100,000</td><td align="right">302,700,000</td><td align="right">284,900,000</td></tr>
    <tr><td>PcgRandom</td><td align="right">719,100,000</td><td align="right">414,300,000</td><td align="right">590,800,000</td><td align="right">163,400,000</td><td align="right">198,500,000</td><td align="right">285,500,000</td><td align="right">285,000,000</td></tr>
    <tr><td>Sfc64Random</td><td align="right">708,900,000</td><td align="right">414,600,000</td><td align="right">527,500,000</td><td align="right">162,500,000</td><td align="right">380,300,000</td><td align="right">302,300,000</td><td align="right">285,800,000</td></tr>
    <tr><td>Xoshiro256StarStar</td><td align="right">615,800,000</td><td align="right">380,200,000</td><td align="right">503,500,000</td><td align="right">156,900,000</td><td align="right">343,700,000</td><td align="right">283,200,000</td><td align="right">260,400,000</td></tr>
    <tr><td>StormDropRandom</td><td align="right">705,800,000</td><td align="right">353,200,000</td><td align="right">501,200,000</td><td align="right">154,400,000</td><td align="right">178,000,000</td><td align="right">268,900,000</td><td align="right">247,700,000</td></tr>
    <tr><td>IllusionFlow</td><td align="right">706,200,000</td><td align="right">412,200,000</td><td align="right">500,500,000</td><td align="right">161,600,000</td><td align="right">198,500,000</td><td align="right">294,500,000</td><td align="right">268,400,000</td></tr>
    <tr><td>Xoshiro128StarStar</td><td align="right">672,800,000</td><td align="right">380,200,000</td><td align="right">482,000,000</td><td align="right">160,000,000</td><td align="right">189,900,000</td><td align="right">285,200,000</td><td align="right">268,300,000</td></tr>
    <tr><td>SquirrelRandom</td><td align="right">720,200,000</td><td align="right">323,800,000</td><td align="right">351,100,000</td><td align="right">144,100,000</td><td align="right">161,000,000</td><td align="right">247,400,000</td><td align="right">239,700,000</td></tr>
    <tr><td>WyRandom</td><td align="right">672,100,000</td><td align="right">237,900,000</td><td align="right">299,500,000</td><td align="right">126,500,000</td><td align="right">230,100,000</td><td align="right">200,000,000</td><td align="right">185,200,000</td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">663,300,000</td><td align="right">207,500,000</td><td align="right">246,000,000</td><td align="right">113,000,000</td><td align="right">101,100,000</td><td align="right">165,400,000</td><td align="right">158,000,000</td></tr>
    <tr><td>WDoomRandom</td><td align="right">694,800,000</td><td align="right">145,100,000</td><td align="right">195,400,000</td><td align="right">106,400,000</td><td align="right">74,800,000</td><td align="right">163,700,000</td><td align="right">156,700,000</td></tr>
    <tr><td>UnityRandom</td><td align="right">623,800,000</td><td align="right">100,000,000</td><td align="right">113,800,000</td><td align="right">74,100,000</td><td align="right">48,400,000</td><td align="right">95,100,000</td><td align="right">94,200,000</td></tr>
    <tr><td>SystemRandom</td><td align="right">130,600,000</td><td align="right">143,800,000</td><td align="right">59,200,000</td><td align="right">113,200,000</td><td align="right">116,900,000</td><td align="right">53,400,000</td><td align="right">52,000,000</td></tr>
    <tr><td>DotNetRandom</td><td align="right">494,600,000</td><td align="right">44,900,000</td><td align="right">47,000,000</td><td align="right">36,500,000</td><td align="right">22,000,000</td><td align="right">42,500,000</td><td align="right">41,800,000</td></tr>
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
