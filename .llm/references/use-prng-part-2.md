# use-prng - Part 2

## Split Content

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
