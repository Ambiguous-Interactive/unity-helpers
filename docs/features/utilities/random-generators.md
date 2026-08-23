# Random Number Generators

<!-- cspell:ignore PRD Prd -->

**TL;DR:** Use `PRNG.Instance` for 10-15x faster random generation than `UnityEngine.Random`, with a rich API for vectors, colors, weighted selection, and more.

---

## Overview

Unity Helpers provides 20+ high-performance pseudo-random number generators (PRNGs) through a unified `IRandom` interface. Each generator carries a `[RandomGeneratorMetadata]` quality rating spanning fast-but-weak toys through generators that clear BigCrush — check the rating before choosing; the table below summarizes.

### Key Features

- **10-15x faster** than `UnityEngine.Random` (see [benchmarks](../../performance/random-performance.md))
- **Thread-safe** access via `PRNG.Instance` (thread-local)
- **Rich API** — vectors, colors, Gaussian distributions, weighted selection, subset sampling
- **Feel-good randomness** — exact-average PRD, pity timers, and weighted shuffle bags
- **Seedable** — reproducible results for replays and testing
- **IL2CPP compatible** — no reflection, AOT-safe

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
restored generator resumes the exact sequence — verified for all of them by
`GeneratorSnapshotRestoreTests`.

`UnityRandom` resumes too, and it is worth knowing how. Its position belongs to
`UnityEngine.Random`'s engine globals rather than to the object, so the snapshot carries that position
and restoring one **writes `UnityEngine.Random.state` back**. Anything else drawing from
`UnityEngine.Random` is moved with it — which is the same global that `new UnityRandom(seed)` already
resets through `InitState`. A snapshot written before 3.6 carries no position; restoring one of those
leaves the engine exactly where it is, and so does a payload that is not an engine position at all —
assigning one would leave `UnityEngine.Random` stuck returning a single value for the rest of the run.

---

## Available Generators

All generators implement the `IRandom` interface:

| Generator                     | Speed           | Quality      | Best For                                                      |
| ----------------------------- | --------------- | ------------ | ------------------------------------------------------------- |
| `LinearCongruentialGenerator` | Fastest         | Poor         | Non-critical effects only                                     |
| `WaveSplatRandom`             | Fastest         | Experimental | Throwaway effects; no formal test results published           |
| `SplitMix64`                  | Very Fast       | Very Good    | High-throughput generation                                    |
| `BlastCircuitRandom`          | Very Fast       | Good         | Bulk effects, chaotic mixing                                  |
| `PcgRandom`                   | Fast            | Excellent    | General purpose, seeded generation                            |
| `FlurryBurstRandom`           | Fast            | Excellent    | All-around alternative to PCG                                 |
| `IllusionFlow`                | Fast            | Excellent    | Balanced speed and quality                                    |
| `XoroShiroRandom`             | Fast            | Good         | Bulk placement, shuffles, procedural noise                    |
| `RomuDuo`                     | Fast            | Good         | Alternative to PCG                                            |
| `Xoshiro128StarStar`          | Not benchmarked | Excellent    | `NextBool`/low-bit masks; WebGL and other 32-bit targets      |
| `Xoshiro256StarStar`          | Not benchmarked | Excellent    | `NextDouble`/`NextUlong`-heavy work (one advance per 64 bits) |
| `StormDropRandom`             | Moderate        | Excellent    | Long streams from a large 1024-word state                     |
| `XorShiftRandom`              | Moderate        | Fair         | Legacy compatibility                                          |
| `WyRandom`                    | Moderate        | Very Good    | Hash-based scenarios                                          |
| `SquirrelRandom`              | Moderate        | Fair         | Noise-based generation                                        |
| `PhotonSpinRandom`            | Slow            | Excellent    | Maximum quality needed                                        |
| `UnityRandom`                 | Slow            | Fair         | Match Unity behavior                                          |
| `SystemRandom`                | Very Slow       | Poor         | .NET compatibility                                            |
| `DotNetRandom`                | Very Slow       | Poor         | Bridging `System.Random` code to `IRandom`                    |
| `WDoomRandom`                 | Fastest         | Poor         | Retro feel, deterministic replays                             |

`Xoshiro128StarStar` and `Xoshiro256StarStar` are new and have not been through the benchmark
harness yet; their speed rows fill in the next time
[Random Performance](../../performance/random-performance.md) is regenerated. Both are rated
`Excellent`: the `**` scrambler leaves no weak output bit, so unlike the `+` scramblers they are
safe for `NextBool` and low-bit masks.

### One state advance per 64-bit draw

`NextUlong()`, and therefore `NextLong()`, `NextDouble()` and `NextUlong(max)`, used to cost **two**
state advances on every generator: the shared base class built a 64-bit value out of two 32-bit
draws. `BlastCircuitRandom`, `RomuDuo`, `SplitMix64`, `WyRandom` and `Xoshiro256StarStar` each
compute a whole 64-bit word internally, so they now answer a 64-bit draw with one advance and return
that word directly -- measured at **2.49x** on Unity 6000.4.6f1 (Mono), 1.32 ns against 3.28 ns.

`XoroShiroRandom` deliberately does not: xoroshiro128+ is a `+` scrambler with no strong 64-bit word
to hand back, so it keeps composing a 64-bit draw out of two strong halves.

A generator that answers 64-bit draws in one advance produces a **different sequence** for the same
seed than 3.5.1 did. See [Seeded streams that moved](#seeded-streams-that-moved).

For detailed benchmarks, see [Random Performance](../../performance/random-performance.md).

---

## Creating Seeded Generators

For reproducible sequences (replays, procedural generation, testing):

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
produced it -- a save format that must survive a generator change should record the drawn values, or
the generator's `InternalState`, rather than a seed.

---

## API Reference

### Basic Generation

```csharp
IRandom random = PRNG.Instance;

// Integers
int value = random.Next();                    // [int.MinValue, int.MaxValue]
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

### Ranges a Designer Authored

`Next(min, max)` and `NextFloat(min, max)` throw when `max <= min`, which is the right contract for a
computed range. It is the wrong one for two `[SerializeField]` floats: collapsing both ends onto the
same value is the obvious way to ask for _no_ spread, so an inspector's most natural "turn this off"
gesture is exactly the input those overloads reject. And these draws usually sit inside a coroutine
or a periodic tick, where an exception ends the loop **permanently** — the system it drove just stops
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

The float and double siblings also answer the low bound when either bound is `NaN`. `high <= low` is
false for a `NaN`, so the strict overload does not raise there -- it returns `NaN`, which then
spreads through whatever consumed it.

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

1. **Use `PRNG.Instance`** for most cases — it's fast, thread-safe, and well-tested
2. **Seed generators explicitly** when reproducibility matters (replays, tests)
3. **Avoid `new` in hot paths** — cache generator instances
4. **Don't use for security** — these are PRNGs, not CSPRNGs

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
