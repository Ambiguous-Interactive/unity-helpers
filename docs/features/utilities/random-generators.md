# Random Number Generators

<!-- cspell:ignore PRD Prd -->

**TL;DR:** Use `PRNG.Instance` for 10-15x faster random generation than `UnityEngine.Random`, with a rich API for vectors, colors, weighted selection, and more.

---

## Overview

Unity Helpers provides 20+ high-performance pseudo-random number generators (PRNGs) through a unified `IRandom` interface. Each generator carries a `[RandomGeneratorMetadata]` quality rating spanning fast-but-weak toys through generators that clear BigCrush. Check the rating before choosing; the table below summarizes.

### Key Features

- **10-15x faster** than `UnityEngine.Random` (see [benchmarks](../../performance/random-performance.md))
- **Thread-safe** access via `PRNG.Instance` (thread-local)
- **Rich API**: vectors, colors, Gaussian distributions, weighted selection, subset sampling
- **Feel-good randomness**: exact-average PRD, pity timers, and weighted shuffle bags
- **Seedable**: reproducible results for replays and testing
- **IL2CPP compatible**: no reflection, AOT-safe

---

## Quick Start (60 Seconds)

```csharp
using WallstopStudios.UnityHelpers.Core.Random;

// Use the thread-local default (fastest)
IRandom random = PRNG.Instance;

// Basic generation
int number = random.Next(0, 100);           // [0, 100)
float value = random.NextFloat();            // [0.0, 1.0)
bool coinFlip = random.NextBool();
uint bits = random.NextUint();

// Unity vectors
Vector2 point2D = random.NextVector2(-10f, 10f);

// Colors
Color randomColor = random.NextColor();

// Weighted selection
string[] items = { "Common", "Rare", "Epic" };
float[] weights = { 70f, 25f, 5f };
string selected = random.NextWeighted(items.Zip(weights, (x, y) => (x, y)));

// Gaussian distribution
float normalValue = random.NextGaussian(mean: 0f, stdDev: 1f);
```

---

## Choosing a Generator

| Use Case                    | Recommended Generator | Why                                            |
| --------------------------- | --------------------- | ---------------------------------------------- |
| **General gameplay**        | `PRNG.Instance`       | Thread-local default, excellent quality        |
| **Procedural generation**   | `PcgRandom`           | Reproducible, excellent statistical properties |
| **High-throughput effects** | `SplitMix64`          | Fastest with good quality                      |
| **Cryptographic seeding**   | N/A                   | Use `System.Security.Cryptography` instead     |
| **Legacy compatibility**    | `UnityRandom`         | Matches `UnityEngine.Random` behavior          |

### Saving and restoring a generator

Every generator answers `InternalState` with a `RandomState` snapshot, and every generator has a
constructor that takes one back. Snapshot mid-stream, store the snapshot in your save file, and the
restored generator resumes the exact sequence, verified for all of them by
`GeneratorSnapshotRestoreTests`.

`UnityRandom` resumes too, and it is worth knowing how. Its position belongs to
`UnityEngine.Random`'s engine globals rather than to the object, so the snapshot carries that position
and restoring one **writes `UnityEngine.Random.state` back**. Anything else drawing from
`UnityEngine.Random` is moved with it, which is the same global that `new UnityRandom(seed)` already
resets through `InitState`. A snapshot written before 3.6 carries no position; restoring one of those
leaves the engine exactly where it is, and so does a payload that is not an engine position at all;
assigning one would leave `UnityEngine.Random` stuck returning a single value for the rest of the run.

`UnityRandom` snapshots and `Copy()` retain cached Gaussian samples, including zero, alongside the
bit and byte reservoirs. Saving a `RandomState` through JSON or protobuf preserves those reservoirs
as well. Copies still share the engine's global stream; they do not become independent generators.
The snapshot's second state word now distinguishes seeded and unseeded snapshots, leaving the
Gaussian field for the actual cached sample. Older snapshots used that field as a seed marker:
loading them preserves the seed metadata and available engine position, but clears the marker
instead of returning a fabricated Gaussian zero. A cached Gaussian discarded by an older snapshot
cannot be recovered ([#728](https://github.com/Ambiguous-Interactive/unity-helpers/issues/728)).

---

## Available Generators

All generators implement the `IRandom` interface:

| Generator                     | Speed     | Quality      | Period                                                                                      | Best For                                                                                   |
| ----------------------------- | --------- | ------------ | ------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `LinearCongruentialGenerator` | Fastest   | Poor         | 2^32; bit k only 2^(k+1)                                                                    | Non-critical effects only                                                                  |
| `WaveSplatRandom`             | Very Fast | Experimental | 2^64 (author's claim, unverified)                                                           | Throwaway effects; no formal test results published                                        |
| `SplitMix64`                  | Fast      | Very Good    | 2^64 (published)                                                                            | High-throughput generation                                                                 |
| `BlastCircuitRandom`          | Fast      | Good         | unpublished; 251/256 state bits live (measured)                                             | Bulk effects, chaotic mixing                                                               |
| `PcgRandom`                   | Fast      | Excellent    | 2^64 (published)                                                                            | General purpose, seeded generation                                                         |
| `FlurryBurstRandom`           | Fast      | Excellent    | unpublished; 192/192 state bits live (measured)                                             | All-around alternative to PCG                                                              |
| `IllusionFlow`                | Moderate  | Excellent    | unpublished; 108/160 state bits live (measured)                                             | Balanced speed and quality                                                                 |
| `XoroShiroRandom`             | Fast      | Good         | 2^128-1 (published)                                                                         | Bulk placement, shuffles, procedural noise                                                 |
| `RomuDuo`                     | Fast      | Good         | no guaranteed period (Romu is non-linear); 128/128 state bits live (measured)               | Alternative to PCG                                                                         |
| `Sfc64Random`                 | Moderate  | Very Good    | unpublished; counter forbids a repeat before 2^64 draws; 204/256 state bits live (measured) | Published-pedigree general purpose; one advance per 64 bits                                |
| `Xoshiro128StarStar`          | Moderate  | Excellent    | 2^128-1 (published)                                                                         | `NextBool`/low-bit masks; WebGL and other 32-bit targets                                   |
| `Xoshiro256StarStar`          | Moderate  | Excellent    | 2^256-1 (published)                                                                         | `NextDouble`/`NextUlong`-heavy work (one advance per 64 bits); the `PRNG.Instance` default |
| `StormDropRandom`             | Moderate  | Excellent    | unpublished; 28,407 state bits live (measured)                                              | Long streams from a large 1024-word state                                                  |
| `XorShiftRandom`              | Fast      | Fair         | 2^32-1 (published)                                                                          | Legacy compatibility                                                                       |
| `WyRandom`                    | Slow      | Very Good    | 2^64 (published)                                                                            | Hash-based scenarios                                                                       |
| `SquirrelRandom`              | Slow      | Fair         | 2^32 (32-bit position counter)                                                              | Noise-based generation                                                                     |
| `PhotonSpinRandom`            | Slow      | Excellent    | unpublished; 734 state bits live (measured)                                                 | Maximum quality needed                                                                     |
| `UnityRandom`                 | Very Slow | Fair         | 2^128-1 (Unity documents Xorshift 128)                                                      | Match Unity behavior                                                                       |
| `SystemRandom`                | Very Slow | Poor         | unpublished; 1,717 state bits live (measured)                                               | .NET compatibility                                                                         |
| `DotNetRandom`                | Very Slow | Poor         | runtime-dependent (System.Random); not fixed                                                | Bridging `System.Random` code to `IRandom`                                                 |
| `WDoomRandom`                 | Very Slow | Poor         | 1024 draws (measured: 10 state bits live)                                                   | Retro feel, deterministic replays                                                          |

### Reading the Period column

A period of 2^128 cannot be observed, so every value in that column is a claim and the column says
whose. `(published)` quotes the algorithm's specification. `(author's claim, unverified)` is a
number the upstream author states that nothing here has checked. And where nothing is published at
all, the value reports what **was** measured instead: `state bits live (measured)` is the count of
state bits observed to change over 3,000 `NextUint()` draws, out of the generator's declared state
width.

Read a live-state count as a **lower bound on state width, not a period**. Bits that did not move
in 3,000 draws may still be live, and a wide state does not by itself guarantee a long cycle. It is
there because it is the strongest honest statement available for a generator whose author published
no period, and because four `Excellent` ratings in this roster came from repositories that are now
offline, so a quoted period with no source is exactly the failure this column is meant to avoid.

Each generator declares its own value through `[RandomGeneratorMetadata(period: "...")]`, readable
at run time as `RandomGeneratorMetadataRegistry.Snapshot(type).Period`. A contract test fails the
build if a generator declares none, or if this table and the annotation disagree.

Both `**` generators are rated `Excellent`: the scrambler leaves no weak output bit, so unlike the
`+` scramblers they are safe for `NextBool` and low-bit masks.

### One state advance per 64-bit draw

`NextUlong()`, and therefore `NextLong()`, `NextDouble()` and `NextUlong(max)`, used to cost **two**
state advances on every generator: the shared base class built a 64-bit value out of two 32-bit
draws. `BlastCircuitRandom`, `RomuDuo`, `Sfc64Random`, `SplitMix64`, `WyRandom` and
`Xoshiro256StarStar` each compute a whole 64-bit word internally, so they answer a 64-bit draw with
one advance and return that word directly, measured at **2.49x** on Unity 6000.4.6f1 (Mono), 1.32 ns
against 3.28 ns.

`XoroShiroRandom` deliberately does not: xoroshiro128+ is a `+` scrambler with no strong 64-bit word
to hand back, so it keeps composing a 64-bit draw out of two strong halves.

A generator that answers 64-bit draws in one advance produces a **different sequence** for the same
seed than 3.5.1 did. See [Seeded streams that moved](#seeded-streams-that-moved).

For detailed benchmarks, see [Random Performance](../../performance/random-performance.md).

---

## Creating Seeded Generators

For reproducible sequences (replays, procedural generation, testing):

<!-- doc-sample: compiles -->

```csharp
using WallstopStudios.UnityHelpers.Core.Random;

// Create with specific seed
PcgRandom seeded = new PcgRandom(seed: 12345);

// Generate reproducible sequence
for (int i = 0; i < 10; i++)
{
    Debug.Log(seeded.Next(0, 100)); // Same values every run
}

// Different seed = different sequence
PcgRandom different = new PcgRandom(seed: 67890);
```

### Seeded streams that moved

Reproducibility is a promise about a _given version_. Two corrections in this release change what
some generators return for a seed they were already given, so a replay, a saved procedural world or
a golden test recorded under 3.5.1 will not reproduce with them:

| generator                                                 | what moved                                              | why                                                                                                                 |
| --------------------------------------------------------- | ------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `XoroShiroRandom`                                         | **every** draw                                          | It returned the linear low half of its word and now returns the strong high half.                                   |
| `BlastCircuitRandom`, `RomuDuo`, `SplitMix64`, `WyRandom` | `NextUlong`, `NextLong`, `NextDouble`, `NextUlong(max)` | One state advance per 64-bit draw instead of two. `NextUint`, `Next`, `NextBool` and `NextFloat` are **unchanged**. |
| `Xoshiro256StarStar`                                      | nothing                                                 | Added in this same unreleased cycle.                                                                                |

Every other generator is untouched. If you need the old stream, pin the package version that
produced it; a save format that must survive a generator change should record the drawn values, or
the generator's `InternalState`, rather than a seed.

---

## API Reference

### Basic Generation

<!-- doc-sample: compiles -->

```csharp
IRandom random = PRNG.Instance;

// Integers
int value = random.Next();                    // [0, int.MaxValue)
int bounded = random.Next(100);               // [0, 100)
int ranged = random.Next(10, 50);             // [10, 50)

// Unsigned integers
uint bits = random.NextUint();
uint boundedUint = random.NextUint(1000u);

// Floating point
float f = random.NextFloat();                 // [0.0, 1.0)
float rangedF = random.NextFloat(-1f, 1f);    // [-1.0, 1.0)
double d = random.NextDouble();               // [0.0, 1.0)

// Boolean
bool b = random.NextBool();                   // 50% true/false
bool weighted = random.NextBool(0.75f);       // 75% true
```

Infinite float and double bounds select finite representable bit patterns, rather than a uniform
real-number distribution over an unbounded interval. `NextFloat(float.PositiveInfinity)` and
`NextDouble(double.PositiveInfinity)` use that same rule as their two-bound overloads. The interval
from negative infinity to the most negative finite value has only negative infinity below its
exclusive maximum: the legacy overload returns that value without consuming entropy, while
`TryNextDouble` returns `false` with a default output because no finite candidate exists.

For saved simulations upgrading from an earlier version, finite-bound draw sequences and raw
streams are unchanged. Calls with infinite bounds can now consume different draws and produce
different values; replay files using those calls should record their package version.

### Exact sampling and stalled sources

`AbstractRandom.TryNextUint`, `TryNextUlong`, `TryNextDouble` and `TryNextGaussian` return
`false` and write `default` for invalid inputs, exhausted rejection sampling, or a floating-point
result that cannot satisfy the requested contract. Always check the boolean before using the output. Infinite-range `TryNextDouble` also reports failure when its
bounded integer sampler exhausted its cap, even if that sampler's fallback would produce a finite
number. It stops on that failure rather than multiplying the nested retry budgets. Legacy infinite-double sampling also stops when exhausted inner sampling yields a nonfinite candidate, returning its existing deterministic fallback. Finite-range
sampling rejects a result rounded to its exclusive maximum; Gaussian sampling rejects an overflow
to infinity. Neither case clamps the result or reports a substitute as success. These methods are
on `AbstractRandom`; `IRandom` remains unchanged.

```csharp
AbstractRandom random = new PcgRandom(1234);
if (random.TryNextDouble(1.0, double.PositiveInfinity, out double sample))
{
    UnityEngine.Debug.Log(sample);
}
```

The existing methods retain their degraded, non-throwing response to a stalled source. They can
return a modulo reduction, a fixed in-range value, or a Gaussian substitute after their rejection
caps. Invalid-bound exceptions remain. Use the `Try` methods when a fallback is unacceptable.
A bounded integer request permits an initial draw followed by 65,536 retries for 32 bits or
1,048,576 retries for 64 bits. Gaussian sampling permits 1,048,576 candidate pairs before taking
its fallback path. A failed Gaussian draw does not cache a fallback partner as an exact sample.

Raw `NextUint()` and `NextUlong()` streams, power-of-two reductions, and serialized continuation
are unaffected by these `Try` guards. A failed `Try` call still consumes its attempted draws;
a Gaussian transform that overflows retains its finite cached partner for the next request.
A failed exact request can now stop earlier than the legacy fallback path, so subsequent draws
after that failure can start at an earlier stream position. `Next()` and `NextLong()` exclude their
signed maximum; the earlier correction to those methods intentionally changed the rare draw that
previously returned that maximum. These generators are not cryptographic random sources.

Arithmetic verification is separate from statistical quality testing. Run the executable
`scripts/random-quality/verify-bounded-sampling.py` proof with a C++17
compiler and Python's `z3-solver` installed:

```bash
python3 scripts/random-quality/verify-bounded-sampling.py
```

It enumerates every word for every nonzero 8-bit and 16-bit bound, verifies exact results and equal
accepted bucket counts, and rejects modulo, threshold and high-word model mutants. Z3 proves
unsigned wrap, product decomposition, the threshold remainder identity, result bounds, and the
carry reconstruction parsed from the production multiply-high body. Source mutations to a partial
product, limb extraction and carry shift must each produce a counterexample. Result bounds and limb expansion
use integer arithmetic; carry reconstruction and unsigned wrap use bit vectors. Unsupported
production syntax or an inconclusive solver result fails the command. The Unity
`AbstractRandomBoundedContractTests` fixture independently checks production acceptance and
outputs against `BigInteger`, including exact retry boundaries and nested rejection failures.
These checks do not replace raw-stream continuation tests or Mono, IL2CPP and WebGL verification.

### Ranges a Designer Authored

`Next(min, max)` and `NextFloat(min, max)` throw when `max <= min`, which is the right contract for a
computed range. It is the wrong one for two `[SerializeField]` floats: collapsing both ends onto the
same value is the obvious way to ask for _no_ spread, so an inspector's most natural "turn this off"
gesture is exactly the input those overloads reject. And these draws usually sit inside a coroutine
or a periodic tick, where an exception ends the loop **permanently**; the system it drove just stops
existing, with one line in the console.

```csharp
[SerializeField] private float _minSpawnDelay = 3f;
[SerializeField] private float _maxSpawnDelay = 3f;   // authored equal: no spread

// Throws, and the coroutine never runs again.
yield return new WaitForSeconds(random.NextFloat(_minSpawnDelay, _maxSpawnDelay));

// Answers 3f, and the spawner keeps spawning.
yield return new WaitForSeconds(random.NextFloatInRange(_minSpawnDelay, _maxSpawnDelay));
```

Every ranged draw has one, and they all answer the low bound:

| Strict (throws on an empty range) | Non-throwing sibling           |
| --------------------------------- | ------------------------------ |
| `Next(min, max)`                  | `NextIntInRange(low, high)`    |
| `NextUint(min, max)`              | `NextUintInRange(low, high)`   |
| `NextShort(min, max)`             | `NextShortInRange(low, high)`  |
| `NextByte(min, max)`              | `NextByteInRange(low, high)`   |
| `NextLong(min, max)`              | `NextLongInRange(low, high)`   |
| `NextUlong(min, max)`             | `NextUlongInRange(low, high)`  |
| `NextFloat(min, max)`             | `NextFloatInRange(low, high)`  |
| `NextDouble(min, max)`            | `NextDoubleInRange(low, high)` |

The float and double siblings also answer the low bound when either bound is `NaN`. The strict
`NextFloat` and `NextDouble` overloads reject `NaN` bounds with `ArgumentException` before drawing.
Their existing support for infinite two-bound ranges and bounded-sampling fallbacks is unchanged.

Weighted selection rejects `NaN` and infinite weights before drawing. Array and tuple overloads also
reject negative weights and totals that overflow `float`; the `IReadOnlyList<float>` overload
continues treating finite negative weights as zero and sums in `double`. `NextBool(probability)`
requires a probability in `[0, 1]`, including rejection of `NaN`.

`NextNoiseMap` requires positive finite scale, persistence, lacunarity and octave offset range,
and a finite base offset. Sphere surface sampling returns its center for nonfinite radius,
matching the volume sampling helpers.

**They answer the low bound, not zero.** These are a _range_, not a scatter: an author who writes
`3 .. 3` means three seconds. A symmetric `[-s, s]` whose collapse genuinely is zero is a different
shape and keeps its own guard. A `null` generator answers the low bound too, so a field that has not
been wired up yet degrades to the authored minimum rather than throwing.

Use the throwing overloads for a range your code computed; use these for one a human typed.

### Vector Generation

```csharp
// 2D vectors
Vector2 v2 = random.NextVector2();                      // Each component [0, 1)
Vector2 ranged2 = random.NextVector2(-10f, 10f);        // Each component [-10, 10)

// 3D vectors
Vector3 v3 = random.NextVector3();
Vector3 ranged3 = random.NextVector3(-5f, 5f);
```

### Color Generation

```csharp
// Random colors
Color c = random.NextColor();                           // Random RGBA
```

### Distributions

```csharp
// Gaussian (normal) distribution
float gaussian = random.NextGaussian(mean: 0f, stdDev: 1f);

// Weighted selection
string[] items = { "Common", "Rare", "Epic", "Legendary" };
float[] weights = { 60f, 25f, 12f, 3f };
string drop = random.NextWeighted(items.Zip(weights, (x, y) => (x, y)));
```

### Feel-Good Randomness

Use these helpers when independent rolls are mathematically fair but feel bad to players because they create long streaks or clumps.

```csharp
// Exact-average pseudo-random distribution:
// long-run success rate remains 25%, but failure streaks increase the next chance.
if (ExactAveragePrd.TryCreate(0.25f, out ExactAveragePrd critChance))
{
    bool criticalHit = critChance.Roll(random);
}

// Bad-luck protection / pity timer:
// starts at 10%, adds 5% after each failure, and guarantees success after 10 failures.
if (BadLuckProtection.TryCreate(0.10f, 0.05f, 10, out BadLuckProtection rareDrop))
{
    bool dropped = rareDrop.Roll(random);
}

// Weighted shuffle bag:
// each three-draw cycle contains exactly two common tickets and one rare ticket.
WeightedShuffleBag<string> bag = new();
bag.TryAdd("Common", 2);
bag.TryAdd("Rare", 1);
bag.TryNext(random, out string reward);
```

`ExactAveragePrd` intentionally rejects very small non-zero targets below
`ExactAveragePrd.MinimumPositiveTargetChance`; use `BadLuckProtection` or a
`WeightedShuffleBag<T>` for ultra-rare rewards. The stateful helpers expose restore
APIs (`TrySetFailuresSinceSuccess`, `TryRestoreRemaining`, and copy helpers for bag
tickets) so save/load systems can persist pity and deck state explicitly.

Choose the helper by design goal:

| Goal                                                  | Helper                  | Behavior                                                                  |
| ----------------------------------------------------- | ----------------------- | ------------------------------------------------------------------------- |
| Preserve exact long-run chance while reducing streaks | `ExactAveragePrd`       | Chance rises after failures by a solved coefficient and resets on success |
| Guarantee eventual success after a dry streak         | `BadLuckProtection`     | Chance ramps by a fixed amount and can force a success after N failures   |
| Avoid repeated clumps in finite weighted sets         | `WeightedShuffleBag<T>` | Draws without replacement until every weighted ticket has appeared        |

### Collection Operations

```csharp
// Shuffle in place
myList.Shuffle(random);

// Random element
T element = random.NextOf(array);
T element2 = random.NextOf(list);

// Random index
int index = random.Next(collection.Count);
```

---

## Thread Safety

`PRNG.Instance` provides thread-local instances, making it safe for multithreaded code without locks:

<!-- doc-sample: compiles -->

```csharp
// Safe - each thread gets its own instance
Parallel.For(0, 1000, i =>
{
    int value = PRNG.Instance.Next(0, 100);
    // No race conditions
});
```

For explicit thread-local control:

```csharp
using WallstopStudios.UnityHelpers.Core.Random;

// Create thread-local wrapper around any generator
ThreadLocalRandom<PcgRandom> threadLocal = new();
IRandom random = threadLocal.Value; // Per-thread instance
```

---

## Perlin Noise

For procedural generation, use the seedable Perlin noise generator:

```csharp
using WallstopStudios.UnityHelpers.Core.Random;

PerlinNoise noise = new PerlinNoise(seed: 42);

// 2D noise (terrain, textures)
float value2D = noise.Noise(x, y);

// Octave noise for more detail
float octaves = noise.OctaveNoise(x, y, octaves: 4, persistence: 0.5f);
```

---

## Best Practices

1. **Use `PRNG.Instance`** for most cases: it's fast, thread-safe, and well-tested
2. **Seed generators explicitly** when reproducibility matters (replays, tests)
3. **Avoid `new` in hot paths**: cache generator instances
4. **Don't use for security**: these are PRNGs, not CSPRNGs

```csharp
// ✅ Good - cache the reference
private IRandom _random = PRNG.Instance;

void Update()
{
    float value = _random.NextFloat();
}

// ❌ Bad - creates new instance every frame
void Update()
{
    PcgRandom random = new PcgRandom(); // Allocation!
    float value = random.NextFloat();
}
```

---

## See Also

- [Random Performance Benchmarks](../../performance/random-performance.md)
- [Math & Extensions](./math-and-extensions.md)
- [README - Random Generators](../../readme.md#random-number-generators)
