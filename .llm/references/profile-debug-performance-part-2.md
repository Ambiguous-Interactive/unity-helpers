# profile-debug-performance - Part 2

## Split Content

## Identifying Hot Paths

### What Qualifies as a Hot Path

| Code Location             | Frequency      | Optimization Priority |
| ------------------------- | -------------- | --------------------- |
| Update()                  | Every frame    | ★★★★★ Critical        |
| FixedUpdate()             | 50x/second     | ★★★★★ Critical        |
| LateUpdate()              | Every frame    | ★★★★★ Critical        |
| OnGUI()                   | Multiple/frame | ★★★★★ Critical        |
| Coroutine loops           | Continuous     | ★★★★☆ High            |
| Event handlers (frequent) | Per-event      | ★★★☆☆ Medium          |
| One-time init             | Once           | ★☆☆☆☆ Low             |

### Profiler-Guided Optimization

1. **Profile** the actual game scenario
2. **Sort by Self Time** to find expensive methods
3. **Check GC.Alloc** for allocation hotspots
4. **Focus on top 10%** — usually 90% of the performance impact

---

## Common Performance Pitfalls

### Symptoms and Causes

| Symptom                       | Likely Cause                | Investigation                     |
| ----------------------------- | --------------------------- | --------------------------------- |
| Frame rate drops periodically | GC collection               | Check GC.Alloc in Profiler        |
| Stuttering every few seconds  | Large GC collection         | Memory Profiler snapshots         |
| Slow first frame              | Cold caches, initialization | Profile startup                   |
| Mobile overheating            | CPU/GPU overwork            | Check both CPU and GPU time       |
| Memory grows over time        | Memory leak                 | Compare Memory Profiler snapshots |

### GC Spike Diagnosis

1. In CPU Profiler, look for `GC.Collect` spikes
2. Check frames before the spike for high `GC.Alloc`
3. Use **Call Stacks** to trace allocation source
4. Common culprits:
   - LINQ in Update
   - String concatenation
   - `new List<T>()` in loops
   - Closures/lambdas

---

## Build-Time Profiling

### Development Builds

Enable for accurate profiling:

- **Development Build** checkbox
- **Autoconnect Profiler**
- **Deep Profiling Support** (optional, adds overhead)

### IL2CPP Considerations

- IL2CPP builds behave differently than Mono
- Always profile IL2CPP builds for release targets
- Some allocations present in Mono may be eliminated in IL2CPP

---

## Benchmark Patterns

### Micro-Benchmark Pattern

```csharp
[Test]
public void CompareImplementations()
{
    const int iterations = 100000;
    List<int> testData = CreateTestData(1000);

    // Warmup
    for (int i = 0; i < 100; i++)
    {
        ImplementationA(testData);
        ImplementationB(testData);
    }

    // Measure A
    Stopwatch swA = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
    {
        ImplementationA(testData);
    }
    swA.Stop();

    // Measure B
    Stopwatch swB = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
    {
        ImplementationB(testData);
    }
    swB.Stop();

    Debug.Log($"A: {swA.ElapsedMilliseconds}ms, B: {swB.ElapsedMilliseconds}ms");
    Debug.Log($"B is {(float)swA.ElapsedMilliseconds / swB.ElapsedMilliseconds:F2}x faster");
}
```

### Loop Comparison Reference

Typical performance for 16M element array:

| Pattern              | Time  | Allocations    |
| -------------------- | ----- | -------------- |
| `for` over array     | 35ms  | 0B             |
| `for` over List      | 62ms  | 0B             |
| `foreach` over array | 35ms  | 0B             |
| `foreach` over List  | 120ms | 24B (old Mono) |
| LINQ `.Sum()`        | 271ms | 24B+           |

---

## Platform-Specific Profiling

### Mobile

- Profile on actual devices, not Editor
- Check thermal throttling (sustained performance)
- Monitor battery usage
- Use platform-specific tools (Xcode Instruments, Android Studio Profiler)

### Android Commands

```bash
# Memory info for specific app
adb shell dumpsys meminfo com.company.game

# Process memory usage
adb shell procrank

# System memory overview
adb shell cat /proc/meminfo
```

### Console

- Use platform SDK profilers
- Check certification requirements for performance
- Test with final build configuration

---
