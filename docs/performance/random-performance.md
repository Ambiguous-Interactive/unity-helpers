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

The outcome manifest must match the host's live `--list` inventory; the ordinary stream contract
checks that equality before the scheduled batteries run. An expected pass with `cleanThrough: null`
is an unmeasured hypothesis. A clean report remains **INCONCLUSIVE** until its baseline is reviewed
and recorded, while a definitive failure still fails the report. `Sfc64Random` currently has this
unmeasured status at both widths; its deterministic bit-plane checks establish no PractRand depth.
Reports must carry matching battery and stream headers plus complete blocks with positive test
results. Empty, malformed or truncated output is an error. Clean output must reach the requested byte
budget; a complete early failure remains conclusive. Control discrimination uses the reported depth,
and a clean run below a recorded passing baseline remains inconclusive.

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
    <tr><td>LinearCongruentialGenerator</td><td align="right">1,238,600,000</td><td data-sort-value="6">Fastest</td><td data-sort-value="5">Poor</td><td>Numerical Recipes &#39;quick and dirty&#39; LCG (a=1664525, c=1013904223, m=2^32) returning the raw state, so bit k has period only 2^(k+1) -- measured linear complexity of bit k is 2^k+1, and bit 0 simply alternates. Cosmetic use only.</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">1,126,300,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="6">Experimental</td><td>Single-word chaotic generator; author notes period 2^64 but provides no formal test results—treat as experimental.</td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">1,024,600,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="3">Good</td><td>xoroshiro128+, returning the high 32 bits -- the half its authors recommend. The discarded low half is linear (bit 0 has linear complexity exactly 128); no output bit of the returned half is. A 64-bit draw costs two state advances, because a + scrambler has no strong 64-bit word to return. <a href="https://prng.di.unimi.it/xoroshiro128plus.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">1,023,900,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="3">Good</td><td>Chaotic ARX mixer rather than a proven statistically optimal generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails.</td></tr>
    <tr><td>RomuDuo</td><td align="right">943,800,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="3">Good</td><td>Published romuDuo: the ROMU multiplier with the ROTL(y,36) + ROTL(y,15) - x update. NextUint returns the low 32 bits of the 64-bit word. <a href="https://arxiv.org/pdf/2002.11331">Overton 2020</a></td></tr>
    <tr><td>Sfc64Random</td><td align="right">941,700,000</td><td data-sort-value="5">Very Fast</td><td data-sort-value="2">Very Good</td><td>sfc64 (Small Fast Chaotic): three 64-bit words plus a draw counter, seeded by the canonical twelve-draw warm-up. NextUint returns the upper half of the output word, where every mixed bit arrives. <a href="https://gist.github.com/imneme/f1f7821f07cf76504a97f6537c818083">O&#39;Neill 2018 (Doty-Humphrey&#39;s SFC)</a></td></tr>
    <tr><td>SplitMix64</td><td align="right">912,200,000</td><td data-sort-value="4">Fast</td><td data-sort-value="2">Very Good</td><td>SplitMix64 mixer; Vigna reports passing TestU01 BigCrush. <a href="https://prng.di.unimi.it/splitmix64.c">Vigna 2015</a></td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">905,800,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Six-word ARX-style generator tuned for all-around use. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports TestU01 BigCrush passes; that run cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>PcgRandom</td><td align="right">896,800,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>PCG XSH RR 64/32 variant; passes TestU01 BigCrush and PractRand in published results. <a href="https://www.pcg-random.org/paper.html">O&#39;Neill 2014</a></td></tr>
    <tr><td>IllusionFlow</td><td align="right">871,600,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Five-word rotate/xor/add generator driven by a 32-bit Weyl counter. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 64GB; that run cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">862,300,000</td><td data-sort-value="4">Fast</td><td data-sort-value="4">Fair</td><td>Classic 32-bit xorshift; known to fail portions of TestU01 and PractRand, acceptable for lightweight effects only. <a href="https://doi.org/10.18637/jss.v008.i14">Marsaglia 2003</a></td></tr>
    <tr><td>StormDropRandom</td><td align="right">696,800,000</td><td data-sort-value="4">Fast</td><td data-sort-value="1">Excellent</td><td>Large-state ARX generator over a 1024-word (4 KB) ring buffer with two 32-bit control words. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author&#39;s own results cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>Xoshiro256StarStar</td><td align="right">606,600,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>xoshiro256** 1.0; the ** scrambler leaves no weak bit, and the native 64-bit word means NextUlong costs one state advance instead of the two every other 64-bit generator here needs. <a href="https://prng.di.unimi.it/xoshiro256starstar.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>Xoshiro128StarStar</td><td align="right">599,100,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="1">Excellent</td><td>xoshiro128** 1.1; the ** scrambler leaves no weak bit, so NextBool and low-bit masks are as strong as the full word. Native 32-bit output, so NextUint discards nothing. <a href="https://prng.di.unimi.it/xoshiro128starstar.c">Blackman &amp; Vigna 2018</a></td></tr>
    <tr><td>WyRandom</td><td align="right">441,400,000</td><td data-sort-value="3">Moderate</td><td data-sort-value="2">Very Good</td><td>Wyhash v1 .NET port; raw 64-bit draws match cocowalla&#39;s WyRng for the same ulong seed. <a href="https://github.com/cocowalla/wyhash-dotnet">cocowalla 2019 (Wang Yi&#39;s wyrand)</a></td></tr>
    <tr><td>SquirrelRandom</td><td align="right">406,400,000</td><td data-sort-value="2">Slow</td><td data-sort-value="4">Fair</td><td>Hash-based generator built on Squirrel3. Measured: fails PractRand 0.95 FPF-14+6/16 at 1GB, reproducibly across four seeds. Good equidistribution for the table lookups it was designed for; not a general-purpose stream. <a href="https://youtu.be/LWFzPP8ZbdU?t=2673">Squirrel Eiserloh</a></td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">254,100,000</td><td data-sort-value="2">Slow</td><td data-sort-value="1">Excellent</td><td>SHISHUA-inspired generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 128GB; that run cannot be checked -- the upstream repository is offline.</td></tr>
    <tr><td>WDoomRandom</td><td align="right">200,100,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Index-into-array generator over a fixed 1024-entry table of 32-bit values. One entry serves one NextUint, so the period is exactly 1024 draws. Deterministic by design, not statistically random.</td></tr>
    <tr><td>UnityRandom</td><td align="right">131,000,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="4">Fair</td><td>Mirrors UnityEngine.Random, documented by Unity as Xorshift 128; suitable for legacy compatibility but not high-stakes simulation. <a href="https://docs.unity3d.com/ScriptReference/Random.html">UnityEngine.Random</a></td></tr>
    <tr><td>SystemRandom</td><td align="right">63,200,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Reimplements the classic .NET System.Random algorithm (Knuth subtractive lagged-Fibonacci, mod 2^31-1) so its sequence is serializable and stable across runtimes. Fails modern statistical batteries. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
    <tr><td>DotNetRandom</td><td align="right">58,700,000</td><td data-sort-value="1">Very Slow</td><td data-sort-value="5">Poor</td><td>Wraps System.Random, which on Mono and for seeded .NET Core is Knuth&#39;s subtractive lagged-Fibonacci generator (mod 2^31-1), not an LCG. The sequence is runtime-dependent, so do not rely on it for cross-platform determinism. <a href="https://nullprogram.com/blog/2017/09/21/">System.Random considered harmful</a></td></tr>
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
    <tr><td>LinearCongruentialGenerator</td><td align="right">777,900,000</td><td align="right">676,100,000</td><td align="right">1,238,600,000</td><td align="right">171,900,000</td><td align="right">369,900,000</td><td align="right">492,500,000</td><td align="right">391,400,000</td></tr>
    <tr><td>WaveSplatRandom</td><td align="right">773,400,000</td><td align="right">591,500,000</td><td align="right">1,126,300,000</td><td align="right">173,600,000</td><td align="right">380,000,000</td><td align="right">438,000,000</td><td align="right">399,500,000</td></tr>
    <tr><td>XoroShiroRandom</td><td align="right">767,000,000</td><td align="right">552,600,000</td><td align="right">1,024,600,000</td><td align="right">166,900,000</td><td align="right">347,400,000</td><td align="right">423,800,000</td><td align="right">363,900,000</td></tr>
    <tr><td>BlastCircuitRandom</td><td align="right">750,900,000</td><td align="right">525,800,000</td><td align="right">1,023,900,000</td><td align="right">166,700,000</td><td align="right">580,000,000</td><td align="right">410,100,000</td><td align="right">339,900,000</td></tr>
    <tr><td>RomuDuo</td><td align="right">778,700,000</td><td align="right">554,300,000</td><td align="right">943,800,000</td><td align="right">169,000,000</td><td align="right">580,800,000</td><td align="right">428,300,000</td><td align="right">389,100,000</td></tr>
    <tr><td>Sfc64Random</td><td align="right">742,800,000</td><td align="right">537,200,000</td><td align="right">941,700,000</td><td align="right">164,600,000</td><td align="right">566,700,000</td><td align="right">392,300,000</td><td align="right">352,200,000</td></tr>
    <tr><td>SplitMix64</td><td align="right">768,600,000</td><td align="right">571,400,000</td><td align="right">912,200,000</td><td align="right">164,300,000</td><td align="right">561,600,000</td><td align="right">411,700,000</td><td align="right">379,600,000</td></tr>
    <tr><td>FlurryBurstRandom</td><td align="right">749,300,000</td><td align="right">546,800,000</td><td align="right">905,800,000</td><td align="right">172,000,000</td><td align="right">293,200,000</td><td align="right">386,100,000</td><td align="right">340,300,000</td></tr>
    <tr><td>PcgRandom</td><td align="right">754,600,000</td><td align="right">545,900,000</td><td align="right">896,800,000</td><td align="right">165,600,000</td><td align="right">321,100,000</td><td align="right">376,200,000</td><td align="right">361,000,000</td></tr>
    <tr><td>IllusionFlow</td><td align="right">775,800,000</td><td align="right">521,800,000</td><td align="right">871,600,000</td><td align="right">165,700,000</td><td align="right">296,400,000</td><td align="right">371,900,000</td><td align="right">338,100,000</td></tr>
    <tr><td>XorShiftRandom</td><td align="right">756,400,000</td><td align="right">620,200,000</td><td align="right">862,300,000</td><td align="right">173,200,000</td><td align="right">364,300,000</td><td align="right">424,800,000</td><td align="right">365,900,000</td></tr>
    <tr><td>StormDropRandom</td><td align="right">744,600,000</td><td align="right">458,000,000</td><td align="right">696,800,000</td><td align="right">166,600,000</td><td align="right">253,600,000</td><td align="right">341,900,000</td><td align="right">309,800,000</td></tr>
    <tr><td>Xoshiro256StarStar</td><td align="right">738,900,000</td><td align="right">461,900,000</td><td align="right">606,600,000</td><td align="right">175,100,000</td><td align="right">539,700,000</td><td align="right">354,900,000</td><td align="right">320,200,000</td></tr>
    <tr><td>Xoshiro128StarStar</td><td align="right">760,000,000</td><td align="right">470,300,000</td><td align="right">599,100,000</td><td align="right">166,300,000</td><td align="right">264,900,000</td><td align="right">359,300,000</td><td align="right">324,800,000</td></tr>
    <tr><td>WyRandom</td><td align="right">744,200,000</td><td align="right">338,400,000</td><td align="right">441,400,000</td><td align="right">157,900,000</td><td align="right">360,200,000</td><td align="right">264,600,000</td><td align="right">228,100,000</td></tr>
    <tr><td>SquirrelRandom</td><td align="right">739,500,000</td><td align="right">391,400,000</td><td align="right">406,400,000</td><td align="right">162,400,000</td><td align="right">191,100,000</td><td align="right">302,800,000</td><td align="right">288,600,000</td></tr>
    <tr><td>PhotonSpinRandom</td><td align="right">688,600,000</td><td align="right">228,800,000</td><td align="right">254,100,000</td><td align="right">116,300,000</td><td align="right">112,800,000</td><td align="right">201,700,000</td><td align="right">184,000,000</td></tr>
    <tr><td>WDoomRandom</td><td align="right">698,400,000</td><td align="right">210,400,000</td><td align="right">200,100,000</td><td align="right">138,200,000</td><td align="right">105,900,000</td><td align="right">207,700,000</td><td align="right">200,600,000</td></tr>
    <tr><td>UnityRandom</td><td align="right">686,200,000</td><td align="right">122,000,000</td><td align="right">131,000,000</td><td align="right">78,500,000</td><td align="right">60,000,000</td><td align="right">117,800,000</td><td align="right">119,300,000</td></tr>
    <tr><td>SystemRandom</td><td align="right">138,300,000</td><td align="right">142,900,000</td><td align="right">63,200,000</td><td align="right">128,400,000</td><td align="right">131,100,000</td><td align="right">57,800,000</td><td align="right">56,800,000</td></tr>
    <tr><td>DotNetRandom</td><td align="right">531,500,000</td><td align="right">54,800,000</td><td align="right">58,700,000</td><td align="right">44,800,000</td><td align="right">27,400,000</td><td align="right">53,100,000</td><td align="right">44,600,000</td></tr>
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

## Raw-stream compatibility

`npm run test:random-quality-stream` checks frozen raw-output vectors for all 20 managed
generators in the standalone host. Each generator has two fixed seeds and both 32-bit and
64-bit streams: 80 cases, each checking its first 256 bytes and the SHA-256 of 1 MiB.
The vectors come from main commit `14adde00a4e372a387dd2a3a3a845b47b7078cc6` and are stored in
`scripts/random-quality/raw-stream-vectors.json`. Preserve them when changing bounded sampling;
an intentional raw-stream change requires an explicit compatibility decision.

`RandomRawStreamCompatibilityTests` runs all 80 one-MiB hashes inside Unity, using direct
constructors so the cases also reach IL2CPP and WebGL test players. The host gate checks exact
parity between the Unity cases and the frozen JSON inventory. A host pass does not prove a player
pass: retain the executed fixture results for each Unity version and backend in the campaign.

The two generators outside that host inventory have separate Unity fixtures:

| Generator             | Raw compatibility check                                                        | Continuation check                                                                                      |
| --------------------- | ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------- |
| 20 managed generators | 80 frozen hashes, two seeds and both widths                                    | Snapshot, copy, JSON and protobuf fixtures; generated protobuf runs on IL2CPP                           |
| NativePcgRandom       | Existing integer-seed golden words at both widths                              | Value copies preserve partially consumed bit reservoirs; no RandomState or protobuf API                 |
| UnityRandom           | Both widths match the current engine's integer draws and final engine position | Mixed snapshot, copy, JSON and generated protobuf continuation with cached Gaussian, bit and byte state |

UnityRandom's adapter check intentionally follows the running engine. It does not promise identical
Unity engine streams or snapshot payloads across engine versions. JSON and direct protobuf-net
oracle cases that require unsupported reflection remain separately marked on IL2CPP. Raw hashes
and continuation checks establish compatibility, not statistical quality, throughput or allocation
performance; the paired player campaign and WebGL execution remain separate acceptance items.
WDoomRandom intentionally skips the inherited statistical fixture; its snapshot, copy and protobuf
and JSON paths have dedicated coverage, including mixed draws with empty and primed caches.

## Refreshing these numbers

Run `RandomPerformanceTests.Benchmark` from Unity's Test Runner, or dispatch the `Unity Benchmarks`
workflow manually; refreshed results land through the protected-branch perf-results commit. Both rewrite the tables in place.
