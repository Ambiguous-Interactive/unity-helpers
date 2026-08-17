# Skill: Use PRNG

<!-- trigger: random, prng, seed, procedural, dice | Implementing randomization | Feature -->

**Trigger**: When implementing randomization, procedural generation, or any random number generation.

---

## Quick Start

```csharp
using WallstopStudios.UnityHelpers.Core.Random;

// Thread-local default (IllusionFlow) - fastest, recommended
float value = PRNG.Instance.NextFloat();
int roll = PRNG.Instance.Next(1, 7);  // 1-6 inclusive
```

---

## Available PRNGs

| PRNG                          | Speed | Quality | Notes                        |
| ----------------------------- | ----- | ------- | ---------------------------- |
| `IllusionFlow`                | ★★★★★ | ★★★★★   | Default, best all-around     |
| `PcgRandom`                   | ★★★★★ | ★★★★★   | PCG algorithm, excellent     |
| `XorShiftRandom`              | ★★★★★ | ★★★★    | Classic, very fast           |
| `XoroShiroRandom`             | ★★★★★ | ★★★★★   | xoroshiro128+, high quality  |
| `SplitMix64`                  | ★★★★★ | ★★★★    | Good for seeding             |
| `RomuDuo`                     | ★★★★★ | ★★★★    | Fast, small state            |
| `WyRandom`                    | ★★★★★ | ★★★★★   | wyrand, excellent            |
| `SquirrelRandom`              | ★★★★  | ★★★★    | Noise-based                  |
| `FlurryBurstRandom`           | ★★★★  | ★★★★    | Burst-compatible             |
| `PhotonSpinRandom`            | ★★★★  | ★★★★    | Novel algorithm              |
| `StormDropRandom`             | ★★★★  | ★★★★    | Novel algorithm              |
| `WaveSplatRandom`             | ★★★★  | ★★★★    | Novel algorithm              |
| `BlastCircuitRandom`          | ★★★★  | ★★★★    | Novel algorithm              |
| `LinearCongruentialGenerator` | ★★★   | ★★      | Simple LCG                   |
| `DotNetRandom`                | ★★★   | ★★★     | Wraps System.Random          |
| `SystemRandom`                | ★★★   | ★★★     | Wraps System.Random          |
| `UnityRandom`                 | ★★    | ★★★     | Wraps UnityEngine.Random     |
| `WDoomRandom`                 | ★★★★★ | ★       | Index-into-array, period 256 |

---

## IRandom Interface

All PRNGs implement `IRandom`:

```csharp
public interface IRandom
{
    // Basic generation
    int Next();                        // Non-negative int
    int Next(int maxExclusive);        // [0, max)
    int Next(int minInclusive, int maxExclusive);  // [min, max)

    // Floating point
    float NextFloat();                 // [0, 1)
    float NextFloat(float max);        // [0, max)
    float NextFloat(float min, float max);  // [min, max)
    double NextDouble();               // [0, 1)

    // Unity types
    Vector2 NextVector2();             // Components in [0, 1)
    Vector2 NextVector2(float min, float max);
    Vector3 NextVector3();
    Vector3 NextVector3(float min, float max);
    Color NextColor();                 // Random RGB, alpha = 1
    Color NextColorWithAlpha();        // Random RGBA

    // Special distributions
    float NextGaussian();              // Normal distribution (mean=0, stddev=1)
    float NextGaussian(float mean, float stddev);

    // Selection
    T NextElement<T>(IList<T> list);   // Random element from list
    T NextEnum<T>() where T : Enum;    // Random enum value
    int NextWeightedIndex(IList<float> weights);  // Weighted selection

    // Utilities
    bool NextBool();                   // 50/50 true/false
    bool NextBool(float probability);  // true with given probability
    Guid NextGuid();                   // Random GUID
    void Shuffle<T>(IList<T> list);    // Fisher-Yates shuffle

    // Seeding
    void SetSeed(ulong seed);
    void SetSeed(string seed);         // Hash-based seeding
}
```

---

## Usage Examples

### Basic Random Values

```csharp
IRandom random = PRNG.Instance;

// Integer ranges
int diceRoll = random.Next(1, 7);      // 1-6
int damage = random.Next(10, 21);      // 10-20

// Floating point
float speedMultiplier = random.NextFloat(0.8f, 1.2f);
float angle = random.NextFloat(0f, 360f);

// Boolean
bool criticalHit = random.NextBool(0.1f);  // 10% chance
bool coinFlip = random.NextBool();         // 50% chance
```

### Unity Types

```csharp
IRandom random = PRNG.Instance;

// Random position in area
Vector2 spawnPos = random.NextVector2(-10f, 10f);

// Random direction
Vector3 direction = random.NextVector3().normalized;

// Random color
Color particleColor = random.NextColor();
```

### Selection

```csharp
IRandom random = PRNG.Instance;

// Random element from list
List<Enemy> enemies = GetEnemies();
Enemy target = random.NextElement(enemies);

// Random enum value
DamageType type = random.NextEnum<DamageType>();

// Weighted selection
List<float> weights = new List<float> { 0.5f, 0.3f, 0.15f, 0.05f };
int index = random.NextWeightedIndex(weights);
// Returns 0 (50%), 1 (30%), 2 (15%), or 3 (5%)
```

### Shuffling

```csharp
IRandom random = PRNG.Instance;

List<Card> deck = GetDeck();
random.Shuffle(deck);  // In-place Fisher-Yates shuffle
```

### Gaussian Distribution

```csharp
IRandom random = PRNG.Instance;

// Normal distribution (mean=0, stddev=1)
float normalValue = random.NextGaussian();

// Custom mean and standard deviation
float height = random.NextGaussian(170f, 10f);  // mean=170, stddev=10
```

---

## Seeded Randomness

For reproducible results:

```csharp
// Create new instance with seed
IRandom seededRandom = new IllusionFlow(12345UL);

// Or set seed on existing instance
IRandom random = new PcgRandom();
random.SetSeed(12345UL);

// String-based seeding (hashes the string)
random.SetSeed("my-level-seed");

// All subsequent calls produce same sequence
float a = random.NextFloat();  // Always same value for same seed
```

---

## Thread Safety

```csharp
// PRNG.Instance is thread-local - safe to use from any thread
// Each thread gets its own instance

// For shared state, create a dedicated instance with locking
private readonly IRandom sharedRandom = new IllusionFlow();
private readonly object randomLock = new object();

public float GetThreadSafeRandom()
{
    lock (randomLock)
    {
        return sharedRandom.NextFloat();
    }
}
```

---

## Performance Tips

### Use PRNG.Instance

```csharp
// ✅ Fast - uses thread-local singleton
float value = PRNG.Instance.NextFloat();

// ❌ Slower - creates new instance each call
float value = new IllusionFlow().NextFloat();
```

### Cache Reference for Hot Paths

```csharp
// ✅ Cache reference in hot paths
private void Update()
{
    IRandom random = PRNG.Instance;
    for (int i = 0; i < 1000; i++)
    {
        ProcessWithRandom(random);
    }
}
```

### Avoid UnityRandom in Hot Paths

```csharp
// ❌ UnityEngine.Random is slower and not thread-safe
float value = UnityEngine.Random.value;

// ✅ Use Unity Helpers PRNGs
float value = PRNG.Instance.NextFloat();
```

---

## Choosing a PRNG

| Use Case              | Recommended PRNG                           |
| --------------------- | ------------------------------------------ |
| General purpose       | `PRNG.Instance` (IllusionFlow)             |
| Procedural generation | `PcgRandom` or `XoroShiroRandom`           |
| Seeding other PRNGs   | `SplitMix64`                               |
| Burst jobs            | `FlurryBurstRandom`                        |
| Minimal state         | `RomuDuo`                                  |
| Cryptographic quality | Use `System.Security.Cryptography` instead |
