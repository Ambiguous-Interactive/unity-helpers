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

**`UnityRandom` is the one exception, and it cannot resume a stream.** Its live position belongs to
`UnityEngine.Random`'s engine globals rather than to the object, so its snapshot carries only the seed it
was constructed with. Restoring it continues from wherever the engine currently is, without any error.
Use a concrete PRNG when a save file has to reproduce a sequence.

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
| `XoroShiroRandom`             | Fast            | Fair         | Bulk placement; bit 0 is linear, so avoid `NextBool`          |
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
safe for `NextBool` and low-bit masks. `Xoshiro256StarStar` is the only generator here that
overrides `NextUlong`, costing one state advance per 64-bit draw instead of two.

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
